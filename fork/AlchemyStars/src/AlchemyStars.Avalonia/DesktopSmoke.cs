using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class DesktopSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        var originalSize = new Size(window.Width, window.Height);
        var originalPage = vm.SelectedPage;
        var originalWorkspace = vm.Workspace;
        var shell = window.FindControl<Grid>("ShellGrid")!;
        var navigation = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("activity")).ToArray();
        var pages = new[] { WorkspacePage.Animations, WorkspacePage.ModelParts, WorkspacePage.DualAnimations, WorkspacePage.Settings, WorkspacePage.About };
        try
        {
            foreach (var width in new[] { 900, 1460 })
            {
                window.Width = width;
                window.Height = width == 900 ? 600 : 900;
                await Task.Delay(100);
                Require(shell.ColumnDefinitions[0].Width.Value == (width == 900 ? 56 : 172), "Navigation did not adapt to window width.");
                for (var index = 0; index < pages.Length; index++)
                {
                    ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(navigation[index])!).Invoke();
                    await Task.Delay(60);
                    Require(vm.SelectedPage == pages[index], "Sidebar command opened the wrong page.");
                    Require(ReferenceEquals(originalWorkspace, vm.Workspace), "Navigation replaced the project.");
                    foreach (var icon in window.GetVisualDescendants().OfType<AppIcon>().Where(i => i.IsEffectivelyVisible))
                        Require(icon.HasSymbol && icon.Bounds.Width > 0 && icon.Bounds.Height > 0, $"Missing symbol: {icon.Kind}");
                    foreach (var button in navigation)
                        Require(button.Bounds.Width >= 44 && button.Bounds.Height >= 44, "Sidebar hit target is too small.");
                    window.VerifyToolbarLayout();
                }
            }

            vm.SelectPage(WorkspacePage.Settings);
            await Task.Delay(80);
            var checkBox = window.GetVisualDescendants().OfType<CheckBox>()
                .First(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
            var originalCheck = checkBox.IsChecked;
            try
            {
                ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(checkBox)!).Toggle();
                await Task.Delay(40);
                Require(checkBox.IsChecked != originalCheck, "The checkbox no longer supports its native toggle action.");
                var indicator = checkBox.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "CheckIndicator");
                var content = checkBox.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "PART_ContentPresenter");
                var indicatorCenter = indicator.TranslatePoint(new Point(0, indicator.Bounds.Height / 2), checkBox)!.Value.Y;
                var contentCenter = content.TranslatePoint(new Point(0, content.Bounds.Height / 2), checkBox)!.Value.Y;
                Require(Math.Abs(indicatorCenter - contentCenter) < 1, "Checkbox label and indicator are misaligned.");
                checkBox.Focus(NavigationMethod.Tab);
                await Task.Delay(40);
                var focus = checkBox.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "CheckFocus");
                Require(checkBox.IsFocused && focus.BorderBrush is ISolidColorBrush brush && brush.Color.A > 0, "Checkbox keyboard focus is invisible.");
            }
            finally { checkBox.IsChecked = originalCheck; }

            var toolbar = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("toolbar"));
            var originalTip = ToolTip.GetTip(toolbar);
            try
            {
                ToolTip.SetTip(toolbar, "C:\\Models\\" + new string('W', 180) + "\\animation.cast");
                ToolTip.SetIsOpen(toolbar, true);
                await Task.Delay(150);
                var tip = toolbar.GetValue(global::Avalonia.Controls.Diagnostics.ToolTipDiagnostics.ToolTipProperty) as ToolTip;
                Require(tip is not null && tip.Bounds.Width <= 421 && tip.Bounds.Height > 40, "Long tooltip did not wrap within its surface.");
            }
            finally { ToolTip.SetIsOpen(toolbar, false); ToolTip.SetTip(toolbar, originalTip); }

            // Exercise real window commands while fully transparent and outside the taskbar.
            var originalPosition = window.Position;
            var originalOpacity = window.Opacity;
            window.Opacity = 0;
            try
            {
                var controls = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("window-control")).ToArray();
                Require(controls.Length == 3, "Window controls are missing.");
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(controls[1])!).Invoke();
                await Task.Delay(80);
                Require(window.WindowState == WindowState.Maximized, "Window zoom did not maximize.");
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(controls[1])!).Invoke();
                await Task.Delay(80);
                Require(window.WindowState == WindowState.Normal, "Window zoom did not restore.");
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(controls[0])!).Invoke();
                await Task.Delay(80);
                Require(window.WindowState == WindowState.Minimized, "Window minimize command failed.");
            }
            finally
            {
                window.WindowState = WindowState.Normal;
                window.Position = originalPosition;
                window.Opacity = originalOpacity;
            }
            Console.WriteLine("Desktop smoke passed: responsive navigation, all page commands, symbols, checkbox interaction/alignment/focus, long tooltips and window controls.");
        }
        finally
        {
            window.Width = originalSize.Width;
            window.Height = originalSize.Height;
            vm.SelectPage(originalPage);
            await Task.Delay(100);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
