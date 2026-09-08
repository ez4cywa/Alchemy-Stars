namespace AlchemyStars.Avalonia;

internal static class OutputDirectorySmoke
{
    internal static async Task RunAsync(string directory)
    {
        var settings = Path.Combine(directory, "output-directory-settings.json");
        var unified = Path.Combine(directory, "unified-output");
        var original = Path.Combine(directory, "item-output");
        var preferences = new ApplicationPreferencesStore(settings);
        var engine = new CaptureEngine();
        var picker = new TestPicker { Folder = unified };
        var store = new WorkspaceProjectStore();
        using var vm = new MainWindowViewModel(engine, store, preferences, picker);
        Require(!vm.HasUnifiedOutputDirectory, "Unified output must be opt-in.");
        vm.Animations.Add(new WorkspaceAnimation { Name = "base.cast", OutputName = "first", OutputFolder = original });
        vm.SelectedAnimation = vm.Animations[0];
        vm.DualAnimations.Add(new WorkspaceDualAnimation { Name = "dual", OutputFolder = original });
        await vm.ExportAsync();
        Require(engine.Request!.Animations[0].OutputFolder == original, "Unset preferences changed an export path.");
        await vm.ChooseUnifiedOutputDirectoryAsync();
        Require(vm.HasUnifiedOutputDirectory && vm.UnifiedOutputDirectory == unified, "Directory selection was not applied.");
        vm.Animations.Add(new WorkspaceAnimation { Name = "later.cast", OutputName = "later" });
        await vm.ExportAsync();
        Require(engine.Request!.Animations.All(job => job.OutputFolder == unified), "Current and future animations were not redirected.");
        Require(new ApplicationPreferencesStore(settings).Snapshot().UnifiedOutputDirectory == unified, "Unified output did not persist across store restart.");
        using (var restarted = new MainWindowViewModel(engine, store, new ApplicationPreferencesStore(settings), picker))
            Require(restarted.UnifiedOutputDirectory == unified, "Restarted view model lost unified output.");
        var snapshot = WorkspaceProjectStore.Snapshot(vm.Workspace);
        vm.ApplyDualOutputDirectory(snapshot.DualAnimations.ToArray(), false, "unused");
        Require(snapshot.DualAnimations.All(task => task.OutputFolder == unified), "Dual export did not use unified output.");
        var cache = Path.Combine(directory, "preview-cache");
        vm.ApplyDualOutputDirectory(snapshot.DualAnimations.ToArray(), true, cache);
        Require(snapshot.DualAnimations.All(task => task.OutputFolder == cache), "Dual preview escaped its cache.");
        await vm.BuildPreviewAsync();
        Require(engine.Request!.Animations.Single().OutputFolder != unified
            && engine.Request.Animations.Single().OutputName == "composition", "Normal preview used the permanent export folder.");
        Require(vm.Animations[0].OutputFolder == original && vm.Animations[1].OutputFolder == string.Empty
            && vm.DualAnimations[0].OutputFolder == original, "Preference changed project folder fields.");
        var project = Path.Combine(directory, "output-directory-project.alchemystars");
        store.Save(vm.Workspace, project);
        var loaded = store.Load(project);
        Require(loaded.Animations[0].OutputFolder == original && loaded.DualAnimations[0].OutputFolder == original,
            "Saving a project serialized overridden paths.");
        vm.ClearUnifiedOutputDirectory();
        await vm.ExportAsync();
        Require(engine.Request!.Animations[0].OutputFolder == original && engine.Request.Animations[1].OutputFolder == string.Empty,
            "Clearing did not restore per-item paths.");
        snapshot = WorkspaceProjectStore.Snapshot(vm.Workspace);
        vm.ApplyDualOutputDirectory(snapshot.DualAnimations.ToArray(), false, cache);
        Require(snapshot.DualAnimations[0].OutputFolder == original, "Clearing did not restore dual paths.");
        Require(new ApplicationPreferencesStore(settings).Snapshot().UnifiedOutputDirectory == string.Empty, "Clear was not persisted.");
        picker.Pending = new TaskCompletionSource<string?>();
        var pending = vm.ChooseUnifiedOutputDirectoryAsync();
        vm.ClearUnifiedOutputDirectory();
        picker.Pending.SetResult(unified);
        await pending;
        Require(!vm.HasUnifiedOutputDirectory, "Pending selection undid a newer clear.");
        Console.WriteLine("Unified output: opt-in, choose, restart, all/new exports, dual, preview isolation, unchanged project, clear, selection race PASS");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class CaptureEngine : IAnimationExportEngine
    {
        public EngineCapabilities Capabilities => new AnimationExportEngine().Capabilities;
        public AnimationExportRequest? Request { get; private set; }
        public AnimationExportResult Export(AnimationExportRequest request)
        {
            Request = request;
            // Capture the real UI export/preview request without writing an asset or loading a renderer.
            throw new InvalidOperationException("Expected capture-only export.");
        }
    }

    private sealed class TestPicker : IWorkspaceFilePicker
    {
        public string? Folder { get; set; }
        public TaskCompletionSource<string?>? Pending { get; set; }
        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string? currentPath) => Pending?.Task ?? Task.FromResult(Folder);
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);
    }
}
