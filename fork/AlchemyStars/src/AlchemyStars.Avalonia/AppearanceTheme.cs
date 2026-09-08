using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

/// <summary>Runtime appearance tokens shared by windows, popups and previews.</summary>
internal static class AppearanceTheme
{
    private static Application? owner;
    private static string currentStyle = "apple";

    internal static void RemoveControlSkin(Application app, Styles skin)
    {
        if (!app.Styles.Remove(skin)) return;
        // Avalonia's removal walks logical children. Template visuals (e.g. the
        // primary button's ContentPresenter) can retain style frames otherwise.
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var window in desktop.Windows.ToArray())
                foreach (var element in window.GetVisualDescendants().OfType<StyledElement>().Where(element => element.TemplatedParent is not null).ToArray())
                    ((IStyleHost)element).StylesRemoved([skin]);
    }

    public static void Apply(string style, string mode)
    {
        var app = Application.Current;
        if (app is null) return; // Headless engine self-tests have no UI application.
        if (!ReferenceEquals(owner, app))
        {
            if (owner is not null) owner.ActualThemeVariantChanged -= ThemeChanged;
            owner = app;
            app.ActualThemeVariantChanged += ThemeChanged;
        }
        currentStyle = style;
        app.RequestedThemeVariant = mode switch
        {
            "dark" => ThemeVariant.Dark,
            "system" => ThemeVariant.Default,
            _ => ThemeVariant.Light,
        };
        UpdateTokens(app);
    }

    private static void ThemeChanged(object? sender, EventArgs e)
    {
        if (owner is not null) UpdateTokens(owner);
    }

    private static void UpdateTokens(Application app)
    {
        var dark = app.ActualThemeVariant == ThemeVariant.Dark;
        var style = currentStyle == "custom" ? CustomAppearance.CurrentTheme?.BaseStyle ?? "apple" : currentStyle;
        if (style is "apple" or "modern-desktop" or "neumorphic")
        {
            ClassicAppleAppearance.Remove(app);
            WindowsXpAppearance.Remove(app);
            ModernDesktopAppearance.Apply(app, dark, desktopIcons: false);
            if (currentStyle == "custom") CustomAppearance.Apply(app, dark);
            return;
        }
        ModernDesktopAppearance.Remove(app);
        if (style == "windows-xp")
        {
            ClassicAppleAppearance.Remove(app);
            WindowsXpAppearance.Apply(app, dark);
            if (currentStyle == "custom") CustomAppearance.Apply(app, dark);
            return;
        }
        WindowsXpAppearance.Remove(app);
        var classic = style == "classic-apple";
        if (!classic) ClassicAppleAppearance.Remove(app);
        var r = app.Resources;
        void Brush(string name, string color)
        {
            var value = Color.Parse(color);
            r["Alchemy" + name] = value;
            r["Alchemy" + name + "Brush"] = new SolidColorBrush(value);
        }

        var background = classic
            ? (dark ? "#202022" : "#eeece7")
            : (dark ? "#1d1d1f" : "#f5f5f7");
        var sidebar = classic
            ? (dark ? "#29292c" : "#e4e2dd")
            : background;
        var surface = classic
            ? (dark ? "#303033" : "#faf9f6")
            : (dark ? "#272729" : "#ffffff");
        var raised = classic
            ? (dark ? "#39393d" : "#ffffff")
            : (dark ? "#2a2a2c" : "#fafafc");
        var ink = dark ? "#f5f5f7" : classic ? "#222224" : "#1d1d1f";
        var muted = dark ? (classic ? "#aaaab0" : "#b8bbc2") : classic ? "#646469" : "#6e6e73";
        var accent = classic ? (dark ? "#d6d4cf" : "#3a3a3c") : dark ? "#2997ff" : "#0066cc";
        Brush("Background", background);
        Brush("Sidebar", sidebar);
        Brush("Surface", surface);
        Brush("SubtleSurface", raised);
        Brush("SurfaceRaised", raised);
        Brush("Canvas", classic ? (dark ? "#1b1b1d" : "#f3f2ef") : (dark ? "#252527" : "#f5f5f7"));
        Brush("CommandBar", classic ? sidebar : "#000000");
        Brush("CommandText", classic ? ink : "#ffffff");
        Brush("OnDarkMuted", classic ? muted : "#cccccc");
        Brush("DarkTile", classic ? (dark ? "#3d3d42" : "#d8d6d1") : dark ? "#353840" : "#272729");
        Brush("Text", ink);
        Brush("MutedText", muted);
        Brush("Border", classic ? (dark ? "#46464b" : "#d1cec8") : dark ? "#383c43" : "#f0f0f0");
        Brush("BorderStrong", classic ? (dark ? "#626269" : "#b6b3ad") : dark ? "#555b66" : "#e0e0e0");
        Brush("Accent", accent);
        Brush("Action", classic ? (dark ? "#5d5d62" : "#3a3a3c") : "#0066cc"); // White primary labels retain contrast in both modes.
        Brush("Focus", classic ? (dark ? "#9b9ba2" : "#5b5b60") : dark ? "#64b3ff" : "#0071e3");
        Brush("AccentHover", classic ? (dark ? "#6d6d73" : "#242426") : "#0071e3");
        Brush("AccentBorder", accent);
        Brush("Hover", classic ? (dark ? "#414146" : "#dfddd8") : dark ? "#353d49" : "#e9edf3");
        Brush("Pressed", classic ? (dark ? "#18181a" : "#cfccc6") : dark ? "#1b2028" : "#d3dde9");
        Brush("Selected", classic ? (dark ? "#454549" : "#d8d5cf") : dark ? "#253e59" : "#e4effb");
        Brush("Hero", raised);
        Brush("Success", muted);
        Brush("Error", dark ? "#ff8896" : "#b83d50");
        Brush("Chip", classic ? (dark ? "#49494f" : "#dad7d1") : dark ? "#414853" : "#d2d2d7");
        Brush("BaseClip", classic ? (dark ? "#454549" : "#d8d5cf") : dark ? "#253e59" : "#e4effb");
        Brush("BaseEdge", accent);
        Brush("LayerClip", raised);
        Brush("LayerEdge", muted);
        r["SystemAccentColor"] = Color.Parse(accent);
        r["SystemAccentColorDark1"] = Color.Parse(accent);
        r["SystemAccentColorLight1"] = Color.Parse(classic ? (dark ? "#eeeeea" : "#57575b") : "#0071e3");
        r["AppearanceUseClassicIcons"] = classic;
        r["AppearanceControlFontSize"] = classic ? 13d : 14d;
        r["AppearanceActionFontSize"] = classic ? 13d : 17d;
        r["AppearanceActionFontWeight"] = classic ? FontWeight.SemiBold : FontWeight.Normal;
        r["AppearanceCardPadding"] = new Thickness(classic ? 16 : 24);
        Brush("Icon", classic ? (dark ? "#d6d6da" : "#3a3a3d") : accent);

        r["AppearanceRaisedShadow"] = BoxShadows.Parse(classic
            ? (dark ? "0 1 2 0 #66000000" : "0 1 2 0 #24000000")
            : "none");
        r["AppearanceInsetShadow"] = BoxShadows.Parse(classic
            ? (dark ? "inset 0 1 2 0 #99000000" : "inset 0 1 2 0 #26000000")
            : "none");
        r["AppearancePanelShadow"] = BoxShadows.Parse(classic
            ? (dark ? "0 2 8 0 #70000000, 0 10 24 0 #38000000" : "0 1 3 0 #24000000, 0 8 22 0 #1a000000")
            : "none");
        r["AppearancePanelRadius"] = new CornerRadius(classic ? 10 : 0);
        r["AppearanceHeaderRadius"] = classic ? new CornerRadius(10, 10, 0, 0) : new CornerRadius(0);
        r["AppearanceButtonRadius"] = new CornerRadius(classic ? 6 : 8);
        r["AppearanceActionRadius"] = new CornerRadius(classic ? 6 : 9999);
        r["AppearanceCardRadius"] = new CornerRadius(classic ? 10 : 18);
        r["AppearanceCardBorder"] = new Thickness(1);
        if (classic) ClassicAppleAppearance.Apply(app, dark);
        if (currentStyle == "custom") CustomAppearance.Apply(app, dark);
    }
}
