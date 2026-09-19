[**English**](README.md) | [简体中文](README.zh-CN.md)

# Alchemy Stars

**A Windows desktop toolkit for first-person weapon animation and CAST model workflows.**

Blend animation layers, solve hand IK, assemble model parts and export assets for Maya or Blender. The Avalonia preview also includes grouped model merging, ammunition filling, independent model previews and a COD weapon database.

## Choose your release

| Channel | Download | Source | Run after extracting |
| --- | --- | --- | --- |
| Stable · 1.1.9 · WPF | [Stable release](https://github.com/ez4cywa/Alchemy-Stars/releases/tag/v1.1.9) | [`main`](https://github.com/ez4cywa/Alchemy-Stars/tree/main) | `Alchemy Stars.exe` |
| Preview · 1.3.0-preview.31 · Avalonia / Native AOT | [Preview release](https://github.com/ez4cywa/Alchemy-Stars/releases/tag/v1.3.0-preview.31) | [`codex/avalonia-aot`](https://github.com/ez4cywa/Alchemy-Stars/tree/codex/avalonia-aot) | `AlchemyStars.Avalonia.exe` |

The preview is a Windows x64 self-contained package: keep its native DLLs and `Converters` folder together; no .NET or Rust installation is required. FBX conversion requires a local Blender or Maya installation. Preview features are **not** included in the stable executable or the source on `main`.

## Preview workspace

| Workspace | What it does |
| --- | --- |
| Animation blend | Animation layers, hand poses, IK, configurable frame rate and separate bound-model export |
| Model parts | Classifies hands, weapons and attachments; preserves skeleton relationships and skin weights |
| Dual-wield composition | Combines left/right animation tasks and exports paired weapon models |
| Model merger · Ctrl+7 | ModelMergerGUI 2.2.2 core; 2–15 parts per group, automatic/manual root, two concurrent tasks, progress, cancellation and overwrite confirmation |
| Ammunition filling | Magazine/slot inspection, opt-in extra slots and spare magazine replication; skips occupied slots and writes a new CAST |
| Independent model preview | Multiple read-only windows, orbit/zoom/grid controls, u32 indices, up to 250,000 displayed triangles and software depth testing |
| COD weapon database | Offline weapon/codename and blueprint lookup, online database updates and optional Wiki reference icons |

The merger module supports Chinese, English, French, Russian and Spanish; the host interface supports Chinese and English. Preview sampling does not alter exported geometry. Existing Unity projects are not modified; export through the documented CAST/FBX workflow rather than treating CAST as a native Unity format.

[Preview quick guide](https://github.com/ez4cywa/Alchemy-Stars/blob/codex/avalonia-aot/docs/avalonia-aot-user-guide.md) · [Model merger guide / 功能对照](https://github.com/ez4cywa/Alchemy-Stars/blob/codex/avalonia-aot/docs/model-merger.zh-CN.md) · [Release notes and validation](https://github.com/ez4cywa/Alchemy-Stars/blob/codex/avalonia-aot/docs/releases/1.3.0-preview.31.zh-CN.md) · [Report an issue](https://github.com/ez4cywa/Alchemy-Stars/issues)

## Stable branch background

The sections below describe the stable 1.1.9 workflow. Use the preview guide above for the Avalonia interface and its newer features.

Alchemy Stars (炼金之星) is a production-focused improvement of [Scobalula/Alchemist](https://github.com/Scobalula/Alchemist) for Windows, first-person CAST weapon assets, and Autodesk Maya 2025. It retains Alchemist's WPF batch interface and RedFox animation pipeline while completing a reliable model-and-animation export workflow.

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
| UI and distribution | Original settings layout and icons | Purpose-specific icons, unclipped tabbed settings, protected language/About controls, and a compact framework-dependent package |
| Regression validation | Upstream examples | Preserves the original MP5 examples byte-for-byte and validates CAST, FBX, SMD, IK, skinning, and weapon motion with real Hawk, 1911 and P27 assets |

Alchemy Stars keeps the upstream animation-layer concepts intact. Attribution and licenses for Alchemist, RedFox, and the CAST components are included in every release package.

## Highlights

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

Development requires the .NET 9 SDK. The compact Windows x64 release is framework-dependent and requires the [.NET 9 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/9.0); it does not bundle .NET or Maya.

```powershell
.\scripts\run-tests.ps1
.\scripts\build-release.ps1
```

`run-tests.ps1` builds the improved upstream project, verifies that the standard MP5 examples were not modified, generates actual Hawk CAST/SMD/FBX outputs, and reimports full and relevant-bone CAST plus FBX into Maya 2025 when available. It checks the skeleton, meshes, skinning, frame range, IK, weapon animation, animation-only CAST contents, retained-curve equivalence, weapon-first model ordering, and Chinese TEMP/output paths and names. The UI smoke suite checks centered dialogs, the protected language/About layout, settings clipping, four format choices, output-option accessibility, three language modes, toolbar controls, and context imports.

Every functional release change increments at least the patch version; this version is `1.1.9`.

The 1.1.9 UI audit fixes the clipped About icon, toolbar overflow, cramped layer paths, and low-contrast controls. See the [UI audit and validation notes](design-system/alchemy-stars/pages/ui-audit-1.1.9.md) for coverage and limitations.

## Source and licenses

- Improved Alchemist source: `fork/AlchemyStars`, GPL-3.0; see `fork/AlchemyStars/LICENSE`.
- Pinned RedFox revision: `fork/RedFox` Git submodule.
- Maya CAST plug-in: `third_party/cast`, MIT; see `THIRD_PARTY_NOTICES.md`.

Upstream baseline: Alchemist `d86da66536ed3bf304a5cb7142d360fb934f73fb`; RedFox `7031da79614d1d979b1f17cae9d4bda2c699fd53`.
