namespace AlchemyStars.Engine;

public enum DualModelMode { Attached = 0, CombinedWeapons = 1 }

/// <summary>References editable source tasks, never cached/exported source files.</summary>
public sealed class WorkspaceDualAnimation : ObservableModel
{
    private DualModelMode mode;
    private string leftBranch = "", rightBranch = "";
    public DualModelMode Mode { get => mode; set { if (SetProperty(ref mode, value)) { RaisePropertyChanged(nameof(ModeIndex)); RaisePropertyChanged(nameof(IsCombinedModel)); } } }
    [System.Text.Json.Serialization.JsonIgnore]
    public int ModeIndex { get => (int)Mode; set { if (value >= 0) Mode = (DualModelMode)value; } }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCombinedModel => Mode == DualModelMode.CombinedWeapons;
    public string LeftWeaponBranch { get => leftBranch; set => SetProperty(ref leftBranch, value?.Trim() ?? ""); }
    public string RightWeaponBranch { get => rightBranch; set => SetProperty(ref rightBranch, value?.Trim() ?? ""); }
    private string name = "dual", left = "", right = "", folder = "";
    private string leftMount = "tag_weapon_left", rightMount = "tag_weapon_right", sourceMount = "tag_weapon";
    private bool exportWeaponModels = true;
    public bool ExportWeaponModels { get => exportWeaponModels; set => SetProperty(ref exportWeaponModels, value); }
    public string Name { get => name; set => SetProperty(ref name, value ?? ""); }
    public string LeftAnimationId { get => left; set => SetProperty(ref left, value ?? ""); }
    public string RightAnimationId { get => right; set => SetProperty(ref right, value ?? ""); }
    public string OutputFolder { get => folder; set => SetProperty(ref folder, PathInput.Normalize(value)); }
    public string LeftMount { get => leftMount; set => SetProperty(ref leftMount, value ?? ""); }
    public string RightMount { get => rightMount; set => SetProperty(ref rightMount, value ?? ""); }
    public string SourceMount { get => sourceMount; set => SetProperty(ref sourceMount, value ?? ""); }
}
