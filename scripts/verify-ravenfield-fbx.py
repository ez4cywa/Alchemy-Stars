"""Independent RF FBX round-trip check; does not read the adapter's report.

Run with Python (launches Blender) or directly with Blender:
  python scripts/verify-ravenfield-fbx.py --blender D:/blender/blender.exe \
      --reference output/example.blend --fbx output/example.fbx

FBX changes bone display axes and terminal bone lengths. Compare joint heads,
parent-child endpoints, bind-relative WORLD deformation rotations, and evaluated
skin, not local bone roll or inferred display tails. Coordinates are metres.
This validates interchange, not anatomical fit or Ravenfield/Unity gameplay.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def global_settings(path):
    from io_scene_fbx import parse_fbx
    tree, version = parse_fbx.parse(str(path))
    settings = next(e for e in tree.elems if e.id == b"GlobalSettings")
    properties = next(e for e in settings.elems if e.id == b"Properties70")
    return version, {e.props[0].decode(): e.props[-1] for e in properties.elems if e.id == b"P"}


def snapshot():
    import bpy
    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    require(len(rigs) == 1, f"Expected one armature, found {len(rigs)}")
    rig = rigs[0]
    require(not any("ReferenceHands" in o.name for o in meshes), "COD reference hand mesh leaked into output")
    require(all(not p.constraints for p in rig.pose.bones), "Output contains bone constraints")
    require(all(any(m.type == "ARMATURE" and m.object == rig for m in o.modifiers) for o in meshes),
            "Every exported mesh must remain skinned to the single output armature")
    result = {"parents": {b.name: b.parent.name if b.parent else None for b in rig.data.bones},
              "counts": {o.name: len(o.data.vertices) for o in meshes},
              "armatureObjectScale": list(rig.scale), "frames": {}}
    roots = [n for n, p in result["parents"].items() if p is None]
    require(roots == ["RF_Root"], f"Unexpected root bones: {roots}")
    require(all(n in result["parents"] for n in ("Hand.L", "Hand.R", "Arm.L", "Arm.R")),
            "RF arm bones missing")
    for frame in (1, 2):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        bones = {}
        for bone in rig.pose.bones:
            world = rig.matrix_world @ bone.matrix
            rest = rig.matrix_world @ bone.bone.matrix_local
            # Right-side rest-basis correction cancels FBX's bone-axis remapping.
            deformation = world @ rest.inverted()
            bones[bone.name] = {"head": world.translation.copy(),
                                "deformation": deformation.copy(),
                                "rotation": deformation.to_quaternion()}
        vertices = {}
        for obj in meshes:
            evaluated = obj.evaluated_get(depsgraph)
            geometry = evaluated.to_mesh()
            vertices[obj.name] = [evaluated.matrix_world @ v.co for v in geometry.vertices]
            evaluated.to_mesh_clear()
        result["frames"][frame] = {"bones": bones, "vertices": vertices}
    return result


def compare(left, right, position_tolerance, rotation_tolerance):
    require(left["parents"] == right["parents"], "Bone names/count/hierarchy changed")
    require(left["counts"] == right["counts"], f"Mesh names/count/topology changed: {left['counts']} vs {right['counts']}")
    errors = {"boneHeadM": 0.0, "deformationRotationDeg": 0.0,
              "deformationMatrix": 0.0, "skinnedVertexM": 0.0, "parentChildEndpointM": 0.0}
    for frame in (1, 2):
        a, b = left["frames"][frame], right["frames"][frame]
        for name, expected in a["bones"].items():
            actual = b["bones"][name]
            errors["boneHeadM"] = max(errors["boneHeadM"], (expected["head"] - actual["head"]).length)
            angle = expected["rotation"].rotation_difference(actual["rotation"]).angle
            angle = min(abs(angle), abs(2 * math.pi - angle))
            errors["deformationRotationDeg"] = max(errors["deformationRotationDeg"], math.degrees(angle))
            errors["deformationMatrix"] = max(errors["deformationMatrix"],
                max(abs(expected["deformation"][i][j] - actual["deformation"][i][j]) for i in range(4) for j in range(4)))
            parent = left["parents"][name]
            if parent:
                source_segment = expected["head"] - a["bones"][parent]["head"]
                target_segment = actual["head"] - b["bones"][parent]["head"]
                errors["parentChildEndpointM"] = max(errors["parentChildEndpointM"], (source_segment - target_segment).length)
        for name, expected in a["vertices"].items():
            actual = b["vertices"][name]
            require(len(expected) == len(actual), f"Evaluated vertex count changed: {name}")
            # Blender's FBX round trip retains control-point order; compare each
            # index, not nearest points that might conceal a broken skin binding.
            error = max((x - y).length for x, y in zip(expected, actual))
            errors["skinnedVertexM"] = max(errors["skinnedVertexM"], error)
    require(all(math.isfinite(v) for v in errors.values()), f"Non-finite comparison: {errors}")
    require(max(errors[k] for k in ("boneHeadM", "skinnedVertexM", "parentChildEndpointM")) <= position_tolerance,
            f"World geometry mismatch: {errors}")
    require(errors["deformationRotationDeg"] <= rotation_tolerance, f"World deformation rotation mismatch: {errors}")
    require(errors["deformationMatrix"] <= max(position_tolerance, 0.0002), f"World deformation matrix mismatch: {errors}")
    return errors


def bounds(snapshot_data):
    vertices = [v for points in snapshot_data["frames"][1]["vertices"].values() for v in points]
    return {"minimumM": [min(v[i] for v in vertices) for i in range(3)],
            "maximumM": [max(v[i] for v in vertices) for i in range(3)],
            "sizeM": [max(v[i] for v in vertices) - min(v[i] for v in vertices) for i in range(3)]}


def animation_snapshot():
    """One evaluated frame, packed arrays; never retain a clip's meshes in RAM."""
    import bpy
    import numpy as np
    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = sorted((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: o.name)
    require(len(rigs) == 1 and meshes, "Expected one armature and skinned meshes")
    rig = rigs[0]
    require(not any(o.constraints for o in [rig, *meshes]), "Output contains object constraints")
    require(all(not p.constraints for p in rig.pose.bones), "Output contains bone constraints")
    require(not any("ReferenceHands" in o.name for o in meshes), "COD reference hand mesh leaked into output")
    require(all(any(m.type == "ARMATURE" and m.object == rig for m in o.modifiers) for o in meshes),
            "Every mesh must remain skinned to the single armature")
    bones = sorted(rig.pose.bones, key=lambda b: b.name)
    parents = {b.name: b.parent.name if b.parent else None for b in bones}
    require([n for n, p in parents.items() if p is None] == ["RF_Root"], "Unexpected root bones")
    require(all(n in parents for n in ("Hand.L", "Hand.R", "Arm.L", "Arm.R")), "RF arm bones missing")
    worlds = [rig.matrix_world @ b.matrix for b in bones]
    deforms = [world @ (rig.matrix_world @ b.bone.matrix_local).inverted() for world, b in zip(worlds, bones)]
    data = {"heads": np.asarray([list(m.translation) for m in worlds], dtype=np.float64),
            "deforms": np.asarray([list(map(list, m)) for m in deforms], dtype=np.float64),
            "rotations": np.asarray([list(m.to_quaternion()) for m in deforms], dtype=np.float64)}
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for index, obj in enumerate(meshes):
        evaluated = obj.evaluated_get(depsgraph)
        geometry = evaluated.to_mesh()
        try:
            coords = np.empty(len(geometry.vertices) * 3, dtype=np.float32)
            geometry.vertices.foreach_get("co", coords)
            matrix = np.asarray(evaluated.matrix_world, dtype=np.float64)
            data[f"vertices{index}"] = coords.reshape(-1, 3) @ matrix[:3, :3].T + matrix[:3, 3]
        finally:
            evaluated.to_mesh_clear()
    metadata = {"parents": parents, "counts": {o.name: len(o.data.vertices) for o in meshes},
                "armatureObjectScale": list(rig.scale)}
    return metadata, data


def animation_errors(a, b, parents):
    import numpy as np
    require(set(a) == set(b), "Snapshot fields changed")
    require(all(a[k].shape == b[k].shape for k in a), "Evaluated topology changed")
    require(all(np.isfinite(v).all() for d in (a, b) for v in d.values()), "Non-finite evaluated geometry")
    distances = lambda v: float(np.max(np.linalg.norm(v, axis=-1), initial=0))
    qa, qb = a["rotations"], b["rotations"]
    dots = np.sum(qa * qb, axis=1) / (np.linalg.norm(qa, axis=1) * np.linalg.norm(qb, axis=1))
    indices = {n: i for i, n in enumerate(parents)}
    children = [indices[n] for n, p in parents.items() if p]
    ancestors = [indices[p] for p in parents.values() if p]
    head_delta = a["heads"] - b["heads"]
    return {"boneHeadM": distances(head_delta),
            "deformationRotationDeg": float(np.max(np.degrees(2 * np.arccos(np.clip(np.abs(dots), 0, 1))), initial=0)),
            "deformationMatrix": float(np.max(np.abs(a["deforms"] - b["deforms"]), initial=0)),
            "skinnedVertexM": max(distances(a[k] - b[k]) for k in a if k.startswith("vertices")),
            "parentChildEndpointM": distances(head_delta[children] - head_delta[ancestors])}


def verify_animation(args):
    import bpy
    import numpy as np
    scene = bpy.context.scene
    start, end = scene.frame_start, scene.frame_end
    require(start == 1 and end >= 2, f"Animation scene must span 1..N, N >= 2: {(start, end)}")
    rig = next((o for o in scene.objects if o.type == "ARMATURE"), None)
    require(rig and rig.animation_data and rig.animation_data.action, "Reference action missing")
    action_range = tuple(rig.animation_data.action.frame_range)
    require(all(abs(a - b) < 1e-5 for a, b in zip(action_range, (1, end))),
            f"Reference action and scene ranges differ: {action_range} vs {(1, end)}")
    fps, fps_base = scene.render.fps, scene.render.fps_base
    rate = fps / fps_base
    require(rate > 0 and math.isfinite(rate), "Invalid reference frame rate")
    version, settings = global_settings(args.fbx)
    required = {"UpAxis": 1, "UpAxisSign": 1, "FrontAxis": 2, "FrontAxisSign": 1,
                "CoordAxis": 0, "CoordAxisSign": 1, "UnitScaleFactor": 100.0}
    require(all(settings.get(k) == v for k, v in required.items()), f"Unexpected FBX axes/units: {settings}")
    # FBX standard TimeMode enumerants, in frames per second. Mode 14 uses CustomFrameRate.
    rates = {1: 120, 2: 100, 3: 60, 4: 50, 5: 48, 6: 30, 7: 30, 8: 30000/1001,
             9: 30000/1001, 10: 25, 11: 24, 12: 1000, 13: 24000/1001, 15: 96, 16: 72, 17: 60000/1001}
    exported_rate = settings.get("CustomFrameRate") if settings.get("TimeMode") == 14 else rates.get(settings.get("TimeMode"))
    require(exported_rate is not None and abs(exported_rate - rate) < 0.001,
            f"FBX frame rate mismatch: expected {rate}, metadata {settings}")
    maxima, worst_frames, motion = {}, {}, {}
    def accumulate(target, values, frame, locations=None):
        for key, value in values.items():
            if key not in target or value > target[key]:
                target[key] = value
                if locations is not None:
                    locations[key] = frame
    def frame_bounds(data):
        points = np.concatenate([v for k, v in data.items() if k.startswith("vertices")])
        return {"minimumM": points.min(axis=0).tolist(), "maximumM": points.max(axis=0).tolist(),
                "sizeM": np.ptp(points, axis=0).tolist()}
    with tempfile.TemporaryDirectory(prefix="rf-fbx-verify-") as cache:
        first = None
        for frame in range(1, end + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            metadata, data = animation_snapshot()
            if first is None:
                expected_meta, first = metadata, data
                source_bounds = frame_bounds(data)
                require(0.1 < max(source_bounds["sizeM"]) < 3, f"Implausible metre scale: {source_bounds}")
            require(metadata == expected_meta, f"Reference metadata changes at frame {frame}")
            accumulate(motion, animation_errors(first, data, metadata["parents"]), frame)
            np.savez(Path(cache) / f"{frame}.npz", **data)
        # A padded single source frame is allowed only for a two-frame clip.
        if end > 2:
            require(max(motion["boneHeadM"], motion["skinnedVertexM"], motion["deformationMatrix"]) > 1e-7,
                    f"Reference animation has no actual motion: {motion}")
        del first, data
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.render.fps, scene.render.fps_base = fps, fps_base
        result = bpy.ops.import_scene.fbx(filepath=str(args.fbx.resolve()), use_anim=True, anim_offset=0,
            automatic_bone_orientation=False, ignore_leaf_bones=False)
        require("FINISHED" in result, "FBX import failed")
        rigs = [o for o in scene.objects if o.type == "ARMATURE"]
        require(len(rigs) == 1 and rigs[0].animation_data and rigs[0].animation_data.action, "FBX action missing")
        require(len(bpy.data.actions) == 1, f"Expected one FBX take, got {[a.name for a in bpy.data.actions]}")
        if args.library:
            require(rigs[0].animation_data.action.name == "Scene" or rigs[0].animation_data.action.name.endswith("|Scene"),
                    f"Library FBX take must be Scene: {rigs[0].animation_data.action.name}")
        imported_range = tuple(rigs[0].animation_data.action.frame_range)
        require(all(abs(a - b) < 1e-4 for a, b in zip(imported_range, (1, end))),
                f"FBX action range/duration mismatch: {imported_range}, expected 1..{end}")
        imported_motion, first = {}, None
        for frame in range(1, end + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            actual_meta, actual = animation_snapshot()
            require(expected_meta["parents"] == actual_meta["parents"], "Bone names/hierarchy changed or leaf bones added")
            require(expected_meta["counts"] == actual_meta["counts"], "Mesh names/count/topology changed")
            if first is None:
                first = actual
                actual_bounds = frame_bounds(actual)
            accumulate(imported_motion, animation_errors(first, actual, actual_meta["parents"]), frame)
            with np.load(Path(cache) / f"{frame}.npz", allow_pickle=False) as cached:
                errors = animation_errors(dict(cached), actual, expected_meta["parents"])
            accumulate(maxima, errors, frame, worst_frames)
        require(all(math.isfinite(v) for v in maxima.values()), f"Non-finite errors: {maxima}")
        require(max(maxima[k] for k in ("boneHeadM", "skinnedVertexM", "parentChildEndpointM")) <= args.position_tolerance,
                f"World geometry mismatch: {maxima}, frames: {worst_frames}")
        require(maxima["deformationRotationDeg"] <= args.rotation_tolerance,
                f"World deformation rotation mismatch: {maxima}, frames: {worst_frames}")
        require(maxima["deformationMatrix"] <= max(args.position_tolerance, 0.0002),
                f"World deformation matrix mismatch: {maxima}, frames: {worst_frames}")
        if end > 2:
            require(max(imported_motion["boneHeadM"], imported_motion["skinnedVertexM"], imported_motion["deformationMatrix"]) > 1e-7,
                    "Imported animation has no actual motion")
    report = {
        "mode": "library" if args.library else "animation",
        "blender": bpy.app.version_string, "fbxVersion": version, "fbxSettings": required,
        "frameRange": [1, end], "framesCompared": end, "fps": rate, "fbxFps": exported_rate,
        "durationSeconds": (end - 1) / rate, "bones": len(expected_meta["parents"]),
        "meshes": len(expected_meta["counts"]), "vertexCounts": expected_meta["counts"],
        "referenceBounds": source_bounds, "fbxBounds": actual_bounds, "roundTripErrors": maxima,
        "worstErrorFrames": worst_frames, "referenceMotion": motion, "fbxMotion": imported_motion,
        "importedArmatureObjectScale": actual_meta["armatureObjectScale"],
        "note": "Every integer frame and every vertex compared. Duration is last minus first sample time. Unity not tested."
    }
    marker = "RAVENFIELD_FBX_TIMELINE_VERIFICATION_PASS " if args.library else "RAVENFIELD_FBX_VERIFICATION_PASS "
    print(marker + json.dumps(report, ensure_ascii=False), flush=True)
    if args.output and not args.library:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    return report


def verify_library(args):
    import bpy
    scene = bpy.context.scene
    require(not any(o.animation_data and o.animation_data.nla_tracks for o in bpy.context.scene.objects),
            "Reference library must use independent actions without NLA mixing")
    rigs = [o for o in scene.objects if o.type == "ARMATURE"]
    require(len(rigs) == 1 and rigs[0].animation_data, "Expected one library armature")
    rig = rigs[0]
    timeline = rig.animation_data.action
    manifest = json.loads(rig.get("ravenfield_adapter", "{}"))
    require(manifest.get("mode") == "animation-library", "Reference is not an animation library")
    require(timeline and timeline.name == manifest.get("timelineAction") and scene.name == "Scene",
            "Reference active action/scene does not match library timeline")
    clips = manifest.get("clips", [])
    require(clips and len({c["name"] for c in clips}) == len(clips), "Missing or duplicate library clip names")
    expected_names = {c.get("actionName", c["name"]) for c in clips} | {timeline.name}
    require({a.name for a in bpy.data.actions} == expected_names, "Missing clip actions or extraneous source actions")
    previous_end, reports = None, []
    for clip in clips:
        first, last = clip["firstFrame"], clip["lastFrame"]
        require(isinstance(first, int) and isinstance(last, int) and last > first, "Invalid clip frame range")
        require(first == 1 if previous_end is None else first == previous_end + 11,
                f"Clip ranges must begin at 1 and have ten unassigned gap frames: {clip}")
        require(clip.get("takeName") == "Scene", f"Wrong clip take name: {clip}")
        require(clip.get("frameCount") == last - first + 1, f"Clip frameCount disagrees with range: {clip}")
        action = bpy.data.actions[clip.get("actionName", clip["name"])]
        require(tuple(action.frame_range) == (1, last - first + 1), f"Wrong independent action range: {action.name}")
        maxima, worst = {}, {}
        for offset in range(last - first + 1):
            rig.animation_data.action = action
            scene.frame_set(offset + 1)
            bpy.context.view_layer.update()
            meta, expected = animation_snapshot()
            rig.animation_data.action = timeline
            scene.frame_set(first + offset)
            bpy.context.view_layer.update()
            actual_meta, actual = animation_snapshot()
            require(meta == actual_meta, "Library action changes skeleton or mesh metadata")
            errors = animation_errors(expected, actual, meta["parents"])
            for key, value in errors.items():
                if key not in maxima or value > maxima[key]:
                    maxima[key], worst[key] = value, first + offset
        require(max(maxima[k] for k in ("boneHeadM", "skinnedVertexM", "parentChildEndpointM")) <= args.position_tolerance
                and maxima["deformationRotationDeg"] <= args.rotation_tolerance
                and maxima["deformationMatrix"] <= max(args.position_tolerance, 0.0002),
                f"Clip differs from Scene (mixed/overwritten): {clip['name']}, errors {maxima}, frames {worst}")
        reports.append({"name": clip["name"], "firstFrame": first, "lastFrame": last,
                        "actionToSceneErrors": maxima, "worstErrorFrames": worst})
        previous_end = last
    require((scene.frame_start, scene.frame_end) == (1, previous_end), "Scene range does not match library extent")
    rig.animation_data.action = timeline
    report = verify_animation(args)
    report.update(clips=reports, clipCount=len(reports))
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print("RAVENFIELD_FBX_VERIFICATION_PASS " + json.dumps(report, ensure_ascii=False), flush=True)


def verify(args):
    import bpy
    bpy.ops.wm.open_mainfile(filepath=str(args.reference.resolve()), load_ui=False, use_scripts=False)
    scene = bpy.context.scene
    require(scene.unit_settings.system == "METRIC" and scene.unit_settings.length_unit == "METERS"
            and abs(scene.unit_settings.scale_length - 1) < 1e-8, "Reference is not configured as 1 BU = 1 m")
    if args.library:
        return verify_library(args)
    if args.animation:
        return verify_animation(args)
    require((scene.frame_start, scene.frame_end) == (1, 2), "Reference does not have exactly two idle frames")
    expected = snapshot()
    fps, fps_base = scene.render.fps, scene.render.fps_base
    source_bounds = bounds(expected)
    require(0.1 < max(source_bounds["sizeM"]) < 3, f"Implausible metre-scale first-person model: {source_bounds}")
    # Verify identical poses independently in EACH file (not just corresponding frames).
    expected_static = dict(expected, frames={1: expected["frames"][1], 2: expected["frames"][1]})
    reference_idle = compare(expected_static, expected, args.position_tolerance, args.rotation_tolerance)
    version, settings = global_settings(args.fbx)
    required = {"UpAxis": 1, "UpAxisSign": 1, "FrontAxis": 2, "FrontAxisSign": 1,
                "CoordAxis": 0, "CoordAxisSign": 1, "UnitScaleFactor": 100.0}
    # FBX FrontAxis uses parity: this metadata is Blender's -Z-forward/Y-up export.
    require(all(settings.get(k) == value for k, value in required.items()), f"Unexpected FBX axes/units: {settings}")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Blender's importer maps FBX seconds to the destination scene FPS.
    bpy.context.scene.render.fps = fps
    bpy.context.scene.render.fps_base = fps_base
    result = bpy.ops.import_scene.fbx(filepath=str(args.fbx.resolve()), use_anim=True, anim_offset=0,
        automatic_bone_orientation=False, ignore_leaf_bones=False)
    require("FINISHED" in result, "FBX import failed")
    actual = snapshot()
    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    require(rigs[0].animation_data and rigs[0].animation_data.action, "FBX animation is missing")
    action_range = tuple(rigs[0].animation_data.action.frame_range)
    require(all(abs(a - b) < 1e-5 for a, b in zip(action_range, (1.0, 2.0))),
            f"FBX idle action must span frames 1–2: {action_range}")
    actual_static = dict(actual, frames={1: actual["frames"][1], 2: actual["frames"][1]})
    actual_idle = compare(actual_static, actual, args.position_tolerance, args.rotation_tolerance)
    errors = compare(expected, actual, args.position_tolerance, args.rotation_tolerance)
    print("RAVENFIELD_FBX_VERIFICATION_PASS " + json.dumps({
        "blender": bpy.app.version_string, "fbxVersion": version, "fbxSettings": required,
        "bones": len(expected["parents"]), "meshes": len(expected["counts"]), "vertexCounts": expected["counts"],
        "referenceBounds": source_bounds, "fbxBounds": bounds(actual), "roundTripErrors": errors,
        "referenceIdleErrors": reference_idle, "fbxIdleErrors": actual_idle,
        "importedArmatureObjectScale": actual["armatureObjectScale"],
        "note": "Object scale is not physical size; world coordinates include FBX unit/axis conversion. Unity not tested."
    }, ensure_ascii=False), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--fbx", type=Path, required=True)
    parser.add_argument("--blender", default="D:/blender/blender.exe")
    parser.add_argument("--output", type=Path, help="Persist animation/library verification JSON after successful checks")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--animation", action="store_true", help="Compare every integer frame in reference action/scene range 1..N")
    modes.add_argument("--library", action="store_true", help="Compare clip actions against Scene ranges, then round-trip the entire Scene take")
    parser.add_argument("--position-tolerance", type=float, default=0.0002)
    parser.add_argument("--rotation-tolerance", type=float, default=0.1)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    args = parser.parse_args(argv)
    require(args.output is None or args.output.resolve() not in (args.reference.resolve(), args.fbx.resolve()),
            "Output report must not overwrite the reference blend or FBX input")
    require(args.reference.is_file() and args.fbx.is_file(), "Reference or FBX file is missing")
    require(args.position_tolerance > 0 and args.rotation_tolerance > 0, "Tolerances must be positive")
    try:
        import bpy
    except ImportError:
        result = subprocess.run([args.blender, "--background", "--factory-startup", "--disable-autoexec",
            "--python-exit-code", "1", "--python", str(Path(__file__).resolve()), "--", *argv])
        raise SystemExit(result.returncode)
    verify(args)


if __name__ == "__main__":
    main()
