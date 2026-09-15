namespace AlchemyStars.Avalonia;

/// <summary>
/// Optional capability for pickers that can run a native save dialog. Kept
/// separate from <see cref="IWorkspaceFilePicker"/> so test pickers only add it
/// when they actually exercise image export.
/// </summary>
public interface IWorkspaceImageExporter
{
    /// <summary>Prompts for a destination and writes <paramref name="bytes"/>. Returns the saved path, or null when cancelled.</summary>
    Task<string?> SaveImageAsync(string suggestedFileName, byte[] bytes);
}
