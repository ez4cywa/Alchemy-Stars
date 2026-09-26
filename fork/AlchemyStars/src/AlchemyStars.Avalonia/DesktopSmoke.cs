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
        // Must match the sidebar button order exactly; the loop clicks by index.
        var pages = new[] { WorkspacePage.Animations, WorkspacePage.ModelParts, WorkspacePage.ModelMerger, WorkspacePage.DualAnimations, WorkspacePage.CodWeaponDb };
        try
        {
            // Reproduce scrolling away from the inspector before selecting a real project layer.
            var animationWithLayer = vm.Animations.FirstOrDefault(a => a.Layers.Count > 0);
            if (animationWithLayer is not null)
            {
                var previousAnimation = vm.SelectedAnimation;
                var previousLayer = vm.SelectedLayer;
                vm.SelectPage(WorkspacePage.Animations);
                vm.SelectedAnimation = animationWithLayer;
                var section = window.FindControl<Expander>("SelectedLayerSection")!;
                var scroll = section.GetVisualAncestors().OfType<ScrollViewer>().First();
                var groups = scroll.GetVisualDescendants().OfType<Expander>().ToArray();
                var expanded = groups.Select(g => g.IsExpanded).ToArray();
                try
                {
                    foreach (var width in new[] { 900, 1460 })
                    {
                        window.Width = width;
                        window.Height = width == 900 ? 600 : 900;
                        vm.SelectedLayer = null;
                        foreach (var group in groups) group.IsExpanded = true;
                        await Task.Delay(100);
                        scroll.Offset = new Vector(0, scroll.Extent.Height);
                        await Task.Delay(60);
                        vm.SelectedLayer = animationWithLayer.Layers[0];
                        await Task.Delay(180);
                        var top = section.TranslatePoint(default, scroll)!.Value.Y;
                        Require(top >= -1 && top + section.Bounds.Height <= scroll.Bounds.Height + 1,
                            "Selected layer properties remain outside the inspector viewport.");
                        foreach (var group in groups.Where(g => g.Header is StackPanel))
                        {
                            Require(ControlAutomationPeer.CreatePeerForElement(group)!.GetName() is { Length: > 0 } label
                                && !label.Contains("Avalonia.Controls"), "Inspector group has no semantic accessible name.");
                            var header = group.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.ToggleButton>()
                                .First(b => b.Name == "ExpanderHeader");
                            Require(ControlAutomationPeer.CreatePeerForElement(header)!.GetName()
                                == ControlAutomationPeer.CreatePeerForElement(group)!.GetName(),
                                "Inspector header button did not inherit its accessible name.");
                        }
                        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, (int)window.Height));
                        bitmap.Render(window);
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(Program.RenderSmokePath!)!, $"layer-visible-{width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                }
                finally
                {
                    for (var i = 0; i < groups.Length; i++) groups[i].IsExpanded = expanded[i];
                    vm.SelectedAnimation = previousAnimation;
                    vm.SelectedLayer = previousLayer;
                }
            }
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
                // Every built-in theme draws its own checkbox and names the parts
                // differently, so resolve the indicator instead of assuming the
                // default theme's `CheckIndicator`.
                var indicator = checkBox.GetVisualDescendants().OfType<Border>()
                    .FirstOrDefault(b => b.Name is "CheckIndicator" or "AppleCheckBox" or "GtkCheckBox" or "Win2000Check" or "XpCheckBox")
                    ?? throw new InvalidOperationException($"The first toggleable checkbox ('{checkBox.Name}' / '{checkBox.Content}') exposes no check indicator part.");
                var content = checkBox.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "PART_ContentPresenter");
                var indicatorCenter = indicator.TranslatePoint(new Point(0, indicator.Bounds.Height / 2), checkBox)!.Value.Y;
                var contentCenter = content.TranslatePoint(new Point(0, content.Bounds.Height / 2), checkBox)!.Value.Y;
                Require(Math.Abs(indicatorCenter - contentCenter) < 1, "Checkbox label and indicator are misaligned.");
                checkBox.Focus(NavigationMethod.Tab);
                await Task.Delay(40);
                Require(checkBox.IsFocused, "The checkbox did not accept keyboard focus.");
                // Only the default theme draws a dedicated focus ring; the older
                // themes re-tint the indicator, which is not colour-comparable.
                if (checkBox.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "CheckFocus") is { } focus)
                    Require(focus.BorderBrush is ISolidColorBrush focusBrush && focusBrush.Color.A > 0, "Checkbox keyboard focus is invisible.");
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
                // App menu, minimize, zoom, close — the integrated app menu sits left of minimize.
                Require(controls.Length == 4, "Window controls are missing.");
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(controls[2])!).Invoke();
                await Task.Delay(80);
                Require(window.WindowState == WindowState.Maximized, "Window zoom did not maximize.");
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(controls[2])!).Invoke();
                await Task.Delay(80);
                Require(window.WindowState == WindowState.Normal, "Window zoom did not restore.");
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(controls[1])!).Invoke();
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
