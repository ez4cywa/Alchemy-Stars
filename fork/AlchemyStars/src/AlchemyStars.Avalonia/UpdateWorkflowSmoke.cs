using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace AlchemyStars.Avalonia;

internal static class UpdateWorkflowSmoke
{
    internal static async Task RunAsync(IAnimationExportEngine engine, string directory, string hands, string weapon)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var entry = new StreamWriter(archive.CreateEntry("AlchemyStars.Avalonia.exe").Open()))
            entry.Write("test executable; never installed");
        var payload = buffer.ToArray();
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(payload));
        const string tag = "99.0.0-preview.1";
        var releaseJson = $$"""
            [{"draft":false,"prerelease":true,"tag_name":"v{{tag}}","html_url":"https://github.com/ez4cywa/Alchemy-Stars/releases/tag/v{{tag}}",
            "assets":[{"name":"AlchemyStars-{{tag}}-win-x64.zip","browser_download_url":"https://github.com/ez4cywa/Alchemy-Stars/releases/download/v{{tag}}/AlchemyStars-{{tag}}-win-x64.zip","digest":"{{digest}}","size":{{payload.Length}}}]}]
            """;
        var exposeRelease = true;
        var blockCheck = false;
        var corruptDownload = false;
        var checks = 0;
        var downloads = 0;
        using var client = new HttpClient(new TestHandler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                checks++;
                if (blockCheck) await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(exposeRelease ? releaseJson : "[]") };
            }
            downloads++;
            var bytes = payload.ToArray();
            if (corruptDownload) bytes[0] ^= 0xff;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var preferences = new ApplicationPreferencesStore(Path.Combine(directory, "update-workflow.json"));
        var picker = new TestPicker();
        using var vm = new MainWindowViewModel(engine, new WorkspaceProjectStore(), preferences, picker, new GitHubUpdateService(client));
        await vm.CheckForUpdatesAsync(true);
        Require(checks == 0, "Automatic updates must not access the network by default.");
        await vm.CheckForUpdatesAsync(false);
        Require(vm.HasUpdate && vm.CanDownloadUpdate && !vm.IsUpdateWorking, "Manual discovery did not expose download.");
        vm.SkipUpdate();
        Require(!vm.HasUpdate && preferences.Snapshot().SkippedUpdateVersion == tag, "Skip version was not persisted.");
        vm.AutoUpdateEnabled = true;
        await vm.CheckForUpdatesAsync(true);
        Require(!vm.HasUpdate && downloads == 0, "Automatic check downloaded a skipped version.");
        await vm.CheckForUpdatesAsync(false);
        await vm.DownloadUpdateAsync();
        Require(vm.CanInstallUpdate && downloads == 1, "Manual check could not reconsider a skipped update.");
        vm.AddPartPaths([weapon]);
        await vm.InstallUpdateAsync();
        Require(vm.CanInstallUpdate && !vm.IsBusy && vm.CurrentProjectPath is null, "Cancelled save must keep the app and update ready.");
        picker.Destination = directory; // A directory is not a valid project file destination.
        await vm.InstallUpdateAsync();
        Require(vm.CanInstallUpdate && !vm.IsBusy, "Failed project save must not launch the installer.");
        exposeRelease = false;
        await vm.CheckForUpdatesAsync(false);
        Require(!vm.HasUpdate && !vm.CanInstallUpdate, "Withdrawn release retained a candidate or prepared update.");
        exposeRelease = true;
        preferences.SaveSkippedUpdate(string.Empty);
        await vm.CheckForUpdatesAsync(true);
        Require(vm.CanInstallUpdate && downloads == 2 && !vm.IsBusy, "Automatic download must prepare without restarting.");
        blockCheck = true;
        var pendingCheck = vm.CheckForUpdatesAsync(true);
        vm.AutoUpdateEnabled = false;
        await pendingCheck;
        Require(!vm.IsUpdateWorking && downloads == 2, "Disabling automatic updates did not cancel the active check.");
        blockCheck = false;
        vm.SkipUpdate();
        await vm.CheckForUpdatesAsync(false);
        corruptDownload = true;
        await vm.DownloadUpdateAsync();
        Require(vm.CanDownloadUpdate && !vm.CanInstallUpdate && !vm.IsUpdateWorking, "Failed checksum exposed install or left UI busy.");

        // Race regressions for the remembered-arms feature.
        vm.ForgetSavedArms();
        var part = vm.Parts.Single();
        var slowHands = new TaskCompletionSource<ModelPartClassification?>();
        vm.PartClassifier = path => path == hands ? slowHands.Task : Task.FromResult<ModelPartClassification?>(ModelPartClassifier.Classify(path));
        var first = vm.SetPartPathFromDropAsync(part, hands);
        await vm.SetPartPathFromDropAsync(part, weapon);
        slowHands.SetResult(ModelPartClassifier.Classify(hands));
        await first;
        Require(part.FilePath == weapon && part.Type == ModelPartKind.Weapon && !vm.HasSavedArms, "Stale classification remembered the wrong file.");
        slowHands = new TaskCompletionSource<ModelPartClassification?>();
        picker.Files = [hands];
        var selecting = vm.ChooseSavedArmsAsync();
        vm.ForgetSavedArms();
        slowHands.SetResult(ModelPartClassifier.Classify(hands));
        await selecting;
        Require(!vm.HasSavedArms, "A pending model selection restored a cleared record.");
        slowHands = new TaskCompletionSource<ModelPartClassification?>();
        var importing = vm.AddPartPathsAsync([hands]);
        vm.NewProject();
        slowHands.SetResult(ModelPartClassifier.Classify(hands));
        Require(await importing == 0 && vm.Parts.Count == 0 && !vm.HasSavedArms, "Pending import leaked into a new project.");
        Console.WriteLine("Update workflow: opt-in, skip/manual override, cancel/fail save, withdrawn release, automatic download, cancellation, checksum failure; arms async races PASS");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class TestPicker : IWorkspaceFilePicker
    {
        public string? Destination { get; set; }
        public IReadOnlyList<string> Files { get; set; } = [];
        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple) => Task.FromResult(Files);
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult(Destination);
        public Task<string?> PickFolderAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);
    }
}
