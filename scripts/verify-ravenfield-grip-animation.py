"""Evaluate the actual fitted skin against the Idle 5 mm gate, before FBX bake.

Run in Blender with --disable-autoexec. Reference calibration is always captured
from the configured reference clip, not recalibrated on the tested failure frame.
"""
import argparse
import json
from pathlib import Path
import sys
import tempfile
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'blender'))
import ravenfield_adapter as rf
from ravenfield_animation import import_action, read_clip
from ravenfield_shape import prepare_grip


def main():
    parser = argparse.ArgumentParser()
    for name in ('config', 'rf', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--all-frames', action='store_true')
    parser.add_argument('--require-pass', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    config = json.loads(args.config.read_text(encoding='utf-8-sig'))
    rf.validate_config(config)
    source_path = Path(config['clips'][config['referenceClip']]['path'])
    inputs = {args.config.resolve(), args.rf.resolve(), *[Path(c['path']).resolve() for c in config['clips']]}
    rf.require((args.output / 'grip.json').resolve() not in inputs, 'Grip report would overwrite an input')
    args.output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    report = {'warnings': [], 'frames': []}
    with tempfile.TemporaryDirectory(prefix='rf-grip-regression-') as folder:
        temporary = Path(folder)
        cod, _, _ = rf.load_cast(source_path, temporary, config, report)
        transform = rf.source_transform(rf.armature_rest(cod), rf.armature_pose(cod), rf.UNIT_FACTORS[config['sourceUnit']])
        pose = {n: transform @ m for n, m in rf.armature_pose(cod).items()}
        rest = {n: transform @ m for n, m in rf.armature_rest(cod).items()}
        source_meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('COD_ReferenceHands_')]
        path, _ = rf.extract_rf_source(args.rf.resolve(), temporary)
        target, hands = rf.load_rf(path, config['handScale'])
        fitter = prepare_grip(cod, target, source_meshes, hands, transform, pose, rest, config, report)
        _, signature = read_clip(source_path)
        cases = {'Idle': [0], 'Sprint': [24], 'Reload': [68], 'reload_empty': [16, 81], 'Inspect': [59, 172]}
        for clip in config['clips']:
            action = import_action(Path(clip['path']), cod, temporary, signature)
            start, end = (int(round(v)) for v in action.frame_range)
            continuity = {}
            frames = range(start, end + 1) if args.all_frames else [start + n for n in cases[clip['name']]]
            for frame in frames:
                bpy.context.scene.frame_set(frame)
                for bone in target.pose.bones:
                    bone.matrix_basis.identity()
                bpy.context.view_layer.update()
                pose = {n: transform @ m for n, m in rf.armature_pose(cod).items()}
                detail = fitter.fit(pose, rest, config, {'warnings': []}, continuity)
                report['frames'].append({'clip': clip['name'], 'sourceFrame': frame, 'detail': detail})
        report['passed'] = all(f['detail']['passed'] for f in report['frames'])
        report['maxErrorM'] = max(f['detail'][s]['maxErrorM'] for f in report['frames'] for s in ('left', 'right'))
        report['failedFrames'] = sum(not f['detail']['passed'] for f in report['frames'])
        (args.output / 'grip.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print('GRIP_ACTUAL_SKIN_GATE', {k: report[k] for k in ('passed', 'failedFrames', 'maxErrorM')}, flush=True)
        if args.require_pass:
            rf.require(report['passed'], 'Animation grip must meet the same 5 mm gate as Idle')


if __name__ == '__main__':
    main()
