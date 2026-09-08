using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using AlchemyStars.Engine;

namespace AlchemyStars.Avalonia;

public enum UpdateChannel { Stable, Preview }

public sealed record UpdateRelease(string Version, string PageUrl, string DownloadUrl, string Sha256, long Size);
public sealed record PreparedUpdate(string ArchivePath, UpdateRelease Release);

/// <summary>GitHub release discovery and verified downloads; no UI or project-state side effects.</summary>
public sealed class GitHubUpdateService
{
    private const string Repository = "ez4cywa/Alchemy-Stars";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient client;
    private readonly string currentVersion;

    public GitHubUpdateService(HttpClient? client = null, string? currentVersion = null)
    {
        this.client = client ?? SharedClient;
        this.currentVersion = currentVersion ?? AnimationExportEngine.EngineVersion;
    }

    public async Task<UpdateRelease?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        var releases = new List<UpdateRelease>();
        // GitHub lists by publication date, not semantic version. Read every page before selecting.
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}");
            request.Headers.UserAgent.ParseAdd("AlchemyStars-Updater/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var items = document.RootElement;
            foreach (var item in items.EnumerateArray())
            {
                if (item.GetProperty("draft").GetBoolean()) continue;
                var tag = item.GetProperty("tag_name").GetString() ?? "";
                if (!SemanticReleaseVersion.TryParse(tag, out var version)) continue;
                var preview = item.GetProperty("prerelease").GetBoolean() || version!.IsPrerelease;
                if (preview != (channel == UpdateChannel.Preview)) continue;
                var expectedAsset = $"AlchemyStars-{tag.TrimStart('v', 'V')}-win-x64.zip";
                foreach (var asset in item.GetProperty("assets").EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), expectedAsset, StringComparison.OrdinalIgnoreCase)) continue;
                    var digest = asset.TryGetProperty("digest", out var digestProperty) ? digestProperty.GetString() : null;
                    releases.Add(new UpdateRelease(tag.TrimStart('v', 'V'), item.GetProperty("html_url").GetString()!,
                        asset.GetProperty("browser_download_url").GetString()!, digest ?? "", asset.GetProperty("size").GetInt64()));
                }
            }
            if (items.GetArrayLength() < 100) break;
            if (page >= 100) throw new InvalidOperationException("Release history is too large to determine the latest version safely.");
        }
        return SelectLatest(releases, currentVersion, channel);
    }

    public static UpdateRelease? SelectLatest(IEnumerable<UpdateRelease> releases, string currentVersion, UpdateChannel channel)
    {
        if (!SemanticReleaseVersion.TryParse(currentVersion, out var current))
            throw new InvalidOperationException("The installed version is not a semantic version.");
        return releases.Select(release => (Release: release, Version: SemanticReleaseVersion.TryParse(release.Version, out var parsed) ? parsed : null))
            .Where(item => item.Version is not null && item.Version.IsPrerelease == (channel == UpdateChannel.Preview)
                && item.Version.CompareTo(current) > 0)
            .OrderByDescending(item => item.Version).Select(item => item.Release).FirstOrDefault();
    }

    public async Task<PreparedUpdate> DownloadAsync(UpdateRelease release, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDigest(release.Sha256);
        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("The update is not an asset of the official GitHub repository.");
        if (release.Size is <= 0 or > 1_073_741_824) throw new InvalidDataException("Invalid update archive size.");
        var folder = Path.Combine(Path.GetTempPath(), "AlchemyStars-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var archive = Path.Combine(folder, "update.zip");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("AlchemyStars-Updater/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += read;
                    if (total > release.Size) throw new InvalidDataException("Update archive exceeds its advertised size.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report((double)total / release.Size);
                }
                if (total != release.Size) throw new InvalidDataException("The update download is incomplete.");
            }
            VerifyArchive(archive, release.Sha256);
            UpdateInstaller.ValidateArchive(archive);
            return new PreparedUpdate(archive, release);
        }
        catch
        {
            Directory.Delete(folder, true);
            throw;
        }
    }

    public static void VerifyArchive(string archivePath, string digest)
    {
        ValidateDigest(digest);
        using var file = File.OpenRead(archivePath);
        var actual = Convert.ToHexString(SHA256.HashData(file));
        if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The SHA-256 checksum does not match GitHub. The update will not be installed.");
    }

    private static void ValidateDigest(string digest)
    {
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71
            || !digest.AsSpan(7).ToString().All(Uri.IsHexDigit))
            throw new InvalidDataException("GitHub did not provide a valid SHA-256 checksum. Automatic installation is unavailable for this release.");
    }

    /// <summary>Only call after the user has saved their work and explicitly chosen to install.</summary>
    public void LaunchInstaller(PreparedUpdate update, bool restart, string? restartProjectPath = null)
    {
        if (!OperatingSystem.IsWindows() || RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException("Installing updates requires the Windows Native AOT release.");
        VerifyArchive(update.ArchivePath, update.Release.Sha256);
        UpdateInstaller.ValidateArchive(update.ArchivePath);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate this application.");
        var directory = Path.GetDirectoryName(update.ArchivePath)!;
        // Serialize handoffs for this payload. A ready marker means ownership already passed to a helper.
        using var launchLock = new FileStream(Path.Combine(directory, "launch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var ready = Path.Combine(directory, "ready");
        if (File.Exists(ready)) throw new InvalidOperationException("This update is already waiting for the application to exit.");
        var helper = Path.Combine(directory, "AlchemyStars.Update-" + Guid.NewGuid().ToString("N") + ".exe");
        var info = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "--apply-update", update.ArchivePath, update.Release.Sha256,
            Path.GetDirectoryName(executable)!, Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), restart ? "restart" : "exit" })
            info.ArgumentList.Add(argument);
        if (restartProjectPath is not null)
        {
            var project = Path.GetFullPath(restartProjectPath);
            if (!File.Exists(project) || !Path.GetExtension(project).Equals(".aprj", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The restart project must be a saved .aprj file.");
            info.ArgumentList.Add(project);
        }
        Process? process = null;
        try
        {
            File.Copy(executable, helper, false);
            process = Process.Start(info) ?? throw new IOException("The update helper could not start.");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (process.HasExited) throw new IOException("The update helper exited before it was ready. See " + Path.Combine(directory, "result.txt"));
                if (wait.Elapsed > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("The update helper did not become ready. The application will remain open.");
                Thread.Sleep(30);
            }
        }
        catch
        {
            // Do not leave a hidden helper that could install later after the UI reported failure.
            if (process is not null)
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit();
            }
            File.Delete(ready);
            File.Delete(helper);
            throw;
        }
        finally { process?.Dispose(); }
    }

    public static void DiscardPrepared(PreparedUpdate update)
    {
        var archive = Path.GetFullPath(update.ArchivePath);
        var directory = Path.GetDirectoryName(archive)!;
        if (Path.GetFileName(archive) != "update.zip" || !Path.GetFileName(directory).StartsWith("AlchemyStars-Update-", StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(directory)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("Refusing to remove a directory not owned by the updater.");
        if (File.Exists(Path.Combine(directory, "ready"))) throw new InvalidOperationException("The update has already been handed to its installer.");
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

internal sealed class SemanticReleaseVersion : IComparable<SemanticReleaseVersion>
{
    private readonly BigInteger[] numbers;
    private readonly string[] prerelease;
    public bool IsPrerelease => prerelease.Length > 0;
    private SemanticReleaseVersion(BigInteger[] numbers, string[] prerelease) { this.numbers = numbers; this.prerelease = prerelease; }

    public static bool TryParse(string value, out SemanticReleaseVersion? result)
    {
        result = null;
        value = value.TrimStart('v', 'V');
        var metadataSplit = value.Split('+');
        if (metadataSplit.Length > 2 || metadataSplit.Length == 2 && !ValidIdentifiers(metadataSplit[1], false)) return false;
        var split = metadataSplit[0].Split('-', 2);
        var core = split[0].Split('.');
        if (core.Length != 3 || core.Any(part => part.Length == 0 || part.Length > 1 && part[0] == '0' || !part.All(char.IsAsciiDigit))) return false;
        if (split.Length == 2 && !ValidIdentifiers(split[1], true)) return false;
        result = new SemanticReleaseVersion(core.Select(part => BigInteger.Parse(part, CultureInfo.InvariantCulture)).ToArray(),
            split.Length == 2 ? split[1].Split('.') : []);
        return true;
    }

    private static bool ValidIdentifiers(string value, bool forbidLeadingZero) => value.Split('.').All(part =>
        part.Length > 0 && part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && !(forbidLeadingZero && part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit)));

    public int CompareTo(SemanticReleaseVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < 3; i++) { var order = numbers[i].CompareTo(other.numbers[i]); if (order != 0) return order; }
        if (!IsPrerelease || !other.IsPrerelease) return other.IsPrerelease.CompareTo(IsPrerelease);
        for (var i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            var numeric = BigInteger.TryParse(prerelease[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number);
            var otherNumeric = BigInteger.TryParse(other.prerelease[i], NumberStyles.None, CultureInfo.InvariantCulture, out var otherNumber);
            var order = numeric && otherNumeric ? number.CompareTo(otherNumber) : numeric != otherNumeric ? (numeric ? -1 : 1)
                : string.CompareOrdinal(prerelease[i], other.prerelease[i]);
            if (order != 0) return order;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }
}
