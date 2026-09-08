using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class UtilitiesSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        var originalStyle = vm.ThemeStyleIndex;
        var originalMode = vm.ThemeModeIndex;
        var originalPage = vm.SelectedPage;
        var remember = vm.RememberArms;
        var automatic = vm.AutoUpdateEnabled;
        var content = window.FindControl<StackPanel>("SettingsContent")!;
        var scroller = content.GetVisualAncestors().OfType<ScrollViewer>().First();
        var utilities = window.FindControl<Border>("UtilitiesCard")!;
        var appearance = window.FindControl<ComboBox>("ThemeStylePicker")!.GetVisualAncestors().OfType<Border>()
            .First(b => b.Classes.Contains("card"));
        var armsToggle = window.FindControl<CheckBox>("RememberArmsCheckBox")!;
        var updateToggle = window.FindControl<CheckBox>("AutoUpdateCheckBox")!;
        var directory = Path.GetDirectoryName(Program.RenderSmokePath!)!;
        try
        {
            vm.SelectPage(WorkspacePage.Settings);
            await Task.Delay(80);
            Require(content.Children.IndexOf(utilities) < content.Children.IndexOf(appearance), "Appearance must follow Utilities.");
            armsToggle.IsChecked = !remember;
            updateToggle.IsChecked = !automatic;
            Require(vm.RememberArms == !remember && vm.AutoUpdateEnabled == !automatic, "Utility toggle binding failed.");
            var disk = new ApplicationPreferencesStore().Snapshot();
            Require(disk.RememberArms == !remember && disk.AutoUpdateEnabled == !automatic, "Utility toggles did not persist immediately.");
            armsToggle.IsChecked = remember;
            updateToggle.IsChecked = automatic;
            foreach (var style in new[] { 0, 1, 2 })
            foreach (var mode in new[] { 0, 1 })
            {
                vm.ThemeStyleIndex = style;
                vm.ThemeModeIndex = mode;
                foreach (var target in new[] { (Card: utilities, Name: "utilities"), (Card: appearance, Name: "appearance") })
                {
                    await Task.Delay(80);
                    scroller.Offset = new Vector(0, target.Card.Bounds.Y);
                    await Task.Delay(80);
                    Require(target.Card.Bounds.Right <= content.Bounds.Width + 1, "Settings card overflows horizontally.");
                    Require(armsToggle.Bounds.Height >= 24 && updateToggle.Bounds.Height >= 24, "Utility toggles have collapsed.");
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(directory, $"{target.Name}-{style}-{mode}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            Console.WriteLine("Utilities UI: immediate persistence, toggle binding, three styles x two modes, appearance below utilities, scrolling PASS");
        }
        finally
        {
            vm.RememberArms = remember;
            vm.AutoUpdateEnabled = automatic;
            vm.ThemeStyleIndex = originalStyle;
            vm.ThemeModeIndex = originalMode;
            vm.SelectPage(originalPage);
            scroller.Offset = default;
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
