using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AlchemyStars.Avalonia;

public sealed class AvaloniaFilePickerAdapter(
    Window owner,
    ApplicationPreferencesStore preferences,
    Func<Uri, Task<bool>>? externalUriLaunch = null) : IWorkspaceFilePicker
{
    private static readonly FilePickerFileType CastFileType = new("CAST") { Patterns = ["*.cast"] };
    private static readonly FilePickerFileType ProjectFileType = new("Alchemy Stars project") { Patterns = ["*.aprj"] };
    private static readonly FilePickerFileType AllFileType = new("All files") { Patterns = ["*.*"] };

    public async Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple)
    {
        var project = purpose == FilePickerPurpose.Project;
        var scope = project ? "project" : PurposeScope(purpose);
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = project ? "Alchemy Stars | Open project" : "Alchemy Stars | Select CAST files",
            AllowMultiple = allowMultiple,
            SuggestedStartLocation = await ResolveStartLocationAsync(scope),
            FileTypeFilter = project ? [ProjectFileType, AllFileType] : [CastFileType, AllFileType],
        });
        var paths = files.Select(file => file.Path.LocalPath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (paths.Length > 0)
            preferences.RememberDirectory(scope, paths[0]);
        return paths;
    }

    public async Task<string?> PickProjectDestinationAsync(string? currentPath)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Alchemy Stars | Save project",
            SuggestedStartLocation = await ResolveStartLocationAsync("project", currentPath),
            SuggestedFileName = string.IsNullOrWhiteSpace(currentPath) ? "Untitled.aprj" : Path.GetFileName(currentPath),
            DefaultExtension = "aprj",
            FileTypeChoices = [ProjectFileType],
            ShowOverwritePrompt = true,
        });
        var path = file?.Path.LocalPath;
        preferences.RememberDirectory("project", path);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public async Task<string?> PickFolderAsync(string? currentPath)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Alchemy Stars | Select output folder",
            AllowMultiple = false,
            SuggestedStartLocation = await ResolveStartLocationAsync("output", currentPath),
        });
        var path = folders.FirstOrDefault()?.Path.LocalPath;
        preferences.RememberDirectory("output", path);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public Task<bool> OpenUriAsync(Uri uri) => externalUriLaunch is not null
        ? externalUriLaunch(uri)
        : ExternalUriLauncher.OpenAsync(uri, owner.Launcher.LaunchUriAsync);

    private async Task<IStorageFolder?> ResolveStartLocationAsync(string scope, string? currentPath = null)
    {
        var candidate = Directory.Exists(currentPath)
            ? currentPath
            : !string.IsNullOrWhiteSpace(currentPath) ? Path.GetDirectoryName(currentPath) : null;
        candidate ??= preferences.GetLastDirectory(scope);
        return string.IsNullOrWhiteSpace(candidate)
            ? null
            : await owner.StorageProvider.TryGetFolderFromPathAsync(candidate);
    }

    private static string PurposeScope(FilePickerPurpose purpose) => purpose switch
    {
        FilePickerPurpose.ModelPart => "part",
        FilePickerPurpose.Preview => "preview",
        FilePickerPurpose.AnimationLayer => "layer",
        FilePickerPurpose.LeftPose or FilePickerPurpose.RightPose => "pose",
        _ => "animation",
    };
}
