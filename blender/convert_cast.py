"""Blender background bridge. Does not install add-ons or modify user preferences.

blender --background --factory-startup --python-exit-code 1 --python convert_cast.py -- input.cast output.fbx
Optional: --report path.json --blend path.blend --verify
"""
from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path, nargs="?")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--blend", type=Path)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    source = args.input.resolve()
    for destination in (args.output, args.report, args.blend):
        if destination and destination.resolve() == source:
            raise ValueError("Output must not overwrite the CAST input")
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    directory = Path(__file__).resolve().parent
    candidates = [directory.parent / "third_party/cast/blender", directory.parent / "BlenderPlugin"]
    addon_root = next((p for p in candidates if (p / "io_scene_cast/__init__.py").is_file()), None)
    if addon_root is None:
        raise FileNotFoundError("Bundled Blender CAST plugin is missing")
    sys.path.insert(0, str(addon_root))
    import io_scene_cast
    from io_scene_cast.cast import Cast, Model, Animation, Metadata
    roots = Cast.load(str(source)).Roots()
    source_animations = [a for root in roots for a in root.ChildrenOfType(Animation)]
    has_animation = len(source_animations) > 0
    metadata = next((m for root in roots for m in root.ChildrenOfType(Metadata)), None)
    # CAST without an axis follows a fresh Maya scene (Y-up). Do not inherit
    # Blender's Z-up convention or a workstation's saved preferences.
    source_up = (metadata.UpAxis() if metadata else None) or "y"
    if source_up not in ("x", "y", "z"):
        raise ValueError("Unsupported CAST up axis: " + source_up)
    io_scene_cast.register()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = bpy.ops.import_scene.cast(filepath=str(source), import_merge=False, import_reset=True,
                                       import_ik=False, import_constraints=False, import_skin=True)
    if "FINISHED" not in result:
        raise RuntimeError("CAST import failed")
    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(armatures) != 1 or not meshes:
        raise RuntimeError("Expected a complete CAST scene with one armature and meshes")
    rig = armatures[0]
    if has_animation and (rig.animation_data is None or rig.animation_data.action is None):
        raise RuntimeError("Imported armature has no active action")
    for mesh in meshes:
        modifiers = [m for m in mesh.modifiers if m.type == "ARMATURE"]
        if len(modifiers) != 1 or modifiers[0].object != rig:
            raise RuntimeError("Mesh is not bound to the unified armature: " + mesh.name)
    scene = bpy.context.scene
    # CAST positions are imported verbatim into Maya's default centimetres.
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 0.01
    report = {"blender": bpy.app.version_string, "source": str(source), "sha256": digest,
              "armatures": len(armatures), "bones": len(rig.data.bones), "meshes": len(meshes),
              "source_up_axis": source_up, "fbx_up_axis": source_up,
              "has_animation": has_animation,
              "fps": scene.render.fps / scene.render.fps_base,
              "range": [scene.frame_start, scene.frame_end],
              "dqs_meshes": sum(any(m.type == "ARMATURE" and m.use_deform_preserve_volume for m in mesh.modifiers) for mesh in meshes)}
    if args.verify:
        roots = Cast.load(str(source)).Roots()
        models = [m for r in roots for m in r.ChildrenOfType(Model)]
        animations = [a for r in roots for a in r.ChildrenOfType(Animation)]
        if len(models) != 1 or len(animations) != 1:
            raise RuntimeError("Verification requires one model and one animation")
        bones = models[0].Skeleton().Bones()
        curves = {(c.NodeName(), c.KeyPropertyName()): c for c in animations[0].Curves()}
        if any(c.Mode() != "absolute" for c in curves.values()):
            raise RuntimeError("Verification expects baked absolute curves")
        first = min(min(c.KeyFrameBuffer()) for c in curves.values())
        last = max(max(c.KeyFrameBuffer()) for c in curves.values())
        if report["range"] != [first, last]:
            raise RuntimeError("Imported timeline differs from CAST keys")
        max_position = 0.0
        max_rotation = 0.0
        worst_position = None
        max_position_tolerance_ratio = 0.0

        def sample(name, channel, frame, default):
            curve = curves.get((name, channel))
            if curve is None:
                return default
            keys = curve.KeyFrameBuffer()
            values = curve.KeyValueBuffer()
            index = max(0, bisect.bisect_right(keys, frame) - 1)
            if keys[index] != frame:
                raise RuntimeError("Verification fixture must have baked keys for every frame")
            return values[index * 4:index * 4 + 4] if channel == "rq" else values[index]

        for frame in range(int(first), int(last) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            expected = {}

            def world(index):
                if index in expected:
                    return expected[index]
                bone = bones[index]
                name = bone.Name()
                position = tuple(sample(name, channel, frame, bone.LocalPosition()[axis]) for axis, channel in enumerate(("tx", "ty", "tz")))
                q = sample(name, "rq", frame, bone.LocalRotation())
                rotation = Quaternion((q[3], q[0], q[1], q[2])).normalized()
                matrix = Matrix.LocRotScale(Vector(position), rotation, Vector(bone.Scale() or (1, 1, 1)))
                if bone.ParentIndex() >= 0:
                    matrix = world(bone.ParentIndex()) @ matrix
                expected[index] = matrix
                return matrix

            for index, bone in enumerate(bones):
                pose = rig.pose.bones.get(bone.Name())
                if pose is None:
                    raise RuntimeError("Missing bone " + bone.Name())
                wanted = world(index)
                error = (pose.matrix.translation - wanted.translation).length
                # Blender matrices are float32. Far-away helper joints (~8,000
                # units in Scarab) require an absolute + relative tolerance.
                tolerance = 0.0002 + 0.000002 * wanted.translation.length
                max_position_tolerance_ratio = max(max_position_tolerance_ratio, error / tolerance)
                if error > max_position:
                    max_position = error
                    worst_position = {"bone": bone.Name(), "frame": frame, "expected": list(wanted.translation), "actual": list(pose.matrix.translation)}
                max_rotation = max(max_rotation, 1 - min(1, abs(pose.matrix.to_quaternion().normalized().dot(wanted.to_quaternion().normalized()))))
        report.update(max_position_error=max_position, max_rotation_error_1_absdot=max_rotation,
                      max_position_tolerance_ratio=max_position_tolerance_ratio, worst_position=worst_position)
        if max_position_tolerance_ratio > 1 or max_rotation > 0.00001:
            raise RuntimeError("Blender pose does not match CAST: " + json.dumps(report))
        if report["dqs_meshes"] != len(meshes):
            raise RuntimeError("DQS skinning was not preserved")
        scene.frame_set(int(first))
    # Convert the complete scene once, including skinned meshes. Keep bone
    # rest/pose/animation data in its source basis; the armature object carries
    # the change of basis for every frame. Never rotate bones independently.
    source_to_blender = (Matrix.Rotation(math.pi / 2, 4, "X") if source_up == "y"
                         else Matrix.Rotation(-math.pi / 2, 4, "Y") if source_up == "x"
                         else Matrix.Identity(4))
    for obj in [rig, *meshes]:
        if obj.parent is None:
            obj.matrix_world = source_to_blender @ obj.matrix_world
    bpy.context.view_layer.update()
    if args.blend:
        args.blend.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(args.blend.resolve()))
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
        bpy.ops.object.select_all(action="DESELECT")
        for obj in [rig, *meshes]:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = rig
        result = bpy.ops.export_scene.fbx(filepath=str(args.output.resolve()), use_selection=True,
            axis_up=source_up.upper(), axis_forward="-Z" if source_up == "y" else "Y",
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=has_animation,
            bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
            bake_anim_simplify_factor=0.0, bake_anim_step=1.0)
        if "FINISHED" not in result or not args.output.is_file() or args.output.stat().st_size == 0:
            raise RuntimeError("Blender FBX export failed")
    if hashlib.sha256(source.read_bytes()).hexdigest() != digest:
        raise RuntimeError("CAST input changed")
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
