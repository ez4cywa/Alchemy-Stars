using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace AlchemyStars.Avalonia;

/// <summary>Independent platinum/graphite control materials, installed only for Classic Apple.</summary>
internal static class ClassicAppleAppearance
{
    private static readonly ClassicAppleStyles Controls = new();

    public static void Remove(Application app) => AppearanceTheme.RemoveControlSkin(app, Controls);

    public static void Apply(Application app, bool dark)
    {
        var r = app.Resources;
        void Material(string key, string[] light, string[] night) => r[key] = Gradient(dark ? night : light);
        Material("AppleChromeBrush", ["#eeede9", "#dedcd7"], ["#39393d", "#2d2d30"]);
        Material("AppleHeaderBrush", ["#f7f6f3", "#e9e7e2"], ["#3e3e43", "#343438"]);
        Material("AppleButtonBrush", ["#ffffff", "#f4f3ef", "#e7e5df"], ["#57575d", "#48484e", "#414146"]);
        Material("AppleButtonHoverBrush", ["#ffffff", "#faf9f7", "#efeee9"], ["#63636a", "#535359"]);
        Material("AppleButtonPressedBrush", ["#c9c6bf", "#dedbd5"], ["#28282b", "#36363b"]);
        Material("AppleActionBrush", ["#646468", "#414144"], ["#69696e", "#505055"]);
        Material("AppleActionHoverBrush", ["#737378", "#505054"], ["#77777c", "#5a5a60"]);
        Material("AppleActionPressedBrush", ["#343436", "#4b4b4f"], ["#38383c", "#4a4a50"]);
        Material("AppleThumbBrush", ["#ffffff", "#e9e7e2"], ["#e8e8ec", "#bebec6"]);
        r["AppleEdgeBrush"] = new SolidColorBrush(Color.Parse(dark ? "#707078" : "#aaa7a0"));
        r["AppleHoverEdgeBrush"] = new SolidColorBrush(Color.Parse(dark ? "#aaaab2" : "#77746f"));
        r["AppleFocusShadow"] = BoxShadows.Parse(dark ? "0 0 0 2 #a7a7b0" : "0 0 0 2 #77746f");
        r["AppearanceRaisedShadow"] = BoxShadows.Parse(dark ? "inset 0 1 0 0 #22ffffff, 0 1 2 0 #55000000" : "inset 0 1 0 0 #ffffff, 0 1 2 0 #18000000");
        r["AppearanceInsetShadow"] = BoxShadows.Parse(dark ? "inset 0 1 2 0 #65000000" : "inset 0 1 2 0 #22000000");
        r["AppearancePanelShadow"] = BoxShadows.Parse(dark ? "0 1 4 0 #40000000" : "0 1 3 0 #14000000");
        r["AppearancePanelRadius"] = new CornerRadius(10);
        r["AppearanceHeaderRadius"] = new CornerRadius(10, 10, 0, 0);
        r["AppearanceButtonRadius"] = new CornerRadius(6);
        r["AppearanceActionRadius"] = new CornerRadius(6);
        r["AppearanceCardRadius"] = new CornerRadius(10);
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

internal sealed partial class ClassicAppleStyles : Styles
{
    public ClassicAppleStyles() => AvaloniaXamlLoader.Load(this);
}
