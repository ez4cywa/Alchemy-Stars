namespace AlchemyStars.Engine;

public enum ModelPartKind
{
    ViewHands,
    Weapon,
    Attachment,
}

public enum AnimationLayerKind
{
    Normal,
    Additive,
    Gesture,
    GesturePose,
}

public enum ExportFormat
{
    Cast,
    Fbx,
    Smd,
    Seanim,
}

public sealed record ModelPartSpec(
    string FilePath,
    ModelPartKind Kind,
    string ParentBoneTag = "");

public sealed record AnimationLayerSpec(
    string FilePath,
    AnimationLayerKind Kind = AnimationLayerKind.Additive,
    int? FrameOffset = null);

public sealed record IkChainSpec(
    string StartBone,
    string MiddleBone,
    string EndBone,
    string TargetBone);

public sealed record AnimationExportJob(
    string SourceFile,
    string OutputName,
    string OutputFolder,
    float Framerate = 30,
    bool EnableLeftHandIk = true,
    bool EnableRightHandIk = true,
    string LeftHandPoseFile = "",
    string RightHandPoseFile = "",
    string LeftIkTargetOverride = "",
    string RightIkTargetOverride = "",
    IReadOnlyList<AnimationLayerSpec>? Layers = null,
    int WeaponFollowMode = 0);

public sealed record AnimationExportOptions(
    IkChainSpec LeftHandIk,
    IkChainSpec RightHandIk,
    ExportFormat Format = ExportFormat.Cast,
    string OutputPrefix = "",
    string OutputSuffix = "",
    bool CastAnimationOnly = false,
    bool BakeRelevantBonesOnly = false,
    bool MatchOldCallOfDuty = false,
    string OutputUpAxis = "source");

public sealed record AnimationExportRequest(
    IReadOnlyList<ModelPartSpec> Parts,
    IReadOnlyList<AnimationExportJob> Animations,
    AnimationExportOptions Options);

public sealed record AnimationExportResult(IReadOnlyList<string> OutputFiles);

public sealed record EngineCapabilities(
    string Version,
    IReadOnlyList<ExportFormat> OutputFormats,
    bool SupportsAnimationOnlyCast,
    bool SupportsSelectiveBoneBake,
    bool SupportsNativeAot);

public enum ExportErrorCode
{
    NoModelParts,
    NoAnimations,
    MissingInputFile,
    MissingOutputFolder,
    MissingOutputName,
    InvalidFramerate,
    OutputWouldOverwriteInput,
}

public sealed class ExportValidationException : ArgumentException
{
    public ExportErrorCode Code { get; }

    public ExportValidationException(ExportErrorCode code, string message, string? parameterName = null)
        : base(message, parameterName)
    {
        Code = code;
    }
}
