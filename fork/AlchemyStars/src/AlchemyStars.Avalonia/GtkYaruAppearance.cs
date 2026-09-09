using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace AlchemyStars.Avalonia;

/// <summary>Ubuntu GTK/Yaru-inspired Avalonia templates; no native GTK dependency.</summary>
internal static class GtkYaruAppearance
{
    private static GtkYaruStyles controls = new();
    private static readonly Lazy<CustomAppearance.Theme> Defaults = new(() =>
    {
        using var stream = typeof(GtkYaruAppearance).Assembly.GetManifestResourceStream("AlchemyStars.UbuntuYaruTheme.json")
            ?? throw new InvalidOperationException("Embedded Ubuntu theme is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return CustomAppearance.ParseTheme(bytes.ToArray());
    });

    public static void Remove(Application app)
    {
        if (!app.Styles.Contains(controls)) return;
        AppearanceTheme.RemoveControlSkin(app, controls);
        // Fresh template identities on re-entry prevent cached template visuals
        // from keeping state selectors detached after StylesRemoved.
        controls = new GtkYaruStyles();
    }

    public static void Apply(Application app, bool dark)
    {
        CustomAppearance.Apply(app, dark, Defaults.Value);
        var r = app.Resources;
        r["AppearanceRaisedShadow"] = BoxShadows.Parse(dark ? "inset 0 1 0 0 #12ffffff, 0 1 1 0 #44000000" : "inset 0 1 0 0 #ffffff, 0 1 1 0 #16000000");
        r["AppearanceInsetShadow"] = BoxShadows.Parse(dark ? "inset 0 1 2 0 #33000000" : "inset 0 1 2 0 #14000000");
        r["AppearancePanelShadow"] = BoxShadows.Parse(dark ? "0 2 5 0 #30000000" : "0 1 3 0 #10000000");
        r["GtkFocusShadow"] = BoxShadows.Parse(dark ? "0 0 0 2 #80F27E43" : "0 0 0 2 #60C34612");
        r["AppearanceUseClassicIcons"] = false;
        r["AppearanceUseWindowsXpIcons"] = false;
        r["AppearanceUseWindows2000Icons"] = false;
        r["AppearanceUseDesktopIcons"] = false;
        r["AppearanceCardBorder"] = new Thickness(1);
        foreach (var (name, source) in new[] { ("Header", "GtkHeader"), ("Control", "GtkEntry"),
            ("NavigationSelected", "AlchemySelected"), ("NavigationSelectedText", "AlchemyText") })
        {
            r["Alchemy" + name] = r[source];
            r["Alchemy" + name + "Brush"] = r[source + "Brush"];
        }
        r["AppearanceControlFontSize"] = 13d;
        r["AppearanceActionFontSize"] = 13d;
        r["AppearanceActionFontWeight"] = FontWeight.SemiBold;
        r["AppearanceCardPadding"] = new Thickness(16);
        if (!app.Styles.Contains(controls)) app.Styles.Add(controls);
    }
}

internal sealed partial class GtkYaruStyles : Styles
{
    public GtkYaruStyles() => AvaloniaXamlLoader.Load(this);
}
