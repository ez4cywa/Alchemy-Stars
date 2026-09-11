using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AlchemyStars.Avalonia;

public enum WorkspacePage
{
    Animations,
    ModelParts,
    Settings,
    About,
    DualAnimations,
}

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    public const string ProjectRepositoryUrl = "https://github.com/ez4cywa/Alchemy-Stars";

    private readonly IAnimationExportEngine engine;
    private readonly WorkspaceProjectStore projectStore;
    private readonly ApplicationPreferencesStore preferences;
    private readonly IWorkspaceFilePicker picker;
    private WorkspaceDocument workspace;
    private WorkspaceAnimation? selectedAnimation;
    private WorkspacePart? selectedPart;
    private WorkspaceLayer? selectedLayer;
    private WorkspacePage selectedPage = WorkspacePage.Animations;
    private string? currentProjectPath;
    private string languageMode;
    private UiText text;
    private bool isBusy;
    private string busyMessage = string.Empty;
    private bool isDialogOpen;
    private string dialogTitle = string.Empty;
    private string dialogMessage = string.Empty;
    private bool dialogIsError;
    private string footerStatus;
    private readonly Dictionary<WorkspacePart, long> partSourceRequests = [];
    private long nextPartSourceRequest;
    private readonly Dictionary<WorkspaceAnimation, AnimationBlendTemplateAnalysis> automaticHandDefaults = [];
    private readonly HashSet<(WorkspaceAnimation Animation, string Property)> manualHandFields = [];
    private bool applyingHandDefaults;
    private void HandDefaultEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (!applyingHandDefaults && sender is WorkspaceAnimation animation && e.PropertyName is
            nameof(WorkspaceAnimation.LeftHandPoseFile) or nameof(WorkspaceAnimation.RightHandPoseFile)
            or nameof(WorkspaceAnimation.LeftIKTargetBoneName) or nameof(WorkspaceAnimation.RightIKTargetBoneName))
            manualHandFields.Add((animation, e.PropertyName));
    }
    internal Func<string, Task<ModelPartClassification?>> PartClassifier { get; set; } = path => Task.Run(() => ClassifyPart(path));

    public MainWindowViewModel(
        IAnimationExportEngine engine,
        WorkspaceProjectStore projectStore,
        ApplicationPreferencesStore preferences,
        IWorkspaceFilePicker picker,
        GitHubUpdateService? updateService = null)
    {
        this.engine = engine;
        this.projectStore = projectStore;
        this.preferences = preferences;
        this.picker = picker;
        this.updateService = updateService ?? new GitHubUpdateService();
        var preferenceSnapshot = preferences.Snapshot();
        CustomAppearance.Initialize(preferences.AppearanceDirectory);
        themeStyleIndex = preferenceSnapshot.ThemeStyle switch
        {
            "classic-apple" => 1,
            "windows-xp" => 2,
            "windows-2000" => 3,
            "custom" => CustomAppearance.CurrentTheme is not null ? 4 : 0,
            _ => 0,
        };
        themeModeIndex = preferenceSnapshot.ThemeMode switch { "dark" => 1, "system" => 2, _ => 0 };
        ApplyAppearance(false);
        languageMode = NormalizeLanguageMode(preferenceSnapshot.Language);
        text = new UiText(ResolveChinese(languageMode));
        InitializeLocalizedOptions();
        NativeTextResources.Apply(text);
        Preview = new CastPreviewViewModel(text);
        Timeline = new AnimationTimelineViewModel(text);
        workspace = preferences.CreateWorkspace();
        selectedAnimation = workspace.Animations.FirstOrDefault();
        Timeline.SetAnimation(selectedAnimation);
        SelectedPart = workspace.Parts.FirstOrDefault();
        footerStatus = text.Ready;
        WatchDualSources();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceDocument Workspace
    {
        get => workspace;
        private set
        {
            UnwatchDualSources();
            partSourceRequests.Clear();
            foreach (var item in automaticHandDefaults.Keys) item.PropertyChanged -= HandDefaultEdited;
            automaticHandDefaults.Clear();
            manualHandFields.Clear();
            workspace = value;
            selectedDual = null;
            WatchDualSources();
            OnPropertyChanged(nameof(Animations));
            OnPropertyChanged(nameof(DualAnimations));
            RaiseDualSelection();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Animations));
            OnPropertyChanged(nameof(Parts));
            OnPropertyChanged(nameof(OutputFormatIndex));
            OnPropertyChanged(nameof(IsCastOutput));
            SelectedDual = workspace.DualAnimations.FirstOrDefault();
        }
    }

    public ObservableCollection<WorkspaceAnimation> Animations => Workspace.Animations;
    public ObservableCollection<WorkspacePart> Parts => Workspace.Parts;
    public UiText Text { get => text; private set { text = value; Preview.Text = value; Timeline.Text = value; OnPropertyChanged(); } }
    public CastPreviewViewModel Preview { get; }
    public AnimationTimelineViewModel Timeline { get; }
    public string Version => AnimationExportEngine.EngineVersion;
    public string RuntimeDescription => $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
    public string ProjectRepositoryDisplayName => "github.com/ez4cywa/Alchemy-Stars";
    public string CurrentProjectLabel => string.IsNullOrWhiteSpace(CurrentProjectPath) ? Text.Untitled : CurrentProjectPath;
    public string CurrentPageTitle => SelectedPage switch
    {
        WorkspacePage.ModelParts => Text.ModelParts,
        WorkspacePage.Settings => Text.Settings,
        WorkspacePage.About => Text.About,
        WorkspacePage.DualAnimations => Text.DualAnimations,
        _ => Text.AnimationBlend,
    };
    public string WindowTitle => $"{Text.ProductName} | {CurrentProjectLabel} | {Version}";
    public string LanguageButtonLabel => IsChinese ? "EN" : "中文";
    public string LanguageButtonAccessibleName => IsChinese ? Text.SwitchToEnglish : Text.SwitchToChinese;
    public bool IsChinese => ResolveChinese(languageMode);
    public string LanguageMode => languageMode;
    public string? CurrentProjectPath { get => currentProjectPath; private set { currentProjectPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentProjectLabel)); OnPropertyChanged(nameof(WindowTitle)); } }
    public WorkspaceAnimation? SelectedAnimation
    {
        get => selectedAnimation;
        set
        {
            if (!ReferenceEquals(selectedAnimation, value)) Preview.Clear();
            if (selectedAnimation is not null) selectedAnimation.PropertyChanged -= SelectedOutputChanged;
            selectedAnimation = value;
            if (value is not null) value.PropertyChanged += SelectedOutputChanged;
            SelectedLayer = null; Timeline.SetAnimation(value);
            OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedAnimation)); OnPropertyChanged(nameof(SelectedOutputDirectory));
            OnPropertyChanged(nameof(CanExportSelectedAnimation));
        }
    }
    public string SelectedOutputDirectory => HasUnifiedOutputDirectory ? UnifiedOutputDirectory : SelectedAnimation?.EffectiveOutputFolder ?? string.Empty;
    private void SelectedOutputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceAnimation.Name) or nameof(WorkspaceAnimation.OutputFolder)) OnPropertyChanged(nameof(SelectedOutputDirectory));
    }
    public WorkspacePart? SelectedPart
    {
        get => selectedPart;
        set
        {
            if (ReferenceEquals(selectedPart, value)) return;
            if (selectedPart is not null) selectedPart.PropertyChanged -= SelectedPartChanged;
            selectedPart = value;
            if (selectedPart is not null) selectedPart.PropertyChanged += SelectedPartChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedPart));
            RaisePartClassificationState();
        }
    }
    public WorkspaceLayer? SelectedLayer { get => selectedLayer; set { selectedLayer = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedTimelineTrack)); OnPropertyChanged(nameof(HasSelectedLayer)); } }
    public AnimationTrackItem? SelectedTimelineTrack
    {
        get => Timeline.FindTrack(SelectedLayer);
        set => SelectedLayer = value?.Layer;
    }
    public bool HasSelectedAnimation => SelectedAnimation is not null;
    public bool HasSelectedPart => SelectedPart is not null;
    public bool HasSelectedPartClassification => SelectedPart?.AutoClassification is not null;
    public string SelectedPartDetection
    {
        get
        {
            if (SelectedPart?.AutoClassification is not { } classification) return string.Empty;
            var type = Text.PartTypes[(int)classification.Kind];
            var confidence = classification.Confidence switch
            {
                >= 0.85f => Text.DetectionHigh,
                >= 0.65f => Text.DetectionMedium,
                _ => Text.DetectionLow,
            };
            var evidence = string.Join(Text.ListSeparator, classification.Evidence.Select(Text.PartEvidence));
            var template = SelectedPart.Type == classification.Kind ? Text.AutoPartDetected : Text.AutoPartOverridden;
            return string.Format(CultureInfo.CurrentCulture, template, type, confidence,
                classification.BoneCount, evidence);
        }
    }
    public bool HasSelectedLayer => SelectedLayer is not null;
    public WorkspacePage SelectedPage { get => selectedPage; private set { selectedPage = value; if (value is WorkspacePage.Settings or WorkspacePage.About) Preview.Pause(); OnPropertyChanged(); RaisePageState(); } }
    public bool IsAnimationsPage => SelectedPage == WorkspacePage.Animations;
    public bool IsModelPartsPage => SelectedPage == WorkspacePage.ModelParts;
    public bool IsSettingsPage => SelectedPage == WorkspacePage.Settings;
    public bool IsAboutPage => SelectedPage == WorkspacePage.About;
    public bool IsBusy { get => isBusy; private set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInteract)); OnPropertyChanged(nameof(CanExportSelectedAnimation)); } }
    public string BusyMessage { get => busyMessage; private set { busyMessage = value; OnPropertyChanged(); } }
    public bool IsDialogOpen { get => isDialogOpen; private set { isDialogOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInteract)); OnPropertyChanged(nameof(CanExportSelectedAnimation)); } }
    public bool CanInteract => !IsBusy && !IsDialogOpen;
    public bool CanExportSelectedAnimation => CanInteract && IsAnimationsPage && HasSelectedAnimation;
    public string DialogTitle { get => dialogTitle; private set { dialogTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(DialogAccessibleDescription)); } }
    public string DialogMessage { get => dialogMessage; private set { dialogMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(DialogAccessibleDescription)); } }
    public string DialogAccessibleDescription => DialogTitle + Environment.NewLine + DialogMessage;
    public bool DialogIsError { get => dialogIsError; private set { dialogIsError = value; OnPropertyChanged(); } }
    public string FooterStatus { get => footerStatus; private set { footerStatus = value; OnPropertyChanged(); } }
    public IReadOnlyList<string> OutputFormats => AlchemyStars.Engine.OutputFormats.All;
    public int OutputFormatIndex
    {
        get => Math.Max(0, AlchemyStars.Engine.OutputFormats.All.ToList().FindIndex(format => string.Equals(format, Workspace.OutputFormat, StringComparison.OrdinalIgnoreCase)));
        set
        {
            if (value >= 0 && value < AlchemyStars.Engine.OutputFormats.All.Count)
                Workspace.OutputFormat = AlchemyStars.Engine.OutputFormats.All[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCastOutput));
        }
    }
    public bool IsCastOutput => string.Equals(Workspace.OutputFormat, ".cast", StringComparison.OrdinalIgnoreCase);

    public void SelectPage(WorkspacePage page)
    {
        if ((page == WorkspacePage.DualAnimations) != IsDualPage) Preview.Clear();
        SelectedPage = page;
    }

    public void NewProject()
    {
        Workspace = preferences.CreateWorkspace();
        CurrentProjectPath = null;
        SelectedAnimation = null;
        SelectedPart = Parts.FirstOrDefault();
        FooterStatus = Text.NewProjectCreated;
        SelectPage(WorkspacePage.Animations);
    }

    public async Task OpenProjectAsync()
    {
        var paths = await picker.PickFilesAsync(FilePickerPurpose.Project, false);
        if (paths.Count > 0)
            LoadProject(paths[0]);
    }

    public void LoadProject(string filePath)
    {
        try
        {
            Workspace = projectStore.Load(filePath);
            CurrentProjectPath = Path.GetFullPath(filePath);
            preferences.RememberDirectory("project", CurrentProjectPath);
            SelectedAnimation = Animations.FirstOrDefault();
            SelectedPart = Parts.FirstOrDefault();
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.ProjectLoaded, Path.GetFileName(CurrentProjectPath));
            SelectPage(WorkspacePage.Animations);
        }
        catch (Exception exception)
        {
            ShowDialog(Text.ProjectLoadFailed, exception.Message, true);
        }
    }

    public async Task SaveProjectAsync(bool saveAs)
    {
        var destination = saveAs || string.IsNullOrWhiteSpace(CurrentProjectPath)
            ? await picker.PickProjectDestinationAsync(CurrentProjectPath)
            : CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(destination))
            return;
        try
        {
            projectStore.Save(Workspace, destination);
            CurrentProjectPath = Path.GetFullPath(destination);
            preferences.RememberDirectory("project", CurrentProjectPath);
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.ProjectSaved, Path.GetFileName(CurrentProjectPath));
        }
        catch (Exception exception)
        {
            ShowDialog(Text.ProjectSaveFailed, exception.Message, true);
        }
    }

    public async Task AddAnimationsAsync() => AddAnimationPaths(await picker.PickFilesAsync(FilePickerPurpose.Animation, true));

    public int AddAnimationPaths(IEnumerable<string> paths)
    {
        var added = 0;
        var normalized = NormalizeCastPaths(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var templates = AnimationBlendTemplateAnalyzer.AnalyzeBatch(normalized, Parts.Select(part => part.FilePath));
        foreach (var template in templates)
        {
            var animation = new WorkspaceAnimation
            {
                Name = template.BaseAnimationFile,
                OutputFolder = string.Empty,
                EnableLeftHandIK = template.EnableLeftHandIK,
                EnableRightHandIK = template.EnableRightHandIK,
                LeftHandPoseFile = template.LeftHandPoseFile,
                RightHandPoseFile = template.RightHandPoseFile,
                LeftIKTargetBoneName = template.LeftIKTargetBoneName,
                RightIKTargetBoneName = template.RightIKTargetBoneName,
            };
            foreach (var layerPath in template.LayerFiles)
                animation.Layers.Add(new WorkspaceLayer { Name = layerPath, Type = AnimationLayerKind.Additive });
            Animations.Add(animation);
            automaticHandDefaults[animation] = template;
            animation.PropertyChanged += HandDefaultEdited;
            SelectedAnimation = animation;
            added++;
        }
        if (added > 0)
        {
            preferences.RememberDirectory("animation", SelectedAnimation?.Name);
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.AnimationsAdded, added);
        }
        return added;
    }

    public async Task AddPartsAsync() => await AddPartPathsAsync(await picker.PickFilesAsync(FilePickerPurpose.ModelPart, true));

    public async Task<int> AddPartPathsAsync(IEnumerable<string> paths)
    {
        var normalized = NormalizeCastPaths(paths).ToArray();
        if (normalized.Length == 0) return 0;
        var importingWorkspace = Workspace;
        FooterStatus = Text.DetectingPartTypes;
        var attempts = await Task.WhenAll(normalized.Select(PartClassifier));
        if (!ReferenceEquals(importingWorkspace, Workspace)) return 0;
        return AddPartPathsCore(normalized.Zip(attempts));
    }

    public int AddPartPaths(IEnumerable<string> paths)
    {
        var normalized = NormalizeCastPaths(paths).ToArray();
        return AddPartPathsCore(normalized.Select(path => (path, ClassifyPart(path))));
    }

    private int AddPartPathsCore(IEnumerable<(string Path, ModelPartClassification? Classification)> inputs)
    {
        var added = 0;
        var detected = 0;
        var needsReview = 0;
        foreach (var (path, classification) in inputs)
        {
            var fallback = Parts.Count == 0 ? ModelPartKind.ViewHands : ModelPartKind.Weapon;
            var part = new WorkspacePart
            {
                FilePath = path,
                Type = classification?.Kind ?? fallback,
                ParentBoneTag = classification?.RecommendedParentBone
                    ?? (fallback == ModelPartKind.Weapon ? "tag_weapon" : string.Empty),
                AutoClassification = classification,
            };
            Parts.Add(part);
            SelectedPart = part;
            if (classification?.Kind == ModelPartKind.ViewHands) RememberImportedArms(part);
            if (classification is not null)
            {
                detected++;
                if (classification.Confidence < 0.65f) needsReview++;
            }
            else needsReview++;
            added++;
        }
        if (added > 0)
        {
            RefreshForegripTemplateDefaults();
            preferences.RememberDirectory("part", SelectedPart?.FilePath);
            FooterStatus = detected == 0
                ? string.Format(CultureInfo.CurrentCulture, Text.PartsAdded, added)
                : string.Format(CultureInfo.CurrentCulture, Text.PartsDetected, added, detected, needsReview);
        }
        return added;
    }

    public async Task AddLayersAsync()
    {
        if (SelectedAnimation is null)
        {
            ShowDialog(Text.NoAnimationSelected, Text.SelectAnimationForLayers, true);
            return;
        }
        AddLayerPaths(await picker.PickFilesAsync(FilePickerPurpose.AnimationLayer, true));
    }

    public int AddLayerPaths(IEnumerable<string> paths)
    {
        if (SelectedAnimation is null)
            return 0;
        var added = 0;
        foreach (var path in NormalizeCastPaths(paths))
        {
            var layer = new WorkspaceLayer { Name = path, Type = AnimationLayerKind.Additive };
            SelectedAnimation.Layers.Add(layer);
            SelectedLayer = layer;
            added++;
        }
        if (added > 0)
        {
            preferences.RememberDirectory("layer", SelectedLayer?.Name);
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.LayersAdded, added);
        }
        return added;
    }

    public void RemoveSelectedAnimation()
    {
        var removed = SelectedAnimation;
        if (removed is null)
            return;
        var index = Animations.IndexOf(removed);
        var removedId = removed.Id;
        foreach (var dual in DualAnimations)
        {
            if (dual.LeftAnimationId == removedId) dual.LeftAnimationId = string.Empty;
            if (dual.RightAnimationId == removedId) dual.RightAnimationId = string.Empty;
        }
        automaticHandDefaults.Remove(removed);
        manualHandFields.RemoveWhere(field => ReferenceEquals(field.Animation, removed));
        removed.PropertyChanged -= HandDefaultEdited;
        Animations.Remove(removed);
        SelectedAnimation = Animations.Count == 0 ? null : Animations[Math.Min(index, Animations.Count - 1)];
    }

    internal string? CopySelectedResource()
    {
        if (IsAnimationsPage && SelectedAnimation is { } animation) return ResourceLibraryClipboard.Create(animation);
        if (IsModelPartsPage && SelectedPart is { } part) return ResourceLibraryClipboard.Create(part);
        return null;
    }

    internal bool PasteResource(string? payload)
    {
        if (!ResourceLibraryClipboard.TryRead(payload, out var animation, out var part)) return false;
        if (animation is not null)
        {
            foreach (var path in new[] { animation.Name, animation.LeftHandPoseFile, animation.RightHandPoseFile }
                .Concat(animation.Layers.Select(layer => layer.Name)).Where(path => !string.IsNullOrWhiteSpace(path)))
                WorkspacePaths.RequireCastAnimation(path);
            animation.Id = Guid.NewGuid().ToString("N");
            var originalName = animation.OutputName;
            var suffix = 1;
            while (Animations.Any(item => item.OutputName.Equals(animation.OutputName, StringComparison.OrdinalIgnoreCase)))
                animation.OutputName = originalName + "_copy" + (suffix++ == 1 ? "" : (suffix - 1).ToString(CultureInfo.InvariantCulture));
            Animations.Add(animation);
            SelectedAnimation = animation;
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.AnimationsAdded, 1);
            return true;
        }
        if (part is not null)
        {
            part.AutoClassification = null;
            Parts.Add(part);
            SelectedPart = part;
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.PartsAdded, 1);
            return true;
        }
        return false;
    }

    public void RemoveSelectedPart()
    {
        if (SelectedPart is null)
            return;
        var index = Parts.IndexOf(SelectedPart);
        Parts.Remove(SelectedPart);
        RefreshForegripTemplateDefaults();
        SelectedPart = Parts.Count == 0 ? null : Parts[Math.Min(index, Parts.Count - 1)];
    }

    public void RemoveSelectedLayer()
    {
        if (SelectedAnimation is null || SelectedLayer is null)
            return;
        var index = SelectedAnimation.Layers.IndexOf(SelectedLayer);
        SelectedAnimation.Layers.Remove(SelectedLayer);
        SelectedLayer = SelectedAnimation.Layers.Count == 0 ? null : SelectedAnimation.Layers[Math.Min(index, SelectedAnimation.Layers.Count - 1)];
    }

    public void MoveSelectedPart(int delta)
    {
        var selected = SelectedPart;
        Move(Parts, selected, delta);
        SelectedPart = selected;
    }
    public void MoveSelectedLayer(int delta)
    {
        if (SelectedAnimation is not null)
        {
            var selected = SelectedLayer;
            Move(SelectedAnimation.Layers, selected, delta);
            SelectedLayer = selected;
        }
    }

    public async Task ReplaceAnimationSourceAsync(WorkspaceAnimation animation)
    {
        var path = (await picker.PickFilesAsync(FilePickerPurpose.Animation, false)).FirstOrDefault();
        if (path is not null)
        {
            WorkspacePaths.RequireCastAnimation(path);
            animation.Name = path;
        }
    }

    public async Task ReplacePartSourceAsync(WorkspacePart part)
    {
        var path = (await picker.PickFilesAsync(FilePickerPurpose.ModelPart, false)).FirstOrDefault();
        if (path is not null)
            await ApplyPartSourceAsync(part, path);
    }

    public async Task ReclassifyPartAsync(WorkspacePart part)
    {
        // FilePath clears the transient result whenever typed/pasted text changes.
        // Preserve a manual type override when focus merely moves between controls.
        if (part.AutoClassification is null)
            await ApplyPartSourceAsync(part, part.FilePath);
    }

    public async Task SetPartPathFromDropAsync(WorkspacePart part, string path)
    {
        await ApplyPartSourceAsync(part, PathInput.Normalize(path));
        preferences.RememberDirectory("part", path);
    }

    public async Task ReplaceLayerSourceAsync(WorkspaceLayer layer)
    {
        var path = (await picker.PickFilesAsync(FilePickerPurpose.AnimationLayer, false)).FirstOrDefault();
        if (path is not null)
        {
            WorkspacePaths.RequireCastAnimation(path);
            layer.Name = path;
        }
    }

    public async Task SetPoseAsync(WorkspaceAnimation animation, bool left)
    {
        var path = (await picker.PickFilesAsync(left ? FilePickerPurpose.LeftPose : FilePickerPurpose.RightPose, false)).FirstOrDefault();
        if (path is null)
            return;
        WorkspacePaths.RequireCastAnimation(path);
        if (left)
            animation.LeftHandPoseFile = path;
        else
            animation.RightHandPoseFile = path;
    }

    public async Task SetOutputFolderAsync(WorkspaceAnimation animation)
    {
        var folder = await picker.PickFolderAsync(animation.OutputFolder);
        if (folder is not null)
            animation.OutputFolder = folder;
    }

    public async Task ExportAsync()
    {
        if (IsDualPage) { await ProcessDualAsync(false); return; }
        await ExportAnimationsAsync(selectedOnly: false);
    }

    public Task ExportSelectedAnimationAsync() => CanExportSelectedAnimation
        ? ExportAnimationsAsync(selectedOnly: true) : Task.CompletedTask;

    private async Task ExportAnimationsAsync(bool selectedOnly)
    {
        if (!CanInteract)
            return;
        try
        {
            IsBusy = true;
            BusyMessage = Text.Exporting;
            FooterStatus = Text.Exporting;
            var request = ApplyUnifiedOutputDirectory(projectStore.CreateExportRequest(Workspace));
            var selection = SelectedAnimation;
            var selectedIndex = selection is null ? 0 : Animations.IndexOf(selection);
            if (selectedOnly)
            {
                request = AnimationExportEngine.SelectAnimation(request, selectedIndex);
                selectedIndex = 0;
            }
            var result = await Task.Run(() => engine.Export(request));
            if (request.Options.Format == ExportFormat.Cast && result.OutputFiles.Count > 0 && ReferenceEquals(selection, SelectedAnimation))
                await Preview.LoadAsync(result.OutputFiles[Math.Clamp(selectedIndex, 0, result.OutputFiles.Count - 1)], parts: request.Parts, legacy: request.Options.MatchOldCallOfDuty);
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.ExportComplete, result.OutputFiles.Count);
            ShowDialog(Text.ExportCompleteTitle, string.Format(CultureInfo.CurrentCulture, Text.ExportCompleteBody, result.OutputFiles.Count) + Environment.NewLine + string.Join(Environment.NewLine, result.OutputFiles), false);
        }
        catch (Exception exception)
        {
            FooterStatus = Text.ExportFailed;
            ShowDialog(Text.ExportFailedTitle, LocalizeExportError(exception), true);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    public void SaveDefaults()
    {
        preferences.SaveDefaults(languageMode, Workspace);
        FooterStatus = Text.DefaultsSaved;
        ShowDialog(Text.SettingsSavedTitle, Text.SettingsSavedBody, false);
    }

    public async Task OpenPreviewAsync()
    {
        var path = (await picker.PickFilesAsync(FilePickerPurpose.Preview, false)).FirstOrDefault();
        if (path is not null) await Preview.LoadAsync(path, parts: Parts.Select(part => new ModelPartSpec(part.FilePath, part.Type, part.ParentBoneTag)).ToArray(), legacy: Workspace.MatchOldCallOfDuty);
    }

    public async Task BuildPreviewAsync()
    {
        if (IsDualPage) { await ProcessDualAsync(true); return; }
        if (IsBusy || SelectedAnimation is null) return;
        // Each invocation owns a unique cache, never a source/output asset directory.
        var cache = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "PreviewCache", Guid.NewGuid().ToString("N")));
        try
        {
            IsBusy = true; BusyMessage = Text.BuildingPreview;
            var selection = SelectedAnimation;
            var request = projectStore.CreateExportRequest(Workspace);
            var job = request.Animations[Animations.IndexOf(selection)] with { OutputFolder = cache, OutputName = "composition" };
            request = request with { Animations = [job], Options = request.Options with { Format = ExportFormat.Cast, OutputPrefix = "", OutputSuffix = "" } };
            Directory.CreateDirectory(cache);
            var result = await Task.Run(() => engine.Export(request));
            if (ReferenceEquals(selection, SelectedAnimation))
                await Preview.LoadAsync(result.OutputFiles.Single(), Text.PreviewSnapshot + " · " + selection.OutputName, request.Parts, request.Options.MatchOldCallOfDuty);
        }
        catch (Exception exception) { ShowDialog(Text.PreviewFailed, LocalizeExportError(exception), true); }
        finally
        {
            IsBusy = false; BusyMessage = string.Empty;
            if (Directory.Exists(cache))
            {
                try { Directory.Delete(cache, true); }
                catch (IOException) { FooterStatus = Text.PreviewCacheRetained; }
                catch (UnauthorizedAccessException) { FooterStatus = Text.PreviewCacheRetained; }
            }
        }
    }

    public void Dispose()
    {
        CancelUpdate();
        DiscardDownloadedUpdate();
        UnwatchDualSources();
        if (selectedPart is not null)
            selectedPart.PropertyChanged -= SelectedPartChanged;
        Timeline.Dispose();
        Preview.Dispose();
    }

    public void ToggleLanguage()
    {
        languageMode = IsChinese ? "en-US" : "zh-CN";
        preferences.SaveLanguage(languageMode);
        ApplyLanguage();
    }

    public void UseSystemLanguage()
    {
        languageMode = "system";
        preferences.SaveLanguage(languageMode);
        ApplyLanguage();
    }

    public void GenerateSprintBatch()
    {
        var source = SelectedAnimation ?? Animations.FirstOrDefault(animation =>
            animation.OutputName.Contains("_idle", StringComparison.OrdinalIgnoreCase));
        if (source is null || !source.OutputName.Contains("_idle", StringComparison.OrdinalIgnoreCase))
        {
            ShowDialog(Text.SprintGeneratorTitle, Text.SprintGeneratorNeedsIdle, true);
            return;
        }

        var suffixes = new[]
        {
            "_sprint_in", "_sprint_loop", "_sprint_out",
            "_super_sprint_in", "_super_sprint_loop", "_super_sprint_out",
            "_slide_in", "_slide_sprint_in", "_slide_loop", "_slide_out",
        };
        var insertionIndex = Animations.IndexOf(source) + 1;
        foreach (var suffix in suffixes)
        {
            var clone = CloneAnimation(source);
            clone.OutputName = ReplaceIdle(source.OutputName, suffix);
            Animations.Insert(insertionIndex++, clone);
        }
        FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.SprintGeneratorComplete, suffixes.Length);
    }

    public void ShowDiagnosticDialog(bool error) => ShowDialog(
        error ? Text.ExportFailedTitle : Text.ExportCompleteTitle,
        error ? Text.OutputWouldOverwrite : Text.SettingsSavedBody,
        error);

    public void CloseDialog() => IsDialogOpen = false;

    public Task OpenUpstreamAsync() => OpenExternalUriAsync(new Uri("https://github.com/Scobalula/Alchemist"));

    public Task OpenProjectRepositoryAsync() => OpenExternalUriAsync(new Uri(ProjectRepositoryUrl));

    private async Task OpenExternalUriAsync(Uri uri)
    {
        try
        {
            if (await picker.OpenUriAsync(uri))
            {
                FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.ExternalLinkOpened, uri.Host);
                return;
            }
        }
        catch
        {
            // Convert launcher failures into an actionable in-app message instead of
            // allowing an async click handler to fail without visible feedback.
        }

        FooterStatus = Text.ExternalLinkOpenFailed;
        ShowDialog(
            Text.ExternalLinkOpenFailed,
            string.Format(CultureInfo.CurrentCulture, Text.ExternalLinkOpenFailedBody, uri.AbsoluteUri),
            true);
    }

    public void SetPathFromDrop(object target, string path, string role)
    {
        var normalized = PathInput.Normalize(path);
        if (role is "animation" or "leftPose" or "rightPose" or "layer")
        {
            if (!WorkspacePaths.IsCastAnimationFile(normalized))
            {
                ShowDialog(Text.Notification, "动画输入仅支持 CAST / Animation input must be CAST.", true);
                return;
            }
        }
        switch (target)
        {
            case WorkspaceAnimation animation when role == "animation": animation.Name = normalized; preferences.RememberDirectory("animation", normalized); break;
            case WorkspaceAnimation animation when role == "leftPose": animation.LeftHandPoseFile = normalized; preferences.RememberDirectory("pose", normalized); break;
            case WorkspaceAnimation animation when role == "rightPose": animation.RightHandPoseFile = normalized; preferences.RememberDirectory("pose", normalized); break;
            case WorkspaceAnimation animation when role == "output": animation.OutputFolder = Directory.Exists(normalized) ? normalized : Path.GetDirectoryName(normalized) ?? string.Empty; preferences.RememberDirectory("output", normalized); break;
            case WorkspaceLayer layer when role == "layer": layer.Name = normalized; preferences.RememberDirectory("layer", normalized); break;
        }
    }

    private async Task ApplyPartSourceAsync(WorkspacePart part, string path)
    {
        var normalized = PathInput.Normalize(path);
        var request = ++nextPartSourceRequest;
        partSourceRequests[part] = request;
        part.FilePath = normalized;
        var classification = await PartClassifier(normalized);
        if (!partSourceRequests.TryGetValue(part, out var latest) || latest != request) return;
        partSourceRequests.Remove(part);
        if (!Parts.Contains(part) || !string.Equals(part.FilePath, normalized, StringComparison.OrdinalIgnoreCase)) return;
        RefreshForegripTemplateDefaults();
        if (classification is not null)
        {
            part.Type = classification.Kind;
            part.ParentBoneTag = classification.RecommendedParentBone;
            part.AutoClassification = classification;
            RememberImportedArms(part);
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.PartDetected,
                Text.PartTypes[(int)classification.Kind]);
        }
        else
        {
            part.AutoClassification = null;
            FooterStatus = Text.PartDetectionFailed;
        }
        RaisePartClassificationState();
    }

    private static ModelPartClassification? ClassifyPart(string path)
    {
        try { return ModelPartClassifier.Classify(path); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private void RefreshForegripTemplateDefaults()
    {
        var items = Animations.Where(animation => WorkspacePaths.IsCastAnimationFile(animation.Name)).ToArray();
        var analyses = AnimationBlendTemplateAnalyzer.AnalyzeBatch(items.Select(animation => animation.Name).ToArray(), Parts.Select(part => part.FilePath));
        for (var index = 0; index < items.Length; index++)
        {
            var animation = items[index];
            var analysis = analyses[index];
            automaticHandDefaults.TryGetValue(animation, out var previous);
            applyingHandDefaults = true;
            if (!manualHandFields.Contains((animation, nameof(WorkspaceAnimation.LeftIKTargetBoneName))) && (string.IsNullOrWhiteSpace(animation.LeftIKTargetBoneName) || previous is not null && animation.LeftIKTargetBoneName == previous.LeftIKTargetBoneName))
                animation.LeftIKTargetBoneName = analysis.LeftIKTargetBoneName;
            if (!manualHandFields.Contains((animation, nameof(WorkspaceAnimation.RightIKTargetBoneName))) && (string.IsNullOrWhiteSpace(animation.RightIKTargetBoneName) || previous is not null && animation.RightIKTargetBoneName == previous.RightIKTargetBoneName))
                animation.RightIKTargetBoneName = analysis.RightIKTargetBoneName;
            if (!manualHandFields.Contains((animation, nameof(WorkspaceAnimation.LeftHandPoseFile))) && (string.IsNullOrWhiteSpace(animation.LeftHandPoseFile) || previous is not null && animation.LeftHandPoseFile == previous.LeftHandPoseFile))
                animation.LeftHandPoseFile = analysis.LeftHandPoseFile;
            if (!manualHandFields.Contains((animation, nameof(WorkspaceAnimation.RightHandPoseFile))) && (string.IsNullOrWhiteSpace(animation.RightHandPoseFile) || previous is not null && animation.RightHandPoseFile == previous.RightHandPoseFile))
                animation.RightHandPoseFile = analysis.RightHandPoseFile;
            automaticHandDefaults[animation] = analysis;
            applyingHandDefaults = false;
            if (previous is null) animation.PropertyChanged += HandDefaultEdited;
        }
    }

    private void SelectedPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspacePart.Type) && sender is WorkspacePart part)
            RememberImportedArms(part);
        if (e.PropertyName is nameof(WorkspacePart.Type) or nameof(WorkspacePart.AutoClassification))
            RaisePartClassificationState();
    }

    private void RaisePartClassificationState()
    {
        OnPropertyChanged(nameof(HasSelectedPartClassification));
        OnPropertyChanged(nameof(SelectedPartDetection));
    }

    private void ApplyLanguage()
    {
        Text = new UiText(IsChinese);
        RefreshLocalizedOptions();
        NativeTextResources.Apply(Text);
        FooterStatus = Text.Ready;
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(LanguageMode));
        OnPropertyChanged(nameof(LanguageButtonLabel));
        OnPropertyChanged(nameof(LanguageButtonAccessibleName));
        RefreshAppearanceLabel();
        RefreshUtilitySettings();
        RefreshOutputDirectorySettings();
        RaiseUpdateState();
        OnPropertyChanged(nameof(CurrentProjectLabel));
        OnPropertyChanged(nameof(WindowTitle));
        RaisePartClassificationState();
        RaisePageState();
    }

    private string LocalizeExportError(Exception exception)
    {
        if (exception is not ExportValidationException validation)
            return exception.Message;
        return validation.Code switch
        {
            ExportErrorCode.NoModelParts => Text.NeedPart,
            ExportErrorCode.NoAnimations => Text.NeedAnimation,
            ExportErrorCode.MissingOutputFolder => Text.NeedOutputFolder,
            ExportErrorCode.MissingOutputName => Text.NeedOutputName,
            ExportErrorCode.InvalidFramerate => Text.InvalidFramerate,
            ExportErrorCode.OutputWouldOverwriteInput => Text.OutputWouldOverwrite,
            _ => validation.Message,
        };
    }

    private void ShowDialog(string title, string message, bool error)
    {
        Preview.Pause();
        DialogTitle = title;
        DialogMessage = message;
        DialogIsError = error;
        IsDialogOpen = true;
    }

    private void RaisePageState()
    {
        OnPropertyChanged(nameof(CanExportSelectedAnimation));
        OnPropertyChanged(nameof(CurrentExportLabel));
        OnPropertyChanged(nameof(IsDualPage));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(IsAnimationsPage));
        OnPropertyChanged(nameof(IsModelPartsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsAboutPage));
    }

    private static string NormalizeLanguageMode(string? value) => value switch
    {
        "zh-CN" => "zh-CN",
        "en-US" => "en-US",
        _ => "system",
    };

    private static bool ResolveChinese(string mode) => mode == "zh-CN"
        || mode == "system" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> NormalizeCastPaths(IEnumerable<string> paths) => paths
        .Select(PathInput.Normalize)
        .Where(path => !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".cast", StringComparison.OrdinalIgnoreCase));

    private static void Move<T>(ObservableCollection<T> collection, T? item, int delta) where T : class
    {
        if (item is null)
            return;
        var current = collection.IndexOf(item);
        var target = current + delta;
        if (current >= 0 && target >= 0 && target < collection.Count)
            collection.Move(current, target);
    }

    private static WorkspaceAnimation CloneAnimation(WorkspaceAnimation source)
    {
        var clone = new WorkspaceAnimation
        {
            Name = source.Name,
            OutputName = source.OutputName,
            OutputFolder = source.OutputFolder,
            OutputFramerate = source.OutputFramerate,
            WeaponFollowMode = source.WeaponFollowMode,
            EnableLeftHandIK = source.EnableLeftHandIK,
            EnableRightHandIK = source.EnableRightHandIK,
            UseExperimentalFeatures = source.UseExperimentalFeatures,
            LeftHandPoseFile = source.LeftHandPoseFile,
            RightHandPoseFile = source.RightHandPoseFile,
            LeftIKTargetBoneName = source.LeftIKTargetBoneName,
            RightIKTargetBoneName = source.RightIKTargetBoneName,
        };
        foreach (var layer in source.Layers)
            clone.Layers.Add(new WorkspaceLayer { Name = layer.Name, Offset = layer.Offset, Color = layer.Color, Type = layer.Type });
        return clone;
    }

    private static string ReplaceIdle(string value, string replacement)
    {
        var index = value.IndexOf("_idle", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? value + replacement : value[..index] + replacement + value[(index + 5)..];
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed partial class UiText
{
    private readonly bool zh;
    public UiText(bool chinese) => zh = chinese;
    private string L(string chinese, string english) => zh ? chinese : english;

    public string ProductName => L("炼金之星", "Alchemy Stars");
    public string Project => L("项目", "Project");
    public string Workspace => L("工作区", "Workspace");
    public string PreviewLine => L("Avalonia · Native AOT 完整迁移测试", "Avalonia · Native AOT full migration test");
    public string Untitled => L("未命名批处理", "Untitled batch");
    public string SwitchToEnglish => "切换到英文界面";
    public string SwitchToChinese => "Switch to Chinese interface";
    public string NewProject => L("新建", "New");
    public string OpenProject => L("打开", "Open");
    public string SaveProject => L("保存", "Save");
    public string SaveProjectAs => L("另存为", "Save as");
    public string Export => L("导出全部", "Export all");
    public string Animations => L("动画", "Animations");
    public string ModelParts => L("模型部件", "Model parts");
    public string Settings => L("设置", "Settings");
    public string About => L("关于", "About");
    public string AnimationWorkspace => L("动画工作区", "Animation workspace");
    public string AnimationWorkspaceHelp => L("选择基础动画，编辑 IK、姿势、输出与动画层。动画层区域优先接收拖入文件。", "Select a base animation, then edit IK, poses, output and layers. Files dropped on the layer area are always imported as layers.");
    public string AddAnimation => L("添加动画", "Add animation");
    public string ImportAnimationsMenu => L("导入动画…", "Import animations…");
    public string ImportLayersMenu => L("导入动画层…", "Import animation layers…");
    public string ImportPartsMenu => L("导入模型部件…", "Import model parts…");
    public string Remove => L("删除", "Remove");
    public string EmptyAnimations => L("尚未添加动画", "No animations yet");
    public string EmptyAnimationsHelp => L("右键此区域、拖入 CAST，或点击“添加动画”。", "Right-click this area, drop CAST files, or choose Add animation.");
    public string AnimationFile => L("动画文件", "Animation file");
    public string AnimationDetails => L("动画属性", "Animation properties");
    public string AssetLibrary => L("资源库", "Asset library");
    public string Composition => L("合成工作区", "Composition");
    public string ResizePanels => L("调整面板宽度（方向键也可调整）", "Resize panels (arrow keys supported)");
    public string ResizeTracks => L("调整图层区高度（方向键也可调整）", "Resize layer area (arrow keys supported)");
    public string ImportHint => L("右键导入", "Right-click to import");
    public string Order => L("排序", "Order");
    public string RestoreLayout => L("恢复面板布局", "Restore panel layout");
    public string BuildPreview => L("合成预览", "Build CAST preview");
    public string BuildingPreview => L("正在合成 CAST 预览…", "Composing CAST preview…");
    public string OpenPreview => L("打开 CAST 预览", "Open CAST preview");
    public string PreviewEmpty => L("点击“合成预览”查看当前结果，也可打开已合并的 CAST。", "Build a preview of the current composition, or open a merged CAST.");
    public string PreviewLoading => L("正在读取 CAST…", "Reading CAST…");
    public string ProjectSkeletonPreview => L("仅动画：使用当前工程骨架，请确保绑定姿势匹配", "Animation only: using project skeleton; verify matching bind pose");
    public string PreviewFailed => L("CAST 预览失败", "CAST preview failed");
    public string PreviewSnapshot => L("合成快照（修改设置后请重新合成）", "Composition snapshot (rebuild after changes)");
    public string PreviewStats => L("{0:N0} 顶点 · {1:N0} 骨骼 · {2:0.##} FPS", "{0:N0} vertices · {1:N0} bones · {2:0.##} FPS");
    public string PreviewCacheRetained => L("预览已完成，临时缓存未能清理。", "Preview completed; temporary cache could not be cleaned.");
    public string PlayPreview => L("播放 / 空格", "Play / Space");
    public string PausePreview => L("暂停 / 空格", "Pause / Space");
    public string PreviousFrame => L("上一帧", "Previous frame");
    public string NextFrame => L("下一帧", "Next frame");
    public string PreviewFrame => L("预览帧", "Preview frame");
    public string FitPreview => L("适应主体 / F；右键查看全部几何体", "Fit subject / F; right-click to fit all geometry");
    public string FitSubjectMenu => L("适应主体 (F)", "Fit subject (F)");
    public string FitAllGeometryMenu => L("适应全部几何体 (Shift+F)", "Fit all geometry (Shift+F)");
    public string FirstPersonView => L("切换到第一人称视角（90° FOV）/ 1", "Switch to first-person view (90° FOV) / 1");
    public string ExitFirstPersonView => L("返回环绕视角 / 1", "Return to orbit view / 1");
    public string FirstPersonBadge => L("第一人称 · 90° FOV", "FIRST PERSON · 90° FOV");
    public string ZoomIn => L("放大 / +", "Zoom in / +");
    public string ZoomOut => L("缩小 / −", "Zoom out / −");
    public string ShowBones => L("显示 / 隐藏骨架", "Show / hide skeleton");
    public string PreviewHelp => L("拖动旋转 · 滚轮缩放 · 方向键旋转 · 灰模预览，不含贴图", "Drag or arrow keys to orbit · wheel to zoom · clay preview, no textures");
    public string FirstPersonPreviewHelp => L("第一人称 90° FOV · Maya 摄像机 T(0,0,0)、R(90°,0°,-90°) · 按 1 返回环绕视角 · 灰模预览，不含贴图", "First person 90° FOV · Maya camera T(0,0,0), R(90°,0°,-90°) · press 1 for orbit · clay preview, no textures");
    public string TrackName => L("名称", "Name");
    public string BaseAnimation => L("基础动画", "Base animation");
    public string CompositionOrder => L("合成顺序 · 非时间轴", "Composition order · not a timeline");
    public string FrameRange(int firstFrame, int lastFrame) => L($"帧范围 {firstFrame}–{lastFrame} · 按时长缩放", $"Frames {firstFrame}–{lastFrame} · scaled by duration");
    public string FrameCount(int count) => L($"{count} 帧", $"{count} frames");
    public string ReadingFrames => L("读取中", "Reading");
    public string FrameCountUnavailable => L("帧数未知", "Frames unavailable");
    public string TrackAccessibleName(string name, int startFrame, int frameCount) => L($"{name}，起始帧 {startFrame}，持续 {frameCount} 帧", $"{name}, starts at frame {startFrame}, duration {frameCount} frames");
    public string TrackTooltip(string path, int startFrame, int frameCount) => L($"{path}\n起始帧：{startFrame}\n持续：{frameCount} 帧", $"{path}\nStart: {startFrame}\nDuration: {frameCount} frames");
    public string TrackMetadataUnavailable(string name) => L($"{name}\n无法读取帧数", $"{name}\nFrame count unavailable");
    public string Inspector => L("属性检查器", "Inspector");
    public string TrackEditor => L("动画层轨道", "Layer tracks");
    public string SelectedLayer => L("所选动画层", "Selected layer");
    public string ProjectAssets => L("项目模型资源", "Project model assets");
    public string HandPoses => L("手部姿势", "Hand poses");
    public string IkControls => L("IK 控制", "IK controls");
    public string ExportTarget => L("导出目标", "Export target");
    public string HierarchyOrder => L("骨架层级顺序", "Skeleton hierarchy order");
    public string AttachmentTarget => L("当前挂接目标", "Current attachment target");
    public string Browse => L("浏览…", "Browse…");
    public string LeftPose => L("左手姿势文件（可选）", "Left-hand pose (optional)");
    public string RightPose => L("右手姿势文件（可选）", "Right-hand pose (optional)");
    public string EnableLeftIk => L("启用左手 IK", "Enable left-hand IK");
    public string EnableRightIk => L("启用右手 IK", "Enable right-hand IK");
    public string LeftTargetOverride => L("左手目标覆盖", "Left target override");
    public string RightTargetOverride => L("右手目标覆盖", "Right target override");
    public string OutputName => L("输出名称", "Output name");
    public string OutputFolder => L("输出目录（默认：源文件夹/output）", "Output folder (default: source folder/output)");
    public string Framerate => L("输出帧率", "Output framerate");
    public string FixedFramerate => L("30 FPS（固定）", "30 FPS (fixed)");
    public string Layers => L("动画层", "Animation layers");
    public string AddLayer => L("添加动画层", "Add layer");
    public string EmptyLayers => L("右键或拖入 CAST 添加动画层", "Right-click or drop CAST files to add layers");
    public string LayerFile => L("动画层文件", "Layer file");
    public string LayerMode => L("模式", "Mode");
    public string Offset => L("帧偏移", "Frame offset");
    public string MoveUp => L("上移", "Move up");
    public string MoveDown => L("下移", "Move down");
    public string ModelWorkspace => L("模型部件", "Model parts");
    public string ModelWorkspaceHelp => L("按层级顺序排列手臂、武器和附件。导入时会根据骨架结构自动识别类型；低置信度结果可在属性检查器中确认或覆盖。", "Order view hands, weapons and attachments by hierarchy. Imports are classified from skeleton structure; review or override low-confidence results in the inspector.");
    public string AddPart => L("添加模型部件", "Add model part");
    public string EmptyParts => L("尚未添加模型部件", "No model parts yet");
    public string EmptyPartsHelp => L("右键此区域、拖入 CAST，或点击“添加模型部件”。", "Right-click this area, drop CAST files, or choose Add model part.");
    public string ModelFile => L("模型文件", "Model file");
    public string PartType => L("部件类型", "Part type");
    public string ParentBone => L("父骨骼", "Parent bone");
    public string PartDetails => L("部件属性", "Part properties");
    public string DetectingPartTypes => L("正在识别模型部件类型…", "Detecting model part types…");
    public string PartsDetected => L("已添加 {0} 个模型部件；自动识别 {1} 个，{2} 个需要确认。", "Added {0} model part(s); detected {1}, with {2} requiring review.");
    public string PartDetected => L("已自动识别为{0}。", "Detected as {0}.");
    public string PartDetectionFailed => L("无法识别模型类型；已保留当前设置。", "Could not detect the model type; the current settings were preserved.");
    public string DetectionHigh => L("高", "High");
    public string DetectionMedium => L("中", "Medium");
    public string DetectionLow => L("低", "Low");
    public string ListSeparator => L("、", ", ");
    public string AutoPartDetected => L("自动识别：{0} · {1}置信度 · {2} 根骨骼\n依据：{3}", "Auto-detected: {0} · {1} confidence · {2} bones\nEvidence: {3}");
    public string AutoPartOverridden => L("已手动覆盖（识别建议：{0} · {1}置信度 · {2} 根骨骼）\n依据：{3}", "Manually overridden (suggestion: {0} · {1} confidence · {2} bones)\nEvidence: {3}");
    public string PartEvidence(ModelPartEvidence evidence) => evidence switch
    {
        ModelPartEvidence.BilateralArmChains => L("完整左右臂链", "complete bilateral arm chains"),
        ModelPartEvidence.SingleArmChain => L("单侧手臂链", "single arm chain"),
        ModelPartEvidence.ViewModelAnchors => L("第一人称骨架锚点", "view-model anchors"),
        ModelPartEvidence.WeaponRoot => L("武器根骨骼", "weapon root"),
        ModelPartEvidence.WeaponAttachmentTags => L("武器挂点", "weapon attachment tags"),
        ModelPartEvidence.WeaponMechanismBones => L("武器机构骨骼", "weapon mechanism bones"),
        ModelPartEvidence.AttachmentRoot => L("附件根挂点", "attachment root tag"),
        ModelPartEvidence.FileNameHint => L("文件名提示", "file-name hint"),
        _ => L("未发现骨架", "no skeleton found"),
    };
    public string PartOrderHelp => L("模型顺序会影响骨架挂接：手臂应在武器之前，附件应位于其目标父级之后。", "Model order controls skeleton attachment: place view hands before the weapon and attachments after their intended parent.");
    public string OutputSettings => L("输出设置", "Output settings");
    public string OutputSettingsHelp => L("项目保留自己的设置；“保存为默认值”会用于以后新建的项目。", "Each project keeps its own settings; Save as defaults applies them to future projects.");
    public string DefaultOutputFormat => L("输出格式", "Output format");
    public string FormatHelp => L("为当前项目选择目标管线和烘焙策略。", "Choose the target pipeline and bake strategy for this project.");
    public string AnimationOnlyCast => L("仅输出合并动画 CAST", "Animation-only merged CAST");
    public string AnimationOnlyHelp => L("只保留唯一的合并动画；导入或预览时需要匹配的骨架。", "Retains one merged animation; importing or previewing requires a matching skeleton.");
    public string SelectiveBake => L("仅烘焙相关骨骼", "Bake relevant bones only");
    public string SelectiveBakeHelp => L("减小动画曲线数量；目标骨架必须与绑定姿势完全匹配。", "Reduces animation curves; the target skeleton must exactly match the bind pose.");
    public string OldCod => L("兼容旧版 Call of Duty", "Legacy Call of Duty compatibility");
    public string Prefix => L("输出前缀", "Output prefix");
    public string Suffix => L("输出后缀", "Output suffix");
    public string NamingAndLanguage => L("命名与语言", "Naming and language");
    public string NamingHelp => L("统一批处理名称并控制界面语言。", "Keep batch names consistent and control the interface language.");
    public string IkDefaults => L("IK 骨骼默认值", "IK bone defaults");
    public string IkHelp => L("左右手链分区显示，屏幕阅读器名称也保持唯一。", "Left and right chains stay visually grouped and expose unique screen-reader names.");
    public string LeftIk => L("左手 IK", "Left-hand IK");
    public string RightIk => L("右手 IK", "Right-hand IK");
    public string StartBone => L("起始骨骼", "Start bone");
    public string MiddleBone => L("中间骨骼", "Middle bone");
    public string EndBone => L("末端骨骼", "End bone");
    public string TargetBone => L("目标骨骼", "Target bone");
    public string LeftStartBone => L("左手 IK 起始骨骼", "Left-hand IK start bone");
    public string LeftMiddleBone => L("左手 IK 中间骨骼", "Left-hand IK middle bone");
    public string LeftEndBone => L("左手 IK 末端骨骼", "Left-hand IK end bone");
    public string LeftTargetBone => L("左手 IK 目标骨骼", "Left-hand IK target bone");
    public string RightStartBone => L("右手 IK 起始骨骼", "Right-hand IK start bone");
    public string RightMiddleBone => L("右手 IK 中间骨骼", "Right-hand IK middle bone");
    public string RightEndBone => L("右手 IK 末端骨骼", "Right-hand IK end bone");
    public string RightTargetBone => L("右手 IK 目标骨骼", "Right-hand IK target bone");
    public string Language => L("界面语言", "Interface language");
    public string FollowSystem => L("跟随系统", "Follow system");
    public string SaveDefaults => L("保存为默认值", "Save as defaults");
    public string GenerateSprintBatch => L("生成冲刺批次", "Generate sprint batch");
    public string SprintGeneratorTitle => L("冲刺批次生成", "Sprint batch generator");
    public string SprintGeneratorNeedsIdle => L("请选择输出名称中包含“_idle”的基础动画。", "Select a base animation whose output name contains '_idle'.");
    public string SprintGeneratorComplete => L("已从 idle 模板生成 {0} 个冲刺与滑铲条目。", "Generated {0} sprint and slide entries from the idle template.");
    public string AboutTitle => L("关于 炼金之星", "About Alchemy Stars");
    public string ApplicationIcon => L("炼金之星应用图标", "Alchemy Stars application icon");
    public string AboutSubtitle => L("面向第一人称武器资产的 CAST 动画合并与 Maya 2025 工作流", "CAST animation merging and Maya 2025 workflow for first-person weapon assets");
    public string AboutOverview => L("炼金之星改进自 Scobalula/Alchemist。本测试版已将完整工作流迁移至 Avalonia，并通过 Native AOT 发布；WPF 版本在 .NET 11 正式版迁移前继续作为生产基线。", "Alchemy Stars improves Scobalula/Alchemist. This preview migrates the complete workflow to Avalonia and publishes with Native AOT; WPF remains the production baseline until the .NET 11 GA migration.");
    public string Capabilities => L("支持基于骨架结构的模型部件自动识别、完整或仅动画 CAST、FBX、SMD、SEAnim、普通/叠加/手势动画层、左右手 IK、相关骨骼烘焙、DQS 蒙皮和旧版 .aprj。合成工作区通过 GPU 加速的 Skia 绘制预览平滑 CAST 灰模、骨架和逐帧动画；安全第一人称取景保持武器完整显示，动画层轨道按源文件真实帧数和偏移显示。", "Supports skeleton-based model-part detection, full-scene or animation-only CAST, FBX, SMD, SEAnim, normal/additive/gesture layers, IK, relevant-bone baking, DQS skinning and legacy .aprj files. The composition workspace uses GPU-backed Skia drawing for smooth CAST geometry, skeleton and frame preview; safe first-person framing keeps the weapon visible, while layer tracks reflect true source frame counts and offsets.");
    public string Build => L("版本与环境", "Build and environment");
    public string ProjectRepositoryTitle => L("项目 GitHub", "Project GitHub");
    public string ProjectRepositoryHelp => L("源代码、问题反馈、版本记录与发布包均维护在项目仓库中。", "Source code, issue tracking, version history and release packages are maintained in the project repository.");
    public string OpenProjectRepository => L("打开炼金之星项目仓库", "Open the Alchemy Stars repository");
    public string UpstreamTitle => L("来源与致谢", "Origin and attribution");
    public string UpstreamHelp => L("炼金之星保留 Alchemist 与 RedFox 的转换基础，并在其上改进生产工作流。", "Alchemy Stars retains the Alchemist and RedFox conversion foundation and improves its production workflow.");
    public string Upstream => L("打开原项目", "Open upstream project");
    public string ExternalLinkOpened => L("已交给默认浏览器打开：{0}", "Opened in the default browser: {0}");
    public string ExternalLinkOpenFailed => L("无法打开网页", "Could not open webpage");
    public string ExternalLinkOpenFailedBody => L("系统无法启动默认浏览器。请检查 Windows 的默认应用设置，或复制以下网址到浏览器：\n{0}", "Windows could not start the default browser. Check the default-app settings, or copy this address into a browser:\n{0}");
    public string License => L("主程序遵循 MIT 许可证；Alchemist、RedFox、CAST 与 Maya 组件保留各自版权和许可证。", "The application uses the MIT license; Alchemist, RedFox, CAST and Maya assets retain their respective copyrights and licenses.");
    public string Close => L("关闭", "Close");
    public string Notification => L("通知", "Notification");
    public string Ready => L("工作区就绪", "Workspace ready");
    public string NewProjectCreated => L("已新建项目；未指定目录时导出到源动画同目录的 output 文件夹。", "New project created; unspecified outputs use an output folder beside the source animation.");
    public string ProjectLoaded => L("已打开项目：{0}", "Project opened: {0}");
    public string ProjectSaved => L("已保存项目：{0}", "Project saved: {0}");
    public string ProjectLoadFailed => L("项目打开失败", "Project open failed");
    public string ProjectSaveFailed => L("项目保存失败", "Project save failed");
    public string AnimationsAdded => L("已添加 {0} 个动画。", "Added {0} animation(s).");
    public string PartsAdded => L("已添加 {0} 个模型部件。", "Added {0} model part(s).");
    public string LayersAdded => L("已添加 {0} 个动画层。", "Added {0} animation layer(s).");
    public string NoAnimationSelected => L("未选择动画", "No animation selected");
    public string SelectAnimationForLayers => L("请先选择一个动画，再添加动画层。", "Select an animation before adding layers.");
    public string Exporting => L("正在合并并导出动画…", "Merging and exporting animations…");
    public string ExportComplete => L("已成功导出 {0} 个动画。", "Successfully exported {0} animation(s).");
    public string ExportCompleteTitle => L("导出成功", "Export complete");
    public string ExportCompleteBody => L("已导出 {0} 个文件：", "Exported {0} file(s):");
    public string ExportFailed => L("导出失败", "Export failed");
    public string ExportFailedTitle => L("导出失败", "Export failed");
    public string NeedPart => L("请至少添加一个模型部件。", "Add at least one model part.");
    public string NeedAnimation => L("请至少添加一个动画。", "Add at least one animation.");
    public string NeedOutputFolder => L("无法根据源动画推断输出目录，请先填写有效的动画路径。", "The output folder could not be inferred; enter a valid animation source path.");
    public string NeedOutputName => L("请填写输出名称。", "Enter an output name.");
    public string InvalidFramerate => L("输出帧率必须大于零。", "Output framerate must be greater than zero.");
    public string OutputWouldOverwrite => L("输出路径与输入文件相同，已阻止覆盖。请选择其他输出目录或名称。", "The output matches an input file. Choose another output folder or name.");
    public string DefaultsSaved => L("默认设置已保存。", "Default settings saved.");
    public string SettingsSavedTitle => L("设置已保存", "Settings saved");
    public string SettingsSavedBody => L("当前输出和语言设置将用于以后新建的项目。", "The current output and language settings will apply to future projects.");
    public string[] PartTypes => zh ? ["手臂", "武器", "附件"] : ["View hands", "Weapon", "Attachment"];
    public string[] LayerTypes => zh ? ["普通", "叠加", "手势", "手势姿势"] : ["Normal", "Additive", "Gesture", "Gesture pose"];
}
