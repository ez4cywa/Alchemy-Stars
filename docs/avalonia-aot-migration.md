# Avalonia + Native AOT migration

Status: complete test implementation on `codex/avalonia-aot`; do not merge into `main` or publish as a stable release before .NET 11 GA.

Version: `1.3.0-preview.16` (Native AOT preview)
Runtime: .NET 11 Preview 7
UI framework: Avalonia 12.1.2
Production baseline: WPF v1.1.9 on `main`

## Result

The test branch now contains the complete Alchemy Stars desktop workflow rather than a migration shell:

- create, open, save and save-as legacy-compatible `.aprj` projects;
- add, edit, reorder and remove view-hand, weapon and attachment model parts;
- add base animations, optional hand poses and ordered Normal/Additive/Gesture/GesturePose layers;
- configure left/right IK, frame rate, output names and an explicitly selected output folder;
- export full-scene or animation-only CAST, FBX, SMD and SEAnim through the existing proven conversion implementation;
- choose full or relevant-bones-only baking, old Call of Duty compatibility, prefixes and suffixes;
- remember per-purpose file-browser locations and follow system, Chinese or English UI language;
- use file browsers, editable/pasteable paths, direct path drops, list drop zones and blank-area context menus;
- keep progress, success and error feedback centered inside the owning window;
- display the actual merged CAST with clay-shaded skinning, frame playback/scrubbing, orbit/zoom and skeleton-only support;
- display source animation layers on a shared frame range, with bar widths driven by real CAST duration metadata and positions driven by layer offsets;
- emit DQS as the merged CAST skinning default and use the same default for legacy unlabelled meshes in the bundled Maya importer.

New animation output folders stay blank. The engine also rejects an output that resolves to any model, animation, pose or layer input, so a same-named export cannot overwrite source data.

## Architecture

```text
Avalonia views and adapters
        │
        ├── WorkspaceProjectStore + source-generated JSON metadata
        ├── ApplicationPreferencesStore
        └── IAnimationExportEngine
                    │
                    └── shared Alchemist/RedFox conversion implementation

WPF production view models ─────────┘
```

The UI layer owns presentation, localization, file pickers and drag/drop. The engine owns skeleton identity, model composition, animation sampling, IK, validation and output formats. Legacy JSON uses source-generated `System.Text.Json` metadata, including the old `$id` / `$values` shape, so Native AOT does not depend on runtime reflection.

## Accessibility and visual behavior

- The minimum supported window is 900 × 600 DIP; each page scrolls vertically without horizontal clipping.
- The main shell adapts the supplied Beutl editor reference into a compact activity rail, project breadcrumb, asset library, composition canvas, functional animation-layer tracks, property inspector and 24 DIP full-width status strip. The 48 DIP activity rail, full-width lower layer area, resizable dividers and flat collapsible inspector follow the supplied reference.
- Primary actions retain at least 44 DIP command targets and visible focus rings. Icon-only navigation has localized tooltips and UI Automation names, with the page title visible in the breadcrumb.
- Icon-only toolbar commands use 24 DIP fixed canvases with inset rounded strokes, localized tooltips and UI Automation names.
- `Ctrl+O`, `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+E` and `Esc` cover project/export/dialog actions.
- Every drag or reorder workflow also has a file-browser, context-menu or button alternative.
- Windows UI Automation checks the published native executable for required names, keyboard focus, 44 × 44 key targets, initial dialog focus, localized track descriptions and visibly different 1/67-frame bar widths.
- The About icon is displayed inside a padded 112 × 112 region and is not clipped.
- No decorative animation is used; CAST playback is explicitly user-controlled and pauses when leaving the preview pages or opening a dialog.

## Verification

Completed on 2026-09-06:

- Release solution build: 0 warnings and 0 errors, excluding the expected preview-support notice.
- Acceptance suite: all 33 checks passed, including true track durations, DQS skinning metadata, CAST, animation-only CAST, selective baking, SMD, Maya-backed FBX, the workspace store and source-overwrite prevention.
- Native AOT self-test, real Win32 startup, Windows UI Automation and four-page off-screen rendering: passed.
- Native AOT standard `HawkSprint.aprj` export: passed.
- CAST preview regression: visible geometry, distinct sampled frames, deterministic reverse scrubbing, complete first-person viewport bounds and unchanged input SHA-256. Interactive triangles use the Avalonia/Skia custom drawing path; the CPU path remains deterministic for headless verification. Animation-only files also pass using the canonical project's matching skeleton. Preview uses a separate scene and does not alter the export pipeline.
- Native AOT and managed Hawk CAST outputs are byte-identical: SHA-256 `B8D9476C71F4483668AB0444770DF9B4B8FF5E4CCFC4B24668A6B7B2EE995504`.
- The clean `win-x64` publish is 4 files / 38.39 MiB (40,251,944 bytes). The application executable is 21,412,864 bytes; PDB files are rejected.

Run the complete native verification with the pinned preview SDK:

```powershell
./scripts/verify-avalonia-aot.ps1
```

The standard-project path can also be exercised directly:

```powershell
./output/avalonia-aot-preview16/AlchemyStars.Avalonia.exe --project-smoke ./fork/AlchemyStars/Example/Hawk/HawkSprint.aprj <output-folder>
```

## Native AOT notes

- Avalonia 12.1.2 currently emits two DirectComposition ILC diagnostics about non-blittable `bool` callbacks. Real Win32 startup, rendering and UI Automation pass; those executable checks remain release gates.
- Runtime assets use explicit `avares://` URIs and compiled bindings so trimming preserves the required UI metadata.
- The published app is self-contained and does not require a separate .NET runtime. Maya is not bundled and remains an external requirement for FBX conversion.
- Build telemetry is disabled in verification to keep restricted and CI runs deterministic.

## Remaining production gate

No implementation phase remains on the test branch. Production migration waits only for the final .NET 11 SDK, an updated dependency audit, a repeat of all 33 engine/Maya checks, Native AOT startup/UI Automation checks on the GA runtime, and explicit approval to merge. Until then, WPF v1.1.9 remains the supported release.
