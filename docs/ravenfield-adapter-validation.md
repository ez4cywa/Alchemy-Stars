# RF adapter validation — 1.3.0-rf.6

## RF.6 palm/web penetration and whole-hand seating

- Red-capable actual-mesh test: `verify-ravenfield-palm.py --local-hand-fit --require-weapon-contact --require-pass` fails RF.5 with `VISIBLE_GRIP_GAP ... 24.300 mm`. Four palm points were insufficient: the web targets still followed the authored hand away from the weapon.
- RF.6 captures all seven reference weapon material points, with inward 1.5 mm targets, and seats the entire left hand using the web contact displacement before local fitting. The arm is re-solved rather than translating a disconnected wrist. Idle seating is approximately 21.411 mm; measured seven-point nearest-weapon distances are 0.144–1.453 mm, target residual 0.324 mm. These are sample distances, not a full-surface collision claim; penetration is intentional.
- All 563 actual source frames pass the unchanged 5 mm gate. Per-clip maxima: Idle 0.324 mm, Sprint 0.33 mm, Reload 2.183 mm, reload_empty 3.02 mm, Inspect 4.278 mm. Active source contact frames remain 1/1, 67/67, 26/91, 36/113 and 39/291 respectively, identical to RF.5. Release remains based on the original four palm samples, not the newly included web samples.
- 22 backend contracts pass, including all-seven inward seating, rotation of the captured weapon surface, and untouched authored targets after release. Native AOT publish and bilingual 900 px RF live-UI checks pass.
- Reports now include seven weapon-anchor distances and the whole-hand seating vector. The full library smoke caught and corrected an old four-point-only reporting assumption before release.
- Final RF.6 Native export: all 564 clip frames pass; 604 timeline frames, 331 bones and 19 meshes pass independent FBX reimport. Maximum round-trip skin error is 2.033e-6 m, bone-head error 1.821e-6 m, matrix error 5.276e-6. Independent Actions match the Scene intervals. First-person reviews cover frames 1, 40, 110, 207, 247, 373, 425, 480 and 556; RF.5/RF.6 Idle comparisons use the same camera. Output: `output/rf6-qa/final`.

The RF.5 results below are historical, not certification of RF.6 exports.

## RF.5 joint grip targets and reference weapon contact

- Joint local-skin fitting covers four palm and three web-space samples. Each control is projected separately to a 30 mm displacement bound. The current seven-point regression passes all 563 original frames, maximum 4.278 mm against the 5 mm threshold; repeating Idle for export produces 564 clip frames.
- Reference weapon triangles provide stable left-palm anchors with a 0.7 mm contact offset. Contact releases smoothly as source-hand separation grows from 6 to 24 mm; no per-frame nearest-triangle chasing is used. Manual offsets and rotations are applied after target construction.
- The seven-point metric measures configured fit targets. Left-palm targets can incorporate weapon contact rather than raw source-hand positions; these are distinct error definitions and should not be compared as though unchanged from RF.4.
- Local fitting remains off by default and depends on `ContactFit`. Source assets, original bone binds/lengths and weapon placement are preserved. The exported hand mesh, helper bones and local skin weights can change.
- Five-clip ranges remain Idle 1–2, Sprint 13–79, Reload 90–180, reload_empty 191–303 and Inspect 314–604. The 40 gap frames are excluded from the 564 clip-frame count.
- Twenty-one Blender contracts pass, including independent per-control limits, cardinal control centers, smooth contact release and explicit no-weapon fallback. The real-skin worst-frame gate goes red on RF.4 at 11 mm and green on the compact RF.5 control representation at 4.278 mm. Manual target offsets remain effective in the static mesh regression.
- Independent weapon BVH checks find the four left-palm contacts on the handguard lower shell/bottom rail exterior, with 0.7000–0.7022 mm separation. Contact triangles are not sight or inner-barrel geometry. Neutral previews and this four-point measurement are not full-surface penetration tests.
- The first joint-control representation increased skin influences (up to 77 per vertex); an independent top-four truncation probe exposed a 10.83 mm Idle deviation. The final representation transfers each original influence to a mirrored per-material-vertex bone instead of multiplying per-vertex influences. It restores the RF baseline counts (146 vertices above four, maximum seven) and does not claim that a four-weight importer preserves this seven-weight source asset.
- Native AOT publish/self-test and real 900×600 bilingual light/dark settings tests pass. Interface help now names the seven-point and reference-weapon targets explicitly.
- Final Native export passes all 564 clip frames with zero exceeded frames: Idle <0.001 mm, Sprint <0.001 mm, Reload 2.183 mm, reload_empty 2.374 mm and Inspect 4.278 mm. All 604 timeline frames (including holds), 331 bones and 19 meshes pass independent FBX reimport: maximum skin error 2.033e-6 m, bone-head error 1.611e-6 m and deformation-matrix error 5.276e-6. Independent Actions match their Scene intervals.
- Independent structure checks preserve all 101 original bind bones within 6.007e-8 m, every weapon bind vertex exactly, every mesh topology and every per-vertex influence count. Aggregating the 230 helper bones back to their original parents recovers original weights exactly; total weight difference is <=2.23e-16. This verifies the compact representation rather than accepting the earlier high-influence prototype.
- These measurements do not establish full-surface collision freedom, all-finger contact, Unity/RFTools compatibility or game acceptance. RF.5 build/interchange results should be recorded independently when completed; earlier-version results below do not certify the new module.

RF.4 and earlier sections retain their historical metrics and validation scope.

## RF.4 local web-space fitting

- `LocalHandFit` defaults to false. It is effective only with `ContactFit`; disabling contact fitting preserves the stored local-fit preference. Managed tests cover persistence/snapshots and all four combinations through the production fitting-config writer. Real 900px bilingual UI checks cover labels, layout, prerequisite disable and busy states.
- Opt-in fitting changes only the exported local web-space mesh, adds mirrored helper bones and redistributes local weights while preserving total weight. Original finger roots are not moved; original bone binds/lengths, source models and weapon placement remain unchanged. Fixed-reference calibration is shared across clips, with dynamic helper correction capped at 30 mm.
- Correspondence now uses three actual web-space points plus four palm points against the source hands, not nearest gun surfaces. This is a 5 mm correspondence gate, not a collision-free or absolute weapon-clearance certificate.
- Five-clip Scene: Idle 1–2, Sprint 13–79, Reload 90–180, reload_empty 191–303 (113 frames), Inspect 314–604. There are 564 clip frames and 40 gap frames.
- Current web-space results: Idle, Sprint, Reload and reload_empty stay within 5 mm. Inspect exceeds 5 mm in 49 frames, maximum 9.864 mm. Combined palm/web grip error exceeds 5 mm in 317 of 564 clip frames. Whole-animation grip acceptance therefore still fails; the generated files remain reviewable and the UI says “Files generated; grip fit needs review.”
- Seventeen Blender contracts pass, including opt-in validation, bounded local controls, rotation equivariance, welded seams and the hand-only eligibility mask. Native AOT publish/self-test, 900×600 bilingual light/dark RF UI and the five-clip engine export pass. Run UI checks with `--page settings --rf-ui-smoke --window-size 900x600 --render-smoke <path>` so the settings visual tree is ready before interaction.
- Independent fixed-triangle/barycentric first-web verification fails the RF.3 idle at 21.912 mm and passes the final RF.4 static pose at 0.01551 mm. Right web-to-weapon distances are 1.709/2.462/4.818 mm versus source 1.710/2.464/4.818 mm. The left web follows its source hand rather than being attracted to the weapon.
- Final native-produced FBX passes independent reimport of all 604 frames, 157 bones and 19 meshes: maximum skin error 2.033e-6 m, bone-head error 1.470e-6 m and deformation-matrix error 4.143e-6. All five Actions match their Scene intervals with zero position/matrix/skin difference. Original 101 bind bones differ by at most 6.007e-8 m; aggregating helper weights back to their original parent differs by at most 3.253e-8, and total weight by 3.285e-8. These checks establish interchange fidelity and bind preservation, not full-grip acceptance.
- Added helper-bone and redistributed-weight compatibility has not been validated in Unity/RFTools. The local Unity Editor license remains inactive. Existing Blender checks and earlier-version FBX round trips do not establish RF.4 Unity or game acceptance.

The RF.3 and earlier records below are historical baselines; their sample counts and preservation claims describe those versions.

## RF.3 palm-fit addition

- Thirteen Blender contracts pass: added proper/bounded rigid fitting, invalid/degenerate samples, and anatomical palm-side classification. Source and RF palms are sampled in bind pose and tracked with triangle barycentrics, never rematched to nearest vertices every frame.
- A real-mesh red/green check is provided by `scripts/verify-ravenfield-palm.py`. With `--baseline-only --require-pass`, the original wrist-only fit fails at 13.253 mm; without `--baseline-only`, the same source and tolerance pass at 4.852 mm left / 3.908 mm right. The script also checks that manual position offsets remain target changes and produces before/after/source views and actual weapon-clearance measurements.
- The 5 mm gate is **corresponding sample position error**, not absolute weapon clearance or all-surface collision. Four stable central/ulnar palm samples intentionally exclude the independently shaped thenar/finger regions. First-person render inspection confirms residual thumb/finger differences; full-hand fitting remains unfinished.
- The initial six-point fit included thenar shape differences and demanded about 40 degrees of hand rotation. The central-palm fit limits rotation to 25 degrees and wrist shift to 40 mm, preserves source finger directions, iterates against actual skin and retains the best measured candidate including the unchanged baseline.
- Baseline/candidates share identical elbow-continuity history; only the final fit advances it. A runtime assertion rejects worsening relative to that same-frame baseline. Palm side is derived from the bind thumb-root offset, independent of reference-frame weapon orientation.
- Full source library: Idle has zero exceeded frames; Sprint 1, Reload 27, Inspect 248. Worst errors approximately 4.85 / 5.00 / 6.45 / 11.00 mm. The latter three clips are not marked as palm-quality passes. Contact rotation/translation steps and every frame's errors are reported.
- Managed build/self-test and real bilingual UI checks pass, including nullable/default opt-in, explicit disable persistence, manual settings retention, busy disable, and a synthetic failed-quality result that retains files while displaying a review-required title. Native checks and interchange results are recorded with the local release artifacts.
- Final RF.3 Native AOT publish/self-test, bilingual UI and full-library export pass. Independent reimport of its 481 timeline frames, 101 bones and 19 meshes finds maximum skin error 1.933e-6 m, bone-head error 1.468e-6 m, deformation matrix error 3.666e-6 and parent/child endpoint error 1.393e-6 m. Separate Actions equal their Scene intervals exactly. These checks certify interchange fidelity, not grip quality; 276 of 451 clip frames still exceed the four-point palm target.
- Follow-up to the user's remaining thumb/index-web gap: a diagnostic six-point run adds radial samples at normalized coordinates (.25, .4) and (.25, .7) without changing the 25-degree/40-mm bounds. Its actual-skin maximum errors decrease from 26.600/28.127 mm to 14.346/14.131 mm (left/right), still fail, and the left correction reaches the rotation cap. First-person and close-up renders still show the gap. The added points sample the thenar side, not a separately validated web-space landmark. This rejected experiment is not enabled in the release. A true web-space correspondence and local thumb/finger fit remain required before claiming that symptom fixed.

Reproduce the geometric gate with Blender `--background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/verify-ravenfield-palm.py -- --input <prepared.cast> --config <engine-config.json> --rf <RFTools.unitypackage> --output <inspection-directory> --require-pass`. Use the engine-prepared full Z-up CAST; source material files are local user assets, not redistributed.

## RF.2 full-animation/library addition

- The supplied RFTools AR15 FBX metadata uses a single `Scene` take and named frame ranges, not independent FBX takes. The library follows that structure while retaining independent editable Blender Actions.
- Ten Blender contracts pass, including complete world/local pose conversion, quaternion hemisphere continuity, a near-straight elbow-plane case and constant holds across library gaps.
- Managed build/self-test and RF.2 Native AOT publish/self-test pass. Native 900×600 bilingual light/dark UI checks cover all three modes, help text, project persistence and busy states. Native full-library export and static-pose compatibility export pass. The final native-produced library passes the same 481-frame independent reimport check.
- Real Hawk fixture: Idle 1–2, Sprint 13–79, Reload 90–180, Inspect 191–481; 451 authored/repeated frames plus 30 gap frames. Each source is separately evaluated at 30 FPS with its own layers and IK configuration.
- Every baked frame verifies all output bone matrices and every skin vertex against the live fitted source rig. Maximum bake matrix error 1.133e-6, vertex error 2.332e-6 m.
- Independent FBX reimport verifies all 481 frames, 101 bones and 19 meshes: maximum skin error 1.933e-6 m, bone-head error 1.502e-6 m, deformation-matrix error 3.517e-6. Every independent Action matches its Scene interval exactly (matrix/position/skin).
- Sampled renders cover sprint, magazine exchange and both hands during inspect, including the former inspect finger-flip frames. A fixed rest-palm mapping now transports the complete source finger orientation; the remaining largest rotations (Reload 142.693°, Inspect 78.812°) match source rotations, rather than adapter-added twists. Render review is not a collision-free fitting guarantee.
- The clip setup script compiles against installed Unity 2020.3 assemblies. Editor execution remains blocked by the previously recorded inactive license. No real melee fixture was exercised in this pass; arbitrary compatible clips use the same full-frame path.

The earlier RF.1 record below remains as the baseline; its static-only scope applies to RF.1, not RF.2.

## RF.1 baseline

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
