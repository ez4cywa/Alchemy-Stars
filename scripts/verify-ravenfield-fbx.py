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


def verify(args):
    import bpy
    bpy.ops.wm.open_mainfile(filepath=str(args.reference.resolve()), load_ui=False, use_scripts=False)
    scene = bpy.context.scene
    require(scene.unit_settings.system == "METRIC" and scene.unit_settings.length_unit == "METERS"
            and abs(scene.unit_settings.scale_length - 1) < 1e-8, "Reference is not configured as 1 BU = 1 m")
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
    parser.add_argument("--position-tolerance", type=float, default=0.0002)
    parser.add_argument("--rotation-tolerance", type=float, default=0.1)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    args = parser.parse_args(argv)
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
