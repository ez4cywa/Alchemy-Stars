# 界面主题

在“设置 → 外观”选择界面风格和明暗模式，即时生效，自动保存。当前提供原版（平面、胶囊主按钮与既有图标）、经典 Apple（铂金/石墨层次、单色线性图标）和拟物化（柔和阴影、凹陷输入框）三种风格，每种支持浅色、深色、跟随系统。

首次启动及旧配置继续保持原版浅色，已有 `apple` 与 `neumorphic` 偏好不迁移、不改写。偏好保存在现有用户 settings.json 的 `ThemeStyle`（apple/classic-apple/neumorphic）与 `ThemeMode`（light/dark/system）字段中，不写进 .aprj 项目文件；未知值回退至默认值。与原偏好机制一致，配置目录不可写时仍可在当前进程切换，但无法跨重启保存。

实现位于 `src/AlchemyStars.Avalonia/AppearanceTheme.cs`（调色板和形状资源）、`Themes/Appearance.axaml`（动态样式）、`ThemedIcon.cs`（主题图标切换），以及 `MainWindowViewModel.Appearance.cs`（设置绑定与文案）。颜色、形状、字号和图标模式通过动态资源更新，明暗模式同步 Avalonia FluentTheme；系统模式监听 ActualThemeVariantChanged，不重建窗口或工作区。

原版与拟物化主题保留现有功能布局、视觉参数和 Apple Blue 图标，旧主题截图可作像素回归基线。经典 Apple 主题使用独立设计的浅/暗色铂金与石墨调色板，主操作、选中态、焦点环、链接和图标都不使用蓝色；单色矢量图标按 20×20 坐标系、圆角端点和一致线宽绘制。应用标志仍作为品牌资产保留原色。

所有常规控件维持 44px 命中区域，预览运输控件保留 32/40px 紧凑尺寸。Button、ToggleButton、TextBox、ComboBox、CheckBox、ListBoxItem、ComboBoxItem、MenuItem 模板以及时间轴内容统一垂直居中；多行 TextBox 明确保留顶部对齐，避免长文本编辑器被错误居中。

验证：`--self-test` 覆盖默认值、三种风格持久化、偏好保存互不覆盖和未知值回退。`--render-smoke <path> --appearance-smoke --window-size 900x600` 通过真实下拉框切换全部风格与明暗组合，遍历五个页面，检查即时效果、控件文字垂直居中、经典 Apple 无蓝色令牌、图标族、磁盘读取、语言刷新和系统模式事件。运行此检查须设置 `ALCHEMY_STARS_SETTINGS_PATH` 指向临时文件，避免改动日常偏好。

视觉验收应在 900×600 与 1366×768 两种窗口尺寸下检查三种风格、亮/暗模式和动画、部件、双持、设置、关于五个页面。系统通知路径通过应用实际主题变化事件验证，不修改本机 Windows 个性化设置。
