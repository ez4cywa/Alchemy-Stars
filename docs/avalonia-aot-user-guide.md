[**English**](avalonia-aot-user-guide.md) | [简体中文](avalonia-aot-user-guide.zh-CN.md)

# Alchemy Stars Avalonia preview quick guide

This guide applies to `1.3.0-preview.15`. WPF v1.1.9 remains the supported release until .NET 11 GA.

The 48 DIP activity rail switches between Animations, Model parts, Settings and About. The base-animation library sits on the left, a real CAST preview in the center, composition layers across the bottom, and collapsible properties on the right. Drag the dividers or focus them and use arrow keys to resize panels. Icon commands have localized tooltips and UI Automation names; the current page remains visible in the breadcrumb.

1. Open **Model parts** and add view hands first, then the weapon and attachments. Each CAST is classified from its skeleton topology; the inspector shows confidence and evidence, recommends `tag_weapon` for weapons, and lets you override either field. Low-confidence or unreadable files retain a visible review path. Existing `.aprj` entries are not reclassified when opened.
2. Open **Animations** and add the base animation. Paths remain editable and accept pasted Windows **Copy as path** values.
3. Add optional left/right pose files and enable only the IK chains required by the animation.
4. Add layers in order. A file dropped or right-click imported inside **Animation layers** is always treated as a layer, not a new base animation.
5. Enter an output name and explicitly choose an output folder. New entries intentionally leave this field blank.
6. In **Settings**, choose CAST, FBX, SMD or SEAnim. Animation-only CAST and relevant-bones-only baking are optional; keep full baking for the broadest compatibility.
7. Choose **Export all** or press `Ctrl+E`. Progress and results stay centered inside the application.

The app remembers the last directory for each picker category and can follow the Windows display language or be pinned to Chinese/English. Project files remain compatible with the original `.aprj` structure.

For the canonical Hawk recipe, open `fork/AlchemyStars/Example/Hawk/HawkSprint.aprj`. It is the single source of truth used by managed and Native AOT export verification.

## Custom themes and icons

Settings retains the Original, Classic Apple, Neumorphic and Windows XP styles. Choose light, dark or system appearance, or use the one-click light/dark switch. Appearance preferences are saved separately from animation projects.

Preview.15 completely redraws Classic Apple controls and all 34 functional icons in an independent platinum-light/graphite-dark skin. Windows XP also has its own controls and 34 matching icons.

- Import a theme: edit the bundled `Samples/Appearance/theme.json`, then choose Import theme in Settings. Use `version: 1`, a theme `name` and a built-in `baseStyle`; `light` and `dark` contain color token values, while `radii` contains control corner radii.
- Import icons: replace named PNG files in `Samples/Appearance/icons-template.zip`, repackage as ZIP, then choose Import icons. Partial packs are supported; omitted icons retain their built-in appearance.
- Restore defaults: use Remove custom theme and Restore built-in icons separately. Themes and icon packs are independent; original import files and animation projects are not deleted.

Selecting a built-in theme disables custom colors and radii but keeps custom icons active. Imports are copied into the settings directory and persist across restarts. The bundled `Samples/Appearance/README.zh-CN.md` documents every token, all 34 icon names and import limits in Chinese.

## Merged CAST preview

Choose **Build preview** in the composition workspace header to merge the selected animation into a unique temporary CAST with the existing export engine. It does not require or change the formal output folder; its cache is removed after loading an independent scene. **Open CAST preview** reads an existing merged file. A successful CAST export also previews the selected result.

- Drag the viewport or use arrow keys to orbit; use the wheel or +/− to zoom.
- Choose the camera button or press `1` for a fixed first-person view. It matches a newly created Maya camera at `T(0,0,0)`, applies `R(90°,0°,-90°)`, and uses a 90° horizontal FOV. Preview-only safe framing keeps the complete weapon visible without changing the CAST scene or export. Press `1` again, or use either Fit command, to return to orbit view.
- Use playback, Space, the frame slider and previous/next frame buttons to inspect animation.
- Press F to frame the subject. Right-click **Fit subject** or press Shift+F to include all geometry, including distant spare parts.
- Toggle the bone button for a skeleton overlay. Animation-only CAST contains curves, not an embedded skeleton: load its matching model parts into the current project first. The viewport identifies this project-supplied skeleton; matching bone names alone cannot guarantee a matching bind pose.
- The current renderer shows clay geometry without textures/materials. Settings edits do not automatically rebuild a snapshot: choose **Build preview** again.
- Track bars read source metadata in the background. Their widths represent true CAST frame counts, their horizontal positions include configured frame offsets, and the header shows the shared frame range. The text inside each bar repeats its frame count; an unreadable source stays visible and is marked **Frames unavailable**.
- Animation sampling, skinning, projection and lighting run in the background with at most one frame in flight and a 960×640 projection cap. The interactive viewport submits the prepared triangles through Avalonia's GPU-backed Skia custom drawing; a deterministic software renderer is retained for headless verification. Playback performance depends on scene complexity; validate final usage in Maya.

Keyboard commands:

| Command | Shortcut |
| --- | --- |
| Open project | `Ctrl+O` |
| Save project | `Ctrl+S` |
| Save project as | `Ctrl+Shift+S` |
| Export all | `Ctrl+E` |
| Toggle first-person CAST preview | `1` |
| Close result/error dialog | `Esc` |

Merged CAST output uses DQS (`quaternion`) for skinned meshes. The bundled Maya importer respects an explicit CAST skinning method and defaults legacy files without one to DQS.

For migration architecture, package details and validation evidence, see [Avalonia + Native AOT migration](avalonia-aot-migration.md).
