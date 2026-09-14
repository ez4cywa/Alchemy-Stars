"""Compare prior library assets against an additive camera-enabled export in Blender."""
import argparse
import json
from pathlib import Path
import sys

import bpy
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument('--before', required=True)
parser.add_argument('--after', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])


def capture(path, frames=None):
    bpy.ops.wm.open_mainfile(filepath=str(Path(path).resolve()))
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    report = json.loads(rig['ravenfield_adapter'])
    if frames is None:
        frames = sorted({f for c in report['clips'] for f in (c['firstFrame'], (c['firstFrame']+c['lastFrame'])//2, c['lastFrame'])})
    bones = {b.name: (b.parent.name if b.parent else None, np.asarray(b.matrix_local).copy()) for b in rig.data.bones if b.name != 'RF_CameraMotion'}
    meshes = sorted((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: o.name)
    geometry = {o.name: (np.array([v.co[:] for v in o.data.vertices]),
                         [[(o.vertex_groups[g.group].name, g.weight) for g in v.groups] for v in o.data.vertices]) for o in meshes}
    poses, skins = {}, {}
    for f in frames:
        bpy.context.scene.frame_set(f)
        poses[f] = np.array([rig.pose.bones[n].matrix[:] for n in sorted(bones)])
        graph = bpy.context.evaluated_depsgraph_get()
        skins[f] = {}
        for obj in meshes:
            evaluated = obj.evaluated_get(graph); mesh = evaluated.to_mesh()
            skins[f][obj.name] = np.array([v.co[:] for v in mesh.vertices])
            evaluated.to_mesh_clear()
    return bones, geometry, poses, skins, report, frames


old = capture(args.before)
new = capture(args.after, old[-1])
assert old[0].keys() == new[0].keys(), 'Existing bone names changed'
for name, (parent, rest) in old[0].items():
    assert new[0][name][0] == parent, 'Existing hierarchy changed: '+name
    assert np.max(np.abs(rest-new[0][name][1])) < 1e-6, 'Existing bind changed: '+name
assert old[1].keys() == new[1].keys(), 'Mesh names changed'
for name, (vertices, weights) in old[1].items():
    assert np.max(np.abs(vertices-new[1][name][0])) < 1e-6, 'Bind mesh changed: '+name
    assert weights == new[1][name][1], 'Skin weights changed: '+name
for before, after in zip(old[4]['clips'], new[4]['clips']):
    for field in ('name', 'firstFrame', 'lastFrame', 'takeName'):
        assert before[field] == after[field], 'Existing clip structure changed'
pose_error = max(float(np.max(np.abs(old[2][f]-new[2][f]))) for f in old[-1])
skin_error = max(float(np.max(np.linalg.norm(old[3][f][n]-new[3][f][n], axis=1))) for f in old[-1] for n in old[1])
assert pose_error < 1e-6 and skin_error < 1e-6, (pose_error, skin_error)
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
assert rig.data.bones['RF_CameraMotion'].parent.name == 'RF_Root'
assert all('RF_CameraMotion' not in [name for name, _ in groups] for _, weights in new[1].values() for groups in weights)
print('CAMERA_COMPATIBILITY_PASS '+json.dumps({'unchangedExistingBones':len(old[0]), 'unchangedMeshes':len(old[1]),
      'sampledPriorFrames':old[-1], 'maxPoseMatrixError':pose_error, 'maxSkinErrorM':skin_error,
      'clips':[{'name':c['name'], 'camera':c['cameraMotion']} for c in new[4]['clips']]}))
