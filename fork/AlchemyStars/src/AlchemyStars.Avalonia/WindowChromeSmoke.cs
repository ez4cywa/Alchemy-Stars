using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class WindowChromeSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        var style = vm.ThemeStyleIndex;
        var mode = vm.ThemeModeIndex;
        try
        {
            foreach (var index in new[] { 1, 0, 2, 3 })
            foreach (var colorMode in new[] { 0, 1 })
            {
                vm.ThemeStyleIndex = index;
                vm.ThemeModeIndex = colorMode;
                await Task.Delay(100);
                if (Program.RenderSmokePath is { } path)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var image = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                    image.Render(window);
                    image.Save(Path.Combine(Path.GetDirectoryName(path)!, $"chrome-{index}-{colorMode}.png"), PngBitmapEncoderOptions.Default);
                }
                foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("window-control")))
                {
                    var row = button.GetVisualAncestors().OfType<Grid>().First();
                    var origin = button.TranslatePoint(default, row)!.Value;
                    var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
                    if (origin.Y < 0 || origin.Y + button.Bounds.Height > row.RowDefinitions[0].ActualHeight + 0.1)
                        throw new InvalidOperationException("Window caption buttons spill into the command toolbar row.");
                    if (presenter.CornerRadius != default || presenter.BoxShadow.Count != 0)
                        throw new InvalidOperationException("Window caption buttons inherit a rounded or shadowed control bezel.");
                }
            }
        }
        finally
        {
            vm.ThemeStyleIndex = style;
            vm.ThemeModeIndex = mode;
        }
        Console.WriteLine("Window caption: four themes, both palettes, bounds, square corners and shadow isolation PASS");
    }
}
