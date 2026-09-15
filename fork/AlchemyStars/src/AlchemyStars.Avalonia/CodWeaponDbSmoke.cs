using System.Net;
using System.Net.Http;
using System.Text;

namespace AlchemyStars.Avalonia;

/// <summary>
/// Offline verification for the COD weapon database page: bundled snapshot
/// loading, search ranking, dataset detection and the wiki icon pipeline driven
/// by a stub transport so no network access is required.
/// </summary>
internal static class CodWeaponDbSmoke
{
    /// <summary>1×1 PNG used to prove the fetched bytes decode into a real bitmap.</summary>
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private const string PageImageResponse = """
        {"batchcomplete":true,"query":{"pages":[{"pageid":3369,"ns":0,"title":"M4A1",
        "thumbnail":{"source":"https://static.wikia.nocookie.net/callofduty/images/b/b2/M4_Carbine_Menu_Icon_MWR.png/revision/latest/scale-to-width-down/640?cb=1","width":640,"height":194},
        "original":{"source":"https://static.wikia.nocookie.net/callofduty/images/b/b2/M4_Carbine_Menu_Icon_MWR.png/revision/latest?cb=1","width":725,"height":219},
        "pageimage":"M4_Carbine_Menu_Icon_MWR.png"}]}}
        """;

    private const string FileInfoResponse = """
        {"batchcomplete":true,"query":{"pages":[{"pageid":1,"ns":6,"title":"File:Arabesque 9mmPM Blueprint BO6.png",
        "imageinfo":[{"url":"https://static.wikia.nocookie.net/callofduty/images/1/1a/Arabesque_9mmPM_Blueprint_BO6.png/revision/latest?cb=2"}]}]}}
        """;

    internal static int Run(string[] args)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "alchemy-stars-cod-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);

            var catalog = CodWeaponCatalog.LoadAsync(CodWeaponCatalog.BuiltInDirectory)
                .GetAwaiter().GetResult();
            Require(catalog.Weapons.Count > 1000, $"bundled snapshot should expose the full roster, saw {catalog.Weapons.Count}.");
            Require(catalog.Games.Count >= 20, $"bundled snapshot should cover every game file, saw {catalog.Games.Count}.");
            Require(catalog.BlueprintCount > 4000, $"blueprint join should attach wiki blueprints, saw {catalog.BlueprintCount}.");
            Console.WriteLine($"Catalog: {catalog.Weapons.Count} records, {catalog.Games.Count} games, {catalog.Classes.Count} classes, {catalog.BlueprintCount} blueprints.");

            // Every blueprint key in blueprints.json must resolve onto a weapon record.
            var withBlueprints = catalog.Weapons.Count(weapon => weapon.HasBlueprints);
            Require(withBlueprints > 300, $"blueprint join should attach to hundreds of records, saw {withBlueprints}.");

            var unknown = catalog.Weapons.FirstOrDefault(weapon => weapon.Class.Equals("Unknown", StringComparison.Ordinal));
            Require(unknown is not null, "the GitHub table keeps unclassified records, so at least one must exist.");

            // Codename lookup ranks an exact hit above a partial one.
            var sample = catalog.Weapons.First(weapon => weapon.HasCodename && weapon.Codename.Length > 4);
            var exact = catalog.Query(sample.Codename, CodWeaponFilter.None);
            Require(exact.Count > 0 && ReferenceEquals(exact[0], sample),
                $"an exact codename must rank first: '{sample.Codename}' returned '{exact.FirstOrDefault()?.Codename}'.");
            Console.WriteLine($"Search: exact codename '{sample.Codename}' ranked first among {exact.Count} matches.");

            var partial = catalog.Query(sample.Codename[..4], CodWeaponFilter.None);
            Require(partial.Count >= exact.Count, "a partial codename must not narrow the result set.");

            var gameFiltered = catalog.Query(null, new CodWeaponFilter(sample.GameShortName));
            Require(gameFiltered.Count > 0 && gameFiltered.All(weapon => weapon.GameShortName == sample.GameShortName),
                "the game filter must only return records from that game.");

            var prefixFiltered = catalog.Query(null, new CodWeaponFilter(CodenamePrefix: "sm_"));
            Require(prefixFiltered.Count > 0 && prefixFiltered.All(weapon => weapon.Codename.StartsWith("sm_", StringComparison.OrdinalIgnoreCase)),
                "the codename prefix filter must only return matching prefixes.");
            Console.WriteLine($"Filters: {gameFiltered.Count} in {sample.GameShortName}, {prefixFiltered.Count} with prefix sm_.");

            Require(!catalog.Query("zzzz-not-a-weapon-zzzz", CodWeaponFilter.None).Any(), "a nonsense query must return nothing.");

            // Dataset detection and provenance.
            Require(CodWeaponCatalog.IsDatasetDirectory(CodWeaponCatalog.BuiltInDirectory), "the bundled snapshot must count as a dataset directory.");
            Require(!CodWeaponCatalog.IsDatasetDirectory(temporary), "an empty folder must not count as a dataset directory.");
            Require(!string.IsNullOrWhiteSpace(catalog.Provenance.Repository), "refresh-manifest.json must supply the upstream repository.");
            Require(catalog.Provenance.CommitSha.Length == 40, $"the pinned commit must be recorded, saw '{catalog.Provenance.CommitSha}'.");
            Console.WriteLine($"Provenance: {catalog.Provenance.Repository} @ {catalog.Provenance.CommitSha[..10]}, mode '{catalog.Provenance.SourceMode}'.");

            VerifyDatasetSwap(temporary, catalog);

            VerifyWikiUrls();
            VerifyIconPipeline(temporary).GetAwaiter().GetResult();
            if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
                VerifyLiveWiki(temporary, catalog).GetAwaiter().GetResult();

            var text = new UiText(true);
            Require(text.CodDbUnknownClass.Equals("未分类", StringComparison.Ordinal), "the Chinese class label for Unknown must read 未分类.");
            Require(new UiText(false).CodDbUnknownClass.Equals("Unclassified", StringComparison.Ordinal), "the English class label for Unknown must read Unclassified.");

            Console.WriteLine("COD weapon database: catalog, search, dataset swap and wiki icon pipeline PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>A minimal dist folder must load through the same code path as the bundled snapshot.</summary>
    private static void VerifyDatasetSwap(string temporary, CodWeaponCatalog bundled)
    {
        var directory = Path.Combine(temporary, "dist");
        Directory.CreateDirectory(directory);
        // weapons.jsonl is one compact record per line, so the fixture must be a single line too.
        const string record =
            "{\"game_id\":\"unit\",\"game_short_name\":\"UNIT\",\"game_title\":\"Unit Test\",\"engine_code\":\"UT\"," +
            "\"weapon_display_name\":\"Smoke Rifle\",\"wiki_title\":\"\",\"weapon_class\":\"Assault Rifle\",\"codename\":\"smk_rifle\"," +
            "\"scope\":\"mp\",\"native_or_carryover\":\"native\",\"section_title\":\"Smoke\",\"source_url\":\"https://example.invalid/smoke\"," +
            "\"source_kind\":\"github\",\"review_status\":\"source_only\",\"revision_id\":42,\"revision_timestamp\":\"2026-01-01T00:00:00Z\"," +
            "\"applied_override_ids\":[\"override-1\"]}";
        File.WriteAllText(Path.Combine(directory, "weapons.jsonl"), record + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, "blueprints.json"),
            """{"blueprint_count":1,"fetched_at":"2026-01-01T00:00:00Z","schema_version":1,"source_scope":"test","weapons":{"unit:Smoke Rifle":[{"name":"Smoke","blueprint_codename":"smk_bp","rarity":"Epic","how_to_obtain":"Test","image_url":"https://callofduty.fandom.com/wiki/File:Smoke_BP.png","source_url":"https://callofduty.fandom.com/wiki/Smoke","revision_id":7}]}}""");

        Require(CodWeaponCatalog.IsDatasetDirectory(directory), "a dist folder containing weapons.jsonl must be detected.");
        var swapped = CodWeaponCatalog.LoadAsync(directory).GetAwaiter().GetResult();
        Require(swapped.Weapons.Count == 1, $"the swapped dataset must replace the roster, saw {swapped.Weapons.Count}.");
        var weapon = swapped.Weapons[0];
        Require(weapon.Codename.Equals("smk_rifle", StringComparison.Ordinal), "the swapped record must keep its codename.");
        Require(weapon.HasBlueprints && weapon.Blueprints[0].Codename.Equals("smk_bp", StringComparison.Ordinal),
            "blueprints must join on game id and weapon name.");
        Require(weapon.HasScope && weapon.HasManualRevision && weapon.HasSourceUrl, "scope, overrides and source links must survive loading.");
        Require(weapon.ReferenceTitle.Equals("Smoke Rifle", StringComparison.Ordinal), "a blank wiki title must fall back to the display name.");
        Require(swapped.Provenance.WeaponCount == 1 && swapped.Provenance.BlueprintCount == 1 && swapped.Provenance.Repository.Length == 0,
            "provenance must describe the swapped dataset, and a folder without a manifest must not invent one.");
        Require(bundled.Weapons.Count > 1000, "loading a second dataset must not disturb the bundled one.");

        // A malformed record must fail loudly instead of silently dropping data.
        File.WriteAllText(Path.Combine(directory, "weapons.jsonl"), "{not json}" + Environment.NewLine);
        var failed = false;
        try { CodWeaponCatalog.LoadAsync(directory).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { failed = true; }
        Require(failed, "a malformed weapons.jsonl must raise InvalidDataException.");
        Console.WriteLine("Dataset: external dist folder loads, joins blueprints and rejects malformed input.");
    }

    private static void VerifyWikiUrls()
    {
        var page = CodWikiIconService.BuildPageImagesQuery("M4A1");
        Require(page.StartsWith("https://callofduty.fandom.com/api.php?", StringComparison.Ordinal), "the wiki API endpoint must be used.");
        Require(page.Contains("formatversion=2", StringComparison.Ordinal), "formatversion=2 keeps the response a page array.");
        Require(page.Contains("prop=pageimages", StringComparison.Ordinal) && page.Contains("pithumbsize=640", StringComparison.Ordinal),
            "the page image query must request a sized thumbnail.");
        Require(page.Contains("titles=M4A1", StringComparison.Ordinal), "the page image query must carry the title.");
        Require(CodWikiIconService.BuildPageImagesQuery("AK-47 (weapon)").Contains("AK-47%20%28weapon%29", StringComparison.Ordinal),
            "titles must be URL encoded.");

        var search = CodWikiIconService.BuildSearchPageImagesQuery("BAL27");
        Require(search.Contains("generator=search", StringComparison.Ordinal) && search.Contains("gsrnamespace=0", StringComparison.Ordinal),
            "the fallback must search articles only.");

        var file = CodWikiIconService.BuildFileInfoQuery("File:Smoke BP.png");
        Require(file.Contains("prop=imageinfo", StringComparison.Ordinal) && file.Contains("iiprop=url", StringComparison.Ordinal),
            "blueprint images resolve through imageinfo.");

        Require(CodWikiIconService.FileTitleFromUrl("https://callofduty.fandom.com/wiki/File:Arabesque_9mmPM_Blueprint_BO6.png")
                .Equals("File:Arabesque 9mmPM Blueprint BO6.png", StringComparison.Ordinal),
            "a blueprint image URL must yield its File: page name.");
        Require(CodWikiIconService.FileTitleFromUrl("https://example.invalid/not-a-file.png").Length == 0,
            "a non-wiki image URL must not produce a File: page.");
        Require(CodWikiIconService.PageUrlFor("AK-47 (weapon)").Equals("https://callofduty.fandom.com/wiki/AK-47_%28weapon%29", StringComparison.Ordinal),
            "page URLs must be escaped with underscores.");
        Console.WriteLine("Wiki: API query construction and file-title parsing verified.");
    }

    private static async Task VerifyIconPipeline(string temporary)
    {
        var cache = Path.Combine(temporary, "icons");
        var handler = new StubWikiHandler();
        using var service = new CodWikiIconService(cache, handler);
        var weapon = new CodWeapon
        {
            GameId = "unit",
            GameShortName = "UNIT",
            GameTitle = "Unit Test",
            Name = "M4A1",
            Class = "Assault Rifle",
            Codename = "ar_mike4",
        };

        var first = await service.GetWeaponIconAsync(weapon, false);
        Require(first.Succeeded, $"the stub wiki response must resolve an icon: {first.Error}");
        Require(first.Icon!.FromCache == false, "a first lookup must report a network fetch.");
        Require(first.Icon.Bytes.Length > 0, "the icon must carry image bytes.");
        Require(first.Icon.PageUrl.EndsWith("/wiki/M4A1", StringComparison.Ordinal), $"the article URL must be recorded, saw '{first.Icon.PageUrl}'.");
        Require(first.Icon.FileName.Equals("M4_Carbine_Menu_Icon_MWR.png", StringComparison.Ordinal), "the wiki file name must be kept for saving.");
        Require(handler.ImageRequests == 1, $"exactly one image download must happen, saw {handler.ImageRequests}.");

        // Bitmap decoding needs a live Avalonia platform, so the console smoke
        // proves the payload is a real PNG and the UI smoke proves it decodes.
        Require(first.Icon.Bytes.Length > 8
                && first.Icon.Bytes[0] == 0x89 && first.Icon.Bytes[1] == 0x50 && first.Icon.Bytes[2] == 0x4E && first.Icon.Bytes[3] == 0x47,
            "the fetched payload must be a PNG image.");

        var second = await service.GetWeaponIconAsync(weapon, false);
        Require(second.Succeeded && second.Icon!.FromCache, "a second lookup must come from the local cache.");
        Require(handler.ImageRequests == 1, "a cached lookup must not download again.");

        var refreshed = await service.GetWeaponIconAsync(weapon, true);
        Require(refreshed.Succeeded && !refreshed.Icon!.FromCache, "a forced refresh must bypass the cache.");
        Require(handler.ImageRequests == 2, $"a forced refresh must download once more, saw {handler.ImageRequests}.");

        Require(service.TryReadCachedBytes(first.Icon.ImageUrl, out var cachedBytes) && cachedBytes.Length == first.Icon.Bytes.Length,
            "cached bytes must be readable without a network round trip.");

        // A fresh service instance must find the same cache on disk.
        using (var reopened = new CodWikiIconService(cache, new StubWikiHandler()))
        {
            var cachedIcon = reopened.TryGetCachedWeaponIcon(weapon);
            Require(cachedIcon is not null && cachedIcon.Bytes.Length > 0, "the cache index must survive a restart.");
            Require(reopened.TryGetCachedWeaponIcon(new CodWeapon { GameId = "unit", Name = "Unknown Weapon" }) is null,
                "an uncached weapon must report no cached icon.");
        }

        // Blueprint images resolve through the File: page.
        var blueprint = new CodBlueprint { Name = "Arabesque", ImageUrl = "https://callofduty.fandom.com/wiki/File:Arabesque_9mmPM_Blueprint_BO6.png" };
        var blueprintIcon = await service.GetBlueprintIconAsync(weapon, blueprint);
        Require(blueprintIcon.Succeeded, $"a blueprint File: page must resolve: {blueprintIcon.Error}");
        Require(blueprintIcon.Icon!.PageTitle.StartsWith("File:", StringComparison.Ordinal), "the blueprint icon must keep its File: page title.");
        Require(service.TryGetCachedBlueprintIcon(blueprint) is not null, "a fetched blueprint icon must be cached.");

        // A transport failure must surface as an error, not an exception.
        using (var failing = new CodWikiIconService(Path.Combine(temporary, "icons-fail"), new FailingWikiHandler()))
        {
            var outcome = await failing.GetWeaponIconAsync(weapon, false);
            Require(!outcome.Succeeded && outcome.Error.Length > 0, "a transport failure must be reported, not thrown.");
            Require(outcome.Attempts.Count > 0, "a failed lookup must report the titles it tried.");
        }

        // A missing page falls through to the search fallback.
        using (var searching = new CodWikiIconService(Path.Combine(temporary, "icons-search"), new SearchFallbackHandler()))
        {
            var outcome = await searching.GetWeaponIconAsync(new CodWeapon { GameId = "unit", Name = "Bal-27" }, false);
            Require(outcome.Succeeded, $"the search fallback must resolve a differently named article: {outcome.Error}");
            Require(outcome.Icon!.PageTitle.Equals("Bal-27", StringComparison.Ordinal), "the search result title must be used.");
        }

        Console.WriteLine("Icons: resolve, download, cache reuse, forced refresh and failure reporting verified.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Optional networked check against the real Call of Duty Wiki. Kept behind
    /// an explicit flag so the default smoke run stays offline and deterministic.
    /// </summary>
    private static async Task VerifyLiveWiki(string temporary, CodWeaponCatalog catalog)
    {
        using var service = new CodWikiIconService(Path.Combine(temporary, "live-icons"));
        var weapon = catalog.Weapons.FirstOrDefault(candidate => candidate.Name.Equals("M4A1", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The bundled snapshot no longer contains M4A1.");
        var outcome = await service.GetWeaponIconAsync(weapon, false);
        Require(outcome.Succeeded, $"the live wiki lookup failed: {outcome.Error}");
        Require(outcome.Icon!.Bytes.Length > 0, "the live wiki returned no image bytes.");
        Require(CodWikiIconService.IsSupportedImage(outcome.Icon.Bytes),
            $"the live wiki payload must be a decodable image, saw {CodWikiIconService.DetectImageExtension(outcome.Icon.Bytes)}.");
        Require(outcome.Icon.PageUrl.Contains("callofduty.fandom.com/wiki/", StringComparison.Ordinal),
            $"the live lookup must record its wiki page: {outcome.Icon.PageUrl}");

        var cached = await service.GetWeaponIconAsync(weapon, false);
        Require(cached.Succeeded && cached.Icon!.FromCache, "a repeated live lookup must be served from the local cache.");
        Console.WriteLine($"Live wiki: {weapon.Name} -> {outcome.Icon.PageTitle} ({outcome.Icon.Bytes.Length} bytes, {outcome.Icon.Extension}, {outcome.Icon.FileName}).");
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static readonly byte[] PngBytes = Convert.FromBase64String(OnePixelPngBase64);

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Png() => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(PngBytes),
    };

    /// <summary>Answers every wiki call the way the live API does.</summary>
    private sealed class StubWikiHandler : HttpMessageHandler
    {
        public int ImageRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("prop=imageinfo", StringComparison.Ordinal)) return Task.FromResult(Json(FileInfoResponse));
            if (url.Contains("api.php", StringComparison.Ordinal)) return Task.FromResult(Json(PageImageResponse));
            ImageRequests++;
            return Task.FromResult(Png());
        }
    }

    /// <summary>Returns "missing" for a direct title and a hit for the search fallback.</summary>
    private sealed class SearchFallbackHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("generator=search", StringComparison.Ordinal))
                return Task.FromResult(Json("""
                    {"batchcomplete":true,"query":{"pages":[{"pageid":596921,"ns":0,"title":"Bal-27","index":1,
                    "thumbnail":{"source":"https://static.wikia.nocookie.net/callofduty/images/4/4f/Bal-27_menu_icon_AW.png/revision/latest/scale-to-width-down/640","width":640,"height":177},
                    "pageimage":"Bal-27_menu_icon_AW.png"}]}}
                    """));
            if (url.Contains("api.php", StringComparison.Ordinal))
                return Task.FromResult(Json("""{"batchcomplete":true,"query":{"pages":[{"ns":0,"title":"Bal-27","missing":true}]}}"""));
            return Task.FromResult(Png());
        }
    }

    /// <summary>Simulates an offline machine.</summary>
    private sealed class FailingWikiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
