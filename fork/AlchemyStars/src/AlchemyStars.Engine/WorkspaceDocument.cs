using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AlchemyStars.Engine;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class WorkspaceDocument : ObservableModel
{
    public int SchemaVersion { get; set; } = 1;
    public ObservableCollection<WorkspaceDualAnimation> DualAnimations { get; set; } = [];
    private bool enableAnimationTrimming;
    private string leftIkStartBoneName = "j_shoulder_le";
    private string leftIkMidBoneName = "j_elbow_le";
    private string leftIkEndBoneName = "j_wrist_le";
    private string leftIkTargetBoneName = "tag_ik_loc_le";
    private string rightIkStartBoneName = "j_shoulder_ri";
    private string rightIkMidBoneName = "j_elbow_ri";
    private string rightIkEndBoneName = "j_wrist_ri";
    private string rightIkTargetBoneName = "tag_ik_loc_ri";
    private string outputPrefix = string.Empty;
    private string outputSuffix = string.Empty;
    private string outputFormat = ".cast";
    private bool castAnimationOnly;
    private bool bakeRelevantBonesOnly;
    private bool matchOldCallOfDuty;

    public bool EnableAnimationTrimming { get => enableAnimationTrimming; set => SetProperty(ref enableAnimationTrimming, value); }
    public string LeftIKStartBoneName { get => leftIkStartBoneName; set => SetProperty(ref leftIkStartBoneName, value ?? string.Empty); }
    public string LeftIKMidBoneName { get => leftIkMidBoneName; set => SetProperty(ref leftIkMidBoneName, value ?? string.Empty); }
    public string LeftIKEndBoneName { get => leftIkEndBoneName; set => SetProperty(ref leftIkEndBoneName, value ?? string.Empty); }
    public string LeftIKTargetBoneName { get => leftIkTargetBoneName; set => SetProperty(ref leftIkTargetBoneName, value ?? string.Empty); }
    public string RightIKStartBoneName { get => rightIkStartBoneName; set => SetProperty(ref rightIkStartBoneName, value ?? string.Empty); }
    public string RightIKMidBoneName { get => rightIkMidBoneName; set => SetProperty(ref rightIkMidBoneName, value ?? string.Empty); }
    public string RightIKEndBoneName { get => rightIkEndBoneName; set => SetProperty(ref rightIkEndBoneName, value ?? string.Empty); }
    public string RightIKTargetBoneName { get => rightIkTargetBoneName; set => SetProperty(ref rightIkTargetBoneName, value ?? string.Empty); }
    public string OutputPrefix { get => outputPrefix; set => SetProperty(ref outputPrefix, value ?? string.Empty); }
    public string OutputSuffix { get => outputSuffix; set => SetProperty(ref outputSuffix, value ?? string.Empty); }
    public string OutputFormat { get => outputFormat; set => SetProperty(ref outputFormat, OutputFormats.Normalize(value)); }
    public bool CastAnimationOnly { get => castAnimationOnly; set => SetProperty(ref castAnimationOnly, value); }
    public bool BakeRelevantBonesOnly { get => bakeRelevantBonesOnly; set => SetProperty(ref bakeRelevantBonesOnly, value); }
    public bool MatchOldCallOfDuty { get => matchOldCallOfDuty; set => SetProperty(ref matchOldCallOfDuty, value); }
    public ObservableCollection<WorkspaceAnimation> Animations { get; set; } = [];
    public ObservableCollection<WorkspacePart> Parts { get; set; } = [];

    public static WorkspaceDocument Create(string? outputFormat = null, bool castAnimationOnly = false, bool bakeRelevantBonesOnly = false) => new()
    {
        OutputFormat = OutputFormats.Normalize(outputFormat),
        CastAnimationOnly = castAnimationOnly,
        BakeRelevantBonesOnly = bakeRelevantBonesOnly,
    };
}

public sealed class WorkspacePart : ObservableModel
{
    private string filePath = string.Empty;
    private string parentBoneTag = string.Empty;
    private ModelPartKind type = ModelPartKind.Attachment;
    private ModelPartClassification? autoClassification;

    public string FilePath
    {
        get => filePath;
        set
        {
            if (SetProperty(ref filePath, PathInput.Normalize(value)))
            {
                AutoClassification = null;
                RaisePropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string ParentBoneTag { get => parentBoneTag; set => SetProperty(ref parentBoneTag, value ?? string.Empty); }
    public ModelPartKind Type { get => type; set => SetProperty(ref type, value); }

    [JsonIgnore]
    public ModelPartClassification? AutoClassification { get => autoClassification; set => SetProperty(ref autoClassification, value); }

    [JsonIgnore]
    public int TypeIndex
    {
        get => (int)Type;
        set => Type = Enum.IsDefined((ModelPartKind)value) ? (ModelPartKind)value : ModelPartKind.Attachment;
    }

    [JsonIgnore]
    public string DisplayName => Path.GetFileNameWithoutExtension(FilePath);
}

public sealed class WorkspaceAnimation : ObservableModel
{
    private int weaponFollowMode;
    public int WeaponFollowMode { get => weaponFollowMode; set { if (value >= 0) SetProperty(ref weaponFollowMode, value); } }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private float outputFramerate = 30;
    private string name = string.Empty;
    private string outputName = string.Empty;
    private string outputFolder = string.Empty;
    private bool enableLeftHandIk = true;
    private bool enableRightHandIk = true;
    private bool useExperimentalFeatures = true;
    private string leftHandPoseFile = string.Empty;
    private string rightHandPoseFile = string.Empty;
    private string leftIkTargetBoneName = string.Empty;
    private string rightIkTargetBoneName = string.Empty;

    public float OutputFramerate { get => outputFramerate; set => SetProperty(ref outputFramerate, value); }
    public string Name
    {
        get => name;
        set
        {
            var normalized = PathInput.Normalize(value);
            var previousStem = Path.GetFileNameWithoutExtension(name);
            if (!SetProperty(ref name, normalized))
                return;
            if (Layers.Count == 0 && (string.IsNullOrWhiteSpace(OutputName) || string.Equals(OutputName, previousStem, StringComparison.OrdinalIgnoreCase)))
                OutputName = Path.GetFileNameWithoutExtension(normalized);
            RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public string OutputName { get => outputName; set { if (SetProperty(ref outputName, value ?? string.Empty)) RaisePropertyChanged(nameof(DisplayName)); } }
    public string OutputFolder { get => outputFolder; set => SetProperty(ref outputFolder, PathInput.Normalize(value)); }
    public bool EnableLeftHandIK { get => enableLeftHandIk; set => SetProperty(ref enableLeftHandIk, value); }
    public bool EnableRightHandIK { get => enableRightHandIk; set => SetProperty(ref enableRightHandIk, value); }
    public bool UseExperimentalFeatures { get => useExperimentalFeatures; set => SetProperty(ref useExperimentalFeatures, value); }
    public string LeftHandPoseFile { get => leftHandPoseFile; set => SetProperty(ref leftHandPoseFile, PathInput.Normalize(value)); }
    public string RightHandPoseFile { get => rightHandPoseFile; set => SetProperty(ref rightHandPoseFile, PathInput.Normalize(value)); }
    public string LeftIKTargetBoneName { get => leftIkTargetBoneName; set => SetProperty(ref leftIkTargetBoneName, value ?? string.Empty); }
    public string RightIKTargetBoneName { get => rightIkTargetBoneName; set => SetProperty(ref rightIkTargetBoneName, value ?? string.Empty); }
    public ObservableCollection<WorkspaceLayer> Layers { get; set; } = [];

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(OutputName)
        ? Path.GetFileNameWithoutExtension(Name)
        : OutputName;
}

public sealed class WorkspaceLayer : ObservableModel
{
    private string name = string.Empty;
    private int? offset;
    private int color;
    private AnimationLayerKind type = AnimationLayerKind.Additive;

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, PathInput.Normalize(value)))
                RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public int? Offset { get => offset; set => SetProperty(ref offset, value); }
    public int Color { get => color; set => SetProperty(ref color, value); }
    public AnimationLayerKind Type { get => type; set => SetProperty(ref type, value); }

    [JsonIgnore]
    public int TypeIndex
    {
        get => (int)Type;
        set => Type = Enum.IsDefined((AnimationLayerKind)value) ? (AnimationLayerKind)value : AnimationLayerKind.Additive;
    }

    [JsonIgnore]
    public string DisplayName => Path.GetFileNameWithoutExtension(Name);
}

public static class OutputFormats
{
    public static IReadOnlyList<string> All { get; } = [".cast", ".fbx", ".smd", ".seanim"];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ".cast";
        var normalized = value.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
            normalized = "." + normalized;
        return All.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : ".cast";
    }

    public static ExportFormat ToExportFormat(string? value) => Normalize(value) switch
    {
        ".fbx" => ExportFormat.Fbx,
        ".smd" => ExportFormat.Smd,
        ".seanim" => ExportFormat.Seanim,
        _ => ExportFormat.Cast,
    };
}

public static class PathInput
{
    public static string Normalize(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
            normalized = normalized[1..^1].Trim();
        return normalized;
    }
}
