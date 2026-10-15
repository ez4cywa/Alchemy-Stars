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

        // Linear-inspired desktop tokens. Keep the legacy "apple" preference key
        // in AppearanceTheme so existing settings and custom themes remain valid.
        var background = dark ? "#010102" : "#f6f7f8";
        var surface = dark ? "#0f1011" : "#ffffff";
        var raised = dark ? "#18191a" : "#f4f5f6";
        var ink = dark ? "#f7f8f8" : "#202125";
        var muted = dark ? "#a0a4ad" : "#646970";
        const string action = "#5e6ad2";
        var accent = dark ? "#9da7ff" : "#535fbc";
        Brush("Background", background);
        Brush("Sidebar", dark ? "#101112" : "#f6f7f8");
        Brush("Header", surface);
        Brush("Surface", surface);
        Brush("SubtleSurface", raised);
        Brush("SurfaceRaised", raised);
        Brush("Canvas", background);
        Brush("CommandBar", surface);
        Brush("CommandText", ink);
        Brush("OnDarkMuted", muted);
        Brush("DarkTile", raised);
        Brush("Text", ink);
        Brush("MutedText", muted);
        Brush("Border", dark ? "#23252a" : "#e3e5e8");
        Brush("BorderStrong", dark ? "#686c74" : "#878b95");
        Brush("Accent", accent);
        Brush("Action", action); // White labels meet 4.5:1 in both modes.
        Brush("Focus", dark ? "#9da7ff" : accent);
        Brush("AccentHover", "#535fbc");
        Brush("AccentBorder", accent);
        Brush("Hover", dark ? "#202125" : "#eceef0");
        Brush("Pressed", dark ? "#2b2d33" : "#e0e2e6");
        Brush("Selected", dark ? "#24262b" : "#e9eaed");
        Brush("NavigationSelected", dark ? "#24262b" : "#e9eaed");
        Brush("NavigationSelectedText", ink);
        Brush("Control", dark ? "#141516" : "#ffffff");
        Brush("Hero", raised);
        Brush("Success", dark ? "#67ce8d" : "#237542");
        Brush("Error", dark ? "#ff8d9b" : "#b3263b");
        Brush("Chip", dark ? "#24262b" : "#e9eaed");
        Brush("BaseClip", dark ? "#24262b" : "#e9eaed");
        Brush("BaseEdge", accent);
        Brush("LayerClip", raised);
        Brush("LayerEdge", muted);
        r["SystemAccentColor"] = Color.Parse(action);
        r["SystemAccentColorDark1"] = Color.Parse("#535fbc");
        r["SystemAccentColorLight1"] = Color.Parse("#9da7ff");

        r["AppearanceRaisedShadow"] = BoxShadows.Parse("none");
        r["AppearanceInsetShadow"] = BoxShadows.Parse("none");
        r["AppearancePanelShadow"] = BoxShadows.Parse("none");
        r["AppearancePanelRadius"] = new CornerRadius(0);
        r["AppearanceHeaderRadius"] = new CornerRadius(0);
        r["AppearanceButtonRadius"] = new CornerRadius(6);
        r["AppearanceActionRadius"] = new CornerRadius(6);
        r["AppearanceCardRadius"] = new CornerRadius(8);
        r["AppearanceCardBorder"] = new Thickness(1);
        Brush("Icon", muted);
        r["AppearanceUseClassicIcons"] = false;
        r["AppearanceUseWindowsXpIcons"] = false;
        r["AppearanceUseDesktopIcons"] = desktopIcons;
        r["AppearanceControlFontSize"] = 13d;
        r["AppearanceActionFontSize"] = 13d;
        r["AppearanceActionFontWeight"] = FontWeight.SemiBold;
        r["AppearanceCardPadding"] = new Thickness(16);
        if (!app.Styles.Contains(Controls)) app.Styles.Add(Controls);
    }
}

internal sealed partial class ModernDesktopStyles : Styles
{
    public ModernDesktopStyles() => AvaloniaXamlLoader.Load(this);
}
