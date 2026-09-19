"""Read back a generated FBX in a disposable Blender process; no user settings changed.

blender --background --factory-startup --python-exit-code 1 --python this.py -- model.fbx 74 38
"""
import json
import sys
from pathlib import Path

import bpy

path, expected_bones, expected_meshes = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(Path(path).resolve()))
rigs = [o for o in bpy.data.objects if o.type == "ARMATURE"]
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
assert len(rigs) == 1, f"Expected one armature, got {len(rigs)}"
assert len(rigs[0].data.bones) == int(expected_bones), "FBX bone count changed"
assert len(meshes) == int(expected_meshes), "FBX mesh count changed"
for mesh in meshes:
    assert len(mesh.data.vertices) > 0, f"Empty mesh: {mesh.name}"
    assert any(m.type == "ARMATURE" and m.object == rigs[0] for m in mesh.modifiers), f"Unbound mesh: {mesh.name}"
    assert all(any(g.weight > 0 for g in v.groups) for v in mesh.data.vertices), f"Unweighted vertices: {mesh.name}"
print(json.dumps({"result": "PASS", "bones": len(rigs[0].data.bones), "meshes": len(meshes),
                  "vertices": sum(len(m.data.vertices) for m in meshes), "file": str(Path(path).resolve())}))
