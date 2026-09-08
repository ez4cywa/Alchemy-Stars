using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AlchemyStars.Avalonia;

/// <summary>Resolution-independent, theme-aware symbols for the desktop workspace.</summary>
public sealed class AppIcon : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<AppIcon, string>(nameof(Kind), "about");
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<AppIcon, IBrush?>(nameof(Foreground), Brushes.Gray, inherits: true);

    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    internal bool HasSymbol => Symbols.ContainsKey(Kind);

    static AppIcon() => AffectsRender<AppIcon>(KindProperty, ForegroundProperty);

    protected override Size MeasureOverride(Size availableSize) => new(20, 20);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!Symbols.TryGetValue(Kind, out var symbol) || Foreground is null) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        if (scale <= 0) return;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2)))
            context.DrawGeometry(null, new Pen(Foreground, 1.65, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), symbol);
    }

    private static readonly IReadOnlyDictionary<string, Geometry> Symbols = new Dictionary<string, string>
    {
        ["animation-library"] = "M5 3H19Q21 3 21 5V19Q21 21 19 21H5Q3 21 3 19V5Q3 3 5 3Z M7 3V21 M17 3V21 M3 8H7 M3 16H7 M17 8H21 M17 16H21 M10 9L14 12L10 15Z",
        ["model-parts"] = "M12 3L22 8L12 13L2 8Z M3 12L12 17L21 12 M3 16L12 21L21 16",
        ["dual-wield"] = "M6 3V8L10 12V21 M18 3V8L14 12V21 M3 3H9 M15 3H21 M7 21H17",
        ["weapon-follow"] = "M4 6H10V12H4Z M14 12H20V18H14Z M10 9H15Q17 9 17 12 M7 12V16Q7 18 10 18H11 M8 15L11 18L8 21",
        ["weapon-processing-mode"] = "M3 4H9V10H3Z M15 14H21V20H15Z M9 7H16Q18 7 18 10V11 M15 8L18 11L21 8 M6 13V17H12 M9 14L12 17L9 20",
        ["animation-layers"] = "M4 4H20V9H4Z M4 13H15V18H4Z M18 14V20 M15 17H21",
        ["hand-pose"] = "M7 13V7Q7 4 9 5V11 M9 7V4Q11 2 12 5V11 M12 6Q14 3 15 6V12 M15 8Q18 6 18 9V15Q18 21 12 21H10Q8 21 6 18L3 13Q2 10 4 11L7 14",
        ["inverse-kinematics"] = "M6 6A2 2 0 1 0 2 6A2 2 0 1 0 6 6 M14 12A2 2 0 1 0 10 12A2 2 0 1 0 14 12 M22 5A2 2 0 1 0 18 5A2 2 0 1 0 22 5 M20 20A2 2 0 1 0 16 20A2 2 0 1 0 20 20 M6 7L10 11 M14 11L18 6 M13 14L17 18",
        ["cast-preview"] = "M12 3L20 7V17L12 21L4 17V7Z M4 7L12 11L20 7 M12 11V21",
        ["import-assets"] = "M3 7V19Q3 21 5 21H19Q21 21 21 19V9H11L9 6H4Q3 6 3 7Z M16 2V6 M13 4L16 7L19 4",
        ["export-animation"] = "M7 8H5Q3 8 3 10V19Q3 21 5 21H19Q21 21 21 19V10Q21 8 19 8H17 M12 15V2 M8 6L12 2L16 6",
        ["batch-processing"] = "M3 3H15V8H3Z M3 12H12V17H3Z M17 11L22 16L17 21 M13 16H22",
        ["project-workspace"] = "M5 3H19Q21 3 21 5V19Q21 21 19 21H5Q3 21 3 19V5Q3 3 5 3Z M3 8H21 M9 8V21",
        ["output-settings"] = "M3 6H8 M12 6H21 M3 12H14 M18 12H21 M3 18H6 M10 18H21 M8 3V9H12V3Z M14 9V15H18V9Z M6 15V21H10V15Z",
        ["camera-view"] = "M8 5L10 3H14L16 5H20Q22 5 22 7V18Q22 20 20 20H4Q2 20 2 18V7Q2 5 4 5Z M17 12A5 5 0 1 0 7 12A5 5 0 1 0 17 12",
        ["timeline-playback"] = "M4 4V20 M8 5L20 12L8 19Z",
        ["output-naming"] = "M3 5H21 M12 5V21 M8 21H16 M3 5V8 M21 5V8",
        ["language"] = "M22 12A10 10 0 1 0 2 12A10 10 0 1 0 22 12 M2 12H22 M12 2C6 7 6 17 12 22C18 17 18 7 12 2 M5 5Q12 9 19 5 M5 19Q12 15 19 19",
        ["save"] = "M3 14V19Q3 21 5 21H19Q21 21 21 19V14 M12 3V15 M8 11L12 15L16 11",
        ["save-as"] = "M3 13V19Q3 21 5 21H19Q21 21 21 19V16 M9 14L10 10L18 2L22 6L14 14Z M16 4L20 8",
        ["about"] = "M22 12A10 10 0 1 0 2 12A10 10 0 1 0 22 12 M12 11V18 M12 7V7.2",
        ["add"] = "M12 4V20 M4 12H20",
        ["delete"] = "M3 6H21 M9 6V3H15V6 M5 6L6 21H18L19 6 M10 10V17 M14 10V17",
        ["move-up"] = "M12 21V3 M5 10L12 3L19 10",
        ["move-down"] = "M12 3V21 M5 14L12 21L19 14",
        ["restore-layout"] = "M4 9A9 9 0 1 1 4 16 M4 3V9H10",
        ["fit-view"] = "M3 9V3H9 M15 3H21V9 M21 15V21H15 M9 21H3V15",
        ["zoom-in"] = "M17 10A7 7 0 1 0 3 10A7 7 0 1 0 17 10 M15 15L22 22 M6 10H14 M10 6V14",
        ["zoom-out"] = "M17 10A7 7 0 1 0 3 10A7 7 0 1 0 17 10 M15 15L22 22 M6 10H14",
        ["previous-frame"] = "M4 4V20 M19 5L8 12L19 19Z",
        ["play"] = "M6 3L21 12L6 21Z",
        ["pause"] = "M6 4H9V20H6Z M15 4H18V20H15Z",
        ["next-frame"] = "M20 4V20 M5 5L16 12L5 19Z",
        ["notification"] = "M5 16L7 13V9Q7 4 12 4Q17 4 17 9V13L19 16V18H5Z M10 21H14 M12 2V4",
        ["chevron-right"] = "M9 5L16 12L9 19",
        ["window-close"] = "M6 6L18 18 M18 6L6 18",
        ["window-minimize"] = "M6 12H18",
        ["window-maximize"] = "M6 6H18V18H6Z",
        ["window-restore"] = "M6 9H15V18H6Z M9 9V6H18V15H15",
    }.ToDictionary(pair => pair.Key, pair => (Geometry)StreamGeometry.Parse(pair.Value));
}
