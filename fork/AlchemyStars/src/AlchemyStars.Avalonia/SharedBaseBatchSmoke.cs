using System.Numerics;
using Cast.NET;
using Cast.NET.Nodes;

namespace AlchemyStars.Avalonia;

internal static class SharedBaseBatchSmoke
{
    internal static void Run(string directory)
    {
        var template = new WorkspaceAnimation
        {
            Name = Path.Combine(directory, "base.cast"), OutputName = "custom-template",
            OutputFolder = Path.Combine(directory, "exports"), OutputFramerate = 60,
            WeaponFollowMode = 2, EnableLeftHandIK = false, EnableRightHandIK = true,
            UseExperimentalFeatures = false, LeftHandPoseFile = "left.cast", RightHandPoseFile = "right.cast",
            LeftIKTargetBoneName = "left_target", RightIKTargetBoneName = "right_target",
        };
        template.Layers.Add(new WorkspaceLayer { Name = "common.cast", Offset = 7, Color = 9, Type = AnimationLayerKind.Additive });
        template.Layers.Add(new WorkspaceLayer { Name = "variable.cast", Offset = 12, Color = 3, Type = AnimationLayerKind.Gesture });
        var first = Path.Combine(directory, "reload.cast");
        var second = Path.Combine(directory, "fire.cast");
        Require(SharedBaseAnimationBatch.Create(template, []).Count == 0, "Empty input created tasks.");
        var tasks = SharedBaseAnimationBatch.Create(template,
            [first, $" \"{first.ToUpperInvariant()}\" ", second, "", "skip.fbx"], template.Layers[1]);
        Require(tasks.Count == 2, "Duplicate or invalid paths were not filtered.");
        Require(tasks[0].OutputName == "reload" && tasks[1].OutputName == "fire", "Variable filenames were not used.");
        var collision = SharedBaseAnimationBatch.Create(template,
            [first, Path.Combine(directory, "another", "reload.cast")], reservedOutputNames: ["RELOAD", "reload_2"]);
        Require(collision[0].OutputName == "reload_3" && collision[1].OutputName == "reload_4",
            "Reserved names or same-stem inputs were allowed to overwrite one another.");
        Require(tasks.Select(task => task.Id).Append(template.Id).Distinct().Count() == 3, "Task IDs were reused.");
        foreach (var task in tasks)
        {
            Require(task.Name == template.Name && task.OutputFolder == template.OutputFolder
                && task.OutputFramerate == 60 && task.WeaponFollowMode == 2
                && !task.EnableLeftHandIK && task.EnableRightHandIK && !task.UseExperimentalFeatures
                && task.LeftHandPoseFile == "left.cast" && task.RightHandPoseFile == "right.cast"
                && task.LeftIKTargetBoneName == "left_target" && task.RightIKTargetBoneName == "right_target",
                "A task setting was lost.");
            Require(task.Layers.Count == 2 && task.Layers[0].Name == "common.cast"
                && task.Layers[0].Offset == 7 && task.Layers[0].Color == 9
                && task.Layers[1].Offset == 12 && task.Layers[1].Color == 3
                && task.Layers[1].Type == template.Layers[1].Type, "Layer settings or order changed.");
        }
        tasks[0].Layers[0].Name = "changed.cast";
        tasks[0].Layers[1].Offset = 99;
        tasks[0].OutputFolder = "changed-output";
        Require(tasks[0].OutputName == "reload", "Common first-layer changes overwrote the batch filename.");
        Require(tasks[1].Layers[0].Name == "common.cast" && tasks[1].Layers[1].Offset == 12
            && template.Layers[0].Name == "common.cast" && template.Layers[1].Name == "variable.cast"
            && template.Layers[1].Offset == 12 && template.OutputName == "custom-template", "Clones shared mutable state.");
        var appended = SharedBaseAnimationBatch.Create(template, [first]).Single();
        Require(appended.Layers.Count == 3 && appended.Layers[2].Name == first
            && appended.Layers[2].Type == AnimationLayerKind.Additive, "Append mode changed common layers.");
        var emptyTemplate = new WorkspaceAnimation { Name = "base.cast" };
        Require(SharedBaseAnimationBatch.Create(emptyTemplate, [second]).Single().Layers.Count == 1, "No-layer template failed.");
        try
        {
            SharedBaseAnimationBatch.Create(new WorkspaceAnimation(), [first]);
            throw new InvalidOperationException("Missing base animation was accepted.");
        }
        catch (ArgumentException) { }
        try
        {
            SharedBaseAnimationBatch.Create(template, [first], new WorkspaceLayer());
            throw new InvalidOperationException("Foreign replacement layer was accepted.");
        }
        catch (ArgumentException) { }
        using var vm = new MainWindowViewModel(new AnimationExportEngine(), new WorkspaceProjectStore(),
            new ApplicationPreferencesStore(Path.Combine(directory, "batch-settings.json")), new TestPicker());
        vm.Animations.Add(template);
        vm.SelectedAnimation = template;
        Require(vm.AddSharedBaseBatchPaths(template, [first, second], template.Layers[1]) == 2
            && vm.Animations.Count == 3 && ReferenceEquals(vm.Animations[0], template)
            && ReferenceEquals(vm.SelectedAnimation, vm.Animations[1]), "View model batch insertion failed.");
        var request = new WorkspaceProjectStore().CreateExportRequest(vm.Workspace);
        Require(request.Animations.Count == 3 && request.Animations.All(animation => animation.Framerate == 60),
            "Generated tasks did not retain the configured output framerate.");
        var snapshot = WorkspaceProjectStore.Snapshot(vm.Workspace);
        Require(snapshot.Animations[1].OutputName == "reload" && snapshot.Animations[2].OutputName == "fire"
            && snapshot.Animations[1].OutputFramerate == 60
            && snapshot.Animations[1].Layers[0].Name == "common.cast", "Snapshot changed generated tasks.");
        vm.AddSharedBaseBatchPaths(template, [first]);
        Require(vm.SelectedAnimation!.OutputName == "reload_2", "UI did not reserve existing task output names.");
        RunRealExport(directory);
        RunPickerRacesAsync(directory).GetAwaiter().GetResult();
        Console.WriteLine("Shared-base batch: empty, deduplication, replacement/append, settings, independent layers/IDs, naming, UI insertion/export snapshot, real CAST outputs, picker races PASS");
    }

    private static void RunRealExport(string directory)
    {
        var fixture = Path.Combine(directory, "shared-base-real");
        Directory.CreateDirectory(fixture);
        var model = Path.Combine(fixture, "hands.cast");
        var basis = Path.Combine(fixture, "base.cast");
        var reload = Path.Combine(fixture, "reload.cast");
        var otherInput = Path.Combine(fixture, "another-source");
        Directory.CreateDirectory(otherInput);
        var fire = Path.Combine(otherInput, "reload.cast");
        var root = new CastNode(CastNodeIdentifier.Root) { Hash = 1 };
        var modelNode = new ModelNode { Parent = root, Hash = 2 };
        var skeleton = new SkeletonNode { Parent = modelNode, Hash = 3 };
        foreach (var (name, parent, hash) in new[] { ("tag_origin", uint.MaxValue, 4UL), ("j_test", 0U, 5UL) })
        {
            var bone = new BoneNode { Parent = skeleton, Hash = hash };
            bone.AddString("n", name); bone.AddValue("p", parent);
            bone.AddValue("lp", Vector3.Zero); bone.AddValue("wp", Vector3.Zero);
            bone.AddValue("lr", new Vector4(0, 0, 0, 1)); bone.AddValue("wr", new Vector4(0, 0, 0, 1));
        }
        CastWriter.Save(model, new Cast.NET.Cast([root]));
        WriteAnimation(basis, 0);
        WriteAnimation(reload, 2);
        WriteAnimation(fire, 7);
        var document = WorkspaceDocument.Create(castAnimationOnly: true);
        document.Parts.Add(new WorkspacePart { FilePath = model, Type = ModelPartKind.ViewHands });
        var template = new WorkspaceAnimation
        {
            Name = basis, OutputFolder = Path.Combine(fixture, "exports"),
            EnableLeftHandIK = false, EnableRightHandIK = false,
        };
        foreach (var task in SharedBaseAnimationBatch.Create(template, [reload, fire])) document.Animations.Add(task);
        var request = new WorkspaceProjectStore().CreateExportRequest(document);
        var outputs = new AnimationExportEngine().Export(request).OutputFiles;
        Require(outputs.Count == 2 && outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
            && outputs.All(File.Exists), "The real batch did not create two distinct outputs.");
        Require(Path.GetFileNameWithoutExtension(outputs[0]) == "reload"
            && Path.GetFileNameWithoutExtension(outputs[1]) == "reload_2", "Real same-stem exports were not disambiguated.");
        var values = outputs.Select(path =>
        {
            var scene = CastPreviewScene.Load(path, request.Parts);
            Require(scene.FrameCount >= 2, "Export lost the animation frames.");
            scene.Sample(1);
            return scene.Skeletons.Single().Bones.Single(bone => bone.Name == "j_test").LocalTranslation.X;
        }).ToArray();
        Require(MathF.Abs(values[0] - 2) < 0.001f && MathF.Abs(values[1] - 7) < 0.001f,
            $"Real exports did not retain the distinct overlays: {values[0]}, {values[1]}.");

        var sixty = Path.Combine(fixture, "sixty.cast");
        WriteAnimation(sixty, 60, 60, 60);
        var timed = request with { Animations = [request.Animations[0] with { SourceFile = sixty, OutputName = "resampled", Layers = [] }] };
        var timedOutput = new AnimationExportEngine().Export(timed).OutputFiles.Single();
        var timedScene = CastPreviewScene.Load(timedOutput, request.Parts);
        Require(timedScene.Framerate == 30 && timedScene.FrameCount == 31, "60 FPS source duration was not preserved at 30 FPS.");
        timedScene.Sample(15);
        Require(MathF.Abs(timedScene.Skeletons.Single().Bones.Single(b => b.Name == "j_test").LocalTranslation.X - 30) < .001f,
            "Resampling changed the half-second pose.");
        var timed60 = timed with
        {
            Animations = [timed.Animations[0] with { OutputName = "resampled60", Framerate = 60 }],
        };
        var timed60Output = new AnimationExportEngine().Export(timed60).OutputFiles.Single();
        var timed60Scene = CastPreviewScene.Load(timed60Output, request.Parts);
        Require(timed60Scene.Framerate == 60 && timed60Scene.FrameCount > timedScene.FrameCount,
            "Custom 60 FPS output did not preserve the source timeline.");
        timed60Scene.Sample(30);
        Require(MathF.Abs(timed60Scene.Skeletons.Single().Bones.Single(b => b.Name == "j_test").LocalTranslation.X - 30) < .001f,
            "Custom 60 FPS resampling changed the midpoint pose.");
        Require(AnimationClipMetadataReader.Read(sixty).FrameCount == 31, "Timeline did not use output-frame units.");
        void Reject(AnimationExportRequest invalid)
        {
            try { new AnimationExportEngine().Export(invalid); }
            catch (InvalidDataException) { return; }
            catch (ExportValidationException) { return; }
            throw new InvalidOperationException("Invalid batch was accepted.");
        }
        Reject(timed with { Animations = [timed.Animations[0], timed.Animations[0]] });
        Reject(timed with { Animations = [timed.Animations[0] with { Framerate = 0 }] });
        var originalBytes = File.ReadAllBytes(sixty);
        Reject(timed with { Animations = [request.Animations[0] with { OutputFolder = fixture, OutputName = "sixty" }, timed.Animations[0]] });
        Require(File.ReadAllBytes(sixty).SequenceEqual(originalBytes), "Cross-task input was overwritten.");
        var unsupported = Path.Combine(fixture, "input.seanim");
        File.Copy(sixty, unsupported);
        Reject(timed with { Animations = [timed.Animations[0] with { SourceFile = unsupported }] });
        Console.WriteLine("Real CAST: variable-FPS duration/pose/timeline, output collisions and CAST-only validation PASS");
    }

    private static void WriteAnimation(string path, float translation, float fps = 30, byte lastFrame = 1)
    {
        var root = new CastNode(CastNodeIdentifier.Root) { Hash = 1 };
        var animation = new AnimationNode { Parent = root, Hash = 2 };
        animation.AddValue("fr", fps);
        ulong hash = 3;
        foreach (var property in new[] { "tx", "ty", "tz" })
        {
            _ = new CurveNode
            {
                Parent = animation, Hash = hash++, NodeName = "j_test", KeyPropertyName = property, Mode = "absolute",
                KeyFrameBuffer = new CastArrayProperty<byte>([0, lastFrame]),
                KeyValueBuffer = new CastArrayProperty<float>([0, property == "tx" ? translation : 0]),
            };
        }
        _ = new CurveNode
        {
            Parent = animation, Hash = hash, NodeName = "j_test", KeyPropertyName = "rq", Mode = "absolute",
            KeyFrameBuffer = new CastArrayProperty<byte>([0, lastFrame]),
            KeyValueBuffer = new CastArrayProperty<Vector4>([new(0, 0, 0, 1), new(0, 0, 0, 1)]),
        };
        CastWriter.Save(path, new Cast.NET.Cast([root]));
    }

    private static async Task RunPickerRacesAsync(string directory)
    {
        var picker = new TestPicker();
        using var vm = new MainWindowViewModel(new AnimationExportEngine(), new WorkspaceProjectStore(),
            new ApplicationPreferencesStore(Path.Combine(directory, "batch-race-settings.json")), picker);
        var template = new WorkspaceAnimation { Name = "base.cast" };
        vm.Animations.Add(template);
        vm.SelectedAnimation = template;
        await vm.AddSharedBaseBatchAsync();
        Require(vm.Animations.Count == 1, "Cancelling the picker created tasks.");
        picker.Pending = new TaskCompletionSource<IReadOnlyList<string>>();
        var pending = vm.AddSharedBaseBatchAsync();
        vm.NewProject();
        picker.Pending.SetResult(["reload.cast"]);
        await pending;
        Require(vm.Animations.Count == 0, "A stale picker added tasks to a new workspace.");
        vm.Animations.Add(template);
        vm.SelectedAnimation = template;
        picker.Pending = new TaskCompletionSource<IReadOnlyList<string>>();
        pending = vm.AddSharedBaseBatchAsync();
        vm.Animations.Remove(template);
        picker.Pending.SetResult(["reload.cast"]);
        await pending;
        Require(vm.Animations.Count == 0, "A stale picker resurrected a removed template.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestPicker : IWorkspaceFilePicker
    {
        public TaskCompletionSource<IReadOnlyList<string>>? Pending { get; set; }
        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple) => Pending?.Task ?? Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);
    }
}
