using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class TextMenuSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        vm.SelectPage(WorkspacePage.Animations);
        vm.AddAnimationPaths(["menu-test.cast"]);
        await Task.Delay(80);
        var textBox = window.GetVisualDescendants().OfType<TextBox>().First(box => AutomationProperties.GetName(box) == vm.Text.OutputFolder);
        foreach (var chinese in new[] { true, false, true })
        {
            if (vm.IsChinese != chinese) vm.ToggleLanguage();
            foreach (var style in new[] { 0, 1, 2 })
            {
                vm.ThemeStyleIndex = style;
                await Task.Delay(60);
                var flyout = textBox.ContextFlyout as MenuFlyout ?? throw new InvalidOperationException("Output folder has no edit menu.");
                flyout.ShowAt(textBox);
                await Task.Delay(60);
                var labels = flyout.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
                flyout.Hide();
                var expected = chinese ? new[] { "剪切", "复制", "粘贴" } : new[] { "Cut", "Copy", "Paste" };
                if (!labels.SequenceEqual(expected)) throw new InvalidOperationException($"Text menu language mismatch: {string.Join(", ", labels)}; expected {string.Join(", ", expected)}.");
            }
        }
        Console.WriteLine("Output-folder context menu: Chinese/English live switching, all three themes PASS");
    }
}
