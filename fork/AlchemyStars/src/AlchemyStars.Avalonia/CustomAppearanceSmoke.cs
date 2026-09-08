using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class CustomAppearanceSmoke
{
    private const string ValidTheme = """
        {"version":1,"name":"Import smoke","baseStyle":"apple",
         "light":{"Accent":"#285D3D","Icon":"#285D3D","Surface":"#FAFCF8"},
         "dark":{"Accent":"#A8D59F","Icon":"#A8D59F","Surface":"#28352C"},
         "radii":{"Button":4}}
        """;

    internal static void VerifyThemeParser()
    {
        var parsed = CustomAppearance.ParseTheme(Encoding.UTF8.GetBytes(ValidTheme));
        var legacy = CustomAppearance.ParseTheme(Encoding.UTF8.GetBytes(ValidTheme.Replace("\"baseStyle\":\"apple\"", "\"baseStyle\":\"neumorphic\"")));
        Require(legacy.BaseStyle == "apple" && legacy.Light["Accent"][0] == "#285D3D",
            "Legacy custom theme bases must migrate while retaining imported colors.");
        Require(parsed.Name == "Import smoke" && parsed.Light["Accent"][0] == "#285D3D" && parsed.Radii["Button"] == 4, "Theme parse lost fields.");
        foreach (var invalid in new[]
        {
            "null", "[]", "{}", ValidTheme.Replace("\"version\":1", "\"version\":2"),
            ValidTheme.Replace("\"name\":", "\"unknown\":"),
            ValidTheme.Replace("\"baseStyle\":\"apple\"", "\"baseStyle\":\"custom\""),
            ValidTheme.Replace("\"Accent\":\"#285D3D\"", "\"Unknown\":\"#285D3D\""),
            ValidTheme.Replace("#285D3D", "#12345X"),
            ValidTheme.Replace("#285D3D", "red"),
            ValidTheme.Replace("\"Button\":4", "\"Button\":100"),
            ValidTheme.Replace("\"Button\":4", "\"Button\":-1"),
            ValidTheme.Replace("\"Button\":4", "\"Button\":4,\"Button\":3"),
            ValidTheme.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            ValidTheme.Replace("\"Accent\":\"#285D3D\"", "\"Accent\":[\"#285D3D\",\"#FFFFFF\"]"),
        })
            Reject(() => CustomAppearance.ParseTheme(Encoding.UTF8.GetBytes(invalid)));
    }

    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm, string outputDirectory)
    {
        VerifyThemeParser();
        var originalPreferences = new ApplicationPreferencesStore();
        var originalStyle = vm.ThemeStyleIndex;
        var originalMode = vm.ThemeModeIndex;
        var sandbox = Path.Combine(outputDirectory, "custom-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var library = Path.Combine(sandbox, "library");
        try
        {
            CustomAppearance.Initialize(library);
            vm.SelectPage(WorkspacePage.Settings);
            var themeSource = Path.Combine(sandbox, "source.json");
            File.WriteAllText(themeSource, ValidTheme);
            vm.ImportAppearance(themeSource, false);
            vm.ThemeModeIndex = 0;
            await Task.Delay(100);
            var picker = window.FindControl<ComboBox>("ThemeStylePicker")!;
            Require(picker.SelectedIndex == 3 && vm.ThemeStyleIndex == 3 && vm.ThemeStyles.Length == 4, "Imported theme was not selected in the real picker.");
            Require(originalPreferences.Snapshot().ThemeStyle == "custom", "Custom style was not saved.");
            Require(Accent() == Color.Parse("#285D3D"), "Light imported palette was not applied.");
            var action = window.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("primary") && button.IsEffectivelyVisible);
            var presenter = action.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().First();
            Require(presenter.Background is ISolidColorBrush actionBrush && actionBrush.Color == ((SolidColorBrush)Application.Current!.Resources["AlchemyActionBrush"]!).Color,
                $"Built-in primary button retained a previous theme material: button={action.Background}, presenter={presenter.Background}.");
            window.FindControl<Button>("AppearanceToggleButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(60);
            Require(Accent() == Color.Parse("#A8D59F"), "Quick dark switch did not apply imported dark palette.");
            var savedTheme = File.ReadAllBytes(Path.Combine(library, "theme.json"));
            File.WriteAllText(themeSource, "{\"version\":99}");
            Reject(() => vm.ImportAppearance(themeSource, false));
            Require(savedTheme.SequenceEqual(File.ReadAllBytes(Path.Combine(library, "theme.json"))) && Accent() == Color.Parse("#A8D59F"), "Rejected import replaced the existing theme.");

            var iconSource = Path.Combine(sandbox, "source.zip");
            using (var asset = AssetLoader.Open(new Uri("avares://AlchemyStars.Avalonia/Assets/AppleBlue/save.png")))
            using (var bytes = new MemoryStream())
            {
                asset.CopyTo(bytes);
                MakeZip(iconSource, [("save.png", bytes.ToArray())]);
            }
            vm.ImportAppearance(iconSource, true);
            await Task.Delay(100);
            Require(CustomAppearance.IconCount == 1, "Partial icon pack did not import.");
            var visibleIcons = window.GetVisualDescendants().OfType<ThemedIcon>().Where(icon => icon.IsEffectivelyVisible).ToArray();
            Require(visibleIcons.Any(icon => icon.Glyph == "save" && icon.HasCustomIcon), "Visible save icon did not refresh after import.");
            Require(visibleIcons.Where(icon => icon.Glyph != "save").All(icon => !icon.HasCustomIcon), "Missing icons did not retain built-in artwork.");
            var savedIcons = File.ReadAllBytes(Path.Combine(library, "icons.zip"));
            foreach (var entries in new (string, byte[])[][]
            {
                [("../save.png", new byte[24])], [("unknown.png", new byte[24])],
                [("save.png", new byte[24])], [("payload.txt", [1])],
            })
            {
                MakeZip(iconSource, entries);
                Reject(() => vm.ImportAppearance(iconSource, true));
                Require(savedIcons.SequenceEqual(File.ReadAllBytes(Path.Combine(library, "icons.zip"))) && CustomAppearance.IconCount == 1, "Rejected icon pack replaced the existing pack.");
            }

            File.Delete(themeSource);
            File.Delete(iconSource);
            CustomAppearance.Initialize(library); // Restart path: source files are no longer present.
            AppearanceTheme.Apply("custom", "dark");
            await Task.Delay(100);
            Require(CustomAppearance.CurrentTheme?.Name == "Import smoke" && CustomAppearance.IconCount == 1 && Accent() == Color.Parse("#A8D59F"), "Imported appearance did not survive a library reload.");
            foreach (var mode in new[] { 0, 1 })
            {
                vm.ThemeModeIndex = mode;
                await Task.Delay(80);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(outputDirectory, $"custom-{(mode == 0 ? "light" : "dark")}-settings.png"), PngBitmapEncoderOptions.Default);
            }
            picker.SelectedIndex = 2;
            await Task.Delay(80);
            Require(Application.Current!.Resources["AppearanceUseWindowsXpIcons"] is true, "Built-in theme could not be selected after import.");
            Require(window.GetVisualDescendants().OfType<ThemedIcon>().Any(icon => icon.Glyph == "save" && icon.HasCustomIcon), "Independent icon pack was lost on theme switch.");
            picker.SelectedIndex = 3;
            await Task.Delay(80);
            Require(Accent() == Color.Parse("#A8D59F"), "Custom theme could not be reselected.");
            window.FindControl<Button>("ResetIconsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.FindControl<Button>("ResetThemeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            Require(CustomAppearance.CurrentTheme is null && CustomAppearance.IconCount == 0 && vm.ThemeStyleIndex == 0 && picker.SelectedIndex == 0 && vm.ThemeStyles.Length == 3, "Restore buttons did not return to built-in appearance.");
            Require(window.GetVisualDescendants().OfType<ThemedIcon>().All(icon => !icon.HasCustomIcon), "Restore left stale custom images.");
            Require(!File.Exists(Path.Combine(library, "theme.json")) && !File.Exists(Path.Combine(library, "icons.zip")), "Restore did not remove saved imports.");
            File.WriteAllText(Path.Combine(library, "theme.json"), "broken");
            CustomAppearance.Initialize(library);
            Require(CustomAppearance.CurrentTheme is null && CustomAppearance.LoadError is not null, "Corrupt saved theme did not fall back safely.");
            foreach (var dimensions in new[] { new PixelSize(1, 1024), new PixelSize(1024, 1), new PixelSize(1, 1) })
            {
                var pngPath = Path.Combine(sandbox, "skinny.png");
                using (var png = new RenderTargetBitmap(dimensions))
                {
                    var fill = new Border { Background = Brushes.Gray, Width = dimensions.Width, Height = dimensions.Height };
                    fill.Measure(new Size(dimensions.Width, dimensions.Height));
                    fill.Arrange(new Rect(0, 0, dimensions.Width, dimensions.Height));
                    png.Render(fill);
                    png.Save(pngPath, PngBitmapEncoderOptions.Default);
                }
                MakeZip(iconSource, [("save.png", File.ReadAllBytes(pngPath))]);
                vm.ImportAppearance(iconSource, true);
                var decoded = CustomAppearance.Icon("save")!.PixelSize;
                Require(decoded.Width is > 0 and <= 64 && decoded.Height is > 0 and <= 64
                    && decoded.Width <= dimensions.Width && decoded.Height <= dimensions.Height, "Extreme-aspect icon was enlarged or exceeded 64px.");
                Require(CustomAppearance.LoadError is not null && vm.CanResetCustomTheme, "Importing icons hid an unresolved theme error.");
            }
            vm.ResetCustomAppearance(true);
            Require(CustomAppearance.LoadError is not null, "Restoring icons hid an unresolved theme error.");
            vm.ResetCustomAppearance(false);
            Require(CustomAppearance.LoadError is null, "Removing corrupt theme did not clear its error.");
            foreach (var basis in new[] { "apple", "classic-apple", "windows-xp" })
            {
                File.WriteAllText(themeSource, ValidTheme.Replace("\"baseStyle\":\"apple\"", $"\"baseStyle\":\"{basis}\""));
                vm.ImportAppearance(themeSource, false);
                await Task.Delay(70);
                var importButton = window.FindControl<Button>("ImportThemeButton")!;
                Require(importButton.CornerRadius.TopLeft == 4, $"Custom button radius was ignored by {basis}.");
            }
            vm.ResetCustomAppearance(false);
            Console.WriteLine("Custom appearance smoke passed: parser rejection, actual picker, light/dark, partial icons, immediate refresh, rejected-import rollback, copied-source reload, built-in switching and restore buttons.");
        }
        finally
        {
            CustomAppearance.Initialize(originalPreferences.AppearanceDirectory);
            vm.RefreshAppearanceLabel();
            vm.ThemeStyleIndex = originalStyle;
            vm.ThemeModeIndex = originalMode;
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static Color Accent() => ((SolidColorBrush)Application.Current!.Resources["AlchemyAccentBrush"]!).Color;
    internal static async Task RunSamplesAsync(MainWindow window, MainWindowViewModel vm, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var sampleDirectory = Path.Combine(AppContext.BaseDirectory, "Samples", "Appearance");
        var storage = new ApplicationPreferencesStore().AppearanceDirectory;
        var sandbox = Path.Combine(outputDirectory, "sample-import-" + Guid.NewGuid().ToString("N"));
        var style = vm.ThemeStyleIndex;
        var mode = vm.ThemeModeIndex;
        try
        {
            CustomAppearance.Initialize(sandbox);
            vm.SelectPage(WorkspacePage.Settings);
            vm.ImportAppearance(Path.Combine(sampleDirectory, "theme.json"), false);
            Require(CustomAppearance.CurrentTheme?.Name == "Forest / 森林", "Shipped theme sample is not the expected Forest theme.");
            vm.ImportAppearance(Path.Combine(sampleDirectory, "icons-template.zip"), true);
            Require(CustomAppearance.IconCount == 34, "Shipped icon sample does not contain all 34 icons.");
            foreach (var palette in new[] { 0, 1 })
            {
                vm.ThemeModeIndex = palette;
                await Task.Delay(100);
                Require(window.GetVisualDescendants().OfType<ThemedIcon>().Where(icon => icon.IsEffectivelyVisible).All(icon => icon.HasCustomIcon), "Shipped icon template did not replace all visible icons.");
                using var image = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                image.Render(window);
                image.Save(Path.Combine(outputDirectory, $"forest-{(palette == 0 ? "light" : "dark")}.png"), PngBitmapEncoderOptions.Default);
            }
            Console.WriteLine("Shipped appearance samples: JSON theme, 34 PNG icons, light/dark render PASS.");
        }
        finally
        {
            vm.ResetCustomAppearance(true);
            vm.ResetCustomAppearance(false);
            CustomAppearance.Initialize(storage);
            vm.RefreshAppearanceLabel();
            vm.ThemeStyleIndex = style;
            vm.ThemeModeIndex = mode;
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    private static void MakeZip(string path, (string Name, byte[] Bytes)[] entries)
    {
        using var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (CustomAppearance.IsImportError(error)) { return; }
        throw new InvalidOperationException("Invalid appearance import was accepted.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
