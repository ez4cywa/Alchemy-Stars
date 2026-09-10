namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    private long outputDirectorySelectionVersion;
    public string UnifiedOutputDirectory => preferences.Snapshot().UnifiedOutputDirectory;
    public bool HasUnifiedOutputDirectory => !string.IsNullOrWhiteSpace(UnifiedOutputDirectory);
    public string UnifiedOutputDirectoryStatus => HasUnifiedOutputDirectory ? UnifiedOutputDirectory : Text.NoUnifiedOutputDirectory;

    public async Task ChooseUnifiedOutputDirectoryAsync()
    {
        var selection = ++outputDirectorySelectionVersion;
        var path = await picker.PickFolderAsync(UnifiedOutputDirectory);
        if (string.IsNullOrWhiteSpace(path) || selection != outputDirectorySelectionVersion) return;
        try
        {
            preferences.SaveUnifiedOutputDirectory(path);
            RefreshOutputDirectorySettings();
        }
        catch (Exception error) { ShowDialog(Text.UnifiedOutputDirectoryLabel, error.Message, true); }
    }

    public void ClearUnifiedOutputDirectory()
    {
        outputDirectorySelectionVersion++;
        preferences.SaveUnifiedOutputDirectory(null);
        RefreshOutputDirectorySettings();
    }

    private void RefreshOutputDirectorySettings()
    {
        OnPropertyChanged(nameof(UnifiedOutputDirectory));
        OnPropertyChanged(nameof(HasUnifiedOutputDirectory));
        OnPropertyChanged(nameof(UnifiedOutputDirectoryStatus));
        OnPropertyChanged(nameof(SelectedOutputDirectory));
    }

    // Only export requests are redirected. Project fields and preview requests keep their own paths.
    internal AnimationExportRequest ApplyUnifiedOutputDirectory(AnimationExportRequest request)
    {
        var directory = UnifiedOutputDirectory;
        return string.IsNullOrWhiteSpace(directory) ? request : request with
        {
            Animations = request.Animations.Select(job => job with { OutputFolder = directory }).ToArray(),
        };
    }

    internal void ApplyDualOutputDirectory(WorkspaceDualAnimation[] snapshotTasks, bool preview, string cache)
    {
        var directory = preview ? cache : UnifiedOutputDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;
        foreach (var task in snapshotTasks) task.OutputFolder = directory;
    }
}

public sealed partial class UiText
{
    public string UnifiedOutputDirectoryLabel => L("统一输出目录", "Unified output folder");
    public string UnifiedOutputDirectoryHelp => L("选择后永久保存，所有普通动画和双持导出统一使用此目录。不会更改项目中原有目录；清除后恢复各条目的目录。预览不受影响。", "Saved across restarts and used for all animation and dual-wield exports. Project folders stay unchanged; clearing restores each item's folder. Previews are unaffected.");
    public string NoUnifiedOutputDirectory => L("未设置，使用各条目的输出目录。", "Not set; use each item's output folder.");
    public string ChooseUnifiedOutputDirectory => L("选择统一目录…", "Choose unified folder…");
    public string ClearUnifiedOutputDirectory => L("清除统一目录", "Clear unified folder");
}
