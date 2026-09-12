namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    private RavenfieldAdaptationResult? ravenfieldResult;
    private bool replacingRavenfieldWorkspace;
    internal Func<AnimationExportRequest, int, RavenfieldAdaptationOptions, string?, Task<RavenfieldAdaptationResult>> RavenfieldRunner { get; set; } =
        (request, index, options, project) => Task.Run(() => new RavenfieldAdaptationEngine().Adapt(request, index, options, project));
    public IReadOnlyList<string> RavenfieldUnits { get; } = ["cm", "ft", "m"];
    public IReadOnlyList<string> RavenfieldUntaggedAxes { get; } = ["hands", "x", "y", "z"];
    public bool HasRavenfieldResult => ravenfieldResult is not null;
    public int RavenfieldModeIndex
    {
        get => Workspace.Ravenfield.Mode switch { "pose" => 0, "animation" => 1, "library" => 2, _ => -1 };
        set
        {
            if (value is 0 or 1 or 2) Workspace.Ravenfield.Mode = value switch { 1 => "animation", 2 => "library", _ => "pose" };
        }
    }
    public string RavenfieldModeHelp => Workspace.Ravenfield.Mode switch { "animation" => Text.RfAnimationHelp, "library" => Text.RfLibraryHelp, _ => Text.RfPoseHelp };
    public WorkspaceAnimation? SelectedRavenfieldAnimation
    {
        get => replacingRavenfieldWorkspace ? null : Animations.FirstOrDefault(a => a.Id == Workspace.Ravenfield.ReferenceAnimationId);
        set
        {
            // ComboBox can send null while its ItemsSource is being replaced. Preserve a
            // missing persisted ID rather than silently losing it or selecting another clip.
            if (value is null || !Animations.Contains(value)) return;
            Workspace.Ravenfield.ReferenceAnimationId = value.Id;
        }
    }
    public bool HasRavenfieldReference => SelectedRavenfieldAnimation is not null;
    private void SuggestRavenfieldIdle()
    {
        if (!string.IsNullOrWhiteSpace(Workspace.Ravenfield.ReferenceAnimationId)) return;
        var candidates = Animations.Where(a => Path.GetFileNameWithoutExtension(a.Name)
            .Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Contains("idle", StringComparer.OrdinalIgnoreCase)).Take(2).ToArray();
        if (candidates.Length == 1) SelectedRavenfieldAnimation = candidates[0];
    }
    private void WatchRavenfieldReference()
    {
        Workspace.Ravenfield.PropertyChanged += RavenfieldReferenceChanged;
        Animations.CollectionChanged += RavenfieldAnimationsChanged;
    }
    private void UnwatchRavenfieldReference()
    {
        Workspace.Ravenfield.PropertyChanged -= RavenfieldReferenceChanged;
        Animations.CollectionChanged -= RavenfieldAnimationsChanged;
    }
    private void RavenfieldReferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RavenfieldAdaptationOptions.ReferenceAnimationId)) RaiseRavenfieldReference();
        if (e.PropertyName == nameof(RavenfieldAdaptationOptions.Mode)) RaiseRavenfieldMode();
    }
    private void RavenfieldAnimationsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RaiseRavenfieldReference();
    private void RaiseRavenfieldReference()
    {
        RaiseRavenfieldMode();
        OnPropertyChanged(nameof(SelectedRavenfieldAnimation));
        OnPropertyChanged(nameof(HasRavenfieldReference));
    }
    private void RaiseRavenfieldMode()
    {
        OnPropertyChanged(nameof(RavenfieldModeIndex));
        OnPropertyChanged(nameof(RavenfieldModeHelp));
    }
    public async Task ChooseRavenfieldAsync()
    {
        if (!CanInteract) return;
        var selectedWorkspace = Workspace;
        var path = (await picker.PickFilesAsync(FilePickerPurpose.Ravenfield, false)).FirstOrDefault();
        if (path is not null && ReferenceEquals(Workspace, selectedWorkspace)) Workspace.Ravenfield.RfSourcePath = path;
    }

    public async Task AdaptRavenfieldAsync()
    {
        if (!CanInteract || SelectedRavenfieldAnimation is null) return;
        var selectedWorkspace = Workspace;
        try
        {
            IsBusy = true;
            BusyMessage = Text.RfWorking;
            FooterStatus = Text.RfWorking;
            var snapshot = WorkspaceProjectStore.Snapshot(Workspace);
            var request = ApplyUnifiedOutputDirectory(projectStore.CreateExportRequest(snapshot));
            var index = Animations.IndexOf(SelectedRavenfieldAnimation);
            var projectPath = CurrentProjectPath;
            ravenfieldResult = null; OnPropertyChanged(nameof(HasRavenfieldResult));
            var result = await RavenfieldRunner(request, index, snapshot.Ravenfield, projectPath);
            if (!ReferenceEquals(Workspace, selectedWorkspace)) return;
            ravenfieldResult = result; OnPropertyChanged(nameof(HasRavenfieldResult));
            FooterStatus = Text.RfComplete;
            ShowDialog(Text.RfComplete, string.Join(Environment.NewLine,
                new[] { result.BlendPath, result.FbxPath, result.ReportPath, result.PreviewPath }.Concat(result.Warnings)), false);
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(Workspace, selectedWorkspace)) return;
            FooterStatus = Text.ExportFailed;
            ShowDialog(Text.ExportFailedTitle, LocalizeExportError(exception), true);
        }
        finally { IsBusy = false; BusyMessage = ""; }
    }

    public async Task OpenRavenfieldAsync(bool preview)
    {
        if (!CanInteract || ravenfieldResult is null) return;
        var path = preview ? ravenfieldResult.PreviewPath : ravenfieldResult.BlendPath;
        try
        {
            if (!File.Exists(path) || !await picker.OpenUriAsync(new Uri(path)))
                ShowDialog(Text.ExportFailedTitle, Text.RfOpenFailed, true);
        }
        catch (Exception e) { ShowDialog(Text.ExportFailedTitle, e.Message, true); }
    }
}
