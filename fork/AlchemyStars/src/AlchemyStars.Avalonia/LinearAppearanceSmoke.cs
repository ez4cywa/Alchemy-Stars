using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

/// <summary>Checks the default skin's semantic colors and live control styling.</summary>
internal static class LinearAppearanceSmoke
{
    internal static void Verify(MainWindow window)
    {
        foreach (var background in new[] { "Surface", "SurfaceRaised", "Control" })
        {
            foreach (var foreground in new[] { "Text", "MutedText", "Accent" })
                RequireContrast(foreground, background, 4.5);
            RequireContrast("BorderStrong", background, 3);
            RequireContrast("Focus", background, 3);
        }
        RequireContrast(Colors.White, TokenColor("Action"), 4.5, "White / Action");
        RequireContrast(Colors.White, TokenColor("AccentHover"), 4.5, "White / AccentHover");
        foreach (var toggle in window.GetVisualDescendants().OfType<ToggleButton>()
            .Where(toggle => toggle.Name == "ExportModelsSwitch" && toggle.IsEffectivelyVisible))
        {
            var previous = toggle.IsChecked;
            try
            {
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                window.UpdateLayout();
                var track = toggle.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "SwitchTrack");
                var thumb = toggle.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>().Single(shape => shape.Name == "SwitchThumb");
                Require(track.Background is ISolidColorBrush fill && fill.Color == TokenColor("Action"),
                    "Checked Linear switch must use the action fill, not the accent text color.");
                Require(thumb.Fill is ISolidColorBrush ink && ink.Color == Colors.White,
                    "Checked Linear switch lost its contrasting thumb.");
                RequireContrast(Colors.White, TokenColor("Action"), 3, "Switch thumb / track");
            }
            finally { toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, previous); }
        }

        var selection = TokenColor("NavigationSelected");
        var spread = Math.Max(selection.R, Math.Max(selection.G, selection.B))
            - Math.Min(selection.R, Math.Min(selection.G, selection.B));
        Require(spread <= 12 && selection == TokenColor("Selected") && selection != TokenColor("Action"),
            $"Linear navigation lost its neutral selection: {selection}.");
        RequireContrast("NavigationSelectedText", "NavigationSelected", 4.5);

        foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible))
            Require(text.LetterSpacing >= 0, $"Linear text retained negative tracking: '{text.Text}'.");

        foreach (var button in window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("activity") && button.Classes.Contains("selected")))
        {
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            Require(presenter.Background is ISolidColorBrush background && background.Color == selection,
                "Selected navigation presenter did not use the neutral Linear surface.");
            Require(presenter.Foreground is ISolidColorBrush foreground && foreground.Color == TokenColor("NavigationSelectedText"),
                "Selected navigation presenter retained another skin's foreground.");
        }
    }

    private static Color TokenColor(string token) => Application.Current!.Resources["Alchemy" + token + "Brush"] is ISolidColorBrush brush
        ? brush.Color
        : throw new InvalidOperationException($"Linear token {token} must be a solid semantic color.");

    private static void RequireContrast(string foreground, string background, double minimum) =>
        RequireContrast(TokenColor(foreground), TokenColor(background), minimum, foreground + " / " + background);

    private static void RequireContrast(Color foreground, Color background, double minimum, string name)
    {
        Require(foreground.A == 255 && background.A == 255, $"Linear contrast pair {name} unexpectedly uses transparency.");
        var light = Luminance(foreground);
        var dark = Luminance(background);
        var ratio = (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
        Require(ratio >= minimum, $"Linear contrast {name} is {ratio:F3}:1; expected at least {minimum}:1.");
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
