using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => KeepEditorsWithinWindow();
    }

    private void KeepEditorsWithinWindow()
    {
        // Fixed side panes need to give space back after a user narrows the window.
        // Keep the center usable without discarding the user's normal splitter positions.
        if (ClientSize.Width < MinWidth) return;
        foreach (var editor in new[] { AnimationEditor, PartsEditor, DualWorkspace.EditorGrid })
        {
            var columns = editor.ColumnDefinitions;
            var overflow = columns[0].Width.Value + columns[4].Width.Value
                - (ClientSize.Width - 48 - 8 - columns[2].MinWidth);
            if (overflow <= 0) continue;
            var libraryReduction = Math.Min(overflow, columns[0].Width.Value - columns[0].MinWidth);
            columns[0].Width = new GridLength(columns[0].Width.Value - libraryReduction);
            columns[4].Width = new GridLength(Math.Max(columns[4].MinWidth, columns[4].Width.Value - overflow + libraryReduction));
        }
    }

    private void RestoreLayoutClick(object? sender, RoutedEventArgs e)
    {
        foreach (var editor in new[] { AnimationEditor, PartsEditor })
        {
            editor.ColumnDefinitions[0].Width = new GridLength(240);
            editor.ColumnDefinitions[4].Width = new GridLength(320);
        }
        AnimationEditor.RowDefinitions[0].Height = new GridLength(3, GridUnitType.Star);
        AnimationEditor.RowDefinitions[2].Height = new GridLength(2, GridUnitType.Star);
        DualWorkspace.EditorGrid.ColumnDefinitions[0].Width = new GridLength(240);
        DualWorkspace.EditorGrid.ColumnDefinitions[4].Width = new GridLength(320);
        KeepEditorsWithinWindow();
    }

    public MainWindowViewModel InitializeWorkspace(
        IAnimationExportEngine engine,
        WorkspaceProjectStore projectStore,
        ApplicationPreferencesStore preferences,
        Func<Uri, Task<bool>>? externalUriLaunch = null)
    {
        var viewModel = new MainWindowViewModel(
            engine,
            projectStore,
            preferences,
            new AvaloniaFilePickerAdapter(this, preferences, externalUriLaunch));
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.IsDialogOpen) && viewModel.IsDialogOpen)
                Dispatcher.UIThread.Post(() => DialogCloseButton.Focus());
        };
        return viewModel;
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    internal void VerifyToolbarLayout()
    {
        var buttons = this.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("toolbar")).ToArray();
        if (buttons.Length != 4) throw new InvalidOperationException("The project toolbar must expose four commands.");
        foreach (var button in buttons)
        {
            var themedIcon = button.GetVisualDescendants().OfType<ThemedIcon>().SingleOrDefault();
            if (themedIcon is not null)
            {
                var iconOrigin = themedIcon.TranslatePoint(default, button)
                    ?? throw new InvalidOperationException("Toolbar theme icon is not attached to its button.");
                if (!themedIcon.HasLegacyBitmap || button.Bounds.Width < 44 || button.Bounds.Height < 44
                    || themedIcon.Bounds.Width < themedIcon.Width || themedIcon.Bounds.Height < themedIcon.Height
                    || iconOrigin.X < 2 || iconOrigin.Y < 2
                    || iconOrigin.X + themedIcon.Bounds.Width > button.Bounds.Width - 2
                    || iconOrigin.Y + themedIcon.Bounds.Height > button.Bounds.Height - 2)
                    throw new InvalidOperationException("A toolbar theme icon or hit target is clipped.");
                continue;
            }
            var productIcon = button.GetVisualDescendants().OfType<Image>().SingleOrDefault();
            if (productIcon is not null)
            {
                var imageOrigin = productIcon.TranslatePoint(default, button)
                    ?? throw new InvalidOperationException("Toolbar image is not attached to its button.");
                if (productIcon.Source is null || button.Bounds.Width < 44 || button.Bounds.Height < 44
                    || productIcon.Bounds.Width < productIcon.Width || productIcon.Bounds.Height < productIcon.Height
                    || imageOrigin.X < 2 || imageOrigin.Y < 2
                    || imageOrigin.X + productIcon.Bounds.Width > button.Bounds.Width - 2
                    || imageOrigin.Y + productIcon.Bounds.Height > button.Bounds.Height - 2)
                    throw new InvalidOperationException("A toolbar image or hit target is clipped.");
                continue;
            }
            var glyph = button.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>().Single();
            var ink = glyph.Data!.Bounds.Inflate(glyph.StrokeThickness / 2);
            var origin = glyph.TranslatePoint(default, button)
                ?? throw new InvalidOperationException("Toolbar glyph is not attached to its button.");
            if (button.Bounds.Width < 44 || button.Bounds.Height < 44
                || ink.Left < 0 || ink.Top < 0 || ink.Right > glyph.Width || ink.Bottom > glyph.Height
                || glyph.Bounds.Width < glyph.Width || glyph.Bounds.Height < glyph.Height
                || origin.X + ink.Left < 2 || origin.Y + ink.Top < 2
                || origin.X + ink.Right > button.Bounds.Width - 2 || origin.Y + ink.Bottom > button.Bounds.Height - 2)
                throw new InvalidOperationException("A toolbar icon or hit target is clipped.");
        }
        VerifyContextMenuLocalization(AnimationDropZone, ViewModel.Text.ImportAnimationsMenu);
        VerifyContextMenuLocalization(LayerDropZone, ViewModel.Text.ImportLayersMenu);
        VerifyContextMenuLocalization(PartDropZone, ViewModel.Text.ImportPartsMenu);
        foreach (var preview in this.GetVisualDescendants().OfType<CastPreviewView>())
            preview.VerifyContextMenuLocalization();
    }

    private static void VerifyContextMenuLocalization(Control target, string expected)
    {
        var menu = target.ContextMenu ?? throw new InvalidOperationException("A workspace context menu is missing.");
        menu.Open(target);
        var item = menu.Items.OfType<MenuItem>().Single();
        var actual = item.Header?.ToString();
        menu.Close();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"A workspace context menu is not localized: '{actual}'.");
    }

    private void NewProjectClick(object? sender, RoutedEventArgs e) => ViewModel.NewProject();
    private async void OpenProjectClick(object? sender, RoutedEventArgs e) => await ViewModel.OpenProjectAsync();
    private async void SaveProjectClick(object? sender, RoutedEventArgs e) => await ViewModel.SaveProjectAsync(false);
    private async void SaveProjectAsClick(object? sender, RoutedEventArgs e) => await ViewModel.SaveProjectAsync(true);
    private async void ExportClick(object? sender, RoutedEventArgs e) => await ViewModel.ExportAsync();
    private async void BuildPreviewClick(object? sender, RoutedEventArgs e) => await ViewModel.BuildPreviewAsync();
    private async void OpenPreviewClick(object? sender, RoutedEventArgs e) => await ViewModel.OpenPreviewAsync();
    private async void AddAnimationClick(object? sender, RoutedEventArgs e) => await ViewModel.AddAnimationsAsync();
    private async void AddPartClick(object? sender, RoutedEventArgs e) => await ViewModel.AddPartsAsync();
    private async void AddLayerClick(object? sender, RoutedEventArgs e) => await ViewModel.AddLayersAsync();
    private void RemoveAnimationClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveSelectedAnimation();
    private void GenerateSprintBatchClick(object? sender, RoutedEventArgs e) => ViewModel.GenerateSprintBatch();
    private void RemovePartClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveSelectedPart();
    private void RemoveLayerClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveSelectedLayer();
    private void MovePartUpClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedPart(-1);
    private void MovePartDownClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedPart(1);
    private void MoveLayerUpClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedLayer(-1);
    private void MoveLayerDownClick(object? sender, RoutedEventArgs e) => ViewModel.MoveSelectedLayer(1);
    private async void BrowseAnimationClick(object? sender, RoutedEventArgs e) { if (ViewModel.SelectedAnimation is { } item) await ViewModel.ReplaceAnimationSourceAsync(item); }
    private async void BrowsePartClick(object? sender, RoutedEventArgs e) { if (ViewModel.SelectedPart is { } item) await ViewModel.ReplacePartSourceAsync(item); }
    private async void BrowseLayerClick(object? sender, RoutedEventArgs e) { if (ViewModel.SelectedLayer is { } item) await ViewModel.ReplaceLayerSourceAsync(item); }
    private async void BrowseLeftPoseClick(object? sender, RoutedEventArgs e) { if (ViewModel.SelectedAnimation is { } item) await ViewModel.SetPoseAsync(item, true); }
    private async void BrowseRightPoseClick(object? sender, RoutedEventArgs e) { if (ViewModel.SelectedAnimation is { } item) await ViewModel.SetPoseAsync(item, false); }
    private async void BrowseOutputFolderClick(object? sender, RoutedEventArgs e) { if (ViewModel.SelectedAnimation is { } item) await ViewModel.SetOutputFolderAsync(item); }
    private void AnimationsPageClick(object? sender, RoutedEventArgs e) => ViewModel.SelectPage(WorkspacePage.Animations);
    private void DualPageClick(object? sender, RoutedEventArgs e) => ViewModel.SelectPage(WorkspacePage.DualAnimations);
    private void PartsPageClick(object? sender, RoutedEventArgs e) => ViewModel.SelectPage(WorkspacePage.ModelParts);
    private void SettingsPageClick(object? sender, RoutedEventArgs e) => ViewModel.SelectPage(WorkspacePage.Settings);
    private void AboutPageClick(object? sender, RoutedEventArgs e) => ViewModel.SelectPage(WorkspacePage.About);
    private void LanguageClick(object? sender, RoutedEventArgs e) => ViewModel.ToggleLanguage();
    private void SystemLanguageClick(object? sender, RoutedEventArgs e) => ViewModel.UseSystemLanguage();
    private void SaveDefaultsClick(object? sender, RoutedEventArgs e) => ViewModel.SaveDefaults();
    private void CloseDialogClick(object? sender, RoutedEventArgs e) => ViewModel.CloseDialog();
    private async void OpenUpstreamClick(object? sender, RoutedEventArgs e) => await ViewModel.OpenUpstreamAsync();
    private async void OpenProjectRepositoryClick(object? sender, RoutedEventArgs e) => await ViewModel.OpenProjectRepositoryAsync();

    private void DropZoneDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private void PathDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private void AnimationDrop(object? sender, DragEventArgs e)
    {
        var paths = GetDroppedPaths(e);
        if (paths.Count == 1 && string.Equals(Path.GetExtension(paths[0]), ".aprj", StringComparison.OrdinalIgnoreCase))
            ViewModel.LoadProject(paths[0]);
        else
            ViewModel.AddAnimationPaths(paths);
        e.DragEffects = DragDropEffects.Copy;
    }

    private async void PartDrop(object? sender, DragEventArgs e)
    {
        await ViewModel.AddPartPathsAsync(GetDroppedPaths(e));
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void LayerDrop(object? sender, DragEventArgs e)
    {
        // This dedicated target intentionally wins over the surrounding animation page.
        ViewModel.AddLayerPaths(GetDroppedPaths(e));
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void AnimationPathDrop(object? sender, DragEventArgs e) => SetDroppedPath(ViewModel.SelectedAnimation, "animation", e);
    private void LeftPosePathDrop(object? sender, DragEventArgs e) => SetDroppedPath(ViewModel.SelectedAnimation, "leftPose", e);
    private void RightPosePathDrop(object? sender, DragEventArgs e) => SetDroppedPath(ViewModel.SelectedAnimation, "rightPose", e);
    private void OutputPathDrop(object? sender, DragEventArgs e) => SetDroppedPath(ViewModel.SelectedAnimation, "output", e);
    private async void PartPathDrop(object? sender, DragEventArgs e)
    {
        var path = GetDroppedPaths(e).FirstOrDefault();
        if (ViewModel.SelectedPart is { } part && path is not null)
        {
            await ViewModel.SetPartPathFromDropAsync(part, path);
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void PartPathLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPart is { } part)
            await ViewModel.ReclassifyPartAsync(part);
    }
    private void LayerPathDrop(object? sender, DragEventArgs e) => SetDroppedPath(ViewModel.SelectedLayer, "layer", e);

    private void SetDroppedPath(object? target, string role, DragEventArgs e)
    {
        var path = GetDroppedPaths(e).FirstOrDefault();
        if (target is not null && path is not null)
        {
            ViewModel.SetPathFromDrop(target, path, role);
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsDialogOpen)
        {
            ViewModel.CloseDialog();
            e.Handled = true;
            return;
        }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        switch (e.Key)
        {
            case Key.O: await ViewModel.OpenProjectAsync(); e.Handled = true; break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift): await ViewModel.SaveProjectAsync(true); e.Handled = true; break;
            case Key.S: await ViewModel.SaveProjectAsync(false); e.Handled = true; break;
            case Key.E: await ViewModel.ExportAsync(); e.Handled = true; break;
        }
    }

    private void DialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.CloseDialog();
            e.Handled = true;
        }
    }

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Formats.Contains(DataFormat.File);

    private static IReadOnlyList<string> GetDroppedPaths(DragEventArgs e) => e.DataTransfer.TryGetFiles()?
        .Select(item => item.Path.LocalPath)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .ToArray() ?? [];
}
