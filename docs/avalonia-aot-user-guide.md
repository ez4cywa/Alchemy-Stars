[**English**](avalonia-aot-user-guide.md) | [简体中文](avalonia-aot-user-guide.zh-CN.md)

# Alchemy Stars Avalonia preview quick guide

This guide applies to `1.3.0-preview.28`. WPF v1.1.9 remains the supported release until .NET 11 GA.

The sidebar switches between Animation blend, Model parts, Dual merge, Settings and About. It shows icons and labels at normal widths and collapses to icons in narrow windows. The base-animation library sits on the left, a real CAST preview in the center, composition layers across the bottom, and collapsible properties on the right. Drag the dividers or focus them and use arrow keys to resize panels. Icon commands have localized tooltips and UI Automation names; the titlebar displays the current page, with window controls at the upper right.

1. Open **Model parts** and add view hands first, then the weapon and attachments. Each CAST is classified from its skeleton topology; the inspector shows confidence and evidence, recommends `tag_weapon` for weapons, and lets you override either field. Low-confidence or unreadable files retain a visible review path. Existing `.aprj` entries are not reclassified when opened.
2. Open **Animation blend** and add the base animation. Paths remain editable and accept pasted Windows **Copy as path** values.
3. Add optional left/right pose files and enable only the IK chains required by the animation.
4. Add layers in order. A file dropped or right-click imported inside **Animation layers** is always treated as a layer, not a new base animation.
5. Enter an output name and explicitly choose an output folder. New entries intentionally leave this field blank.
6. In **Settings**, choose CAST, FBX, SMD or SEAnim. Animation CAST is animation-only by default with no switch; relevant-bones-only baking remains optional, and full baking has the broadest compatibility.
7. Choose **Export all** or press `Ctrl+E`. Progress and results stay centered inside the application.

When the hands and weapon are ready, choose **Export bound model** in the Model Parts inspector. It reuses the animation blend skeleton attachment, mesh transform and skin-weight remapping rules, including attachments. The file is named `<weapon source name>_model`; CAST, FBX, SMD and SEAnim animation formats map to CAST, FBX, SMD and SEModel model files.

The app remembers the last directory for each picker category and can follow the Windows display language or be pinned to Chinese/English. Project files remain compatible with the original `.aprj` structure.

For the canonical Hawk recipe, open `fork/AlchemyStars/Example/Hawk/HawkSprint.aprj`. It is the single source of truth used by managed and Native AOT export verification.

## Selected export and keyboard accessibility

Select an animation and choose **Export selected** or press `Ctrl+Shift+E` to export just that job. `Ctrl+E` still exports all jobs. The selected job retains its layers, IK, format, naming and unified output directory. Unselected jobs with missing assets do not block it, but no workspace input may be overwritten. The action is unavailable without a selection, during a task, or while a message is open.

Dialogs isolate the workspace and cycle `Tab` / `Shift+Tab` internally. Focus the message reader to use arrow keys or `Page Up` / `Page Down`; `Esc` closes the dialog and restores the previous focus. Dialog text and preview instructions are exposed to assistive technology. Show bones is a semantic toggle synchronized with the `B` key.

## Output up axis

Choose **Keep scene axis (unspecified, default)**, **Z-up** or **Y-up** in Settings → Output format → Output up axis. This controls the whole exported scene, not individual parts. It is persisted in the project and can be saved as the default for new projects. Existing explicit Y/Z selections remain unchanged.

Keep scene axis writes the primary model's marker (arms first, otherwise the first model in merge order) and preserves every input's raw coordinates. This tolerates third-party CAST files whose axis marker does not match their numeric data. Composition previews use the base animation's marker for the camera in this mode, rather than reinterpreting the retained coordinates through a potentially incorrect arms marker. Choose explicit Z-up or Y-up when every input has reliable axis metadata and should be converted. Full/selected exports, dual animations and companion models share the same coordinate-preservation rule.

Inputs may mix X/Y/Z-up without rejection. Before merging, bone translations/rotations/scale axes, mesh positions, normals and tangents are converted in memory; base animations, layers and hand poses use the same target basis. Missing/unknown metadata uses Y-up and cannot reveal an asset's intended orientation. Source files are never rewritten.

Model CAST, animation-only CAST and FBX carry the resolved output axis explicitly. SMD/SEAnim numbers follow the same rule without a matching scene-axis field. The Blender/Maya bridges retain centimetre units. The preview's upper-right XYZ gizmo follows the camera: drag it to orbit, click a positive or negative axis endpoint to snap the view, or focus it and press X/Y/Z (Shift for the negative axis). Preserving an X-up FBX requires Blender; the Maya backend supports explicit Y/Z-up output.

Developer regression: `AlchemyStars.Avalonia.exe --axis-smoke <test-output-directory> --fbx`. The backend round-trip check remains `mayapy scripts/verify-fbx-axis.py --blender <blender.exe>`.

## Utilities: remember an arms model

Reuse the first imported arms model is enabled by default under Settings → Utilities. The app remembers the first imported arms path and adds it at startup or when creating a new project. Opening an existing `.aprj` does not insert or replace models. Only the path is remembered; the source asset is not copied.

Turning the option off stops reuse without removing the saved path or current models. Choose arms model lets you specify another file. Forget arms clears only the saved path, not the source file or project models; while reuse is enabled, a later first import can be remembered again. Missing saved files are skipped with a visible status message.

## Batch overlays with one shared base

Select a task with a base animation, open the batch icon in the Animation layers toolbar, and choose **Batch overlays with shared base…**. Select multiple CAST files to create one independent task per overlay, reusing the base, output folder, frame rate, IK and weapon-follow settings.

If a layer is selected, each file replaces that slot and retains its offset and type; otherwise it is appended. The template and other common layers remain unchanged. Output names use each selected filename, adding a numeric suffix on collisions. Review the new tasks in the library, then use **Export all**. The original sprint batch command remains in the same menu.

## Windows 2000 classic desktop theme

Choose **Windows 2000 · Classic desktop** in Settings → Interface style for gray beveled controls, navy title and selection states, and 34 matching pixel-grid icons. Light, dark and system modes are supported and persisted across restarts, without modifying projects. Custom theme JSON can also use `windows-2000` as its `baseStyle`.

For Ubuntu GTK/Yaru controls, import `Samples/Ubuntu-Yaru/theme.json` and `Samples/Ubuntu-Yaru/icons.zip` separately. This enables independent GTK/Yaru-inspired Avalonia templates and 34 icons in light/dark modes. Theme JSON selects `baseStyle: "ubuntu-yaru"` and supports `Gtk` material tokens. Requires preview.22 or later; no GTK runtime or external GTK CSS/XML is loaded.

## GitHub updates

Automatic updates are off by default. When enabled, the app checks and downloads newer releases from `ez4cywa/Alchemy-Stars` on GitHub, staying on the installed channel: preview builds select previews and stable builds select stable releases. Downloads are checked for size, SHA-256 and archive structure.

Installation requires saving the current project and confirming restart after the download finishes. You can decline and continue working; a completed download never forces a restart. Older builds do not contain this updater: install preview.16 manually once before using future in-app updates.

## Custom themes and icons

Appearance settings are below Utilities in Settings.

Settings provides the Original, Classic Apple and Windows XP styles. Original uses the modern desktop controls with its original icons. The standalone Modern desktop style has been removed; its saved preference migrates to Original. Choose light, dark or system appearance, or use the one-click light/dark switch. Appearance preferences are saved separately from animation projects.

Preview.15 completely redraws Classic Apple controls and all 34 functional icons in an independent platinum-light/graphite-dark skin. Windows XP also has its own controls and 34 matching icons.

- Import a theme: edit the bundled `Samples/Appearance/theme.json`, then choose Import theme in Settings. Use `version: 1`, a theme `name` and a built-in `baseStyle`; `light` and `dark` contain color token values, while `radii` contains control corner radii.
- Import icons: replace named PNG files in `Samples/Appearance/icons-template.zip`, repackage as ZIP, then choose Import icons. Partial packs are supported; omitted icons retain their built-in appearance.
- Restore defaults: use Remove custom theme and Restore built-in icons separately. Themes and icon packs are independent; original import files and animation projects are not deleted.

Selecting a built-in theme disables custom colors and radii but keeps custom icons active. Imports are copied into the settings directory and persist across restarts. The bundled `Samples/Appearance/README.zh-CN.md` documents every token, all 34 icon names and import limits in Chinese.

## Merged CAST preview

Choose **Build preview** in the composition workspace header to merge the selected animation into a unique temporary complete CAST with the existing export engine. It does not require or change the formal output folder; its cache is removed after loading an independent scene containing the hands and weapon meshes. **Open CAST preview** reads an existing file. Formal animation CAST contains animation data only; after export, the UI builds a separate complete preview snapshot so preview and disk output stay independent.

- Drag the viewport or use arrow keys to orbit; use the wheel or +/− to zoom.
- Choose the camera button or press `1` for a fixed first-person view. It matches a newly created Maya camera at `T(0,0,0)`, applies `R(90°,0°,-90°)`, and uses a 90° horizontal FOV. Preview-only safe framing keeps the complete weapon visible without changing the CAST scene or export. Press `1` again, or use either Fit command, to return to orbit view.
- Use playback, Space, the frame slider and previous/next frame buttons to inspect animation.
- Press F to frame the subject. Right-click **Fit subject** or press Shift+F to include all geometry, including distant spare parts.
- Toggle the bone button for a skeleton overlay. An exported animation CAST contains curves without an embedded skeleton, while composition preview uses a separate complete model scene. Before manually opening an animation-only CAST, load its matching model parts into the project; matching bone names alone cannot guarantee a matching bind pose.
- The current renderer shows clay geometry without textures/materials. Settings edits do not automatically rebuild a snapshot: choose **Build preview** again.
- Track bars read source metadata in the background. Their widths represent true CAST frame counts, their horizontal positions include configured frame offsets, and the header shows the shared frame range. The text inside each bar repeats its frame count; an unreadable source stays visible and is marked **Frames unavailable**.
- Animation sampling, skinning, projection and lighting run in the background with at most one frame in flight and a 960×640 projection cap. The interactive viewport submits the prepared triangles through Avalonia's GPU-backed Skia custom drawing; a deterministic software renderer is retained for headless verification. Playback performance depends on scene complexity; validate final usage in Maya.

Keyboard commands:

The product name is centered at the top of the window. The default output name follows the first animation layer's filename stem, including replacement, renaming, and reordering; removing all layers restores the base animation's name. Changes to other layers do not affect the output name, and custom names remain unchanged. Text-field Cut/Copy/Paste menus follow the selected language. Settings → Utilities → Unified output folder persists a shared destination for animation and dual-wield exports without changing project paths or preview caches; clearing it restores per-item destinations.

| Command | Shortcut |
| --- | --- |
| New project | `Ctrl+N` |
| Open project | `Ctrl+O` |
| Save project | `Ctrl+S` |
| Save project as | `Ctrl+Shift+S` |
| Export all | `Ctrl+E` |
| Import animations/models on the active page | `Ctrl+I` |
| Import animation layers | `Ctrl+L` |
| New dual task | `Ctrl+T` |
| Build preview / open CAST preview | `F5` / `Ctrl+Shift+O` |
| Navigate between five pages | `Ctrl+1` through `Ctrl+5` |
| Remove focused list selection / reorder models or layers | `Delete` / `Alt+Up`, `Alt+Down` |
| Complete shortcut reference | `F1` |
| Toggle first-person CAST preview | `1` |
| Close result/error dialog | `Esc` |

List commands require list focus; text fields retain editing keys. Viewport commands require preview focus. Other shortcuts pause while tasks or messages are active.

Merged CAST output uses DQS (`quaternion`) for skinned meshes. The bundled Maya importer respects an explicit CAST skinning method and defaults legacy files without one to DQS.

For migration architecture, package details and validation evidence, see [Avalonia + Native AOT migration](avalonia-aot-migration.md).
