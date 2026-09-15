using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlchemyStars.Avalonia;

/// <summary>A weapon or blueprint image resolved from the Call of Duty Wiki.</summary>
public sealed class CodWikiIcon
{
    public string CacheKey { get; init; } = string.Empty;
    public string PageTitle { get; init; } = string.Empty;
    public string PageUrl { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public byte[] Bytes { get; init; } = [];
    public string CachedPath { get; init; } = string.Empty;
    public bool FromCache { get; init; }
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>
    /// Real container extension. Fandom's CDN negotiates WebP even for URLs that
    /// end in <c>.png</c>, so the saved file must follow the bytes, not the URL.
    /// </summary>
    public string Extension => CodWikiIconService.DetectImageExtension(Bytes);

    public string Attribution => $"Call of Duty Wiki · {PageTitle} · CC BY-SA 3.0";
}

/// <summary>Outcome of an icon lookup, including why a lookup failed.</summary>
public sealed class CodWikiIconOutcome
{
    public CodWikiIcon? Icon { get; init; }
    public string Error { get; init; } = string.Empty;
    public IReadOnlyList<string> Attempts { get; init; } = [];

    public bool Succeeded => Icon is not null;
    public static CodWikiIconOutcome Failed(string error, IReadOnlyList<string> attempts) => new() { Error = error, Attempts = attempts };
}

/// <summary>
/// Resolves and caches reference icons from <c>callofduty.fandom.com</c>. The
/// wiki is reached through the public MediaWiki API: a direct page lookup first,
/// then a search fallback, then the file page behind a blueprint image. Every
/// downloaded image is cached on disk so a second lookup works offline.
/// </summary>
public sealed class CodWikiIconService : IDisposable
{
    private const string ApiEndpoint = "https://callofduty.fandom.com/api.php";
    private const string UserAgent = "AlchemyStars/1.3.0 (COD weapon reference panel; +https://github.com/ez4cywa/Alchemy-Stars)";
    private const int DefaultThumbnailSize = 640;
    private const int MaxImageBytes = 12 * 1024 * 1024;

    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly object indexLock = new();
    private readonly Dictionary<string, CodWikiCacheEntryDto> index = new(StringComparer.Ordinal);
    private bool indexLoaded;

    public CodWikiIconService(string cacheDirectory, HttpMessageHandler? handler = null)
    {
        CacheDirectory = Path.GetFullPath(cacheDirectory);
        client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        ownsClient = true;
        client.Timeout = TimeSpan.FromSeconds(30);
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    public string CacheDirectory { get; }
    private string IndexPath => Path.Combine(CacheDirectory, "index.json");

    /// <summary>Direct page lookup used for the weapon's wiki article.</summary>
    public static string BuildPageImagesQuery(string title, int thumbnailSize = DefaultThumbnailSize) =>
        ApiEndpoint
        + "?action=query&format=json&formatversion=2&redirects=1"
        + "&prop=pageimages&piprop=thumbnail%7Coriginal%7Cname&pithumbsize=" + thumbnailSize.ToString(CultureInfo.InvariantCulture)
        + "&titles=" + Uri.EscapeDataString(title);

    /// <summary>Search fallback for weapons whose console name differs from the wiki title.</summary>
    public static string BuildSearchPageImagesQuery(string search, int thumbnailSize = DefaultThumbnailSize) =>
        ApiEndpoint
        + "?action=query&format=json&formatversion=2&generator=search&gsrnamespace=0&gsrlimit=5"
        + "&gsrsearch=" + Uri.EscapeDataString(search)
        + "&prop=pageimages&piprop=thumbnail%7Coriginal%7Cname&pithumbsize=" + thumbnailSize.ToString(CultureInfo.InvariantCulture);

    /// <summary>Resolves a <c>File:</c> page (used by blueprint image links) to a direct URL.</summary>
    public static string BuildFileInfoQuery(string fileTitle) =>
        ApiEndpoint
        + "?action=query&format=json&formatversion=2&prop=imageinfo&iiprop=url"
        + "&titles=" + Uri.EscapeDataString(fileTitle);

    /// <summary>Wiki page name of a blueprint image URL, or an empty string when it is not a file page.</summary>
    public static string FileTitleFromUrl(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return string.Empty;
        const string marker = "/wiki/";
        var indexOf = imageUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0) return string.Empty;
        var title = imageUrl[(indexOf + marker.Length)..];
        var queryIndex = title.IndexOfAny(['?', '#']);
        if (queryIndex >= 0) title = title[..queryIndex];
        title = Uri.UnescapeDataString(title).Replace('_', ' ');
        return title.StartsWith("File:", StringComparison.OrdinalIgnoreCase) ? title : string.Empty;
    }

    /// <summary>Public wiki page URL for a weapon, used by the "open source" action.</summary>
    public static string PageUrlFor(string title) =>
        "https://callofduty.fandom.com/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));

    /// <summary>Sniffs the image container from its magic bytes.</summary>
    public static string DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return ".webp";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
        return ".bin";
    }

    /// <summary>True when the bytes start with a container this app can display.</summary>
    public static bool IsSupportedImage(byte[] bytes) => DetectImageExtension(bytes) is ".png" or ".webp" or ".jpg" or ".gif" or ".bmp";

    public async Task<CodWikiIconOutcome> GetWeaponIconAsync(CodWeapon weapon, bool forceRefresh, CancellationToken cancellationToken = default)
    {
        var cacheKey = "weapon|" + weapon.Key;
        if (!forceRefresh && TryReadCached(cacheKey) is { } cached)
            return new CodWikiIconOutcome { Icon = cached, Attempts = [weapon.ReferenceTitle] };

        var attempts = new List<string>();
        // The console table name and the wiki article title differ often enough
        // that a plain search is worth trying before reporting a miss.
        var page = await ResolvePageIconAsync(weapon.ReferenceTitle, attempts, cancellationToken).ConfigureAwait(false);
        if (page is null)
            page = await ResolveSearchIconAsync(weapon.Name, attempts, cancellationToken).ConfigureAwait(false);
        if (page is null && !string.Equals(weapon.ReferenceTitle, weapon.Name, StringComparison.OrdinalIgnoreCase))
            page = await ResolveSearchIconAsync(weapon.ReferenceTitle, attempts, cancellationToken).ConfigureAwait(false);
        if (page is null)
            return CodWikiIconOutcome.Failed("no wiki page with a lead image matched this weapon", attempts);

        return await DownloadAsync(cacheKey, page, attempts, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CodWikiIconOutcome> GetBlueprintIconAsync(CodWeapon weapon, CodBlueprint blueprint, CancellationToken cancellationToken = default)
    {
        var fileTitle = FileTitleFromUrl(blueprint.ImageUrl);
        if (fileTitle.Length == 0)
            return CodWikiIconOutcome.Failed("this blueprint has no wiki image link", []);

        var cacheKey = "file|" + fileTitle;
        if (TryReadCached(cacheKey) is { } cached)
            return new CodWikiIconOutcome { Icon = cached, Attempts = [fileTitle] };

        var attempts = new List<string> { fileTitle };
        var resolved = await ResolveFileImageAsync(fileTitle, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
            return CodWikiIconOutcome.Failed("the wiki file page could not be resolved", attempts);

        return await DownloadAsync(cacheKey, resolved, attempts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads bytes already cached for an image URL without any network access.</summary>
    public bool TryReadCachedBytes(string imageUrl, out byte[] bytes)
    {
        bytes = [];
        var path = Path.Combine(CacheDirectory, Hash(imageUrl) + ".bin");
        if (!File.Exists(path)) return false;
        try
        {
            bytes = File.ReadAllBytes(path);
            return bytes.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Cache-only lookup used to paint the reference panel before any network call.</summary>
    public CodWikiIcon? TryGetCachedWeaponIcon(CodWeapon weapon) => TryReadCached("weapon|" + weapon.Key);

    /// <summary>Cache-only lookup for a blueprint image.</summary>
    public CodWikiIcon? TryGetCachedBlueprintIcon(CodBlueprint blueprint)
    {
        var fileTitle = FileTitleFromUrl(blueprint.ImageUrl);
        return fileTitle.Length == 0 ? null : TryReadCached("file|" + fileTitle);
    }

    private CodWikiIcon? TryReadCached(string cacheKey)
    {
        try
        {
            lock (indexLock)
            {
                LoadIndex();
                if (!index.TryGetValue(cacheKey, out var entry) || string.IsNullOrWhiteSpace(entry.ImageUrl)) return null;
                var path = Path.Combine(CacheDirectory, Hash(entry.ImageUrl) + ".bin");
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0) return null;
                return new CodWikiIcon
                {
                    CacheKey = cacheKey,
                    PageTitle = entry.PageTitle ?? string.Empty,
                    PageUrl = entry.PageUrl ?? string.Empty,
                    ImageUrl = entry.ImageUrl,
                    FileName = entry.FileName ?? string.Empty,
                    Bytes = bytes,
                    CachedPath = path,
                    FromCache = true,
                    FetchedAt = DateTimeOffset.TryParse(entry.FetchedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : DateTimeOffset.MinValue,
                };
            }
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task<CodWikiIconOutcome> DownloadAsync(string cacheKey, CodWikiPage page, IReadOnlyList<string> attempts, CancellationToken cancellationToken)
    {
        var imageUrl = page.ImageUrl;
        if (imageUrl.Length == 0)
            return CodWikiIconOutcome.Failed("the wiki page has no lead image", attempts);
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return CodWikiIconOutcome.Failed("the wiki returned an unusable image address", attempts);

        byte[] bytes;
        try
        {
            bytes = await client.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return CodWikiIconOutcome.Failed($"downloading the icon failed: {error.Message}", attempts);
        }
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
            return CodWikiIconOutcome.Failed("the wiki returned an empty or oversized image", attempts);

        var path = Path.Combine(CacheDirectory, Hash(imageUrl) + ".bin");
        try
        {
            lock (indexLock)
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllBytes(path, bytes);
                LoadIndex();
                index[cacheKey] = new CodWikiCacheEntryDto
                {
                    PageTitle = page.PageTitle,
                    PageUrl = page.PageUrl,
                    ImageUrl = imageUrl,
                    FileName = page.FileName,
                    FetchedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                };
                SaveIndex();
            }
        }
        catch (IOException)
        {
            // A cache write failure must not lose the icon we already downloaded.
        }

        return new CodWikiIconOutcome
        {
            Attempts = attempts,
            Icon = new CodWikiIcon
            {
                CacheKey = cacheKey,
                PageTitle = page.PageTitle,
                PageUrl = page.PageUrl,
                ImageUrl = imageUrl,
                FileName = page.FileName,
                Bytes = bytes,
                CachedPath = path,
                FromCache = false,
                FetchedAt = DateTimeOffset.UtcNow,
            },
        };
    }

    private async Task<CodWikiPage?> ResolvePageIconAsync(string title, List<string> attempts, CancellationToken cancellationToken)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0) return null;
        attempts.Add(trimmed);
        var json = await GetStringAsync(BuildPageImagesQuery(trimmed), cancellationToken).ConfigureAwait(false);
        if (json is null) return null;
        return PickPage(ParsePages(json));
    }

    private async Task<CodWikiPage?> ResolveSearchIconAsync(string search, List<string> attempts, CancellationToken cancellationToken)
    {
        var trimmed = search.Trim();
        if (trimmed.Length == 0) return null;
        if (!attempts.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) attempts.Add(trimmed);
        var json = await GetStringAsync(BuildSearchPageImagesQuery(trimmed), cancellationToken).ConfigureAwait(false);
        if (json is null) return null;
        return PickPage(ParsePages(json));
    }

    private async Task<CodWikiPage?> ResolveFileImageAsync(string fileTitle, CancellationToken cancellationToken)
    {
        var json = await GetStringAsync(BuildFileInfoQuery(fileTitle), cancellationToken).ConfigureAwait(false);
        if (json is null) return null;
        foreach (var page in ParsePages(json))
        {
            if (page.Missing || page.ImageInfo is not { Count: > 0 } info) continue;
            var url = info[0].Url;
            if (string.IsNullOrWhiteSpace(url)) continue;
            var title = page.Title ?? fileTitle;
            return new CodWikiPage(title, PageUrlFor(title), url, FileNameFromUrl(url));
        }
        return null;
    }

    private async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static IReadOnlyList<WikiPageDto> ParsePages(string json)
    {
        try
        {
            var response = JsonSerializer.Deserialize(json, CodWikiJsonContext.Default.WikiQueryResponseDto);
            return response?.Query?.Pages ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Chooses the best page for a weapon icon. Search results arrive ranked by
    /// <c>index</c>; sub-pages such as "Bal-27/Variants" are skipped because
    /// their lead image is a variant collage rather than the weapon icon.
    /// </summary>
    private static CodWikiPage? PickPage(IReadOnlyList<WikiPageDto> pages)
    {
        var candidates = pages
            .Where(page => !page.Missing && page.ImageUrl is not null)
            .OrderBy(page => page.Index <= 0 ? int.MaxValue : page.Index)
            .ToArray();
        foreach (var page in candidates)
        {
            var title = page.Title ?? string.Empty;
            if (title.Contains('/', StringComparison.Ordinal)) continue;
            return new CodWikiPage(title, PageUrlFor(title), page.ImageUrl!, page.FileName ?? FileNameFromUrl(page.ImageUrl!));
        }
        return candidates.Length > 0
            ? new CodWikiPage(
                candidates[0].Title ?? string.Empty,
                PageUrlFor(candidates[0].Title ?? string.Empty),
                candidates[0].ImageUrl!,
                candidates[0].FileName ?? FileNameFromUrl(candidates[0].ImageUrl!))
            : null;
    }

    private static string FileNameFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return string.Empty;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var revisionIndex = Array.FindIndex(segments, segment => segment.Equals("revision", StringComparison.OrdinalIgnoreCase));
        var nameIndex = revisionIndex > 0 ? revisionIndex - 1 : segments.Length - 1;
        return nameIndex >= 0 && nameIndex < segments.Length ? Uri.UnescapeDataString(segments[nameIndex]) : string.Empty;
    }

    private void LoadIndex()
    {
        if (indexLoaded) return;
        indexLoaded = true;
        if (!File.Exists(IndexPath)) return;
        try
        {
            var document = JsonSerializer.Deserialize(File.ReadAllText(IndexPath), CodWikiJsonContext.Default.CodWikiCacheDocument);
            if (document?.Entries is null) return;
            foreach (var (key, value) in document.Entries) index[key] = value;
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            index.Clear();
        }
    }

    /// <summary>Must be called while holding <see cref="indexLock"/>.</summary>
    private void SaveIndex()
    {
        var document = new CodWikiCacheDocument { SchemaVersion = 1, Entries = index };
        var json = JsonSerializer.Serialize(document, CodWikiJsonContext.Default.CodWikiCacheDocument);
        var temporary = IndexPath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, IndexPath, overwrite: true);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
    }

    private sealed record CodWikiPage(string PageTitle, string PageUrl, string ImageUrl, string FileName);
}

internal sealed class WikiQueryResponseDto
{
    [JsonPropertyName("query")] public WikiQueryDto? Query { get; set; }
}

internal sealed class WikiQueryDto
{
    [JsonPropertyName("pages")] public List<WikiPageDto>? Pages { get; set; }
}

internal sealed class WikiPageDto
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("missing")] public bool Missing { get; set; }
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("pageimage")] public string? FileName { get; set; }
    [JsonPropertyName("thumbnail")] public WikiImageDto? Thumbnail { get; set; }
    [JsonPropertyName("original")] public WikiImageDto? Original { get; set; }
    [JsonPropertyName("imageinfo")] public List<WikiImageInfoDto>? ImageInfo { get; set; }

    /// <summary>Prefer the scaled thumbnail; fall back to the original file.</summary>
    internal string? ImageUrl => Thumbnail?.Source ?? Original?.Source;
}

internal sealed class WikiImageDto
{
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

internal sealed class WikiImageInfoDto
{
    [JsonPropertyName("url")] public string? Url { get; set; }
}

internal sealed class CodWikiCacheDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("entries")] public Dictionary<string, CodWikiCacheEntryDto>? Entries { get; set; }
}

internal sealed class CodWikiCacheEntryDto
{
    [JsonPropertyName("page_title")] public string? PageTitle { get; set; }
    [JsonPropertyName("page_url")] public string? PageUrl { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("file_name")] public string? FileName { get; set; }
    [JsonPropertyName("fetched_at")] public string? FetchedAt { get; set; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WikiQueryResponseDto))]
[JsonSerializable(typeof(CodWikiCacheDocument))]
internal sealed partial class CodWikiJsonContext : JsonSerializerContext;
