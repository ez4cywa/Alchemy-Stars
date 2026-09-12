using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlchemyStars.Engine;

/// <summary>RF calibration is independent from normal animation export settings.</summary>
public sealed class RavenfieldAdaptationOptions : ObservableModel
{
    private string rfSourcePath = "";
    private string referenceAnimationId = "";
    private string mode = "pose";
    private bool contactFit = true;
    private bool localHandFit;
    public bool LocalHandFit { get => localHandFit; set => SetProperty(ref localHandFit, value); }
    [JsonConverter(typeof(RavenfieldContactFitConverter))]
    public bool ContactFit { get => contactFit; set => SetProperty(ref contactFit, value); }
    public string Mode { get => mode; set => SetProperty(ref mode, value ?? "pose"); }
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

// Old or explicitly null settings retain the enabled default; false stays false.
public sealed class RavenfieldContactFitConverter : JsonConverter<bool>
{
    public override bool HandleNull => true;
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null || reader.GetBoolean();
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
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
