# RF adapter validation — 1.3.0-rf.1

Local verification on 2026-09-12, Windows x64, Blender 4.0.2.

## Source interpretation

- Original RF `Hands.blend`: Blender 2.77 data, 30 bones (26 deform), 148 base vertices plus Mirror/EdgeSplit, existing skin weights, no animation actions. Its three finger chains are Thumb/Index/Pinky on each side. The no-IK variant still includes targetless finger IK constraints, so the adapter removes constraints explicitly.
- COD `viewhands_mp_base_iw8_LOD0.cast`: left upper-arm length 29.28482 numeric units; `tag_view` rest height 163.3728. RF geometry is metre-sized. This supports **cm**, not interpreting each raw number as feet; the user authorized determining the numeric unit from comparison. `ft` remains an explicit supported input unit.
- Hawk hands and idle explicitly declare Z-up; the weapon has no axis metadata, but its magazine extends along raw -Z. The original RF preparation inherited the ordinary exporter's untagged Y-up default and rolled the weapon by 90 degrees. All 79,288 weapon vertices matched that unintended basis change exactly. The RF-only preparation now gives untagged weapon/attachment temporary copies the hands' axis by default, with an explicit override; tagged assets and original files are unchanged. The corrected preview has its magazine below the receiver. This source-interpretation check is separate from skinning/FBX round-trip equivalence, which cannot detect an already-wrong input pose.
- The input package's FBX screenshot specifies -Z forward/Y up, FBX All scaling, primary X/secondary -Y bone axes, deform-only bones, no leaf bones. RF export matches those settings but disables key simplification (0 instead of screenshot's 1) to preserve the calibration keys.

## Passed

- Managed build and core self-test; Native AOT publish and core self-test.
- Native 900×600 RF settings, both languages, light/dark, advanced adjustments expanded. An initial UI test helper reset the global app theme; core checks now run separately so the final dark render genuinely tests dark mode.
- RF reference ID survives two-animation save/reload, including replacing a project that already has a selected RF reference. Real ComboBox selection and its visible label are checked. Publishing a cleared selection before replacing ItemsSource prevents the control's reset from overriding a cached binding value. Ordinary animation selection stays independent, invalid/deleted reference IDs do not silently fall back, unique idle suggestion is unambiguous.
- RF-only axis preprocessing tests: untagged model follows hands, explicit X/Y/Z override, explicitly tagged attachment unchanged, original untagged source bytes unchanged, invalid choice rejected, choice persisted with the project.
- Input-path protection includes unselected animations, poses, layers, source RF file and project; a simulated failure on the third output restores all four previous outputs.
- Seven Blender contract tests: cm/m/ft physical equivalence, camera-relative yaw, orthonormal palm frames, reachable/unreachable/degenerate arm solves and invalid numeric inputs.
- Real Hawk idle end-to-end via `.unitypackage` and the Native AOT executable. Output: one root, 101 bones, 19 meshes, RF Hands 1052 evaluated vertices. Corrected-axis bone bake position error <= 1.21e-7 m, skinning error <= 6.01e-7 m.
- Independent Blender FBX reimport: actual Y-up/-Z-forward metadata, metre unit scale, matching bone hierarchy and all indexed skinned vertices, two identical keyed frames. Default Hawk maximum vertex error 1.29e-6 m.
- Direct `Hands.blend` input with hand scale 1.08, per-hand translation/rotation, elbow swivel and finger curl; independent FBX maximum vertex error 2.20e-6 m.
- Hawk's provided idle is a single processed frame (frame 0). Selecting frame 10 was correctly rejected before output publication; this was a fixture boundary, not a conversion failure.
- Unity import check script compiles against installed Unity 2020.3 assemblies without launching an editor.

The first implementation assigned an edit-bone rest matrix while its length was zero, corrupting the output bind orientation. The final implementation sets length first and checks complete world matrices and every evaluated skin vertex before reporting success. Bone-head-only checks would not have detected this bug.

## Not verified

Unity 2020.3.49f1c1 batch initialization exited 1 with `License is not active (com.unity.editor.headless)` and `No valid Unity Editor license found`. No test project was created and no Unity importer or game runtime was exercised. No license, account or other Unity project was modified.

This release is a static idle calibration feature. Full idle/reload/fire retargeting, automatic collision-free finger fitting, and complete Ravenfield weapon prefab configuration are outside this first implementation. Missing weapon texture references are surfaced in the result report; neutral previews do not claim fully textured output.

## Reproduction

See `scripts/test-ravenfield-adapter.py`, `scripts/verify-ravenfield-fbx.py`, `scripts/verify-ravenfield-hawk-orientation.py`, `scripts/UnityRavenfieldImportCheck.cs`, `--self-test`, `--rf-ui-smoke`, and `--rf-smoke`. The Hawk-only semantic regression fails the old sideways output (magazine Z direction -0.0954) and passes the corrected upright output (-0.9954); it is intentionally not a universal restriction on animated weapon poses. Real COD/RF source assets are supplied locally by the user and are not redistributed in the repository or application package.
