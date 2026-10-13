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
        CastAxisSmoke.AddGeometry(source);
        VerifyBoundModelFbxProtection(source, folder);
        VerifyDefaultOutputProtection(source, folder);
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
        var outputChildren = Cast.NET.CastReader.Load(Path.Combine(folder, "chosen-from-ui.cast")).RootNodes.Single().Children;
        for (var attempt = 0; attempt < 100 && realVm.Preview.FrameData is null; attempt++)
            await Task.Delay(10);
        Require(!realVm.DialogIsError, "Successful UI animation export reported an error.");
        Require(realVm.Preview.HasScene && realVm.Preview.Source == Path.Combine(folder, "chosen-from-ui.cast"),
            "Successful UI animation export did not load its independent preview scene.");
        Require(realVm.Preview.FrameData is { TriangleCount: > 0 },
            "Independent export preview did not render the model geometry.");
        Require(outputChildren.OfType<Cast.NET.Nodes.ModelNode>().Count() == 0
            && outputChildren.OfType<Cast.NET.Nodes.AnimationNode>().Count() == 1,
            "Formal CAST animation output still contains model data.");
        Require(!File.Exists(Path.Combine(folder, "untouched.cast")), "Selected export wrote an unselected animation.");
        Console.WriteLine("Single animation export: selected index, settings/layers, unified output, guards, all-export, protected workspace inputs, inferred output and real CAST PASS");
    }

    private static void VerifyDefaultOutputProtection(string source, string folder)
    {
        var inferredFolder = Path.Combine(Path.GetDirectoryName(source)!, "output");
        Directory.CreateDirectory(inferredFolder);
        var sourceBytes = File.ReadAllBytes(source);
        var cases = 0;
        foreach (var configuredFolder in new[] { "", " \t " })
        {
            foreach (var format in Enum.GetValues<ExportFormat>())
            {
                var extension = "." + format.ToString().ToLowerInvariant();
                var destination = Path.Combine(inferredFolder, "prefix-protected-suffix" + extension);
                foreach (var input in new[] { "source", "left-pose", "right-pose", "layer" })
                {
                    File.WriteAllBytes(destination, sourceBytes);
                    var other = new AnimationExportJob(Path.Combine(folder, "unselected.cast"), "unused", folder);
                    other = input switch
                    {
                        "source" => other with { SourceFile = destination },
                        "left-pose" => other with { LeftHandPoseFile = destination },
                        "right-pose" => other with { RightHandPoseFile = destination },
                        _ => other with { Layers = [new(destination)] },
                    };
                    var selected = new AnimationExportJob(source, "protected", configuredFolder,
                        EnableLeftHandIk: false, EnableRightHandIk: false);
                    var request = new AnimationExportRequest([new(source, ModelPartKind.ViewHands)], [other, selected],
                        new(new("", "", "", ""), new("", "", "", ""), format, "prefix-", "-suffix"));
                    var rejected = false;
                    try { AnimationExportEngine.SelectAnimation(request, 1); }
                    catch (ExportValidationException error) when (error.Code == ExportErrorCode.OutputWouldOverwriteInput)
                    {
                        rejected = true;
                    }
                    Require(sourceBytes.SequenceEqual(File.ReadAllBytes(source))
                        && sourceBytes.SequenceEqual(File.ReadAllBytes(destination)),
                        "Default-output collision validation changed an input.");
                    Require(rejected, $"Default output could overwrite an unselected {input}: {format}, folder '{configuredFolder}'.");
                    cases++;
                }
            }
            var safeJob = new AnimationExportJob(source, configuredFolder.Length == 0 ? "safe-empty" : "safe-whitespace",
                configuredFolder, EnableLeftHandIk: false, EnableRightHandIk: false);
            var safeRequest = new AnimationExportRequest([new(source, ModelPartKind.ViewHands)],
                [new(Path.Combine(folder, "missing-unselected.cast"), "unused", folder), safeJob],
                new(new("", "", "", ""), new("", "", "", ""), OutputPrefix: "prefix-", OutputSuffix: "-suffix"));
            var selectedRequest = AnimationExportEngine.SelectAnimation(safeRequest, 1);
            Require(selectedRequest.Animations.Single() == safeJob with { OutputFolder = inferredFolder },
                "Selected export did not preserve its normalized default output folder/settings.");
            Require(safeRequest.Animations[1].OutputFolder == configuredFolder,
                "Selecting a default-output task mutated the original request.");
            var result = new AnimationExportEngine().Export(selectedRequest);
            Require(result.OutputFiles.Single() == Path.Combine(inferredFolder, "prefix-" + safeJob.OutputName + "-suffix.cast")
                && File.Exists(result.OutputFiles.Single()), "Default-output single export did not write its inferred destination.");
            Require(sourceBytes.SequenceEqual(File.ReadAllBytes(source)), "Default-output single export changed its source.");
        }
        Console.WriteLine($"Single export default-output protection: PASS ({cases} source/pose/layer collision cases, 2 real CAST exports)");
    }

    private static void VerifyBoundModelFbxProtection(string hands, string folder)
    {
        var weapon = Path.Combine(folder, "atomic-weapon.cast");
        var weaponCast = Cast.NET.CastReader.Load(hands);
        weaponCast.RootNodes.Single().Children.OfType<Cast.NET.Nodes.ModelNode>().Single().Skeleton!.Bones[0]
            .AddString("n", "weapon_root");
        Cast.NET.CastWriter.Save(weapon, weaponCast);
        var destinationFolder = Path.Combine(folder, "atomic-fbx");
        Directory.CreateDirectory(destinationFolder);
        var destination = Path.Combine(destinationFolder, "atomic-weapon_model.fbx");
        var request = new ModelExportRequest([new(hands, ModelPartKind.ViewHands), new(weapon, ModelPartKind.Weapon, "tag_origin")],
            destinationFolder, ExportFormat.Fbx);
        var oldBytes = "previous complete FBX"u8.ToArray();
        var newBytes = "replacement complete FBX"u8.ToArray();
        var handsBytes = File.ReadAllBytes(hands);
        var weaponBytes = File.ReadAllBytes(weapon);
        string? stagedCast = null;
        string? stagedFbx = null;
        void VerifyStage(string cast, string fbx, float framerate)
        {
            stagedCast = cast; stagedFbx = fbx;
            var model = Cast.NET.CastReader.Load(cast).RootNodes.Single().Children.OfType<Cast.NET.Nodes.ModelNode>().Single();
            Require(model.Meshes.Length > 0 && model.Skeleton!.Bones.Length > 0,
                "Bound-model FBX regression did not exercise a real merged CAST.");
            Require(framerate == WorkspacePaths.StandardAnimationFramerate, "Bound-model FBX framerate changed.");
        }
        var expectedFailure = new IOException("Simulated FBX converter failure after a partial write.");
        var failingEngine = new ModelExportEngine((cast, fbx, framerate) =>
        {
            VerifyStage(cast, fbx, framerate);
            File.WriteAllBytes(fbx, "partial FBX"u8.ToArray());
            throw expectedFailure;
        });
        foreach (var existingOutput in new[] { true, false })
        {
            if (existingOutput) File.WriteAllBytes(destination, oldBytes);
            else File.Delete(destination);
            var observedFailure = false;
            try { failingEngine.Export(request); }
            catch (IOException error) when (ReferenceEquals(error, expectedFailure)) { observedFailure = true; }
            Require(observedFailure, "Bound-model FBX converter failure was not preserved.");
            Require(existingOutput ? oldBytes.SequenceEqual(File.ReadAllBytes(destination)) : !File.Exists(destination),
                "Failed bound-model FBX export replaced an existing result or left a partial new result.");
            Require(stagedFbx is not null && !File.Exists(stagedFbx) && stagedCast is not null && !File.Exists(stagedCast)
                && !Directory.EnumerateFiles(destinationFolder).Any(path => path != destination),
                "Failed bound-model FBX export retained staged files.");
        }
        File.WriteAllBytes(destination, oldBytes);
        var successfulEngine = new ModelExportEngine((cast, fbx, framerate) =>
        {
            VerifyStage(cast, fbx, framerate);
            Require(oldBytes.SequenceEqual(File.ReadAllBytes(destination)), "FBX destination changed before conversion completed.");
            Require(fbx != destination && Path.GetDirectoryName(fbx) == destinationFolder && Path.GetExtension(fbx) == ".fbx",
                "FBX converter was not given a same-directory temporary FBX.");
            File.WriteAllBytes(fbx, newBytes);
        });
        var result = successfulEngine.Export(request);
        Require(result.OutputFile == destination && newBytes.SequenceEqual(File.ReadAllBytes(destination)),
            "Successful bound-model FBX export did not commit its replacement.");
        Require(stagedFbx is not null && !File.Exists(stagedFbx) && stagedCast is not null && !File.Exists(stagedCast)
            && Directory.EnumerateFiles(destinationFolder).Count() == 1, "Successful bound-model FBX export retained staged files.");
        Require(handsBytes.SequenceEqual(File.ReadAllBytes(hands)) && weaponBytes.SequenceEqual(File.ReadAllBytes(weapon)),
            "Bound-model FBX regression changed its model inputs.");
        Console.WriteLine("Bound-model FBX atomic protection: PASS (real merged CAST, existing/new output failures, successful replacement, staged cleanup, unchanged inputs)");
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
