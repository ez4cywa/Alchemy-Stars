using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace AlchemyStars.Avalonia;

/// <summary>Windows 2000 classic gray materials and two-level edges, independently switchable.</summary>
internal static class Windows2000Appearance
{
    private static readonly Windows2000Styles Controls = new();

    public static void Remove(Application app)
    {
        AppearanceTheme.RemoveControlSkin(app, Controls);
        app.Resources["AppearanceUseWindows2000Icons"] = false;
    }

    public static void Apply(Application app, bool dark)
    {
        var r = app.Resources;
        void Brush(string name, string light, string night)
        {
            var color = Color.Parse(dark ? night : light);
            r["Alchemy" + name] = color;
            r["Alchemy" + name + "Brush"] = new SolidColorBrush(color);
        }
        Brush("Background", "#d4d0c8", "#202020");
        Brush("Sidebar", "#c0c0c0", "#303030");
        Brush("Surface", "#d4d0c8", "#383838");
        Brush("SubtleSurface", "#d4d0c8", "#414141");
        Brush("SurfaceRaised", "#ffffff", "#414141");
        Brush("Canvas", "#ffffff", "#202020");
        Brush("CommandBar", "#173579", "#173579");
        Brush("CommandText", "#ffffff", "#ffffff");
        Brush("OnDarkMuted", "#c0c0c0", "#c0c0c0");
        Brush("DarkTile", "#173579", "#173579");
        Brush("Text", "#202020", "#ffffff");
        Brush("MutedText", "#505050", "#c0c0c0");
        Brush("Border", "#808080", "#808080");
        Brush("BorderStrong", "#404040", "#b0b0b0");
        Brush("Accent", "#173579", "#b5ccff");
        Brush("Action", "#d4d0c8", "#414141");
        Brush("Focus", "#173579", "#ffdf80");
        Brush("AccentHover", "#e0ddd7", "#505050");
        Brush("AccentBorder", "#245c24", "#b0ddb0");
        Brush("Hover", "#e0ddd7", "#404040");
        Brush("Pressed", "#c0c0c0", "#282828");
        Brush("Selected", "#173579", "#173579");
        Brush("Hero", "#d4d0c8", "#383838");
        Brush("Success", "#245c24", "#b0ddb0");
        Brush("Error", "#a02020", "#ffaaaa");
        Brush("Chip", "#e0ddd7", "#34445d");
        Brush("BaseClip", "#c0c0c0", "#34445d");
        Brush("BaseEdge", "#173579", "#b5ccff");
        Brush("LayerClip", "#dce4d4", "#3a4432");
        Brush("LayerEdge", "#245c24", "#b0ddb0");
        Brush("Icon", "#173579", "#b5ccff");
        r["Win2000OnActionBrush"] = new SolidColorBrush(Color.Parse(dark ? "#ffffff" : "#000000"));
        r["Win2000OnSelectedBrush"] = Brushes.White;
        r["Win2000RedBrush"] = new SolidColorBrush(Color.Parse("#a02020"));
        r["Win2000EdgeBrush"] = new SolidColorBrush(Color.Parse(dark ? "#101010" : "#404040"));
        r["Win2000HighlightBrush"] = new SolidColorBrush(Color.Parse(dark ? "#808080" : "#ffffff"));
        r["SystemAccentColor"] = Color.Parse("#173579");
        r["SystemAccentColorDark1"] = Color.Parse("#173579");
        r["SystemAccentColorLight1"] = Color.Parse("#b5ccff");
        r["AppearanceUseClassicIcons"] = false;
        r["AppearanceUseWindowsXpIcons"] = false;
        r["AppearanceUseDesktopIcons"] = false;
        r["AppearanceUseWindows2000Icons"] = true;
        r["AppearanceControlFontSize"] = 13d;
        r["AppearanceActionFontSize"] = 13d;
        r["AppearanceActionFontWeight"] = FontWeight.Bold;
        r["AppearanceCardPadding"] = new Thickness(16);
        r["AppearanceRaisedShadow"] = BoxShadows.Parse(dark ? "inset 1 1 0 0 #808080, inset -1 -1 0 0 #101010" : "inset 1 1 0 0 #ffffff, inset -1 -1 0 0 #808080");
        r["AppearanceInsetShadow"] = BoxShadows.Parse(dark ? "inset 2 2 0 0 #101010" : "inset 2 2 0 0 #b0b0b0");
        r["AppearancePanelShadow"] = BoxShadows.Parse(dark ? "inset 1 1 0 0 #808080, 1 1 0 0 #101010" : "inset 1 1 0 0 #ffffff, 1 1 0 0 #404040");
        foreach (var key in new[] { "Panel", "Header", "Button", "Action", "Card" })
            r["Appearance" + key + "Radius"] = new CornerRadius(0);
        r["AppearanceCardBorder"] = new Thickness(2);
        if (!app.Styles.Contains(Controls)) app.Styles.Add(Controls);
    }
}

internal sealed partial class Windows2000Styles : Styles
{
    public Windows2000Styles() => AvaloniaXamlLoader.Load(this);
}
