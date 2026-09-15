using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AlchemyStars.Avalonia;

/// <summary>The upstream default-branch revision of the codename repository.</summary>
public sealed record CodWeaponDbRevision(string CommitSha, string CommitTimestamp)
{
    public string ShortSha => CommitSha.Length >= 8 ? CommitSha[..8] : CommitSha;
}

/// <summary>Outcome of one database refresh, including how it differs from the previous data.</summary>
public sealed record CodWeaponDbUpdateResult(
    string Directory,
    int RecordCount,
    int GameCount,
    string CommitSha,
    string CommitTimestamp,
    int AddedCount,
    int RemovedCount,
    int ChangedCount)
{
    public string ShortSha => CommitSha.Length >= 8 ? CommitSha[..8] : CommitSha;
}

public sealed class CodWeaponDbUpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Refreshes the weapon codename database from the commit-pinned upstream table.
/// This mirrors CODWeaponDB's <c>github-only</c> path: pin the repository revision,
/// read the README plus every <c>Games/*.md</c> at that exact commit, parse the
/// two-column Markdown tables, and publish the result atomically. Blueprint data
/// is not an upstream table, so it is carried over from the dataset being replaced.
/// </summary>
public sealed class CodWeaponDbUpdater : IDisposable
{
    public const string Repository = "SadSlothXL/COD-Weapon-codenames";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    private const string GitUrl = RepositoryUrl + ".git";
    private const string CodeloadUrl = "https://codeload.github.com/" + Repository;
    private const string ApiUrl = "https://api.github.com/repos/" + Repository;
    private const string ParserVersion = "github-markdown-v2";
    private const string SourceId = "sadslothxl-cod-weapon-codenames";
    private const string UserAgent = "AlchemyStars/1.3.0 (COD weapon database refresh)";
    private const long ArchiveMaxBytes = 64L * 1024 * 1024;
    private const long ArchiveMaxExpandedBytes = 256L * 1024 * 1024;

    private readonly HttpClient client;
    private readonly bool ownsClient;

    public CodWeaponDbUpdater(HttpMessageHandler? handler = null)
    {
        client = handler is null
            // The Git smart-HTTP endpoint and codeload both serve gzip, and a client
            // that never asks for decompression would hand raw gzip to the parsers.
            ? new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
            : new HttpClient(handler);
        ownsClient = true;
        client.Timeout = TimeSpan.FromMinutes(5);
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    /// <summary>
    /// Reads only the advertised <c>main</c> ref so a check costs one small request,
    /// with the REST API as a fallback when the Git advertisement is unavailable.
    /// </summary>
    public async Task<CodWeaponDbRevision> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var advertisement = await client.GetByteArrayAsync(
                $"{GitUrl}/info/refs?service=git-upload-pack", cancellationToken).ConfigureAwait(false);
            foreach (var packet in DecodeGitPackets(advertisement))
            {
                var advertised = Encoding.UTF8.GetString(packet).Split('\0', 2)[0].TrimEnd('\n');
                var match = Regex.Match(advertised, @"^([0-9a-f]{40}) refs/heads/main$");
                if (match.Success) return new CodWeaponDbRevision(match.Groups[1].Value, string.Empty);
            }
            throw new CodWeaponDbUpdateException("the Git advertisement has no single main ref");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return await CheckOverApiAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CodWeaponDbRevision> CheckOverApiAsync(CancellationToken cancellationToken)
    {
        var json = await GetStringAsync($"{ApiUrl}/commits/main", 4 * 1024 * 1024, cancellationToken).ConfigureAwait(false)
            ?? throw new CodWeaponDbUpdateException("the upstream revision could not be read over Git or the REST API");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("sha", out var sha) || sha.GetString() is not { Length: 40 } value)
            throw new CodWeaponDbUpdateException("the commit response has no usable sha");
        var timestamp = root.TryGetProperty("commit", out var commit)
            && commit.TryGetProperty("committer", out var committer)
            && committer.TryGetProperty("date", out var date)
                ? date.GetString() ?? string.Empty
                : string.Empty;
        return new CodWeaponDbRevision(value, timestamp);
    }

    /// <summary>
    /// Fetches the pinned archive, parses it, and publishes it into
    /// <paramref name="targetDirectory"/> only after every stage succeeded.
    /// </summary>
    public async Task<CodWeaponDbUpdateResult> UpdateAsync(
        string targetDirectory,
        string? blueprintSourceDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(targetDirectory);
        try
        {
            progress?.Report("正在读取上游版本…");
            var revision = await CheckAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(revision.CommitTimestamp))
            {
                // The archive path has no commit timestamp; the patch header carries it.
                var timestamp = await ReadCommitTimestampAsync(revision.CommitSha, cancellationToken).ConfigureAwait(false);
                revision = revision with { CommitTimestamp = timestamp };
            }

            progress?.Report($"正在下载 {revision.ShortSha} 的快照…");
            var archive = await GetBytesAsync($"{CodeloadUrl}/zip/{revision.CommitSha}", ArchiveMaxBytes, cancellationToken).ConfigureAwait(false)
                ?? throw new CodWeaponDbUpdateException("the upstream archive could not be downloaded");

            progress?.Report("正在解析武器代号表…");
            var blobs = ReadArchive(archive, revision.CommitSha);
            var readme = blobs.TryGetValue("README.md", out var readmeBytes)
                ? DecodeUtf8(readmeBytes, "README.md")
                : throw new CodWeaponDbUpdateException("the upstream snapshot has no README.md");
            var records = BuildRecords(blobs, readme, revision);
            if (records.Count == 0)
                throw new CodWeaponDbUpdateException("the upstream tables produced no weapon records");

            var (added, removed, changed) = DiffAgainstCurrent(destination, records);
            progress?.Report("正在发布数据集…");
            Publish(destination, records, revision, blueprintSourceDirectory);

            var games = records.Select(record => record.GameId).Distinct(StringComparer.Ordinal).Count();
            return new CodWeaponDbUpdateResult(destination, records.Count, games, revision.CommitSha,
                revision.CommitTimestamp, added, removed, changed);
        }
        catch (CodWeaponDbUpdateException)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidDataException)
        {
            throw new CodWeaponDbUpdateException(error.Message, error);
        }
    }

    // ---------------------------------------------------------------- fetching

    private async Task<string?> GetStringAsync(string url, long maxBytes, CancellationToken cancellationToken)
    {
        var bytes = await GetBytesAsync(url, maxBytes, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : DecodeUtf8(bytes, url);
    }

    private async Task<byte[]?> GetBytesAsync(string url, long maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
                throw new CodWeaponDbUpdateException($"the response for {url} exceeds the size limit");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes) throw new CodWeaponDbUpdateException($"the response for {url} exceeds the size limit");
            }
            return buffer.ToArray();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private async Task<string> ReadCommitTimestampAsync(string commitSha, CancellationToken cancellationToken)
    {
        // Reading the patch header avoids the REST API, which is rate limited per IP.
        var patch = await GetStringAsync($"{RepositoryUrl}/commit/{commitSha}.patch", 256 * 1024, cancellationToken).ConfigureAwait(false);
        if (patch is null) return string.Empty;
        foreach (var line in patch.Split('\n').Take(60))
        {
            if (!line.StartsWith("Date: ", StringComparison.Ordinal)) continue;
            return DateTimeOffset.TryParse(line[6..].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : string.Empty;
        }
        return string.Empty;
    }

    private static IEnumerable<byte[]> DecodeGitPackets(byte[] content)
    {
        var offset = 0;
        while (offset + 4 <= content.Length)
        {
            var header = Encoding.ASCII.GetString(content, offset, 4);
            if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var length))
                throw new CodWeaponDbUpdateException("the Git reference stream is malformed");
            offset += 4;
            if (length == 0) continue;
            if (length < 4 || offset + length - 4 > content.Length)
                throw new CodWeaponDbUpdateException("the Git reference packet is truncated");
            yield return content[offset..(offset + length - 4)];
            offset += length - 4;
        }
    }

    private static Dictionary<string, byte[]> ReadArchive(byte[] archive, string commitSha)
    {
        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var expectedRoot = Repository.Split('/')[1] + "-" + commitSha;
        long expanded = 0;
        try
        {
            using var stream = new MemoryStream(archive, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var parts = entry.FullName.Split('/');
                if (parts.Length < 2 || !string.Equals(parts[0], expectedRoot, StringComparison.Ordinal))
                    throw new CodWeaponDbUpdateException("the archive has an unexpected root folder");
                if (parts.Any(part => part.Length == 0 || part is "." or ".."))
                    throw new CodWeaponDbUpdateException("the archive has an unsafe path");
                expanded += entry.Length;
                if (expanded > ArchiveMaxExpandedBytes)
                    throw new CodWeaponDbUpdateException("the archive exceeds the expanded size limit");
                var path = string.Join('/', parts.Skip(1));
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                blobs[path] = buffer.ToArray();
            }
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            throw new CodWeaponDbUpdateException("the upstream archive is not a valid ZIP file", error);
        }
        return blobs;
    }

    private static string DecodeUtf8(byte[] bytes, string path)
    {
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
        catch (DecoderFallbackException error)
        {
            throw new CodWeaponDbUpdateException($"{path} is not valid UTF-8", error);
        }
    }

    private static string GitBlobSha(byte[] content) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {content.Length}\0").Concat(content).ToArray()));

    // ---------------------------------------------------------------- parsing

    /// <summary>Stable display metadata for the files currently listed upstream.</summary>
    internal static readonly IReadOnlyDictionary<string, (string GameId, string Title, string ShortName, string EngineCode, int ReleaseYear)> GameMetadata =
        new Dictionary<string, (string, string, string, string, int)>(StringComparer.Ordinal)
        {
            ["codo"] = ("codo", "Call of Duty Online", "CODO", "OL", 2015),
            ["h1 mwr"] = ("mwr", "Call of Duty: Modern Warfare Remastered", "MWR", "H1", 2016),
            ["h2 mw2cr"] = ("mw2cr", "Call of Duty: Modern Warfare 2 Campaign Remastered", "MW2CR", "H2", 2020),
            ["iw cod"] = ("cod", "Call of Duty", "COD", "IW", 2003),
            ["iw2 cod2"] = ("cod2", "Call of Duty 2", "COD2", "IW2", 2005),
            ["iw3 cod4"] = ("cod4", "Call of Duty 4: Modern Warfare", "COD4", "IW3", 2007),
            ["iw4 mw2"] = ("mw2", "Call of Duty: Modern Warfare 2", "MW2", "IW4", 2009),
            ["iw5 mw3"] = ("mw3", "Call of Duty: Modern Warfare 3", "MW3", "IW5", 2011),
            ["iw6 ghosts"] = ("ghosts", "Call of Duty: Ghosts", "Ghosts", "IW6", 2013),
            ["iw7 iw"] = ("infinite_warfare", "Call of Duty: Infinite Warfare", "IW", "IW7", 2016),
            ["iw8 mw19"] = ("mw2019", "Call of Duty: Modern Warfare", "MW19", "IW8", 2019),
            ["iw9 mw22"] = ("mwii", "Call of Duty: Modern Warfare II (2022)", "MWII", "IW9", 2022),
            ["jup mw23"] = ("mwiii", "Call of Duty: Modern Warfare III (2023)", "MWIII", "JUP", 2023),
            ["nx1 fw"] = ("future_warfare", "Call of Duty: Future Warfare", "FW", "NX1", 2011),
            ["rex mw4"] = ("mw4", "Call of Duty: Modern Warfare 4", "MW4", "REX", 2026),
            ["s1 aw"] = ("advanced_warfare", "Call of Duty: Advanced Warfare", "AW", "S1", 2014),
            ["s2 ww2"] = ("wwii", "Call of Duty: WWII", "WWII", "S2", 2017),
            ["s4 vg"] = ("vanguard", "Call of Duty: Vanguard", "Vanguard", "S4", 2021),
            ["sat bo7"] = ("bo7", "Call of Duty: Black Ops 7", "BO7", "SAT", 2025),
            ["t10 bo6"] = ("bo6", "Call of Duty: Black Ops 6", "BO6", "T10", 2024),
            ["t4 waw"] = ("waw", "Call of Duty: World at War", "WaW", "T4", 2008),
            ["t5 bo"] = ("bo", "Call of Duty: Black Ops", "BO", "T5", 2010),
            ["t6 bo2"] = ("bo2", "Call of Duty: Black Ops II", "BO2", "T6", 2012),
            ["t7 bo3"] = ("bo3", "Call of Duty: Black Ops III", "BO3", "T7", 2015),
            ["t8 bo4"] = ("bo4", "Call of Duty: Black Ops 4", "BO4", "T8", 2018),
            ["t9 bocw"] = ("bocw", "Call of Duty: Black Ops Cold War", "BOCW", "T9", 2020),
            ["cod3"] = ("cod3", "Call of Duty 3", "COD3", "COD3", 2006),
        };

    internal static readonly IReadOnlyDictionary<string, string> WeaponClasses = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ar"] = "Assault Rifle", ["assault"] = "Assault Rifle",
        ["br"] = "Battle Rifle", ["battle"] = "Battle Rifle",
        ["dm"] = "Marksman Rifle", ["marksman"] = "Marksman Rifle",
        ["la"] = "Launcher", ["launcher"] = "Launcher",
        ["lm"] = "Light Machine Gun", ["lmg"] = "Light Machine Gun",
        ["me"] = "Melee", ["melee"] = "Melee",
        ["pi"] = "Handgun", ["pistol"] = "Handgun",
        ["sh"] = "Shotgun", ["shotgun"] = "Shotgun",
        ["sl"] = "Special", ["special"] = "Special",
        ["sm"] = "Submachine Gun", ["smg"] = "Submachine Gun",
        ["sn"] = "Sniper Rifle", ["sniper"] = "Sniper Rifle",
    };

    private static readonly Regex WeaponClassPattern = new(
        "(?:^|_)(" + string.Join('|', WeaponClasses.Keys.OrderByDescending(key => key.Length)) + ")(?:_|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NameTokenPattern = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LinkPattern = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SeparatorRowPattern = new("^:?-{3,}:?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Derives the weapon class from an explicit codename prefix, or Unknown.</summary>
    internal static string DeriveWeaponClass(string codename)
    {
        var match = WeaponClassPattern.Match(codename.ToLowerInvariant());
        return match.Success && WeaponClasses.TryGetValue(match.Groups[1].Value, out var weaponClass) ? weaponClass : "Unknown";
    }

    /// <summary>Parses the repository's two-column Markdown weapon table.</summary>
    internal static List<CodWeaponOutputDto> ParseGameMarkdown(
        string text,
        string gameId,
        string gameTitle,
        string gameShortName,
        string engineCode,
        string commitSha,
        string commitTimestamp,
        string path,
        int ordinalBase = 1)
    {
        var records = new List<CodWeaponOutputDto>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var stripped = rawLine.Trim().TrimEnd('\r');
            if (!stripped.StartsWith('|')) continue;
            var cells = stripped.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 2) continue;
            var displayName = cells[0];
            var rawCodenames = cells[1];
            if (displayName.ToLowerInvariant() is "in-game" or "in game" or "name"
                || SeparatorRowPattern.IsMatch(displayName)
                || SeparatorRowPattern.IsMatch(rawCodenames)
                || rawCodenames.Length == 0
                || displayName.Length == 0) continue;

            var codenames = rawCodenames.Split('/').Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
            var ordinal = ordinalBase;
            foreach (var codename in codenames)
            {
                var sourceUrl = $"https://github.com/{Repository}/blob/{commitSha}/{Uri.EscapeDataString(path).Replace("%2F", "/")}#L{lineNumber}";
                records.Add(new CodWeaponOutputDto
                {
                    GameId = gameId,
                    GameTitle = gameTitle,
                    GameShortName = gameShortName,
                    EngineCode = engineCode,
                    WeaponDisplayName = displayName,
                    WikiTitle = string.Empty,
                    PageId = 0,
                    WeaponClass = DeriveWeaponClass(codename),
                    Codename = codename,
                    Scope = "unspecified",
                    Ordinal = ordinal,
                    NativeOrCarryover = "native",
                    SectionTitle = displayName,
                    SourceUrl = sourceUrl,
                    RevisionId = 0,
                    RevisionTimestamp = commitTimestamp,
                    RawConsoleValue = rawCodenames,
                    RawSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawLine.TrimEnd('\r')))),
                    ParserVersion = ParserVersion,
                    ReviewStatus = "source_only",
                    AppliedOverrideIds = [],
                    SourceKind = "github",
                    Sources =
                    [
                        new CodWeaponOutputSourceDto
                        {
                            Codename = codename,
                            CommitSha = commitSha,
                            Kind = "github",
                            Name = Repository,
                            Path = path,
                            Role = "primary",
                            Url = sourceUrl,
                            WeaponDisplayName = displayName,
                            BlobSha = string.Empty,
                            ContentSha256 = string.Empty,
                        },
                    ],
                });
                ordinal++;
            }
        }
        return records;
    }

    private static List<CodWeaponOutputDto> BuildRecords(Dictionary<string, byte[]> blobs, string readme, CodWeaponDbRevision revision)
    {
        var titles = ReadmeGameTitles(readme);
        var catalog = new List<(string Path, string GameId, string Title, string ShortName, string EngineCode, int ReleaseYear)>();
        foreach (var path in blobs.Keys.Where(key => key.StartsWith("Games/", StringComparison.Ordinal)
            && key.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).OrderBy(key => key.ToLowerInvariant()))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var stemKey = stem.ToLowerInvariant();
            string gameId, title, shortName, engineCode;
            int releaseYear;
            if (GameMetadata.TryGetValue(stemKey, out var metadata))
            {
                (gameId, title, shortName, engineCode, releaseYear) = metadata;
                if (titles.TryGetValue(path.ToLowerInvariant(), out var readmeTitle)) title = readmeTitle;
            }
            else
            {
                // An unknown future game file still enters the catalog deterministically.
                var slug = string.Join('_', NameTokenPattern.Matches(stemKey).Select(match => match.Value));
                gameId = "github_" + (slug.Length > 0 ? slug : "unknown");
                var tokens = stem.Split(' ', 2);
                engineCode = tokens[0].ToUpperInvariant();
                shortName = tokens.Length > 1 ? tokens[1] : stem;
                title = titles.TryGetValue(path.ToLowerInvariant(), out var readmeTitle) ? readmeTitle : stem;
                releaseYear = 9999;
            }
            catalog.Add((path, gameId, title, shortName, engineCode, releaseYear));
        }
        catalog = catalog
            .OrderBy(entry => entry.ReleaseYear)
            .ThenBy(entry => entry.GameId.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(entry => entry.Path.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();

        var records = new List<CodWeaponOutputDto>();
        var seenGames = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var gameOrder = 0; gameOrder < catalog.Count; gameOrder++)
        {
            var entry = catalog[gameOrder];
            if (!seenGames.Add(entry.GameId))
                throw new CodWeaponDbUpdateException($"the upstream tree yields a duplicate game id '{entry.GameId}'");
            var text = DecodeUtf8(blobs[entry.Path], entry.Path);
            foreach (var record in ParseGameMarkdown(text, entry.GameId, entry.Title, entry.ShortName, entry.EngineCode,
                revision.CommitSha, revision.CommitTimestamp, entry.Path))
            {
                record.GameOrder = gameOrder;
                record.ReleaseYear = entry.ReleaseYear;
                var source = record.Sources![0];
                var bytes = blobs[entry.Path];
                source.BlobSha = GitBlobSha(bytes);
                source.ContentSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
                // Upstream repeats the same assertion across files; keep the first.
                if (!seen.Add($"{record.GameId}\u001f{ComparisonCodename(record)}\u001f{NormalizeName(record.WeaponDisplayName)}")) continue;
                records.Add(record);
            }
        }

        return records
            .OrderBy(record => record.ReleaseYear)
            .ThenBy(record => record.GameOrder)
            .ThenBy(record => record.WeaponDisplayName.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(record => record.Ordinal)
            .ThenBy(record => record.Codename.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
    }

    private static string ComparisonCodename(CodWeaponOutputDto record)
    {
        var codename = record.Codename.ToLowerInvariant();
        var prefix = record.EngineCode.ToLowerInvariant() + "_";
        return prefix != "_" && codename.StartsWith(prefix, StringComparison.Ordinal) ? codename[prefix.Length..] : codename;
    }

    /// <summary>Mirrors the upstream alias normalisation used to deduplicate assertions.</summary>
    internal static string NormalizeName(string value) =>
        string.Join(' ', NameTokenPattern.Matches(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant()).Select(match => match.Value));

    /// <summary>Maps every <c>Games/*.md</c> link in the README to its display title.</summary>
    internal static Dictionary<string, string> ReadmeGameTitles(string readme)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in LinkPattern.Matches(readme))
        {
            var title = match.Groups[1].Value;
            var target = Uri.UnescapeDataString(match.Groups[2].Value);
            var marker = target.IndexOf("/Games/", StringComparison.Ordinal);
            string path;
            if (marker >= 0) path = "Games/" + target[(marker + "/Games/".Length)..];
            else if (target.StartsWith("Games/", StringComparison.Ordinal)) path = target;
            else continue;
            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) titles[path.ToLowerInvariant()] = title.Trim();
        }
        return titles;
    }

    // ---------------------------------------------------------------- publishing

    /// <summary>
    /// Compares the refreshed records with the dataset being replaced. One console
    /// codename can legitimately back more than one display name, so the key is not
    /// unique and each key carries the set of names it appeared under.
    /// </summary>
    private static (int Added, int Removed, int Changed) DiffAgainstCurrent(string destination, List<CodWeaponOutputDto> records)
    {
        var previous = ReadCodenameMap(Path.Combine(destination, "weapons.jsonl"));
        var current = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var key = record.GameId + "\u001f" + record.Codename;
            if (!current.TryGetValue(key, out var names)) current[key] = names = new HashSet<string>(StringComparer.Ordinal);
            names.Add(record.WeaponDisplayName);
        }
        if (previous.Count == 0) return (current.Count, 0, 0);
        var added = current.Keys.Count(key => !previous.ContainsKey(key));
        var removed = previous.Keys.Count(key => !current.ContainsKey(key));
        var changed = current.Count(pair => previous.TryGetValue(pair.Key, out var names) && !names.SetEquals(pair.Value));
        return (added, removed, changed);
    }

    private static Dictionary<string, HashSet<string>> ReadCodenameMap(string path)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (!File.Exists(path)) return map;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var dto = JsonSerializer.Deserialize(line, CodWeaponDbUpdateJsonContext.Default.CodWeaponOutputDto);
                if (dto is null || string.IsNullOrWhiteSpace(dto.Codename)) continue;
                var key = (dto.GameId ?? string.Empty) + "\u001f" + dto.Codename;
                if (!map.TryGetValue(key, out var names)) map[key] = names = new HashSet<string>(StringComparer.Ordinal);
                names.Add(dto.WeaponDisplayName ?? string.Empty);
            }
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return map;
        }
        return map;
    }

    private static void Publish(string destination, List<CodWeaponOutputDto> records, CodWeaponDbRevision revision, string? blueprintSourceDirectory)
    {
        var parent = Path.GetDirectoryName(destination) ?? throw new CodWeaponDbUpdateException("the dataset destination has no parent directory");
        Directory.CreateDirectory(parent);
        var staging = destination + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            using (var writer = new StreamWriter(Path.Combine(staging, "weapons.jsonl"), false, new UTF8Encoding(false)))
            {
                foreach (var record in records)
                {
                    writer.Write(JsonSerializer.Serialize(record, CodWeaponDbUpdateJsonContext.Default.CodWeaponOutputDto));
                    writer.Write('\n');
                }
            }

            // Blueprints are a Wiki-derived artifact with no upstream table to refresh
            // from, so the best available copy is carried over unchanged.
            var carried = new[] { blueprintSourceDirectory, CodWeaponCatalog.BuiltInDirectory }
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory => Path.Combine(directory!, "blueprints.json"))
                .FirstOrDefault(File.Exists);
            if (carried is not null) File.Copy(carried, Path.Combine(staging, "blueprints.json"), overwrite: true);

            var manifest = new CodWeaponManifestDto
            {
                SchemaVersion = 1,
                SourceMode = "github-only",
                SupplementalSources =
                [
                    new CodWeaponManifestSourceDto
                    {
                        Repository = Repository,
                        RepositoryUrl = RepositoryUrl,
                        CommitSha = revision.CommitSha,
                        CommitTimestamp = revision.CommitTimestamp,
                        LicenseStatus = "not_declared",
                        SourceId = SourceId,
                    },
                ],
            };
            File.WriteAllText(Path.Combine(staging, "refresh-manifest.json"),
                JsonSerializer.Serialize(manifest, CodWeaponDbUpdateJsonContext.Default.CodWeaponManifestDto), new UTF8Encoding(false));

            var qa = new CodWeaponQaDto { SourceMode = "github-only", IssueCount = 0, RefreshedFrom = "in-app github-only refresh" };
            File.WriteAllText(Path.Combine(staging, "qa.json"),
                JsonSerializer.Serialize(qa, CodWeaponDbUpdateJsonContext.Default.CodWeaponQaDto), new UTF8Encoding(false));

            File.WriteAllText(Path.Combine(staging, "README.md"), BuildReadme(revision, records.Count), new UTF8Encoding(false));

            // Swap only after every artifact validated, so a failure keeps the old data.
            var previous = destination + ".previous";
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            var movedAside = false;
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, previous);
                movedAside = true;
            }
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (movedAside && !Directory.Exists(destination)) Directory.Move(previous, destination);
                throw;
            }
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static string BuildReadme(CodWeaponDbRevision revision, int recordCount) =>
        $"""
        # COD 武器数据库（应用内更新）

        本目录由 Alchemy Stars 的「COD 武器库」页面在线更新生成，不是随包快照。

        - 上游：{RepositoryUrl}
        - 固定 commit：{revision.CommitSha}
        - commit 时间：{(revision.CommitTimestamp.Length > 0 ? revision.CommitTimestamp : "未知")}
        - 记录数：{recordCount}
        - 解析版本：{ParserVersion}（github-only）

        武器名与控制台代号来自上述 GitHub 表格；该仓库未声明许可证，数据仅在本机使用。

        蓝图资料没有可刷新的上游表格，因此仍沿用更新前数据集（或随包 v0.12.0 快照）中的
        `blueprints.json`。武器图标由应用在运行时按需从 Call of Duty Wiki 获取，只缓存在本机。
        """;

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
    }
}

internal sealed class CodWeaponOutputDto
{
    [JsonPropertyName("game_id")] public string GameId { get; set; } = string.Empty;
    [JsonPropertyName("game_short_name")] public string GameShortName { get; set; } = string.Empty;
    [JsonPropertyName("game_title")] public string GameTitle { get; set; } = string.Empty;
    [JsonPropertyName("engine_code")] public string EngineCode { get; set; } = string.Empty;
    [JsonPropertyName("weapon_display_name")] public string WeaponDisplayName { get; set; } = string.Empty;
    [JsonPropertyName("wiki_title")] public string WikiTitle { get; set; } = string.Empty;
    [JsonPropertyName("page_id")] public long PageId { get; set; }
    [JsonPropertyName("weapon_class")] public string WeaponClass { get; set; } = string.Empty;
    [JsonPropertyName("codename")] public string Codename { get; set; } = string.Empty;
    [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
    [JsonPropertyName("ordinal")] public int Ordinal { get; set; }
    [JsonPropertyName("native_or_carryover")] public string NativeOrCarryover { get; set; } = string.Empty;
    [JsonPropertyName("section_title")] public string SectionTitle { get; set; } = string.Empty;
    [JsonPropertyName("source_url")] public string SourceUrl { get; set; } = string.Empty;
    [JsonPropertyName("revision_id")] public long RevisionId { get; set; }
    [JsonPropertyName("revision_timestamp")] public string RevisionTimestamp { get; set; } = string.Empty;
    [JsonPropertyName("raw_console_value")] public string RawConsoleValue { get; set; } = string.Empty;
    [JsonPropertyName("raw_sha256")] public string RawSha256 { get; set; } = string.Empty;
    [JsonPropertyName("parser_version")] public string ParserVersion { get; set; } = string.Empty;
    [JsonPropertyName("review_status")] public string ReviewStatus { get; set; } = string.Empty;
    [JsonPropertyName("applied_override_ids")] public List<string> AppliedOverrideIds { get; set; } = [];
    [JsonPropertyName("source_kind")] public string SourceKind { get; set; } = string.Empty;
    [JsonPropertyName("sources")] public List<CodWeaponOutputSourceDto>? Sources { get; set; } = [];
    [JsonIgnore] public int GameOrder { get; set; }
    [JsonIgnore] public int ReleaseYear { get; set; }
}

internal sealed class CodWeaponOutputSourceDto
{
    [JsonPropertyName("codename")] public string Codename { get; set; } = string.Empty;
    [JsonPropertyName("commit_sha")] public string CommitSha { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("weapon_display_name")] public string WeaponDisplayName { get; set; } = string.Empty;
    [JsonPropertyName("blob_sha")] public string BlobSha { get; set; } = string.Empty;
    [JsonPropertyName("content_sha256")] public string ContentSha256 { get; set; } = string.Empty;
}

internal sealed class CodWeaponManifestDto
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("source_mode")] public string SourceMode { get; set; } = string.Empty;
    [JsonPropertyName("supplemental_sources")] public List<CodWeaponManifestSourceDto> SupplementalSources { get; set; } = [];
}

internal sealed class CodWeaponManifestSourceDto
{
    [JsonPropertyName("repository")] public string Repository { get; set; } = string.Empty;
    [JsonPropertyName("repository_url")] public string RepositoryUrl { get; set; } = string.Empty;
    [JsonPropertyName("commit_sha")] public string CommitSha { get; set; } = string.Empty;
    [JsonPropertyName("commit_timestamp")] public string CommitTimestamp { get; set; } = string.Empty;
    [JsonPropertyName("license_status")] public string LicenseStatus { get; set; } = string.Empty;
    [JsonPropertyName("source_id")] public string SourceId { get; set; } = string.Empty;
}

internal sealed class CodWeaponQaDto
{
    [JsonPropertyName("source_mode")] public string SourceMode { get; set; } = string.Empty;
    [JsonPropertyName("issue_count")] public int IssueCount { get; set; }
    [JsonPropertyName("refreshed_from")] public string RefreshedFrom { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CodWeaponOutputDto))]
[JsonSerializable(typeof(CodWeaponManifestDto))]
[JsonSerializable(typeof(CodWeaponQaDto))]
internal sealed partial class CodWeaponDbUpdateJsonContext : JsonSerializerContext;
