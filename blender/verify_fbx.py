"""Compare FBX reimported bones against a CAST-derived .blend reference.

blender --background --factory-startup --python-exit-code 1 --python verify_fbx.py -- reference.blend result.fbx
"""
import json
import sys
from pathlib import Path

import bpy

reference, fbx = map(Path, sys.argv[sys.argv.index("--") + 1:])
bpy.ops.wm.open_mainfile(filepath=str(reference.resolve()))
rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
frames = list(range(bpy.context.scene.frame_start, bpy.context.scene.frame_end + 1))
mesh_count = sum(o.type == "MESH" for o in bpy.context.scene.objects)
unit_scale = bpy.context.scene.unit_settings.scale_length
expected = {}
for frame in frames:
    bpy.context.scene.frame_set(frame)
    expected[frame] = {b.name: (rig.matrix_world @ b.matrix).translation.copy() for b in rig.pose.bones}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.scale_length = unit_scale
bpy.ops.import_scene.fbx(filepath=str(fbx.resolve()), anim_offset=0.0)
rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
assert len(rigs) == 1, "FBX must contain one armature"
rig = rigs[0]
assert len(rig.data.bones) == len(expected[frames[0]]), "FBX bone count changed"
assert sum(o.type == "MESH" for o in bpy.context.scene.objects) == mesh_count, "FBX mesh count changed"
assert rig.animation_data and rig.animation_data.action, "FBX action is missing"
assert list(rig.animation_data.action.frame_range) == [float(frames[0]), float(frames[-1])], "FBX timeline shifted"
max_ratio = 0.0
worst = None
max_position_error = 0.0
max_weapon_root_error = 0.0
for frame in frames:
    bpy.context.scene.frame_set(frame)
    for name, position in expected[frame].items():
        actual = (rig.matrix_world @ rig.pose.bones[name].matrix).translation
        error = (actual - position).length
        max_position_error = max(max_position_error, error)
        if name in ("j_gun__left", "j_gun__right"):
            max_weapon_root_error = max(max_weapon_root_error, error)
        # FBX decomposes joint matrices through Euler/pre-post rotations.
        # Track its additional round-trip error separately from CAST accuracy.
        ratio = error / (0.0005 + 0.000005 * position.length)
        if ratio > max_ratio:
            max_ratio = ratio
            worst = {"bone": name, "frame": frame, "expected": list(position), "actual": list(actual)}
print(json.dumps({"fbx": str(fbx), "frames": len(frames), "bones": len(rig.data.bones), "meshes": mesh_count,
                  "max_position_tolerance_ratio": max_ratio, "max_position_error": max_position_error,
                  "max_weapon_root_error": max_weapon_root_error, "within_tolerance": max_ratio <= 1, "worst": worst}))
assert max_ratio <= 1, "FBX bone positions changed: tolerance ratio " + str(max_ratio)
