namespace AlchemyStars.Engine;

/// <summary>Builds independent export tasks around one unchanged base animation.</summary>
public static class SharedBaseAnimationBatch
{
    public static IReadOnlyList<WorkspaceAnimation> Create(WorkspaceAnimation template,
        IEnumerable<string> overlayPaths, WorkspaceLayer? replacementLayer = null,
        IEnumerable<string>? reservedOutputNames = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(overlayPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.Name);
        var replacementIndex = replacementLayer is null ? -1 : template.Layers.IndexOf(replacementLayer);
        if (replacementLayer is not null && replacementIndex < 0)
            throw new ArgumentException("The replacement layer must belong to the template.", nameof(replacementLayer));

        var paths = overlayPaths.Select(PathInput.Normalize)
            .Where(path => path.Length > 0 && string.Equals(Path.GetExtension(path), ".cast", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var results = new List<WorkspaceAnimation>();
        var usedNames = new HashSet<string>(reservedOutputNames ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var animation = Clone(template);
            if (replacementIndex >= 0)
                animation.Layers[replacementIndex].Name = path;
            else
                animation.Layers.Add(new WorkspaceLayer { Name = path, Type = AnimationLayerKind.Additive });
            // Each result is named after its variable layer, regardless of fixed layers before it.
            var stem = Path.GetFileNameWithoutExtension(path);
            var outputName = stem;
            for (var suffix = 2; !usedNames.Add(outputName); suffix++) outputName = $"{stem}_{suffix}";
            animation.OutputName = outputName;
            results.Add(animation);
        }
        return results;
    }

    private static WorkspaceAnimation Clone(WorkspaceAnimation source)
    {
        var clone = new WorkspaceAnimation
        {
            Name = source.Name,
            OutputFolder = source.OutputFolder,
            OutputFramerate = source.OutputFramerate,
            WeaponFollowMode = source.WeaponFollowMode,
            EnableLeftHandIK = source.EnableLeftHandIK,
            EnableRightHandIK = source.EnableRightHandIK,
            UseExperimentalFeatures = source.UseExperimentalFeatures,
            LeftHandPoseFile = source.LeftHandPoseFile,
            RightHandPoseFile = source.RightHandPoseFile,
            LeftIKTargetBoneName = source.LeftIKTargetBoneName,
            RightIKTargetBoneName = source.RightIKTargetBoneName,
        };
        foreach (var layer in source.Layers)
            clone.Layers.Add(new WorkspaceLayer { Name = layer.Name, Offset = layer.Offset, Color = layer.Color, Type = layer.Type });
        return clone;
    }
}
