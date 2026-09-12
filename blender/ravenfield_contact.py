"""Match stable, anatomical palm surface samples; never chase nearest vertices per frame.

Samples are captured once on the bind meshes and carried by triangle barycentrics.
The source hand therefore defines contact/release motion even during magazine work.
"""
from __future__ import annotations

import math
import bpy
import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

import ravenfield_adapter as rf

SAMPLES = ((-.25, .4), (0., .4), (-.25, .7), (0., .7))
TOLERANCE_M = .005
MAX_ROTATION_DEG = 25.
MAX_SHIFT_M = .04


def contact_warning(detail):
    maximum = max(detail[side]['maxErrorM'] for side in ('left', 'right'))
    if maximum > TOLERANCE_M:
        return (f'握持贴合未达标：最大对应点误差 {maximum*1000:.2f} mm，目标 5 mm；请检查报告 / '
                f'Grip fit exceeds tolerance: {maximum*1000:.2f} mm, target 5 mm; inspect report')
    return None


def evaluated_mesh(obj, transform, triangles=False):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    try:
        values = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
        mesh.vertices.foreach_get('co', values)
        world = np.asarray(transform @ evaluated.matrix_world)
        points = values.reshape((-1, 3)) @ world[:3, :3].T + world[:3, 3]
        if not triangles:
            return points
        mesh.calc_loop_triangles()
        faces = [tuple(t.vertices) for t in mesh.loop_triangles]
        groups = [[(g.group, g.weight) for g in v.groups] for v in mesh.vertices]
        return points, faces, groups
    finally:
        evaluated.to_mesh_clear()


def rigid_fit(actual, wanted, pivot, weights=None):
    """A proper rotation (never reflection/scale), bounded about the hand wrist."""
    actual, wanted = np.asarray(actual, dtype=float), np.asarray(wanted, dtype=float)
    rf.require(actual.shape == wanted.shape and actual.ndim == 2 and actual.shape[1] == 3
               and len(actual) >= 3 and np.isfinite(actual).all() and np.isfinite(wanted).all(),
               'Invalid palm contact samples')
    weights = np.ones(len(actual)) if weights is None else np.asarray(weights, dtype=float)
    rf.require(weights.shape == (len(actual),) and np.isfinite(weights).all() and (weights > 0).all(),
               'Invalid palm sample weights')
    weights = weights / weights.sum()
    a, b = (actual*weights[:, None]).sum(axis=0), (wanted*weights[:, None]).sum(axis=0)
    u, singular, vt = np.linalg.svd(((actual-a)*weights[:, None]).T @ (wanted-b))
    rf.require(singular[1] > 1e-10, 'Palm contact samples are collinear')
    fix = np.eye(3)
    fix[2, 2] = np.linalg.det(vt.T @ u.T)
    rotation = Matrix(vt.T @ fix @ u.T).to_quaternion().normalized()
    if rotation.w < 0: rotation.negate()
    limited = rotation.angle > math.radians(MAX_ROTATION_DEG)
    if limited:
        rotation = Quaternion().slerp(rotation, math.radians(MAX_ROTATION_DEG) / rotation.angle)
    shift = Vector(b) - (rotation @ (Vector(a)-pivot) + pivot)
    if shift.length > MAX_SHIFT_M:
        shift = shift.normalized() * MAX_SHIFT_M
        limited = True
    return rotation, shift, limited


def palm_side(wrist, index, pinky, thumb):
    frame = rf.palm_frame(wrist, index, pinky)
    offset = (frame.inverted() @ (thumb-wrist)).z
    rf.require(abs(offset) > (index-pinky).length*.02, 'Thumb root does not distinguish palm from back of hand')
    return 1 if offset > 0 else -1


class PalmContactFitter:
    def __init__(self, source, target, source_meshes, target_meshes, transform):
        self.source, self.target = source, target
        self.transform = transform
        self.meshes = {'source': source_meshes, 'rf': target_meshes}
        self.anchors = {}
        for kind, rig, matrix in (('source', source, transform), ('rf', target, Matrix.Identity(4))):
            previous = rig.data.pose_position
            try:
                rig.data.pose_position = 'REST'
                bpy.context.view_layer.update()
                rest = {n: matrix @ m for n, m in rf.armature_rest(rig).items()}
                geometry = {obj: evaluated_mesh(obj, matrix, True) for obj in self.meshes[kind]}
                for side in ('le', 'ri'):
                    wrist_name, index_name, pinky_name = self.names(kind, side)
                    wrist, index, pinky = [rest[n].translation for n in (wrist_name, index_name, pinky_name)]
                    q = rf.palm_frame(wrist, index, pinky)
                    thumb = f'j_thumb_{side}_1' if kind == 'source' else ('Thumb.L' if side == 'le' else 'Thumb.R')
                    sign = palm_side(wrist, index, pinky, rest[thumb].translation)
                    length, width = ((index+pinky)*.5-wrist).length, (index-pinky).length
                    descendants = {wrist_name, *[b.name for b in rig.data.bones[wrist_name].children_recursive]}
                    surfaces = []
                    for obj, (points, triangles, groups) in geometry.items():
                        allowed = {g.index for g in obj.vertex_groups if g.name in descendants}
                        weights = [sum(w for i, w in gs if i in allowed) for gs in groups]
                        triangles = [tri for tri in triangles if min(weights[i] for i in tri) > .25]
                        if triangles:
                            surfaces.append((obj, points, triangles,
                                             BVHTree.FromPolygons(points, triangles, all_triangles=True)))
                    anchors = []
                    for x, y in SAMPLES:
                        center = wrist + q @ Vector((x*width, y*length, 0))
                        direction = q @ Vector((0, 0, sign))
                        hits = []
                        for obj, points, triangles, bvh in surfaces:
                            point, normal, face, distance = bvh.ray_cast(center+direction*length, -direction, length*2)
                            if point is not None and normal.dot(direction) > .05:
                                hits.append((distance, obj, points, triangles[face], point))
                        rf.require(hits, f'No anatomical palm surface: {kind}/{side}/{x}/{y}')
                        _, obj, points, triangle, hit = min(hits, key=lambda h: h[0])
                        weights = barycentric_transform(hit, *[Vector(points[i]) for i in triangle],
                                                        Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
                        anchors.append((obj, triangle, weights))
                    self.anchors[kind, side] = anchors
            finally:
                rig.data.pose_position = previous
                bpy.context.view_layer.update()

    @staticmethod
    def names(kind, side):
        if kind == 'source':
            return [f'j_wrist_{side}', f'j_index_{side}_1', f'j_pinky_{side}_1']
        suffix = 'L' if side == 'le' else 'R'
        return [f'Hand.{suffix}', f'Index.{suffix}', f'Pinky.{suffix}']

    def points(self, kind):
        transform = self.transform if kind == 'source' else Matrix.Identity(4)
        objects = {a[0] for side in ('le', 'ri') for a in self.anchors[kind, side]}
        values = {obj: evaluated_mesh(obj, transform) for obj in objects}
        result = {}
        for side in ('le', 'ri'):
            result[side] = np.array([sum((values[obj][i]*w for i, w in zip(ids, weights)), np.zeros(3))
                                     for obj, ids, weights in self.anchors[kind, side]])
        return result

    def fit(self, pose, rest, config, report, continuity=None):
        # Baseline does not advance elbow continuity; only the final fitted pose does.
        rf.fit_hands(self.target, pose, rest, config, {'warnings': []},
                     dict(continuity) if continuity is not None else None)
        source_points, before = self.points('source'), self.points('rf')
        corrections, wanted, details = {}, {}, {}
        best, best_errors = {}, {}
        for side, setting_name in (('le', 'left'), ('ri', 'right')):
            setting = config[setting_name]
            wrist = pose[f'j_wrist_{side}'].translation
            user = Euler([math.radians(v) for v in setting['rotation']], 'XYZ').to_quaternion()
            offset = Vector(setting['position'])
            desired = np.array([list(wrist+offset+user@(Vector(p)-wrist)) for p in source_points[side]])
            pivot = wrist+offset
            rotation, shift, limited = rigid_fit(before[side], desired, pivot)
            corrections[setting_name] = (rotation, shift)
            wanted[side] = desired
            best[setting_name] = (Quaternion(), Vector())
            best_errors[setting_name] = float(np.linalg.norm(before[side]-desired, axis=1).max())
            details[setting_name] = {'beforeMaxErrorM': float(np.linalg.norm(before[side]-desired, axis=1).max()),
                                     'rotationDegrees': math.degrees(rotation.angle), 'shiftM': list(shift),
                                     'limited': limited}
        # A palm vertex can also carry proximal-finger/wrist weights. Re-measure
        # the real skin, instead of mistaking a rigid point prediction for a pass.
        for _ in range(3):
            rf.fit_hands(self.target, pose, rest, config, {'warnings': []},
                         dict(continuity) if continuity is not None else None, corrections)
            measured = self.points('rf')
            for side, setting_name in (('le', 'left'), ('ri', 'right')):
                pivot = pose[f'j_wrist_{side}'].translation + Vector(config[setting_name]['position'])
                errors = np.linalg.norm(measured[side]-wanted[side], axis=1)
                if errors.max() < best_errors[setting_name]:
                    best_errors[setting_name] = float(errors.max())
                    best[setting_name] = tuple(v.copy() for v in corrections[setting_name])
                increment, delta, limited = rigid_fit(measured[side], wanted[side], pivot,
                                                     1+(errors/TOLERANCE_M)**2)
                old_rotation, old_shift = corrections[setting_name]
                rotation, shift = increment @ old_rotation, increment @ old_shift + delta
                if rotation.w < 0: rotation.negate()
                if rotation.angle > math.radians(MAX_ROTATION_DEG):
                    rotation = Quaternion().slerp(rotation, math.radians(MAX_ROTATION_DEG)/rotation.angle)
                    # Recenter after a total-angle clamp; the unclamped delta
                    # otherwise rotates the palm away from its target centroid.
                    relative = rotation @ old_rotation.inverted()
                    shift = Vector(wanted[side].mean(axis=0))-pivot-relative@(Vector(measured[side].mean(axis=0))-pivot-old_shift)
                    limited = True
                if shift.length > MAX_SHIFT_M:
                    shift = shift.normalized()*MAX_SHIFT_M
                    limited = True
                corrections[setting_name] = rotation, shift
                details[setting_name].update(rotationDegrees=math.degrees(rotation.angle), shiftM=list(shift),
                                             limited=details[setting_name]['limited'] or limited)
        rf.fit_hands(self.target, pose, rest, config, {'warnings': []},
                     dict(continuity) if continuity is not None else None, corrections)
        measured = self.points('rf')
        for side, setting_name in (('le', 'left'), ('ri', 'right')):
            if np.linalg.norm(measured[side]-wanted[side], axis=1).max() < best_errors[setting_name]:
                best[setting_name] = tuple(v.copy() for v in corrections[setting_name])
            rotation, shift = best[setting_name]
            details[setting_name].update(rotationDegrees=math.degrees(rotation.angle), shiftM=list(shift),
                                         rotationQuaternion=list(rotation))
        rf.fit_hands(self.target, pose, rest, config, report, continuity, best)
        after = self.points('rf')
        for side, setting_name in (('le', 'left'), ('ri', 'right')):
            errors = np.linalg.norm(after[side]-wanted[side], axis=1)
            rf.require(errors.max() <= details[setting_name]['beforeMaxErrorM'] + 1e-5,
                       f'{setting_name} palm correction worsened the measured baseline')
            details[setting_name].update(maxErrorM=float(errors.max()), rmsErrorM=float(np.sqrt(np.mean(errors**2))),
                                         passed=bool(errors.max() <= TOLERANCE_M),
                                         sourcePointsM=wanted[side].tolist(), beforePointsM=before[side].tolist(),
                                         afterPointsM=after[side].tolist())
        report['palmContact'] = {'method': 'bind-surface-barycentric-rigid', 'toleranceM': TOLERANCE_M,
                                 'scope': 'Four central/ulnar palm samples; not fingers, thenar or collision-free geometry',
                                 'passed': all(details[s]['passed'] for s in ('left', 'right')),
                                 **details}
        return report['palmContact']
