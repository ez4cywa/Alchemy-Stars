using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace AlchemyStars.Avalonia;

internal static class UpdateSelfTest
{
    internal static int RunLiveCheck()
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            foreach (var channel in new[] { UpdateChannel.Preview, UpdateChannel.Stable })
            {
                var latest = new GitHubUpdateService(currentVersion: "0.0.0").CheckAsync(channel, cancellation.Token).GetAwaiter().GetResult();
                Require(latest is not null, "GitHub has a compatible " + channel + " release");
                Console.WriteLine($"LIVE UPDATE {channel}: {latest!.Version}; checksum available: {latest.Sha256.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)}");
                var next = new GitHubUpdateService(currentVersion: latest.Version).CheckAsync(channel, cancellation.Token).GetAwaiter().GetResult();
                if (next is not null)
                {
                    Require(SemanticReleaseVersion.TryParse(latest.Version, out var current) && SemanticReleaseVersion.TryParse(next.Version, out var newer)
                        && newer!.CompareTo(current) > 0, "live update must never downgrade");
                    Console.WriteLine($"A newer {channel} release was published during this check: {next.Version}");
                }
                else Console.WriteLine($"LIVE UPDATE {channel}: latest version correctly yields no update.");
            }
            Console.WriteLine("LIVE UPDATE CHECK PASS (read-only GitHub metadata; no assets downloaded or installed).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    /// <summary>Exercises the actual copied Native AOT helper, but only against a disposable installation.</summary>
    internal static int RunHelper()
    {
        if (!OperatingSystem.IsWindows() || System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            Console.Error.WriteLine("The helper integration test requires the Windows Native AOT executable.");
            return 1;
        }
        var root = Path.Combine(Path.GetTempPath(), "AlchemyStars-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var current = Environment.ProcessPath!;
            var target = Path.Combine(root, "installation");
            Directory.CreateDirectory(target);
            var installedExecutable = Path.Combine(target, UpdateInstaller.ExecutableName);
            File.Copy(current, installedExecutable);
            File.WriteAllText(Path.Combine(target, "user.aprj"), "keep this project");
            var zip = Path.Combine(root, "update.zip");
            CreateZip(zip, (UpdateInstaller.ExecutableName, "new executable"), ("README.md", "release docs"));
            var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
            var info = new System.Diagnostics.ProcessStartInfo(installedExecutable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--update-launch-self-test", zip, digest })
                info.ArgumentList.Add(argument);
            using var launcher = System.Diagnostics.Process.Start(info)!;
            if (!launcher.WaitForExit(30000)) { launcher.Kill(); launcher.WaitForExit(); throw new TimeoutException("The test launcher did not exit."); }
            Require(launcher.ExitCode == 0, "actual LaunchInstaller, rejected duplicate handoff and failed-start retry succeeded");
            var helperId = int.Parse(File.ReadAllText(Path.Combine(root, "ready")));
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(helperId);
                if (!process.WaitForExit(30000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("The copied helper did not exit."); }
                // This is an attached process, not the Process.Start object: ExitCode is unsupported.
                // Completion is verified below against the helper's result and actual on-disk transaction.
            }
            catch (ArgumentException) { /* Helper already completed before we opened its process handle. */ }
            Require(File.ReadAllText(Path.Combine(target, UpdateInstaller.ExecutableName)) == "new executable", "helper replaced executable after parent exit");
            Require(File.ReadAllText(Path.Combine(target, "README.md")) == "release docs", "helper installed the complete payload");
            Require(File.ReadAllText(Path.Combine(target, "user.aprj")) == "keep this project", "helper preserved user project");
            Require(File.ReadAllText(Path.Combine(root, "result.txt")).StartsWith("SUCCESS\n", StringComparison.Ordinal), "helper result protocol");
            Require(!File.Exists(zip), "helper cleaned the downloaded payload");
            Require(!Directory.Exists(Path.Combine(root, "staged")) && !Directory.Exists(Path.Combine(root, "backup")), "successful helper transaction cleaned staging and backup");
            Console.WriteLine("UPDATE HELPER SELF-TEST PASS: actual LaunchInstaller copy/ready/launch-lock handoff, failed-start retry, duplicate rejection, live parent exit, transactional install, project preservation and result protocol.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }

    internal static int RunLauncher(string[] args)
    {
        try
        {
            Require(args.Length == 3, "launcher arguments");
            var archive = Path.GetFullPath(args[1]);
            var prepared = new PreparedUpdate(archive, new UpdateRelease("1.3.0-preview.16", "", "", args[2], new FileInfo(archive).Length));
            var service = new GitHubUpdateService();
            Throws(() => service.LaunchInstaller(prepared, false, Path.Combine(Path.GetDirectoryName(archive)!, "missing.aprj")), "failed pre-launch rejects an unsaved restart project");
            Require(!File.Exists(Path.Combine(Path.GetDirectoryName(archive)!, "ready")), "failed launch did not hand off ownership");
            service.LaunchInstaller(prepared, false);
            Throws(() => service.LaunchInstaller(prepared, false), "duplicate live-helper handoff rejected");
            Require(new FileInfo(Environment.ProcessPath!).Length > 1024, "running installation is unchanged until this parent exits");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "AlchemyStars-Update-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            UpdateRelease Release(string version) => new(version, "", "", "", 0);
            var releases = new[] { Release("1.3.0-preview.9"), Release("1.3.0-preview.16"), Release("1.3.0-preview.10"), Release("1.3.0"), Release("1.4.0-preview.1") };
            Require(GitHubUpdateService.SelectLatest(releases, "1.3.0-preview.15", UpdateChannel.Preview)?.Version == "1.4.0-preview.1", "preview channel ordering");
            Require(GitHubUpdateService.SelectLatest(releases.Take(3), "1.3.0-preview.9", UpdateChannel.Preview)?.Version == "1.3.0-preview.16", "numeric prerelease ordering");
            Require(GitHubUpdateService.SelectLatest(releases, "1.2.0", UpdateChannel.Stable)?.Version == "1.3.0", "stable channel excludes previews");
            Require(GitHubUpdateService.SelectLatest(releases, "1.4.0-preview.1", UpdateChannel.Preview) is null, "no update at latest");
            Require(GitHubUpdateService.SelectLatest(releases, "2.0.0", UpdateChannel.Stable) is null, "no downgrade");
            Require(!SemanticReleaseVersion.TryParse("1.3.0-preview.01", out _) && !SemanticReleaseVersion.TryParse("1.3", out _), "invalid semver rejected");
            foreach (var path in new[] { "../evil", "a/../../evil", "C:/evil", "/evil", "a:stream", "x/CON.txt", "x/../y", "x./y", "a//b", "\\\\server\\share" })
                Throws(() => UpdateInstaller.ValidateEntryPath(path), "unsafe archive path " + path);

            var zip = Path.Combine(root, "valid.zip");
            CreateZip(zip, (UpdateInstaller.ExecutableName, "new executable"), ("Example/user.aprj", "new example"), ("README.md", "new readme"));
            var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
            GitHubUpdateService.VerifyArchive(zip, digest);
            Throws(() => GitHubUpdateService.VerifyArchive(zip, "sha256:" + new string('0', 64)), "checksum mismatch rejected");
            Throws(() => GitHubUpdateService.VerifyArchive(zip, ""), "missing checksum rejected");
            UpdateInstaller.ValidateArchive(zip);
            var unsafeZip = Path.Combine(root, "unsafe.zip");
            CreateZip(unsafeZip, (UpdateInstaller.ExecutableName, "exe"), ("../escape", "bad"));
            Throws(() => UpdateInstaller.ValidateArchive(unsafeZip), "zip traversal rejected");
            var duplicateZip = Path.Combine(root, "duplicate.zip");
            CreateZip(duplicateZip, (UpdateInstaller.ExecutableName, "exe"), ("README.md", "a"), ("readme.md", "b"));
            Throws(() => UpdateInstaller.ValidateArchive(duplicateZip), "case-insensitive duplicate rejected");

            var stage = Path.Combine(root, "stage");
            Directory.CreateDirectory(stage);
            UpdateInstaller.ExtractArchive(zip, stage);
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(target, "Example"));
            File.WriteAllText(Path.Combine(target, UpdateInstaller.ExecutableName), "old executable");
            File.WriteAllText(Path.Combine(target, "Example", "user.aprj"), "my project");
            File.WriteAllText(Path.Combine(target, "private.aprj"), "private project");
            File.WriteAllText(Path.Combine(target, "settings.json"), "my settings");
            Throws(() => UpdateInstaller.ApplyFiles(stage, target, Path.Combine(root, "rollback"), count =>
            {
                if (count == 2) throw new IOException("Injected post-replacement failure");
            }), "injected failure reports rollback");
            Require(File.ReadAllText(Path.Combine(target, UpdateInstaller.ExecutableName)) == "old executable", "rollback restored executable");
            Require(!File.Exists(Path.Combine(target, "README.md")), "rollback removed newly added file");
            UpdateInstaller.ApplyFiles(stage, target, Path.Combine(root, "backup"));
            Require(File.ReadAllText(Path.Combine(target, UpdateInstaller.ExecutableName)) == "new executable", "successful replacement");
            Require(File.ReadAllText(Path.Combine(target, "Example", "user.aprj")) == "my project", "existing example project preserved");
            Require(File.ReadAllText(Path.Combine(target, "private.aprj")) == "private project" && File.ReadAllText(Path.Combine(target, "settings.json")) == "my settings", "user files preserved");

            DownloadTests(zip, digest).GetAwaiter().GetResult();
            DiscoveryTests().GetAwaiter().GetResult();
            Console.WriteLine("UPDATE SELF-TEST PASS: channel/semver, no update, trusted URL, download length/hash, unsafe ZIP paths, duplicates, apply and rollback, user-file preservation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }

    private static async Task DownloadTests(string zip, string digest)
    {
        var bytes = File.ReadAllBytes(zip);
        using var client = new HttpClient(new FakeHandler(bytes));
        var service = new GitHubUpdateService(client, "1.3.0-preview.15");
        var release = new UpdateRelease("1.3.0-preview.16", "https://github.com/ez4cywa/Alchemy-Stars/releases/tag/v1.3.0-preview.16",
            "https://github.com/ez4cywa/Alchemy-Stars/releases/download/v1.3.0-preview.16/AlchemyStars-1.3.0-preview.16-win-x64.zip", digest, bytes.Length);
        var prepared = await service.DownloadAsync(release);
        Require(File.Exists(prepared.ArchivePath), "verified download retained");
        GitHubUpdateService.DiscardPrepared(prepared);
        Require(!File.Exists(prepared.ArchivePath), "discard removes prepared payload");
        Throws(() => GitHubUpdateService.DiscardPrepared(prepared with { ArchivePath = zip }), "discard protects non-updater directories");
        await ThrowsAsync(() => service.DownloadAsync(release with { Size = bytes.Length + 1 }), "incomplete download rejected");
        await ThrowsAsync(() => service.DownloadAsync(release with { Sha256 = "sha256:" + new string('0', 64) }), "download digest rejected");
        await ThrowsAsync(() => service.DownloadAsync(release with { DownloadUrl = "https://example.com/update.zip" }), "untrusted download URL rejected");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await ThrowsAsync(() => service.DownloadAsync(release, cancellationToken: canceled.Token), "cancellation honored");
    }

    private static async Task DiscoveryTests()
    {
        string Item(string version, bool preview, bool draft = false) => "{\"draft\":" + (draft ? "true" : "false")
            + ",\"prerelease\":" + (preview ? "true" : "false") + ",\"tag_name\":\"v" + version
            + "\",\"html_url\":\"https://github.com/ez4cywa/Alchemy-Stars/releases\",\"assets\":[{\"name\":\"AlchemyStars-"
            + version + "-win-x64.zip\",\"browser_download_url\":\"https://github.com/ez4cywa/Alchemy-Stars/releases/download/v"
            + version + "/update.zip\",\"digest\":\"sha256:" + new string('a', 64) + "\",\"size\":12}]}";
        var first = "[" + string.Join(',', Enumerable.Repeat(Item("1.3.0-preview.9", true), 100)) + "]";
        var second = "[" + string.Join(',', Item("1.3.0-preview.16", true), Item("1.3.0-preview.10", true),
            Item("1.3.0-preview.99", true, true), Item("1.3.0", false)) + "]";
        using var handler = new ReleaseHandler(first, second);
        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, "1.3.0-preview.15");
        Require((await service.CheckAsync(UpdateChannel.Preview))?.Version == "1.3.0-preview.16", "GitHub pagination, draft exclusion and version ordering");
        Require(handler.RequestCount == 2, "all release pages inspected");
        Require((await new GitHubUpdateService(client, "1.2.0").CheckAsync(UpdateChannel.Stable))?.Version == "1.3.0", "GitHub stable selection");
        Require(await new GitHubUpdateService(client, "1.3.0-preview.16").CheckAsync(UpdateChannel.Preview) is null, "GitHub no-update result");
    }

    private static void CreateZip(string path, params (string Path, string Content)[] files)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(file.Path).Open(), Encoding.UTF8);
            writer.Write(file.Content);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action, string message)
    {
        try { action(); } catch (Exception) { return; }
        throw new InvalidOperationException("Expected failure: " + message);
    }
    private static async Task ThrowsAsync(Func<Task<PreparedUpdate>> action, string message)
    {
        try { await action(); } catch (Exception) { return; }
        throw new InvalidOperationException("Expected failure: " + message);
    }
    private sealed class FakeHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    private sealed class ReleaseHandler(string first, string second) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Require(request.Headers.UserAgent.Count > 0, "GitHub User-Agent provided");
            var json = request.RequestUri!.Query.EndsWith("page=1", StringComparison.Ordinal) ? first : second;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
