namespace AlchemyStars.Engine;

public static class WorkspacePaths
{
    public const float StandardAnimationFramerate = 30f;

    public static string ResolveAnimationOutputFolder(string sourceFile, string? configuredFolder = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder))
            return Path.GetFullPath(configuredFolder);
        if (string.IsNullOrWhiteSpace(sourceFile))
            throw new ArgumentException("An animation source is required to infer the output folder.", nameof(sourceFile));

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFile));
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            throw new InvalidDataException("The animation source has no containing directory.");
        return Path.Combine(sourceDirectory, "output");
    }

    public static bool IsCastAnimationFile(string path) =>
        string.Equals(Path.GetExtension(path), ".cast", StringComparison.OrdinalIgnoreCase);

    public static void RequireCastAnimation(string path)
    {
        if (!IsCastAnimationFile(path))
            throw new InvalidDataException("动画输入仅支持 CAST / Animation input must be CAST: " + path);
    }
}
