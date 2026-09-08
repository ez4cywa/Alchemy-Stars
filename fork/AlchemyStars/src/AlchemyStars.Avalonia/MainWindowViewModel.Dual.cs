using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    private WorkspaceDualAnimation? selectedDual;
    private long dualRevision;
    private readonly List<INotifyPropertyChanged> dualProperties = [];
    private readonly List<INotifyCollectionChanged> dualCollections = [];
    public ObservableCollection<WorkspaceDualAnimation> DualAnimations => Workspace.DualAnimations;
    public bool IsDualPage => SelectedPage == WorkspacePage.DualAnimations;
    public string CurrentExportLabel => IsDualPage ? Text.DualExportSelected : Text.Export;
    public bool HasSelectedDual => SelectedDual is not null;
    public int DualLeftIndex => DualLeftSource is { } a ? Animations.IndexOf(a) : -1;
    public int DualRightIndex => DualRightSource is { } a ? Animations.IndexOf(a) : -1;
    public WorkspaceDualAnimation? SelectedDual
    {
        get => selectedDual;
        set { selectedDual = value; Preview.Clear(); OnPropertyChanged(); RaiseDualSelection(); }
    }
    public WorkspaceAnimation? DualLeftSource
    {
        get => Animations.FirstOrDefault(a => a.Id == SelectedDual?.LeftAnimationId);
        set { if (SelectedDual is not null && value is not null) SelectedDual.LeftAnimationId = value.Id; RaiseDualSelection(); }
    }
    public WorkspaceAnimation? DualRightSource
    {
        get => Animations.FirstOrDefault(a => a.Id == SelectedDual?.RightAnimationId);
        set { if (SelectedDual is not null && value is not null) SelectedDual.RightAnimationId = value.Id; RaiseDualSelection(); }
    }
    public string DualSourceSummary => $"{Text.DualLeft}: {DualLeftSource?.DisplayName ?? "—"}\n{Text.DualRight}: {DualRightSource?.DisplayName ?? "—"}";

    private void RaiseDualSelection()
    {
        OnPropertyChanged(nameof(HasSelectedDual)); OnPropertyChanged(nameof(DualLeftSource));
        OnPropertyChanged(nameof(DualRightSource)); OnPropertyChanged(nameof(DualSourceSummary));
        OnPropertyChanged(nameof(DualLeftIndex)); OnPropertyChanged(nameof(DualRightIndex));
    }

    public void AddDualTask()
    {
        var task = new WorkspaceDualAnimation
        {
            Name = "dual_" + (DualAnimations.Count + 1),
            LeftAnimationId = (SelectedAnimation ?? Animations.FirstOrDefault())?.Id ?? "",
        };
        task.RightAnimationId = Animations.FirstOrDefault(a => a.Id != task.LeftAnimationId)?.Id ?? "";
        DualAnimations.Add(task); SelectedDual = task;
    }

    public void RemoveDualTask()
    {
        if (SelectedDual is null) return;
        DualAnimations.Remove(SelectedDual); SelectedDual = DualAnimations.FirstOrDefault();
    }

    public void PairDualTasks()
    {
        try
        {
            var groups = new Dictionary<string, (List<WorkspaceAnimation> Left, List<WorkspaceAnimation> Right)>(StringComparer.OrdinalIgnoreCase);
            foreach (var animation in Animations)
            {
                var stem = Path.GetFileNameWithoutExtension(animation.Name);
                var index = stem.IndexOf("_l_", StringComparison.OrdinalIgnoreCase);
                var left = index >= 0;
                if (!left) index = stem.IndexOf("_r_", StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                var key = stem[..index] + "_" + stem[(index + 3)..];
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = ([], []);
                (left ? group.Left : group.Right).Add(animation);
            }
            if (groups.Values.Any(g => g.Left.Count > 1 || g.Right.Count > 1))
                throw new InvalidDataException(Text.DualAmbiguousPairs);
            var added = 0;
            foreach (var (name, group) in groups.Where(g => g.Value.Left.Count == 1 && g.Value.Right.Count == 1))
            {
                var l = group.Left[0]; var r = group.Right[0];
                if (DualAnimations.Any(t => t.LeftAnimationId == l.Id && t.RightAnimationId == r.Id)) continue;
                var task = new WorkspaceDualAnimation { Name = name + "_dual", LeftAnimationId = l.Id, RightAnimationId = r.Id };
                DualAnimations.Add(task); SelectedDual = task; added++;
            }
            var missing = groups.Count(g => g.Value.Left.Count == 0 || g.Value.Right.Count == 0);
            FooterStatus = string.Format(Text.DualPairResult, added, missing);
            if (added == 0 || missing > 0) ShowDialog(Text.DualAnimations, FooterStatus, false);
        }
        catch (Exception ex) { ShowDialog(Text.DualAnimations, ex.Message, true); }
    }

    public async Task SetDualOutputAsync(bool all = false)
    {
        if (SelectedDual is null) return;
        var path = await picker.PickFolderAsync(SelectedDual.OutputFolder);
        if (path is null) return;
        foreach (var task in all ? DualAnimations.ToArray() : [SelectedDual]) task.OutputFolder = path;
    }

    public async Task ProcessDualAsync(bool preview, bool all = false)
    {
        if (IsBusy || SelectedDual is null) return;
        var selection = SelectedDual;
        var revision = dualRevision;
        var cache = Path.Combine(Path.GetTempPath(), "AlchemyStarsDualPreview", Guid.NewGuid().ToString("N"));
        try
        {
            IsBusy = true; Preview.Pause(); BusyMessage = preview ? Text.BuildingPreview : Text.Exporting;
            var snapshot = WorkspaceProjectStore.Snapshot(Workspace);
            var index = DualAnimations.IndexOf(selection);
            var tasks = all ? snapshot.DualAnimations.ToArray() : [snapshot.DualAnimations[index]];
            ApplyDualOutputDirectory(tasks, preview, cache);
            var destinationNames = tasks.SelectMany(t => DualWieldEngine.GetOutputFiles(snapshot, t, preview)).ToArray();
            if (destinationNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinationNames.Length)
                throw new InvalidDataException(Text.DualDuplicateOutputs);
            var results = await Task.Run(() => tasks.Select(t => new DualWieldEngine().Export(snapshot, t, preview)).ToArray());
            if (revision == dualRevision && ReferenceEquals(selection, SelectedDual) && (preview || (snapshot.OutputFormat == ".cast" && !snapshot.CastAnimationOnly)))
            {
                var result = results[all ? index : 0];
                await Preview.LoadAsync(result.OutputFile, Text.PreviewSnapshot + " · " + selection.Name);
            }
            var warnings = results.SelectMany(r => r.UnmappedTargets).Distinct().ToArray();
            FooterStatus = string.Format(Text.ExportComplete, results.Length)
                + (warnings.Length == 0 ? "" : " · " + Text.DualUnmapped + ": " + string.Join(", ", warnings));
            if (!preview) ShowDialog(Text.ExportCompleteTitle,
                string.Join(Environment.NewLine, results.SelectMany(r => r.OutputFiles))
                + (warnings.Length == 0 ? "" : "\n\n" + Text.DualUnmapped + ": " + string.Join(", ", warnings)), false);
        }
        catch (Exception ex) { FooterStatus = Text.ExportFailed; ShowDialog(Text.ExportFailedTitle, ex.Message, true); }
        finally
        {
            IsBusy = false; BusyMessage = "";
            if (Directory.Exists(cache))
                try { Directory.Delete(cache, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void WatchDualSources()
    {
        void Watch(INotifyPropertyChanged item) { item.PropertyChanged += DualSourceChanged; dualProperties.Add(item); }
        void Collection(INotifyCollectionChanged item) { item.CollectionChanged += DualCollectionChanged; dualCollections.Add(item); }
        Watch(Workspace); Collection(Animations); Collection(Parts); Collection(DualAnimations);
        foreach (var part in Parts) Watch(part);
        foreach (var task in DualAnimations) Watch(task);
        foreach (var animation in Animations)
        {
            Watch(animation); Collection(animation.Layers);
            foreach (var layer in animation.Layers) Watch(layer);
        }
    }
    private void UnwatchDualSources()
    {
        foreach (var item in dualProperties) item.PropertyChanged -= DualSourceChanged;
        foreach (var item in dualCollections) item.CollectionChanged -= DualCollectionChanged;
        dualProperties.Clear(); dualCollections.Clear();
    }
    private void DualCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnwatchDualSources(); WatchDualSources(); DualSourceChanged(sender, new PropertyChangedEventArgs(null));
    }
    private void DualSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        dualRevision++;
        if (IsDualPage) { Preview.Clear(); FooterStatus = Text.DualStale; }
        RaiseDualSelection();
    }
}
