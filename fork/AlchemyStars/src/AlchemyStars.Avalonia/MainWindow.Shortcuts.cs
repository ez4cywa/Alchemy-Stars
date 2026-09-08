using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

public sealed partial class MainWindow
{
    private bool shortcutRunning;
    private void InitializeShortcuts()
    {
        void Bind(string gesture, Func<Task> action, Func<bool>? available = null) => KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse(gesture),
            Command = new ShortcutCommand(this, action, available),
        });
        Task Run(Action action) { action(); return Task.CompletedTask; }
        Bind("Ctrl+N", () => Run(ViewModel.NewProject));
        Bind("Ctrl+O", ViewModel.OpenProjectAsync);
        Bind("Ctrl+S", () => ViewModel.SaveProjectAsync(false));
        Bind("Ctrl+Shift+S", () => ViewModel.SaveProjectAsync(true));
        Bind("Ctrl+E", ViewModel.ExportAsync, () => ViewModel.IsAnimationsPage || ViewModel.IsDualPage);
        Bind("Ctrl+I", () => ViewModel.IsModelPartsPage ? ViewModel.AddPartsAsync() : ViewModel.AddAnimationsAsync(),
            () => ViewModel.IsAnimationsPage || ViewModel.IsModelPartsPage);
        Bind("Ctrl+L", ViewModel.AddLayersAsync, () => ViewModel.IsAnimationsPage && ViewModel.HasSelectedAnimation);
        Bind("Ctrl+T", () => Run(ViewModel.AddDualTask), () => ViewModel.IsDualPage);
        Bind("F5", () => ViewModel.IsDualPage ? ViewModel.ProcessDualAsync(true) : ViewModel.BuildPreviewAsync(),
            () => ViewModel.IsDualPage ? ViewModel.HasSelectedDual : ViewModel.IsAnimationsPage && ViewModel.HasSelectedAnimation);
        Bind("Ctrl+Shift+O", ViewModel.OpenPreviewAsync, () => ViewModel.IsAnimationsPage || ViewModel.IsModelPartsPage || ViewModel.IsDualPage);
        Bind("Ctrl+D1", () => Run(() => ViewModel.SelectPage(WorkspacePage.Animations)));
        Bind("Ctrl+D2", () => Run(() => ViewModel.SelectPage(WorkspacePage.ModelParts)));
        Bind("Ctrl+D3", () => Run(() => ViewModel.SelectPage(WorkspacePage.DualAnimations)));
        Bind("Ctrl+D4", () => Run(() => ViewModel.SelectPage(WorkspacePage.Settings)));
        Bind("Ctrl+D5", () => Run(() => ViewModel.SelectPage(WorkspacePage.About)));
        Bind("F1", () => Run(ViewModel.ShowShortcuts));
    }

    internal bool HandleListShortcut(Key key, KeyModifiers modifiers, Visual? source)
    {
        if (shortcutRunning || ViewModel.IsBusy || ViewModel.IsDialogOpen || source is null) return false;
        var ancestors = source.GetVisualAncestors().Prepend(source).ToArray();
        if (ancestors.Any(item => item is TextBox)) return false;
        var list = ancestors.OfType<ListBox>().FirstOrDefault();
        if (list is null || !list.IsEffectivelyVisible) return false;
        var parts = ReferenceEquals(list.ItemsSource, ViewModel.Parts);
        var layers = ReferenceEquals(list.ItemsSource, ViewModel.Timeline.LayerTracks);
        if (key == Key.Delete && modifiers == KeyModifiers.None)
        {
            if (parts) ViewModel.RemoveSelectedPart();
            else if (layers) ViewModel.RemoveSelectedLayer();
            else if (ReferenceEquals(list.ItemsSource, ViewModel.Animations)) ViewModel.RemoveSelectedAnimation();
            else if (ReferenceEquals(list.ItemsSource, ViewModel.DualAnimations)) ViewModel.RemoveDualTask();
            else return false;
            return true;
        }
        if (modifiers == KeyModifiers.Alt && key is Key.Up or Key.Down)
        {
            var delta = key == Key.Up ? -1 : 1;
            if (parts) ViewModel.MoveSelectedPart(delta);
            else if (layers) ViewModel.MoveSelectedLayer(delta);
            else return false;
            return true;
        }
        return false;
    }

    private void ShortcutsClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ViewModel.ShowShortcuts();

    private sealed class ShortcutCommand(MainWindow owner, Func<Task> action, Func<bool>? available) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => !owner.shortcutRunning && !owner.ViewModel.IsBusy
            && !owner.ViewModel.IsDialogOpen && (available?.Invoke() ?? true);
        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            owner.shortcutRunning = true;
            try { await action(); }
            catch (Exception error) { owner.ViewModel.ReportShortcutError(error); }
            finally { owner.shortcutRunning = false; }
        }
    }
}

public sealed partial class MainWindowViewModel
{
    public void ShowShortcuts() => ShowDialog(Text.Shortcuts, Text.ShortcutsHelp, false);
    internal void ReportShortcutError(Exception error) => ShowDialog(Text.Shortcuts, error.Message, true);
}

public sealed partial class UiText
{
    public string Shortcuts => L("快捷键 · F1", "Keyboard shortcuts · F1");
    public string ShortcutsHelp => L(
        "项目\nCtrl+N  新建项目\nCtrl+O  打开项目\nCtrl+S  保存\nCtrl+Shift+S  另存为\nCtrl+E  导出（动画/双持页）\n\n导入与预览\nCtrl+I  导入动画或模型（按当前页面）\nCtrl+L  为选中动画导入动画层\nCtrl+T  新增双持任务（双持页）\nF5  合成选中动画/双持任务的预览\nCtrl+Shift+O  打开 CAST 预览\n\n页面\nCtrl+1  动画合成\nCtrl+2  模型部件\nCtrl+3  双持合成\nCtrl+4  设置\nCtrl+5  关于\n\n列表获得焦点时\nDelete  移除选中条目（不删除源文件）\nAlt+↑ / ↓  上移/下移模型或动画层\n\n预览画面获得焦点时\nSpace  播放/暂停\n← ↑ → ↓  旋转视角\nF / Shift+F  适应主体/全部\n1  第一人称/环绕视角\n+ / −  缩放\n[ / ]  上一帧/下一帧\nB  显示/隐藏骨骼\n\nF1  本速查表 · Esc  关闭提示\nTab / Shift+Tab  切换控件；输入框保留文字编辑按键。执行任务或显示提示时暂停其他快捷操作。",
        "Project\nCtrl+N  New project\nCtrl+O  Open project\nCtrl+S  Save\nCtrl+Shift+S  Save as\nCtrl+E  Export (animation/dual page)\n\nImport and preview\nCtrl+I  Import animations or models on the active page\nCtrl+L  Import layers into the selected animation\nCtrl+T  New dual task (dual page)\nF5  Build selected animation/dual preview\nCtrl+Shift+O  Open CAST preview\n\nPages\nCtrl+1  Animation blend\nCtrl+2  Model parts\nCtrl+3  Dual merge\nCtrl+4  Settings\nCtrl+5  About\n\nWith a list focused\nDelete  Remove selected item (keeps source files)\nAlt+↑ / ↓  Move model or layer up/down\n\nWith the preview viewport focused\nSpace  Play/pause\n← ↑ → ↓  Orbit camera\nF / Shift+F  Fit subject/all\n1  First-person/orbit camera\n+ / −  Zoom\n[ / ]  Previous/next frame\nB  Show/hide bones\n\nF1  This reference · Esc  Close message\nTab / Shift+Tab  Focus next/previous control. Text fields retain editing keys. Other shortcuts pause during tasks and messages.");
    public string NewProjectShortcut => NewProject + " · Ctrl+N";
    public string OpenProjectShortcut => OpenProject + " · Ctrl+O";
    public string SaveProjectShortcut => SaveProject + " · Ctrl+S";
    public string SaveProjectAsShortcut => SaveProjectAs + " · Ctrl+Shift+S";
    public string AddAnimationShortcut => AddAnimation + " · Ctrl+I";
    public string AddPartShortcut => AddPart + " · Ctrl+I";
    public string AddLayerShortcut => AddLayer + " · Ctrl+L";
    public string BuildPreviewShortcut => BuildPreview + " · F5";
    public string OpenPreviewShortcut => OpenPreview + " · Ctrl+Shift+O";
}
