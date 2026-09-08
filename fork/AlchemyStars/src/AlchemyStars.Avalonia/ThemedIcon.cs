using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AlchemyStars.Avalonia;

/// <summary>
/// Keeps the shipped Apple Blue artwork intact for the existing themes while
/// providing a crisp, tintable monoline glyph for the Classic Apple theme.
/// </summary>
public sealed class ThemedIcon : Grid
{
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<ThemedIcon, string>(nameof(Glyph), "notification");

    public static readonly StyledProperty<bool> UseClassicGlyphProperty =
        AvaloniaProperty.Register<ThemedIcon, bool>(nameof(UseClassicGlyph));

    public static readonly StyledProperty<IBrush?> IconBrushProperty =
        AvaloniaProperty.Register<ThemedIcon, IBrush?>(nameof(IconBrush));

    private static readonly Dictionary<string, Bitmap> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> GlyphData =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["about"] = "M10,3 A7,7 0 1,0 10,17 A7,7 0 1,0 10,3 M10,9 L10,14 M10,6 L10,6.25",
            ["add"] = "M10,4 L10,16 M4,10 L16,10",
            ["animation-layers"] = "M3.5,3.5 L14.5,3.5 L17,6 L14.5,8.5 L3.5,8.5 Z M4,11 L16,11 M4,14 L16,14 M4,17 L13,17",
            ["animation-library"] = "M3.5,4.5 L16.5,4.5 L16.5,17 L3.5,17 Z M6,3 L6,6 M14,3 L14,6 M3.5,8 L16.5,8 M8,10.5 L13,13 L8,15.5 Z",
            ["batch-processing"] = "M3,3 L8.5,3 L8.5,8.5 L3,8.5 Z M11.5,3 L17,3 L17,8.5 L11.5,8.5 Z M3,11.5 L8.5,11.5 L8.5,17 L3,17 Z M11.5,11.5 L17,11.5 L17,17 L11.5,17 Z",
            ["camera-view"] = "M3,6.5 L6.5,6.5 L8,4.5 L12,4.5 L13.5,6.5 L17,6.5 L17,16 L3,16 Z M10,8.5 A2.75,2.75 0 1,0 10,14 A2.75,2.75 0 1,0 10,8.5",
            ["cast-preview"] = "M3,3.5 L17,3.5 L17,14.5 L3,14.5 Z M10,14.5 L10,17 M7,17 L13,17 M8,6.5 L13,9 L8,11.5 Z",
            ["delete"] = "M4,6 L16,6 M7.5,3.5 L12.5,3.5 L14,6 M6,6 L7,17 L13,17 L14,6 M9,9 L9,14 M11,9 L11,14",
            ["dual-wield"] = "M6,3 L8.5,5.5 L6.75,12.5 L5.25,12.5 L3.5,5.5 Z M3.5,14 L8.5,14 M6,14 L6,17 M14,3 L16.5,5.5 L14.75,12.5 L13.25,12.5 L11.5,5.5 Z M11.5,14 L16.5,14 M14,14 L14,17",
            ["export-animation"] = "M10,3 L10,12 M6.5,8.5 L10,12 L13.5,8.5 M3,14.5 L3,17 L17,17 L17,14.5",
            ["fit-view"] = "M3,7.5 L3,3 L7.5,3 M12.5,3 L17,3 L17,7.5 M17,12.5 L17,17 L12.5,17 M7.5,17 L3,17 L3,12.5",
            ["hand-pose"] = "M5,10 L5,6 C5,4.5 7,4.5 7,6 L7,9 L7,4 C7,2.8 9,2.8 9,4 L9,9 L9,4 C9,2.8 11,2.8 11,4 L11,9 L11,5 C11,3.8 13,3.8 13,5 L13,10 C14,7.2 16,7.8 15.3,10 L14,14 C13.4,16 11.8,17 9,17 C6.2,17 4.6,15.2 4,13 L3,10.5 C2.6,9.2 4.2,8.6 5,10 Z",
            ["import-assets"] = "M3,5 L8,5 L10,7 L17,7 L17,17 L3,17 Z M10,9 L10,14 M7.5,11.5 L10,14 L12.5,11.5",
            ["inverse-kinematics"] = "M4.75,3 A1.75,1.75 0 1,0 4.75,6.5 A1.75,1.75 0 1,0 4.75,3 M10,8.25 A1.75,1.75 0 1,0 10,11.75 A1.75,1.75 0 1,0 10,8.25 M15.25,13.5 A1.75,1.75 0 1,0 15.25,17 A1.75,1.75 0 1,0 15.25,13.5 M6.2,6.2 L8.6,8.6 M11.4,11.4 L13.8,13.8",
            ["language"] = "M10,3 A7,7 0 1,0 10,17 A7,7 0 1,0 10,3 M3,10 L17,10 M10,3 C7.5,6.5 7.5,13.5 10,17 M10,3 C12.5,6.5 12.5,13.5 10,17",
            ["model-parts"] = "M10,3 L17,6.5 L10,10 L3,6.5 Z M3,10.5 L10,14 L17,10.5 M3,14 L10,17.5 L17,14",
            ["move-down"] = "M4.5,8 L10,13.5 L15.5,8 M10,3.5 L10,13.5",
            ["move-up"] = "M4.5,12 L10,6.5 L15.5,12 M10,6.5 L10,16.5",
            ["next-frame"] = "M5,4 L13,10 L5,16 Z M15.5,4 L15.5,16",
            ["notification"] = "M10,3 A7,7 0 1,0 10,17 A7,7 0 1,0 10,3 M10,7 L10,11.5 M10,14 L10,14.25",
            ["output-naming"] = "M3,5 L8,3 L17,3 L17,12 L12,17 L3,17 Z M7,7 A1.25,1.25 0 1,0 7,9.5 A1.25,1.25 0 1,0 7,7 M10.5,8 L14.5,8 M10.5,11 L13.5,11",
            ["output-settings"] = "M3,5 L6,5 M10,5 L17,5 M6,3 L6,7 M3,10 L12,10 M16,10 L17,10 M12,8 L12,12 M3,15 L8,15 M12,15 L17,15 M8,13 L8,17",
            ["pause"] = "M6,4 L9,4 L9,16 L6,16 Z M11,4 L14,4 L14,16 L11,16 Z",
            ["play"] = "M6,3.5 L16,10 L6,16.5 Z",
            ["previous-frame"] = "M4.5,4 L4.5,16 M15,4 L7,10 L15,16 Z",
            ["project-workspace"] = "M3,5 L8,5 L10,7 L17,7 L17,17 L3,17 Z M6,10 L9,10 L9,13 L6,13 Z M11,10 L14,10 L14,13 L11,13 Z",
            ["restore-layout"] = "M4.5,6.5 A6.5,6.5 0 1,1 3.5,12.5 M4.5,3 L4.5,6.5 L8,6.5",
            ["save"] = "M3,3 L14.5,3 L17,5.5 L17,17 L3,17 Z M6,3 L6,7.5 L14,7.5 L14,3 M6,11 L14,11 L14,17 L6,17 Z",
            ["save-as"] = "M3,3 L11.5,3 L13.5,5 L13.5,17 L3,17 Z M5.5,3 L5.5,7 L11,7 L11,3 M5.5,11 L11,11 L11,17 L5.5,17 Z M15.5,3.5 L15.5,8.5 M13.5,6 L17.5,6",
            ["timeline-playback"] = "M3,5 L17,5 M3,15 L17,15 M5,3 L5,7 M15,13 L15,17 M8,8 L13,10.5 L8,13 Z",
            ["weapon-follow"] = "M5,3 A2,2 0 1,0 5,7 A2,2 0 1,0 5,3 M15,13 A2,2 0 1,0 15,17 A2,2 0 1,0 15,13 M7,5 C12,5 10,15 13,15 M10.5,12.5 L13,15 L10.5,17.5",
            ["weapon-processing-mode"] = "M10,3 L10,7 M10,7 C10,10 5,9 5,12 L5,17 M10,7 C10,10 15,9 15,12 L15,17 M3,17 L7,17 M13,17 L17,17",
            ["zoom-in"] = "M8.5,3 A5.5,5.5 0 1,0 8.5,14 A5.5,5.5 0 1,0 8.5,3 M12.5,12.5 L17,17 M8.5,6 L8.5,11 M6,8.5 L11,8.5",
            ["zoom-out"] = "M8.5,3 A5.5,5.5 0 1,0 8.5,14 A5.5,5.5 0 1,0 8.5,3 M12.5,12.5 L17,17 M6,8.5 L11,8.5",
        };

    private readonly Image bitmap = new()
    {
        Stretch = Stretch.Uniform,
        IsHitTestVisible = false,
    };

    private readonly global::Avalonia.Controls.Shapes.Path vector = new()
    {
        Fill = Brushes.Transparent,
        StrokeThickness = 1.5,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        IsHitTestVisible = false,
    };

    private readonly Viewbox vectorHost;

    static ThemedIcon()
    {
        GlyphProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateGlyph());
        UseClassicGlyphProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateMode());
        IconBrushProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateBrush());
    }

    public ThemedIcon()
    {
        var canvas = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
        canvas.Children.Add(vector);
        vectorHost = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = canvas,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapInterpolationMode(bitmap, BitmapInterpolationMode.HighQuality);
        Children.Add(bitmap);
        Children.Add(vectorHost);
        UpdateGlyph();
        UpdateMode();
        UpdateBrush();
    }

    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool UseClassicGlyph
    {
        get => GetValue(UseClassicGlyphProperty);
        set => SetValue(UseClassicGlyphProperty, value);
    }

    public IBrush? IconBrush
    {
        get => GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    internal bool HasLegacyBitmap => bitmap.Source is not null;

    internal static IEnumerable<(string Glyph, Rect Bounds)> ClassicGlyphBounds =>
        GlyphData.Select(pair => (pair.Key, Geometry.Parse(pair.Value).Bounds));

    private void UpdateGlyph()
    {
        if (!GlyphData.TryGetValue(Glyph, out var data))
            data = GlyphData["notification"];
        vector.Data = Geometry.Parse(data);

        if (!BitmapCache.TryGetValue(Glyph, out var image))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://AlchemyStars.Avalonia/Assets/AppleBlue/{Glyph}.png"));
            image = new Bitmap(stream);
            BitmapCache[Glyph] = image;
        }
        bitmap.Source = image;
    }

    private void UpdateMode()
    {
        bitmap.IsVisible = !UseClassicGlyph;
        vectorHost.IsVisible = UseClassicGlyph;
    }

    private void UpdateBrush() => vector.Stroke = IconBrush;
}
