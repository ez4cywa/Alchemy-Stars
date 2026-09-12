using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class RavenfieldUiSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        // Core contracts run in --self-test. Their temporary view models apply a
        // light theme globally, so running them here would invalidate dark-mode QA.
        vm.SelectPage(WorkspacePage.Settings);
        var view = window.GetVisualDescendants().OfType<RavenfieldView>().Single();
        await VerifyReferenceAsync(window, vm, view);
        var advanced = view.FindControl<Expander>("RfAdvanced")!;
        var scale = view.FindControl<NumericUpDown>("RfScale")!;
        scale.Value = 1.25m;
        if (vm.Workspace.Ravenfield.HandScale != 1.25) throw new InvalidOperationException("RF scale did not update workspace.");
        if (scale.Minimum != 0.1m || scale.Maximum != 10) throw new InvalidOperationException("RF scale bounds mismatch.");
        var scroll = view.GetVisualAncestors().OfType<ScrollViewer>().First();
        var originalLanguage = vm.IsChinese;
        var directory = Path.GetDirectoryName(Program.RenderSmokePath!)!;
        Directory.CreateDirectory(directory);
        foreach (var chinese in new[] { true, false })
        {
            if (vm.IsChinese != chinese) vm.ToggleLanguage();
            advanced.IsExpanded = true;
            scroll.Offset = default;
            await Task.Delay(150);
            window.UpdateLayout();
            foreach (var number in view.GetVisualDescendants().OfType<NumericUpDown>())
                if (number.Bounds.Width < 170) throw new InvalidOperationException("RF numeric field is too narrow: " + number.Bounds.Width);
            Save(window, Path.Combine(directory, "rf-900-" + (chinese ? "zh" : "en") + "-top.png"));
            advanced.BringIntoView();
            await Task.Delay(100);
            window.UpdateLayout();
            Save(window, Path.Combine(directory, "rf-900-" + (chinese ? "zh" : "en") + "-advanced.png"));
        }
        if (vm.IsChinese != originalLanguage) vm.ToggleLanguage();
        Console.WriteLine("RF 900px UI: bilingual expanded layout, numeric widths, scale binding and bounds PASS");
    }

    private static async Task VerifyReferenceAsync(MainWindow window, MainWindowViewModel vm, RavenfieldView view)
    {
        var temporary = Directory.CreateTempSubdirectory("AlchemyStars-RF-reference-ui-");
        var store = new WorkspaceProjectStore();
        var original = Path.Combine(temporary.FullName, "original.aprj");
        var originalProject = vm.CurrentProjectPath;
        store.Save(vm.Workspace, original);
        try
        {
            var workspace = new WorkspaceDocument();
            workspace.Animations.Add(new() { Name = "idle-first.cast" });
            workspace.Animations.Add(new() { Name = "idle-second.cast" });
            workspace.Ravenfield.ReferenceAnimationId = workspace.Animations[1].Id;
            var project = Path.Combine(temporary.FullName, "two-clips.aprj");
            store.Save(workspace, project);
            vm.LoadProject(project);
            vm.SelectPage(WorkspacePage.Settings);
            var combo = view.FindControl<ComboBox>("RfReference")!;
            async Task CheckSelected()
            {
                await Task.Delay(80);
                window.UpdateLayout();
                if (!ReferenceEquals(combo.SelectedItem, vm.Animations[1]) || combo.SelectedIndex != 1
                    || !combo.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "idle-second" && t.IsEffectivelyVisible))
                    throw new InvalidOperationException("RF saved reference is not visibly selected in ComboBox: index=" + combo.SelectedIndex
                        + ", selected=" + (combo.SelectedItem as WorkspaceAnimation)?.DisplayName + ", vm=" + vm.SelectedRavenfieldAnimation?.DisplayName
                        + ", labels=" + string.Join("|", combo.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text + ":" + t.IsEffectivelyVisible)));
            }
            await CheckSelected();
            vm.ToggleLanguage();
            await CheckSelected();
            vm.ToggleLanguage();
            vm.SelectedAnimation = vm.Animations[0];
            await CheckSelected();
            vm.Animations.RemoveAt(1);
            await Task.Delay(80);
            if (combo.SelectedItem is not null || vm.HasRavenfieldReference)
                throw new InvalidOperationException("Removed RF reference stayed selected in ComboBox.");
            Console.WriteLine("RF live ComboBox: saved second reference, visible label, language switch, independent normal selection and removal PASS");
        }
        finally
        {
            vm.LoadProject(originalProject ?? original);
            vm.SelectPage(WorkspacePage.Settings);
            temporary.Delete(recursive: true);
        }
    }

    private static void Save(MainWindow window, string path)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
        image.Render(window);
        image.Save(path, PngBitmapEncoderOptions.Default);
    }
}
