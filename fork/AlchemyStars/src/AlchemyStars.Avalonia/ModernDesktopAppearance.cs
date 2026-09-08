using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace AlchemyStars.Avalonia;

internal static class ModernDesktopAppearance
{
    private static readonly ModernDesktopStyles Controls = new();
    public static void Remove(Application app)
    {
        AppearanceTheme.RemoveControlSkin(app, Controls);
        app.Resources["AppearanceUseDesktopIcons"] = false;
    }
    public static void Apply(Application app, bool dark, bool desktopIcons = true)
    {
        var r = app.Resources;
        void Brush(string name, string color)
        {
            var value = Color.Parse(color);
            r["Alchemy" + name] = value;
            r["Alchemy" + name + "Brush"] = new SolidColorBrush(value);
        }

        var background = dark ? "#202022" : "#f2f2f4";
        var surface = dark ? "#272729" : "#ffffff";
        var raised = dark ? "#2a2a2c" : "#fafafc";
        var ink = dark ? "#f5f5f7" : "#1d1d1f";
        var muted = dark ? "#b8bbc2" : "#6e6e73";
        var accent = dark ? "#2997ff" : "#0066cc";
        Brush("Background", background);
        Brush("Sidebar", dark ? "#29292c" : "#e9e9ed");
        Brush("Header", dark ? "#2d2d30" : "#f4f4f6");
        Brush("Surface", surface);
        Brush("SubtleSurface", raised);
        Brush("SurfaceRaised", raised);
        Brush("Canvas", dark ? "#252527" : "#f5f5f7");
        Brush("CommandBar", dark ? "#303033" : "#eeeef1");
        Brush("CommandText", ink);
        Brush("OnDarkMuted", muted);
        Brush("DarkTile", dark ? "#353840" : "#272729");
        Brush("Text", ink);
        Brush("MutedText", muted);
        Brush("Border", dark ? "#414145" : "#dedee3");
        Brush("BorderStrong", dark ? "#626267" : "#c8c8cf");
        Brush("Accent", accent);
        Brush("Action", "#0066cc"); // White primary labels retain contrast in both modes.
        Brush("Focus", dark ? "#64b3ff" : "#0071e3");
        Brush("AccentHover", "#0071e3");
        Brush("AccentBorder", accent);
        Brush("Hover", dark ? "#353d49" : "#e9edf3");
        Brush("Pressed", dark ? "#1b2028" : "#d3dde9");
        Brush("Selected", dark ? "#253e59" : "#e4effb");
        Brush("NavigationSelected", dark ? "#075ec1" : "#0869da");
        Brush("NavigationSelectedText", "#ffffff");
        Brush("Control", dark ? "#39393d" : "#ffffff");
        Brush("Hero", raised);
        Brush("Success", muted);
        Brush("Error", dark ? "#ff8896" : "#b83d50");
        Brush("Chip", dark ? "#414853" : "#d2d2d7");
        Brush("BaseClip", dark ? "#253e59" : "#e4effb");
        Brush("BaseEdge", accent);
        Brush("LayerClip", raised);
        Brush("LayerEdge", muted);
        r["SystemAccentColor"] = Color.Parse("#0066cc");
        r["SystemAccentColorDark1"] = Color.Parse("#0066cc");
        r["SystemAccentColorLight1"] = Color.Parse("#0071e3");

        r["AppearanceRaisedShadow"] = BoxShadows.Parse("none");
        r["AppearanceInsetShadow"] = BoxShadows.Parse("none");
        r["AppearancePanelShadow"] = BoxShadows.Parse("none");
        r["AppearancePanelRadius"] = new CornerRadius(8);
        r["AppearanceHeaderRadius"] = new CornerRadius(8, 8, 0, 0);
        r["AppearanceButtonRadius"] = new CornerRadius(7);
        r["AppearanceActionRadius"] = new CornerRadius(7);
        r["AppearanceCardRadius"] = new CornerRadius(10);
        r["AppearanceCardBorder"] = new Thickness(1);
        Brush("Icon", accent);
        r["AppearanceUseClassicIcons"] = false;
        r["AppearanceUseWindowsXpIcons"] = false;
        r["AppearanceUseDesktopIcons"] = desktopIcons;
        r["AppearanceControlFontSize"] = 13d;
        r["AppearanceActionFontSize"] = 13d;
        r["AppearanceActionFontWeight"] = FontWeight.SemiBold;
        r["AppearanceCardPadding"] = new Thickness(20);
        if (!app.Styles.Contains(Controls)) app.Styles.Add(Controls);
    }
}

internal sealed partial class ModernDesktopStyles : Styles
{
    public ModernDesktopStyles() => AvaloniaXamlLoader.Load(this);
}
