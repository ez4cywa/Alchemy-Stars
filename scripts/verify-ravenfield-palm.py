"""Real-mesh palm regression/visual inspection (run inside Blender).

--config is the RF engine's prepared config; --input is its complete Z-up CAST.
Writes separate before/after scenes, closeups and an independently sampled report.
Does not modify source assets. --require-pass makes the 5 mm target a hard gate.
"""
import argparse
import json
import sys
import tempfile
import copy
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'blender'))
import ravenfield_adapter as rf
from ravenfield_contact import PalmContactFitter, evaluated_mesh, TOLERANCE_M


def main():
    parser = argparse.ArgumentParser()
    for name in ('input', 'config', 'rf', 'output'):
        parser.add_argument('--'+name, type=Path, required=True)
    parser.add_argument('--frame', type=int, default=0)
    parser.add_argument('--require-pass', action='store_true')
    parser.add_argument('--baseline-only', action='store_true', help='Measure the old wrist-only fit, without applying contact correction')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    rf.require(not any(p.resolve().is_relative_to(output) for p in (args.input, args.config, args.rf)),
               'Inspection output must not contain source inputs')
    config = json.loads(args.config.read_text(encoding='utf-8-sig'))
    config.update(mode='pose', idleFrame=args.frame)
    rf.validate_config(config)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    report = {'warnings': []}
    with tempfile.TemporaryDirectory(prefix='rf-palm-check-') as directory:
        temporary = Path(directory)
        cod, weapons, _ = rf.load_cast(args.input.resolve(), temporary, config, report)
        factor = rf.UNIT_FACTORS[config['sourceUnit']]
        transform = rf.source_transform(rf.armature_rest(cod), rf.armature_pose(cod), factor)
        pose = {n: transform @ m for n, m in rf.armature_pose(cod).items()}
        rest = {n: transform @ m for n, m in rf.armature_rest(cod).items()}
        source = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('COD_ReferenceHands_')]
        path, _ = rf.extract_rf_source(args.rf.resolve(), temporary)
        target, hands = rf.load_rf(path, config['handScale'])
        fitter = PalmContactFitter(cod, target, source, hands, transform)
        rf.fit_hands(target, pose, rest, config, report)
        baseline = {b.name: b.matrix.copy() for b in target.pose.bones}
        if args.baseline_only:
            wanted, actual = fitter.points('source'), fitter.points('rf')
            maximum = max(float(np.linalg.norm(wanted[s]-actual[s], axis=1).max()) for s in ('le', 'ri'))
            print(f'PALM_BASELINE_MEASURED {maximum*1000:.3f} mm')
            if args.require_pass:
                rf.require(maximum <= TOLERANCE_M, f'PALM_CONTACT_FAIL {maximum*1000:.3f} mm exceeds 5 mm')
            return
        detail = fitter.fit(pose, rest, config, report)
        corrected = {b.name: b.matrix.copy() for b in target.pose.bones}
        # User offsets remain a deliberate target change, not something the
        # automatic solver silently cancels to get a smaller error number.
        adjusted = copy.deepcopy(config)
        adjusted['left']['position'][0] += .01
        adjusted['right']['position'][0] -= .01
        check = fitter.fit(pose, rest, adjusted, {'warnings': []})
        for side, delta in (('left', .01), ('right', -.01)):
            change = np.asarray(check[side]['sourcePointsM'])-np.asarray(detail[side]['sourcePointsM'])
            rf.require(np.max(np.abs(change-np.array((delta, 0, 0)))) < 1e-6, 'User position offset was canceled')
        fitter.fit(pose, rest, config, {'warnings': []})
        vertices, faces = [], []
        for obj in weapons:
            values, triangles, _ = evaluated_mesh(obj, transform, True)
            offset = len(vertices)
            vertices.extend(values)
            faces.extend(tuple(offset+i for i in face) for face in triangles)
        weapon = BVHTree.FromPolygons(vertices, faces, all_triangles=True)
        for side in ('left', 'right'):
            item = detail[side]
            for kind in ('source', 'before', 'after'):
                item[kind+'WeaponGapM'] = [weapon.find_nearest(Vector(p))[3] for p in item[kind+'PointsM']]
            independent = np.linalg.norm(np.asarray(item['sourcePointsM'])-np.asarray(item['afterPointsM']), axis=1)
            rf.require(abs(independent.max()-item['maxErrorM']) < 1e-9, 'Incorrect contact report maximum')
            rf.require(item['maxErrorM'] <= item['beforeMaxErrorM']+1e-5, 'Contact fit worsened the baseline')
        (output/'verification.json').write_text(json.dumps(detail, indent=2), encoding='utf-8')
        # Transform source roots once, for comparable editable inspection scenes.
        for obj in list(bpy.context.scene.objects):
            if obj == cod or (obj in source+weapons and obj.parent is None):
                obj.matrix_world = transform @ obj.matrix_world
        rf.configure_scene(target, hands+weapons)
        scene = bpy.context.scene
        scene.render.resolution_x, scene.render.resolution_y = 800, 700
        scene.display.shading.color_type = 'OBJECT'
        for obj in source: obj.color = (.15, .7, .35, 1)
        for obj in hands: obj.color = (.25, .5, .85, 1)
        for obj in weapons: obj.color = (.35, .38, .42, 1)
        for obj in scene.objects:
            if obj.type == 'ARMATURE': obj.hide_render = True
        for variant, matrices in (('before', baseline), ('after', corrected)):
            for bone in target.pose.bones:
                bone.matrix = matrices[bone.name]
                bpy.context.view_layer.update()
            for obj in source+hands: obj.hide_render = False
            bpy.ops.wm.save_as_mainfile(filepath=str(output/(variant+'.blend')))
            inspect_camera = scene.camera
            scene.camera = bpy.data.objects['RF First Person']
            for obj in source: obj.hide_render = True
            scene.render.filepath = str(output/('first-person-'+variant+'.png'))
            bpy.ops.render.render(write_still=True)
            if variant == 'before':
                for obj in source: obj.hide_render = False
                for obj in hands: obj.hide_render = True
                scene.render.filepath = str(output/'first-person-source.png')
                bpy.ops.render.render(write_still=True)
            scene.camera = inspect_camera
            for side, cod_side in (('left', 'le'), ('right', 'ri')):
                center = Vector(np.mean(detail[side]['sourcePointsM'], axis=0))
                scene.camera.location = center+Vector((-.25, .15, .12))
                scene.camera.rotation_euler = (center-scene.camera.location).to_track_quat('-Z', 'Y').to_euler()
                scene.camera.data.ortho_scale = .23*config['handScale']
                for obj in source: obj.hide_render = True
                for obj in hands: obj.hide_render = False
                scene.render.filepath = str(output/(side+'-'+variant+'.png'))
                bpy.ops.render.render(write_still=True)
                if variant == 'before':
                    for obj in source: obj.hide_render = False
                    for obj in hands: obj.hide_render = True
                    scene.render.filepath = str(output/(side+'-source.png'))
                    bpy.ops.render.render(write_still=True)
        maximum = max(detail[s]['maxErrorM'] for s in ('left', 'right'))
        print('PALM_CONTACT_MEASURED', json.dumps({s: {k: detail[s][k] for k in ('beforeMaxErrorM', 'maxErrorM', 'passed')}
                                                  for s in ('left', 'right')}))
        if args.require_pass:
            rf.require(maximum <= TOLERANCE_M, f'PALM_CONTACT_FAIL {maximum*1000:.3f} mm exceeds 5 mm')
            print('PALM_CONTACT_PASS')


if __name__ == '__main__':
    main()
