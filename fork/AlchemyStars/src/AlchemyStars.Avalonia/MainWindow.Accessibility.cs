using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

public sealed partial class MainWindow
{
    private Control? workspaceFocus;
    private int overlayFocusRevision;

    private void InitializeAccessibility()
    {
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (DataContext is MainWindowViewModel { CanInteract: true }
                && e.Source is Control control && ShellGrid.IsVisualAncestorOf(control))
                workspaceFocus = control;
        }, RoutingStrategies.Bubble);
    }

    private void UpdateOverlayFocus()
    {
        var revision = ++overlayFocusRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision != overlayFocusRevision) return;
            if (ViewModel.IsDialogOpen) DialogCloseButton.Focus(NavigationMethod.Tab);
            else if (ViewModel.IsBusy) BusyStatus.Focus(NavigationMethod.Tab);
            else if (workspaceFocus is not { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous
                || !ShellGrid.IsVisualAncestorOf(previous) || !previous.Focus(NavigationMethod.Tab))
                ShellGrid.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(button => button.Classes.Contains("activity"))?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }
}

public sealed partial class UiText
{
    public string ExportSelectedAnimation => L("导出选中动画", "Export selected");
    public string ExportSelectedAnimationShortcut => ExportSelectedAnimation + " · Ctrl+Shift+E";
    public string MessageScrollHelp => L("使用方向键或 Page Up / Page Down 阅读长消息，按 Tab 返回关闭按钮。",
        "Use arrow keys or Page Up / Page Down to read a long message. Press Tab to return to Close.");
    public string PreviewViewport => L("动画预览画面", "Animation preview viewport");
}
