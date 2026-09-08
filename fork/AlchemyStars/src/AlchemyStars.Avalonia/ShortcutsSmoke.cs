using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class ShortcutsSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        void Press(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            control.Focus();
            var input = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
            // Evaluate the actual public KeyBindings, then route unconsumed keys to focused controls.
            // Direct RaiseEvent alone bypasses the platform keyboard device's KeyBinding dispatch.
            foreach (var binding in window.KeyBindings)
                if (binding.Gesture?.Matches(input) == true && binding.Command?.CanExecute(binding.CommandParameter) == true)
                {
                    binding.Command.Execute(binding.CommandParameter);
                    input.Handled = true;
                    break;
                }
            control.RaiseEvent(input);
        }
        Press(window, Key.D2, KeyModifiers.Control);
        Require(vm.IsModelPartsPage, "Ctrl+2 did not navigate through the key binding.");
        vm.AddPartPaths(["shortcuts-first.cast", "shortcuts-second.cast"]);
        await Task.Delay(80);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(item => ReferenceEquals(item.ItemsSource, vm.Parts));
        var second = vm.SelectedPart;
        Press(list, Key.Up, KeyModifiers.Alt);
        Require(ReferenceEquals(vm.Parts[0], second), "Alt+Up did not reorder the focused model list.");
        Require(ReferenceEquals(vm.SelectedPart, second), "Reordering lost the list selection.");
        var textBox = window.GetVisualDescendants().OfType<TextBox>().First(item => item.IsEffectivelyVisible);
        var count = vm.Parts.Count;
        Press(textBox, Key.Delete);
        Require(vm.Parts.Count == count, "Text editing deleted a model item.");
        Press(list, Key.Delete);
        Require(vm.Parts.Count == count - 1, "Delete did not remove the focused list selection.");
        Press(window, Key.F1);
        Require(vm.IsDialogOpen && vm.DialogMessage.Contains("Ctrl+Shift+S"), "F1 did not open the shortcut reference.");
        Press(window, Key.N, KeyModifiers.Control);
        Require(vm.Parts.Count == count - 1, "A project shortcut ran through the dialog.");
        Press(window, Key.Escape);
        Require(!vm.IsDialogOpen, "Escape did not dismiss help.");
        Press(window, Key.N, KeyModifiers.Control | KeyModifiers.Alt);
        Require(vm.Parts.Count == count - 1, "Extra modifiers incorrectly matched Ctrl+N.");
        Press(window, Key.N, KeyModifiers.Control);
        Require(vm.Parts.Count == 0, "Ctrl+N did not create a new project.");
        Press(window, Key.D3, KeyModifiers.Control);
        Press(window, Key.T, KeyModifiers.Control);
        Require(vm.DualAnimations.Count == 1, "Ctrl+T did not add a dual task.");
        Press(window, Key.D1, KeyModifiers.Control);
        await Task.Delay(80);
        var preview = window.GetVisualDescendants().OfType<CastPreviewView>().First(item => item.IsEffectivelyVisible);
        var viewport = preview.FindControl<Control>("Viewport")!;
        var firstPerson = vm.Preview.IsFirstPerson;
        Press(viewport, Key.D2, KeyModifiers.Control);
        Require(vm.IsModelPartsPage && vm.Preview.IsFirstPerson == firstPerson, "Modified viewport keys did not reach global shortcuts.");
        Press(window, Key.D1, KeyModifiers.Control);
        Press(viewport, Key.D1);
        Require(vm.Preview.IsFirstPerson != firstPerson, "Viewport camera shortcut failed.");
        Press(viewport, Key.D1, KeyModifiers.Control);
        Require(vm.Preview.IsFirstPerson != firstPerson, "Ctrl+1 also changed the viewport camera.");
        Press(window, Key.F1);
        Console.WriteLine("Keyboard shortcuts: registered commands, navigation, focused delete/reorder, text editing, exact modifiers, dialog guard, camera and help PASS");
        await Task.Delay(80);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
