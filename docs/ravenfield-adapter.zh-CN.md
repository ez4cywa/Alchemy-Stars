# Ravenfield 手臂 idle 适配

`1.3.0-rf.1` 是基于 `1.3.0-preview.27` 的独立实验分支 `EZ4/ravenfield-arms-adapter`。普通 CAST/FBX/SMD/SEAnim 导出保持原流程；RF 适配使用独立入口和米制 FBX 预设。

## 当前范围

使用当前 COD 工程的一帧 idle 姿势，把 Ravenfield 原版手臂对齐到 COD 的握持位置，保留 RF 原始蒙皮及 COD 武器骨架，输出一个统一骨架。生成的是**静态校准姿势，两帧相同关键帧**，不是整段 idle 或换弹动画的重定向。

支持原版 RFTools 的 `Hands no IK for custom models.blend` 和 `Hands.blend`。选择 `.unitypackage` 时优先读取前者，仅在缺失时使用后者；不会导入整个 Unity 工程，也不运行包内代码。自定义 `.blend` 必须保留相同的手臂骨架结构。

COD 参考骨架目前要求 `j_shoulder_le/ri`、`j_elbow_le/ri`、`j_wrist_le/ri`，以及 `j_index_*_1/2/3`、`j_pinky_*_1/2/3`、`j_thumb_*_1/2/3`，并含 `tag_camera` 或 `tag_view`。缺少时给出具体错误，不猜测骨骼映射。

## 使用

1. 打开包含 COD 手臂、武器和 idle 动画的工程；确认普通导出的姿势正确。COD 手臂只提供姿势参考，最终不输出它的网格。
2. 在“设置”顶部找到“Ravenfield 手臂适配 · idle”，选择 RF `.blend` 或 `.unitypackage`。
3. 选择参考 idle 和参考帧。首次进入设置时，只有一个名称含独立 `idle` 字段的候选会自动选中；多个候选需手动选择。RF 参考动画随工程独立保存，普通动画页的选择不影响它。帧按程序处理后的 **30 FPS** 时间线从 0 开始计数；已有动画层、手部姿势和 IK 一并参与计算。
4. 确认 COD 输入数值单位，必要时调整 RF 手臂整体比例。
5. 点击“适配选中 idle”。结果使用当前动画的输出目录（包括统一输出目录设置），文件名追加 `_rf_idle`。
6. 打开预览或 `.blend` 检查握持；需要时调整高级参数并重新适配。保存工程可保留全部 RF 参数。

RF 适配需要本机 Blender。沿用程序现有 Blender 查找规则；未安装在常规目录时，可在启动程序前设置 `ALCHEMY_STARS_BLENDER` 为 `blender.exe` 的完整路径。无需安装插件，随程序提供 CAST 导入脚本。

## 单位与微调

**场景的显示单位不等于文件的原始数值单位。** 已检查的 COD 手臂上臂骨长约 29.28 个数值单位，人体参考高度约 163.37；按厘米解释分别是 0.2928 m 和 1.6337 m，与 RF 的米级手臂相符。因此该分支默认 COD 数值为 `cm`，RF 固定为 `m`。

| COD 数值单位 | 转换到米 |
|---|---|
| cm（默认） | × 0.01 |
| ft | × 0.3048 |
| m | × 1 |

转换只执行一次，同时覆盖模型、骨骼及已求值的动画位置。程序检查参考上臂尺寸，明显不合理时要求核对单位，不通过自动拉大 RF 手臂来隐藏差异。旋转和蒙皮权重不受单位换算影响。

“未标记武器坐标轴”默认沿用 COD 手臂的坐标轴，也可明确选择 X/Y/Z-up。此项仅用于缺少有效坐标轴元数据的武器及附件；已有明确标记的文件按自身标记处理。当前 Hawk 武器没有轴标记，但弹匣沿原始 -Z，与 Z-up 手臂/idle 一致；按普通导出的缺省 Y-up 解释会使武器侧转 90°。RF 入口只给临时副本补充轴标记，不修改源文件，不改变普通导出的缺省规则。报告的 `sourceAxisAssumptions` 和结果提示会列出采用的解释。

微调在对齐 RF 视角后的 **Z-up 场景**进行：`+X` 向左、`-Y` 向前、`+Z` 向上。它不同于最终 FBX 的 Y-up 坐标。

- 整体比例：0.1–10，显式改变 RF 手臂尺寸，不改变武器尺寸。
- 左右手位置：以 m 为单位，偏移目标手腕。
- 左右手旋转：以度为单位，按场景 XYZ 旋转掌心和手指方向。
- 肘部朝向：±180°，绕肩到手腕的轴调整弯曲平面。
- 手指弯曲：±90°，在映射姿势上追加各节弯曲。

保持 RF 上臂和前臂长度，优先满足握持位置；若手臂不可达，移动肩部并在报告里给出位移，不拉伸骨骼。RF 只有三组手指骨链：拇指、食指、小指。保留各自原有权重及指节长度，不生成虚构的中指/无名指骨骼。不同手型可能需要微调才能消除穿模。

## 输出

- `.blend`：可编辑的统一骨架、原始绑定姿势和烘焙校准姿势；包含检查相机及 `RF First Person` 相机。可用纹理会打包到文件中。
- `.fbx`：米制、`-Z Forward / Y Up`，骨骼主轴 X、次轴 -Y；仅输出选定骨架和网格，关闭叶骨骼，只烘焙当前两帧，简化为 0。
- `.preview.png`：中性色握持检查图，优先取景手臂与主武器。源模型中存放在远处的备用弹匣等仍保留在输出模型中。
- `.report.json`：输入摘要、数值单位与换算系数、骨骼映射、肩部调整、手腕误差、合并前后蒙皮误差和缺失贴图记录。

贴图缺失不阻止绑定，报告会提示；本功能不自动补齐原素材未提供的材质资源。FBX 不是可直接部署的 Ravenfield 武器 mod：武器预制体、事件、音效等仍需在 Unity/RFTools 中配置。

输出先在独立临时目录生成，完整成功后才替换四个目标文件；发布失败时恢复已有结果。源素材和工程不改写。程序默认设置保存在 `%LOCALAPPDATA%/Alchemy Stars RF/`，与原版本分开。

## 开发验证

```powershell
D:/blender/blender.exe --background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/test-ravenfield-adapter.py
python scripts/verify-ravenfield-fbx.py --blender D:/blender/blender.exe --reference path/to/result.blend --fbx path/to/result.fbx
AlchemyStars.Avalonia.exe --rf-smoke project.aprj RFTools.unitypackage output-folder
AlchemyStars.Avalonia.exe --rf-ui-smoke --page settings --render-smoke rf-settings.png --window-size 900x600
```

核心验证覆盖三种数值单位的相同物理结果、双骨求解边界、工程持久化、源文件防覆盖和四文件发布回滚。每次实际适配还检查骨架合并前后的骨骼矩阵和蒙皮顶点。FBX 往返比较经过骨轴重定向后的世界变形，而非错误地要求本地骨骼 roll 完全相同。

本机 Unity 2020.3.49f1c1 的批处理验证被未激活的 Editor 许可证阻止，尚未进入 FBX 导入阶段。`scripts/UnityRavenfieldImportCheck.cs` 提供隔离工程中的后续检查入口，已通过本机 Unity 2020 程序集的独立编译检查，但未在 Editor 执行；不能把 Blender 往返通过视为游戏内验收。
