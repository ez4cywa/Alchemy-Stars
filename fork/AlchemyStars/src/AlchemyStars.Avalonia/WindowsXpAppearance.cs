using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace AlchemyStars.Avalonia;

/// <summary>Luna controls are installed only while this theme is active, including popup styles.</summary>
internal static class WindowsXpAppearance
{
    private static readonly WindowsXpStyles Controls = new();

    public static void Remove(Application app)
    {
        AppearanceTheme.RemoveControlSkin(app, Controls);
        app.Resources["AppearanceUseWindowsXpIcons"] = false;
    }

    public static void Apply(Application app, bool dark)
    {
        var r = app.Resources;
        void Brush(string name, string light, string night)
        {
            var value = Color.Parse(dark ? night : light);
            r["Alchemy" + name] = value;
            r["Alchemy" + name + "Brush"] = new SolidColorBrush(value);
        }
        Brush("Background", "#ece9d8", "#202a3b");
        Brush("Sidebar", "#d9e5fa", "#263751");
        Brush("Surface", "#f5f3e8", "#2c384a");
        Brush("SubtleSurface", "#ffffff", "#34455b");
        Brush("SurfaceRaised", "#ffffe1", "#34455b");
        Brush("Canvas", "#ffffff", "#182333");
        Brush("CommandBar", "#1459d5", "#123977");
        Brush("CommandText", "#ffffff", "#ffffff");
        Brush("OnDarkMuted", "#ffffff", "#e6efff");
        Brush("DarkTile", "#245fbd", "#254c83");
        Brush("Text", "#18233b", "#f0f3f8");
        Brush("MutedText", "#525b64", "#bac9dd");
        Brush("Border", "#b4b5a4", "#50627b");
        Brush("BorderStrong", "#7f9db9", "#839ab7");
        Brush("Accent", "#164fa4", "#a6cbff");
        Brush("Action", "#286923", "#286923");
        Brush("Focus", "#003c74", "#ffc96b");
        Brush("AccentHover", "#347c2c", "#347c2c");
        Brush("AccentBorder", "#5679ab", "#769bc9");
        Brush("Hover", "#fff0cc", "#4d452f");
        Brush("Pressed", "#c9d8ef", "#1d3454");
        Brush("Selected", "#c5dcff", "#31557e");
        Brush("Hero", "#ffffff", "#34455b");
        Brush("Success", "#286923", "#a4d987");
        Brush("Error", "#ad2727", "#ffaaaa");
        Brush("Chip", "#d7e5fa", "#3b5272");
        Brush("BaseClip", "#d6e7ff", "#31557e");
        Brush("BaseEdge", "#316ac5", "#a6cbff");
        Brush("LayerClip", "#fff0c4", "#4c452e");
        Brush("LayerEdge", "#93712a", "#ebc66e");
        Brush("Icon", "#164fa4", "#d5e5ff");

        r["XpTitleBrush"] = Gradient(dark ? ["#507bb2", "#245596", "#183c79", "#3168a6"] : ["#5b9cff", "#2568df", "#1552c4", "#2c73e8"]);
        r["AlchemyCommandBarBrush"] = r["XpTitleBrush"];
        r["XpHeaderBrush"] = Gradient(dark ? ["#4c6686", "#314a69"] : ["#ffffff", "#d6e3f8"]);
        r["XpButtonBrush"] = Gradient(dark ? ["#53677f", "#35485f"] : ["#ffffff", "#f5f3e9", "#e1decb"]);
        r["XpButtonHoverBrush"] = Gradient(dark ? ["#65728a", "#44536b"] : ["#ffffff", "#fff4d1", "#efcf87"]);
        r["XpButtonPressedBrush"] = Gradient(dark ? ["#1e3049", "#405a78"] : ["#c6d5e9", "#e2ecfa"]);
        r["XpActionBrush"] = Gradient(["#6fa765", "#397d2d", "#25641f"]);
        r["XpActionHoverBrush"] = Gradient(["#7cb771", "#468a38", "#2d7225"]);
        r["XpActionPressedBrush"] = Gradient(["#174b15", "#397d2d"]);
        r["XpHighlightBrush"] = new SolidColorBrush(Color.Parse(dark ? "#7189a8" : "#ffffff"));
        r["XpHoverBorderBrush"] = new SolidColorBrush(Color.Parse("#d99221"));
        r["SystemAccentColor"] = Color.Parse(dark ? "#8db7f4" : "#316ac5");
        r["SystemAccentColorDark1"] = Color.Parse("#2454a2");
        r["SystemAccentColorLight1"] = Color.Parse("#8db7f4");
        r["AppearanceUseClassicIcons"] = false;
        r["AppearanceUseWindowsXpIcons"] = true;
        r["AppearanceControlFontSize"] = 13d;
        r["AppearanceActionFontSize"] = 13d;
        r["AppearanceActionFontWeight"] = FontWeight.Bold;
        r["AppearanceCardPadding"] = new Thickness(16);
        r["AppearanceRaisedShadow"] = BoxShadows.Parse(dark ? "inset 0 1 0 0 #7189a8, 0 1 1 0 #50000000" : "inset 0 1 0 0 #ffffff, 0 1 1 0 #40000000");
        r["AppearanceInsetShadow"] = BoxShadows.Parse("inset 1 1 2 0 #35000000");
        r["AppearancePanelShadow"] = BoxShadows.Parse("0 1 2 0 #30000000");
        r["AppearancePanelRadius"] = new CornerRadius(4);
        r["AppearanceHeaderRadius"] = new CornerRadius(4, 4, 0, 0);
        r["AppearanceButtonRadius"] = new CornerRadius(3);
        r["AppearanceActionRadius"] = new CornerRadius(5);
        r["AppearanceCardRadius"] = new CornerRadius(4);
        r["AppearanceCardBorder"] = new Thickness(1);
        if (!app.Styles.Contains(Controls)) app.Styles.Add(Controls);
    }

    private static LinearGradientBrush Gradient(string[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(Color.Parse(colors[i]), (double)i / (colors.Length - 1)));
        return brush;
    }
}

internal sealed partial class WindowsXpStyles : Styles
{
    public WindowsXpStyles() => AvaloniaXamlLoader.Load(this);
}
