using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace AlchemyStars.Avalonia;

/// <summary>Explicit ammunition selection: extras and replica targets never opt in implicitly.</summary>
public sealed class ModelMergerAmmoWindow : Window
{
    private readonly ModelMergerView workspace;
    private string weapon = "", ammunition = "", output = "", inspectedWeapon = "";
    private string statusKey = "ammoHint", detail = "", stage = "";
    private string? result, source;
    private readonly List<Magazine> magazines = [];
    private readonly List<Target> extras = [], spares = [];
    private CancellationTokenSource? cancellation;
    private Task? activeTask;
    private TextBlock status = null!;
    private ProgressBar progress = null!;
    private bool closeAfterTask;
    private bool shuttingDown;
    public bool HasActiveTask => activeTask is { IsCompleted: false };
    private ModelMergerText Text => workspace.Text;

    public ModelMergerAmmoWindow(ModelMergerView workspace, string? initialWeapon)
    {
        this.workspace = workspace;
        Width = 780; Height = 800; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (initialWeapon is not null) SetWeapon(initialWeapon);
        Relocalize();
        Opened += (_, _) => { if (File.Exists(weapon)) Start(InspectAsync); };
        Closing += (_, e) =>
        {
            if (!HasActiveTask) return;
            e.Cancel = true; closeAfterTask = true; Cancel();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public void Cancel() => cancellation?.Cancel();
    internal void SetShuttingDown(bool value) { shuttingDown = value; IsEnabled = !value; }
    public Task WaitForCompletionAsync() => activeTask ?? Task.CompletedTask;
    public void Relocalize()
    {
        Title = Text["ammo"];
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        var body = new StackPanel { Spacing = 12 };
        AddPath(body, "weapon", weapon, value => { if (weapon != value) SetWeapon(value); }, async () =>
        {
            var path = (await ChooseFile()).FirstOrDefault();
            if (path is not null) { SetWeapon(path); Relocalize(); Start(InspectAsync); }
        });
        body.Children.Add(workspace.Button("inspect", () => Start(InspectAsync), enabled: !HasActiveTask));
        AddPath(body, "ammunition", ammunition, value => ammunition = value, async () =>
        {
            var path = (await ChooseFile()).FirstOrDefault(); if (path is not null) { ammunition = path; Relocalize(); }
        });
        AddPath(body, "output", output, value => output = value, async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Text["output"],
                SuggestedFileName = Path.GetFileName(output), DefaultExtension = "cast", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("CAST") { Patterns = ["*.cast"] }],
                SuggestedStartLocation = Directory.Exists(Path.GetDirectoryName(output)) ? await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(output)!) : null });
            if (file?.TryGetLocalPath() is { } path) { output = path; Relocalize(); }
        });
        body.Children.Add(workspace.Label("magazines"));
        if (magazines.Count == 0) body.Children.Add(workspace.Label("empty"));
        foreach (var magazine in magazines)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            var check = new CheckBox { Content = $"{magazine.Name} · {Text["slots"]}: {magazine.Slots.Length} / {magazine.Occupied.Length}", IsChecked = magazine.Selected, IsEnabled = !HasActiveTask };
            ToolTip.SetTip(check, string.Join(Environment.NewLine, magazine.Slots));
            check.IsCheckedChanged += (_, _) => magazine.Selected = check.IsChecked == true;
            row.Children.Add(check);
            var radio = new RadioButton { Content = Text["source"], GroupName = "MagazineLayout", IsChecked = source == magazine.Name, IsEnabled = !HasActiveTask };
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) source = magazine.Name; };
            Grid.SetColumn(radio, 1); row.Children.Add(radio); body.Children.Add(row);
        }
        AddTargets(body, "extras", extras); AddTargets(body, "spares", spares);
        body.Children.Add(workspace.Label("ammoHint"));
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var footer = new StackPanel { Spacing = 8 };
        status = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        progress = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100 };
        footer.Children.Add(status); footer.Children.Add(progress);
        var commands = new WrapPanel();
        commands.Children.Add(workspace.Button("ammo", () => Start(FillAsync), "primary", !HasActiveTask));
        commands.Children.Add(workspace.Button("cancel", Cancel, enabled: HasActiveTask));
        commands.Children.Add(workspace.Button("preview", () => { if (result is not null) workspace.RequestPreview(result); }, enabled: result is not null && File.Exists(result)));
        commands.Children.Add(workspace.Button("close", () => Close())); footer.Children.Add(commands);
        Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;
        UpdateStatus();
    }

    private void SetWeapon(string path)
    {
        weapon = path; inspectedWeapon = ""; magazines.Clear(); extras.Clear(); spares.Clear(); source = null; result = null;
        if (!string.IsNullOrWhiteSpace(path)) output = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "_filled.cast");
    }
    private void AddPath(StackPanel body, string key, string value, Action<string> changed, Func<Task> browse)
    {
        body.Children.Add(workspace.Label(key));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var input = workspace.Input(value, key, changed); input.IsEnabled = !HasActiveTask;
        row.Children.Add(input);
        var button = workspace.Button("browse", async () => { try { await browse(); } catch (Exception ex) { statusKey = "failed"; detail = ex.Message; UpdateStatus(); } }, enabled: !HasActiveTask);
        Grid.SetColumn(button, 1); row.Children.Add(button); body.Children.Add(row);
    }
    private void AddTargets(StackPanel body, string key, List<Target> targets)
    {
        var children = new StackPanel();
        foreach (var target in targets)
        {
            var check = new CheckBox { Content = target.Name, IsChecked = target.Selected, IsEnabled = !HasActiveTask };
            check.IsCheckedChanged += (_, _) => target.Selected = check.IsChecked == true; children.Children.Add(check);
        }
        body.Children.Add(new Expander { Header = Text[key] + $" · {targets.Count}", Content = children, HorizontalAlignment = HorizontalAlignment.Stretch });
    }
    private async Task<string[]> ChooseFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Text["browse"], AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CAST") { Patterns = ["*.cast"] }] });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }

    private void Start(Func<CancellationToken, Task> action)
    {
        if (HasActiveTask || shuttingDown) return;
        cancellation = new CancellationTokenSource();
        statusKey = "queued"; stage = ""; detail = "";
        // Defer execution so controls see the active task before rendering.
        activeTask = ExecuteAsync(action, cancellation.Token);
        Relocalize();
    }
    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        await Task.Yield();
        try { await action(token); }
        catch (OperationCanceledException) { statusKey = "cancelled"; detail = ""; }
        catch (Exception ex) { statusKey = "failed"; detail = ex.Message; }
        finally
        {
            cancellation?.Dispose(); cancellation = null;
            stage = "";
            // The task itself is not completed until this finally returns.
            activeTask = null;
            Relocalize();
            if (closeAfterTask) Close();
        }
    }
    private async Task InspectAsync(CancellationToken token)
    {
        if (!File.Exists(weapon)) { statusKey = "fileMissing"; detail = ""; return; }
        var selectedWeapon = weapon;
        var analysis = await workspace.Service.RunAsync(ModelMergerService.Json(w => { w.WriteString("command", "inspect_ammunition"); w.WriteString("file_path", selectedWeapon); }), OnProgress, null, token);
        magazines.Clear(); extras.Clear(); spares.Clear();
        foreach (var item in analysis.GetProperty("magazines").EnumerateArray())
            magazines.Add(new Magazine { Name = item.GetProperty("name").GetString()!, Slots = Strings(item, "slots"), Occupied = Strings(item, "occupied"), Selected = magazines.Count == 0 });
        extras.AddRange(Strings(analysis, "excluded_slots").Select(name => new Target { Name = name }));
        spares.AddRange(Strings(analysis, "spare_magazines").Select(name => new Target { Name = name }));
        source = magazines.FirstOrDefault()?.Name; inspectedWeapon = selectedWeapon;
        statusKey = "ready"; detail = "";
    }
    private async Task FillAsync(CancellationToken token)
    {
        if (inspectedWeapon != weapon || !File.Exists(weapon) || !File.Exists(ammunition) || string.IsNullOrWhiteSpace(output) || File.Exists(output)
            || !output.EndsWith(".cast", StringComparison.OrdinalIgnoreCase)
            || !magazines.Any(m => m.Selected) && !extras.Any(s => s.Selected) && !spares.Any(s => s.Selected)
            || spares.Any(s => s.Selected) && source is null)
        { statusKey = "ammoInvalid"; detail = ""; return; }
        result = null;
        var request = ModelMergerService.Json(w =>
        {
            w.WriteString("command", "fill_ammunition"); w.WriteString("weapon", weapon); w.WriteString("ammunition", ammunition); w.WriteString("output", output);
            ModelMergerService.Strings(w, "magazines", magazines.Where(m => m.Selected).Select(m => m.Name));
            ModelMergerService.Strings(w, "extra_slots", extras.Where(s => s.Selected).Select(s => s.Name));
            w.WriteStartArray("replicas");
            foreach (var spare in spares.Where(s => s.Selected)) { w.WriteStartObject(); w.WriteString("source", source); w.WriteString("target", spare.Name); w.WriteEndObject(); }
            w.WriteEndArray();
        });
        var filled = await workspace.Service.RunAsync(request, OnProgress, null, token, output);
        result = filled.GetProperty("output_path").GetString(); statusKey = "completed";
        detail = $"{filled.GetProperty("inserted")} / {filled.GetProperty("skipped")}\n{result}";
    }
    private void OnProgress(JsonElement data)
    {
        statusKey = "running";
        stage = data.TryGetProperty("stage", out var s) ? ModelMergerView.NormalizeStage(s.GetString() ?? "running") : "running";
        if (data.TryGetProperty("current", out var c) && data.TryGetProperty("total", out var t) && t.GetDouble() > 0)
            progress.Value = Math.Clamp(100 * c.GetDouble() / t.GetDouble(), 0, 100);
        UpdateStatus();
    }
    private void UpdateStatus() => status.Text = Text[statusKey] + (stage.Length > 0 ? " · " + Text[stage] : "") + (detail.Length > 0 ? "\n" + detail : "");

    internal async Task VerifyWorkflowAsync(string fixtureDirectory, string outputDirectory)
    {
        weapon = Path.Combine(fixtureDirectory, "weapon.cast"); ammunition = Path.Combine(fixtureDirectory, "ammo.cast");
        output = Path.Combine(outputDirectory, "ammunition-all.cast");
        Start(InspectAsync); await WaitForCompletionAsync();
        ModelMergerServiceSmoke.Require(statusKey == "ready", "Ammunition bone inspection failed: " + detail);
        ModelMergerServiceSmoke.Require(magazines.Count > 0 && magazines[0].Selected && magazines.Skip(1).All(m => !m.Selected), "Only the first magazine must be selected by default.");
        ModelMergerServiceSmoke.Require(extras.All(e => !e.Selected) && spares.All(s => !s.Selected), "Extra slots or spare magazines were silently opted in.");
        ModelMergerServiceSmoke.Require(source == "j_mag1", "The first magazine was not the replica source.");
        extras.Single(e => e.Name == "j_ammo_99").Selected = true;
        spares.Single(s => s.Name == "j_mag2").Selected = true;
        var originalLanguage = workspace.Language;
        for (var language = 0; language < 5; language++)
        {
            workspace.ChangeLanguage(language); Relocalize();
            ModelMergerServiceSmoke.Require(extras.Single(e => e.Name == "j_ammo_99").Selected && spares.Single(s => s.Name == "j_mag2").Selected && source == "j_mag1", "Live language switching lost ammunition selections.");
        }
        workspace.ChangeLanguage(originalLanguage); Relocalize();
        await Task.Delay(100);
        using (var screenshot = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height)))
        {
            screenshot.Render(this); screenshot.Save(Path.Combine(outputDirectory, "ammunition-selection.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        Start(FillAsync); await WaitForCompletionAsync();
        ModelMergerServiceSmoke.Require(statusKey == "completed" && result is not null && File.Exists(result), "Ammunition fill failed: " + detail);
        ModelMergerServiceSmoke.Require(detail.StartsWith("3 / 0", StringComparison.Ordinal), "Expected magazine + extra + replica (3 inserts): " + detail);
        var previous = File.ReadAllBytes(output);
        Start(FillAsync); await WaitForCompletionAsync();
        ModelMergerServiceSmoke.Require(statusKey == "ammoInvalid" && previous.SequenceEqual(File.ReadAllBytes(output)), "Ammunition filling overwrote an existing output.");
        output = Path.Combine(outputDirectory, "ammunition-extra-only.cast");
        foreach (var magazine in magazines) magazine.Selected = false;
        foreach (var spare in spares) spare.Selected = false;
        Start(FillAsync); await WaitForCompletionAsync();
        ModelMergerServiceSmoke.Require(statusKey == "completed" && detail.StartsWith("1 / 0", StringComparison.Ordinal), "Extra-only ammunition filling failed.");
        Start(InspectAsync);
        var closingTask = WaitForCompletionAsync();
        Close();
        await closingTask;
        ModelMergerServiceSmoke.Require(!HasActiveTask && !IsVisible && statusKey == "cancelled", "Closing an active ammunition window did not await cancellation.");
        Console.WriteLine("MODEL_MERGER_AMMO_UI_SMOKE_OK: defaults, source radio, extras, replicas, five languages, 3 inserted, no overwrite, extra-only fill.");
    }
    private static string[] Strings(JsonElement element, string key) => element.TryGetProperty(key, out var values) ? values.EnumerateArray().Select(v => v.GetString()!).ToArray() : [];
    private sealed class Magazine { public string Name = ""; public string[] Slots = [], Occupied = []; public bool Selected; }
    private sealed class Target { public string Name = ""; public bool Selected; }
}
