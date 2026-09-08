using Avalonia.Controls.ApplicationLifetimes;

namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    private GitHubUpdateService updateService = new();
    private CancellationTokenSource? updateCancellation;
    private UpdateRelease? availableUpdate;
    private PreparedUpdate? preparedUpdate;
    private string? updateStatus;
    private bool isUpdateWorking;
    private bool installingUpdate;
    internal bool IsUpdateHandoffInProgress => installingUpdate && !updateHandoffComplete;
    private bool updateHandoffComplete;

    public bool AutoUpdateEnabled
    {
        get => preferences.Snapshot().AutoUpdateEnabled;
        set
        {
            if (value == AutoUpdateEnabled) return;
            preferences.SaveAutoUpdate(value);
            OnPropertyChanged();
            if (!value) CancelUpdate();
        }
    }
    private UpdateChannel CurrentUpdateChannel => Version.Contains('-', StringComparison.Ordinal) ? UpdateChannel.Preview : UpdateChannel.Stable;
    public string UpdateStatus => updateStatus ?? $"{Text.CurrentVersion}: {Version} · {(CurrentUpdateChannel == UpdateChannel.Preview ? Text.PreviewChannel : Text.StableChannel)}";
    public bool IsUpdateWorking => isUpdateWorking;
    public bool CanCheckUpdate => !isUpdateWorking && !installingUpdate;
    public bool HasUpdate => availableUpdate is not null;
    public bool CanDownloadUpdate => HasUpdate && preparedUpdate is null && !isUpdateWorking;
    public bool CanInstallUpdate => preparedUpdate is not null && !isUpdateWorking && !installingUpdate;

    public async Task CheckForUpdatesAsync(bool automatic)
    {
        if (!CanCheckUpdate || automatic && !AutoUpdateEnabled) return;
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        updateCancellation = cancellation;
        isUpdateWorking = true;
        SetUpdateStatus(Text.CheckingUpdate);
        var checkedSuccessfully = false;
        try
        {
            var release = await updateService.CheckAsync(CurrentUpdateChannel, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (automatic && !AutoUpdateEnabled) return;
            if (release is null)
            {
                DiscardDownloadedUpdate();
                availableUpdate = null;
                SetUpdateStatus(Text.NoUpdate);
                return;
            }
            if (automatic && preferences.Snapshot().SkippedUpdateVersion == release.Version)
            {
                SetUpdateStatus(Text.SkippedUpdate + " " + release.Version);
                return;
            }
            if (preparedUpdate is not null && preparedUpdate.Release.Version != release.Version) DiscardDownloadedUpdate();
            availableUpdate = release;
            checkedSuccessfully = true;
            SetUpdateStatus(Text.UpdateAvailable + " " + release.Version);
        }
        catch (OperationCanceledException) { SetUpdateStatus(Text.UpdateCancelled); }
        catch (Exception error) { SetUpdateStatus(Text.UpdateFailed + " " + error.Message); }
        finally
        {
            isUpdateWorking = false;
            updateCancellation = null;
            cancellation.Dispose();
            RaiseUpdateState();
        }
        if (checkedSuccessfully && automatic && AutoUpdateEnabled && CanDownloadUpdate)
            await DownloadUpdateAsync();
    }

    public async Task DownloadUpdateAsync()
    {
        if (!CanDownloadUpdate) return;
        var release = availableUpdate!;
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        updateCancellation = cancellation;
        isUpdateWorking = true;
        SetUpdateStatus(Text.DownloadingUpdate + " " + release.Version);
        try
        {
            preparedUpdate = await updateService.DownloadAsync(release, new Progress<double>(progress =>
            {
                if (isUpdateWorking && !cancellation.IsCancellationRequested)
                    SetUpdateStatus($"{Text.DownloadingUpdate} {release.Version} · {progress:P0}");
            }), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                DiscardDownloadedUpdate();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            SetUpdateStatus(Text.UpdateReady + " " + release.Version);
            FooterStatus = Text.UpdateReady + " " + release.Version;
        }
        catch (OperationCanceledException) { SetUpdateStatus(Text.UpdateCancelled); }
        catch (Exception error) { SetUpdateStatus(Text.UpdateFailed + " " + error.Message); }
        finally
        {
            isUpdateWorking = false;
            updateCancellation = null;
            cancellation.Dispose();
            RaiseUpdateState();
        }
    }

    public async Task InstallUpdateAsync()
    {
        if (!CanInstallUpdate || IsBusy || IsDialogOpen) return;
        installingUpdate = true;
        IsBusy = true;
        BusyMessage = Text.InstallUpdate;
        RaiseUpdateState();
        try
        {
            string? restartProject = CurrentProjectPath;
            if (restartProject is not null || Parts.Count > 0 || Animations.Count > 0 || DualAnimations.Count > 0)
            {
                restartProject ??= await picker.PickProjectDestinationAsync(null);
                if (string.IsNullOrWhiteSpace(restartProject))
                {
                    SetUpdateStatus(Text.UpdateSaveCancelled);
                    return;
                }
                projectStore.Save(Workspace, restartProject);
                CurrentProjectPath = Path.GetFullPath(restartProject);
                preferences.RememberDirectory("project", restartProject);
            }
            await Task.Run(() => updateService.LaunchInstaller(preparedUpdate!, true, restartProject));
            updateHandoffComplete = true;
            preparedUpdate = null; // The helper owns this download now.
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (Exception error) { SetUpdateStatus(Text.UpdateFailed + " " + error.Message); }
        finally { installingUpdate = false; IsBusy = false; RaiseUpdateState(); }
    }

    public void CancelUpdate() => updateCancellation?.Cancel();
    public void ShowUpdateResult(string? path)
    {
        if (path is null) return;
        try
        {
            // Only the updater's own short result files are accepted from this command-line option.
            var fullPath = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(fullPath)!;
            if (!Path.GetFileName(folder).StartsWith("AlchemyStars-Update-", StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(fullPath), "result.txt", StringComparison.Ordinal)
                || !folder.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath) || new FileInfo(fullPath).Length > 65536) return;
            var result = File.ReadAllText(fullPath);
            SetUpdateStatus(result.StartsWith("SUCCESS", StringComparison.Ordinal)
                ? Text.UpdateInstalled + " " + Version : Text.UpdateFailed + " " + result);
            if (!result.StartsWith("SUCCESS", StringComparison.Ordinal))
                ShowDialog(Text.CheckUpdate, UpdateStatus, true);
        }
        catch (IOException error) { SetUpdateStatus(Text.UpdateFailed + " " + error.Message); }
        catch (UnauthorizedAccessException error) { SetUpdateStatus(Text.UpdateFailed + " " + error.Message); }
    }
    public void SkipUpdate()
    {
        if (isUpdateWorking || installingUpdate || availableUpdate is null) return;
        preferences.SaveSkippedUpdate(availableUpdate.Version);
        SetUpdateStatus(Text.SkippedUpdate + " " + availableUpdate.Version);
        DiscardDownloadedUpdate();
        availableUpdate = null;
        RaiseUpdateState();
    }

    private void DiscardDownloadedUpdate()
    {
        if (preparedUpdate is null) return;
        try { GitHubUpdateService.DiscardPrepared(preparedUpdate); }
        catch (IOException) { /* A locked download may be reclaimed by Windows' temporary-file cleanup. */ }
        catch (UnauthorizedAccessException) { }
        preparedUpdate = null;
    }
    private void SetUpdateStatus(string message) { updateStatus = message; RaiseUpdateState(); }
    private void RaiseUpdateState()
    {
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(AutoUpdateEnabled));
        OnPropertyChanged(nameof(IsUpdateWorking));
        OnPropertyChanged(nameof(CanCheckUpdate));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        OnPropertyChanged(nameof(CanInstallUpdate));
    }
}

public sealed partial class UiText
{
    public string AutoUpdateLabel => L("启动时自动检查并下载更新", "Check and download updates at startup");
    public string AutoUpdateHelp => L("默认关闭。预览版只更新预览版，正式版只更新正式版。下载完成后由你选择保存项目并重启，或暂不更新；不会自动中断工作。", "Off by default. Preview and stable releases update within their own channel. Choose to save and restart after download, or skip; your work is never interrupted automatically.");
    public string CurrentVersion => L("当前版本", "Current version");
    public string PreviewChannel => L("预览版渠道", "Preview channel");
    public string StableChannel => L("正式版渠道", "Stable channel");
    public string CheckUpdate => L("检查更新", "Check for updates");
    public string DownloadUpdate => L("下载更新", "Download update");
    public string InstallUpdate => L("保存项目并重启更新", "Save project and restart to update");
    public string SkipUpdate => L("跳过此版本", "Skip this version");
    public string CancelUpdate => L("取消", "Cancel");
    public string CheckingUpdate => L("正在检查 GitHub…", "Checking GitHub…");
    public string NoUpdate => L("当前已是本渠道的最新版本。", "You have the latest version in this channel.");
    public string UpdateAvailable => L("发现新版本：", "Update available:");
    public string DownloadingUpdate => L("正在下载：", "Downloading:");
    public string UpdateReady => L("更新已就绪，请到设置中保存项目并重启：", "Update ready; save and restart from Settings:");
    public string SkippedUpdate => L("已跳过；手动检查可重新选择：", "Skipped; check manually to reconsider:");
    public string UpdateCancelled => L("检查或下载已取消/超时，当前版本不变。", "Check/download cancelled or timed out; the current version is unchanged.");
    public string UpdateFailed => L("更新未完成，可重试：", "Update did not complete; you can retry:");
    public string UpdateSaveCancelled => L("未保存项目；更新未安装，软件保持运行。", "Project not saved; update not installed and the app stays open.");
    public string UpdateInstalled => L("更新已安装：", "Update installed:");
}
