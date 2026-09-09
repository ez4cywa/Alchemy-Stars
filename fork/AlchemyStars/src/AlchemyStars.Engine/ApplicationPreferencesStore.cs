using System.Text.Json;

namespace AlchemyStars.Engine;

public sealed class ApplicationPreferencesStore
{
    private readonly object sync = new();
    private readonly string settingsPath;
    private AppPreferenceData? data;

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
        "neumorphic" or "modern-desktop" => "apple", // Removed styles inherit the current Original theme.
        "classic-apple" or "windows-xp" or "windows-2000" or "custom" => style,
        _ => "apple",
    };

    public string AppearanceDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "Appearance");
    public string UpdatesDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "Updates");

    public void SaveRememberArms(bool enabled) => Update(p => p.RememberArms = enabled);
    public void SaveArmsPath(string? path) => Update(p => p.SavedArmsPath = path ?? string.Empty);
    public void RememberFirstArms(string path)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".cast", StringComparison.OrdinalIgnoreCase)) return;
        Update(p =>
        {
            if (p.RememberArms && string.IsNullOrWhiteSpace(p.SavedArmsPath)) p.SavedArmsPath = Path.GetFullPath(path);
        });
    }
    public void SaveAutoUpdate(bool enabled) => Update(p => p.AutoUpdateEnabled = enabled);
    public void SaveSkippedUpdate(string version) => Update(p => p.SkippedUpdateVersion = version);
    public void SaveUnifiedOutputDirectory(string? path) => Update(p => p.UnifiedOutputDirectory =
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path));

    public WorkspaceDocument CreateWorkspace()
    {
        var preferences = Snapshot();
        var document = WorkspaceDocument.Create(preferences.DefaultOutputFormat, preferences.DefaultCastAnimationOnly, preferences.DefaultBakeRelevantBonesOnly);
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
        Update(preferences => preferences.LastDirectories[scope] = Path.GetFullPath(directory));
    }

    public void SaveDefaults(string language, WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Update(preferences =>
        {
            preferences.Language = string.IsNullOrWhiteSpace(language) ? "system" : language;
            preferences.DefaultOutputFormat = OutputFormats.Normalize(document.OutputFormat);
            preferences.DefaultCastAnimationOnly = document.CastAnimationOnly;
            preferences.DefaultBakeRelevantBonesOnly = document.BakeRelevantBonesOnly;
        });
    }

    public void SaveLanguage(string language) =>
        Update(preferences => preferences.Language = string.IsNullOrWhiteSpace(language) ? "system" : language);

    public void SaveAppearance(string style, string mode) => Update(preferences =>
    {
        preferences.ThemeStyle = NormalizeThemeStyle(style);
        preferences.ThemeMode = mode is "dark" or "system" ? mode : "light";
    });

    private AppPreferenceData Read()
    {
        if (data is not null)
            return data;
        try
        {
            data = File.Exists(settingsPath)
                ? JsonSerializer.Deserialize(File.ReadAllText(settingsPath), WorkspaceJsonContext.Default.AppPreferenceData)
                : null;
        }
        catch
        {
            data = null;
        }
        data ??= new AppPreferenceData();
        data.LastDirectories ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        data.DefaultOutputFormat = OutputFormats.Normalize(data.DefaultOutputFormat);
        data.ThemeStyle = NormalizeThemeStyle(data.ThemeStyle);
        data.ThemeMode = data.ThemeMode is "dark" or "system" ? data.ThemeMode : "light";
        return data;
    }

    private void Update(Action<AppPreferenceData> update)
    {
        lock (sync)
        {
            var preferences = Read();
            update(preferences);
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
    public string ThemeStyle { get; set; } = "apple";
    public string ThemeMode { get; set; } = "light";
    public string Language { get; set; } = "system";
    public string DefaultOutputFormat { get; set; } = ".cast";
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
        Language = Language,
        ThemeStyle = ThemeStyle,
        ThemeMode = ThemeMode,
        DefaultOutputFormat = DefaultOutputFormat,
        DefaultCastAnimationOnly = DefaultCastAnimationOnly,
        DefaultBakeRelevantBonesOnly = DefaultBakeRelevantBonesOnly,
        LastDirectories = new Dictionary<string, string>(LastDirectories, StringComparer.OrdinalIgnoreCase),
    };
}
