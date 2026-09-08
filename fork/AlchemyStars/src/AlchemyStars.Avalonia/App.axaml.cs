using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var preferences = new ApplicationPreferencesStore();
            var mainWindow = new MainWindow();
            var viewModel = mainWindow.InitializeWorkspace(
                new AnimationExportEngine(),
                new WorkspaceProjectStore(),
                preferences,
                Program.ExternalLinkLogPath is { } linkLogPath
                    ? uri => RecordExternalLinkAsync(linkLogPath, uri)
                    : null);
            if (Program.StartupProjectPath is not null)
                viewModel.LoadProject(Program.StartupProjectPath);
            if (Program.RenderSmokePage is { } smokePage)
                viewModel.SelectPage(smokePage);
            if (Program.RenderDialogKind is { } dialogKind)
                viewModel.ShowDiagnosticDialog(dialogKind.Equals("error", StringComparison.OrdinalIgnoreCase));
            if (Program.AccessibilitySmokeRequested)
            {
                // Keep a fully rendered, real Win32 window alive for external UI Automation.
                // It stays offscreen and out of the taskbar so verification never interrupts the user.
                mainWindow.ShowInTaskbar = false;
                mainWindow.Width = Program.RenderSmokeSize?.Width ?? 900;
                mainWindow.Height = Program.RenderSmokeSize?.Height ?? 600;
                mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                mainWindow.Position = new PixelPoint(-32000, -32000);
            }
            else if (Program.StartupSmokeRequested)
            {
                mainWindow.ShowInTaskbar = false;
                mainWindow.Opacity = Program.RenderSmokePath is null ? 0 : 1;
                if (Program.RenderSmokePath is not null)
                {
                    if (Program.RenderSmokeSize is { } renderSize)
                    {
                        mainWindow.Width = renderSize.Width;
                        mainWindow.Height = renderSize.Height;
                    }
                    mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                    mainWindow.Position = new PixelPoint(-32000, -32000);
                }
                mainWindow.Opened += async (_, _) =>
                {
                    if (viewModel.IsDualPage)
                        mainWindow.GetVisualDescendants().OfType<DualWieldView>().Single().VerifySourceInteraction();
                    if (Program.BuildPreviewSmoke) await viewModel.BuildPreviewAsync();
                    else if (Program.PreviewSmokePath is { } castPath) await viewModel.Preview.LoadAsync(castPath);
                    if (Program.FirstPersonPreviewRequested && viewModel.Preview.HasScene)
                        viewModel.Preview.ToggleFirstPerson();
                    await Task.Delay(500);
                    if (Program.DesktopSmokeRequested)
                        await DesktopSmoke.RunAsync(mainWindow, viewModel);
                    if (Program.UtilitiesSmokeRequested)
                        await UtilitiesSmoke.RunAsync(mainWindow, viewModel);
                    if (Program.ShortcutsSmokeRequested)
                        await ShortcutsSmoke.RunAsync(mainWindow, viewModel);
                    if (Program.TextMenuSmokeRequested)
                        await TextMenuSmoke.RunAsync(mainWindow, viewModel);
                    if (Program.InspectorSmokeRequested)
                        await InspectorSmoke.RunAsync(mainWindow, viewModel);
                    if (Program.AppearanceSmokeRequested)
                        await AppearanceSmoke.RunAsync(mainWindow, viewModel);
                    if (Program.AppearanceSamplesSmokeRequested)
                        await CustomAppearanceSmoke.RunSamplesAsync(mainWindow, viewModel, Path.GetDirectoryName(Program.RenderSmokePath!)!);
                    mainWindow.VerifyToolbarLayout();
                    if (viewModel.IsDualPage)
                        mainWindow.GetVisualDescendants().OfType<DualWieldView>().Single().VerifySourceSelection();
                    if (Program.RenderSmokePath is not null)
                    {
                        var width = Math.Max(1, (int)Math.Ceiling(mainWindow.ClientSize.Width));
                        var height = Math.Max(1, (int)Math.Ceiling(mainWindow.ClientSize.Height));
                        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
                        bitmap.Render(mainWindow);
                        Directory.CreateDirectory(Path.GetDirectoryName(Program.RenderSmokePath)!);
                        bitmap.Save(Program.RenderSmokePath, PngBitmapEncoderOptions.Default);
                    }
                    desktop.Shutdown((Program.BuildPreviewSmoke || Program.PreviewSmokePath is not null) && !viewModel.Preview.HasScene ? 1 : 0);
                };
            }
            if (!Program.StartupSmokeRequested && !Program.AccessibilitySmokeRequested)
                mainWindow.Opened += async (_, _) =>
                {
                    viewModel.ShowUpdateResult(Program.UpdateResultPath);
                    await viewModel.CheckForUpdatesAsync(true);
                };
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Task<bool> RecordExternalLinkAsync(string path, Uri uri)
    {
        File.AppendAllLines(path, [uri.AbsoluteUri]);
        return Task.FromResult(true);
    }
}
