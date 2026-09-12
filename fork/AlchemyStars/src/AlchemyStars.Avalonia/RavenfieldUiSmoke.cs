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
            var mode = view.FindControl<ComboBox>("RfMode")!;
            var help = view.FindControl<TextBlock>("RfModeHelp")!;
            var contact = view.FindControl<CheckBox>("RfContactFit")!;
            var local = view.FindControl<CheckBox>("RfLocalHandFit")!;
            contact.IsChecked = true;
            local.IsChecked = true;
            foreach (var enabled in new[] { false, true })
            {
                contact.IsChecked = enabled;
                await Task.Delay(50);
                if (vm.Workspace.Ravenfield.ContactFit != enabled || vm.RavenfieldContactFit != enabled
                    || !Equals(contact.Content, vm.Text.RfContactFit)
                    || view.FindControl<TextBlock>("RfContactFitHelp")!.Text != vm.Text.RfContactFitHelp)
                    throw new InvalidOperationException("RF contact fit binding or localized label/help mismatch.");
                if (local.IsEffectivelyEnabled != enabled || local.IsChecked != true || !vm.Workspace.Ravenfield.LocalHandFit)
                    throw new InvalidOperationException("RF local fit prerequisite disabled state lost the saved preference.");
            }
            if (local.Content is not TextBlock label || label.Text != vm.Text.RfLocalHandFit
                || view.FindControl<TextBlock>("RfLocalHandFitHelp")!.Text != vm.Text.RfLocalHandFitHelp)
                throw new InvalidOperationException("RF local fit localized label/help mismatch.");
            view.FindControl<TextBlock>("RfLocalHandFitHelp")!.BringIntoView();
            await Task.Delay(80);
            window.UpdateLayout();
            if (local.TranslatePoint(default, scroll) is { } localPosition)
                scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + localPosition.Y - 40));
            await Task.Delay(50);
            window.UpdateLayout();
            if (local.Bounds.Width > view.Bounds.Width || label.Bounds.Width > view.Bounds.Width)
                throw new InvalidOperationException("RF local fit label overflows the view.");
            Save(window, Path.Combine(directory, "rf-900-" + (chinese ? "zh" : "en") + "-local-fit.png"));
            view.FindControl<TextBlock>("RfContactFitHelp")!.BringIntoView();
            await Task.Delay(80);
            window.UpdateLayout();
            Save(window, Path.Combine(directory, "rf-900-" + (chinese ? "zh" : "en") + "-contact.png"));
            foreach (var index in new[] { 0, 1, 2, 0, 2 })
            {
                mode.SelectedIndex = index;
                await Task.Delay(80);
                window.UpdateLayout();
                var expectedLabel = index switch { 1 => vm.Text.RfAnimation, 2 => vm.Text.RfLibrary, _ => vm.Text.RfPose };
                if (vm.Workspace.Ravenfield.Mode != (index switch { 1 => "animation", 2 => "library", _ => "pose" })
                    || !mode.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == expectedLabel && t.IsEffectivelyVisible)
                    || help.Text != (index switch { 1 => vm.Text.RfAnimationHelp, 2 => vm.Text.RfLibraryHelp, _ => vm.Text.RfPoseHelp }))
                    throw new InvalidOperationException("RF live mode selection/label/help mismatch.");
                if (mode.Bounds.Width > view.Bounds.Width || help.Bounds.Width > view.Bounds.Width)
                    throw new InvalidOperationException("RF mode layout overflows the view.");
            }
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
        Console.WriteLine("RF 900px UI: bilingual local/contact-fit bindings, prerequisite and preserved preference, mode switches, expanded layout and numeric bounds PASS");
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
            workspace.Animations.Add(new() { Name = "weapon-fire.cast" });
            workspace.Ravenfield.ReferenceAnimationId = workspace.Animations[1].Id;
            workspace.Ravenfield.Mode = "library";
            workspace.Ravenfield.ContactFit = false;
            workspace.Ravenfield.LocalHandFit = true;
            var project = Path.Combine(temporary.FullName, "two-clips.aprj");
            store.Save(workspace, project);
            vm.LoadProject(project);
            vm.SelectPage(WorkspacePage.Settings);
            var combo = view.FindControl<ComboBox>("RfReference")!;
            var mode = view.FindControl<ComboBox>("RfMode")!;
            var contact = view.FindControl<CheckBox>("RfContactFit")!;
            var local = view.FindControl<CheckBox>("RfLocalHandFit")!;
            async Task CheckSelected()
            {
                await Task.Delay(80);
                window.UpdateLayout();
                if (mode.SelectedIndex != 2 || vm.Workspace.Ravenfield.Mode != "library")
                    throw new InvalidOperationException("RF saved mode was not restored or changed with language/normal selection.");
                if (contact.IsChecked != false || vm.RavenfieldContactFit)
                    throw new InvalidOperationException("RF saved disabled contact fit was not restored.");
                if (local.IsChecked != true || !vm.RavenfieldLocalHandFit || local.IsEffectivelyEnabled)
                    throw new InvalidOperationException("RF saved local fit should remain checked and disabled while contact fit is off.");
                if (!ReferenceEquals(combo.SelectedItem, vm.Animations[1]) || combo.SelectedIndex != 1
                    || !combo.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "weapon-fire" && t.IsEffectivelyVisible))
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
            var adapt = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, vm.Text.RfAdapt));
            if (!adapt.IsEffectivelyEnabled) throw new InvalidOperationException("Valid RF reference did not enable adaptation.");
            contact.IsChecked = true;
            await Task.Delay(50);
            if (!local.IsEffectivelyEnabled) throw new InvalidOperationException("RF palm alignment did not enable local fit.");
            var runner = vm.RavenfieldRunner;
            var pending = new TaskCompletionSource<RavenfieldAdaptationResult>();
            vm.RavenfieldRunner = (_, index, options, _) =>
            {
                if (index != 1 || options.Mode != "library" || !options.ContactFit || !options.LocalHandFit) throw new InvalidOperationException("RF UI lost mode/reference/fitting preferences in export snapshot.");
                return pending.Task;
            };
            try
            {
                var run = vm.AdaptRavenfieldAsync();
                await Task.Delay(80);
                if (!vm.IsBusy || mode.IsEffectivelyEnabled || contact.IsEffectivelyEnabled || local.IsEffectivelyEnabled || adapt.IsEffectivelyEnabled || string.IsNullOrWhiteSpace(vm.BusyMessage))
                    throw new InvalidOperationException("RF busy state did not disable inputs or provide feedback.");
                pending.SetResult(new("result.blend", "result.fbx", "result.report.json", "result.preview.png", []));
                await run;
                vm.CloseDialog();
                if (vm.IsBusy || !vm.HasRavenfieldResult || !mode.IsEffectivelyEnabled)
                    throw new InvalidOperationException("RF completion state did not restore interaction/results.");
            }
            finally { vm.RavenfieldRunner = runner; }
            vm.Animations.RemoveAt(1);
            await Task.Delay(80);
            if (combo.SelectedItem is not null || vm.HasRavenfieldReference || adapt.IsEffectivelyEnabled)
                throw new InvalidOperationException("Removed RF reference stayed selected in ComboBox.");
            contact.IsChecked = true;
            store.Save(vm.Workspace, project);
            vm.LoadProject(project);
            await Task.Delay(80);
            if (contact.IsChecked != true || !vm.RavenfieldContactFit)
                throw new InvalidOperationException("RF enabled contact fit did not survive save/reload in UI.");
            if (local.IsChecked != true || !vm.RavenfieldLocalHandFit || !local.IsEffectivelyEnabled)
                throw new InvalidOperationException("RF opted-in local fit did not survive save/reload in UI.");
            File.WriteAllText(project, "{\"Ravenfield\":{\"ContactFit\":null}}");
            vm.LoadProject(project);
            await Task.Delay(80);
            if (contact.IsChecked != true || !vm.RavenfieldContactFit)
                throw new InvalidOperationException("RF null contact fit did not restore the enabled UI default.");
            if (local.IsChecked != false || vm.RavenfieldLocalHandFit)
                throw new InvalidOperationException("Old RF project enabled local reshaping without opt-in.");
            Console.WriteLine("RF live UI: contact-fit false/true/null persistence, saved non-idle reference/library mode, language switch, independent selection, busy/completion states and removal PASS");
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
