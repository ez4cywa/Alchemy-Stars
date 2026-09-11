namespace AlchemyStars.Avalonia;

internal static class SingleAnimationExportSmoke
{
    internal static async Task RunAsync(string directory)
    {
        var folder = Path.Combine(directory, "single-export");
        Directory.CreateDirectory(folder);
        var preferences = new ApplicationPreferencesStore(Path.Combine(folder, "settings.json"));
        var unified = Path.Combine(folder, "unified");
        preferences.SaveUnifiedOutputDirectory(unified);
        var engine = new CaptureEngine();
        var store = new WorkspaceProjectStore();
        using var vm = new MainWindowViewModel(engine, store, preferences, new TestPicker());
        vm.Workspace.OutputFormat = "fbx";
        vm.Workspace.OutputPrefix = "prefix-";
        vm.Workspace.OutputSuffix = "-suffix";
        var first = new WorkspaceAnimation { Name = "missing-first.cast", OutputName = "same", OutputFolder = "first" };
        var selected = new WorkspaceAnimation
        {
            Name = "selected.cast", OutputName = "same", OutputFolder = "original", WeaponFollowMode = 2,
            EnableLeftHandIK = false, EnableRightHandIK = true, LeftHandPoseFile = "left.cast", RightHandPoseFile = "right.cast",
            LeftIKTargetBoneName = "left_target", RightIKTargetBoneName = "right_target",
        };
        selected.Layers.Add(new WorkspaceLayer { Name = "layer.cast", Offset = 12, Type = AnimationLayerKind.Gesture });
        vm.Animations.Add(first);
        vm.Animations.Add(selected);
        await vm.ExportSelectedAnimationAsync();
        Require(engine.Request is null && !vm.CanExportSelectedAnimation, "No selection exported a task.");
        vm.SelectedAnimation = selected;
        Require(vm.CanExportSelectedAnimation, "Selection did not enable single export.");
        var original = store.CreateExportRequest(vm.Workspace);
        await vm.ExportSelectedAnimationAsync();
        var captured = engine.Request!;
        Require(captured.Animations.Count == 1 && captured.Animations[0] with { Layers = original.Animations[1].Layers }
            == original.Animations[1] with { OutputFolder = unified },
            "Single export lost the selected job settings or picked the wrong index.");
        Require(captured.Options == original.Options && captured.Animations[0].Layers!.SequenceEqual(original.Animations[1].Layers!),
            "Single export lost output options/layers.");
        Require(vm.Animations.Count == 2 && selected.OutputFolder == "original" && ReferenceEquals(vm.SelectedAnimation, selected),
            "Single export mutated workspace jobs or selection.");
        Require(!vm.IsBusy && vm.IsDialogOpen && !vm.CanInteract && !vm.CanExportSelectedAnimation,
            "Export failure did not restore busy state or guard the dialog.");
        var calls = engine.Calls;
        await vm.ExportSelectedAnimationAsync();
        Require(engine.Calls == calls, "A dialog allowed another export.");
        vm.CloseDialog();
        vm.SelectPage(WorkspacePage.Settings);
        await vm.ExportSelectedAnimationAsync();
        Require(engine.Calls == calls, "Single export ran outside the animation page.");
        vm.SelectPage(WorkspacePage.Animations);
        await vm.ExportAsync();
        Require(engine.Request!.Animations.Count == 2, "Export all stopped exporting all jobs.");
        vm.CloseDialog();

        // Even an unselected job's inputs must not be overwritten by the chosen output.
        foreach (var format in Enum.GetValues<ExportFormat>())
        {
            var destination = Path.Combine(folder, "protected." + format.ToString().ToLowerInvariant());
            var request = original with
            {
                Options = original.Options with { Format = format, OutputPrefix = "", OutputSuffix = "" },
                Animations = [original.Animations[0] with { SourceFile = destination },
                    original.Animations[1] with { OutputFolder = folder, OutputName = "protected" }],
            };
            try { AnimationExportEngine.SelectAnimation(request, 1); }
            catch (ExportValidationException e) when (e.Code == ExportErrorCode.OutputWouldOverwriteInput) { continue; }
            throw new InvalidOperationException("Single export could overwrite an unselected source: " + format);
        }

        // Real engine seam: an invalid unselected task must not block the chosen CAST.
        var source = Path.Combine(folder, "source.cast");
        CastAxisSmoke.Write(source, "y");
        var real = new AnimationExportRequest([new(source, ModelPartKind.ViewHands)],
            [new(Path.Combine(folder, "missing.cast"), "other", folder),
             new(source, "chosen", folder, EnableLeftHandIk: false, EnableRightHandIk: false)],
            new(new("", "", "", ""), new("", "", "", "")));
        var output = new AnimationExportEngine().Export(AnimationExportEngine.SelectAnimation(real, 1)).OutputFiles.Single();
        Require(File.Exists(output) && !File.Exists(Path.Combine(folder, "other.cast")), "Real single export wrote extra jobs.");
        using var realVm = new MainWindowViewModel(new AnimationExportEngine(), store,
            new ApplicationPreferencesStore(Path.Combine(folder, "real-settings.json")), new TestPicker());
        realVm.Parts.Add(new WorkspacePart { FilePath = source, Type = ModelPartKind.ViewHands });
        realVm.Animations.Add(new WorkspaceAnimation { Name = "missing-unselected.cast", OutputName = "untouched", OutputFolder = folder });
        var realSelected = new WorkspaceAnimation
        {
            Name = source, OutputName = "chosen-from-ui", OutputFolder = folder,
            EnableLeftHandIK = false, EnableRightHandIK = false,
        };
        realVm.Animations.Add(realSelected);
        realVm.SelectedAnimation = realSelected;
        await realVm.ExportSelectedAnimationAsync();
        Require(!realVm.DialogIsError && realVm.Preview.HasScene
            && realVm.Preview.Source == Path.Combine(folder, "chosen-from-ui.cast")
            && !File.Exists(Path.Combine(folder, "untouched.cast")), "Successful UI export did not preview the selected output.");
        Console.WriteLine("Single animation export: selected index, settings/layers, unified output, guards, all-export, protected workspace inputs and real CAST PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CaptureEngine : IAnimationExportEngine
    {
        public EngineCapabilities Capabilities => new AnimationExportEngine().Capabilities;
        public AnimationExportRequest? Request { get; private set; }
        public int Calls { get; private set; }
        public AnimationExportResult Export(AnimationExportRequest request)
        {
            Request = request;
            Calls++;
            throw new InvalidOperationException("Expected capture-only export.");
        }
    }

    private sealed class TestPicker : IWorkspaceFilePicker
    {
        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);
    }
}
