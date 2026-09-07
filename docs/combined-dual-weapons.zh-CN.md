# 单文件内包含左右武器的双持模式

在 `codex/avalonia-ui` 上新增任务级“模型处理模式”。模型部件仍选择一个手臂文件和一个武器文件；两者各包含一个 CAST Model 节点。

## 使用

1. 在模型部件中导入手臂、包含左右武器的模型。
2. 在动画页面导入左右动画，按需要配置姿势、叠加层和 IK。
3. 在双持页面配对来源，在属性检查器的“模型处理模式”中选择“模型已包含左右武器”。模式按任务保存，批量导出允许任务采用不同模式。
4. 左右武器分支留空时，程序根据所选左右来源及工程内同武器前缀的左右动画曲线识别分支。无法唯一识别时，填写各侧分支的根骨骼名称。显式填写两侧后不依赖自动识别。
5. 生成合成预览；选定输出目录后导出。原有“导出武器模型”开关继续控制额外的 `_model.cast`。

普通单武器输入继续使用“挂点模式：复用单个武器”。旧项目默认该模式；包含新模式的工程保存为 schema 3，旧程序会拒绝加载，避免把双武器错误地复制两遍。

## quantao 样本

当前样本手臂为 141 骨骼/3 网格，拳套为 25 骨骼/4 网格，左右两侧共用武器根。其中两个网格横跨左右两套几何，不能简单按网格编号分配。

自动识别出的分支：

- 左：`bone_485fca2f995d0083`
- 右：`bone_3f6fb22ccb69d785`

输出为一个连接骨架、191 骨骼、9 网格、101526 顶点、111170 三角面。网格数增加来自混合网格的左右拆分；顶点与三角面总数等于原始手臂加一套左右拳套，没有复制整套拳套。两侧保留完整源辅助骨架，供独立源动作和 IK 采样使用，因此不以最少骨骼数为目标。

## 处理与边界

拆分在内存快照中完成，不修改源文件。根据分支后代及非零蒙皮权重判断每个顶点的归属，再拆分三角面、重建索引，并同步保留位置、法线、切线、UV、顶点颜色、蒙皮权重和材质引用。左右快照分别进入现有姿势/叠加/IK 烘焙，然后复用挂点求解和统一骨架导出。

分支必须互不包含，且能覆盖全部带权重的武器几何。无法识别、跨侧权重、绑定公共骨骼的顶点、未蒙皮顶点、跨侧连接面、形态键或不支持的网格子数据会明确报错，不进行猜测性切割。此时需要指定合适分支或在 Blender/Maya 中整理模型。既有双持的同帧率、同处理后帧数、单位缩放等限制继续适用。

## 复现验证

```powershell
& 'output/dotnet-sdk/dotnet.exe' build fork/AlchemyStars/src/AlchemyStars.Avalonia/AlchemyStars.Avalonia.csproj -c Release --no-restore
& 'output/dotnet-sdk/dotnet.exe' fork/AlchemyStars/src/AlchemyStars.Avalonia/bin/Release/net11.0/AlchemyStars.Avalonia.dll --combined-dual-smoke E:/AAAAAAStudy/cast/quantao E:/AAAAAAStudy/Alchemy-Stars/output/combined-dual-qa
```

测试覆盖 17 对动作的几何数量守恒、所有帧的挂点及蒙皮武器骨骼世界变换、工程模式保存加载、手动分支、非法分支、模型开关、仅动画输出和原始素材哈希。原有 `--dual-smoke` 的 9 对 Scarab 动作及叠加/导出回归通过。

本地可运行程序：`output/avalonia-ui-combined-dual/AlchemyStars.Avalonia.exe`。
测试工程：`output/combined-dual-qa/Quantao-Dual.aprj`。
Native 测试日志：`output/combined-dual-qa/native-tests.log`。
Blender 4.3 检视动作 301 帧报告：`output/combined-dual-qa/blender-inspect.json`。
Blender 分离模型/动画 71 帧等价校验：`output/combined-dual-qa/blender-companion.log`。

测试素材引用的部分纹理不在目录内，因此验证范围为几何、骨架和动画。Maya 导出链路保留，但本次未在 Maya 中实测；本次也未重新验证严格 FBX 往返等价性。
