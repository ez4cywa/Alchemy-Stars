using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class SharedBaseBatchUiSmoke
{
    internal static async Task RunAsync(MainWindow window)
    {
        var original = (MainWindowViewModel)window.DataContext!;
        var directory = Path.Combine(Path.GetTempPath(), "AlchemyStars-batch-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var vm = new MainWindowViewModel(new AnimationExportEngine(), new WorkspaceProjectStore(),
            new ApplicationPreferencesStore(Path.Combine(directory, "settings.json")), new Picker());
        var button = window.FindControl<Button>("BatchActionsButton")!;
        var menu = (MenuFlyout)button.Flyout!;
        try
        {
            window.DataContext = vm;
            vm.ThemeStyleIndex = 3;
            await Task.Delay(100);
            Require(!button.IsEnabled, "Batch action is enabled without a template.");
            vm.AddAnimationPaths([Path.Combine(directory, "shared_base.cast")]);
            vm.AddLayerPaths([Path.Combine(directory, "template_layer.cast")]);
            var template = vm.SelectedAnimation!;
            template.OutputFolder = Path.Combine(directory, "exports");
            template.Layers[0].Offset = 10;
            await Task.Delay(100);
            Require(button.IsEnabled, "Batch action remained disabled with a template.");
            menu.ShowAt(button);
            await Task.Delay(100);
            var actions = menu.Items.OfType<MenuItem>().ToArray();
            Require(actions.Length == 2 && actions[0].Header?.ToString() == vm.Text.SharedBaseBatchMenu
                && actions[1].Header?.ToString() == vm.Text.GenerateSprintBatch, "Batch menu actions lost their bindings.");
            Require(ControlAutomationPeer.CreatePeerForElement(button)!.GetName() == vm.Text.BatchActions,
                "Batch action has no accessible name.");
            actions[0].IsSelected = true;
            window.UpdateLayout();
            var selectedText = actions[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == vm.Text.SharedBaseBatchMenu);
            Require(selectedText.Foreground is ISolidColorBrush ink && ink.Color == Colors.White,
                "Selected batch menu text is not readable on the Windows 2000 blue selection.");
            if (Program.RenderSmokePath is { } output && TopLevel.GetTopLevel(actions[0]) is { } popup)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var menuImage = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(popup.Bounds.Width), (int)Math.Ceiling(popup.Bounds.Height)));
                menuImage.Render(popup);
                menuImage.Save(Path.Combine(Path.GetDirectoryName(output)!, "shared-base-batch-menu.png"), PngBitmapEncoderOptions.Default);
            }
            actions[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            await Task.Delay(100);
            Require(vm.Animations.Count == 3 && ReferenceEquals(vm.Animations[0], template), "Real batch menu did not create tasks.");
            Require(vm.Animations[1].Name == template.Name && vm.Animations[2].Name == template.Name
                && vm.Animations[1].OutputName == "reload" && vm.Animations[2].OutputName == "fire"
                && vm.Animations[1].Layers[0].Offset == 10, "Batch menu did not reuse the base and layer settings.");
            vm.ToggleLanguage();
            menu.ShowAt(button);
            await Task.Delay(80);
            Require(actions[0].Header?.ToString() == vm.Text.SharedBaseBatchMenu
                && actions[1].Header?.ToString() == vm.Text.GenerateSprintBatch, "Language refresh left stale batch menu labels.");
            menu.Hide();
            vm.ToggleLanguage();
            window.VerifyToolbarLayout();
            if (Program.RenderSmokePath is { } path)
            {
                using var image = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                image.Render(window);
                image.Save(Path.Combine(Path.GetDirectoryName(path)!, "shared-base-batch-tasks.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            menu.Hide();
            window.DataContext = original;
            AppearanceTheme.Apply(original.ThemeStyleIndex switch { 1 => "classic-apple", 2 => "windows-xp", 3 => "windows-2000", 4 => "custom", _ => "apple" },
                original.ThemeModeIndex switch { 1 => "dark", 2 => "system", _ => "light" });
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("Shared-base batch UI: disabled empty state, localized menu, real click, settings and generated tasks PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Picker : IWorkspaceFilePicker
    {
        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple)
        {
            Require(purpose == FilePickerPurpose.AnimationLayer && allowMultiple, "Batch picker did not allow multiple layers.");
            return Task.FromResult<IReadOnlyList<string>>(["reload.cast", "fire.cast"]);
        }
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);
    }
}
