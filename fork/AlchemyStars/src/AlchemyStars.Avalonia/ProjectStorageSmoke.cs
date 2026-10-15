namespace AlchemyStars.Avalonia;

internal static class ProjectStorageSmoke
{
    internal static void Run(string directory)
    {
        var folder = Path.Combine(directory, "project-storage");
        Directory.CreateDirectory(folder);
        var failures = new List<string>();
        Check("project null entries", () => VerifyProjectEntries(folder));
        Check("failed project load", () => VerifyFailedProjectLoad(folder));
        Check("settings read recovery", () => VerifyReadRecovery(folder, editWhileLocked: false));
        Check("settings edits during read failure", () => VerifyReadRecovery(folder, editWhileLocked: true));
        Check("settings write failure", () => VerifyWriteFailure(folder));
        if (failures.Count > 0)
            throw new InvalidOperationException("Storage regression failures: " + string.Join("; ", failures));
        Console.WriteLine("Project storage: null validation, failed-load isolation, settings read recovery and in-memory write failures PASS");

        void Check(string name, Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); }
        }
    }

    private static void VerifyProjectEntries(string folder)
    {
        var store = new WorkspaceProjectStore();
        foreach (var (name, json) in new[]
        {
            ("parts", """{"Parts":[null]}"""),
            ("animations", """{"Animations":[null]}"""),
            ("dual", """{"SchemaVersion":2,"DualAnimations":[null]}"""),
            ("layers", """{"Animations":[{"Layers":[null]}]}"""),
        })
        {
            var path = Path.Combine(folder, name + ".aprj");
            File.WriteAllText(path, json);
            try { store.Load(path); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Project with a null " + name + " entry was accepted.");
        }

        var compatible = Path.Combine(folder, "null-containers.aprj");
        File.WriteAllText(compatible, """{"Parts":null,"Animations":null,"DualAnimations":null}""");
        var document = store.Load(compatible);
        Require(document.Parts.Count == 0 && document.Animations.Count == 0 && document.DualAnimations.Count == 0,
            "Legacy null project containers stopped normalizing to empty collections.");
        File.WriteAllText(compatible, """{"Animations":[{"Layers":null}]}""");
        document = store.Load(compatible);
        Require(document.Animations.Single().Layers.Count == 0,
            "A legacy null layer container stopped normalizing to an empty collection.");
        File.WriteAllText(compatible, """{"SchemaVersion":2,"DualAnimations":[{"LeftAnimationId":null,"RightAnimationId":null}]}""");
        document = store.Load(compatible);
        Require(document.DualAnimations[0].LeftAnimationId == string.Empty && document.DualAnimations[0].RightAnimationId == string.Empty,
            "Unset dual task references stopped normalizing to empty identifiers.");
    }

    private static void VerifyFailedProjectLoad(string folder)
    {
        var store = new WorkspaceProjectStore();
        using var vm = new MainWindowViewModel(new AnimationExportEngine(), store,
            new ApplicationPreferencesStore(Path.Combine(folder, "project-settings.json")), new TestPicker());
        vm.Animations.Add(new WorkspaceAnimation { Name = "sentinel.cast", OutputName = "keep-me" });
        var originalPath = Path.Combine(folder, "original.aprj");
        store.Save(vm.Workspace, originalPath);
        vm.LoadProject(originalPath);
        var originalWorkspace = vm.Workspace;
        var originalSelection = vm.SelectedAnimation;
        var originalBytes = File.ReadAllBytes(originalPath);
        var invalidPath = Path.Combine(folder, "invalid-dual.aprj");
        File.WriteAllText(invalidPath, """{"SchemaVersion":2,"DualAnimations":[null]}""");
        vm.LoadProject(invalidPath);
        Require(vm.IsDialogOpen && vm.DialogIsError, "Invalid project load did not report an error.");
        Require(ReferenceEquals(vm.Workspace, originalWorkspace) && ReferenceEquals(vm.SelectedAnimation, originalSelection)
            && vm.CurrentProjectPath == originalPath && vm.Animations.Single().OutputName == "keep-me",
            "Failed project load replaced the existing workspace, selection or save path.");
        vm.SaveProjectAsync(false).GetAwaiter().GetResult();
        Require(originalBytes.SequenceEqual(File.ReadAllBytes(originalPath)), "Saving after a failed load replaced the original project.");
    }

    private static void VerifyReadRecovery(string folder, bool editWhileLocked)
    {
        var path = Path.Combine(folder, editWhileLocked ? "locked-read-edits.json" : "locked-read.json");
        SeedSettings(path, folder);
        var originalBytes = File.ReadAllBytes(path);
        var preferences = new ApplicationPreferencesStore(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            _ = preferences.Snapshot();
            if (editWhileLocked)
            {
                var defaults = WorkspaceDocument.Create(".smd", castAnimationOnly: true, bakeRelevantBonesOnly: true);
                defaults.OutputUpAxis = "y";
                preferences.SaveDefaults("fr-FR", defaults);
                defaults.OutputFormat = ".seanim";
                preferences.SaveLanguage("system");
                preferences.SaveAppearance("classic-apple", "dark");
                preferences.RememberDirectory("output", folder);
                var snapshot = preferences.Snapshot();
                Require(snapshot.Language == "system" && snapshot.ThemeStyle == "classic-apple" && snapshot.ThemeMode == "dark",
                    "Settings read failure blocked in-memory preference edits.");
            }
        }
        Require(originalBytes.SequenceEqual(File.ReadAllBytes(path)), "Unreadable settings were replaced before recovery.");
        if (!editWhileLocked)
            preferences.SaveAppearance("classic-apple", "dark");
        else
            preferences.SaveAutoUpdate(true);
        var reloaded = new ApplicationPreferencesStore(path).Snapshot();
        Require(reloaded.Language == (editWhileLocked ? "system" : "en-US")
            && reloaded.SavedArmsPath == Path.Combine(folder, "remembered.cast")
            && reloaded.UnifiedOutputDirectory == folder && reloaded.DefaultOutputFormat == (editWhileLocked ? ".smd" : ".fbx")
            && reloaded.LastDirectories["project"] == folder,
            "A transient settings read failure overwrote unrelated persisted preferences.");
        if (editWhileLocked)
            Require(reloaded.DefaultOutputUpAxis == "y" && reloaded.DefaultCastAnimationOnly && reloaded.DefaultBakeRelevantBonesOnly
                && reloaded.LastDirectories["output"] == folder,
                "Recovered settings lost pending defaults, captured mutable project state or replaced remembered scopes.");
        Require(reloaded.ThemeStyle == "classic-apple" && reloaded.ThemeMode == "dark"
            && reloaded.AutoUpdateEnabled == editWhileLocked,
            "Settings recovery discarded preference edits made during the read failure.");
    }

    private static void VerifyWriteFailure(string folder)
    {
        var path = Path.Combine(folder, "locked-write.json");
        SeedSettings(path, folder);
        var originalBytes = File.ReadAllBytes(path);
        var preferences = new ApplicationPreferencesStore(path);
        _ = preferences.Snapshot();
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            preferences.SaveLanguage("fr-FR");
            Require(preferences.Snapshot().Language == "fr-FR", "Settings write failure lost the in-memory update.");
        }
        Require(originalBytes.SequenceEqual(File.ReadAllBytes(path)), "Failed settings write changed the existing file.");
        preferences.SaveAutoUpdate(true);
        var reloaded = new ApplicationPreferencesStore(path).Snapshot();
        Require(reloaded.Language == "fr-FR" && reloaded.AutoUpdateEnabled && reloaded.UnifiedOutputDirectory == folder,
            "Settings write retry lost in-memory changes or unrelated persisted preferences.");
        Require(!Directory.EnumerateFiles(folder, "*.settings.tmp").Any(), "Settings write failure left temporary files.");
    }

    private static void SeedSettings(string path, string folder)
    {
        var preferences = new ApplicationPreferencesStore(path);
        preferences.SaveLanguage("en-US");
        preferences.SaveArmsPath(Path.Combine(folder, "remembered.cast"));
        preferences.SaveUnifiedOutputDirectory(folder);
        preferences.SaveDefaults("en-US", WorkspaceDocument.Create(".fbx"));
        preferences.RememberDirectory("project", folder);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class TestPicker : IWorkspaceFilePicker
    {
        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult(currentPath);
        public Task<string?> PickFolderAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);
    }
}
