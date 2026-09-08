# 界面主题

## 自定义导入与经典 Apple 独立控件

`CustomAppearance.cs` 管理经过校验的主题 JSON 和 PNG 图标 ZIP 副本，保存到设置文件旁的 `Appearance` 目录。主题导入后出现第五项 `custom`，基础造型继承四套内置风格之一；切回内置风格不会删除导入记录。图标覆盖独立于配色选择，未提供的功能图标回退到当前主题。格式与全部 50 个配色／材质 token 见仓库 `docs/samples/appearance/README.zh-CN.md`，发行包中为 `Samples/Appearance/README.zh-CN.md`。

经典 Apple 的独立控件位于 `Themes/ClassicApple.axaml`，材质由 `ClassicAppleAppearance.cs` 管理，34 个原创图标由 `ClassicAppleIcons.cs` 在 24×24 画布上绘制。浅色铂金与深色石墨分别设计，保持中性图标和立体按钮、凹陷输入框、圆角开关与独立复选框／滑块。`AppearanceTheme.RemoveControlSkin` 同时清除逻辑树和模板视觉树的样式，避免切走后遗留按钮材质。

`CustomAppearanceSmoke` 覆盖主题解析、非法文件不替换已有内容、浅深色即时生效、局部图标覆盖、来源文件删除后的重新加载、恢复按钮、极端长宽比缩放和各基础风格的自定义圆角。错误主题与图标的加载提示分别保存，修复其中一项不会隐藏另一项。

## Windows XP 与快速明暗切换

新增第四套 `windows-xp`（Windows XP · 经典蓝）主题。浅色采用 Luna 蓝色渐变工具栏、米色面板、立体按钮与凹陷输入框；深色为同一造型的暗色适配。独立样式位于 `Themes/WindowsXp.axaml`，只在选中 XP 时加载，切走后移除。调色板由 `WindowsXpAppearance.cs` 管理。

`WindowsXpIcons.cs` 提供独立重绘的 34 个彩色分层矢量图标，使用固定 24×24 画布、渐变填色、描边和高光，不依赖外部图片。按钮、输入框、下拉箭头与菜单、复选框、双持开关、滚动条、播放滑块、轨道和面板使用 XP 样式；保留原有交互与键盘操作。

设置页“切换为深色／切换为浅色”按钮适用于全部主题，立即生效并保存。跟随系统时按实际显示模式切换为相反的显式模式；按钮文案随系统外观和语言同步更新。

预览播放栏改为自动高度，滑轨预留 36px，取消负边距，容纳 XP 的 20px 滑块与刻度。外观检查遍历四套主题、两种模式、五个页面，并验证滑块在起点、中点、终点的完整边界。

在“设置 → 外观”选择界面风格和明暗模式，即时生效，自动保存。当前提供原版（平面、胶囊主按钮与既有图标）、经典 Apple（铂金/石墨层次、单色线性图标）、拟物化（柔和阴影、凹陷输入框）和 Windows XP（经典蓝 Luna 控件与彩色图标）四种风格，每种支持浅色、深色、跟随系统。

首次启动及旧配置继续保持原版浅色，已有 `apple` 与 `neumorphic` 偏好不迁移、不改写。偏好保存在现有用户 settings.json 的 `ThemeStyle`（apple/classic-apple/neumorphic/windows-xp/custom）与 `ThemeMode`（light/dark/system）字段中，不写进 .aprj 项目文件；未知值回退至默认值。与原偏好机制一致，配置目录不可写时仍可在当前进程切换内置风格，但无法跨重启保存；自定义导入必须成功保存副本后才生效。

实现位于 `src/AlchemyStars.Avalonia/AppearanceTheme.cs`（调色板和形状资源）、`Themes/Appearance.axaml`（动态样式）、`ThemedIcon.cs`（主题图标切换），以及 `MainWindowViewModel.Appearance.cs`（设置绑定与文案）。颜色、形状、字号和图标模式通过动态资源更新，明暗模式同步 Avalonia FluentTheme；系统模式监听 ActualThemeVariantChanged，不重建窗口或工作区。

原版与拟物化主题保留现有功能布局、视觉参数和 Apple Blue 图标，旧主题截图可作像素回归基线。经典 Apple 主题使用独立设计的浅/暗色铂金与石墨调色板，主操作、选中态、焦点环、链接和图标都不使用蓝色；单色矢量图标按 24×24 坐标系、圆角端点和一致线宽绘制。应用标志仍作为品牌资产保留原色。

所有常规控件维持 44px 命中区域，预览运输控件保留 32/40px 紧凑尺寸。Button、ToggleButton、TextBox、ComboBox、CheckBox、ListBoxItem、ComboBoxItem、MenuItem 模板以及时间轴内容统一垂直居中；多行 TextBox 明确保留顶部对齐，避免长文本编辑器被错误居中。

验证：`--self-test` 覆盖默认值、四种风格持久化、偏好保存互不覆盖和未知值回退。`--render-smoke <path> --appearance-smoke --window-size 900x600` 通过真实下拉框切换全部风格与明暗组合，遍历五个页面，检查即时效果、控件文字垂直居中、经典 Apple 无蓝色令牌、图标族、磁盘读取、语言刷新和系统模式事件。运行此检查须设置 `ALCHEMY_STARS_SETTINGS_PATH` 指向临时文件，避免改动日常偏好。

视觉验收应在 900×600 与 1366×768 两种窗口尺寸下检查四种风格、亮/暗模式和动画、部件、双持、设置、关于五个页面。系统通知路径通过应用实际主题变化事件验证，不修改本机 Windows 个性化设置。
