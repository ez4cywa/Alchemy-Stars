using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class UbuntuThemeSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm, string outputDirectory)
    {
        var originalStorage = new ApplicationPreferencesStore().AppearanceDirectory;
        var originalStyle = vm.ThemeStyleIndex;
        var originalMode = vm.ThemeModeIndex;
        var originalPage = vm.SelectedPage;
        var sandbox = Path.Combine(outputDirectory, "ubuntu-import-" + Guid.NewGuid().ToString("N"));
        var samples = Path.Combine(AppContext.BaseDirectory, "Samples", "Ubuntu-Yaru");
        try
        {
            Directory.CreateDirectory(outputDirectory);
            CustomAppearance.Initialize(sandbox);
            vm.ImportAppearance(Path.Combine(samples, "theme.json"), false);
            vm.ImportAppearance(Path.Combine(samples, "icons.zip"), true);
            Require(CustomAppearance.CurrentTheme?.BaseStyle == "ubuntu-yaru" && CustomAppearance.IconCount == 34,
                "Ubuntu sample import must select GTK templates and all 34 icons.");
            CustomAppearance.Initialize(sandbox);
            Require(CustomAppearance.LoadError is null && CustomAppearance.IconCount == 34,
                "Ubuntu theme/icon reload failed.");
            foreach (var mode in new[] { 0, 1 })
            {
                vm.ThemeModeIndex = mode;
                AppearanceTheme.Apply("custom", mode == 0 ? "light" : "dark");
                vm.SelectPage(WorkspacePage.Settings);
                await Task.Delay(100);
                Require(Application.Current!.Styles.OfType<GtkYaruStyles>().Count() == 1,
                    "GTK control skin is missing or duplicated.");
                Require(!Application.Current.Styles.Any(s => s is ClassicAppleStyles or WindowsXpStyles or ModernDesktopStyles),
                    "Ubuntu retained another control skin.");
                var check = window.GetVisualDescendants().OfType<CheckBox>().First(c => c.IsEffectivelyVisible);
                var state = check.IsChecked;
                foreach (var value in new bool?[] { false, true })
                {
                    check.SetCurrentValue(ToggleButton.IsCheckedProperty, value);
                    window.UpdateLayout();
                    var mark = check.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "GtkCheckMark");
                    var mixed = check.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "GtkIndeterminate");
                    Require(mark.IsVisible == (value == true) && mixed.IsVisible == (value is null),
                        $"GTK checkbox state failed in mode {mode}: requested {value}, actual {check.IsChecked}, mark {mark.IsVisible}, mixed {mixed.IsVisible}.");
                }
                check.SetCurrentValue(ToggleButton.IsCheckedProperty, state);
                var button = window.FindControl<Button>("ImportThemeButton")!;
                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
                Require(button.CornerRadius.TopLeft == 5, "GTK theme radii were ignored.");
                var classes = (IPseudoClasses)button.Classes;
                classes.Set(":pointerover", true);
                window.UpdateLayout();
                Require(Equals(presenter.Background, Application.Current.Resources["GtkButtonHoverBrush"]), "GTK hover material did not apply.");
                classes.Set(":pressed", true);
                window.UpdateLayout();
                Require(Equals(presenter.Background, Application.Current.Resources["GtkButtonPressedBrush"]), "GTK pressed material did not apply.");
                classes.Set(":pressed", false);
                classes.Set(":pointerover", false);
                button.IsEnabled = false;
                window.UpdateLayout();
                Require(Equals(presenter.Foreground, Application.Current.Resources["AlchemyMutedTextBrush"]), "GTK disabled label did not apply.");
                button.IsEnabled = true;
                classes.Set(":focus-visible", true);
                window.UpdateLayout();
                Require(Equals(presenter.BorderBrush, Application.Current.Resources["AlchemyFocusBrush"]), "GTK focus ring did not apply.");
                classes.Set(":focus-visible", false);
                foreach (var (page, key) in new[] { (WorkspacePage.Settings, "settings"), (WorkspacePage.Animations, "animations"),
                    (WorkspacePage.ModelParts, "parts"), (WorkspacePage.DualAnimations, "dual") })
                {
                    vm.SelectPage(page);
                    await Task.Delay(100);
                    window.VerifyToolbarLayout();
                    Require(window.GetVisualDescendants().OfType<ThemedIcon>().Where(i => i.IsEffectivelyVisible).All(i => i.HasCustomIcon),
                        "Ubuntu pack did not replace a visible functional icon.");
                    if (page == WorkspacePage.DualAnimations)
                    {
                        var toggle = window.FindControl<ToggleButton>("ExportModelsSwitch")
                            ?? window.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "ExportModelsSwitch");
                        var previous = toggle.IsChecked;
                        foreach (var on in new[] { false, true })
                        {
                            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, on);
                            window.UpdateLayout();
                            var thumb = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "GtkSwitchThumb");
                            Require(thumb.HorizontalAlignment == (on ? global::Avalonia.Layout.HorizontalAlignment.Right : global::Avalonia.Layout.HorizontalAlignment.Left),
                                "GTK switch thumb did not follow state.");
                        }
                        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, previous);
                    }
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(outputDirectory, $"ubuntu-{(mode == 0 ? "light" : "dark")}-{key}.png"), PngBitmapEncoderOptions.Default);
                }
                foreach (var style in new[] { 1, 2, 3, 0 })
                {
                    vm.ThemeStyleIndex = style;
                    await Task.Delay(60);
                    Require(!Application.Current.Styles.OfType<GtkYaruStyles>().Any(), "GTK templates leaked into a built-in theme.");
                    vm.ThemeStyleIndex = 4;
                    await Task.Delay(60);
                    Require(Application.Current.Styles.OfType<GtkYaruStyles>().Count() == 1, "GTK reselect lost or duplicated its skin.");
                }
            }
            Console.WriteLine("Ubuntu GTK/Yaru: import, 34 icons, reload, light/dark, checkbox states, button hover/pressed/disabled/focus, switch, four pages and skin isolation PASS.");
        }
        finally
        {
            CustomAppearance.Initialize(originalStorage);
            vm.RefreshAppearanceLabel();
            vm.ThemeStyleIndex = 0;
            vm.ThemeStyleIndex = originalStyle;
            vm.ThemeModeIndex = originalMode;
            vm.SelectPage(originalPage);
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
