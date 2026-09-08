# 自定义主题和图标（preview.15）

在“设置 → 外观”使用“导入主题 JSON”和“导入图标 ZIP”。本目录的 `theme.json` 是森林配色主题，`icons-template.zip` 包含 34 个可替换的 PNG 图标。主题与图标独立导入、独立移除；无需修改程序文件。

## 主题 JSON

复制 `theme.json` 后编辑，再导入。成功后立即选中“自定义 · 主题名称”。以下是最小示例：

```json
{
  "version": 1,
  "name": "My theme",
  "baseStyle": "classic-apple",
  "light": { "Accent": "#365A45" },
  "dark": { "Accent": "#ACD4AD" },
  "radii": { "Button": 4 }
}
```

顶层只接受 `version`、`name`、`baseStyle`、`light`、`dark`、`radii`。字段和配色 token 区分大小写，不允许未知字段或重复字段；这是配色数据文件，不支持 CSS、XAML 或脚本。

| 字段 | 格式和作用 |
| --- | --- |
| `version` | 必填，整数 `1`。 |
| `name` | 必填，去除首尾空格后为 1–64 个字符。 |
| `baseStyle` | 可省略，默认 `apple`；决定未覆盖的配色、控件与内置图标。 |
| `light` / `dark` | 两者都必填，各为至少包含一个有效 token 的对象。只写要修改的 token，其余继承基础风格。没有 `colors` 包装层。 |
| `radii` | 可省略；圆角对象，数值单位为 DIP，范围 0–32，允许小数，不能重复命名。 |

`baseStyle` 的全部取值：

| 值 | 内置基础风格 |
| --- | --- |
| `apple` | 原版 · 简洁 |
| `classic-apple` | 经典 Apple：独立铂金浅色／石墨深色控件及 34 个重绘图标 |
| `neumorphic` | 拟物化 · 柔和浮雕 |
| `windows-xp` | Windows XP · 经典蓝 |

配色使用 `"#RRGGBB"` 或 `"#AARRGGBB"`；八位格式的透明度在最前面，不是 `#RRGGBBAA`。不支持颜色名称、`rgb()` 或三位十六进制。下面列出全部 50 个 token；名称不带 `Alchemy` 或 `Brush` 前后缀：

| 类别 | 可用 token |
| --- | --- |
| 页面与面板 | `Background`、`Sidebar`、`Surface`、`SubtleSurface`、`SurfaceRaised`、`Canvas` |
| 命令区 | `CommandBar`、`CommandText`、`OnDarkMuted`、`DarkTile` |
| 文字与图标 | `Text`、`MutedText`、`Icon` |
| 边框 | `Border`、`BorderStrong` |
| 强调与焦点 | `Accent`、`Action`、`Focus`、`AccentHover`、`AccentBorder` |
| 交互状态 | `Hover`、`Pressed`、`Selected` |
| 标题与反馈 | `Hero`、`Success`、`Error`、`Chip` |
| 动画轨道 | `BaseClip`、`BaseEdge`、`LayerClip`、`LayerEdge` |
| XP 控件 | `XpHeader`、`XpButton`、`XpButtonHover`、`XpButtonPressed`、`XpAction`、`XpActionHover`、`XpActionPressed`、`XpHoverBorder` |
| 经典 Apple 控件 | `AppleChrome`、`AppleHeader`、`AppleButton`、`AppleButtonHover`、`AppleButtonPressed`、`AppleAction`、`AppleActionHover`、`AppleActionPressed`、`AppleThumb`、`AppleEdge`、`AppleHoverEdge` |

`Xp` 和 `Apple` 开头的 token 还可使用 2–5 个颜色组成的数组，生成从上到下、等间距的渐变，例如 `"XpButton": ["#FFFFFF", "#E5E8EE"]` 或 `"AppleAction": ["#39734D", "#285D3D"]`。它们分别用于 XP 和经典 Apple 基础风格的控件材质。其他 token 只接受单色（单元素颜色数组也可读取）。调整 `Accent` 不会自动替你改写 `Action`、`Selected` 或独立材质 token，应按需分别设置。

经典 Apple 材质中，`AppleChrome` 是外框区域，`AppleHeader` 是标题，`AppleButton`／`AppleAction` 分别为普通／主操作按钮，其 `Hover`、`Pressed` 变体控制悬停和按下状态；`AppleThumb` 用于滑块，`AppleEdge`／`AppleHoverEdge` 分别控制普通／悬停边缘。本目录森林示例已同时覆盖通用配色与经典 Apple 标题、主按钮状态及边缘配色。

`radii` 支持全部五个名称：`Button`（普通按钮）、`Action`（主操作）、`Panel`（面板）、`Header`（标题区域）、`Card`（卡片）。省略的项沿用基础风格；圆角配置同时用于浅色和深色。

主题文件最大 128 KiB，JSON 最大嵌套深度为 8。使用标准 JSON，不写注释或末尾多余逗号。建议同时检查浅色和深色下的文字、选中状态、工具提示及焦点可见性。

## PNG 图标 ZIP

解压 `icons-template.zip`，编辑需要替换的图片。可删除不想覆盖的 PNG，只把需要的图标重新压缩为 ZIP 后导入。文件名使用以下 34 个名称之一，扩展名为 `.png`；名称及扩展名不区分大小写：

```text
about.png
add.png
animation-layers.png
animation-library.png
batch-processing.png
camera-view.png
cast-preview.png
delete.png
dual-wield.png
export-animation.png
fit-view.png
hand-pose.png
import-assets.png
inverse-kinematics.png
language.png
model-parts.png
move-down.png
move-up.png
next-frame.png
notification.png
output-naming.png
output-settings.png
pause.png
play.png
previous-frame.png
project-workspace.png
restore-layout.png
save.png
save-as.png
timeline-playback.png
weapon-follow.png
weapon-processing-mode.png
zoom-in.png
zoom-out.png
```

PNG 可放 ZIP 根目录，也可放普通子目录，例如 `icons/play.png`。图标按文件名识别，所以不同目录中的同名图标仍算重复。文件路径不能使用绝对路径、盘符、冒号、`.`、`..` 或空路径段；最简单的方式是直接压缩根目录中的 PNG。

图标包的限制：

- 只放已知名称的 PNG 文件，不夹带 README、JSON、SVG、ICO 或其他文件。空目录项可以存在；包括目录项在内最多 68 个 ZIP 条目，至少有一个有效 PNG，每个图标只能出现一次。
- ZIP 压缩文件最大 8 MiB；每张 PNG 文件为 24 字节至 2 MiB，全部 PNG 解压后总计不超过 16 MiB。
- 每张图的宽、高分别为 1–1024 像素。完整解码通过尺寸检查后，程序按最长边不超过 64 像素等比例缩小；原图最长边不超过 64 时不放大。供界面使用的位图因此最多为 64×64，再等比例适配控件。建议使用透明背景、方形画布并保留边缘空白。
- 自定义 PNG 保留原始颜色，不随主题重新染色，也没有独立的浅色／深色图标文件。请检查两种模式下的可见性。

每次导入会替换当前整套自定义图标包，不会与上一包合并。新包没提供的图标使用当前主题的内置图标，而不是沿用上一包。图标包只替换上述功能图标，不替换应用 EXE、任务栏或“关于”页的应用标志。

## 保存、切换与恢复

导入通过校验后立即生效，并将文件复制到用户设置目录的 `Appearance` 子目录，默认是 `%LOCALAPPDATA%\Alchemy Stars\Appearance`。其中主题保存为 `theme.json`，图标保存为 `icons.zip`。重新启动继续加载这些副本；移动或删除原始导入文件不会影响已导入外观。若使用 `ALCHEMY_STARS_SETTINGS_PATH` 指定设置文件，则 `Appearance` 位于该设置文件旁。

切换到任一内置主题时，自定义主题的配色和圆角停止生效，但导入记录仍保留，可重新选择“自定义 · 名称”；自定义图标独立于主题，仍继续覆盖同名图标。

- “移除自定义主题”：删除设置目录中的主题副本。当前使用自定义主题时回到“原版 · 简洁”，否则保留当前内置主题；图标包不受影响。
- “恢复内置图标”：删除设置目录中的图标包副本，所有功能图标恢复当前主题的内置绘制；主题选择不受影响。
- 要完整恢复内置外观，分别使用以上两个按钮。它们不删除你原始的 JSON／ZIP 文件，也不修改动画工程。

导入文件无效时会显示错误，并保留此前外观；启动时若保存的副本无法读取，会在设置页提示加载失败并对无法加载的部分回退。
