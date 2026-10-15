using System.Text.Json;

namespace AlchemyStars.Engine;

public sealed class ApplicationPreferencesStore
{
    private readonly object sync = new();
    private readonly string settingsPath;
    private AppPreferenceData? data;
    private bool settingsLoaded;
    private PreferenceChanges pendingReadChanges;

    [Flags]
    private enum PreferenceChanges
    {
        None = 0,
        RememberArms = 1,
        ArmsPath = 2,
        AutoUpdate = 4,
        SkippedUpdate = 8,
        UnifiedOutput = 16,
        CodDataset = 32,
        LastDirectories = 64,
        Defaults = 128,
        Language = 256,
        Sidebar = 512,
        Appearance = 1024,
    }

    public ApplicationPreferencesStore(string? settingsPath = null)
    {
        var environmentPath = Environment.GetEnvironmentVariable("ALCHEMY_STARS_SETTINGS_PATH");
        this.settingsPath = Path.GetFullPath(settingsPath
            ?? (string.IsNullOrWhiteSpace(environmentPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alchemy Stars", "settings.json")
                : environmentPath));
    }

    public AppPreferenceData Snapshot()
    {
        lock (sync)
            return Read().Clone();
    }

    internal static string NormalizeThemeStyle(string? style) => style switch
    {
        "neumorphic" or "modern-desktop" => "apple", // Legacy keys inherit the default Linear workbench.
        "classic-apple" or "windows-xp" or "windows-2000" or "custom" => style,
        _ => "apple",
    };

    public string AppearanceDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "Appearance");
    public string UpdatesDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "Updates");
    /// <summary>Cache for reference icons downloaded from the Call of Duty Wiki.</summary>
    public string CodWikiIconDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "CodWikiIcons");
    /// <summary>Where an in-app COD weapon database refresh publishes its dataset.</summary>
    public string CodWeaponDbDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "CodWeaponDb");

    public void SaveRememberArms(bool enabled) => Update(p =>
    {
        p.RememberArms = enabled;
        return PreferenceChanges.RememberArms;
    });
    public void SaveArmsPath(string? path) => Update(p =>
    {
        p.SavedArmsPath = path ?? string.Empty;
        return PreferenceChanges.ArmsPath;
    });
    public void RememberFirstArms(string path)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".cast", StringComparison.OrdinalIgnoreCase)) return;
        Update(p =>
        {
            if (!p.RememberArms || !string.IsNullOrWhiteSpace(p.SavedArmsPath)) return PreferenceChanges.None;
            p.SavedArmsPath = Path.GetFullPath(path);
            return PreferenceChanges.ArmsPath;
        });
    }
    public void SaveAutoUpdate(bool enabled) => Update(p =>
    {
        p.AutoUpdateEnabled = enabled;
        return PreferenceChanges.AutoUpdate;
    });
    public void SaveSkippedUpdate(string version) => Update(p =>
    {
        p.SkippedUpdateVersion = version;
        return PreferenceChanges.SkippedUpdate;
    });
    public void SaveUnifiedOutputDirectory(string? path) => Update(p =>
    {
        p.UnifiedOutputDirectory = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        return PreferenceChanges.UnifiedOutput;
    });

    /// <summary>Remembers an external CODWeaponDB dataset; empty means the bundled snapshot.</summary>
    public void SaveCodWeaponDatasetDirectory(string? path) => Update(p =>
    {
        p.CodWeaponDatasetDirectory = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        return PreferenceChanges.CodDataset;
    });

    public WorkspaceDocument CreateWorkspace()
    {
        var preferences = Snapshot();
        var document = WorkspaceDocument.Create(preferences.DefaultOutputFormat, preferences.DefaultCastAnimationOnly, preferences.DefaultBakeRelevantBonesOnly);
        document.OutputUpAxis = preferences.DefaultOutputUpAxis;
        if (preferences.RememberArms && File.Exists(preferences.SavedArmsPath)
            && string.Equals(Path.GetExtension(preferences.SavedArmsPath), ".cast", StringComparison.OrdinalIgnoreCase))
            document.Parts.Add(new WorkspacePart { FilePath = preferences.SavedArmsPath, Type = ModelPartKind.ViewHands });
        return document;
    }

    public string? GetLastDirectory(string scope)
    {
        lock (sync)
        {
            return Read().LastDirectories.TryGetValue(scope, out var directory) && Directory.Exists(directory)
                ? directory
                : null;
        }
    }

    public void RememberDirectory(string scope, string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;
        var normalized = PathInput.Normalize(selectedPath);
        var directory = Directory.Exists(normalized) ? normalized : Path.GetDirectoryName(normalized);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        Update(preferences =>
        {
            preferences.LastDirectories[scope] = Path.GetFullPath(directory);
            return PreferenceChanges.LastDirectories;
        });
    }

    public void SaveDefaults(string language, WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Update(preferences =>
        {
            preferences.Language = string.IsNullOrWhiteSpace(language) ? "system" : language;
            preferences.DefaultOutputFormat = OutputFormats.Normalize(document.OutputFormat);
            preferences.DefaultOutputUpAxis = document.OutputUpAxis;
            preferences.DefaultCastAnimationOnly = document.CastAnimationOnly;
            preferences.DefaultBakeRelevantBonesOnly = document.BakeRelevantBonesOnly;
            return PreferenceChanges.Defaults | PreferenceChanges.Language;
        });
    }

    public void SaveLanguage(string language) => Update(preferences =>
    {
        preferences.Language = string.IsNullOrWhiteSpace(language) ? "system" : language;
        return PreferenceChanges.Language;
    });

    public void SaveSidebarSections(bool commonExpanded, bool otherExpanded) => Update(preferences =>
    {
        preferences.SidebarCommonExpanded = commonExpanded;
        preferences.SidebarOtherExpanded = otherExpanded;
        return PreferenceChanges.Sidebar;
    });

    public void SaveAppearance(string style, string mode) => Update(preferences =>
    {
        preferences.ThemeStyle = NormalizeThemeStyle(style);
        preferences.ThemeMode = mode is "dark" or "system" ? mode : "light";
        return PreferenceChanges.Appearance;
    });

    private AppPreferenceData Read()
    {
        if (settingsLoaded)
            return data!;
        try
        {
            AppPreferenceData loaded;
            try
            {
                loaded = JsonSerializer.Deserialize(File.ReadAllText(settingsPath), WorkspaceJsonContext.Default.AppPreferenceData)
                    ?? new AppPreferenceData();
            }
            catch (FileNotFoundException) { loaded = new AppPreferenceData(); }
            catch (DirectoryNotFoundException) { loaded = new AppPreferenceData(); }
            loaded.LastDirectories ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (data is not null) MergeReadChanges(loaded, data, pendingReadChanges);
            data = loaded;
            settingsLoaded = true;
            pendingReadChanges = PreferenceChanges.None;
        }
        catch
        {
            // Keep this fallback usable in memory, but retry before any future disk write.
        }
        data ??= new AppPreferenceData();
        data.LastDirectories ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        data.DefaultOutputFormat = OutputFormats.Normalize(data.DefaultOutputFormat);
        data.ThemeStyle = NormalizeThemeStyle(data.ThemeStyle);
        data.ThemeMode = data.ThemeMode is "dark" or "system" ? data.ThemeMode : "light";
        return data;
    }

    private static void MergeReadChanges(AppPreferenceData destination, AppPreferenceData source, PreferenceChanges changes)
    {
        if (changes.HasFlag(PreferenceChanges.RememberArms)) destination.RememberArms = source.RememberArms;
        if (changes.HasFlag(PreferenceChanges.ArmsPath)) destination.SavedArmsPath = source.SavedArmsPath;
        if (changes.HasFlag(PreferenceChanges.AutoUpdate)) destination.AutoUpdateEnabled = source.AutoUpdateEnabled;
        if (changes.HasFlag(PreferenceChanges.SkippedUpdate)) destination.SkippedUpdateVersion = source.SkippedUpdateVersion;
        if (changes.HasFlag(PreferenceChanges.UnifiedOutput)) destination.UnifiedOutputDirectory = source.UnifiedOutputDirectory;
        if (changes.HasFlag(PreferenceChanges.CodDataset)) destination.CodWeaponDatasetDirectory = source.CodWeaponDatasetDirectory;
        if (changes.HasFlag(PreferenceChanges.Language)) destination.Language = source.Language;
        if (changes.HasFlag(PreferenceChanges.Defaults))
        {
            destination.DefaultOutputFormat = source.DefaultOutputFormat;
            destination.DefaultOutputUpAxis = source.DefaultOutputUpAxis;
            destination.DefaultCastAnimationOnly = source.DefaultCastAnimationOnly;
            destination.DefaultBakeRelevantBonesOnly = source.DefaultBakeRelevantBonesOnly;
        }
        if (changes.HasFlag(PreferenceChanges.Sidebar))
        {
            destination.SidebarCommonExpanded = source.SidebarCommonExpanded;
            destination.SidebarOtherExpanded = source.SidebarOtherExpanded;
        }
        if (changes.HasFlag(PreferenceChanges.Appearance))
        {
            destination.ThemeStyle = source.ThemeStyle;
            destination.ThemeMode = source.ThemeMode;
        }
        if (changes.HasFlag(PreferenceChanges.LastDirectories))
            foreach (var (scope, directory) in source.LastDirectories)
                destination.LastDirectories[scope] = directory;
    }

    private void Update(Func<AppPreferenceData, PreferenceChanges> update)
    {
        lock (sync)
        {
            var preferences = Read();
            var changes = update(preferences);
            if (!settingsLoaded)
            {
                // A fixed set of field groups keeps repeated edits bounded while the file is unavailable.
                pendingReadChanges |= changes;
                return;
            }
            try
            {
                var directory = Path.GetDirectoryName(settingsPath)
                    ?? throw new InvalidOperationException("The settings destination has no parent directory.");
                Directory.CreateDirectory(directory);
                var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.settings.tmp");
                try
                {
                    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, WorkspaceJsonContext.Default.AppPreferenceData));
                    File.Move(temporaryPath, settingsPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Preferences are best-effort and must never block project or export workflows.
            }
            catch (UnauthorizedAccessException)
            {
                // Sandboxed or read-only environments can still use the in-memory preferences.
            }
        }
    }
}

public sealed class AppPreferenceData
{
    public bool RememberArms { get; set; } = true;
    public string SavedArmsPath { get; set; } = string.Empty;
    public bool AutoUpdateEnabled { get; set; }
    public string SkippedUpdateVersion { get; set; } = string.Empty;
    public string UnifiedOutputDirectory { get; set; } = string.Empty;
    public string CodWeaponDatasetDirectory { get; set; } = string.Empty;
    public string ThemeStyle { get; set; } = "apple";
    public string ThemeMode { get; set; } = "light";
    public string Language { get; set; } = "system";
    public bool SidebarCommonExpanded { get; set; } = true;
    public bool SidebarOtherExpanded { get; set; } = true;
    public string DefaultOutputFormat { get; set; } = ".cast";
    public string DefaultOutputUpAxis { get; set; } = "source";
    public bool DefaultCastAnimationOnly { get; set; }
    public bool DefaultBakeRelevantBonesOnly { get; set; }
    public Dictionary<string, string> LastDirectories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal AppPreferenceData Clone() => new()
    {
        RememberArms = RememberArms,
        SavedArmsPath = SavedArmsPath,
        AutoUpdateEnabled = AutoUpdateEnabled,
        SkippedUpdateVersion = SkippedUpdateVersion,
        UnifiedOutputDirectory = UnifiedOutputDirectory,
        CodWeaponDatasetDirectory = CodWeaponDatasetDirectory,
        Language = Language,
        SidebarCommonExpanded = SidebarCommonExpanded,
        SidebarOtherExpanded = SidebarOtherExpanded,
        ThemeStyle = ThemeStyle,
        ThemeMode = ThemeMode,
        DefaultOutputFormat = DefaultOutputFormat,
        DefaultOutputUpAxis = DefaultOutputUpAxis,
        DefaultCastAnimationOnly = DefaultCastAnimationOnly,
        DefaultBakeRelevantBonesOnly = DefaultBakeRelevantBonesOnly,
        LastDirectories = new Dictionary<string, string>(LastDirectories, StringComparer.OrdinalIgnoreCase),
    };
}
