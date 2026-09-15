using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlchemyStars.Avalonia;

/// <summary>
/// One COD Wiki blueprint entry attached to a weapon record. Blueprint data is
/// optional: a weapon without a matching blueprint page simply carries none.
/// </summary>
public sealed class CodBlueprint
{
    public string Name { get; init; } = string.Empty;
    public string Codename { get; init; } = string.Empty;
    public string Rarity { get; init; } = string.Empty;
    public string HowToObtain { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public long RevisionId { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Codename : Name;
    public bool HasCodename => !string.IsNullOrWhiteSpace(Codename);
    public bool HasObtain => !string.IsNullOrWhiteSpace(HowToObtain);
}

/// <summary>
/// One console-codename record. Every field mirrors the released
/// <c>weapons.jsonl</c> schema so the built-in snapshot and a user refresh stay
/// byte-compatible with CODWeaponDB itself.
/// </summary>
public sealed class CodWeapon
{
    public string GameId { get; init; } = string.Empty;
    public string GameShortName { get; init; } = string.Empty;
    public string GameTitle { get; init; } = string.Empty;
    public string EngineCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string WikiTitle { get; init; } = string.Empty;
    public string Class { get; init; } = string.Empty;
    public string Codename { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string NativeOrCarryover { get; init; } = string.Empty;
    public string SectionTitle { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string SourceKind { get; init; } = string.Empty;
    public string ReviewStatus { get; init; } = string.Empty;
    public long RevisionId { get; init; }
    public string RevisionTimestamp { get; init; } = string.Empty;
    public IReadOnlyList<string> AppliedOverrideIds { get; init; } = [];
    public IReadOnlyList<CodBlueprint> Blueprints { get; init; } = [];

    /// <summary>Join key used by <c>blueprints.json</c>.</summary>
    public string Key => GameId + ":" + Name;
    public string GameLabel => string.IsNullOrWhiteSpace(GameShortName) ? GameTitle : GameShortName;

    /// <summary>Class label resolved against the active language; "Unknown" reads as unclassified.</summary>
    public string ClassLabel { get; internal set; } = string.Empty;

    /// <summary>Wiki page queried for the reference icon; falls back to the display name.</summary>
    public string ReferenceTitle => string.IsNullOrWhiteSpace(WikiTitle) ? Name : WikiTitle;

    public bool HasBlueprints => Blueprints.Count > 0;
    public bool HasSection => !string.IsNullOrWhiteSpace(SectionTitle);
    public bool HasCodename => !string.IsNullOrWhiteSpace(Codename);
    /// <summary>The GitHub table has no mode column, so "unspecified" is hidden rather than shown.</summary>
    public bool HasScope => !string.IsNullOrWhiteSpace(Scope) && !Scope.Equals("unspecified", StringComparison.OrdinalIgnoreCase);
    public bool HasEngine => !string.IsNullOrWhiteSpace(EngineCode);
    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);
    public bool HasManualRevision => AppliedOverrideIds.Count > 0;
    /// <summary>GitHub-sourced records carry no wiki revision, so the row is hidden rather than left blank.</summary>
    public bool HasRevision => RevisionId > 0;
    public string RevisionDisplay => RevisionId > 0 ? RevisionId.ToString(CultureInfo.InvariantCulture) : string.Empty;

    internal string SearchBlob { get; set; } = string.Empty;
}

/// <summary>Where the currently loaded catalog came from, for the source panel.</summary>
public sealed class CodCatalogProvenance
{
    public string SourceMode { get; init; } = string.Empty;
    public string Repository { get; init; } = string.Empty;
    public string CommitSha { get; init; } = string.Empty;
    public string BlueprintsFetchedAt { get; init; } = string.Empty;
    public int WeaponCount { get; init; }
    public int BlueprintCount { get; init; }
    public int IssueCount { get; init; }
}

/// <summary>Filter state for the weapon list. Empty values mean "no restriction".</summary>
public sealed record CodWeaponFilter(string Game = "", string Class = "", string CodenamePrefix = "")
{
    public static readonly CodWeaponFilter None = new();
}

/// <summary>
/// Loads a CODWeaponDB dataset. Both the bundled v0.12.0 snapshot and a
/// user-selected <c>dist</c> folder use the exact same files, so refreshing is a
/// directory swap rather than a second data format.
/// </summary>
public sealed class CodWeaponCatalog
{
    public const string SnapshotDirectoryName = "CodWeaponDb";
    private const string WeaponsFileName = "weapons.jsonl";
    private const string BlueprintsFileName = "blueprints.json";
    private const string QaFileName = "qa.json";
    private const string ManifestFileName = "refresh-manifest.json";

    private CodWeaponCatalog(IReadOnlyList<CodWeapon> weapons, CodCatalogProvenance provenance, int distinctBlueprintCount)
    {
        Weapons = weapons;
        Provenance = provenance;
        Games = weapons
            .Select(weapon => weapon.GameShortName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Classes = weapons
            .Select(weapon => weapon.Class)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // Distinct blueprints, not the sum over records: one weapon page can back
        // several codename rows, and counting those twice would overstate coverage.
        BlueprintCount = distinctBlueprintCount;
    }

    public IReadOnlyList<CodWeapon> Weapons { get; }
    public CodCatalogProvenance Provenance { get; }
    public IReadOnlyList<string> Games { get; }
    public IReadOnlyList<string> Classes { get; }
    public int BlueprintCount { get; }

    /// <summary>Directory of the snapshot shipped next to the executable.</summary>
    public static string BuiltInDirectory => Path.Combine(AppContext.BaseDirectory, SnapshotDirectoryName);

    /// <summary>True when a usable weapon file exists in <paramref name="directory"/>.</summary>
    public static bool IsDatasetDirectory(string directory) =>
        !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, WeaponsFileName));

    public static async Task<CodWeaponCatalog> LoadAsync(string directory, CancellationToken cancellationToken = default)
    {
        var weaponsPath = Path.Combine(directory, WeaponsFileName);
        if (!File.Exists(weaponsPath))
            throw new FileNotFoundException($"The COD weapon dataset is missing {WeaponsFileName}.", weaponsPath);

        var blueprints = await ReadBlueprintsAsync(Path.Combine(directory, BlueprintsFileName), cancellationToken).ConfigureAwait(false);
        var records = new List<CodWeapon>(1536);
        var jsonOptions = CodWeaponJsonContext.Default;

        await foreach (var line in File.ReadLinesAsync(weaponsPath, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            CodWeaponLineDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize(line, jsonOptions.CodWeaponLineDto);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"Malformed COD weapon record in {WeaponsFileName}: {error.Message}", error);
            }
            if (dto is null || string.IsNullOrWhiteSpace(dto.WeaponDisplayName)) continue;

            var key = (dto.GameId ?? string.Empty) + ":" + dto.WeaponDisplayName;
            var weapon = new CodWeapon
            {
                GameId = dto.GameId ?? string.Empty,
                GameShortName = dto.GameShortName ?? string.Empty,
                GameTitle = dto.GameTitle ?? string.Empty,
                EngineCode = dto.EngineCode ?? string.Empty,
                Name = dto.WeaponDisplayName,
                WikiTitle = dto.WikiTitle ?? string.Empty,
                Class = dto.WeaponClass ?? string.Empty,
                Codename = dto.Codename ?? string.Empty,
                Scope = dto.Scope ?? string.Empty,
                NativeOrCarryover = dto.NativeOrCarryover ?? string.Empty,
                SectionTitle = dto.SectionTitle ?? string.Empty,
                SourceUrl = dto.SourceUrl ?? string.Empty,
                SourceKind = dto.SourceKind ?? string.Empty,
                ReviewStatus = dto.ReviewStatus ?? string.Empty,
                RevisionId = dto.RevisionId,
                RevisionTimestamp = dto.RevisionTimestamp ?? string.Empty,
                AppliedOverrideIds = dto.AppliedOverrideIds is { Count: > 0 } ids ? ids.ToArray() : [],
                Blueprints = blueprints.Weapons.TryGetValue(key, out var list) ? list : [],
            };
            weapon.SearchBlob = BuildSearchBlob(weapon);
            records.Add(weapon);
        }
        if (records.Count == 0)
            throw new InvalidDataException($"The COD weapon dataset in {directory} contains no usable records.");

        var qa = await ReadQaAsync(Path.Combine(directory, QaFileName), cancellationToken).ConfigureAwait(false);
        var manifest = await ReadManifestAsync(Path.Combine(directory, ManifestFileName), cancellationToken).ConfigureAwait(false);
        var provenance = new CodCatalogProvenance
        {
            SourceMode = qa?.SourceMode ?? manifest?.SourceMode ?? string.Empty,
            Repository = manifest?.SupplementalSources?.FirstOrDefault()?.Repository ?? string.Empty,
            CommitSha = manifest?.SupplementalSources?.FirstOrDefault()?.CommitSha ?? string.Empty,
            BlueprintsFetchedAt = blueprints.FetchedAt ?? string.Empty,
            WeaponCount = records.Count,
            BlueprintCount = blueprints.TotalCount,
            IssueCount = qa?.IssueCount ?? 0,
        };
        return new CodWeaponCatalog(records, provenance, blueprints.TotalCount);
    }

    /// <summary>
    /// Ranked lookup used by the search box: codename hits first, then weapon
    /// names, then blueprint names/codenames.
    /// </summary>
    public IReadOnlyList<CodWeapon> Query(string? query, CodWeaponFilter? filter, int limit = 5000)
    {
        filter ??= CodWeaponFilter.None;
        var trimmed = (query ?? string.Empty).Trim();
        var needle = trimmed.ToLowerInvariant();
        var prefix = (filter.CodenamePrefix ?? string.Empty).ToLowerInvariant();
        var scored = new List<(int Score, CodWeapon Weapon)>();

        foreach (var weapon in Weapons)
        {
            if (filter.Game.Length > 0 && !string.Equals(weapon.GameShortName, filter.Game, StringComparison.OrdinalIgnoreCase)) continue;
            if (filter.Class.Length > 0 && !string.Equals(weapon.Class, filter.Class, StringComparison.OrdinalIgnoreCase)) continue;
            if (prefix.Length > 0 && !weapon.Codename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var score = Score(weapon, needle);
            if (score < 0) continue;
            scored.Add((score, weapon));
        }

        return scored
            .OrderBy(entry => entry.Score)
            .ThenBy(entry => entry.Weapon.GameShortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Weapon.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Weapon.Codename, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(entry => entry.Weapon)
            .ToArray();
    }

    private static int Score(CodWeapon weapon, string needle)
    {
        if (needle.Length == 0) return 0;
        var codename = weapon.Codename.ToLowerInvariant();
        if (codename.Length > 0)
        {
            if (codename == needle) return 0;
            if (codename.StartsWith(needle, StringComparison.Ordinal)) return 1;
        }
        var name = weapon.Name.ToLowerInvariant();
        if (name == needle) return 0;
        if (name.StartsWith(needle, StringComparison.Ordinal)) return 2;
        if (codename.Contains(needle, StringComparison.Ordinal)) return 3;
        if (name.Contains(needle, StringComparison.Ordinal)) return 4;
        if (weapon.WikiTitle.Contains(needle, StringComparison.OrdinalIgnoreCase)) return 4;
        foreach (var blueprint in weapon.Blueprints)
        {
            if (blueprint.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || blueprint.Codename.Contains(needle, StringComparison.OrdinalIgnoreCase)) return 5;
        }
        return weapon.SearchBlob.Contains(needle, StringComparison.Ordinal) ? 6 : -1;
    }

    private static string BuildSearchBlob(CodWeapon weapon)
    {
        var builder = new StringBuilder(160);
        builder.Append(weapon.Name).Append('\u001f')
            .Append(weapon.Codename).Append('\u001f')
            .Append(weapon.WikiTitle).Append('\u001f')
            .Append(weapon.SectionTitle).Append('\u001f')
            .Append(weapon.GameTitle).Append('\u001f')
            .Append(weapon.Class).Append('\u001f')
            .Append(weapon.EngineCode);
        foreach (var blueprint in weapon.Blueprints)
            builder.Append('\u001f').Append(blueprint.Name).Append('\u001f').Append(blueprint.Codename);
        return builder.ToString().ToLowerInvariant();
    }

    private static async Task<BlueprintLookup> ReadBlueprintsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return BlueprintLookup.Empty;
        await using var stream = File.OpenRead(path);
        CodBlueprintFileDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync(stream, CodWeaponJsonContext.Default.CodBlueprintFileDto, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Malformed {BlueprintsFileName}: {error.Message}", error);
        }
        if (dto?.Weapons is not { Count: > 0 }) return BlueprintLookup.Empty;

        var lookup = new Dictionary<string, IReadOnlyList<CodBlueprint>>(dto.Weapons.Count, StringComparer.Ordinal);
        var total = 0;
        foreach (var (key, entries) in dto.Weapons)
        {
            if (entries is not { Count: > 0 }) continue;
            var mapped = new List<CodBlueprint>(entries.Count);
            foreach (var entry in entries)
            {
                mapped.Add(new CodBlueprint
                {
                    Name = entry.Name ?? string.Empty,
                    Codename = entry.BlueprintCodename ?? string.Empty,
                    Rarity = entry.Rarity ?? string.Empty,
                    HowToObtain = entry.HowToObtain ?? string.Empty,
                    ImageUrl = entry.ImageUrl ?? string.Empty,
                    SourceUrl = entry.SourceUrl ?? string.Empty,
                    RevisionId = entry.RevisionId,
                });
            }
            lookup[key] = mapped;
            total += mapped.Count;
        }
        return new BlueprintLookup(lookup, total, dto.FetchedAt ?? string.Empty);
    }

    private static async Task<CodQaDto?> ReadQaAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, CodWeaponJsonContext.Default.CodQaDto, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<CodRefreshManifestDto?> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, CodWeaponJsonContext.Default.CodRefreshManifestDto, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string FormatTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : value;
    }

    private sealed record BlueprintLookup(Dictionary<string, IReadOnlyList<CodBlueprint>> Weapons, int TotalCount, string FetchedAt)
    {
        public static readonly BlueprintLookup Empty = new(new Dictionary<string, IReadOnlyList<CodBlueprint>>(StringComparer.Ordinal), 0, string.Empty);
    }
}

internal sealed class CodWeaponLineDto
{
    [JsonPropertyName("game_id")] public string? GameId { get; set; }
    [JsonPropertyName("game_short_name")] public string? GameShortName { get; set; }
    [JsonPropertyName("game_title")] public string? GameTitle { get; set; }
    [JsonPropertyName("engine_code")] public string? EngineCode { get; set; }
    [JsonPropertyName("weapon_display_name")] public string? WeaponDisplayName { get; set; }
    [JsonPropertyName("wiki_title")] public string? WikiTitle { get; set; }
    [JsonPropertyName("weapon_class")] public string? WeaponClass { get; set; }
    [JsonPropertyName("codename")] public string? Codename { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("native_or_carryover")] public string? NativeOrCarryover { get; set; }
    [JsonPropertyName("section_title")] public string? SectionTitle { get; set; }
    [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
    [JsonPropertyName("source_kind")] public string? SourceKind { get; set; }
    [JsonPropertyName("review_status")] public string? ReviewStatus { get; set; }
    [JsonPropertyName("revision_id")] public long RevisionId { get; set; }
    [JsonPropertyName("revision_timestamp")] public string? RevisionTimestamp { get; set; }
    [JsonPropertyName("applied_override_ids")] public List<string>? AppliedOverrideIds { get; set; }
}

internal sealed class CodBlueprintFileDto
{
    [JsonPropertyName("blueprint_count")] public int BlueprintCount { get; set; }
    [JsonPropertyName("fetched_at")] public string? FetchedAt { get; set; }
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("source_scope")] public string? SourceScope { get; set; }
    [JsonPropertyName("unmatched_weapons")] public List<string>? UnmatchedWeapons { get; set; }
    [JsonPropertyName("weapons")] public Dictionary<string, List<CodBlueprintDto>>? Weapons { get; set; }
}

internal sealed class CodBlueprintDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("blueprint_codename")] public string? BlueprintCodename { get; set; }
    [JsonPropertyName("rarity")] public string? Rarity { get; set; }
    [JsonPropertyName("how_to_obtain")] public string? HowToObtain { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
    [JsonPropertyName("revision_id")] public long RevisionId { get; set; }
}

internal sealed class CodQaDto
{
    [JsonPropertyName("source_mode")] public string? SourceMode { get; set; }
    [JsonPropertyName("issue_count")] public int IssueCount { get; set; }
}

internal sealed class CodRefreshManifestDto
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("source_mode")] public string? SourceMode { get; set; }
    [JsonPropertyName("blueprints_sha256")] public string? BlueprintsSha256 { get; set; }
    [JsonPropertyName("manifest_sha256")] public string? ManifestSha256 { get; set; }
    [JsonPropertyName("supplemental_sources")] public List<CodSupplementalSourceDto>? SupplementalSources { get; set; }
}

internal sealed class CodSupplementalSourceDto
{
    [JsonPropertyName("repository")] public string? Repository { get; set; }
    [JsonPropertyName("commit_sha")] public string? CommitSha { get; set; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CodWeaponLineDto))]
[JsonSerializable(typeof(CodBlueprintFileDto))]
[JsonSerializable(typeof(CodQaDto))]
[JsonSerializable(typeof(CodRefreshManifestDto))]
internal sealed partial class CodWeaponJsonContext : JsonSerializerContext;
