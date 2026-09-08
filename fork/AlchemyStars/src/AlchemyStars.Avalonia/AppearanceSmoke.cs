using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class AppearanceSmoke
{
    private static readonly (int Index, string Key)[] Styles =
    [
        (0, "apple"),
        (1, "classic-apple"),
        (2, "neumorphic"),
    ];

    private static readonly (WorkspacePage Page, string Key)[] Pages =
    [
        (WorkspacePage.Animations, "animations"),
        (WorkspacePage.ModelParts, "parts"),
        (WorkspacePage.DualAnimations, "dual"),
        (WorkspacePage.Settings, "settings"),
        (WorkspacePage.About, "about"),
    ];

    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        var app = Application.Current!;
        var workspace = vm.Workspace;
        var stylePicker = window.FindControl<ComboBox>("ThemeStylePicker")!;
        var modePicker = window.FindControl<ComboBox>("ThemeModePicker")!;
        vm.SelectPage(WorkspacePage.Settings);
        var originalStyle = vm.ThemeStyleIndex;
        var originalMode = vm.ThemeModeIndex;
        var directory = Path.GetDirectoryName(Program.RenderSmokePath!)!;
        Directory.CreateDirectory(directory);
        try
        {
            // Change the actual frontend controls, not only the view-model values.
            foreach (var (style, styleKey) in Styles)
            foreach (var mode in new[] { 1, 0 })
            {
                vm.SelectPage(WorkspacePage.Settings);
                stylePicker.SelectedIndex = style;
                modePicker.SelectedIndex = mode;
                await Task.Delay(160);
                Require(vm.ThemeStyleIndex == style && vm.ThemeModeIndex == mode, "Appearance picker binding failed.");
                Require(ReferenceEquals(workspace, vm.Workspace), "Theme switch recreated the workspace.");
                Require(app.ActualThemeVariant == (mode == 1 ? ThemeVariant.Dark : ThemeVariant.Light), "Fluent theme mode did not switch.");
                Require(stylePicker.Bounds.Height == 44 && modePicker.Bounds.Height == 44, "Appearance control heights drifted.");
                var shadow = (BoxShadows)app.Resources["AppearancePanelShadow"]!;
                Require(shadow.Equals(BoxShadows.Parse("none")) == (style == 0), "Style switch left stale shadows.");
                Require((bool)app.Resources["AppearanceUseClassicIcons"]! == (style == 1), "Theme icon mode did not switch.");
                var snapshot = new ApplicationPreferencesStore().Snapshot();
                Require(snapshot.ThemeStyle == styleKey
                    && snapshot.ThemeMode == (mode == 1 ? "dark" : "light"), "Appearance selection did not survive a disk reload.");
                VerifyVerticalContentAlignment(window);
                VerifyThemedIcons(window, style == 1);
                if (style == 1) VerifyClassicAppleHasNoBlue(app);
                window.VerifyToolbarLayout();
                await VerifyTooltipsAsync(window, directory, style, mode);
                foreach (var (page, pageKey) in Pages)
                {
                    vm.SelectPage(page);
                    await Task.Delay(40);
                    window.VerifyToolbarLayout();
                    VerifyVerticalContentAlignment(window);
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(directory, $"{styleKey}-{(mode == 1 ? "dark" : "light")}-{pageKey}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            vm.ThemeStyleIndex = 0;
            modePicker.SelectedIndex = 2;
            await Task.Delay(160);
            Require(app.RequestedThemeVariant == ThemeVariant.Default && vm.ThemeModeIndex == 2, "System mode did not delegate to Avalonia.");
            // Verify palette updates when the actual variant changes (same event
            // path used by OS light/dark notifications), without changing Windows.
            app.RequestedThemeVariant = ThemeVariant.Dark;
            await Task.Delay(80);
            Require(((SolidColorBrush)app.Resources["AlchemySurfaceBrush"]!).Color == Color.Parse("#272729"), "Actual dark change did not update palette.");
            app.RequestedThemeVariant = ThemeVariant.Light;
            await Task.Delay(80);
            Require(((SolidColorBrush)app.Resources["AlchemySurfaceBrush"]!).Color == Colors.White, "Actual light change did not update palette.");
            vm.ThemeStyleIndex = 1;
            vm.ToggleLanguage();
            await Task.Delay(80);
            Require(vm.ThemeModeIndex == 2 && vm.ThemeStyleIndex == 1, "Language refresh reset appearance selection.");
            vm.ToggleLanguage();
            Console.WriteLine("Appearance smoke passed: three live styles, two palettes, all pages, centered control text, graphite-only Classic Apple, persistence, system delegation and language refresh.");
        }
        finally
        {
            vm.ThemeStyleIndex = originalStyle;
            vm.ThemeModeIndex = originalMode;
            AppearanceTheme.Apply(originalStyle switch { 1 => "classic-apple", 2 => "neumorphic", _ => "apple" }, originalMode switch { 1 => "dark", 2 => "system", _ => "light" });
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyVerticalContentAlignment(MainWindow window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button is not RepeatButton))
            Require(button.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"Button text is not vertically centered: {AutomationProperties.GetName(button) ?? button.GetType().Name}.");
        foreach (var toggle in window.GetVisualDescendants().OfType<ToggleButton>())
            Require(toggle.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"Toggle text is not vertically centered: {AutomationProperties.GetName(toggle) ?? toggle.GetType().Name}.");
        foreach (var textBox in window.GetVisualDescendants().OfType<TextBox>().Where(control => !control.AcceptsReturn))
            Require(textBox.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"TextBox text is not vertically centered: {AutomationProperties.GetName(textBox) ?? textBox.GetType().Name}.");
        foreach (var comboBox in window.GetVisualDescendants().OfType<ComboBox>())
            Require(comboBox.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"ComboBox text is not vertically centered: {AutomationProperties.GetName(comboBox) ?? comboBox.GetType().Name}.");
        foreach (var item in window.GetVisualDescendants().OfType<ListBoxItem>())
            Require(item.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                "List item text is not vertically centered.");
    }

    private static void VerifyThemedIcons(MainWindow window, bool classic)
    {
        var icons = window.GetVisualDescendants().OfType<ThemedIcon>().ToArray();
        Require(icons.Length > 20, "The complete icon system was not instantiated.");
        Require(icons.All(icon => icon.UseClassicGlyph == classic), "A screen retained the wrong theme icon family.");
        foreach (var (glyph, bounds) in ThemedIcon.ClassicGlyphBounds)
        {
            Require(bounds.Left >= 2.5 && bounds.Top >= 2.5 && bounds.Right <= 17.5 && bounds.Bottom <= 17.5,
                $"Classic Apple glyph '{glyph}' breaches the 2.5-unit optical safe area: {bounds}.");
        }
        foreach (var icon in icons)
        {
            var button = icon.GetVisualAncestors().OfType<Button>().FirstOrDefault();
            if (button is null) continue;
            var origin = icon.TranslatePoint(default, button)
                ?? throw new InvalidOperationException($"Theme icon '{icon.Glyph}' is detached from its button.");
            Require(origin.X >= 2 && origin.Y >= 2
                && origin.X + icon.Bounds.Width <= button.Bounds.Width - 2
                && origin.Y + icon.Bounds.Height <= button.Bounds.Height - 2,
                $"Theme icon '{icon.Glyph}' is obscured or clipped by its button.");
        }
    }

    private static void VerifyClassicAppleHasNoBlue(Application app)
    {
        foreach (var key in new[]
        {
            "AlchemyAccentBrush",
            "AlchemyActionBrush",
            "AlchemyFocusBrush",
            "AlchemyAccentHoverBrush",
            "AlchemySelectedBrush",
            "AlchemyIconBrush",
        })
        {
            var color = ((SolidColorBrush)app.Resources[key]!).Color;
            Require(color.B <= color.R + 12 || color.B <= color.G + 6,
                $"Classic Apple contains a blue-dominant token: {key}={color}.");
        }
    }

    private static async Task VerifyTooltipsAsync(MainWindow window, string directory, int style, int mode)
    {
        var index = 0;
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("toolbar")))
        {
            ToolTip.SetIsOpen(button, true);
            try
            {
                await Task.Delay(100);
                var tip = button.GetValue(global::Avalonia.Controls.Diagnostics.ToolTipDiagnostics.ToolTipProperty) as ToolTip
                    ?? throw new InvalidOperationException("Toolbar tooltip failed to open.");
                var text = tip.GetVisualDescendants().OfType<TextBlock>().Single(t => !string.IsNullOrEmpty(t.Text));
                var expected = ((SolidColorBrush)Application.Current!.Resources["AlchemyTextBrush"]!).Color;
                Require(text.Foreground is ISolidColorBrush foreground && foreground.Color == expected,
                    "Toolbar text color leaked into its tooltip.");
                Require(tip.Background is ISolidColorBrush background && background.Color != expected,
                    "Tooltip surface hides its text.");
                Require(text.Text == ToolTip.GetTip(button)?.ToString(), "Tooltip label changed.");
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(tip.Bounds.Width), (int)Math.Ceiling(tip.Bounds.Height)));
                bitmap.Render(tip);
                bitmap.Save(Path.Combine(directory, $"tooltip-{style}-{mode}-{index++}.png"), PngBitmapEncoderOptions.Default);
            }
            finally { ToolTip.SetIsOpen(button, false); }
        }
    }
}
