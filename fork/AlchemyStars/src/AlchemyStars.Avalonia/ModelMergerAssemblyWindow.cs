using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace AlchemyStars.Avalonia;

/// <summary>Arm + weapon assembly: the weapon root bone splices onto the arms' tag_weapon.</summary>
public sealed class ModelMergerAssemblyWindow : Window
{
    private readonly ModelMergerView workspace;
    private string arms = "", weapon = "", output = "";
    private string statusKey = "assemblyHint", detail = "", stage = "";
    private string? result;
    private readonly List<string> bones = [];
    private string? target;
    private string inspectedArms = "";
    private int attachedMeshes;
    private CancellationTokenSource? cancellation;
    private Task? activeTask;
    private TextBlock status = null!;
    private ProgressBar progress = null!;
    private bool closeAfterTask;
    private bool shuttingDown;
    public bool HasActiveTask => activeTask is { IsCompleted: false };
    private ModelMergerText Text => workspace.Text;

    public ModelMergerAssemblyWindow(ModelMergerView workspace, string? initialWeapon)
    {
        this.workspace = workspace;
        Width = 780; Height = 720; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (initialWeapon is not null) SetWeapon(initialWeapon);
        Relocalize();
        Closing += (_, e) =>
        {
            if (!HasActiveTask) return;
            e.Cancel = true; closeAfterTask = true; Cancel();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !HasActiveTask) Close(); };
    }

    public void Cancel() => cancellation?.Cancel();
    internal void SetShuttingDown(bool value) { shuttingDown = value; IsEnabled = !value; }
    public Task WaitForCompletionAsync() => activeTask ?? Task.CompletedTask;

    public void Relocalize()
    {
        Title = Text["armAssembly"];
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(workspace.Label("assemblyHint"));
        AddPath(body, "arms", arms, value => { if (arms != value) SetArms(value); }, async () =>
        {
            var path = (await ChooseFile()).FirstOrDefault();
            if (path is not null) { SetArms(path); Relocalize(); }
        });
        AddPath(body, "weaponModel", weapon, value => { if (weapon != value) SetWeapon(value); }, async () =>
        {
            var path = (await ChooseFile()).FirstOrDefault();
            if (path is not null) { SetWeapon(path); Relocalize(); }
        });
        AddPath(body, "output", output, value => output = value, async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Text["output"],
                SuggestedFileName = Path.GetFileName(output), DefaultExtension = "cast", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("CAST") { Patterns = ["*.cast"] }],
                SuggestedStartLocation = Directory.Exists(Path.GetDirectoryName(output)) ? await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(output)!) : null });
            if (file?.TryGetLocalPath() is { } path) { output = path; Relocalize(); }
        });
        body.Children.Add(workspace.Label("targetBone"));
        var targets = new List<string> { Text["autoBone"] };
        targets.AddRange(bones);
        var combo = new ComboBox { ItemsSource = targets, IsEnabled = !HasActiveTask, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (target is not null && bones.Contains(target)) combo.SelectedIndex = bones.IndexOf(target) + 1; else combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) => target = combo.SelectedIndex > 0 ? bones[combo.SelectedIndex - 1] : null;
        body.Children.Add(combo);
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var footer = new StackPanel { Spacing = 8 };
        status = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        progress = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100 };
        footer.Children.Add(status); footer.Children.Add(progress);
        var commands = new WrapPanel();
        commands.Children.Add(workspace.Button("armAssembly", () => Start(AssembleAsync), "primary", !HasActiveTask));
        commands.Children.Add(workspace.Button("inspect", () => Start(InspectAsync), enabled: !HasActiveTask));
        commands.Children.Add(workspace.Button("cancel", Cancel, enabled: HasActiveTask));
        commands.Children.Add(workspace.Button("preview", () => { if (result is not null) workspace.RequestPreview(result); }, enabled: result is not null && File.Exists(result)));
        commands.Children.Add(workspace.Button("close", () => Close()));
        footer.Children.Add(commands);
        Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;
        UpdateStatus();
    }

    private void SetArms(string path)
    {
        arms = path; inspectedArms = ""; bones.Clear(); target = null; result = null;
        if (string.IsNullOrWhiteSpace(weapon) && !string.IsNullOrWhiteSpace(path))
            SetWeapon(path);
    }
    private void SetWeapon(string path)
    {
        weapon = path; result = null;
        if (!string.IsNullOrWhiteSpace(path))
            output = ModelMergerNaming.AssemblyOutputName(Path.GetDirectoryName(path) ?? "", path);
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
            activeTask = null;
            Relocalize();
            if (closeAfterTask) Close();
        }
    }
    private async Task InspectAsync(CancellationToken token)
    {
        if (!File.Exists(arms)) { statusKey = "fileMissing"; detail = ""; return; }
        var selectedArms = arms;
        var analysis = await workspace.Service.RunAsync(ModelMergerService.Json(w => { w.WriteString("command", "inspect_arms"); w.WriteString("file_path", selectedArms); }), OnProgress, null, token);
        bones.Clear();
        bones.AddRange(analysis.TryGetProperty("bones", out var list) ? list.EnumerateArray().Select(v => v.GetString() ?? "") : []);
        target = null; inspectedArms = selectedArms;
        statusKey = "ready"; detail = "";
        Relocalize();
    }
    private async Task AssembleAsync(CancellationToken token)
    {
        if (!File.Exists(arms) || !File.Exists(weapon) || string.IsNullOrWhiteSpace(output) || File.Exists(output)
            || !output.EndsWith(".cast", StringComparison.OrdinalIgnoreCase)
            || target is not null && !bones.Contains(target))
        { statusKey = "armAssemblyInvalid"; detail = ""; return; }
        result = null;
        var request = ModelMergerService.Json(w =>
        {
            w.WriteString("command", "assemble"); w.WriteString("arms", arms); w.WriteString("weapon", weapon); w.WriteString("output", output);
            if (target is not null) w.WriteString("target_bone", target);
        });
        var assembled = await workspace.Service.RunAsync(request, OnProgress, null, token, output);
        result = assembled.GetProperty("output_path").GetString(); statusKey = "completed";
        attachedMeshes = assembled.GetProperty("attached_meshes").GetInt32();
        detail = $"{Text["targetBone"].Split("（")[0].Split(" (")[0]}: {assembled.GetProperty("target_bone")} · {Text["attachedMeshes"]}: {attachedMeshes}\n{result}";
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

    internal async Task VerifyAssemblyWorkflowAsync(string fixtureDirectory, string outputDirectory)
    {
        var weaponPath = Path.Combine(fixtureDirectory, "weapon.cast");
        if (!File.Exists(weaponPath)) weaponPath = Path.Combine(fixtureDirectory, "part-00.cast");
        arms = Path.Combine(fixtureDirectory, "part-00.cast");
        SetWeapon(weaponPath);
        output = Path.Combine(outputDirectory, "assembly-smoke.cast");
        Start(InspectAsync); await WaitForCompletionAsync();
        ModelMergerServiceSmoke.Require(statusKey == "ready" && bones.Count > 0, "Arms bone inspection failed: " + detail);
        Start(AssembleAsync); await WaitForCompletionAsync();
        ModelMergerServiceSmoke.Require(statusKey == "completed" && result is not null && File.Exists(result), "Arm assembly failed: " + detail);
        ModelMergerServiceSmoke.Require(attachedMeshes > 0, "Arm assembly attached no meshes.");
        Start(AssembleAsync); await WaitForCompletionAsync();
        ModelMergerServiceSmoke.Require(statusKey == "armAssemblyInvalid", "Assembly accepted an existing output file.");
        Console.WriteLine("MODEL_MERGER_ASSEMBLY_UI_SMOKE_OK: inspect, assemble, target bone, existing-output rejection.");
    }
}
