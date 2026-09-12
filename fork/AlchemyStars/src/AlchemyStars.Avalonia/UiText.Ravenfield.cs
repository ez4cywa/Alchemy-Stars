namespace AlchemyStars.Avalonia;

public sealed partial class UiText
{
    public string RfTitle => L("Ravenfield 手臂适配", "Ravenfield hands adaptation");
    public string RfHelp => L("选择 RF 手臂与参考动画，使用已求值的动画层、手部姿势与 IK。生成 .blend、FBX、预览图和报告。", "Choose RF hands and a reference clip, using evaluated layers, hand poses and IK. Generates .blend, FBX, preview and report.");
    public string RfMode => L("输出模式", "Output mode");
    public string RfContactFit => L("自动对齐主掌面", "Align central palm surfaces");
    public string RfLocalHandFit => L("局部虎口拟合（修改导出手型）", "Local web-space fit (reshape exported hands)");
    public string RfLocalHandFitHelp => L("需开启主掌面对齐。仅修改导出副本的局部虎口网格，新增镜像辅助骨并重分配局部权重（总权重保持）；参考帧校准后用于全部动画。原指根、骨头绑定与长度、源素材及枪位置不变。不保证全程贴合或无穿模。", "Requires palm alignment. Reshapes the exported web-space mesh using mirrored helper bones and redistributed local weights, preserving total weight. Reference-frame calibration applies to all clips. Original finger roots, bone binds/lengths, source assets and weapon placement stay unchanged. Full-sequence fit and collision-free results are not guaranteed.");
    public string RfContactFitHelp => L("比较源手臂与 RF 手臂的稳定掌面点进行对齐，仍可使用手动微调。不保证手指贴合或无穿模；误差超过 5 mm 会在报告中提示。", "Aligns stable palm points on the source and RF hands; manual adjustments remain available. Finger contact and collision-free results are not guaranteed. Errors over 5 mm are reported.");
    public string RfPose => L("静态姿势", "Static pose");
    public string RfAnimation => L("完整动画", "Full animation");
    public string RfLibrary => L("工程全部动画（同一文件）", "All project animations (one file)");
    public string RfLibraryHelp => L("工程全部动画写入同一 FBX，按命名帧区间组织，报告供 Unity 切片；.blend 保留各片段 Action。文件名以 _rf_library 结尾。参考动画与帧仅用于统一视角校准和预览，各片段保留完整的 30 FPS 动画。", "Writes all project animations into one FBX with named frame ranges; use the report to split Unity clips. The .blend keeps each clip's Action. File names end in _rf_library. The reference clip and frame only set shared-view calibration and preview; each clip keeps its full 30 FPS animation.");
    public string RfPoseHelp => L("将参考帧输出为静态姿势（两帧相同关键帧），文件名以 _rf_idle 结尾。", "Exports the reference frame as a static pose (two identical keyed frames). File names end in _rf_idle.");
    public string RfAnimationHelp => L("输出整个已求值的 30 FPS 动画，文件名以 _rf_anim 结尾。参考帧仅用于固定视角校准和预览，不裁剪动画。", "Exports the entire evaluated 30 FPS animation. File names end in _rf_anim. The reference frame only sets fixed-view calibration and preview; it does not trim the animation.");
    public string RfSource => L("RF 手臂（.blend / .unitypackage）", "RF hands (.blend / .unitypackage)");
    public string RfBrowse => L("选择 RF 文件", "Choose RF file");
    public string RfIdle => L("参考动画（随工程保存）", "Reference animation (saved with project)");
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
    public string RfAdapt => L("生成 RF 结果", "Generate RF output");
    public string RfWorking => L("正在适配 RF 手臂并生成预览…", "Adapting RF hands and rendering preview…");
    public string RfComplete => L("RF 手臂适配完成", "RF hands adaptation complete");
    public string RfPalmFitReview => L("文件已生成，握持贴合需检查", "Files generated; grip fit needs review");
    public string RfOpenBlend => L("打开结果 .blend", "Open result .blend");
    public string RfOpenPreview => L("打开预览图", "Open preview image");
    public string RfOpenFailed => L("结果不存在或无法打开。", "The result is missing or could not be opened.");
}
