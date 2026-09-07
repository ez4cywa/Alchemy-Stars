"""Validate separate model + animation import against a complete CAST scene.

blender --background --factory-startup --python-exit-code 1 --python verify_model_companion.py -- model.cast animation.cast complete.cast
"""
import json
import sys
from pathlib import Path

import bpy

model, animation, complete = map(Path, sys.argv[sys.argv.index("--") + 1:])
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "third_party/cast/blender"))
import io_scene_cast
io_scene_cast.register()


def load(path):
    result = bpy.ops.import_scene.cast(filepath=str(path.resolve()), import_merge=False,
        import_reset=True, import_ik=False, import_constraints=False, import_skin=True)
    assert "FINISHED" in result
    rigs = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    assert len(rigs) == 1
    return rigs[0]


bpy.ops.wm.read_factory_settings(use_empty=True)
rig = load(complete)
expected_bones = set(rig.data.bones.keys())
expected_meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
expected_mesh_count = len(expected_meshes)
expected_vertices = sum(len(obj.data.vertices) for obj in expected_meshes)
frames = list(range(bpy.context.scene.frame_start, bpy.context.scene.frame_end + 1))
expected = {}
for frame in frames:
    bpy.context.scene.frame_set(frame)
    expected[frame] = {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
bpy.ops.wm.read_factory_settings(use_empty=True)
rig = load(model)
assert set(rig.data.bones.keys()) == expected_bones
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
assert len(meshes) == expected_mesh_count
assert sum(len(obj.data.vertices) for obj in meshes) == expected_vertices
assert not rig.animation_data or not rig.animation_data.action
assert all(any(m.type == "ARMATURE" and m.object == rig for m in obj.modifiers) for obj in meshes)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
assert load(animation) == rig
assert rig.animation_data and rig.animation_data.action
assert [bpy.context.scene.frame_start, bpy.context.scene.frame_end] == [frames[0], frames[-1]]
maximum = 0.0
for frame in frames:
    bpy.context.scene.frame_set(frame)
    for name, wanted in expected[frame].items():
        actual = rig.pose.bones[name].matrix
        error = (actual.translation - wanted.translation).length
        maximum = max(maximum, error)
        assert error <= 0.0002 + 0.000002 * wanted.translation.length, (frame, name)
        assert 1 - abs(actual.to_quaternion().normalized().dot(wanted.to_quaternion().normalized())) <= 0.00001
print(json.dumps({"result": "PASS", "bones": len(expected_bones), "meshes": expected_mesh_count, "frames": len(frames), "max_position_error": maximum}))
