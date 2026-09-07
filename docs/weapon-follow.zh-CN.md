# 武器跟随手腕

动画任务的属性检查器新增“武器跟随”：原始动画挂点、左手、右手。默认原始挂点，旧项目行为不变；跟随设置也用于普通动画预览、导出及双持任务的左右源动画处理。

对于 echo 的左侧 idle + sprint_loop：基础动画选择 idle，sprint_loop 作为“叠加”层，pose_l 放入左手姿势栏，帧率 30 FPS，武器跟随选择“左手（保留握持偏移）”。该侧 IK 在跟随模式下自动跳过，不改写原有 IK 开关设置。另一侧 IK 在武器跟随后求解。

参考握持来自基础动画第 0 帧加手部姿势及任务原有 IK，不包含叠加层。每帧先叠加手臂动作，再按照该固定握持偏移求出武器挂点世界变换，并转换为原父级下的局部变换。武器子骨骼动画和原有骨架父级保持不变。CAST 量化后的旋转先归一化后参与握持计算。

普通任务驱动 `tag_weapon`；双持源任务驱动该双持任务配置的“源动作武器挂点”。武器须位于此挂点的后代中，手腕使用工程 IK 设置的末端骨骼（默认 `j_wrist_le` / `j_wrist_ri`）。缺少骨骼、依赖循环、非单位绑定缩放及旧版 COD 变换会报错。本模式固定握持关系，不能同时保留武器挂点原本相对手腕的独立运动；需要这种运动的动作继续使用原始挂点模式。

包含跟随设置的工程保存为 schema 4，旧程序会拒绝加载。复制动画任务和预览快照保留该设置。

## 本地验证

程序：`output/avalonia-ui-weapon-follow/AlchemyStars.Avalonia.exe`。
echo 测试工程：`output/echo-idle-sprint-qa/follow-left.aprj`。

实际 60 帧对照显示：跟随后的手腕与关闭 IK 的原始叠加结果一致，武器挂点位移约 25.985 模型单位，握持位置最大误差约 0.000025，握持旋转保持不变；未开启跟随时输出 SHA256 与修改前相同。Blender 4.3 的 60 帧 CAST 导入验证通过，报告在 `output/echo-idle-sprint-qa/blender-follow.json`。本次没有 Maya 实测。

可复用检查脚本：

```powershell
python blender/verify_weapon_follow.py baseline.cast followed.cast idle-reference.cast
```

baseline 为不跟随、关闭该手 IK 的分层结果；idle-reference 为基础动画加手部姿势、无叠加层、保留任务原有 IK 的结果。脚本检查逐帧手腕一致性、武器运动和握持位置/旋转不变。
