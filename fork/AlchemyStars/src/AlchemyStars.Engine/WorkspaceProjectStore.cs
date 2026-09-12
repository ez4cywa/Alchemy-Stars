using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlchemyStars.Engine;

public sealed class WorkspaceProjectStore
{
    public static WorkspaceDocument Snapshot(WorkspaceDocument document) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(document, WorkspaceJsonContext.Default.WorkspaceDocument),
            WorkspaceJsonContext.Default.WorkspaceDocument)!;
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReferenceHandler = ReferenceHandler.Preserve,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
    private static readonly WorkspaceJsonContext ReadContext = new(ReadOptions);

    public WorkspaceDocument Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        var json = File.ReadAllText(fullPath);
        var document = JsonSerializer.Deserialize(json, ReadContext.WorkspaceDocument)
            ?? throw new InvalidDataException("The project file did not contain a workspace document.");
        Normalize(document);
        return document;
    }

    public void Save(WorkspaceDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.SchemaVersion = document.Animations.Any(a => a.WeaponFollowMode != 0) ? 4 : document.DualAnimations.Any(t => t.Mode != DualModelMode.Attached) ? 3 : document.DualAnimations.Count > 0 ? 2 : document.SchemaVersion;
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The project destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, WorkspaceJsonContext.Default.WorkspaceDocument));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public AnimationExportRequest CreateExportRequest(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new AnimationExportRequest(
            document.Parts.Select(part => new ModelPartSpec(part.FilePath, part.Type, part.ParentBoneTag)).ToArray(),
            document.Animations.Select(animation => new AnimationExportJob(
                animation.Name,
                animation.OutputName,
                animation.OutputFolder,
                WorkspacePaths.StandardAnimationFramerate,
                animation.EnableLeftHandIK,
                animation.EnableRightHandIK,
                animation.LeftHandPoseFile,
                animation.RightHandPoseFile,
                animation.LeftIKTargetBoneName,
                animation.RightIKTargetBoneName,
                animation.Layers.Select(layer => new AnimationLayerSpec(layer.Name, layer.Type, layer.Offset)).ToArray(), animation.WeaponFollowMode)).ToArray(),
            new AnimationExportOptions(
                new IkChainSpec(document.LeftIKStartBoneName, document.LeftIKMidBoneName, document.LeftIKEndBoneName, document.LeftIKTargetBoneName),
                new IkChainSpec(document.RightIKStartBoneName, document.RightIKMidBoneName, document.RightIKEndBoneName, document.RightIKTargetBoneName),
                OutputFormats.ToExportFormat(document.OutputFormat),
                document.OutputPrefix,
                document.OutputSuffix,
                document.CastAnimationOnly,
                document.BakeRelevantBonesOnly,
                document.MatchOldCallOfDuty,
                document.OutputUpAxis));
    }

    private static void Normalize(WorkspaceDocument document)
    {
        if (document.SchemaVersion > 4) throw new InvalidDataException("This project requires a newer Alchemy Stars version.");
        document.DualAnimations ??= [];
        document.OutputFormat = OutputFormats.Normalize(document.OutputFormat);
        document.Parts ??= [];
        document.Animations ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var animation in document.Animations)
        {
            if (string.IsNullOrWhiteSpace(animation.Id)) animation.Id = Guid.NewGuid().ToString("N");
            if (!ids.Add(animation.Id)) throw new InvalidDataException("Duplicate animation task identifiers.");
        }
        foreach (var part in document.Parts)
        {
            part.FilePath = part.FilePath;
            part.ParentBoneTag ??= string.Empty;
        }
        foreach (var animation in document.Animations)
        {
            animation.Name = animation.Name;
            animation.OutputName ??= string.Empty;
            animation.OutputFolder = animation.OutputFolder;
            animation.LeftHandPoseFile = animation.LeftHandPoseFile;
            animation.RightHandPoseFile = animation.RightHandPoseFile;
            animation.LeftIKTargetBoneName ??= string.Empty;
            animation.RightIKTargetBoneName ??= string.Empty;
            animation.Layers ??= [];
            foreach (var layer in animation.Layers)
                layer.Name = layer.Name;
        }
    }
}
