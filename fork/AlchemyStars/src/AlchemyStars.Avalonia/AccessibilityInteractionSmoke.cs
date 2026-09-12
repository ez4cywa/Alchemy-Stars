using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class AccessibilityInteractionSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        vm.CloseDialog();
        vm.SelectPage(WorkspacePage.Animations);
        await Task.Delay(60);
        var origin = window.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("activity"));
        var shell = window.FindControl<Grid>("ShellGrid")!;
        var overlay = window.FindControl<Grid>("DialogOverlay")!;
        var close = window.FindControl<Button>("DialogCloseButton")!;
        var reader = window.FindControl<ScrollViewer>("DialogMessageView")!;
        Require(origin.Focus(NavigationMethod.Tab), "The workspace could not receive keyboard focus.");
        vm.ShowShortcuts();
        await Task.Delay(80);
        Require(close.IsFocused && !shell.IsEffectivelyEnabled, "Modal focus/background isolation failed.");
        Require(!origin.Focus(), "A background control stole modal focus.");
        Require(AutomationProperties.GetHelpText(close) == vm.DialogAccessibleDescription
            && AutomationProperties.GetHelpText(reader) == vm.Text.MessageScrollHelp, "Dialog text/help is not accessible.");
        foreach (var direction in new[] { NavigationDirection.Next, NavigationDirection.Previous })
            for (var index = 0; index < 8; index++)
            {
                window.FocusManager!.TryMoveFocus(direction);
                Require(window.FocusManager.GetFocusedElement() is Control focused && overlay.IsVisualAncestorOf(focused),
                    "Tab/Shift+Tab escaped the dialog.");
            }
        Require(reader.Focus(NavigationMethod.Tab) && reader.FocusAdorner is not null, "Long messages have no keyboard focus indicator.");
        reader.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
        Require(reader.Offset.Y > 0, "Page Down did not scroll the long message.");
        if (Program.RenderSmokePath is { } imagePath)
        {
            var mode = vm.ThemeModeIndex;
            try
            {
                foreach (var theme in new[] { 0, 1 })
                {
                    vm.ThemeModeIndex = theme;
                    await Task.Delay(80);
                    reader.Focus(NavigationMethod.Tab);
                    using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
                        new global::Avalonia.PixelSize((int)window.Width, (int)window.Height));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(imagePath)!, $"dialog-focus-{(vm.IsChinese ? "zh" : "en")}-{theme}.png"),
                        global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
            finally { vm.ThemeModeIndex = mode; }
        }
        reader.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await Task.Delay(80);
        Require(!vm.IsDialogOpen && shell.IsEffectivelyEnabled && origin.IsFocused, "Escape did not restore workspace focus.");

        var file = Path.Combine(Path.GetTempPath(), "AlchemyStars-a11y-" + Guid.NewGuid().ToString("N") + ".cast");
        try
        {
            CastAxisSmoke.Write(file, "y");
            await vm.Preview.LoadAsync(file);
            await Task.Delay(60);
            var preview = window.GetVisualDescendants().OfType<CastPreviewView>().First(view => view.IsEffectivelyVisible);
            var toggle = preview.FindControl<ToggleButton>("BonesToggle")!;
            var viewport = preview.FindControl<Control>("Viewport")!;
            var gizmo = preview.FindControl<CameraAxisGizmo>("AxisGizmo")!;
            var peer = ControlAutomationPeer.CreatePeerForElement(toggle)!;
            var before = vm.Preview.ShowBones;
            ((IToggleProvider)peer).Toggle();
            Require(vm.Preview.ShowBones != before && toggle.IsChecked == vm.Preview.ShowBones, "Accessible bone toggle did not change state.");
            viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.B });
            Require(vm.Preview.ShowBones == before && toggle.IsChecked == before, "Keyboard bone state did not synchronize with UI Automation.");
            Require(peer.GetName() == vm.Text.ShowBones && AutomationProperties.GetName(viewport) == vm.Text.PreviewViewport
                && AutomationProperties.GetHelpText(viewport) == vm.Preview.InteractionHelp && viewport.FocusAdorner is not null,
                "Preview name/help/focus indicator is missing.");
            Require(gizmo.IsEffectivelyVisible && gizmo.Focus(NavigationMethod.Tab)
                && AutomationProperties.GetName(gizmo) == vm.Text.CameraAxisGizmo
                && AutomationProperties.GetHelpText(gizmo) == vm.Text.CameraAxisGizmoHelp,
                "Camera axis gizmo is inaccessible.");
            gizmo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.X });
            Require(MathF.Abs(vm.Preview.CameraYaw) < 1e-5f && MathF.Abs(vm.Preview.CameraPitch) < 1e-5f,
                "Camera axis gizmo keyboard view selection failed.");
        }
        finally { vm.Preview.Clear(); File.Delete(file); }

        var selectedExport = window.FindControl<Button>("ExportSelectedAnimationButton")!;
        var selected = new WorkspaceAnimation { Name = "a11y-selection.cast" };
        vm.Animations.Add(selected);
        vm.SelectedAnimation = selected;
        await Task.Delay(50);
        Require(selectedExport.IsEffectivelyEnabled && AutomationProperties.GetName(selectedExport) == vm.Text.ExportSelectedAnimation
            && AutomationProperties.GetAcceleratorKey(selectedExport) == "Ctrl+Shift+E", "Single export is not accessible.");
        var shortcut = window.KeyBindings.Single(binding => binding.Gesture?.Matches(new KeyEventArgs
            { Key = Key.E, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift }) == true);
        Require(shortcut.Command!.CanExecute(null), "Single-export shortcut is unavailable with a selection.");
        vm.SelectedAnimation = null;
        await Task.Delay(50);
        Require(!selectedExport.IsEffectivelyEnabled && !shortcut.Command.CanExecute(null), "Single export is enabled without a selection.");
        vm.Animations.Remove(selected);
        vm.SelectPage(WorkspacePage.ModelParts);
        await Task.Delay(50);
        var modelExport = window.FindControl<Button>("ExportBoundModelButton")!;
        var addedParts = new List<WorkspacePart>();
        if (!vm.Parts.Any(part => part.Type == ModelPartKind.ViewHands))
        {
            var hands = new WorkspacePart { FilePath = "a11y-hands.cast", Type = ModelPartKind.ViewHands };
            vm.Parts.Add(hands); addedParts.Add(hands);
        }
        if (!vm.Parts.Any(part => part.Type == ModelPartKind.Weapon))
        {
            var weapon = new WorkspacePart { FilePath = "a11y-weapon.cast", Type = ModelPartKind.Weapon };
            vm.Parts.Add(weapon); addedParts.Add(weapon);
        }
        vm.SelectedPart = vm.Parts.Last();
        await Task.Delay(50);
        Require(modelExport.IsEffectivelyEnabled && AutomationProperties.GetName(modelExport) == vm.Text.ExportBoundModel
            && AutomationProperties.GetHelpText(modelExport) == vm.Text.ExportBoundModelHelp,
            "Bound-model export is not accessible from the model-parts workspace.");
        foreach (var part in addedParts) vm.Parts.Remove(part);
        vm.SelectedPart = vm.Parts.FirstOrDefault();
        vm.SelectPage(WorkspacePage.Settings);
        await Task.Delay(80);
        var axisCombo = window.FindControl<ComboBox>("OutputUpAxisCombo")!;
        var originalAxis = vm.OutputUpAxisIndex;
        Require(axisCombo.IsEffectivelyVisible && axisCombo.Focus(NavigationMethod.Tab)
            && AutomationProperties.GetName(axisCombo) == vm.Text.OutputUpAxis
            && AutomationProperties.GetHelpText(axisCombo) == vm.Text.OutputUpAxisHelp, "Output axis control is inaccessible.");
        foreach (var index in new[] { 2, 1, 0 })
        {
            axisCombo.SelectedIndex = index;
            await Task.Delay(40);
            Require(vm.OutputUpAxisIndex == index && vm.Workspace.OutputUpAxis == (index switch { 1 => "z", 2 => "y", _ => "source" }),
                "Output axis selection did not update the export document.");
        }
        Require(axisCombo.Items[0]!.ToString() == vm.Text.KeepSceneAxis, "Keep-scene option is not localized.");
        vm.ToggleLanguage();
        await Task.Delay(50);
        Require(vm.OutputUpAxisIndex == 0 && axisCombo.SelectedIndex == 0 && axisCombo.Items[0]!.ToString() == vm.Text.KeepSceneAxis,
            "Changing language changed or lost the keep-scene selection.");
        vm.ToggleLanguage();
        await Task.Delay(50);
        vm.OutputUpAxisIndex = originalAxis;
        if (Program.RenderSmokePath is { } settingsImage)
        {
            using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
                new global::Avalonia.PixelSize((int)window.Width, (int)window.Height));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(Path.GetDirectoryName(settingsImage)!, $"axis-settings-{(vm.IsChinese ? "zh" : "en")}.png"),
                global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        vm.SelectPage(WorkspacePage.Animations);
        Console.WriteLine("UI accessibility: modal Tab cycle, Escape/focus restore, background isolation, long-message scroll, help, preview toggle/gizmo state, selected animation export and bound-model export PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
