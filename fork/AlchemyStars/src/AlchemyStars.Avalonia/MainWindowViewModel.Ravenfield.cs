using Avalonia.Media.Imaging;

namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    private RavenfieldAdaptationResult? ravenfieldResult;
    private Bitmap? ravenfieldPreview;
    private string ravenfieldError = "";
    public bool IsRavenfieldPage => SelectedPage == WorkspacePage.Ravenfield;
    public Bitmap? RavenfieldPreview => ravenfieldPreview;
    public bool HasRavenfieldPreview => ravenfieldPreview is not null;
    public bool CanGenerateRavenfield => HasRavenfieldReference && !string.IsNullOrWhiteSpace(Workspace.Ravenfield.RfSourcePath);
    public string RavenfieldReadiness => !HasRavenfieldReference ? Text.RfNeedReference
        : string.IsNullOrWhiteSpace(Workspace.Ravenfield.RfSourcePath) ? Text.RfNeedSource : Text.RfReady;
    public string RavenfieldResultStatus => !string.IsNullOrEmpty(ravenfieldError) ? Text.ExportFailedTitle
        : ravenfieldResult is null ? Text.RfNoResult
        : ravenfieldResult.PalmFitPassed == false ? Text.RfPalmFitReview : Text.RfComplete;
    public string RavenfieldResultDetail => !string.IsNullOrEmpty(ravenfieldError) ? ravenfieldError
        : ravenfieldResult is null ? Text.RfPreviewEmpty
        : Text.RfSnapshotHelp + (ravenfieldResult.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, ravenfieldResult.Warnings) : "");
    public string RavenfieldOutputPath => ravenfieldResult?.FbxPath ?? "";
    private void RaiseRavenfieldPresentation()
    {
        foreach (var name in new[] { nameof(CanGenerateRavenfield), nameof(RavenfieldReadiness), nameof(RavenfieldResultStatus),
            nameof(RavenfieldResultDetail), nameof(RavenfieldOutputPath), nameof(RavenfieldPreview), nameof(HasRavenfieldPreview), nameof(HasRavenfieldResult) })
            OnPropertyChanged(name);
    }
    private void ClearRavenfieldResult()
    {
        ravenfieldResult = null;
        ravenfieldError = "";
        var previous = ravenfieldPreview;
        ravenfieldPreview = null;
        RaiseRavenfieldPresentation();
        previous?.Dispose();
    }
    internal void SetRavenfieldResult(RavenfieldAdaptationResult result)
    {
        ClearRavenfieldResult();
        ravenfieldResult = result;
        try { if (File.Exists(result.PreviewPath)) ravenfieldPreview = new Bitmap(result.PreviewPath); }
        catch (Exception) { /* Output files remain available when a preview cannot be decoded. */ }
        RaiseRavenfieldPresentation();
    }
    private bool replacingRavenfieldWorkspace;
    internal Func<AnimationExportRequest, int, RavenfieldAdaptationOptions, string?, Task<RavenfieldAdaptationResult>> RavenfieldRunner { get; set; } =
        (request, index, options, project) => Task.Run(() => new RavenfieldAdaptationEngine().Adapt(request, index, options, project));
    public IReadOnlyList<string> RavenfieldUnits { get; } = ["cm", "ft", "m"];
    public IReadOnlyList<string> RavenfieldUntaggedAxes { get; } = ["hands", "x", "y", "z"];
    public bool HasRavenfieldResult => ravenfieldResult is not null;
    public bool RavenfieldLocalHandFit
    {
        get => Workspace.Ravenfield.LocalHandFit;
        set => Workspace.Ravenfield.LocalHandFit = value;
    }
    public bool RavenfieldContactFit
    {
        get => Workspace.Ravenfield.ContactFit;
        set => Workspace.Ravenfield.ContactFit = value;
    }
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
        RaiseRavenfieldPresentation();
        if (e.PropertyName == nameof(RavenfieldAdaptationOptions.ReferenceAnimationId)) RaiseRavenfieldReference();
        if (e.PropertyName == nameof(RavenfieldAdaptationOptions.Mode)) RaiseRavenfieldMode();
        if (e.PropertyName == nameof(RavenfieldAdaptationOptions.ContactFit)) OnPropertyChanged(nameof(RavenfieldContactFit));
        if (e.PropertyName == nameof(RavenfieldAdaptationOptions.LocalHandFit)) OnPropertyChanged(nameof(RavenfieldLocalHandFit));
    }
    private void RavenfieldAnimationsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RaiseRavenfieldReference();
    private void RaiseRavenfieldReference()
    {
        RaiseRavenfieldPresentation();
        RaiseRavenfieldMode();
        OnPropertyChanged(nameof(RavenfieldContactFit));
        OnPropertyChanged(nameof(RavenfieldLocalHandFit));
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
        if (!CanInteract || !CanGenerateRavenfield || SelectedRavenfieldAnimation is not { } reference) return;
        var selectedWorkspace = Workspace;
        try
        {
            IsBusy = true;
            BusyMessage = Text.RfWorking;
            FooterStatus = Text.RfWorking;
            var snapshot = WorkspaceProjectStore.Snapshot(Workspace);
            var request = ApplyUnifiedOutputDirectory(projectStore.CreateExportRequest(snapshot));
            var index = Animations.IndexOf(reference);
            var projectPath = CurrentProjectPath;
            ClearRavenfieldResult();
            var result = await RavenfieldRunner(request, index, snapshot.Ravenfield, projectPath);
            if (!ReferenceEquals(Workspace, selectedWorkspace)) return;
            SetRavenfieldResult(result);
            var completion = result.PalmFitPassed == false ? Text.RfPalmFitReview : Text.RfComplete;
            FooterStatus = completion;
            // The workspace owns completion feedback and file actions; leave
            // the actual preview visible instead of covering it with a dialog.
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(Workspace, selectedWorkspace)) return;
            FooterStatus = Text.ExportFailed;
            ravenfieldError = LocalizeExportError(exception);
            RaiseRavenfieldPresentation();
            ShowDialog(Text.ExportFailedTitle, LocalizeExportError(exception), true);
        }
        finally { IsBusy = false; BusyMessage = ""; }
    }

    public async Task OpenRavenfieldAsync(bool preview)
        => await OpenRavenfieldArtifactAsync(preview ? "preview" : "blend");

    public async Task OpenRavenfieldArtifactAsync(string kind)
    {
        if (!CanInteract || ravenfieldResult is null) return;
        var path = kind switch { "preview" => ravenfieldResult.PreviewPath, "fbx" => ravenfieldResult.FbxPath,
            "report" => ravenfieldResult.ReportPath, "blend" => ravenfieldResult.BlendPath, _ => null };
        if (path is null) return;
        try
        {
            if (!File.Exists(path) || !await picker.OpenUriAsync(new Uri(path)))
                ShowDialog(Text.ExportFailedTitle, Text.RfOpenFailed, true);
        }
        catch (Exception e) { ShowDialog(Text.ExportFailedTitle, e.Message, true); }
    }
}
