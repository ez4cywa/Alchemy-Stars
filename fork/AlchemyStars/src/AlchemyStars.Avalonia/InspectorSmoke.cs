using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class InspectorSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        vm.NewProject();
        vm.AddAnimationPaths([@"C:\Assets\animations\vm_pi_golf21_idle.cast"]);
        vm.AddLayerPaths([@"C:\Assets\animations\vm_pi_golf21_sprint_loop.cast", @"C:\Assets\animations\vm_pi_golf21_sprint_offset.cast"]);
        vm.AddPartPaths([@"C:\Assets\models\viewhands.cast"]);
        vm.AddDualTask();
        var animation = vm.SelectedAnimation!;
        Require(animation.WeaponFollowMode == 0, "New animations must leave weapon follow disabled.");
        var layer = vm.SelectedLayer!;
        var part = vm.SelectedPart!;
        var dual = vm.SelectedDual!;
        layer.Offset = -15;
        layer.Type = AnimationLayerKind.GesturePose;
        part.Type = ModelPartKind.Weapon;
        animation.WeaponFollowMode = 2;
        dual.ModeIndex = 1;
        var editor = window.FindControl<Grid>("AnimationEditor")!;

        foreach (var style in new[] { 0, 1, 2, 3 })
        foreach (var chinese in new[] { true, false, true })
        {
            vm.ThemeStyleIndex = style;
            if (vm.IsChinese != chinese) vm.ToggleLanguage();
            await Task.Delay(80);
            Require(layer.Type == AnimationLayerKind.GesturePose && part.Type == ModelPartKind.Weapon
                && animation.WeaponFollowMode == 2 && dual.ModeIndex == 1, "Language/theme switching changed project modes.");
            foreach (var page in new[] { WorkspacePage.Animations, WorkspacePage.ModelParts, WorkspacePage.DualAnimations })
            {
                vm.SelectPage(page);
                foreach (var section in window.GetVisualDescendants().OfType<Expander>().Where(e => e.IsEffectivelyVisible)) section.IsExpanded = true;
                await Task.Delay(70);
                if (page == WorkspacePage.Animations)
                {
                    VerifyChoice(window, vm.Text.LayerMode, 3, vm.Text.LayerTypes[3]);
                    VerifyChoice(window, vm.Text.WeaponFollow, 2, vm.Text.WeaponFollowModes[2]);
                }
                else if (page == WorkspacePage.ModelParts) VerifyChoice(window, vm.Text.PartType, 1, vm.Text.PartTypes[1]);
                else VerifyChoice(window, vm.Text.DualMode, 1, vm.Text.DualModes[1]);
                foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Classes.Contains("inspector-scroll") && s.IsEffectivelyVisible)) VerifyBounds(scroll);
            }
        }
        vm.SelectPage(WorkspacePage.Animations);
        var mode = FindChoice(window, vm.Text.LayerMode);
        mode.SelectedIndex = 2;
        Require(layer.Type == AnimationLayerKind.Gesture, "Choosing a mode did not update the layer.");
        layer.Type = AnimationLayerKind.GesturePose;
        await Task.Delay(60);
        VerifyChoice(window, vm.Text.LayerMode, 3, vm.Text.LayerTypes[3]);
        vm.SelectedLayer = animation.Layers[0];
        await Task.Delay(60);
        VerifyChoice(window, vm.Text.LayerMode, 1, vm.Text.LayerTypes[1]);
        vm.SelectedLayer = null;
        vm.SelectedLayer = layer;
        await Task.Delay(60);
        VerifyChoice(window, vm.Text.LayerMode, 3, vm.Text.LayerTypes[3]);
        Require(layer.Offset == -15, "Layout/language changes altered the frame offset.");

        foreach (var size in new[] { (900, 600, 280), (1460, 900, 280), (1460, 900, 320), (1460, 900, 460) })
        {
            window.Width = size.Item1;
            window.Height = size.Item2;
            editor.ColumnDefinitions[4].Width = new GridLength(size.Item3);
            await Task.Delay(80);
            VerifyBounds(mode.GetVisualAncestors().OfType<ScrollViewer>().First());
        }
        editor.ColumnDefinitions[4].Width = new GridLength(320);
        await Task.Delay(80);
        Console.WriteLine("Inspector: three themes, Chinese/English, four mode selectors, unchanged project values, layer switching, UI/model synchronization and 280–460 DIP panels PASS");
    }

    private static ComboBox FindChoice(MainWindow window, string name) => window.GetVisualDescendants().OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == name);

    private static void VerifyChoice(MainWindow window, string name, int index, string label)
    {
        var combo = FindChoice(window, name);
        Require(combo.SelectedIndex == index && combo.SelectedItem?.ToString() == label, $"Blank/wrong {name}: index={combo.SelectedIndex}, item={combo.SelectedItem}, expected={label}");
        Require(combo.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == label && text.IsEffectivelyVisible), $"Selected label is not rendered: {name} / {label}");
    }

    private static void VerifyBounds(ScrollViewer scroll)
    {
        var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        var scrollbar = scroll.GetVisualDescendants().OfType<ScrollBar>().First(bar => bar.Orientation == Orientation.Vertical && ReferenceEquals(bar.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(), scroll));
        var right = scrollbar.IsVisible ? Math.Min(presenter.Bounds.Width, scrollbar.TranslatePoint(default, presenter)!.Value.X) : presenter.Bounds.Width;
        foreach (var control in presenter.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is TextBox or ComboBox or Button && !c.GetVisualAncestors().OfType<ScrollBar>().Any()))
        {
            var origin = control.TranslatePoint(default, presenter)!.Value;
            Require(origin.X >= -1 && origin.X + control.Bounds.Width <= right + 0.5,
                $"Scrollbar covers {control.GetType().Name} {AutomationProperties.GetName(control)}: x={origin.X}, width={control.Bounds.Width}, clear width={right}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
