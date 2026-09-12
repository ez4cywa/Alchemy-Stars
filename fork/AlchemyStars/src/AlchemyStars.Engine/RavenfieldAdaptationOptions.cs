namespace AlchemyStars.Engine;

/// <summary>RF calibration is independent from normal animation export settings.</summary>
public sealed class RavenfieldAdaptationOptions : ObservableModel
{
    private string rfSourcePath = "";
    private string referenceAnimationId = "";
    public string ReferenceAnimationId { get => referenceAnimationId; set => SetProperty(ref referenceAnimationId, value ?? ""); }
    private string sourceUnit = "cm";
    private string untaggedModelUpAxis = "hands";
    private int idleFrame;
    private double handScale = 1;
    public string RfSourcePath { get => rfSourcePath; set => SetProperty(ref rfSourcePath, PathInput.Normalize(value)); }
    public string SourceUnit { get => sourceUnit; set => SetProperty(ref sourceUnit, value ?? "cm"); }
    public string UntaggedModelUpAxis { get => untaggedModelUpAxis; set => SetProperty(ref untaggedModelUpAxis, value ?? "hands"); }
    public int IdleFrame { get => idleFrame; set => SetProperty(ref idleFrame, value); }
    public double HandScale { get => handScale; set => SetProperty(ref handScale, value); }
    public RavenfieldHandAdjustment Left { get; set; } = new();
    public RavenfieldHandAdjustment Right { get; set; } = new();
}

public sealed class RavenfieldHandAdjustment : ObservableModel
{
    private double positionX, positionY, positionZ, rotationX, rotationY, rotationZ, elbowSwivel, fingerCurl;
    public double PositionX { get => positionX; set => SetProperty(ref positionX, value); }
    public double PositionY { get => positionY; set => SetProperty(ref positionY, value); }
    public double PositionZ { get => positionZ; set => SetProperty(ref positionZ, value); }
    public double RotationX { get => rotationX; set => SetProperty(ref rotationX, value); }
    public double RotationY { get => rotationY; set => SetProperty(ref rotationY, value); }
    public double RotationZ { get => rotationZ; set => SetProperty(ref rotationZ, value); }
    public double ElbowSwivel { get => elbowSwivel; set => SetProperty(ref elbowSwivel, value); }
    public double FingerCurl { get => fingerCurl; set => SetProperty(ref fingerCurl, value); }
}
