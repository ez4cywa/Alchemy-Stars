[**English**](README.md) | [简体中文](README.zh-CN.md)

# Alchemy Stars

> **Avalonia AOT preview branch:** this test line targets .NET 11 Preview 7, Avalonia 12.1.2 and version `1.3.0-preview.15`. The complete desktop workflow now runs as a self-contained Native AOT application. Production remains v1.1.9 on `main`; the existing WPF application is still the stable baseline until .NET 11 GA. Do not distribute this build as a stable release.


Latest changes: [preview.15 update notes (Chinese)](docs/releases/1.3.0-preview.15.zh-CN.md), including Windows XP styling, a light/dark toggle, custom theme/icon imports and the playback slider fix.
See the [Avalonia preview quick guide](docs/avalonia-aot-user-guide.md), [migration report](docs/avalonia-aot-migration.md) and [.NET 11 preview compatibility report](docs/dotnet11-preview.md).

This preview adds attached dual-wield composition and Blender 4.3 FBX conversion. See the [dual-wield workflow and current limits (Chinese)](docs/dual-wield.zh-CN.md).

Alchemy Stars (炼金之星) is a production-focused improvement of [Scobalula/Alchemist](https://github.com/Scobalula/Alchemist) for Windows, first-person CAST weapon assets, and Autodesk Maya 2025. The stable line retains Alchemist's WPF interface, while this test branch moves the complete workflow to Avalonia and Native AOT without forking the proven RedFox animation pipeline.

The maintained source lives in `fork/AlchemyStars` and pins the matching RedFox revision to keep builds reproducible. The earlier standalone rewrite remains preserved on the `independent-rewrite-v1` branch and is no longer the active implementation.

## Improvements over upstream Alchemist

| Area | Upstream Alchemist | Alchemy Stars 1.1.9 |
| --- | --- | --- |
| Maya model and animation | Import behavior can leave duplicate skeletons or lose weapon motion | Uses hierarchy-aware bone identities, remaps skin weights, and emits one baked animation per file |
| Output formats | Primarily CAST / SEAnim | Adds real FBX and native SMD while retaining CAST / SEAnim |
| FBX workflow | Not provided | Detects the local Maya installation and uses the official `fbxmaya` plug-in without bundling a large conversion runtime |
| Asset import | Primarily the original UI controls | System file dialogs, editable/pasteable path fields, targeted path drops, blank-area context menus, and `Shift+F10`; drops over animation layers are routed there first |
| Localization | Original UI capability | Follows the system language by default, can be pinned to Simplified Chinese or English, and refreshes open About content |
| Continuity | Project files retain absolute paths | Also remembers recent animation, layer, model, project, and output folders by category |
| UI and distribution | Original settings layout and icons | Reference-driven creative workbench with an asset library, composition canvas, layer tracks and inspector; AtomBox-informed forms and lists; accessible rounded-stroke icons; centered in-window dialogs; and an optional self-contained Native AOT package |
| Regression validation | Upstream examples | Preserves the original MP5 examples byte-for-byte and validates CAST, FBX, SMD, IK, skinning, and weapon motion with real Hawk, 1911 and P27 assets |

Alchemy Stars keeps the upstream animation-layer concepts intact. Attribution and licenses for Alchemist, RedFox, and the CAST components are included in every release package.

## Highlights

- Model-part imports inspect CAST skeleton topology to distinguish view hands, weapons and attachments, recommend `tag_weapon` parenting, show confidence/evidence, and remain manually overridable. Filenames are only a weak tie-breaker, so a hand rig containing helper `j_gun` is not mistaken for a weapon.
- Preserves distinct same-name bones: the wrist helper remains `j_gun`, while the weapon root becomes `j_gun__weapon` under `tag_weapon`. Model, skin and animation exports share one mapping.
- Normalizes model order as ViewHands → Weapon → Attachment and physically combines all parts before export.
- Keeps every mesh, material, and remapped skin weight in each CAST while including exactly one selected baked animation.
- Preserves Normal, Additive, Gesture, GesturePose, positive offsets, and negative offsets through the RedFox sampling pipeline.
- Rejects cyclic IK targets and fixes two-bone IK and animation-clone state loss.
- Restores layer and part ownership after project loading so reorder, remove, and drag operations remain usable.
- Supports `.cast`, `.fbx`, `.smd`, and `.seanim`; SMD contains the full skeleton and per-frame local transforms, while FBX preserves models, skinning, and animation through Maya.
- Optionally writes a true animation-only CAST with no model, mesh, material, or skin data; full-scene CAST remains the default.
- Optionally bakes only bones referenced by the base animation, poses, layers, or IK, while automatically retaining any indirectly changed bone and falling back to all bones for unknown solvers.
- Uses system file dialogs for animation, pose-layer, model, project, and output paths; each path box also accepts typed, pasted, or directly dropped paths and remembers the most recent folder for its category.
- Leaves the output folder blank for every newly imported animation. Export therefore requires an explicit destination and cannot silently replace a same-named source CAST; existing projects retain their saved destinations.
- Offers Follow System, Simplified Chinese, and English interface modes.
- Keeps all completion, warning, and error dialogs centered over the application; long diagnostics are scrollable and copyable.
- Uses a redesigned alchemy-flask-and-star application icon and function-specific toolbar icons.
- The Avalonia preview adapts Beutl's editor hierarchy into a functional asset-library / composition / layer-track / inspector workspace. AtomBox informs the restrained form spacing and list treatment without adding either project's UI dependencies.
- CAST preview uses Avalonia's GPU-backed Skia custom rendering for smooth skinned shading, and includes a fixed first-person camera with a 90° horizontal FOV and Maya transform `T(0,0,0)`, `R(90°,0°,-90°)`, alongside the existing orbit view. Preview-only whole-scene fitting keeps weapons fully framed without altering CAST data.
- Layer-track bars read each CAST source in the background and scale their width to its true frame count; configured positive or negative offsets move the bar on the shared frame range. Frame-count text and localized accessible names keep duration understandable without relying on length or color alone.
- Merged skinned meshes write Dual Quaternion (`quaternion`) as their CAST skinning method, and the bundled Maya importer also uses DQS when an older CAST has no explicit method.
- Persistent labels, 44 DIP targets, visible focus, localized UI Automation names, keyboard shortcuts and button alternatives cover every drop/reorder operation.

## Download and use

Download the latest ZIP from [GitHub Releases](https://github.com/ez4cywa/Alchemy-Stars/releases), extract it, and run:

`Alchemy Stars.exe`

The app starts with an empty batch. Use the toolbar buttons, the folder buttons beside path fields, or the context menus to select assets with the system file browser. Existing path boxes remain editable: type or paste a path, or drop a CAST file directly on its intended field. Dropping a file on an output-folder field uses that file's containing directory.

For safety, a newly imported animation has no default output folder. Choose, paste, type, or drop an output destination before exporting. This prevents a same-named `.cast` output from overwriting the input animation. Replacing the input animation does not repopulate the destination, while an output folder explicitly stored in an existing `.aprj` remains unchanged.

Open **Settings → Output** to choose `.cast`, `.fbx`, `.smd`, or `.seanim` as the default output format. For `.cast`, **Animation-only CAST** omits the complete model scene. **Bake relevant bones only** reduces baked curves to source-animation, pose, layer, IK, and indirectly changed bones. Both options apply immediately, are remembered globally, and are stored in project files.

Relevant-bone baking is off by default for maximum compatibility. It is safe for a full-scene export when the retained curves pass validation. When importing an animation-only CAST onto an existing rig, use the exact matching skeleton in a clean bind pose; otherwise leave this option off. SMD must still write a complete pose on every frame. FBX requires a locally installed Maya, with Maya 2025 preferred.

On the Animation page, right-click anywhere in the main list—including blank space—and choose **Import animations…**. The animation-layer area has its own **Import animation layers…** menu. When an external file is dropped over an animation-layer area, that hovered animation takes priority over the outer selection. The Model Parts list offers the same blank-area right-click workflow. All lists also support `Shift+F10`.

Each batch entry produces a separate file containing one baked animation. Project files store absolute paths; after moving to another computer, reselect the assets and output directory, then use **Save Project As**.

## Standard examples

The release directory and ZIP include the complete `Example` folder. `MP5Base.aprj` and `MP5Grip.aprj` are migrated directly from upstream Alchemist and remain byte-identical. `manifest.json` records the required files, structure, and checksums. Improved Hawk sprint, idle, and batch projects live under `Example/Hawk`.

After extracting a release, open `Example/README.en-US.md`; the Chinese guide is `Example/README.zh-CN.md`. Online copies are available in the [English example guide](https://github.com/ez4cywa/Alchemy-Stars/blob/main/fork/AlchemyStars/Example/README.en-US.md) and [Chinese example guide](https://github.com/ez4cywa/Alchemy-Stars/blob/main/fork/AlchemyStars/Example/README.zh-CN.md).

Examples are not loaded automatically. Open or drop an `.aprj` file, or pass it as a command-line argument. Future Hawk sprint checks use `Example/Hawk/HawkSprint.aprj` as the single source of truth: idle base animation, two ordered additive layers, IK, model parts, format, and output naming all come from that project.

## Maya 2025

The release `MayaPlugin` directory contains the CAST importer. Copy `cast.py` and `castplugin.py` into a Maya script or plug-in path, load `castplugin.py` in Plug-in Manager, and import the generated CAST through **File → Import**.

For FBX, Alchemy Stars locates the local Maya installation and invokes `fbxmaya`. Set `ALCHEMY_STARS_MAYAPY` to select a particular `mayapy.exe`. Conversion is isolated in an ASCII-only temporary workspace, so Chinese Windows user names, output folders, and output file names remain supported despite the Maya 2025 FBX plug-in's path limitation. Enable **Fill Timeline** when manually importing FBX into Maya.

The Hawk sprint release artifact has been tested headlessly in Maya 2025 with:

- 215 joints, one skeleton root, and separate wrist-helper and weapon-root joints;
- 21 imported and visible skinned meshes;
- 1,290 translation/rotation curves with every transform channel keyed on every frame;
- 30 FPS and a 0–66 playback range;
- left-hand IK validated against the physically reachable target;
- preserved right-hand and weapon motion, with the weapon root following `tag_weapon`.

The relevant-bone Hawk variant retains 121 of 215 bones. Every retained transform curve is compared with the full bake, every omitted target is confirmed to remain at bind pose, and the reduced full-scene CAST is imported separately in Maya 2025.

The 1.1.8 weapon regression also covers the original 1911 project and P27 ADS. It compares every joint at every frame across full, selective and animation-only CAST, independently assembled source rigs, FBX, and SMD. Source-animation references normalize quantized quaternions and sample short additive layers across the full range before Maya import; this preserves the represented rotations and the original project's persistent-offset semantics. Results are recorded in `fork/AlchemyStars/output/weapon-regression/weapon-regression.maya2025.json`.

For weapon parts with an empty parent, a unique `tag_weapon` is resolved during export; otherwise export asks for a parent. Existing explicit parents are honored. Set older Hawk projects that used `j_gun` to `tag_weapon` and re-export into a new destination. Animation-only files must be used with the matching 1.1.8 skeleton; old merged scenes need to be regenerated.

The generated report is `fork/AlchemyStars/output/sat_vm_ar_hawk_sprint_alchemy_stars.maya2025.json`.

## Build and validation

This preview branch requires .NET SDK `11.0.100-preview.7.26381.103` to build. `verify-avalonia-aot.ps1` produces a self-contained `win-x64` Native AOT package, so that test executable does not require a separately installed .NET runtime; Maya itself is still not bundled and remains required for FBX conversion. Stable users should continue using v1.1.9 from `main` with the .NET 9 Desktop Runtime until the .NET 11 GA gate is passed.

```powershell
.\scripts\run-tests.ps1
.\scripts\verify-avalonia-aot.ps1
```

`run-tests.ps1` builds the stable WPF baseline and runs the Maya-backed conversion regressions. `verify-avalonia-aot.ps1` publishes the trimmed native application, runs its AOT contract/project export checks, starts a real Win32 window, validates Windows UI Automation names/focus/target bounds, and renders all four pages plus a centered dialog at the 900 × 600 minimum size.

Every functional preview change increments the prerelease revision; this test version is `1.3.0-preview.15`. The stable release remains `1.1.9`.

The 1.1.9 UI audit fixes the clipped About icon, toolbar overflow, cramped layer paths, and low-contrast controls. See the [UI audit and validation notes](design-system/alchemy-stars/pages/ui-audit-1.1.9.md) for coverage and limitations.

## Source and licenses

- Improved Alchemist source: `fork/AlchemyStars`, GPL-3.0; see `fork/AlchemyStars/LICENSE`.
- Pinned RedFox revision: `fork/RedFox` Git submodule.
- Maya CAST plug-in: `third_party/cast`, MIT; see `THIRD_PARTY_NOTICES.md`.

Upstream baseline: Alchemist `d86da66536ed3bf304a5cb7142d360fb934f73fb`; RedFox `7031da79614d1d979b1f17cae9d4bda2c699fd53`.
