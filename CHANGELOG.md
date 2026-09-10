# Changelog

## 1.3.0-preview.23 — 2026-09-10 (CAST timing, export safety and resource workflow)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.23.zh-CN.md).

- Normalize CAST animation, pose and layer sampling to the fixed 30 FPS export timeline. Source frame rates are read from the CAST animation node, output duration uses ceiling-based frame conversion, and translation, rotation, scale and action tracks are resampled rather than merely relabelled. This keeps a 60 FPS source at the correct duration and preserves intermediate poses.
- Reject invalid or non-finite CAST frame rates before export. Timeline metadata now reports output-frame units, so the editor bars, playback and exported output agree on duration.
- Enforce CAST-only animation imports for base animations, layers, hand poses, drag-and-drop paths, file-picker selections, clipboard payloads and dual-wield sources. SEAnim remains an output format, but cannot silently enter an input slot.
- Prevent an export from overwriting any model, animation, pose or layer input in the whole export batch. Duplicate output paths are rejected before writing, including cross-task collisions.
- Improve blend-template analysis: batch scans reuse model and directory metadata, match poses and offsets within the same animation family, support action variants, avoid ambiguous ties and restrict automatic defaults to the intended idle/pose/offset naming patterns.
- Preserve manual hand-pose and IK edits while applying automatic defaults. Default hand fields are ordered before IK inference, refresh after source changes, and are cleared safely when resources are removed.
- Fix animation-resource deletion when the list selection changes during collection removal. Dual-wield references, automatic-default subscriptions and stale selection state are cleaned before choosing the next item; deleting the final animation now leaves a valid empty selection.
- Keep the effective unified output directory visible in the inspector, harden paste/drop race handling, and retain editable path fields and trailing filename visibility.
- Add regression coverage for 60-to-30 FPS duration and midpoint sampling, output collisions and source immutability, CAST-only rejection, resource deletion, variant matching, clipboard/picker races, UI synchronization and AOT startup.

## 1.3.0-preview.22 — 2026-09-09 (Ubuntu GTK/Yaru control templates)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.22.zh-CN.md).

- Add independent Avalonia GTK/Yaru-inspired control templates via the `ubuntu-yaru` custom theme base.
- Add 13 Gtk material tokens and bundle light/dark Ubuntu theme data with 34 original PNG icons.
- Verify imported theme reload, button/checkbox/switch states, four pages and isolation from built-in skins.
- Preserve existing theme format compatibility, attachment fixes and animation/IK behavior.

## 1.3.0-preview.21 — 2026-09-09 (attachment mount frames)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.21.zh-CN.md).

- Fix automatic attachment parenting: reuse an equivalent bind frame, otherwise attach below the matching weapon mount.
- Preserve explicit parent selection, existing bone reuse and missing/ambiguous mount validation.
- Add regression coverage for transform differences, equivalent quaternions and exported attachment geometry.

## 1.3.0-preview.20 — 2026-09-09 (classic desktop theme and shared-base batches)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.20.zh-CN.md).

- Fix caption buttons overflowing into the command toolbar without reducing their hit targets.
- Add a Windows 2000 classic desktop theme with 34 pixel-grid icons, beveled controls and light/dark support.
- Create independent overlay tasks sharing one base animation, preserving settings and avoiding duplicate output names.
- Retain the sprint batch action in the new batch menu.

## 1.3.0-preview.19 — 2026-09-09 (inspector visibility and UI details)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.19.zh-CN.md).

- Merge the latest preview.18 updates, retaining shortcuts, updater, output defaults and stable inspector selections.
- Expand and reveal selected layer properties after the inspector has been scrolled.
- Preserve filename suffixes with middle ellipsis in asset lists.
- Keep default output names synchronized with the first animation layer across renaming, replacement, reordering and project reloads, preserving custom names.
- Bind the About badge to the current version and give inspector headers localized accessible names.


## 1.3.0-preview.18 — 2026-09-08 (inspector and selection stability)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.18.zh-CN.md).

- Fixed clipped field borders in the inspector scroll area.
- Preserve layer, model, dual-mode and weapon-follow selections during language/theme refresh.
- Weapon follow remains off by default; withdrawn grip-IK inference and limb-stretch changes are not included.

## 1.3.0-preview.17 — 2026-09-08 (shortcuts and output defaults)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.17.zh-CN.md).

- Added project, import, preview, page navigation and focused-list keyboard commands, with an F1 reference and text-field/dialog guards; reordering keeps the selected item.
- Default output names now use the first imported animation layer's filename stem, preserving custom names and later imports.
- Added a persistent unified output folder for animation and dual-wield exports, without modifying project paths or preview caches.
- Localized native Cut/Copy/Paste menus, including output paths, with live language switching in all three built-in themes.
- Centered the localized product name at the top of the window with a separate command row.

## 1.3.0-preview.16 — 2026-09-08 (utilities and updates)

Chinese release notes: [更新日志](docs/releases/1.3.0-preview.16.zh-CN.md). Built on the latest merged GitHub changes, including desktop-shell consolidation at `90f5e08`.

- Remember the first imported arms model by default and reuse its path at startup and in new projects, with disable, manual selection and forget controls; existing projects are unchanged.
- Added opt-in GitHub update checks and verified downloads, restricted to the installed stable or preview channel. Installation requires saving the current project and confirming restart; users can decline an update.
- Moved appearance settings below Utilities.
- Older builds do not contain the updater: users must install preview.16 manually once before receiving future in-app updates.

## 1.3.0-preview.15 — 2026-09-08 (custom appearance)

Chinese release notes, compared with published preview.13 (`b76e996`): [更新日志](docs/releases/1.3.0-preview.15.zh-CN.md). The local historical preview.14 notes below are retained unchanged.

- Added an independent Windows XP theme with 34 matching vector icons and themed controls, preserving all existing themes.
- Completely redrew Classic Apple controls and all 34 functional icons in a dedicated platinum-light/graphite-dark skin.
- Added a one-click light/dark appearance switch with saved preferences.
- Added custom JSON theme and PNG icon ZIP imports from Settings, with restoration of the built-in appearance and bundled editable templates.
- Fixed playback slider thumb clipping at the start, middle and end of the track.

## 1.3.0-preview.13 — 2026-09-08 (ez4cywa AOT integration)

Simple Chinese update log, compared with target branch commit `c817e47`: [更新日志](docs/releases/1.3.0-preview.13.zh-CN.md).

- Added dual-wield composition, attached and combined-weapon model modes, optional companion model export, and hand-follow with preserved grip offset.
- Added clearer timing errors, Apple/neumorphic themes, consistent controls and icons, and resizable dual-wield panes.
- Added an independent graphite/platinum Classic Apple theme with 34 monoline vector icons, safe icon margins and centered single-line control text; retained both existing themes and preferences.
- Hardened both About links with browser-launch fallback and a copyable URL on failure.
- Added Blender FBX conversion while retaining Maya support and the target repository's About-page link.
- Integrated the final local UI branch and unified the application, engine and build scripts at preview.13. Earlier entries below retain the local development numbering as historical records.

## Earlier local development history

## 1.3.0-preview.14 — 2026-09-07

Consolidated Chinese release notes covering preview.12–preview.14: [release notes](docs/releases/1.3.0-preview.14.zh-CN.md). The requested download archive is named `AlchemyStars-1.3.0-preview.11-win-x64.zip`; its actual application version is preview.14.

- Added draggable splitters on both sides of the dual-wield preview, matching the animation and model-parts editors, including arrow-key resizing and hover/focus feedback.
- Included dual-wield panes in window-width clamping and Restore layout so widening a list cannot leave the preview inaccessible after shrinking the window.

## 1.3.0-preview.13 — 2026-09-07

- Added a per-dual-task “Export weapon models” switch, enabled by default and saved with the project. It writes a separate `_model.cast` containing hands, both complete weapon instances, skin weights and one skeleton without animation, independently of animation format and animation-only CAST settings.
- Included companion paths in export results, source-overwrite protection and batch collision detection. Preview never writes companion files.
- Added checks for both weapon mesh sets, switch persistence, enabled/disabled export, animation-only CAST, SMD/SEAnim companions and Blender model-then-animation import.

## 1.3.0-preview.12 — 2026-09-07

- Added a Dual wield sidebar page with source-task pairing, editable mount names, composition preview, selected/batch export and persistent project references.
- Added native attached-mode composition from one hands model and one weapon model. Both source tasks run the existing pose/layer/IK pipeline before per-frame mount solving; two weapon instances share one output skeleton and one baked animation.
- Added explicit validation for source references, matching timing, scale curves, output collisions and missing inputs; report unmapped source targets.
- Added Blender background FBX conversion, preferring installed Blender 4.3, while retaining the Maya backend and explicit backend selection.
- Bundled a pinned MIT Blender CAST importer with a complete-scene animation-binding fix. Added Scarab engine and Blender pose verification; see `docs/dual-wield.zh-CN.md` for workflow and limits.

## 1.3.0-preview.11 — 2026-09-06

- Added structural CAST model-part detection for view hands, weapons and attachments. Complete bilateral arm chains take precedence over helper `j_gun` and weapon tags, while weapon/attachment roots and mechanism topology drive the remaining recommendations.
- Applied detection consistently to file-picker imports, model-list drops, path-field drops, and typed/pasted paths when the field loses focus; parsing runs off the UI thread for interactive paths.
- Added localized confidence and evidence feedback in the model inspector. Suggested types and `tag_weapon` parenting remain editable, and manual overrides are visibly preserved.
- Kept detection metadata transient so existing `.aprj` files are neither reclassified on load nor changed on disk.
- Added self-contained topology fixtures and real Hawk checks for hands/weapon classification before the Native AOT export regression.

## 1.3.0-preview.10 — 2026-09-06

- Replaced flat face shading in CAST preview with skinned vertex normals, dual-light clay shading, controlled highlights and a subtle rim light. Legacy meshes without normals receive deterministic area-weighted smooth normals.
- Moved interactive CAST rasterization to an Avalonia/Skia custom draw operation so the desktop backend submits shaded triangle vertices to the GPU. The deterministic CPU renderer remains available for headless regression and software fallback verification.
- Added 2-sample multisample anti-aliasing to the deterministic CPU fallback and Skia anti-aliasing to the interactive preview.
- Rebuilt playback as a compact Premiere-inspired two-tier control: an adaptive frame ruler sits above centered previous/play/next controls, with the current/total frame readout anchored at the right.
- Fixed first-person previews whose weapon crossed the Maya-origin camera and was cut by the viewport. A preview-only whole-scene fit keeps the subject visible without changing CAST transforms or exports.
- Reduced preview command chrome to 40 DIPs and 17-DIP glyphs, tightened status/badge spacing, and unified workspace, menu and tooltip typography across English and Chinese.
- Added a new optimistic Alchemy Stars icon with a strong small-size silhouette: a sunrise-gold star emerges from a sky-blue alchemy flask while a single orbit represents animation composition. Avalonia uses dedicated transparent PNG and multi-resolution ICO assets; the stable WPF icon remains unchanged.

## 1.3.0-preview.9 — 2026-09-06

- Added a fixed first-person CAST preview camera matching Maya transform `T(0,0,0)`, `R(90°,0°,-90°)`, with a 90° horizontal FOV and 0.1 near clip. The camera toolbar toggle, `1` shortcut, localized state badge and accessibility name keep orbit and first-person modes explicit.
- Made animation-track bars read each source CAST asynchronously and scale to its true frame count. The shared frame range also reflects positive and negative layer offsets; frame-count labels, localized tooltips and UI Automation names supplement the visual length encoding.
- Set merged skinned CAST meshes to Dual Quaternion (`quaternion`) skinning. The bundled Maya importer now defaults older meshes without an explicit skinning method to DQS while continuing to honor explicit methods.
- Added pure layout, canonical Hawk metadata, Native AOT UI Automation geometry and merged-output DQS regressions. The standard Hawk project visibly resolves to 1-frame idle, 67-frame sprint-loop and 1-frame additive-offset tracks.

## 1.3.0-preview.8 — 2026-09-06

- Localized every workspace context-menu command through the active UI language. Animation, animation-layer, model-part and CAST framing menus now show Chinese or English exclusively and update with the global language switch.
- Added localization contract checks so bilingual hard-coded menu labels cannot silently return.

## 1.3.0-preview.7 — 2026-09-06

- Rebuilt the supplied reference's panel geometry: a 48 DIP activity rail, resizable library/viewport/inspector, a full-width lower layer area, and flat collapsible properties.
- Replaced project-toolbar glyphs with distinct, padded 24 DIP vectors while retaining 44 DIP hit targets and localized accessibility names; render smoke checks include stroke-bound clipping assertions.
- Added a real read-only merged CAST preview, backed by the existing exporter and sampler: clay-shaded meshes, skinned animation, playback/frame scrubbing, orbit/zoom, subject/all-geometry framing and skeleton overlay. Curve-only CAST can use the matching current-project skeleton, with an explicit bind-pose compatibility notice.
- Build-preview caches are uniquely scoped and cleaned after loading; successful CAST exports load the selected output automatically. Preview work runs off the UI thread and does not modify source assets, output settings or the conversion algorithm.
- The interrupted frame-ruler experiment was withdrawn. Layer bars describe composition order, while the preview's frame slider reads the loaded CAST's actual animation.
- Kept this work on the Avalonia Native AOT test branch; no stable release or GitHub publication is included.

## 1.3.0-preview.6 — 2026-09-05

- Rebuilt the Avalonia workspace from the supplied Beutl editor reference: project breadcrumbs, a compact activity rail, asset library, central composition canvas, animation-layer tracks and a dedicated property inspector.
- Adapted the reference to real Alchemy Stars workflows instead of adding placeholder playback or 3D controls; animation-layer drop priority, right-click import, editable paths and all export settings remain functional.
- Applied the same library/canvas/inspector grammar to model assembly and retained 44 DIP command targets, keyboard focus, screen-reader names and owner-centered dialogs.

## 1.3.0-preview.5 — 2026-09-05

- Reworked the Avalonia shell into a Beutl-inspired creative workbench: compact activity rail, flat dock-like asset and property panels, low-noise dividers, active-tab accent lines and a full-width status strip.
- Kept AtomBox-informed 20/14 DIP form spacing, compact lists and restrained control surfaces while avoiding new AtomUI, Beutl or Dock.Avalonia dependencies in the Native AOT package.
- Raised essential input/control boundaries to 3:1 non-text contrast, retained 44 DIP command targets, localized UI Automation names, visible keyboard focus and centered window-owned dialogs.

## 1.3.0-preview.4 — 2026-09-05

- Introduced a resource-list/inspector workspace and consolidated shared visual tokens while evaluating the Lunacy and AtomBox references.
- Added page-level project context, compact command actions and scroll-safe settings/About layouts without changing the export workflow.

## 1.3.0-preview.3 — 2026-09-05

- Replaced ambiguous filled toolbar glyphs with a consistent 20 DIP, 1.8 DIP rounded-stroke icon family for New, Open, Save, Save As, Export and primary navigation.
- Preserved 44 × 44 DIP command targets, visible tooltips and localized UI Automation names so the icon-only commands remain keyboard- and screen-reader-accessible.
- Added an external Windows UI Automation smoke test that verifies required button names, keyboard focus, centered-dialog focus and minimum key-target bounds against the published Native AOT executable.

## 1.3.0-preview.2 — 2026-09-05

- Completed the Avalonia vertical workflow: legacy-compatible project open/save, model parts, base animations, poses, ordered layers, IK, output settings, remembered directories and batch export.
- Added source-generated AOT-safe JSON metadata for existing `.aprj` files, including old `$id` / `$values` reference-preserved projects.
- Added system file dialogs, editable and pasteable path fields, direct file drops, blank-area right-click imports and a dedicated animation-layer drop target that takes priority over its parent page.
- Kept new output folders blank and added a final engine guard that rejects any output path matching a model, animation, layer or pose input.
- Added full-scene and animation-only CAST, FBX, SMD and SEAnim controls, selective-bone baking, system/Chinese/English language modes, centered in-window progress/result dialogs and an unclipped About page.
- Extended acceptance coverage to 32 checks and added an actual Native AOT export from the standard Hawk project. The managed and AOT CAST files remain byte-identical.

## 1.3.0-preview.1 — 2026-09-05

- Added a test-only Avalonia 12.1.2 desktop shell that publishes as a 38 MB-class self-contained Native AOT package on .NET 11 Preview 7, including Skia, HarfBuzz and ANGLE rendering libraries.
- Introduced the WPF-free `IAnimationExportEngine` seam, immutable export requests, capability discovery, and structured validation errors.
- Reused the single proven conversion implementation across the WPF and AOT adapters, retaining CAST, FBX, SMD, SEAnim, IK, animation layers, selective baking, and animation-only CAST.
- Added a Native AOT contract self-test, deterministic real-window startup regression, off-screen UI rendering, and direct AOT Hawk export verification.
- Verified the new engine against the standard Hawk idle-plus-two-additive-layers recipe; Native AOT and managed CAST outputs are byte-identical.
- Kept the existing WPF application and production release line unchanged. This preview is a migration milestone, not a stable release.

## 1.2.0-preview.1 — 2026-09-05

- Created an isolated .NET 11 Preview test line; the production `main` branch and v1.1.9 release remain on .NET 9.
- Retargeted Alchemy Stars and its acceptance tests to `net11.0`; the pinned upstream RedFox submodule remains untouched and runs as a compatible .NET 9 library on the .NET 11 runtime.
- Pinned .NET SDK `11.0.100-preview.7.26381.103` with preview opt-in for reproducible testing.
- Display the full informational version so test builds are visibly identified as `1.2.0-preview.1`.
- Keep the preview package framework-dependent after .NET 11 disallowed compressed framework-dependent single-file bundles; ZIP distribution remains compressed and no runtime is bundled.
- Create validated archive subdirectories on first use so preview packages can remain separated from stable release assets.
- This branch is not a production release. It will move to .NET 11 GA only after Microsoft publishes the final SDK and the full Maya 2025 regression suite passes.

## 1.1.9 — 2026-09-05

- Fixed the partially clipped About icon with explicit icon sizing and padding. The toolbar now overflows before reaching the protected language/About controls.
- Improved dark-theme icon and format-selection contrast, restored themed text inputs, and added accessible names to inline icon buttons.
- Reflowed animation layers into separate path and operation rows. Animation and model columns adapt to window width; narrow windows scroll instead of crushing controls.
- Arranged output formats in a readable two-column grid and kept settings, About, and message dialogs within their owner window, with scrollable content and fixed actions.
- Added bilingual empty-list import hints, full-path tooltips, and a text-editing shortcut guard. Corrected the export tooltip to describe the existing whole-project export behavior.
- Added real-WPF layout regression checks and renders for Chinese/English at 900, 1100, 1366, and 1920 DIPs, including icon bounds, dialog content, dropdowns, and keyboard-editing isolation.

## 1.1.8 — 2026-09-05

- Fixed weapon offsets caused by collapsing a weapon-root `j_gun` into the view-hands wrist helper. Bone reuse now requires a matching parent and bind transform; distinct bones receive stable unique names.
- Sampling, CAST bones, mesh weights and hash references now use one merge plan. The weapon root `j_gun__weapon` follows `tag_weapon` while hand animation still targets the wrist helper `j_gun`.
- Empty weapon parents resolve to a unique `tag_weapon`; missing or ambiguous parents/animation targets produce actionable errors. Explicit parents remain honored.
- Corrected the Hawk examples to use `tag_weapon`. Re-export old merged scenes; animation-only files require the matching new skeleton.
- Corrected rigid mesh alignment for rotated, non-zero source roots and improved SMD Euler conversion precision near 90-degree pitch.
- Added 1911/P27/Hawk regression checks for weapon ancestry, skin weights and world-space playback, including separate-source Maya comparisons and full/selective/animation-only CAST, FBX and SMD.
- Updated English/Chinese guides and About content. The original MP5 example files remain unchanged.

## 1.1.7 — 2026-09-05

- Left the output folder blank for every newly imported animation, requiring an explicit destination before export so a same-named CAST cannot silently overwrite its source.
- Kept explicitly selected output folders stable when replacing an animation source and preserved saved destinations when loading existing projects.
- Added focused acceptance coverage for new imports, source replacement, and project round trips.

## 1.1.6 — 2026-09-05

- Added direct Explorer file drops to every editable source-path field; all file drops targeted at a field—including rejected types—stop there instead of bubbling into the surrounding animation, layer, or model import list.
- Output-folder fields accept either a folder or a file (using its containing folder), and successful field drops update the corresponding remembered directory.
- Added acceptance coverage for CAST filtering, folder resolution, and quoted clipboard paths.

## 1.1.5 — 2026-09-04

- Restored direct editing and clipboard paste support for animation, pose, animation-layer, model-part, and output-folder paths while retaining every file-browser button.
- Trimmed surrounding whitespace and matching quotes on focus loss, so paths copied with Windows **Copy as path** are accepted without manual cleanup.
- Added UI automation coverage for editable, keyboard-focusable, screen-reader-named path fields and quoted-path normalization.

## 1.1.4 — 2026-09-04

- Replaced the narrow output-format drop-down with four direct, keyboard-focusable radio options so the popup can no longer detach, shrink, or cover help text.
- Reflowed output settings into a responsive two-column card layout and increased the adaptive dialog height; scrolling now appears only when the window or localized content genuinely needs it.
- Extended UI automation to verify all four format choices, focusability, minimum target height, dialog containment, and the non-overlapping IK fields.

## 1.1.3 — 2026-09-04

- Redesigned the Output and IK settings dialog with grouped cards, explicit field labels, consistent spacing, and scroll-safe layout.
- Removed overlapping floating IK labels and added unique accessibility names plus predictable keyboard order for all eight IK fields.
- Expanded UI smoke coverage for label/input separation, minimum input height, bilingual content, and output-option toggles.

## 1.1.2 — 2026-09-04

- Added an optional relevant-bones-only bake for CAST, FBX, and SEAnim pipelines.
- Retains all base-animation, pose, layer, IK-chain, and indirectly changed bones; unknown solvers safely fall back to a full bake.
- Keeps compatibility mode as the default and documents the matching-bind-pose requirement for animation-only imports.
- Added full-versus-selective curve comparison and separate Maya 2025 validation; Hawk sprint retains 121 of 214 bones.
- Added global and project-level persistence plus bilingual, accessible settings UI.

## 1.1.1 — 2026-09-04

- Added optional animation-only CAST output containing one merged, baked animation without model geometry, materials, or skinning.
- Preserved full-scene CAST as the default and stored the option globally and in `.aprj` projects.
- Added animation-only CAST structure and weapon-curve acceptance coverage.

## 1.1.0 — 2026-09-04

Compared with upstream Alchemist, this release keeps its animation-layer and RedFox pipeline while adding a verified single-skeleton Maya package, direct SMD, Maya-backed FBX, safer import UX, persistent folders, and a complete bilingual release workflow.

- Added real FBX export through the locally installed Autodesk Maya FBX plug-in.
- Added native SMD animation export with complete skeleton hierarchy and per-frame transforms.
- Added `.cast`, `.fbx`, `.smd`, and `.seanim` default-format selection.
- Added automatic system-language detection plus Follow System, Simplified Chinese, and English choices.
- Added persistent recent folders for animations, layers, models, projects, and output.
- Redesigned the settings dialog to prevent clipping and protected the language/About controls from toolbar overlap.
- Updated About content and Hawk validation for CAST, FBX, SMD, skinning, and weapon motion.
- Kept the Windows x64 package framework-dependent; no .NET or Maya runtime is bundled.

## 1.0.3

- Redesigned toolbar icons to match their actions.
- Prioritized external file drops over the hovered animation-layer region.

## 1.0.2

- Fixed merged Maya animation output, including weapon animation and single-skeleton import.

## 1.0.0

- Initial Alchemy Stars release based on Scobalula/Alchemist.
