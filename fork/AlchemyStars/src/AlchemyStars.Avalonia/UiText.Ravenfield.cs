namespace AlchemyStars.Avalonia;

public sealed partial class UiText
{
    public string RfTitle => L("Ravenfield 手臂适配 · idle", "Ravenfield hands · idle adaptation");
    public string RfHelp => L("选择 RF 手臂与参考动画，保留现有动画层、手部姿势与 IK。输出静态 idle 姿势（两帧相同关键帧），不是整段动画；生成 .blend、FBX、预览图和报告，不改变普通导出。", "Choose RF hands and a reference clip. Current layers, hand poses and IK are evaluated. Outputs a static idle pose (two identical keyed frames), not a full animation: .blend, FBX, preview and report. Normal exports are unchanged.");
    public string RfSource => L("RF 手臂（.blend / .unitypackage）", "RF hands (.blend / .unitypackage)");
    public string RfBrowse => L("选择 RF 文件", "Choose RF file");
    public string RfIdle => L("参考 idle（随工程保存）", "Reference idle (saved with project)");
    public string RfFrame => L("参考帧（30 FPS，从 0 开始）", "Reference frame (30 FPS, zero-based)");
    public string RfUnit => L("COD 输入数值单位（RF 固定为 m）", "COD numeric input unit (RF always m)");
    public string RfUnitHelp => L("默认 cm 根据当前 COD 样本骨骼尺寸推断：100 cm = 1 m。场景显示 ft 不一定代表数值单位；其他素材请核对尺寸后选择。", "Default cm is inferred from the current COD sample's bone dimensions: 100 cm = 1 m. An ft display setting does not necessarily describe numeric units; check other assets before choosing.");
    public string RfAxesHelp => L("微调使用统一场景轴：+X 向左，−Y 向前，+Z 向上。位置以米计，旋转以度计。", "Adjustments use scene axes: +X left, −Y forward, +Z up. Positions are in meters; rotations are in degrees.");
    public string RfUntaggedAxis => L("未标记武器坐标轴", "Untagged weapon up axis");
    public string RfUntaggedAxisHelp => L("hands：沿用 COD 手臂坐标轴。仅适用于未标记的武器/附件；已有轴标记不覆盖，源文件不改写。", "hands: follow the COD hands' up axis. Only untagged weapons/attachments are affected; explicit metadata and source files are unchanged.");
    public string RfScale => L("RF 手臂整体比例", "RF hands overall scale");
    public string RfAdvanced => L("高级微调（Z-up 场景）", "Advanced adjustments (Z-up scene)");
    public string RfLeft => L("左手", "Left hand");
    public string RfRight => L("右手", "Right hand");
    public string RfPosition => L("位置偏移 X / Y / Z（m）", "Position offset X / Y / Z (m)");
    public string RfRotation => L("旋转偏移 X / Y / Z（°）", "Rotation offset X / Y / Z (degrees)");
    public string RfElbow => L("肘部朝向（°）", "Elbow swivel (degrees)");
    public string RfFinger => L("手指弯曲（°）", "Finger curl (degrees)");
    public string RfAdapt => L("适配选中 idle", "Adapt selected idle");
    public string RfWorking => L("正在适配 RF 手臂并生成预览…", "Adapting RF hands and rendering preview…");
    public string RfComplete => L("RF idle 适配完成", "RF idle adaptation complete");
    public string RfOpenBlend => L("打开结果 .blend", "Open result .blend");
    public string RfOpenPreview => L("打开预览图", "Open preview image");
    public string RfOpenFailed => L("结果不存在或无法打开。", "The result is missing or could not be opened.");
}
