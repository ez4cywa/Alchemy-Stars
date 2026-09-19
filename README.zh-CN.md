[English](README.md) | **简体中文**

# Alchemy Stars（炼金之星）

**面向第一人称武器动画与 CAST 模型处理的 Windows 桌面工具。**

支持动画层混合、手部 IK、模型部件组装，以及 Maya / Blender 资产导出。Avalonia 预览版还提供多组合并、弹匣填弹、独立模型预览与 COD 武器库。

## 选择版本

| 渠道 | 下载 | 源码分支 | 解压后运行 |
| --- | --- | --- | --- |
| 稳定版 · 1.1.9 · WPF | [下载稳定版](https://github.com/ez4cywa/Alchemy-Stars/releases/tag/v1.1.9) | [`main`](https://github.com/ez4cywa/Alchemy-Stars/tree/main) | `Alchemy Stars.exe` |
| 预览版 · 1.3.0-preview.31 · Avalonia / Native AOT | [下载预览版](https://github.com/ez4cywa/Alchemy-Stars/releases/tag/v1.3.0-preview.31) | [`codex/avalonia-aot`](https://github.com/ez4cywa/Alchemy-Stars/tree/codex/avalonia-aot) | `AlchemyStars.Avalonia.exe` |

预览版为 Windows x64 自包含程序，无需安装 .NET 或 Rust；请完整解压并保留原生 DLL 和 `Converters` 目录。FBX 转换仍需本机安装 Blender 或 Maya。预览版新增功能**不包含在稳定版程序及 `main` 源码中**。

## 预览版工作区

| 工作区 | 功能 |
| --- | --- |
| 动画混合 | 动画层、手部姿势、IK、可调输出帧率，以及独立绑定模型导出 |
| 模型部件 | 识别手臂、武器与附件，保留骨骼关系和蒙皮权重 |
| 双持合并 | 组合左右动画任务，导出配套双持武器模型 |
| 模型合并 · Ctrl+7 | 整合 ModelMergerGUI 2.2.2 核心；每组 2–15 部件、自动/手动根模型、最多两项并行、进度、取消与覆盖确认 |
| 弹匣填弹 | 识别弹匣和槽位，按需选择额外槽位及备用弹匣复制；跳过已占用槽位，另存新 CAST |
| 独立模型预览 | 多个只读窗口、旋转/缩放/网格、32 位索引、最多 250,000 个显示三角面及软件深度缓冲 |
| COD 武器库 | 离线查询武器名、代号和蓝图，在线更新数据库，按需获取 Wiki 对照图标 |

模型合并模块支持中、英、法、俄、西五种语言，主界面支持中英双语。预览抽样不改变导出几何；软件不修改已有 Unity 工程，需按文档通过 CAST/FBX 导出流程使用资产，不能将 CAST 视为 Unity 原生格式。

[预览版快速指南](https://github.com/ez4cywa/Alchemy-Stars/blob/codex/avalonia-aot/docs/avalonia-aot-user-guide.zh-CN.md) · [模型合并指南与功能对照](https://github.com/ez4cywa/Alchemy-Stars/blob/codex/avalonia-aot/docs/model-merger.zh-CN.md) · [更新日志与验证记录](https://github.com/ez4cywa/Alchemy-Stars/blob/codex/avalonia-aot/docs/releases/1.3.0-preview.31.zh-CN.md) · [问题反馈](https://github.com/ez4cywa/Alchemy-Stars/issues)

## 稳定分支说明

下文介绍稳定版 1.1.9 的功能和使用方法。Avalonia 界面与新增功能请查阅上方预览版指南。

Alchemy Stars 是 [Scobalula/Alchemist](https://github.com/Scobalula/Alchemist) 的可用化改进版，面向 Windows、CAST 第一人称武器资产与 Autodesk Maya 2025。项目保留原版 Alchemist 的 WPF 批处理界面和 RedFox 动画管线，并补齐了原仓库尚未完成的模型/动画一体化导出。

主源码位于 `fork/AlchemyStars`，固定使用与原项目同期的 RedFox 提交，避免上游变动破坏构建。先前的独立重写已保存在 Git 分支 `independent-rewrite-v1`，不再是当前实现。

## 相比原版 Alchemist 的改进

Alchemy Stars 保留原版批处理、动画层、IK 与 RedFox 转换管线，在此基础上补齐面向实际 Maya 生产流程的闭环：

| 对比项 | 原版 Alchemist | Alchemy Stars 1.1.9 |
| --- | --- | --- |
| Maya 模型与动画 | 模型部件和动画的组合依赖导入行为，可能出现重复骨架或武器动画丢失 | 导出前按层级和绑定姿势区分同名骨骼，统一映射模型与蒙皮权重；每个文件只有一个已烘焙动画 |
| 输出格式 | 主要为 CAST / SEAnim 管线 | 新增真实 FBX 与原生 SMD，并保留 CAST / SEAnim |
| FBX 工作流 | 未提供 | 自动检测本机 Maya，调用官方 `fbxmaya`，不捆绑大型转换运行环境 |
| 素材导入 | 以原界面操作为主 | 文件浏览器、可编辑/粘贴路径框、定向拖放、列表空白处右键、`Shift+F10`；动画层悬停区域优先路由 |
| 本地化 | 原版界面能力 | 自动检测系统语言，可固定简体中文或 English，并即时刷新 About 等窗口 |
| 使用连续性 | 项目保存绝对路径 | 额外按动画、层、模型、项目与输出类别记忆最近目录 |
| UI 与发布 | 原版设置布局和图标 | 重做功能图标、无截断设置页、受保护的语言/About 区、精简无内置 .NET 运行时发布 |
| 回归验证 | 上游示例 | 原版 MP5 示例逐字节保留，并以 Hawk、1911 和 P27 实际素材验证 CAST、FBX、SMD、IK、蒙皮和武器运动 |

这些改进没有替换上游核心的动画混合思想；标准 MP5 项目仍作为兼容基准，原项目、RedFox 与 CAST 组件的署名和许可证均随发布包保留。

## 已完成的改进

- 区分用途不同的同名骨骼：右腕辅助骨骼保留 `j_gun`，武器根为 `tag_weapon` 下的 `j_gun__weapon`；模型、蒙皮和动画共用一份映射。
- 导出时按 ViewHands → Weapon → Attachment 规范化模型顺序，并把全部部件物理合并成一个 Model；即使工程把武器放在手臂之前、Maya 未启用 Import Merge，也只会生成一套骨架。
- 每个输出 CAST 保留全部模型网格、材质和重映射后的蒙皮权重，但只包含当前选中的一个烘焙动画。
- Additive、Gesture、GesturePose、普通层以及正负帧偏移继续走原版 RedFox 采样流程，最终转为绝对动画曲线。
- 修复原版双骨 IK 算法；循环目标会被拒绝，防止右手腕通过 `j_gun` 反向依赖自身。
- 修复动画复制时右手 IK、目标覆盖与层偏移丢失的问题。
- 项目载入后恢复层和部件的 UI 所有权，拖动、删除与排序命令可继续使用。
- 外部文件拖入动画行的“动画层”区域时，悬停动画优先于外层选择，文件只会加入该动画的层列表。
- CAST 写入采用临时文件替换，并在写入前后验证模型数、唯一动画和节点哈希。
- 输出格式扩展为 `.cast`、`.fbx`、`.smd`、`.seanim`；SMD 直接写出完整骨骼层级与逐帧局部变换，FBX 通过本机 Maya 官方插件保留模型、蒙皮和动画。
- 可选择输出真正的“仅动画 CAST”，其中不含模型、网格、材质或蒙皮；完整场景 CAST 仍是默认行为。
- 可选择只烘焙基础动画、姿势、动画层、IK 及间接受影响骨骼；遇到未知求解器时自动回退为全骨骼烘焙。
- 工具与产品名改为 Alchemy Stars；移除未使用的 Supabase 依赖，并将 `log4net` 更新至 3.4.0。
- 动画、姿势层、模型和输出目录可通过系统文件浏览器选择，也可在路径框输入、粘贴或直接拖入；软件按类别记忆上一次目录，重启后继续生效。
- 每个新导入动画的输出目录默认留空，必须明确选择后才能导出，避免同名 `.cast` 输出意外覆盖源动画；已有项目中保存的输出目录保持不变。
- 主界面、对话框和 About 窗口支持“跟随系统 / 简体中文 / English”，首次启动自动检测系统语言并记忆手动选择。
- 设置窗口按“输出 / IK 骨骼”重新分区，在最小窗口尺寸下仍可滚动使用；语言与 About 固定在受保护的右侧区域，不再被工具栏遮挡。
- 使用“炼金术瓶 + 星芒”主题的新应用图标。

## 直接使用

从 [GitHub Releases](https://github.com/ez4cywa/Alchemy-Stars/releases) 下载最新版 ZIP，解压后运行：

`Alchemy Stars.exe`

程序以空白批处理启动。点击工具栏的动画与模型按钮，或使用每个路径字段右侧的文件夹按钮，通过系统文件浏览器选择文件；路径框也可直接输入或粘贴路径，并支持从资源管理器把 CAST 文件准确拖到目标路径框。向输出目录框拖入文件时会自动采用其所在目录；可选姿势文件旁的清除按钮可恢复为空。

为保证安全，新导入动画不会自动采用源文件所在目录作为输出目录。导出前必须通过文件夹按钮、输入、粘贴或拖放明确设置输出位置，因此同名 `.cast` 不会在无意中替换源动画。更换动画源也不会自动补回输出目录；已有 `.aprj` 中明确保存过的输出目录则会继续保留。

打开“设置 → 输出”可在 `.cast`、`.fbx`、`.smd`、`.seanim` 中选择默认格式。选择 `.cast` 时，“仅输出合并动画 CAST”会移除完整模型场景；“仅烘焙相关骨骼”只保留基础动画、姿势、动画层、IK 及间接受影响骨骼的曲线。两个选项都会立即应用、全局记忆，并写入项目文件。

武器父节点留空时，导出会解析唯一的 `tag_weapon`；无法确定时会提示选择父骨骼。显式填写的父节点仍受尊重，旧 Hawk 项目若填为 `j_gun`，请改成 `tag_weapon` 并重新导出。仅动画 CAST 需配套 1.1.8 骨架，旧版合并场景应重新生成。

“仅烘焙相关骨骼”默认关闭，以保证最大兼容性。完整场景导出在保留曲线通过验证时可安全使用；若把仅动画 CAST 导入已有绑定，必须使用完全匹配且处于干净绑定姿势的骨架，否则应关闭该选项。SMD 因格式要求仍会逐帧写出完整姿势。FBX 需要本机 Maya（优先自动检测 Maya 2025）。

在“动画”页的主列表区域（包括空白处）右键，选择“导入动画…”可一次加入一个或多个 `.cast`。动画行内的“动画层”子区域（包括空白处）有独立的“导入动画层…”右键菜单，不会混淆导入目标。

也可以从资源管理器直接拖入动画文件：落在动画层子区域时会优先加入鼠标所在动画的层列表，即使外层选中了其他动画也不会导错；落在主列表其他区域时才按主动画处理。

在“模型部件”页的列表区域（包括空白处）右键，选择“导入模型部件…”可一次加入一个或多个 `.cast`。上述列表均可聚焦后按 `Shift+F10` 打开对应菜单。

选择手臂、武器、基础动画和需要的动画层后，点击工具栏中的“保存动画”按钮即可生成所选格式，例如：

`E:\Alchemy Stars\fork\AlchemyStars\output\sat_vm_ar_hawk_sprint_alchemy_stars.cast`

发布目录和 ZIP 均包含完整的 `Example` 文件夹。根目录的 `MP5Base.aprj`、`MP5Grip.aprj` 是从原版 Alchemist `Example` 目录直接迁移、保持逐字节一致的标准示例；统一的 `manifest.json` 为验收与发布提供路径、结构和校验值。按标准示例改进的 Hawk 冲刺、Idle 与批处理项目集中在 `Example\Hawk`。发布包解压后直接打开 `Example\README.zh-CN.md`（英文为 `Example\README.en-US.md`）；仓库在线版本见 [中文示例说明](https://github.com/ez4cywa/Alchemy-Stars/blob/main/fork/AlchemyStars/Example/README.zh-CN.md) 和 [English example guide](https://github.com/ez4cywa/Alchemy-Stars/blob/main/fork/AlchemyStars/Example/README.en-US.md)。

示例不会自动加载。`.aprj` 可从文件浏览器打开、拖进窗口或作为命令行参数载入。批处理中可以加入更多原项目支持的动画；程序会为每个条目分别输出一个文件，因此每个输出只对应一个已烘焙动画。项目文件保存绝对路径，换机器后应通过文件浏览器重新选择素材与输出目录，再使用“项目另存为”。

## Maya 2025

发布目录的 `MayaPlugin` 文件夹包含官方 CAST 导入插件。将其中的 `cast.py` 和 `castplugin.py` 放入 Maya 脚本/插件路径，在 Plug-in Manager 中载入 `castplugin.py`，然后用 File → Import 导入输出 CAST。

选择 FBX 时，炼金之星会自动查找本机 Maya 并调用 `fbxmaya` 生成二进制 FBX。可用环境变量 `ALCHEMY_STARS_MAYAPY` 指定其他 `mayapy.exe`。FBX 在 Maya 中导入时启用 **Fill Timeline** 可自动把播放范围设为动画范围。

当前冲刺产物已在本机 Maya 2025 中完成无界面实测：

- 215 个关节，一个骨架根，分别保留右腕辅助骨骼和武器根骨骼；
- 21 个网格全部导入且可见；
- 1290 条平移/旋转曲线，每个关节每帧都有关键帧；
- 30 FPS，播放范围 0–66；
- 左手 IK 按物理可达范围验证；
- 保留右手和武器动画，武器根随 `tag_weapon` 运动。

1.1.8 还按原始 1911 工程与 P27 ADS 验证武器：逐帧比较完整、精简、仅动画 CAST，以及独立组装的源骨架、FBX 和 SMD。源动画参考会先归一化量化四元数，并将短叠加层采样至完整区间再导入 Maya，以保留原旋转含义和原项目的持续偏移规则。结果记录在 `fork/AlchemyStars/output/weapon-regression/weapon-regression.maya2025.json`。

相关骨骼模式下，Hawk 冲刺从 215 根骨骼精简到 121 根；所有保留曲线都与全骨骼版本逐帧对比，所有省略骨骼均确认保持绑定姿势，精简后的完整场景 CAST 还会单独导入 Maya 2025 验证。

验证报告：`fork/AlchemyStars/output/sat_vm_ar_hawk_sprint_alchemy_stars.maya2025.json`。

## 构建与验证

开发构建需要 .NET 9 SDK；运行发布版需要本机安装 [.NET 9 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/9.0)：

```powershell
.\scripts\run-tests.ps1
.\scripts\build-release.ps1
```

`run-tests.ps1` 会编译改进后的原项目，先验证两份标准 MP5 示例没有被改写，再以 `fork\AlchemyStars\Example\Hawk\HawkSprint.aprj` 作为 Hawk 冲刺验证的唯一配置来源。验收会实际生成 CAST、仅动画 CAST、相关骨骼 CAST、SMD 和 FBX；逐帧比较精简与完整曲线，并把两类完整场景 CAST 和 FBX 重新导入 Maya 2025 检查骨架、网格、蒙皮、帧范围及武器动画；同时覆盖 Idle、批处理和“武器排在手臂之前”的回归。`build-release.ps1` 会生成不内置 .NET 运行环境的精简 Windows x64 单文件发布包和 ZIP。

项目约定每次功能性发布改动至少迭代补丁版本；本次版本为 `1.1.9`。

1.1.9 的 UI 检查修正了 About 图标裁切、工具栏挤压、动画层路径过窄及部分控件对比度不足的问题。检查范围和验证边界见 [UI 检查记录](design-system/alchemy-stars/pages/ui-audit-1.1.9.md)。

## 源码与许可

- 改进后的 Alchemist：`fork/AlchemyStars`，GPL-3.0，详见 `fork/AlchemyStars/LICENSE`。
- 固定版本 RedFox：`fork/RedFox` Git 子模块。
- Maya CAST 插件：`third_party/cast`，MIT，详见 `THIRD_PARTY_NOTICES.md`。

上游基线：Alchemist `d86da66536ed3bf304a5cb7142d360fb934f73fb`；RedFox `7031da79614d1d979b1f17cae9d4bda2c699fd53`。
