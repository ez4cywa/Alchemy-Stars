using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AlchemyStars.Avalonia;

public partial class DualWieldView : UserControl
{
    public DualWieldView() => InitializeComponent();
    internal Grid EditorGrid => DualEditor;
    internal void VerifySourceSelection()
    {
        if (LeftSourcePicker.SelectedIndex != ViewModel.DualLeftIndex || RightSourcePicker.SelectedIndex != ViewModel.DualRightIndex)
            throw new InvalidOperationException($"Dual source selectors mismatch: UI {LeftSourcePicker.SelectedIndex}/{RightSourcePicker.SelectedIndex}, VM {ViewModel.DualLeftIndex}/{ViewModel.DualRightIndex}, counts {LeftSourcePicker.ItemCount}/{RightSourcePicker.ItemCount}.");
        if (ViewModel.DualLeftIndex >= 0 && LeftSourcePicker.SelectedItem is not WorkspaceAnimation)
            throw new InvalidOperationException("Dual source selector has no visible label.");
    }
    internal void VerifySourceInteraction()
    {
        VerifySourceSelection();
        var selected = ViewModel.SelectedDual;
        if (selected is not null)
        {
            var mode = selected.ModeIndex;
            DualModePicker.SelectedIndex = 1 - mode;
            if (selected.ModeIndex != 1 - mode) throw new InvalidOperationException("Dual mode selector did not update task.");
            DualModePicker.SelectedIndex = mode;
            var original = selected.ExportWeaponModels;
            ExportModelsSwitch.IsChecked = !original;
            if (selected.ExportWeaponModels == original) throw new InvalidOperationException("Model export switch did not update the task.");
            ExportModelsSwitch.IsChecked = original;
        }
        if (selected is null || ViewModel.Animations.Count < 3) return;
        var originalIndex = LeftSourcePicker.SelectedIndex;
        var replacement = (originalIndex + 2) % ViewModel.Animations.Count;
        LeftSourcePicker.SelectedIndex = replacement;
        if (selected.LeftAnimationId != ViewModel.Animations[replacement].Id)
            throw new InvalidOperationException("Selecting a dual source did not update the task reference.");
        LeftSourcePicker.SelectedIndex = originalIndex;
        if (ViewModel.DualAnimations.Count > 1)
        {
            ViewModel.SelectedDual = ViewModel.DualAnimations.First(t => t != selected);
            VerifySourceSelection();
            ViewModel.SelectedDual = selected;
        }
        VerifySourceSelection();
    }
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private void AddClick(object? sender, RoutedEventArgs e) => ViewModel.AddDualTask();
    private void PairClick(object? sender, RoutedEventArgs e) => ViewModel.PairDualTasks();
    private void RemoveClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveDualTask();
    private async void PreviewClick(object? sender, RoutedEventArgs e) => await ViewModel.ProcessDualAsync(true);
    private async void ExportClick(object? sender, RoutedEventArgs e) => await ViewModel.ProcessDualAsync(false);
    private async void ExportAllClick(object? sender, RoutedEventArgs e) => await ViewModel.ProcessDualAsync(false, true);
    private async void FolderClick(object? sender, RoutedEventArgs e) => await ViewModel.SetDualOutputAsync();
    private async void AllFolderClick(object? sender, RoutedEventArgs e) => await ViewModel.SetDualOutputAsync(true);
    private void EditLeftClick(object? sender, RoutedEventArgs e) => Edit(ViewModel.DualLeftSource);
    private void EditRightClick(object? sender, RoutedEventArgs e) => Edit(ViewModel.DualRightSource);
    private void Edit(WorkspaceAnimation? source)
    {
        if (source is null) return;
        ViewModel.SelectedAnimation = source; ViewModel.SelectPage(WorkspacePage.Animations);
    }
}
