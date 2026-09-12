"""Semantic upright-orientation regression for the local Hawk idle fixture only.

Run in Blender with --python-exit-code 1 --python this_file -- result.blend|result.fbx.
Unlike a round-trip equality check, this detects the 90-degree source-axis mistake.
Do not apply this fixture's upright/forward requirement to arbitrary animated poses.
"""
import json
from pathlib import Path
import sys

import bpy


def main():
    path = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.suffix.lower() == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False)
    elif path.suffix.lower() == ".fbx":
        bpy.context.scene.render.fps = 30
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True, anim_offset=0,
                                 automatic_bone_orientation=False, ignore_leaf_bones=False)
    else:
        raise ValueError("Expected a .blend or .fbx Hawk RF result")
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    rig, = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    def position(name):
        return (rig.matrix_world @ rig.pose.bones[name].matrix).translation
    magazine = position("j_mag1") - position("tag_mag_attach")
    muzzle = position("tag_flash") - position("j_gun__weapon")
    magazine.normalize()
    muzzle.normalize()
    evidence = {"file": str(path), "magazineDirectionZUp": list(magazine),
                "muzzleDirectionZUp": list(muzzle)}
    print("HAWK_ORIENTATION " + json.dumps(evidence), flush=True)
    assert magazine.z < -0.95, "Hawk magazine must extend down, not sideways/up"
    assert muzzle.y < -0.95, "Hawk muzzle must face RF forward (-Y in the Blender scene)"
    print("HAWK_ORIENTATION_PASS", flush=True)


if __name__ == "__main__":
    main()
