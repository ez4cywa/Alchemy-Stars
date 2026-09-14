# RF 摄像机动画：可选接入

导出的摄像机轨道与 Unity 游戏视角驱动是两件事。`RF_Root/RF_CameraMotion` 是独立动画骨骼，不驱动蒙皮，也不作为手臂或武器的父级；不安装桥接时，普通 FBX 动画继续按原工作流使用。桥接文件不会自动复制进 Unity 工程，不会自动修改导入器、Animator Controller、Prefab 或项目设置。

## 已核实的接口与边界

本机 `RFTools (2).unitypackage` 的 `Assets/RFTools/Scripts/Weapon Tools/Components/Weapon.cs` 未提供直接绑定动画摄像机骨骼的字段。采用官方 Ravenscript 的 `PlayerCamera.fpCameraLocalPosition` 和 `fpCameraLocalRotation` 偏移接口；不是通过移动玩家朝向来模拟镜头。其默认值分别是零位移和单位旋转。[PlayerCamera 官方接口](https://ravenfieldgame.com/ravenscript/api/PlayerCamera.html)

在 `LateUpdate` 读取 Animator 已求值的骨骼，因此不依赖片段名：Idle、冲刺、换弹、检视、ADS、开火、回稳及混合过渡均走同一读取逻辑。官方定义了动画更新后的 `LateUpdate`，以及禁用、销毁回调。[Behaviours 官方接口](https://ravenfieldgame.com/ravenscript/api-behaviours.html)

运行时脚本是 `scripts/RFCameraMotion.txt`。Ravenscript 要求 `.txt` 作为 Unity TextAsset，并由 RFTools 的 `ScriptedBehaviour` 执行；不是将自定义 C# MonoBehaviour 打进 mod。[官方接入教程](https://ravenfieldgame.com/ravenscript/getting-started.html)

## 显式绑定

发布包中，运行时文件位于 `Tools/Ravenscript/RFCameraMotion.txt`，编辑器工具位于 `Tools/UnityEditor/UnityRavenfieldCameraSetup.cs`；下文的 `scripts/` 是源码仓库路径。

1. 正常导入 FBX，并自行配置现有动画控制器。保留 `RF_CameraMotion` 的 Transform 和曲线；使用 Optimize Game Objects 时需要自行暴露该节点。工具不替你更改优化选项。
2. 将 `scripts/RFCameraMotion.txt` 复制到自己的 Unity Assets 目录；将 `scripts/UnityRavenfieldCameraSetup.cs` 复制到 `Assets/Editor`。打开 `Tools > Ravenfield > Bind optional RF camera motion`。
3. 停止 Play 与 Animation 预览。指定场景中的 Weapon 对象、其下的 `RF_CameraMotion`、脚本文本，以及一个静态摄像机参考 Transform。参考 Transform 的 +Z 是实际第一人称视线方向、+Y 是上方向；不要凭名称假定武器根节点已经对齐。
4. 点击绑定按钮，仅在该场景实例新增 `RF Camera Bridge` 子对象和 Ravenscript。原骨骼、网格、权重和控制器不会修改。支持 Undo；保存场景或 Apply Prefab 由你手动决定。
5. 使用 RFTools 的 Test Map or Content Mod 检查效果，通过后再保存为正式预制体。移除整个 `RF Camera Bridge` 子对象即可撤销接入；仅关闭生成端开关不能替代移除已接入的桥接。

绑定要求 Motion 仍与导入的 FBX prefab 源关联，以读取导入模型的静态默认 Transform；不会用播放中第一帧作为 rest，也不假定 FBX 导入后的 `localRotation` 为 identity。非均匀或镜像祖先缩放不支持，会在修改前拒绝绑定。

## 坐标与生命周期

镜头姿态先转换到 `RF_Root.parent`（稳定 armature 对象）坐标：`rig.InverseTransformPoint(motion.position)` 和 `inverse(rig.rotation) * motion.rotation`。rest 同样从 FBX 源中读取相对于对应 armature 的姿态，再相减平移、相乘逆 rest 旋转。这样包含 `RF_Root` 的绑定与动画姿态差异：它的 bind 旋转非 identity，但正常动画可为 identity，直接使用镜头的 parent-local 增量会错误引入静态旋转。

结果使用绑定时固定的 armature 到摄像机参考坐标变换转换。固定比例包含 armature 的正均匀缩放，使平移进入 Unity 世界米制。不能运行时用已晃动的摄像机 world transform 反求基准，否则可能把上一帧镜头动作反馈到下一帧。

每帧先撤销自身上一帧偏移，再读取本帧骨骼并组合进现有偏移；不会累计同一曲线。切枪、死亡、乘车、非第一人称、覆盖摄像机、Animator 停用时停止输出，禁用和销毁时恢复原偏移。恢复前检查当前值是否仍等于自身最后写入值，避免抹掉后来接管的脚本值。

这些相机偏移属性是共享通道，官方没有按脚本隔离的混合层。两个 mod 同时持续写同一属性不能保证无冲突；本桥接的条件恢复是尽力保护，不是跨 mod 仲裁。现有原生后坐力、ADS FOV、瞄准位置和脚本镜头效果均不自动关闭，游戏内需要检查重复叠加。

所有会进入同一控制器的片段都应包含摄像机中性或运动曲线。混用旧片段（没有该骨骼曲线）时，应自行确认 Animator 的层、Avatar Mask 和 Write Defaults 设置不会留下上一段姿态；本工具不改这些设置。生成端的强度已经烘焙进轨道，桥接不再次缩放。

## 验证状态

已核实本机 RFTools 的 `Lua.ScriptedBehaviour.source/behaviour/targets` 和 `Lua.NamedTarget.name/value` 字段，以及官方摄像机偏移、Animator、生命周期接口。`scripts/test-ravenfield-camera-bridge.py` 使用可选 `lupa` 执行真实 Lua 文件，使用三维向量与四元数覆盖非交换旋转、非 identity 的 RF_Root bind 与动画差异、运动 armature/root 消除、100 帧不累加、切枪/禁用/销毁恢复、其他写入者保护和停用条件；数学宿主不代替 Unity 导入和 API 绑定验证。尚未在目标 Unity 工程编译绑定工具，也未进入 Ravenfield 实机验证，不能将“骨骼已导出”视为“游戏摄像机已验证”。

目标工程验收至少包含：零轨道无静态偏移；三个平移轴和三个旋转轴方向；短开火曲线、长检视曲线与过渡；切枪/死亡/禁用后无残留；Animator 停用、第三人称、载具不会输出；原生后坐力叠加；移除桥接后原武器和全部动画仍正常工作。
