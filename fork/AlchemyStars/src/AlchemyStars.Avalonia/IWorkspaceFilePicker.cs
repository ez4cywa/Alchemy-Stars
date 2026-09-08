namespace AlchemyStars.Avalonia;

public enum FilePickerPurpose
{
    Project,
    Animation,
    ModelPart,
    AnimationLayer,
    LeftPose,
    RightPose,
    Preview,
}

public interface IWorkspaceFilePicker
{
    Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple);
    Task<string?> PickProjectDestinationAsync(string? currentPath);
    Task<string?> PickFolderAsync(string? currentPath);
    Task<bool> OpenUriAsync(Uri uri);
}
