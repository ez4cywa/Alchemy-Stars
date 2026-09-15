using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

/// <summary>
/// In-app verification for the COD weapon database page. It runs inside a real
/// Avalonia lifetime so bitmap decoding, control visibility and the comparison
/// window are exercised for real, while the wiki transport is stubbed so the
/// check stays offline and deterministic.
/// </summary>
internal static class CodWeaponDbUiSmoke
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private const string PageImageResponse = """
        {"batchcomplete":true,"query":{"pages":[{"pageid":3369,"ns":0,"title":"Smoke Icon",
        "thumbnail":{"source":"https://static.wikia.nocookie.net/callofduty/images/s/smoke.png/revision/latest/scale-to-width-down/640","width":640,"height":194},
        "pageimage":"smoke.png"}]}}
        """;

    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        var originalPage = vm.SelectedPage;
        var originalChinese = vm.IsChinese;
        var temporary = Path.Combine(Path.GetTempPath(), "alchemy-stars-cod-ui-" + Guid.NewGuid().ToString("N"));
        CodWikiIconService? stub = null;
        try
        {
            Directory.CreateDirectory(temporary);
            var navigation = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "CodDbNavigation")
                ?? throw new InvalidOperationException("The sidebar has no COD weapon database entry.");
            ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(navigation)!).Invoke();
            await Task.Delay(80);
            Require(vm.SelectedPage == WorkspacePage.CodWeaponDb, "The sidebar entry did not open the weapon database page.");
            Require(await vm.WaitForCodWeaponDbAsync(TimeSpan.FromSeconds(60)), "The bundled COD snapshot did not finish loading.");

            var view = window.GetVisualDescendants().OfType<CodWeaponDbView>().SingleOrDefault()
                ?? throw new InvalidOperationException("The weapon database view is not present.");
            Require(view.IsEffectivelyVisible, "The weapon database view is not visible.");
            Require(vm.CodResults.Count > 1000, $"The unfiltered list must show the whole roster, saw {vm.CodResults.Count}.");
            Require(vm.CodGameOptions.Count == vm.CodCatalogGameCount + 1, "Every game must appear in the game filter plus the 'all' entry.");
            Require(vm.CodClassOptions.Count >= 12, $"The class filter must list the derived classes, saw {vm.CodClassOptions.Count}.");
            Require(view.GamePicker.SelectedItem is CodFilterOption { Value: "" } && ReferenceEquals(view.GamePicker.SelectedItem, vm.CodGameFilter),
                $"The game filter must show the 'all games' entry, saw '{view.GamePicker.SelectedItem}'.");
            Require(view.ClassPicker.SelectedItem is CodFilterOption { Value: "" } && ReferenceEquals(view.ClassPicker.SelectedItem, vm.CodClassFilter),
                $"The class filter must show the 'all classes' entry, saw '{view.ClassPicker.SelectedItem}'.");
            Console.WriteLine($"COD page: {vm.CodResults.Count} rows, {vm.CodGameOptions.Count - 1} games, {vm.CodClassOptions.Count - 1} classes.");

            // Sidebar and header chrome.
            Require(navigation.Bounds.Width >= 44 && navigation.Bounds.Height >= 44, "The sidebar hit target is too small.");
            foreach (var icon in view.GetVisualDescendants().OfType<ThemedIcon>().Where(item => item.IsEffectivelyVisible))
                Require(icon.HasOriginalIcon || icon.HasClassicIcon || icon.HasWindowsXpIcon || icon.HasWindows2000Icon
                        || icon.HasCustomIcon || icon.HasDesktopIcon,
                    $"A page icon failed to resolve: {icon.Glyph}");

            // A selected weapon must expose its codename and blueprints.
            var first = vm.CodSelectedWeapon;
            Require(first is not null, "Opening the page must select a weapon.");
            Require(first!.HasCodename && vm.CodCodenameDisplay == first.Codename, "The selected weapon must show its console codename.");
            Require(vm.CodHasBlueprints || vm.CodShowNoBlueprints, "The blueprint section must report either blueprints or an explicit empty state.");

            // Searching by an exact codename must select that weapon.
            var sample = vm.CodResults.First(weapon => weapon.HasCodename && weapon.Blueprints.Count > 0);
            view.SearchBox.Text = sample.Codename;
            await Task.Delay(120);
            Require(ReferenceEquals(vm.CodSelectedWeapon, sample),
                $"An exact codename search must select '{sample.Name}', selected '{vm.CodSelectedWeapon?.Name}'.");
            Require(vm.CodResults.Count is > 0 and < 200, $"A codename search must narrow the list, saw {vm.CodResults.Count}.");
            Require(view.BlueprintList.ItemCount > 0, "A weapon with blueprints must list them.");
            Console.WriteLine($"Search: '{sample.Codename}' selected {sample.Name} with {sample.Blueprints.Count} blueprints.");

            // Filters must round-trip through the combo boxes.
            view.SearchBox.Text = string.Empty;
            var gameIndex = 1 + vm.CodGameOptions.Skip(1).ToList().FindIndex(option => option.Value == sample.GameShortName);
            view.GamePicker.SelectedIndex = gameIndex;
            await Task.Delay(120);
            Require(vm.CodResults.Count > 0 && vm.CodResults.All(weapon => weapon.GameShortName == sample.GameShortName),
                "The game picker must filter the list.");
            Require(vm.CodSelectedWeapon is not null, "A filtered list must still select a weapon.");
            Require(view.ClassPicker.SelectedItem is CodFilterOption { Value: "" }, "The class picker must stay on 'all classes'.");
            view.GamePicker.SelectedItem = vm.CodGameOptions[0];
            await Task.Delay(120);
            Require(vm.CodResults.Count > 1000, "Resetting the game picker must restore the full roster.");

            // The unclassified label must follow the interface language.
            var unknown = vm.CodResults.FirstOrDefault(weapon => weapon.Class.Equals("Unknown", StringComparison.Ordinal));
            if (unknown is not null)
            {
                var chineseLabel = vm.IsChinese ? unknown.ClassLabel : null;
                vm.ToggleLanguage();
                await Task.Delay(120);
                Require(unknown.ClassLabel != "Unknown", "The unclassified class must be shown in the interface language, not as raw data.");
                if (chineseLabel is not null) Require(unknown.ClassLabel != chineseLabel, "Switching language must re-label the class.");
                Require(vm.CodClassOptions.Any(option => option.Label == unknown.ClassLabel), "The class filter must reuse the localized label.");
                vm.ToggleLanguage();
                await Task.Delay(80);
            }

            // The in-app database update controls must be present and actionable.
            var checkButton = view.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "CodCheckUpdateButton")
                ?? throw new InvalidOperationException("The database update card has no check button.");
            var updateButton = view.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "CodUpdateDatabaseButton")
                ?? throw new InvalidOperationException("The database update card has no update button.");
            Require(checkButton.IsEffectivelyVisible && checkButton.IsEffectivelyEnabled,
                $"The check-for-updates button is not actionable (visible={checkButton.IsEffectivelyVisible}, enabled={checkButton.IsEffectivelyEnabled}, own={checkButton.IsEnabled}, vm={vm.CodCanUpdateDatabase}, ready={vm.CodIsReady}, updating={vm.CodIsUpdatingDatabase}, busy={vm.IsBusy}, dialog={vm.IsDialogOpen}, interact={vm.CanInteract}).");
            Require(updateButton.IsEffectivelyVisible && updateButton.IsEffectivelyEnabled, "The update button is not actionable.");
            Require(checkButton.Bounds.Width >= 44 && checkButton.Bounds.Height >= 44, "The check-for-updates hit target is too small.");
            Require(updateButton.Bounds.Width >= 44 && updateButton.Bounds.Height >= 44, "The update hit target is too small.");
            Require(!string.IsNullOrWhiteSpace(vm.CodUpdateSummary), "The update card must show the loaded dataset revision.");
            Require(!vm.CodHasUpdate, "An unchecked dataset must not claim an available update before a check runs.");

            // A failing check must report a reason and stay actionable.
            var failing = new CodWeaponDbUpdater(new ThrowingHandler());
            vm.OverrideCodUpdater(failing);
            await vm.CheckCodUpdateAsync();
            Require(!vm.CodHasUpdate, "A failed check must not report an available update.");
            Require(vm.CodUpdateStatus.Length > 0, "A failed check must surface a status message.");
            Require(vm.CodCanUpdateDatabase, "The card must stay actionable after a failed check.");
            vm.OverrideCodUpdater(null);
            Console.WriteLine($"COD page: update card verified, loaded revision '{vm.CodUpdateSummary}'.");

            // Reference icon: stub transport, real download/decode/cache path.
            var handler = new StubWikiHandler();
            stub = new CodWikiIconService(Path.Combine(temporary, "icons"), handler);
            vm.OverrideCodIconService(stub);
            vm.CodSelectedWeapon = sample;
            await Task.Delay(80);
            Require(ReferenceEquals(vm.CodSelectedWeapon, sample), "The blueprint sample must stay selected for the icon check.");
            Require(!vm.CodHasIcon, "A weapon with no cached icon must start without one.");
            Require(vm.CodCanFetchIcon, "The fetch action must be available once a weapon is selected.");
            await vm.FetchCodIconAsync(false);
            Require(vm.CodHasIcon, "The fetched icon must decode into a displayable bitmap.");
            Require(vm.CodIconBitmap!.Size.Width == 1, "The stub image must decode at its real size.");
            Require(vm.CodIconAttribution.Contains("CC BY-SA", StringComparison.Ordinal), "The icon must carry its wiki attribution.");
            Require(view.IconPreview.Source is not null && view.IconPreview.IsEffectivelyVisible, "The reference panel must display the icon.");
            Require(vm.CodIconStatus != vm.Text.CodDbFetching, "The fetch status must settle once the icon arrives.");

            // Repeated selection must reuse the cache rather than re-downloading.
            var requestsAfterFetch = handler.ImageRequests;
            vm.CodSelectedWeapon = vm.CodResults.Last();
            vm.CodSelectedWeapon = sample;
            await Task.Delay(120);
            Require(vm.CodHasIcon, "Returning to a weapon must restore its cached icon.");
            Require(vm.CodCanSaveIcon, "A cached icon must be saveable.");

            // The comparison window must bind to the live view model.
            var reference = new CodIconReferenceWindow(vm) { Position = new PixelPoint(-32000, -32000) };
            try
            {
                reference.Show(window);
                await Task.Delay(150);
                Require(reference.Preview.Source is not null, "The comparison window must show the reference icon.");
                Require(reference.TopmostToggle.IsChecked == vm.CodCompareTopmost, "The comparison window must reflect the topmost toggle.");
                Require(reference.Title!.Contains(sample.Name, StringComparison.Ordinal), "The comparison window title must name the weapon.");
                var before = vm.CodCompareImageWidth;
                reference.ScaleSlider.Value = 1.6;
                await Task.Delay(80);
                Require(vm.CodCompareScale > 1.5 && vm.CodCompareImageWidth > before, "The comparison zoom must scale the reference image.");
            }
            finally
            {
                reference.Close();
            }

            Require(requestsAfterFetch == 1, $"Selecting a cached weapon must not download again, saw {requestsAfterFetch} requests.");
            Console.WriteLine("COD page: icon fetch, cache reuse, comparison window and language switching verified.");

            // Opt-in live path: the real CDN negotiates WebP even for .png URLs, so
            // decoding an actual response proves the reference panel can display it.
            if (Program.CodWeaponDbLiveRequested)
            {
                using var live = new CodWikiIconService(Path.Combine(temporary, "live"), null);
                vm.OverrideCodIconService(live);
                var liveWeapon = vm.CodResults.FirstOrDefault(weapon => weapon.Name.Equals("M4A1", StringComparison.OrdinalIgnoreCase))
                    ?? vm.CodResults[0];
                vm.CodSelectedWeapon = liveWeapon;
                await vm.FetchCodIconAsync(true);
                Require(vm.CodHasIcon, $"The live wiki icon must decode inside the app: {vm.CodIconStatus}");
                Require(vm.CodIconBitmap!.Size.Width > 8 && vm.CodIconBitmap.Size.Height > 8, "The live icon must decode at its real size.");
                Require(vm.CodIconAttribution.Contains("CC BY-SA", StringComparison.Ordinal), "The live icon must carry its attribution.");
                Console.WriteLine($"Live icon: {liveWeapon.Name} decoded {vm.CodIconBitmap.Size.Width}x{vm.CodIconBitmap.Size.Height} and rendered in the panel.");
            }
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"COD weapon database UI smoke failed: {error.Message}", error);
        }
        finally
        {
            vm.OverrideCodIconService(null);
            stub?.Dispose();
            vm.SelectPage(originalPage);
            if (vm.IsChinese != originalChinese) vm.ToggleLanguage();
            await Task.Delay(80);
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class StubWikiHandler : HttpMessageHandler
    {
        public int ImageRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri.Contains("api.php", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(PageImageResponse, Encoding.UTF8, "application/json"),
                });
            ImageRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Convert.FromBase64String(OnePixelPngBase64)),
            });
        }
    }

    /// <summary>Simulates an unreachable upstream so the failure path can be asserted offline.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
