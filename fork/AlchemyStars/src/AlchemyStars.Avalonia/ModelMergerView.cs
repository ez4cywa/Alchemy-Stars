using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AlchemyStars.Avalonia;

/// <summary>Independent ModelMerger workspace. Navigation must retain this instance.</summary>
public sealed class ModelMergerView : UserControl
{
    private readonly ModelMergerService service = new();
    private readonly List<MergeGroup> groups = [];
    private readonly HashSet<Task> tasks = [];
    private readonly List<ModelMergerAmmoWindow> ammoWindows = [];
    private readonly List<ModelMergerAssemblyWindow> assemblyWindows = [];
    private readonly ModelMergerText text = new();
    private StackPanel groupList = null!;
    private TextBlock notice = null!;
    private string defaultFolder = "";
    private bool defaultManual;
    private bool defaultArmAssembly;
    private int nextId;
    private bool shuttingDown;
    private Task? shutdownTask;
    private string noticeKey = "defaults";
    private string noticeDetail = "";
    public event Action<string>? PreviewRequested;
    public event Action<int>? LanguageChanged;
    public int Language => text.Language;
    public bool HasActiveTasks => tasks.Count != 0 || ammoWindows.Any(w => w.HasActiveTask) || assemblyWindows.Any(w => w.HasActiveTask);
    internal int GroupCount => groups.Count;
    internal ModelMergerText Text => text;
    internal ModelMergerService Service => service;

    public ModelMergerView()
    {
        Focusable = true;
        text.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch { "en" => 1, "fr" => 2, "ru" => 3, "es" => 4, _ => 0 };
        LoadSettings();
        NewGroup(false);
        Render();
        KeyDown += async (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Control) return;
            try { switch (e.Key)
            {
                case Key.N: NewGroup(); break;
                case Key.S: SaveSettings(); break;
                case Key.Enter: RunAll(); break;
                case Key.O: await OpenPreview(); break;
                default: return;
            } }
            catch (Exception ex) { Report("failed", " · " + ex.Message); }
            e.Handled = true;
        };
        AcceptDrop(this, paths =>
        {
            var group = groups.FirstOrDefault(g => !g.Busy && g.Parts.Count == 0) ?? NewGroup(false);
            AddPaths(group, paths);
            RenderGroups();
        });
    }

    public void CancelAll()
    {
        foreach (var group in groups) group.Cancellation?.Cancel();
        foreach (var window in ammoWindows.ToArray()) window.Cancel();
        foreach (var window in assemblyWindows.ToArray()) window.Cancel();
    }

    public Task ShutdownAsync()
    {
        if (shutdownTask is { IsCompleted: false }) return shutdownTask;
        shuttingDown = true; IsEnabled = false;
        foreach (var window in ammoWindows) window.SetShuttingDown(true);
        foreach (var window in assemblyWindows) window.SetShuttingDown(true);
        return shutdownTask = ShutdownCoreAsync();
    }
    private async Task ShutdownCoreAsync()
    {
        try
        {
            CancelAll();
            await Task.WhenAll(tasks.ToArray().Concat(ammoWindows.Select(w => w.WaitForCompletionAsync())).Concat(assemblyWindows.Select(w => w.WaitForCompletionAsync())));
        }
        finally
        {
            foreach (var window in ammoWindows) window.SetShuttingDown(false);
            foreach (var window in assemblyWindows) window.SetShuttingDown(false);
            IsEnabled = true; shuttingDown = false;
        }
    }

    private void Render()
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(16) };
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Button("new", () => NewGroup()));
        toolbar.Children.Add(AsyncButton("openPreview", OpenPreview));
        toolbar.Children.Add(Button("ammo", () => OpenAmmo(null)));
        var armAssembly = new CheckBox { Content = text["armAssembly"], IsChecked = defaultArmAssembly, Margin = new Thickness(4, 0) };
        ToolTip.SetTip(armAssembly, text["armAssemblyHint"]);
        armAssembly.IsCheckedChanged += (_, _) => { defaultArmAssembly = armAssembly.IsChecked == true; SaveSettings(false); RenderGroups(); };
        toolbar.Children.Add(armAssembly);
        toolbar.Children.Add(Button("save", SaveSettings));
        toolbar.Children.Add(Button("reset", () => { defaultFolder = ""; defaultManual = false; ChangeLanguage(0); SaveSettings(false); }));
        toolbar.Children.Add(AsyncButton("about", About));
        var languages = new ComboBox { ItemsSource = ModelMergerText.Languages, SelectedIndex = text.Language, MinWidth = 138, Margin = new Thickness(4) };
        ToolTip.SetTip(languages, text["language"]);
        languages.SelectionChanged += (_, _) => { if (languages.SelectedIndex >= 0 && languages.SelectedIndex != text.Language) ChangeLanguage(languages.SelectedIndex); };
        toolbar.Children.Add(languages);
        layout.Children.Add(toolbar);
        var preview = new Border { Padding = new Thickness(14), Classes = { "subcard" }, Child = Label("previewDrop") };
        preview.PointerPressed += (_, _) => _ = SafelyAsync(OpenPreview);
        AcceptDrop(preview, paths => { foreach (var path in paths) RequestPreview(path); });
        Grid.SetRow(preview, 1); layout.Children.Add(preview);
        groupList = new StackPanel { Spacing = 14 };
        var scroll = new ScrollViewer { Content = groupList, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); layout.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 6 };
        var actions = new WrapPanel();
        actions.Children.Add(Button("runAll", RunAll, "primary"));
        actions.Children.Add(Button("cancelAll", CancelAll));
        actions.Children.Add(Label("hint"));
        footer.Children.Add(actions);
        notice = Label(noticeKey); notice.Text += noticeDetail;
        footer.Children.Add(notice);
        Grid.SetRow(footer, 3); layout.Children.Add(footer);
        Content = layout;
        RenderGroups();
    }

    private MergeGroup NewGroup(bool render = true)
    {
        var group = new MergeGroup { Id = ++nextId, Folder = defaultFolder, Manual = defaultManual };
        groups.Add(group);
        if (render) RenderGroups();
        return group;
    }

    internal void ChangeLanguage(int language)
    {
        text.Language = Math.Clamp(language, 0, 4); Render();
        foreach (var window in ammoWindows) window.Relocalize();
        foreach (var window in assemblyWindows) window.Relocalize();
        LanguageChanged?.Invoke(text.Language);
    }

    private void RenderGroups()
    {
        if (groupList is null) return;
        groupList.Children.Clear();
        foreach (var group in groups)
        {
            var body = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
            var commands = new WrapPanel();
            commands.Children.Add(AsyncButton("add", async () => { AddPaths(group, await PickFiles(group.RecentFolder)); RenderGroups(); }, enabled: !group.Busy && group.Parts.Count < 15));
            commands.Children.Add(Button("ammo", () => OpenAmmo(group), enabled: !group.Busy));
            if (defaultArmAssembly) commands.Children.Add(Button("armAssembly", () => OpenAssembly(group), enabled: !group.Busy));
            commands.Children.Add(Button("deleteGroup", () => { groups.Remove(group); RenderGroups(); }, enabled: !group.Busy));
            body.Children.Add(commands);
            var slots = new global::Avalonia.Controls.Primitives.UniformGrid { Columns = 5, Rows = 3 };
            for (var index = 0; index < 15; index++)
            {
                var slot = index;
                var card = new StackPanel { Spacing = 3 };
                if (index < group.Parts.Count)
                {
                    var file = group.Parts[index];
                    var fileName = new TextBlock { Text = Path.GetFileName(file), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
                    ToolTip.SetTip(fileName, file); card.Children.Add(fileName);
                    var actions = new WrapPanel();
                    actions.Children.Add(Button("preview", () => RequestPreview(file)));
                    actions.Children.Add(AsyncButton("replace", async () =>
                    {
                        var selected = (await PickFiles(group.RecentFolder, false)).FirstOrDefault();
                        if (selected is not null && !group.Parts.Contains(selected, StringComparer.OrdinalIgnoreCase))
                        {
                            ReplacePart(group, slot, selected); RenderGroups();
                        }
                    }, enabled: !group.Busy));
                    actions.Children.Add(Button("remove", () => { RemovePart(group, slot); RenderGroups(); }, enabled: !group.Busy));
                    card.Children.Add(actions);
                    card.Children.Add(Button(group.Root == file && group.Manual ? "rootSelected" : "root", () => { group.Root = file; group.Manual = true; RenderGroups(); }, enabled: !group.Busy));
                }
                else card.Children.Add(AsyncButton("add", async () => { AddPaths(group, await PickFiles(group.RecentFolder)); RenderGroups(); }, enabled: !group.Busy));
                var tile = new Border { Classes = { "subcard" }, Padding = new Thickness(8), Margin = new Thickness(3), Child = card, MinHeight = 110 };
                slots.Children.Add(tile);
            }
            body.Children.Add(slots);
            var rootMode = new ComboBox { ItemsSource = new[] { text["auto"], text["manual"] }, SelectedIndex = group.Manual ? 1 : 0, IsEnabled = !group.Busy, HorizontalAlignment = HorizontalAlignment.Stretch };
            rootMode.SelectionChanged += (_, _) => group.Manual = rootMode.SelectedIndex == 1;
            body.Children.Add(rootMode);
            var output = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var folder = Input(group.Folder, "folder", value => group.Folder = value); folder.IsEnabled = !group.Busy;
            output.Children.Add(folder);
            var browse = AsyncButton("browse", async () => { var path = await PickFolder(group.Folder); if (path is not null) { group.Folder = path; RenderGroups(); } }, enabled: !group.Busy);
            Grid.SetColumn(browse, 1); output.Children.Add(browse);
            body.Children.Add(Label("folder")); body.Children.Add(output);
            body.Children.Add(Label("name"));
            var name = Input(group.OutputName, "name", value => group.OutputName = value); name.IsEnabled = !group.Busy; group.NameInput = name; body.Children.Add(name);
            var taskCommands = new WrapPanel();
            taskCommands.Children.Add(Button("run", () => Start(group), "primary", !group.Busy));
            taskCommands.Children.Add(Button("cancel", () => group.Cancellation?.Cancel(), enabled: group.Busy));
            taskCommands.Children.Add(Button("preview", () => { if (group.Result is not null) RequestPreview(group.Result); }, enabled: group.Result is not null && File.Exists(group.Result)));
            body.Children.Add(taskCommands);
            group.StatusLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };
            group.ProgressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5 };
            body.Children.Add(group.StatusLabel); body.Children.Add(group.ProgressBar);
            group.LogBox = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 160 };
            body.Children.Add(new Expander { Header = text["logs"], Content = group.LogBox, HorizontalAlignment = HorizontalAlignment.Stretch });
            var expander = new Expander { Header = $"{text["group"]} {group.Id} · {group.Parts.Count}/15", Content = body, IsExpanded = group.Expanded, HorizontalAlignment = HorizontalAlignment.Stretch };
            expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) group.Expanded = expander.IsExpanded; };
            var panel = new Border { Classes = { "workspace-panel" }, Child = expander };
            AcceptDrop(panel, paths => { if (!group.Busy) { AddPaths(group, paths); RenderGroups(); } });
            groupList.Children.Add(panel);
            UpdateStatus(group);
        }
    }

    private void AddPaths(MergeGroup group, IEnumerable<string> paths)
    {
        if (group.Busy) return;
        var skipped = false;
        foreach (var path in paths)
        {
            if (!path.EndsWith(".cast", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
            var full = Path.GetFullPath(path);
            if (group.Parts.Count >= 15 || group.Parts.Contains(full, StringComparer.OrdinalIgnoreCase)) { skipped = true; continue; }
            group.Parts.Add(full); group.RecentFolder = Path.GetDirectoryName(full) ?? "";
            if (string.IsNullOrWhiteSpace(group.Folder)) group.Folder = group.RecentFolder;
        }
        group.Result = null;
        group.Status = group.Parts.Count >= 2 ? "ready" : "idle";
        if (skipped) Report("capacity");
    }

    private static void ReplacePart(MergeGroup group, int index, string selected)
    {
        if (group.Busy || group.Parts.Contains(selected, StringComparer.OrdinalIgnoreCase)) return;
        var previous = group.Parts[index]; group.Parts[index] = selected;
        if (group.Root == previous) group.Root = selected;
        group.RecentFolder = Path.GetDirectoryName(selected) ?? ""; group.Result = null;
    }
    private static void RemovePart(MergeGroup group, int index)
    {
        if (group.Busy) return;
        var previous = group.Parts[index]; group.Parts.RemoveAt(index);
        if (group.Root == previous) group.Root = null;
        group.Result = null;
    }

    internal void VerifyEditingWorkflow(string fixture, string outputDirectory)
    {
        var originalLanguage = text.Language;
        var initialGroups = groups.Count;
        var group = NewGroup(false);
        var part0 = Path.Combine(fixture, "part-00.cast"); var part1 = Path.Combine(fixture, "part-01.cast");
        AddPaths(group, [part0, part1, part0]);
        ModelMergerServiceSmoke.Require(group.Parts.Count == 2 && IsReady(group), "Adding distinct CAST parts failed.");
        group.Root = part0; group.Manual = true;
        RemovePart(group, 1);
        ReplacePart(group, 0, part1);
        ModelMergerServiceSmoke.Require(group.Root == part1 && group.Parts[0] == part1, "Replacing the manual root did not update the root.");
        RemovePart(group, 0);
        ModelMergerServiceSmoke.Require(group.Root is null && !IsReady(group), "Removing the root left a runnable invalid plan.");
        AddPaths(group, [part0, part1]); group.Root = part0; group.Folder = outputDirectory;
        group.Expanded = false;
        foreach (var language in Enumerable.Range(0, 5))
        {
            text.Language = language; Render();
            ModelMergerServiceSmoke.Require(group.Parts.Count == 2 && group.Root == part0 && !group.Expanded, "Language change lost group state.");
            ModelMergerServiceSmoke.Require(text["run"] != "run" && text["ammo"] != "ammo", "Missing language text.");
        }
        text.Language = originalLanguage;
        SaveSettings(false);
        var settings = File.ReadAllText(SettingsPath);
        ModelMergerServiceSmoke.Require(!settings.Contains("part-00.cast", StringComparison.Ordinal) && !settings.Contains("part-01.cast", StringComparison.Ordinal), "Settings stored selected files.");
        groups.Remove(group); Render();
        ModelMergerServiceSmoke.Require(groups.Count == initialGroups, "Smoke group cleanup failed.");
    }

    internal async Task VerifyShutdownAsync(string fixture, string outputDirectory)
    {
        var pending = new List<MergeGroup>();
        for (var index = 0; index < 3; index++)
        {
            var group = NewGroup(false); group.Manual = false;
            AddPaths(group, [Path.Combine(fixture, "part-00.cast"), Path.Combine(fixture, "part-01.cast")]);
            group.Folder = outputDirectory; group.OutputName = $"shutdown-{index}.cast";
            pending.Add(group);
        }
        Start(pending[0]); Start(pending[1]);
        var shutdown = ShutdownAsync();
        var sameShutdown = ShutdownAsync();
        ModelMergerServiceSmoke.Require(ReferenceEquals(shutdown, sameShutdown) && !IsEnabled, "Concurrent shutdown was not shared or workspace remained enabled.");
        Start(pending[2]);
        ModelMergerServiceSmoke.Require(!pending[2].Busy, "Shutdown accepted a new merge task.");
        await shutdown;
        ModelMergerServiceSmoke.Require(!HasActiveTasks && IsEnabled, "Shutdown left tasks running or failed to restore interaction.");
        foreach (var group in pending) groups.Remove(group);
        RenderGroups();
        Console.WriteLine("MODEL_MERGER_SHUTDOWN_SMOKE_OK: shared shutdown, no new jobs, cancellation awaited, interaction restored.");
    }

    private void RunAll()
    {
        foreach (var group in groups.Where(g => !g.Busy && IsReady(g)).ToArray()) Start(group);
    }

    private static bool IsReady(MergeGroup group) => group.Parts.Count is >= 2 and <= 15 && group.Parts.All(File.Exists)
        && !string.IsNullOrWhiteSpace(group.Folder) && (!group.Manual || group.Root is not null && group.Parts.Contains(group.Root));

    private void Start(MergeGroup group)
    {
        if (group.Busy || shuttingDown) return;
        if (!IsReady(group)) { group.Status = "invalid"; group.Detail = ""; UpdateStatus(group); return; }
        group.Cancellation = new CancellationTokenSource();
        group.Status = "queued"; group.Detail = ""; group.Result = null; group.Progress = 0; group.Log.Clear();
        // Automatic names derive from the weapon code and never overwrite: conflicts
        // get the differing-segment prefix, then a numeric ladder (see ModelMergerNaming).
        if (string.IsNullOrWhiteSpace(group.OutputName) && !string.IsNullOrWhiteSpace(group.Folder))
        {
            group.OutputName = ModelMergerNaming.MergeOutputName(group.Folder, group.Parts);
            var nameInput = group.NameInput;
            if (nameInput is not null) nameInput.Text = group.OutputName;
        }
        var request = ModelMergerService.Json(w =>
        {
            w.WriteString("command", "merge"); ModelMergerService.Strings(w, "input_files", group.Parts);
            w.WriteString("output_directory", group.Folder); w.WriteString("output_file_name", string.IsNullOrWhiteSpace(group.OutputName) ? null : group.OutputName);
            w.WriteString("manual_root_file", group.Manual ? group.Root : null); w.WriteBoolean("overwrite", true);
        });
        RenderGroups();
        var task = Execute(group, request, group.Cancellation.Token);
        tasks.Add(task);
        _ = RemoveWhenDone(task);
    }

    private async Task RemoveWhenDone(Task task) { try { await task; } finally { tasks.Remove(task); } }
    private async Task Execute(MergeGroup group, string request, CancellationToken token)
    {
        try
        {
            var result = await service.RunAsync(request, data =>
            {
                group.Status = "running";
                var stage = data.TryGetProperty("stage", out var s) ? s.GetString() ?? "running" : "running";
                group.Stage = NormalizeStage(stage);
                var item = data.TryGetProperty("item", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : "";
                var current = data.TryGetProperty("current", out var c) ? c.GetDouble() : 0;
                var total = data.TryGetProperty("total", out var t) ? t.GetDouble() : 0;
                group.Progress = total > 0 ? Math.Clamp(100 * current / total, 0, 100) : 0;
                group.Detail = item;
                if (group.Log.Count == 0 || group.Log[^1].Key != group.Stage) group.Log.Add((group.Stage, item));
                UpdateStatus(group);
            }, ConfirmOverwrite, token);
            group.Result = result.GetProperty("output_path").GetString(); group.Status = "completed"; group.Stage = ""; group.Progress = 100;
            group.Detail = group.Result ?? "";
            if (result.TryGetProperty("warnings", out var warnings)) foreach (var warning in warnings.EnumerateArray()) group.Log.Add(("", warning.ToString()));
            group.Log.Add(("completed", group.Detail));
        }
        catch (OperationCanceledException) { group.Status = "cancelled"; group.Detail = ""; group.Stage = ""; }
        catch (Exception ex) { group.Status = "failed"; group.Detail = ex.Message; group.Stage = ""; group.Log.Add(("failed", ex.Message)); }
        finally { group.Cancellation?.Dispose(); group.Cancellation = null; RenderGroups(); }
    }

    internal static string NormalizeStage(string value) => value.Replace("SelectingRoot", "selecting_root", StringComparison.Ordinal).ToLowerInvariant();
    private void UpdateStatus(MergeGroup group)
    {
        if (group.StatusLabel is null) return;
        group.StatusLabel.Text = text[group.Status] + (group.Stage.Length > 0 ? $" · {text[group.Stage]} · {group.Progress:0}%" : "") + (group.Detail.Length > 0 ? "\n" + group.Detail : "");
        group.ProgressBar!.Value = group.Progress;
        group.LogBox!.Text = string.Join(Environment.NewLine, group.Log.Select(l => (l.Key.Length > 0 ? text[l.Key] + " · " : "") + l.Detail));
    }

    internal async Task<bool> ConfirmOverwrite(string output, CancellationToken cancellationToken)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;
        var dialog = new Window { Title = text["overwrite"], Width = 520, Height = 240, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = true };
        var content = new StackPanel { Spacing = 16, Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = text["overwrite"] + "\n" + output, TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel();
        buttons.Children.Add(Button("cancel", () => dialog.Close(false)));
        buttons.Children.Add(Button("yes", () => dialog.Close(true), "primary"));
        content.Children.Add(buttons); dialog.Content = content;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(false); };
        var confirmation = dialog.ShowDialog<bool>(owner);
        using var cancellation = cancellationToken.Register(() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await confirmation;
    }

    private async Task OpenPreview() { foreach (var path in await PickFiles("")) RequestPreview(path); }
    internal void RequestPreview(string path)
    {
        if (File.Exists(path)) PreviewRequested?.Invoke(path);
        else Report("fileMissing", " · " + path);
    }
    private void OpenAmmo(MergeGroup? group)
    {
        if (shuttingDown) return;
        var weapon = group?.Result is { } result && File.Exists(result) ? result : group?.Root ?? group?.Parts.FirstOrDefault();
        var window = new ModelMergerAmmoWindow(this, weapon);
        ammoWindows.Add(window); window.Closed += (_, _) => ammoWindows.Remove(window);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner); else window.Show();
    }
    private void OpenAssembly(MergeGroup? group)
    {
        if (shuttingDown) return;
        var weapon = group?.Result is { } result && File.Exists(result) ? result : group?.Root ?? group?.Parts.FirstOrDefault();
        var window = new ModelMergerAssemblyWindow(this, weapon);
        assemblyWindows.Add(window); window.Closed += (_, _) => assemblyWindows.Remove(window);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner); else window.Show();
    }

    internal async Task<string[]> PickFiles(string recent, bool multiple = true)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return [];
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = text["add"], AllowMultiple = multiple,
            SuggestedStartLocation = Directory.Exists(recent) ? await storage.TryGetFolderFromPathAsync(recent) : null,
            FileTypeFilter = [new FilePickerFileType("CAST") { Patterns = ["*.cast"] }] });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }
    private async Task<string?> PickFolder(string recent)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = text["folder"], AllowMultiple = false,
            SuggestedStartLocation = Directory.Exists(recent) ? await storage.TryGetFolderFromPathAsync(recent) : null });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    internal Button Button(string key, Action action, string style = "secondary", bool enabled = true)
    {
        var button = new Button { Content = text[key], Margin = new Thickness(3), Padding = new Thickness(10, 6), MinHeight = 32, IsEnabled = enabled };
        button.Classes.Add(style); button.Click += (_, _) => action();
        global::Avalonia.Automation.AutomationProperties.SetName(button, text[key]);
        return button;
    }
    internal Button AsyncButton(string key, Func<Task> action, string style = "secondary", bool enabled = true) => Button(key, () => _ = SafelyAsync(action), style, enabled);
    private async Task SafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Report("failed", " · " + ex.Message); }
    }
    internal TextBlock Label(string key) => new() { Text = text[key], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    internal TextBox Input(string value, string key, Action<string> changed)
    {
        var input = new TextBox { Text = value, MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
        global::Avalonia.Automation.AutomationProperties.SetName(input, text[key]);
        input.TextChanged += (_, _) => changed(input.Text ?? ""); return input;
    }
    private void Report(string key, string detail = "") { noticeKey = key; noticeDetail = detail; if (notice is not null) notice.Text = text[key] + detail; }
    private static void AcceptDrop(Control control, Action<string[]> action)
    {
        DragDrop.SetAllowDrop(control, true);
        control.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        control.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Where(p => p.EndsWith(".cast", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (paths.Length > 0) action(paths); e.Handled = true;
        });
    }

    private static string SettingsPath
    {
        get
        {
            var hostSettings = Environment.GetEnvironmentVariable("ALCHEMY_STARS_SETTINGS_PATH");
            return !string.IsNullOrWhiteSpace(hostSettings) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(hostSettings))!, "model-merger-settings.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alchemy Stars", "model-merger-settings.json");
        }
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath)); var root = json.RootElement;
            if (root.TryGetProperty("language", out var l)) text.Language = Math.Clamp(l.GetInt32(), 0, 4);
            if (root.TryGetProperty("output_directory", out var f)) defaultFolder = f.GetString() ?? "";
            if (root.TryGetProperty("manual_root", out var m)) defaultManual = m.GetBoolean();
            if (root.TryGetProperty("arm_assembly", out var a)) defaultArmAssembly = a.GetBoolean();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { Report("settingsError", " · " + ex.Message); }
    }
    private void SaveSettings() => SaveSettings(true);
    private void SaveSettings(bool copyGroup)
    {
        try
        {
            if (copyGroup && groups.FirstOrDefault() is { } group) { defaultFolder = group.Folder; defaultManual = group.Manual; }
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, ModelMergerService.Json(w => { w.WriteNumber("language", text.Language); w.WriteString("output_directory", defaultFolder); w.WriteBoolean("manual_root", defaultManual); w.WriteBoolean("arm_assembly", defaultArmAssembly); }));
            File.Move(temp, SettingsPath, true); Report("saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report("settingsError", " · " + ex.Message); }
    }
    private async Task About()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var window = new Window { Title = text["about"], Width = 610, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 16, Margin = new Thickness(24) }; body.Children.Add(Label("aboutText"));
        var links = new WrapPanel();
        links.Children.Add(Button("repository", () => Launch("https://github.com/ez4cywa/ModelMergerGUI")));
        links.Children.Add(Button("issues", () => Launch("https://github.com/ez4cywa/Alchemy-Stars/issues")));
        links.Children.Add(Button("releases", () => Launch("https://github.com/ez4cywa/Alchemy-Stars/releases")));
        links.Children.Add(AsyncButton("copy", async () => { if (owner.Clipboard is { } clipboard) { await clipboard.SetTextAsync("https://github.com/ez4cywa/ModelMergerGUI"); Report("copied"); } }));
        body.Children.Add(links); body.Children.Add(Button("close", () => window.Close())); window.Content = body;
        await window.ShowDialog(owner);
    }
    private void Launch(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Report("failed", " · " + ex.Message); } }

    private sealed class MergeGroup
    {
        public int Id;
        public List<string> Parts = [];
        public string Folder = "", OutputName = "", RecentFolder = "", Status = "idle", Stage = "", Detail = "";
        public string? Root, Result;
        public bool Manual, Expanded = true;
        public double Progress;
        public CancellationTokenSource? Cancellation;
        public bool Busy => Cancellation is not null;
        public TextBlock? StatusLabel;
        public ProgressBar? ProgressBar;
        public TextBox? LogBox, NameInput;
        public List<(string Key, string Detail)> Log = [];
    }
}
