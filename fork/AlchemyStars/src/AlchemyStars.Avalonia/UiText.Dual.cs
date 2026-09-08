namespace AlchemyStars.Avalonia;

public sealed partial class UiText
{
    public string WeaponFollow => L("武器跟随", "Weapon follow");
    public string[] WeaponFollowModes => [L("原始动画挂点", "Original animation mount"), L("左手（保留握持偏移）", "Left hand (preserve grip)"), L("右手（保留握持偏移）", "Right hand (preserve grip)")];
    public string WeaponFollowHelp => L("以基础动画第 0 帧、手部姿势及原有 IK 记录握持偏移，跟随叠加后的手腕；该侧 IK 自动跳过。武器内部动作保留。", "Capture grip from base frame 0, hand poses and original IK, then follow the layered wrist. IK on that side is skipped; internal weapon animation is preserved.");
    public string DualMode => L("模型处理模式", "Model processing mode");
    public string[] DualModes => [L("挂点模式：复用单个武器", "Attached: duplicate one weapon"), L("模型已包含左右武器", "Model contains both weapons")];
    public string DualBranchLeft => L("左武器分支骨骼（留空自动识别）", "Left weapon branch (blank: auto)");
    public string DualBranchRight => L("右武器分支骨骼（留空自动识别）", "Right weapon branch (blank: auto)");
    public string DualCombinedHelp => L("按骨骼分支和蒙皮权重拆分左右几何，再分别合成。无法自动识别时请指定分支；跨侧蒙皮或连接面需先在建模软件中修正。", "Split geometry by bone branches and skin weights before composing. Specify branches if detection is ambiguous. Cross-side weights/faces require correction in your modeling tool.");
    public string DualTasks => L("任务", "Tasks");
    public string DualSources => L("动画来源", "Animation sources");
    public string DualMounts => L("武器挂点", "Weapon mounts");
    public string DualBatch => L("批量操作", "Batch actions");
    public string DualAnimations => L("双持合并", "Dual merge");
    public string DualAdd => L("新建双持任务", "New dual task");
    public string DualPair => L("配对已有动画", "Pair source tasks");
    public string DualLeft => L("左侧动画任务", "Left animation task");
    public string DualRight => L("右侧动画任务", "Right animation task");
    public string DualEditLeft => L("编辑左侧动画", "Edit left source");
    public string DualEditRight => L("编辑右侧动画", "Edit right source");
    public string DualName => L("双持输出名称", "Dual output name");
    public string DualLeftMount => L("左武器挂点", "Left weapon mount");
    public string DualRightMount => L("右武器挂点", "Right weapon mount");
    public string DualSourceMount => L("源动作武器挂点", "Source weapon mount");
    public string DualHelp => L("选择一个手臂和一个武器模型，并按模型内容选择处理模式。左右任务的姿势、叠加层和 IK 会先独立处理，再组装左右武器。", "Choose one hands model and one weapon model, then select the matching model mode. Source poses, layers and IK are processed before assembling the two weapons.");
    public string DualTimingHelp => L("左右任务须使用相同帧率和处理后帧数。公共骨骼使用右侧任务，双持始终全骨骼烘焙。", "Source frame rates and processed durations must match. Shared bones use the right task; dual output always bakes all bones.");
    public string DualPairHelp => L("在动画栏目导入左右文件，再按共同前缀和 _l_ / _r_ 动作名配对。", "Import sources in Animations, then pair matching prefixes and _l_ / _r_ action names.");
    public string DualPairResult => L("新建 {0} 个双持任务；{1} 组缺少一侧动画。", "Created {0} dual tasks; {1} groups are missing one side.");
    public string DualAmbiguousPairs => L("存在重复的左右来源，请删除重复动画任务后重试。", "Duplicate source tasks make pairing ambiguous. Remove duplicates first.");
    public string DualDuplicateOutputs => L("双持任务的输出路径重复，请修改名称或目录。", "Dual tasks have duplicate output paths. Change their names or folders.");
    public string DualUnmapped => L("未绑定到所选模型的源曲线", "Source targets absent from the selected models");
    public string DualStale => L("配置已变化，请重新生成双持预览。", "Configuration changed. Rebuild the dual preview.");
    public string DualAllFolder => L("为全部双持任务选择目录", "Set folder for all dual tasks");
    public string DualExportAll => L("导出全部双持任务", "Export all dual tasks");
    public string DualExportSelected => L("导出当前双持", "Export selected dual");
    public string DualExportModels => L("导出武器模型", "Export weapon models");
    public string DualExportModelsHelp => L("额外输出 _model.cast，包含手臂、左右武器全部网格和统一骨架。不受动画格式或“仅动画 CAST”影响。关闭只停止生成配套模型文件。", "Also writes _model.cast with hands, both weapons and one skeleton, regardless of animation format or animation-only CAST. Disabling stops the companion file only.");
}
