using Alchemist.InverseKinematics;
using UiAnimation = Alchemist.UI.Animation;
using UiAnimationLayer = Alchemist.UI.AnimationLayer;
using UiAnimationLayerType = Alchemist.UI.AnimationLayerType;
using UiAnimationConverter = Alchemist.UI.AnimationConverter;
using UiPart = Alchemist.UI.Part;
using UiPartType = Alchemist.UI.PartType;
using UiSkeletonMergePlan = Alchemist.UI.SkeletonMergePlan;

namespace AlchemyStars.Engine;

public sealed class AnimationExportEngine : IAnimationExportEngine
{
    public const string EngineVersion = "1.3.0-preview.15";

    /// <summary>Creates an independent bind skeleton for previewing animation-only CAST data.</summary>
    public static RedFox.Graphics3D.Skeletal.Skeleton CreatePreviewSkeleton(IReadOnlyList<ModelPartSpec> parts, bool legacy) =>
        UiSkeletonMergePlan.Build(parts.Select(ToCompatibilityPart), legacy).Skeleton;

    public EngineCapabilities Capabilities { get; } = new(
        EngineVersion,
        Enum.GetValues<ExportFormat>(),
        SupportsAnimationOnlyCast: true,
        SupportsSelectiveBoneBake: true,
        SupportsNativeAot: true);

    public AnimationExportResult Export(AnimationExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var parts = request.Parts.Select(ToCompatibilityPart).ToArray();
        var mergePlan = UiSkeletonMergePlan.Build(parts, request.Options.MatchOldCallOfDuty);
        var outputs = new List<string>(request.Animations.Count);

        foreach (var job in request.Animations)
        {
            var leftIk = ToIkSettings(request.Options.LeftHandIk, job.LeftIkTargetOverride);
            var rightIk = ToIkSettings(request.Options.RightHandIk, job.RightIkTargetOverride);
            outputs.Add(UiAnimationConverter.Convert(
                mergePlan,
                ToCompatibilityAnimation(job),
                leftIk,
                rightIk,
                request.Options.OutputPrefix,
                request.Options.OutputSuffix,
                ToExtension(request.Options.Format),
                request.Options.CastAnimationOnly,
                request.Options.BakeRelevantBonesOnly,
                request.Options.MatchOldCallOfDuty, job.WeaponFollowMode));
        }

        return new AnimationExportResult(outputs);
    }

    private static void Validate(AnimationExportRequest request)
    {
        if (request.Parts is null || request.Parts.Count == 0)
            throw new ExportValidationException(ExportErrorCode.NoModelParts, "At least one model part is required.", nameof(request.Parts));
        if (request.Animations is null || request.Animations.Count == 0)
            throw new ExportValidationException(ExportErrorCode.NoAnimations, "At least one animation is required.", nameof(request.Animations));

        foreach (var part in request.Parts)
            RequireFile(part.FilePath, "Model part");

        foreach (var job in request.Animations)
        {
            RequireFile(job.SourceFile, "Animation");
            RequireOptionalFile(job.LeftHandPoseFile, "Left-hand pose");
            RequireOptionalFile(job.RightHandPoseFile, "Right-hand pose");
            foreach (var layer in job.Layers ?? [])
                RequireFile(layer.FilePath, "Animation layer");

            if (string.IsNullOrWhiteSpace(job.OutputFolder))
                throw new ExportValidationException(ExportErrorCode.MissingOutputFolder, "An output folder is required.", nameof(job.OutputFolder));
            if (string.IsNullOrWhiteSpace(job.OutputName))
                throw new ExportValidationException(ExportErrorCode.MissingOutputName, "An output name is required.", nameof(job.OutputName));
            if (!float.IsFinite(job.Framerate) || job.Framerate <= 0)
                throw new ExportValidationException(ExportErrorCode.InvalidFramerate, "Framerate must be a finite value greater than zero.", nameof(job.Framerate));

            var outputPath = Path.GetFullPath(Path.Combine(
                job.OutputFolder,
                request.Options.OutputPrefix + job.OutputName + request.Options.OutputSuffix + ToExtension(request.Options.Format)));
            var inputPaths = request.Parts.Select(part => part.FilePath)
                .Append(job.SourceFile)
                .Concat(job.Layers?.Select(layer => layer.FilePath) ?? [])
                .Append(job.LeftHandPoseFile)
                .Append(job.RightHandPoseFile)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath);
            if (inputPaths.Any(path => string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ExportValidationException(
                    ExportErrorCode.OutputWouldOverwriteInput,
                    $"The output would overwrite an input file: {outputPath}",
                    nameof(job.OutputFolder));
            }
        }
    }

    private static void RequireOptionalFile(string path, string label)
    {
        if (!string.IsNullOrWhiteSpace(path))
            RequireFile(path, label);
    }

    private static void RequireFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new ExportValidationException(ExportErrorCode.MissingInputFile, $"{label} file was not found: {path}");
    }

    internal static UiPart ToCompatibilityPart(ModelPartSpec part) => new()
    {
        FilePath = part.FilePath,
        ParentBoneTag = part.ParentBoneTag,
        Type = part.Kind switch
        {
            ModelPartKind.ViewHands => UiPartType.ViewHands,
            ModelPartKind.Weapon => UiPartType.Weapon,
            _ => UiPartType.Attachment,
        },
    };

    internal static UiAnimation ToCompatibilityAnimation(AnimationExportJob job)
    {
        var animation = new UiAnimation
        {
            Name = job.SourceFile,
            OutputName = job.OutputName,
            OutputFolder = job.OutputFolder,
            OutputFramerate = job.Framerate,
            EnableLeftHandIK = job.EnableLeftHandIk,
            EnableRightHandIK = job.EnableRightHandIk,
            LeftHandPoseFile = job.LeftHandPoseFile,
            RightHandPoseFile = job.RightHandPoseFile,
            LeftIKTargetBoneName = job.LeftIkTargetOverride,
            RightIKTargetBoneName = job.RightIkTargetOverride,
        };
        foreach (var layer in job.Layers ?? [])
        {
            animation.Layers.Add(new UiAnimationLayer
            {
                Name = layer.FilePath,
                Offset = layer.FrameOffset,
                Type = layer.Kind switch
                {
                    AnimationLayerKind.Normal => UiAnimationLayerType.Normal,
                    AnimationLayerKind.Gesture => UiAnimationLayerType.Gesture,
                    AnimationLayerKind.GesturePose => UiAnimationLayerType.GesturePose,
                    _ => UiAnimationLayerType.Additive,
                },
            });
        }
        return animation;
    }

    internal static IKSettings ToIkSettings(IkChainSpec chain, string targetOverride) => new(
        chain.StartBone,
        chain.MiddleBone,
        chain.EndBone,
        string.IsNullOrWhiteSpace(targetOverride) ? chain.TargetBone : targetOverride);

    private static string ToExtension(ExportFormat format) => format switch
    {
        ExportFormat.Fbx => ".fbx",
        ExportFormat.Smd => ".smd",
        ExportFormat.Seanim => ".seanim",
        _ => ".cast",
    };
}
