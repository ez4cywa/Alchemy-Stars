using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AlchemyStars.Avalonia;

/// <summary>Validated data-only imports, copied to the user's preference directory.</summary>
internal static class CustomAppearance
{
    internal sealed record Theme(string Name, string BaseStyle,
        Dictionary<string, string[]> Light, Dictionary<string, string[]> Dark,
        Dictionary<string, double> Radii);

    private static readonly HashSet<string> Colors = new(StringComparer.Ordinal)
    {
        "Background", "Sidebar", "Surface", "SubtleSurface", "SurfaceRaised", "Canvas",
        "CommandBar", "CommandText", "OnDarkMuted", "DarkTile", "Text", "MutedText",
        "Border", "BorderStrong", "Accent", "Action", "Focus", "AccentHover", "AccentBorder",
        "Hover", "Pressed", "Selected", "Hero", "Success", "Error", "Chip", "BaseClip",
        "BaseEdge", "LayerClip", "LayerEdge", "Icon",
        "XpHeader", "XpButton", "XpButtonHover", "XpButtonPressed", "XpAction",
        "XpActionHover", "XpActionPressed", "XpHoverBorder",
        "AppleChrome", "AppleHeader", "AppleButton", "AppleButtonHover", "AppleButtonPressed",
        "AppleAction", "AppleActionHover", "AppleActionPressed", "AppleThumb", "AppleEdge", "AppleHoverEdge",
    };
    private static readonly HashSet<string> RadiusNames = new(StringComparer.Ordinal)
        { "Button", "Action", "Panel", "Header", "Card" };
    private static Dictionary<string, Bitmap> icons = new(StringComparer.OrdinalIgnoreCase);
    private static string directory = "";
    private static int revision;
    internal static Theme? CurrentTheme { get; private set; }
    internal static int IconCount => icons.Count;
    internal static bool HasSavedTheme => directory.Length > 0 && File.Exists(Path.Combine(directory, "theme.json"));
    internal static bool HasSavedIcons => directory.Length > 0 && File.Exists(Path.Combine(directory, "icons.zip"));
    private static string? themeError;
    private static string? iconError;
    internal static string? LoadError => themeError is null ? iconError : iconError is null ? themeError : themeError + " / " + iconError;
    internal static Bitmap? Icon(string glyph) => icons.GetValueOrDefault(glyph);

    internal static void Initialize(string storageDirectory)
    {
        directory = storageDirectory;
        CurrentTheme = null;
        icons = new(StringComparer.OrdinalIgnoreCase);
        themeError = iconError = null;
        try
        {
            var path = Path.Combine(directory, "theme.json");
            if (File.Exists(path)) CurrentTheme = ParseTheme(ReadLimited(path, 128 * 1024));
        }
        catch (Exception error) when (IsImportError(error)) { themeError = "theme.json: " + error.Message; }
        try
        {
            var path = Path.Combine(directory, "icons.zip");
            if (File.Exists(path)) icons = ParseIcons(ReadLimited(path, 8 * 1024 * 1024));
        }
        catch (Exception error) when (IsImportError(error)) { iconError = "icons.zip: " + error.Message; }
        NotifyIcons();
    }

    internal static bool IsImportError(Exception error) => error is InvalidDataException or IOException or UnauthorizedAccessException
        or JsonException or ArgumentException or InvalidOperationException or NotSupportedException;

    internal static void ImportTheme(string path)
    {
        var bytes = ReadLimited(path, 128 * 1024);
        var theme = ParseTheme(bytes);
        Save("theme.json", bytes);
        CurrentTheme = theme;
        themeError = null;
    }

    internal static void ImportIcons(string path)
    {
        var bytes = ReadLimited(path, 8 * 1024 * 1024);
        var imported = ParseIcons(bytes);
        try { Save("icons.zip", bytes); }
        catch { foreach (var icon in imported.Values) icon.Dispose(); throw; }
        icons = imported;
        iconError = null;
        NotifyIcons();
    }

    internal static void ClearTheme()
    {
        File.Delete(Path.Combine(directory, "theme.json"));
        CurrentTheme = null;
        themeError = null;
    }

    internal static void ClearIcons()
    {
        File.Delete(Path.Combine(directory, "icons.zip"));
        icons = new(StringComparer.OrdinalIgnoreCase);
        iconError = null;
        NotifyIcons();
    }

    private static void NotifyIcons()
    {
        if (Application.Current is { } app) app.Resources["CustomIconRevision"] = ++revision;
    }

    private static byte[] ReadLimited(string path, int limit)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new InvalidDataException($"Import exceeds {limit / 1024} KB.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void Save(string name, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, Path.Combine(directory, name), overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static Theme ParseTheme(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Theme must be a JSON object.");
        RejectUnknown(root, ["version", "name", "baseStyle", "light", "dark", "radii"]);
        if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1)
            throw new InvalidDataException("Theme version must be 1.");
        var name = root.TryGetProperty("name", out var title) ? title.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(name) || name.Length > 64) throw new InvalidDataException("Theme name must contain 1–64 characters.");
        var baseStyle = root.TryGetProperty("baseStyle", out var basis) ? basis.GetString() : "apple";
        if (baseStyle is "neumorphic" or "modern-desktop") baseStyle = "apple";
        if (baseStyle is not ("apple" or "classic-apple" or "windows-xp"))
            throw new InvalidDataException("Unknown baseStyle.");
        var light = Palette(root, "light");
        var dark = Palette(root, "dark");
        var radii = new Dictionary<string, double>(StringComparer.Ordinal);
        if (root.TryGetProperty("radii", out var shape))
        {
            if (shape.ValueKind != JsonValueKind.Object) throw new InvalidDataException("radii must be an object.");
            foreach (var property in shape.EnumerateObject())
            {
                if (!RadiusNames.Contains(property.Name) || !property.Value.TryGetDouble(out var value)
                    || !double.IsFinite(value) || value < 0 || value > 32 || !radii.TryAdd(property.Name, value))
                    throw new InvalidDataException($"Invalid radius: {property.Name} (expected 0–32).");
            }
        }
        return new Theme(name, baseStyle, light, dark, radii);
    }

    private static void RejectUnknown(JsonElement element, string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException($"Unknown or duplicate property: {property.Name}.");
    }

    private static Dictionary<string, string[]> Palette(JsonElement root, string mode)
    {
        if (!root.TryGetProperty(mode, out var palette) || palette.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Theme requires a {mode} object.");
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var property in palette.EnumerateObject())
        {
            if (!Colors.Contains(property.Name)) throw new InvalidDataException($"Unknown color token: {property.Name}.");
            var values = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().Select(value => value.GetString() ?? "").ToArray()
                : [property.Value.GetString() ?? ""];
            if (values.Length is < 1 or > 5 || values.Length > 1 && !IsMaterial(property.Name))
                throw new InvalidDataException("Only Xp/Apple material tokens support gradients of 2–5 colors.");
            foreach (var value in values)
                if (value.Length is not (7 or 9) || value[0] != '#' || value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
                    throw new InvalidDataException($"Invalid color: {property.Name}; use #RRGGBB or #AARRGGBB.");
            foreach (var value in values) _ = Color.Parse(value);
            if (!result.TryAdd(property.Name, values)) throw new InvalidDataException($"Duplicate color: {property.Name}.");
        }
        if (result.Count == 0) throw new InvalidDataException($"Theme {mode} palette is empty.");
        return result;
    }

    private static Dictionary<string, Bitmap> ParseIcons(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count is < 1 or > 68) throw new InvalidDataException("Icon ZIP contains too many entries (maximum 68).");
        var result = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        try
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var normalized = entry.FullName.Replace('\\', '/');
                if (normalized.StartsWith('/') || normalized.Split('/').Any(part => part is ".." or "." or "") || normalized.Contains(':'))
                    throw new InvalidDataException("Invalid icon ZIP path.");
                if (!entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Icon ZIP must contain only named PNG icons.");
                var glyph = Path.GetFileNameWithoutExtension(entry.Name);
                if (!ThemedIcon.GlyphNames.Contains(glyph, StringComparer.OrdinalIgnoreCase) || result.ContainsKey(glyph))
                    throw new InvalidDataException($"Unknown or duplicate icon: {glyph}.");
                total += entry.Length;
                if (entry.Length is < 24 or > 2 * 1024 * 1024 || total > 16 * 1024 * 1024)
                    throw new InvalidDataException("Icon ZIP exceeds the image size limit.");
                using var source = entry.Open();
                var png = new byte[(int)entry.Length];
                source.ReadExactly(png);
                if (!png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException($"Invalid PNG: {glyph}.");
                var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
                if (width is < 1 or > 1024 || height is < 1 or > 1024)
                    throw new InvalidDataException($"Icon {glyph} dimensions must be 1–1024 pixels.");
                using var image = new MemoryStream(png, writable: false);
                using var decoded = new Bitmap(image); // Header validation caps this temporary decode at 1024×1024.
                var scale = Math.Min(1d, 64d / Math.Max(width, height));
                var size = new PixelSize(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
                result.Add(glyph, decoded.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality));
            }
            if (result.Count == 0) throw new InvalidDataException("Icon ZIP contains no PNG icons.");
            return result;
        }
        catch { foreach (var icon in result.Values) icon.Dispose(); throw; }
    }

    internal static void Apply(Application app, bool dark)
    {
        if (CurrentTheme is not { } theme) return;
        foreach (var (name, colors) in dark ? theme.Dark : theme.Light)
        {
            var key = IsMaterial(name) ? name : "Alchemy" + name;
            var first = Color.Parse(colors[0]);
            IBrush brush = new SolidColorBrush(first);
            if (colors.Length > 1)
            {
                var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
                for (var i = 0; i < colors.Length; i++) gradient.GradientStops.Add(new GradientStop(Color.Parse(colors[i]), (double)i / (colors.Length - 1)));
                brush = gradient;
            }
            app.Resources[key] = first;
            app.Resources[key + "Brush"] = brush;
            if (name == "Accent")
                foreach (var accent in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorLight1" }) app.Resources[accent] = first;
        }
        foreach (var (name, radius) in theme.Radii)
            app.Resources["Appearance" + name + "Radius"] = name == "Header" ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(radius);
    }

    private static bool IsMaterial(string name) => name.StartsWith("Xp", StringComparison.Ordinal) || name.StartsWith("Apple", StringComparison.Ordinal);
}
