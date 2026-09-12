namespace AlchemyStars.Avalonia;

internal static class RavenfieldSmoke
{
    internal static int RunProject(string[] args)
    {
        try
        {
            if (args.Length < 3) throw new ArgumentException("Usage: --rf-smoke project.aprj rf.blend|rf.unitypackage output-folder [animation-index]");
            var store = new WorkspaceProjectStore();
            var workspace = store.Load(args[0]);
            workspace.Ravenfield.RfSourcePath = args[1];
            var savedId = workspace.Ravenfield.ReferenceAnimationId;
            var index = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture)
                : string.IsNullOrWhiteSpace(savedId) ? 0
                : workspace.Animations.ToList().FindIndex(a => a.Id == savedId);
            var request = store.CreateExportRequest(workspace);
            request = request with { Animations = request.Animations.Select(a => a with { OutputFolder = Path.GetFullPath(args[2]) }).ToArray() };
            var result = new RavenfieldAdaptationEngine().Adapt(request, index, workspace.Ravenfield, args[0]);
            Console.WriteLine(string.Join(Environment.NewLine, new[] { result.BlendPath, result.FbxPath, result.ReportPath, result.PreviewPath }.Concat(result.Warnings)));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    internal static void Run(string directory)
    {
        var folder = Path.Combine(directory, "ravenfield");
        Directory.CreateDirectory(folder);
        var rf = Path.Combine(folder, "hands.blend");
        File.WriteAllText(rf, "validation fixture");
        var doc = new WorkspaceDocument();
        doc.Ravenfield.RfSourcePath = rf;
        doc.Ravenfield.IdleFrame = 7;
        doc.Ravenfield.UntaggedModelUpAxis = "y";
        doc.Ravenfield.HandScale = 1.1;
        doc.Ravenfield.Left.PositionX = .012;
        doc.Ravenfield.Right.RotationZ = 12;
        doc.Ravenfield.Right.ElbowSwivel = -25;
        doc.Ravenfield.Left.FingerCurl = 5;
        doc.Parts.Add(new() { FilePath = Path.Combine(folder, "hands.cast"), Type = ModelPartKind.ViewHands });
        doc.Parts.Add(new() { FilePath = Path.Combine(folder, "weapon.cast"), Type = ModelPartKind.Weapon });
        doc.Animations.Add(new() { Name = Path.Combine(folder, "idle.cast"), OutputName = "idle", OutputFolder = folder });
        var store = new WorkspaceProjectStore();
        var project = Path.Combine(folder, "rf.aprj");
        store.Save(doc, project);
        var loaded = store.Load(project);
        Require(loaded.Ravenfield.SourceUnit == "cm" && loaded.Ravenfield.UntaggedModelUpAxis == "y" && loaded.Ravenfield.IdleFrame == 7 && loaded.Ravenfield.HandScale == 1.1
            && loaded.Ravenfield.Left.PositionX == .012 && loaded.Ravenfield.Right.RotationZ == 12
            && loaded.Ravenfield.Right.ElbowSwivel == -25 && loaded.Ravenfield.Left.FingerCurl == 5, "RF persistence lost fields.");
        var request = store.CreateExportRequest(loaded);
        var outputs = RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield, project);
        Require(outputs.Count == 4 && outputs[0].EndsWith("idle_rf_idle.blend"), "RF output naming changed.");
        foreach (var output in outputs)
        {
            var protectedRequest = request with { Animations = [request.Animations[0], request.Animations[0] with { SourceFile = output }] };
            Reject(() => RavenfieldAdaptationEngine.Validate(protectedRequest, 0, loaded.Ravenfield), "Unselected input overwrite allowed.");
            protectedRequest = request with { Animations = [request.Animations[0], request.Animations[0] with {
                LeftHandPoseFile = output, Layers = [new(output)] }] };
            Reject(() => RavenfieldAdaptationEngine.Validate(protectedRequest, 0, loaded.Ravenfield), "Unselected pose/layer overwrite allowed.");
            Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield, output), "Project overwrite allowed.");
        }
        var nullableProject = Path.Combine(folder, "null-rf.aprj");
        foreach (var json in new[] { "{}", "{\"Ravenfield\":null}", "{\"Ravenfield\":{\"Left\":null,\"Right\":null,\"SourceUnit\":null}}" })
        {
            File.WriteAllText(nullableProject, json);
            var empty = store.Load(nullableProject);
            Require(empty.Ravenfield is not null && empty.Ravenfield.Left is not null && empty.Ravenfield.Right is not null
                && empty.Ravenfield.SourceUnit == "cm", "Nullable RF project did not normalize.");
        }
        TestPublication(folder);
        TestModelAxes(folder, request);
        TestReferencePersistence(folder);
        TestWorkspaceSwitchAsync(folder).GetAwaiter().GetResult();
        loaded.Ravenfield.IdleFrame = -1;
        Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Negative frame allowed.");
        loaded.Ravenfield.IdleFrame = 0; loaded.Ravenfield.HandScale = double.NaN;
        Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Nonfinite scale allowed.");
        loaded.Ravenfield.HandScale = 1; loaded.Ravenfield.Left.PositionZ = double.PositiveInfinity;
        Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Nonfinite adjustment allowed.");
        loaded.Ravenfield.Left.PositionZ = 0;
        foreach (var scale in new[] { 0.099, 10.001 })
        {
            loaded.Ravenfield.HandScale = scale;
            Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Out-of-range scale allowed.");
        }
        loaded.Ravenfield.HandScale = 1;
        foreach (var set in new Action<double>[] { v => loaded.Ravenfield.Left.PositionX = v, v => loaded.Ravenfield.Right.RotationZ = v })
        {
            set(10001); Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Out-of-range transform allowed."); set(0);
        }
        loaded.Ravenfield.Left.ElbowSwivel = 181;
        Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Out-of-range elbow allowed.");
        loaded.Ravenfield.Left.ElbowSwivel = 180; loaded.Ravenfield.Right.FingerCurl = -91;
        Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Out-of-range finger curl allowed.");
        loaded.Ravenfield.Right.FingerCurl = -90;
        RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield);
        loaded.Ravenfield.Left.PositionZ = 0; loaded.Ravenfield.SourceUnit = "unknown";
        Reject(() => RavenfieldAdaptationEngine.Validate(request, 0, loaded.Ravenfield), "Unknown unit allowed.");
        Require(doc.OutputFormat == ".cast" && doc.OutputUpAxis == "source", "RF settings mutated normal export.");
        Console.WriteLine("RF persistence/nulls, units, validation, protected inputs, transactional publication and workspace-switch races PASS");
    }

    private static void TestModelAxes(string folder, AnimationExportRequest request)
    {
        string Fixture(string name, string? axis)
        {
            var path = Path.Combine(folder, name + ".cast");
            var root = new Cast.NET.CastNode(Cast.NET.CastNodeIdentifier.Root);
            if (axis is not null)
            {
                var metadata = new Cast.NET.CastNode(Cast.NET.CastNodeIdentifier.Metadata) { Parent = root };
                metadata.AddString("up", axis);
            }
            Cast.NET.CastWriter.Save(path, new Cast.NET.Cast([root]));
            return path;
        }
        string? Axis(string path) => Cast.NET.CastReader.Load(path).RootNodes.SelectMany(r => r.Children)
            .OfType<Cast.NET.Nodes.MetadataNode>().FirstOrDefault()?.UpAxis;
        var hands = Fixture("axis-hands", "z");
        var weapon = Fixture("axis-weapon", null);
        var attachment = Fixture("axis-tagged", "y");
        var original = File.ReadAllBytes(weapon);
        request = request with { Parts = [new(hands, ModelPartKind.ViewHands), new(weapon, ModelPartKind.Weapon), new(attachment, ModelPartKind.Attachment)] };
        foreach (var fallback in new[] { "hands", "x", "y", "z" })
        {
            var (prepared, assumptions) = RavenfieldAdaptationEngine.PrepareModelAxes(request, fallback, folder);
            var expected = fallback == "hands" ? "z" : fallback;
            Require(Axis(prepared.Parts[1].FilePath) == expected && assumptions.Count == 1 && assumptions[0].Axis == expected,
                "Untagged RF weapon axis was not resolved explicitly.");
            Require(prepared.Parts[0].FilePath == hands && prepared.Parts[2].FilePath == attachment && Axis(attachment) == "y",
                "RF axis fallback changed hands or explicit metadata.");
            Require(File.ReadAllBytes(weapon).SequenceEqual(original) && Axis(weapon) is null,
                "RF axis preprocessing modified the source file.");
        }
        Reject(() => RavenfieldAdaptationEngine.PrepareModelAxes(request, "bad", folder), "Invalid RF axis accepted.");
    }

    private static void TestPublication(string folder)
    {
        var staged = Enumerable.Range(0, 4).Select(i => Path.Combine(folder, "new-" + i)).ToArray();
        var targets = Enumerable.Range(0, 4).Select(i => Path.Combine(folder, "final-" + i)).ToArray();
        for (var i = 0; i < 4; i++) File.WriteAllText(staged[i], "new " + i);
        foreach (var existing in new[] { true, false })
        {
            for (var i = 0; i < 4; i++) { if (existing) File.WriteAllText(targets[i], "old " + i); else if (File.Exists(targets[i])) File.Delete(targets[i]); }
            var calls = 0;
            try
            {
                RavenfieldAdaptationEngine.PublishOutputs(staged, targets, (source, target, overwrite) => {
                    if (++calls == 3) throw new IOException("Injected third-file publication failure.");
                    File.Move(source, target, overwrite);
                });
                throw new InvalidOperationException("Injected publication failure was swallowed.");
            }
            catch (IOException) { }
            Require(targets.Select((p, i) => existing ? File.ReadAllText(p) == "old " + i : !File.Exists(p)).All(v => v),
                "Mid-publication failure left mixed or partial results.");
            Require(!Directory.EnumerateDirectories(folder, ".AlchemyStars-RF-publish-*").Any(), "Rollback left temporary files.");
        }
        RavenfieldAdaptationEngine.PublishOutputs(staged, targets);
        Require(targets.Select((p, i) => File.ReadAllText(p) == "new " + i).All(v => v), "Successful publication lost results.");
        Require(!Directory.EnumerateDirectories(folder, ".AlchemyStars-RF-publish-*").Any(), "Success left publication temporary files.");
    }

    private static async Task TestWorkspaceSwitchAsync(string folder)
    {
        using var vm = new MainWindowViewModel(new AnimationExportEngine(), new WorkspaceProjectStore(),
            new ApplicationPreferencesStore(Path.Combine(folder, "preferences.json")), new EmptyPicker());
        var result = new RavenfieldAdaptationResult("result.blend", "result.fbx", "result.report.json", "result.preview.png", []);
        vm.RavenfieldRunner = (_, _, _, _) => Task.FromResult(result);
        vm.Animations.Add(new() { Name = "idle.cast" });
        vm.SelectedAnimation = vm.Animations[0];
        vm.SelectedRavenfieldAnimation = vm.Animations[0];
        await vm.AdaptRavenfieldAsync();
        Require(vm.HasRavenfieldResult, "Successful RF result did not enable open actions.");
        vm.CloseDialog();
        vm.NewProject();
        Require(!vm.HasRavenfieldResult, "New workspace kept previous result buttons.");
        vm.Animations.Add(new() { Name = "idle.cast" });
        vm.SelectedAnimation = vm.Animations[0];
        vm.SelectedRavenfieldAnimation = vm.Animations[0];
        var pending = new TaskCompletionSource<RavenfieldAdaptationResult>();
        vm.RavenfieldRunner = (_, _, _, _) => pending.Task;
        var run = vm.AdaptRavenfieldAsync();
        vm.NewProject();
        pending.SetResult(result);
        await run;
        Require(!vm.HasRavenfieldResult && !vm.IsDialogOpen && !vm.IsBusy, "In-flight RF result leaked into another workspace.");
    }

    private static void TestReferencePersistence(string folder)
    {
        var store = new WorkspaceProjectStore();
        using var vm = new MainWindowViewModel(new AnimationExportEngine(), store,
            new ApplicationPreferencesStore(Path.Combine(folder, "reference-preferences.json")), new EmptyPicker());
        vm.Animations.Add(new() { Name = "idle-first.cast" });
        vm.Animations.Add(new() { Name = "idle-second.cast" });
        vm.SelectPage(WorkspacePage.Settings);
        Require(!vm.HasRavenfieldReference, "Multiple clips silently chose an RF reference.");
        vm.SelectedRavenfieldAnimation = vm.Animations[1];
        vm.SelectedAnimation = vm.Animations[0];
        var expectedId = vm.Animations[1].Id;
        Require(vm.SelectedRavenfieldAnimation?.Id == expectedId, "Normal selection changed RF reference.");
        var project = Path.Combine(folder, "reference.aprj");
        store.Save(vm.Workspace, project);
        vm.NewProject();
        vm.LoadProject(project);
        Require(vm.SelectedAnimation == vm.Animations[0] && vm.SelectedRavenfieldAnimation == vm.Animations[1]
            && vm.Workspace.Ravenfield.ReferenceAnimationId == expectedId, "RF second-clip reference was not restored.");
        vm.SelectedAnimation = vm.Animations[0];
        Require(vm.SelectedRavenfieldAnimation?.Id == expectedId, "Ordinary selection changed restored RF reference.");
        vm.Animations.RemoveAt(1);
        Require(!vm.HasRavenfieldReference && vm.Workspace.Ravenfield.ReferenceAnimationId == expectedId,
            "Removed reference fell back or lost its saved ID.");
        store.Save(vm.Workspace, project);
        vm.LoadProject(project);
        vm.SelectPage(WorkspacePage.Settings);
        Require(!vm.HasRavenfieldReference && vm.SelectedRavenfieldAnimation is null,
            "Missing persisted reference silently selected another animation.");
        vm.NewProject();
        vm.Animations.Add(new() { Name = "weapon_fire.cast" });
        vm.Animations.Add(new() { Name = "weapon_IDLE.cast" });
        vm.SelectPage(WorkspacePage.Settings);
        Require(vm.SelectedRavenfieldAnimation == vm.Animations[1], "Unique idle was not suggested.");
        vm.SelectedAnimation = vm.Animations[0];
        vm.SelectPage(WorkspacePage.Settings);
        Require(vm.SelectedRavenfieldAnimation == vm.Animations[1], "Normal selection overrode the RF idle suggestion.");
    }

    private sealed class EmptyPicker : IWorkspaceFilePicker
    {
        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
}
