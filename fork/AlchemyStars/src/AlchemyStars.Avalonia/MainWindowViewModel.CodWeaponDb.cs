using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;

namespace AlchemyStars.Avalonia;

/// <summary>One entry of a filter combo box; an empty <see cref="Value"/> means "no restriction".</summary>
public sealed class CodFilterOption(string label, string value)
{
    public string Label { get; set; } = label;
    public string Value { get; } = value;
    public override string ToString() => Label;
}

/// <summary>
/// The COD weapon database page: offline search over the bundled CODWeaponDB
/// snapshot plus on-demand reference icons from the Call of Duty Wiki.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private readonly ObservableCollection<CodWeapon> codResults = [];
    private readonly ObservableCollection<CodFilterOption> codGameOptions = [];
    private readonly ObservableCollection<CodFilterOption> codClassOptions = [];
    private CodWeaponCatalog? codCatalog;
    private CodWikiIconService? codIconService;
    private CodWeapon? codSelectedWeapon;
    private CodBlueprint? codSelectedBlueprint;
    private Bitmap? codIconBitmap;
    private CodWikiIcon? codIcon;
    private string codSearch = string.Empty;
    private string codPrefix = string.Empty;
    private string codStatus = string.Empty;
    private string codIconStatus = string.Empty;
    private string codDatasetDirectory = string.Empty;
    private bool codDatasetIsBuiltIn = true;
    private bool codIsLoading;
    private bool codIsFetchingIcon;
    private bool codLoaded;
    private CodFilterOption? codGameFilter;
    private CodFilterOption? codClassFilter;
    private long codIconRequestVersion;
    private bool codSuppressFilterRefresh;
    private CodWeaponDbUpdater? codUpdater;
    private CodWeaponDbRevision? codLatestRevision;
    private string codUpdateStatus = string.Empty;
    private bool codIsCheckingUpdate;
    private bool codIsUpdating;

    public ObservableCollection<CodWeapon> CodResults => codResults;
    public ObservableCollection<CodFilterOption> CodGameOptions => codGameOptions;
    public ObservableCollection<CodFilterOption> CodClassOptions => codClassOptions;

    public bool CodIsLoading { get => codIsLoading; private set { codIsLoading = value; OnPropertyChanged(); RaiseCodReadiness(); } }
    public bool CodIsFetchingIcon { get => codIsFetchingIcon; private set { codIsFetchingIcon = value; OnPropertyChanged(); OnPropertyChanged(nameof(CodCanFetchIcon)); } }
    public bool CodIsReady => codCatalog is not null && !codIsLoading;
    public bool CodCanFetchIcon => CodIsReady && !codIsFetchingIcon && codSelectedWeapon is not null;

    /// <summary>
    /// Every action that depends on the catalog being loaded must be re-raised whenever
    /// readiness flips; the actions are bound before the lazy load completes.
    /// </summary>
    private void RaiseCodReadiness()
    {
        OnPropertyChanged(nameof(CodIsReady));
        OnPropertyChanged(nameof(CodCanFetchIcon));
        OnPropertyChanged(nameof(CodCanUpdateDatabase));
        OnPropertyChanged(nameof(CodHasUpdate));
        OnPropertyChanged(nameof(CodUpdateSummary));
    }

    public string CodSearch
    {
        get => codSearch;
        set
        {
            if (codSearch == value) return;
            codSearch = value ?? string.Empty;
            OnPropertyChanged();
            RefreshCodResults();
        }
    }

    public string CodPrefix
    {
        get => codPrefix;
        set
        {
            if (codPrefix == value) return;
            codPrefix = value ?? string.Empty;
            OnPropertyChanged();
            RefreshCodResults();
        }
    }

    /// <summary>
    /// The selected filter entry. The pickers bind to the item, not the index:
    /// rebuilding the option list momentarily clears a bound ComboBox, and an
    /// index that "changes back" to its old value never reaches the control.
    /// A fresh option object always reaches it.
    /// </summary>
    public CodFilterOption? CodGameFilter
    {
        get => codGameFilter;
        set
        {
            // A rebuilt ItemsSource pushes null before the new entry is assigned.
            if (value is null || ReferenceEquals(codGameFilter, value)) return;
            codGameFilter = value;
            OnPropertyChanged();
            if (!codSuppressFilterRefresh) RefreshCodResults();
        }
    }

    public CodFilterOption? CodClassFilter
    {
        get => codClassFilter;
        set
        {
            if (value is null || ReferenceEquals(codClassFilter, value)) return;
            codClassFilter = value;
            OnPropertyChanged();
            if (!codSuppressFilterRefresh) RefreshCodResults();
        }
    }

    public CodWeapon? CodSelectedWeapon
    {
        get => codSelectedWeapon;
        set
        {
            if (ReferenceEquals(codSelectedWeapon, value)) return;
            codSelectedWeapon = value;
            codSelectedBlueprint = null;
            OnPropertyChanged();
            RaiseCodSelectionState();
            ShowCodCachedIcon();
        }
    }

    public CodBlueprint? CodSelectedBlueprint
    {
        get => codSelectedBlueprint;
        set
        {
            if (ReferenceEquals(codSelectedBlueprint, value)) return;
            codSelectedBlueprint = value;
            OnPropertyChanged();
            RaiseCodSelectionState();
            ShowCodCachedIcon();
        }
    }

    private void RaiseCodSelectionState()
    {
        OnPropertyChanged(nameof(CodHasSelection));
        OnPropertyChanged(nameof(CodBlueprints));
        OnPropertyChanged(nameof(CodHasBlueprints));
        OnPropertyChanged(nameof(CodShowNoBlueprints));
        OnPropertyChanged(nameof(CodCodenameDisplay));
        OnPropertyChanged(nameof(CodSelectedBlueprint));
        OnPropertyChanged(nameof(CodReferenceTitle));
        OnPropertyChanged(nameof(CodReferenceSubtitle));
        OnPropertyChanged(nameof(CodCanFetchIcon));
        OnPropertyChanged(nameof(CodWikiPageUrl));
        OnPropertyChanged(nameof(CodHasWikiLink));
        OnPropertyChanged(nameof(CodSourceUrl));
        OnPropertyChanged(nameof(CodHasSourceLink));
        OnPropertyChanged(nameof(CodSourceLabel));
        OnPropertyChanged(nameof(CodCanSaveIcon));
        OnPropertyChanged(nameof(CodCompareTitleText));
    }

    public IReadOnlyList<CodBlueprint> CodBlueprints => codSelectedWeapon?.Blueprints ?? [];
    public bool CodHasBlueprints => codSelectedWeapon is { Blueprints.Count: > 0 };
    public bool CodShowNoBlueprints => codSelectedWeapon is not null && !CodHasBlueprints;
    public bool CodHasSelection => codSelectedWeapon is not null;
    public bool CodHasResults => codResults.Count > 0;
    public bool CodHasIcon => codIconBitmap is not null;
    public Bitmap? CodIconBitmap => codIconBitmap;

    public string CodStatus { get => codStatus; private set { codStatus = value; OnPropertyChanged(); } }
    public string CodIconStatus { get => codIconStatus; private set { codIconStatus = value; OnPropertyChanged(); } }
    public string CodCodenameDisplay => codSelectedWeapon is null || !codSelectedWeapon.HasCodename ? Text.CodDbNoCodename : codSelectedWeapon.Codename;

    public string CodResultSummary => codCatalog is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, Text.CodDbDatasetLoaded, codResults.Count, codCatalog.BlueprintCount);

    public string CodDatasetLabel => codDatasetIsBuiltIn ? Text.CodDbDatasetBuiltIn : Text.CodDbDatasetExternal;
    public string CodDatasetDirectory => codDatasetDirectory;
    public bool CodDatasetIsBuiltIn => codDatasetIsBuiltIn;
    public bool CodHasExternalDatasetDirectory => !string.IsNullOrWhiteSpace(codDatasetDirectory);
    public bool CodCanRestoreDataset => !codDatasetIsBuiltIn && codCatalog is not null;
    public string CodDatasetDirectoryHelp => string.Format(CultureInfo.CurrentCulture, Text.CodDbDatasetDirHelp, codDatasetDirectory);

    public string CodReferenceTitle => codSelectedBlueprint is not null
        ? codSelectedBlueprint.DisplayName
        : codSelectedWeapon?.Name ?? string.Empty;

    public string CodReferenceSubtitle => codSelectedBlueprint is not null
        ? Text.CodDbBlueprints
        : codSelectedWeapon is { } weapon ? weapon.GameLabel + " · " + weapon.ClassLabel : string.Empty;

    public string CodIconAttribution => codIcon is null
        ? string.Empty
        : codIcon.Attribution + (codIcon.FromCache
            ? " · " + string.Format(CultureInfo.CurrentCulture, Text.CodDbIconCached, CodWeaponCatalog.FormatTimestamp(codIcon.FetchedAt.ToString("O", CultureInfo.InvariantCulture)))
            : " · " + string.Format(CultureInfo.CurrentCulture, Text.CodDbIconFetched, CodWeaponCatalog.FormatTimestamp(codIcon.FetchedAt.ToString("O", CultureInfo.InvariantCulture))));

    /// <summary>Wiki article behind the current reference target.</summary>
    public string CodWikiPageUrl => codSelectedBlueprint is { SourceUrl.Length: > 0 } blueprint
        ? blueprint.SourceUrl
        : codSelectedWeapon is null ? string.Empty : CodWikiIconService.PageUrlFor(codSelectedWeapon.ReferenceTitle);

    public bool CodHasWikiLink => !string.IsNullOrWhiteSpace(CodWikiPageUrl);

    /// <summary>Pinned upstream record link for the current selection.</summary>
    public string CodSourceUrl => codSelectedBlueprint is { SourceUrl.Length: > 0 } blueprint
        ? blueprint.SourceUrl
        : codSelectedWeapon?.SourceUrl ?? string.Empty;

    public bool CodHasSourceLink => !string.IsNullOrWhiteSpace(CodSourceUrl);

    public string CodSourceLabel => codSelectedBlueprint is not null ? Text.CodDbBlueprintSource : Text.CodDbOpenRevision;

    public bool CodCanSaveIcon => codIcon is { Bytes.Length: > 0 };

    public string CodProvenanceSummary
    {
        get
        {
            if (codCatalog is not { } catalog) return string.Empty;
            var provenance = catalog.Provenance;
            var parts = new List<string>
            {
                string.Format(CultureInfo.CurrentCulture, Text.CodDbDatasetLoaded, provenance.WeaponCount, provenance.BlueprintCount),
            };
            if (!string.IsNullOrWhiteSpace(provenance.Repository)) parts.Add(provenance.Repository);
            if (!string.IsNullOrWhiteSpace(provenance.SourceMode)) parts.Add(provenance.SourceMode);
            if (!string.IsNullOrWhiteSpace(provenance.CommitSha))
                parts.Add(Text.CodDbRevision + ": " + provenance.CommitSha[..Math.Min(10, provenance.CommitSha.Length)]);
            if (!string.IsNullOrWhiteSpace(provenance.BlueprintsFetchedAt))
                parts.Add(Text.CodDbBlueprintsFetched + ": " + CodWeaponCatalog.FormatTimestamp(provenance.BlueprintsFetchedAt));
            parts.Add(Text.CodDbQaIssues + ": " + provenance.IssueCount.ToString(CultureInfo.CurrentCulture));
            return string.Join(" · ", parts);
        }
    }

    private CodWikiIconService CodIconService => codIconService ??= new CodWikiIconService(preferences.CodWikiIconDirectory);
    private CodWeaponDbUpdater CodUpdater => codUpdater ??= new CodWeaponDbUpdater();

    public int CodCatalogGameCount => codCatalog?.Games.Count ?? 0;

    /// <summary>True while any database update step runs; the page disables its actions.</summary>
    public bool CodIsUpdatingDatabase => codIsCheckingUpdate || codIsUpdating;
    public bool CodCanUpdateDatabase => CodIsReady && !CodIsUpdatingDatabase;
    public string CodUpdateStatus { get => codUpdateStatus; private set { codUpdateStatus = value; OnPropertyChanged(); } }

    /// <summary>The loaded dataset is behind the latest upstream revision we saw.</summary>
    public bool CodHasUpdate => codLatestRevision is { } latest
        && codCatalog is { } catalog
        && latest.CommitSha.Length == 40
        && !latest.CommitSha.Equals(catalog.Provenance.CommitSha, StringComparison.OrdinalIgnoreCase);

    public string CodUpdateSummary
    {
        get
        {
            if (codCatalog is not { } catalog) return string.Empty;
            var loaded = catalog.Provenance.CommitSha;
            var loadedText = loaded.Length >= 8 ? loaded[..8] : (loaded.Length > 0 ? loaded : Text.CodDbUpdateUnknownRevision);
            if (codLatestRevision is not { } latest || latest.CommitSha.Length != 40) return loadedText;
            var checkedAt = CodWeaponCatalog.FormatTimestamp(latest.CommitTimestamp);
            return checkedAt.Length == 0 ? latest.ShortSha : $"{latest.ShortSha} · {checkedAt}";
        }
    }

    /// <summary>Reads only the advertised upstream revision; cheap enough to run on demand.</summary>
    public async Task CheckCodUpdateAsync()
    {
        if (CodIsUpdatingDatabase || !CodIsReady) return;
        codIsCheckingUpdate = true;
        OnPropertyChanged(nameof(CodIsUpdatingDatabase));
        OnPropertyChanged(nameof(CodCanUpdateDatabase));
        CodUpdateStatus = Text.CodDbUpdateChecking;
        try
        {
            var revision = await CodUpdater.CheckAsync().ConfigureAwait(true);
            codLatestRevision = revision;
            OnPropertyChanged(nameof(CodHasUpdate));
            OnPropertyChanged(nameof(CodUpdateSummary));
            CodUpdateStatus = CodHasUpdate ? Text.CodDbUpdateAvailable : Text.CodDbUpdateCurrent;
        }
        catch (Exception error)
        {
            CodUpdateStatus = DescribeUpdateFailure(error);
        }
        finally
        {
            codIsCheckingUpdate = false;
            OnPropertyChanged(nameof(CodIsUpdatingDatabase));
            OnPropertyChanged(nameof(CodCanUpdateDatabase));
        }
    }

    /// <summary>
    /// Rebuilds the dataset from the commit-pinned upstream tables and switches the
    /// page to it. Blueprints have no upstream table, so the current copy is carried over.
    /// </summary>
    public async Task UpdateCodDatabaseAsync()
    {
        if (CodIsUpdatingDatabase || !CodIsReady) return;
        codIsUpdating = true;
        OnPropertyChanged(nameof(CodIsUpdatingDatabase));
        OnPropertyChanged(nameof(CodCanUpdateDatabase));
        CodUpdateStatus = Text.CodDbUpdateStarting;
        var previousDirectory = codDatasetDirectory;
        try
        {
            var progress = new Progress<string>(stage => CodUpdateStatus = stage);
            var result = await CodUpdater
                .UpdateAsync(preferences.CodWeaponDbDirectory, previousDirectory, progress)
                .ConfigureAwait(true);
            codLatestRevision = new CodWeaponDbRevision(result.CommitSha, result.CommitTimestamp);
            preferences.SaveCodWeaponDatasetDirectory(result.Directory);
            codLoaded = false;
            await LoadCodWeaponDbAsync(result.Directory).ConfigureAwait(true);
            CodUpdateStatus = string.Format(CultureInfo.CurrentCulture, Text.CodDbUpdateComplete,
                result.RecordCount, result.GameCount, result.AddedCount, result.RemovedCount, result.ChangedCount);
            FooterStatus = CodUpdateStatus;
        }
        catch (Exception error)
        {
            CodUpdateStatus = DescribeUpdateFailure(error);
            ShowDialog(Text.CodDbUpdateFailed, CodUpdateStatus, true);
        }
        finally
        {
            codIsUpdating = false;
            OnPropertyChanged(nameof(CodIsUpdatingDatabase));
            OnPropertyChanged(nameof(CodCanUpdateDatabase));
            OnPropertyChanged(nameof(CodHasUpdate));
            OnPropertyChanged(nameof(CodUpdateSummary));
        }
    }

    private string DescribeUpdateFailure(Exception error) =>
        Text.CodDbUpdateFailed + ": " + (error is CodWeaponDbUpdateException ? error.Message : error.GetType().Name + ": " + error.Message);

    /// <summary>Test seam: swaps the wiki transport so a smoke can run without the network.</summary>
    internal void OverrideCodIconService(CodWikiIconService? service)
    {
        if (!ReferenceEquals(codIconService, service)) codIconService?.Dispose();
        codIconService = service;
        ShowCodCachedIcon();
    }

    /// <summary>Test seam: swaps the database transport so a smoke can run without the network.</summary>
    internal void OverrideCodUpdater(CodWeaponDbUpdater? updater)
    {
        if (!ReferenceEquals(codUpdater, updater)) codUpdater?.Dispose();
        codUpdater = updater;
    }

    /// <summary>Waits for the lazy dataset load; used by render and UI verification.</summary>
    internal async Task<bool> WaitForCodWeaponDbAsync(TimeSpan timeout)
    {
        await EnsureCodWeaponDbLoadedAsync().ConfigureAwait(true);
        var deadline = DateTime.UtcNow + timeout;
        while (!CodIsReady && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25).ConfigureAwait(true);
            if (codCatalog is null && codLoaded) return false;
        }
        return CodIsReady;
    }

    /// <summary>Loads the dataset the first time the page is shown.</summary>
    internal async Task EnsureCodWeaponDbLoadedAsync()
    {
        if (codLoaded || codIsLoading) return;
        await LoadCodWeaponDbAsync(preferences.Snapshot().CodWeaponDatasetDirectory).ConfigureAwait(true);
    }

    private async Task LoadCodWeaponDbAsync(string? externalDirectory)
    {
        if (codIsLoading) return;
        CodIsLoading = true;
        CodStatus = Text.CodDbLoading;
        try
        {
            var useExternal = !string.IsNullOrWhiteSpace(externalDirectory) && CodWeaponCatalog.IsDatasetDirectory(externalDirectory);
            var directory = useExternal ? externalDirectory! : CodWeaponCatalog.BuiltInDirectory;
            var catalog = await CodWeaponCatalog.LoadAsync(directory).ConfigureAwait(true);
            codDatasetDirectory = directory;
            codDatasetIsBuiltIn = !useExternal;
            codLoaded = true;
            ApplyCodCatalog(catalog);
        }
        catch (Exception error)
        {
            codLoaded = true;
            codCatalog = null;
            codDatasetIsBuiltIn = true;
            codDatasetDirectory = CodWeaponCatalog.BuiltInDirectory;
            codResults.Clear();
            CodStatus = Text.CodDbLoadFailed + ": " + error.Message;
            RaiseCodReadiness();
        }
        finally
        {
            CodIsLoading = false;
        }
    }

    private void ApplyCodCatalog(CodWeaponCatalog catalog)
    {
        codCatalog = catalog;
        foreach (var weapon in catalog.Weapons) weapon.ClassLabel = LocalizeCodClass(weapon.Class);
        RebuildCodFilterOptions();
        RefreshCodResults();
        OnPropertyChanged(nameof(CodDatasetLabel));
        OnPropertyChanged(nameof(CodDatasetIsBuiltIn));
        OnPropertyChanged(nameof(CodDatasetDirectory));
        OnPropertyChanged(nameof(CodHasExternalDatasetDirectory));
        OnPropertyChanged(nameof(CodDatasetDirectoryHelp));
        OnPropertyChanged(nameof(CodCanRestoreDataset));
        OnPropertyChanged(nameof(CodProvenanceSummary));
        RaiseCodReadiness();
        OnPropertyChanged(nameof(CodResultSummary));
        CodStatus = CodResultSummary;
    }

    private void RebuildCodFilterOptions()
    {
        var game = codGameFilter?.Value ?? string.Empty;
        var weaponClass = codClassFilter?.Value ?? string.Empty;

        codSuppressFilterRefresh = true;
        try
        {
            codGameOptions.Clear();
            codGameOptions.Add(new CodFilterOption(Text.CodDbAllGames, string.Empty));
            foreach (var item in codCatalog?.Games ?? []) codGameOptions.Add(new CodFilterOption(item, item));

            codClassOptions.Clear();
            codClassOptions.Add(new CodFilterOption(Text.CodDbAllClasses, string.Empty));
            foreach (var item in codCatalog?.Classes ?? [])
                codClassOptions.Add(new CodFilterOption(LocalizeCodClass(item), item));

            codGameFilter = FindCodOption(codGameOptions, game);
            codClassFilter = FindCodOption(codClassOptions, weaponClass);
        }
        finally
        {
            codSuppressFilterRefresh = false;
        }

        // The freshly created entries differ from the previous ones, so the pickers
        // always receive the assignment and re-display the active filter.
        OnPropertyChanged(nameof(CodGameFilter));
        OnPropertyChanged(nameof(CodClassFilter));
    }

    private static CodFilterOption FindCodOption(IReadOnlyList<CodFilterOption> options, string value)
    {
        foreach (var option in options)
            if (string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)) return option;
        return options[0];
    }

    private string LocalizeCodClass(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? Text.CodDbUnknownClass : value;

    private void RefreshCodResults()
    {
        var previous = codSelectedWeapon;
        codResults.Clear();
        if (codCatalog is not { } catalog)
        {
            OnPropertyChanged(nameof(CodResultSummary));
            return;
        }

        var game = codGameFilter?.Value ?? string.Empty;
        var weaponClass = codClassFilter?.Value ?? string.Empty;
        var matches = catalog.Query(codSearch, new CodWeaponFilter(game, weaponClass, codPrefix));
        foreach (var weapon in matches) codResults.Add(weapon);

        CodSelectedWeapon = previous is not null && matches.Contains(previous) ? previous : matches.FirstOrDefault();
        OnPropertyChanged(nameof(CodResultSummary));
        OnPropertyChanged(nameof(CodHasResults));
    }

    /// <summary>Paints whatever is already cached without touching the network.</summary>
    private void ShowCodCachedIcon()
    {
        var weapon = codSelectedWeapon;
        if (weapon is null || codIsFetchingIcon)
        {
            if (weapon is null) SetCodIcon(null, null);
            return;
        }
        var cached = codSelectedBlueprint is { } blueprint
            ? CodIconService.TryGetCachedBlueprintIcon(blueprint)
            : CodIconService.TryGetCachedWeaponIcon(weapon);
        SetCodIcon(cached, cached);
    }

    public async Task FetchCodIconAsync(bool forceRefresh)
    {
        if (codIsFetchingIcon || codSelectedWeapon is not { } weapon) return;
        var blueprint = codSelectedBlueprint;
        var version = ++codIconRequestVersion;
        CodIsFetchingIcon = true;
        CodIconStatus = Text.CodDbFetching;
        try
        {
            var service = CodIconService;
            var outcome = blueprint is not null
                ? await service.GetBlueprintIconAsync(weapon, blueprint).ConfigureAwait(true)
                : await service.GetWeaponIconAsync(weapon, forceRefresh).ConfigureAwait(true);
            if (version != codIconRequestVersion) return;
            if (!outcome.Succeeded)
            {
                CodIconStatus = Text.CodDbIconUnavailable;
                ShowDialog(Text.CodDbIconFailed, DescribeCodIconFailure(outcome), true);
                return;
            }
            SetCodIcon(outcome.Icon, outcome.Icon);
        }
        catch (Exception error)
        {
            if (version == codIconRequestVersion) ShowDialog(Text.CodDbIconFailed, error.Message, true);
        }
        finally
        {
            if (version == codIconRequestVersion) CodIsFetchingIcon = false;
        }
    }

    private string DescribeCodIconFailure(CodWikiIconOutcome outcome)
    {
        var attempts = outcome.Attempts.Count == 0
            ? string.Empty
            : Environment.NewLine + string.Join(", ", outcome.Attempts);
        return Text.CodDbIconFailed + ": " + outcome.Error + attempts;
    }

    private void SetCodIcon(CodWikiIcon? icon, CodWikiIcon? attribution)
    {
        var previous = codIconBitmap;
        Bitmap? bitmap = null;
        if (icon is { Bytes.Length: > 0 })
        {
            try
            {
                bitmap = new Bitmap(new MemoryStream(icon.Bytes));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                bitmap = null;
            }
        }
        codIcon = bitmap is null ? null : attribution;
        codIconBitmap = bitmap;
        previous?.Dispose();
        OnPropertyChanged(nameof(CodIconBitmap));
        OnPropertyChanged(nameof(CodHasIcon));
        OnPropertyChanged(nameof(CodIconAttribution));
        OnPropertyChanged(nameof(CodCanSaveIcon));
        OnPropertyChanged(nameof(CodCompareImageWidth));
        OnPropertyChanged(nameof(CodCompareImageHeight));
        CodIconStatus = bitmap is null ? (codSelectedWeapon is null ? string.Empty : Text.CodDbIconUnavailable) : string.Empty;
    }

    /// <summary>Saves the currently shown icon under its wiki file name and real container format.</summary>
    public async Task SaveCodIconAsync()
    {
        if (codIcon is not { Bytes.Length: > 0 } icon) return;
        var stem = string.IsNullOrWhiteSpace(icon.FileName)
            ? "cod-weapon-icon"
            : Path.GetFileNameWithoutExtension(icon.FileName);
        var name = stem + icon.Extension;
        if (picker is not IWorkspaceImageExporter exporter)
        {
            ShowDialog(Text.CodDbSaveImageFailed, Text.CodDbIconUnavailable, true);
            return;
        }
        try
        {
            var path = await exporter.SaveImageAsync(name, icon.Bytes).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path)) return;
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.CodDbImageSaved, path);
        }
        catch (Exception error)
        {
            ShowDialog(Text.CodDbSaveImageFailed, error.Message, true);
        }
    }

    public async Task ChooseCodDatasetAsync()
    {
        var path = await picker.PickFolderAsync(codDatasetDirectory).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!CodWeaponCatalog.IsDatasetDirectory(path))
        {
            ShowDialog(Text.CodDbDataset, Text.CodDbDatasetInvalid, true);
            return;
        }
        preferences.SaveCodWeaponDatasetDirectory(path);
        codLoaded = false;
        await LoadCodWeaponDbAsync(path).ConfigureAwait(true);
    }

    public async Task RestoreBuiltInCodDatasetAsync()
    {
        preferences.SaveCodWeaponDatasetDirectory(null);
        codLoaded = false;
        await LoadCodWeaponDbAsync(null).ConfigureAwait(true);
        FooterStatus = Text.CodDbDatasetRestored;
    }

    public async Task OpenCodSourceAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        await OpenExternalUriAsync(uri).ConfigureAwait(true);
    }

    public void ReportCodenameCopied(string codename) =>
        FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.CodDbCopied, codename);

    public string CodCompareTitleText => string.Format(CultureInfo.CurrentCulture, Text.CodDbCompareTitle,
        string.IsNullOrWhiteSpace(CodReferenceTitle) ? Text.CodDbNavigation : CodReferenceTitle);

    private bool codCompareTopmost = true;
    private double codCompareScale = 1;

    public bool CodCompareTopmost
    {
        get => codCompareTopmost;
        set { if (codCompareTopmost == value) return; codCompareTopmost = value; OnPropertyChanged(); }
    }

    /// <summary>Independent zoom for the comparison window so it can be matched to the 3D view.</summary>
    public double CodCompareScale
    {
        get => codCompareScale;
        set
        {
            var clamped = Math.Clamp(value, 0.4, 3);
            if (Math.Abs(codCompareScale - clamped) < 0.001) return;
            codCompareScale = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CodCompareImageWidth));
            OnPropertyChanged(nameof(CodCompareImageHeight));
        }
    }

    public double CodCompareImageWidth => IconBaseWidth * CodCompareScale;
    public double CodCompareImageHeight => IconBaseHeight * CodCompareScale;
    private double IconBaseWidth => codIconBitmap is { } bitmap && bitmap.Size.Width > 0 ? bitmap.Size.Width : 320;
    private double IconBaseHeight => codIconBitmap is { } bitmap && bitmap.Size.Height > 0 ? bitmap.Size.Height : 180;

    /// <summary>Re-labels data-driven text after a language switch.</summary>
    private void RefreshCodLocalizedOptions()
    {
        if (codCatalog is { } catalog)
            foreach (var weapon in catalog.Weapons) weapon.ClassLabel = LocalizeCodClass(weapon.Class);
        RebuildCodFilterOptions();
        RefreshCodResults();
        OnPropertyChanged(nameof(CodIconAttribution));
        OnPropertyChanged(nameof(CodCodenameDisplay));
        OnPropertyChanged(nameof(CodReferenceSubtitle));
    }
}
