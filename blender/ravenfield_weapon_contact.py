"""Reference material contacts with authored release, not nearest-surface chasing."""
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
from ravenfield_contact import evaluated_mesh


def release_weight(movement):
    """Keep contact at rest and smoothly release between 6 and 24 mm travel."""
    t = np.clip((movement-.006)/.018, 0, 1)
    return float(1-t*t*(3-2*t))


class SupportContacts:
    def __init__(self, samples, transform):
        self.transform = transform
        self.anchors = []
        self.reference_gaps = []
        self.normal_signs = []
        self.reason = 'reference-contact-captured'
        weapons = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith('COD_Weapon')]
        surfaces = []
        for obj in weapons:
            points, triangles, _ = evaluated_mesh(obj, transform, True)
            surfaces.append((obj, points, triangles, BVHTree.FromPolygons(points, triangles, all_triangles=True)))
        for index, source in enumerate(samples.points('source')['le']):
            candidates = []
            for obj, points, triangles, tree in surfaces:
                point, normal, face, distance = tree.find_nearest(Vector(source))
                if point is not None:
                    candidates.append((distance, obj, points, triangles[face], point))
            if not candidates:
                self.anchors = []
                self.reason = 'no-weapon-surface'
                break
            distance, obj, points, triangle, point = min(candidates, key=lambda value: value[0])
            if distance > (.012 if index < 4 else .035):
                self.anchors = []
                self.reason = 'reference-palm-too-far-from-weapon' if index < 4 else 'reference-web-too-far-from-weapon'
                break
            bary = barycentric_transform(point, *[Vector(points[i]) for i in triangle],
                                         Vector((1,0,0)), Vector((0,1,0)), Vector((0,0,1)))
            self.anchors.append((obj, triangle, bary))
            self.reference_gaps.append(distance)
            a,b,c = [np.asarray(points[i]) for i in triangle]
            self.normal_signs.append(1. if np.dot(np.cross(b-a,c-a),source-np.asarray(point)) >= 0 else -1.)

    def describe(self):
        return {'enabled': len(self.anchors)==7, 'reason': self.reason,
                'referenceGapsM': self.reference_gaps, 'clearanceM': -.0015,
                'coverage': 'four-palm-and-three-web-material-points',
                'captureRadiusM': .012, 'webCaptureRadiusM': .035, 'releaseStartM': .006, 'releaseEndM': .024,
                'anchors': [{'mesh':obj.name,'triangle':list(triangle),'barycentric':list(bary)}
                            for obj,triangle,bary in self.anchors]}

    def targets(self, authored):
        if not self.anchors:
            return authored, {'active': False, 'weight': 0.}
        geometry = {obj: evaluated_mesh(obj, self.transform) for obj in {a[0] for a in self.anchors}}
        anchors = np.array([sum((geometry[obj][i]*w for i,w in zip(triangle,bary)),np.zeros(3))
                            for obj,triangle,bary in self.anchors])
        vectors = authored - anchors
        distances = np.linalg.norm(vectors, axis=1)
        # Release remains driven by the original four support contacts. The
        # intentionally wider web target must not alter authored release timing.
        movement = float(np.max(np.maximum(0, distances[:4] - np.array(self.reference_gaps[:4]))))
        weight = release_weight(movement)
        normals=[]
        for (obj,triangle,_),sign in zip(self.anchors,self.normal_signs):
            a,b,c=[geometry[obj][i] for i in triangle]
            normal=np.cross(b-a,c-a)*sign
            normals.append(normal/max(np.linalg.norm(normal),1e-12))
        contact = anchors - np.asarray(normals)*.0015
        result = authored.copy()
        result += weight*(contact-authored)
        return result, {'active': weight > 0, 'weight': weight, 'clearanceM': -.0015,
                        'authoredAnchorDistancesM': distances.tolist(),
                        'weaponAnchorPointsM': anchors.tolist(),
                        'maxTargetShiftM': float(np.linalg.norm(result-authored,axis=1).max())}
