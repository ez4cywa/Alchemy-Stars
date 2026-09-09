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
    public static readonly StyledProperty<bool> UseWindowsXpGlyphProperty =
        AvaloniaProperty.Register<ThemedIcon, bool>(nameof(UseWindowsXpGlyph));
    public static readonly StyledProperty<bool> UseWindows2000GlyphProperty =
        AvaloniaProperty.Register<ThemedIcon, bool>(nameof(UseWindows2000Glyph));
    public static readonly StyledProperty<int> CustomIconRevisionProperty =
        AvaloniaProperty.Register<ThemedIcon, int>(nameof(CustomIconRevision));

    public static readonly StyledProperty<IBrush?> IconBrushProperty =
        AvaloniaProperty.Register<ThemedIcon, IBrush?>(nameof(IconBrush));

    private static readonly Dictionary<string, Bitmap> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Names =
    [
        "about",
        "add",
        "animation-layers",
        "animation-library",
        "batch-processing",
        "camera-view",
        "cast-preview",
        "delete",
        "dual-wield",
        "export-animation",
        "fit-view",
        "hand-pose",
        "import-assets",
        "inverse-kinematics",
        "language",
        "model-parts",
        "move-down",
        "move-up",
        "next-frame",
        "notification",
        "output-naming",
        "output-settings",
        "pause",
        "play",
        "previous-frame",
        "project-workspace",
        "restore-layout",
        "save",
        "save-as",
        "timeline-playback",
        "weapon-follow",
        "weapon-processing-mode",
        "zoom-in",
        "zoom-out",
    ];

    private readonly Image bitmap = new()
    {
        Stretch = Stretch.Uniform,
        IsHitTestVisible = false,
    };

    private readonly Viewbox vectorHost = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Viewbox xpHost = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Viewbox win2000Host = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly AppIcon desktopIcon = new() { IsHitTestVisible = false };
    public static readonly StyledProperty<bool> UseDesktopGlyphProperty =
        AvaloniaProperty.Register<ThemedIcon, bool>(nameof(UseDesktopGlyph));
    public bool UseDesktopGlyph { get => GetValue(UseDesktopGlyphProperty); set => SetValue(UseDesktopGlyphProperty, value); }
    internal bool HasDesktopIcon => desktopIcon.IsVisible && desktopIcon.HasSymbol;

    private readonly Image custom = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };

    static ThemedIcon()
    {
        UseDesktopGlyphProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateMode());
        GlyphProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateGlyph());
        UseClassicGlyphProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateMode());
        UseWindowsXpGlyphProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateMode());
        UseWindows2000GlyphProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateMode());
        IconBrushProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateBrush());
        CustomIconRevisionProperty.Changed.AddClassHandler<ThemedIcon>((icon, _) => icon.UpdateMode());
    }

    public ThemedIcon()
    {
        RenderOptions.SetBitmapInterpolationMode(bitmap, BitmapInterpolationMode.HighQuality);
        Children.Add(bitmap);
        Children.Add(vectorHost);
        Children.Add(xpHost);
        Children.Add(win2000Host);
        Children.Add(desktopIcon);
        Children.Add(custom);
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

    internal bool HasClassicIcon => vectorHost.IsVisible && vectorHost.Child is not null;
    internal bool HasLegacyBitmap => bitmap.Source is not null;
    internal bool HasOriginalIcon => bitmap.IsVisible && bitmap.Source is not null;
    internal bool HasCustomIcon => custom.IsVisible && custom.Source is not null;
    public int CustomIconRevision
    {
        get => GetValue(CustomIconRevisionProperty);
        set => SetValue(CustomIconRevisionProperty, value);
    }
    public bool UseWindowsXpGlyph
    {
        get => GetValue(UseWindowsXpGlyphProperty);
        set => SetValue(UseWindowsXpGlyphProperty, value);
    }
    public bool UseWindows2000Glyph
    {
        get => GetValue(UseWindows2000GlyphProperty);
        set => SetValue(UseWindows2000GlyphProperty, value);
    }
    internal bool HasWindows2000Icon => win2000Host.IsVisible && win2000Host.Child is not null;

    internal static IEnumerable<string> GlyphNames => Names;

    private void UpdateGlyph()
    {
        vectorHost.Child = UseClassicGlyph ? ClassicAppleIcons.Create(Glyph, IconBrush) : null;
        xpHost.Child = UseWindowsXpGlyph ? WindowsXpIcons.Create(Glyph) : null;
        win2000Host.Child = UseWindows2000Glyph ? Windows2000Icons.Create(Glyph) : null;

        if (!BitmapCache.TryGetValue(Glyph, out var image))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://AlchemyStars.Avalonia/Assets/AppleBlue/{Glyph}.png"));
            image = new Bitmap(stream);
            BitmapCache[Glyph] = image;
        }
        bitmap.Source = image;
        UpdateMode();
    }

    private void UpdateMode()
    {
        desktopIcon.Kind = Glyph;
        desktopIcon.Foreground = IconBrush;
        custom.Source = CustomAppearance.Icon(Glyph);
        custom.IsVisible = custom.Source is not null;
        desktopIcon.IsVisible = !custom.IsVisible && UseDesktopGlyph && !UseWindows2000Glyph;
        bitmap.IsVisible = !custom.IsVisible && !UseDesktopGlyph && !UseClassicGlyph && !UseWindowsXpGlyph && !UseWindows2000Glyph;
        vectorHost.IsVisible = !custom.IsVisible && UseClassicGlyph && !UseWindowsXpGlyph && !UseWindows2000Glyph;
        vectorHost.Child = UseClassicGlyph ? ClassicAppleIcons.Create(Glyph, IconBrush) : null;
        if (UseWindowsXpGlyph && xpHost.Child is null) xpHost.Child = WindowsXpIcons.Create(Glyph);
        xpHost.IsVisible = !custom.IsVisible && UseWindowsXpGlyph && !UseWindows2000Glyph;
        if (UseWindows2000Glyph && win2000Host.Child is null) win2000Host.Child = Windows2000Icons.Create(Glyph);
        win2000Host.IsVisible = !custom.IsVisible && UseWindows2000Glyph;
    }

    private void UpdateBrush()
    {
        desktopIcon.Foreground = IconBrush;
        if (UseClassicGlyph) vectorHost.Child = ClassicAppleIcons.Create(Glyph, IconBrush);
    }
}
