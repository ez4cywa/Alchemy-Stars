"""Small backend contract suite. Run via Blender with --disable-autoexec.

blender --background --factory-startup --disable-autoexec --python-exit-code 1 \
    --python scripts/test-ravenfield-adapter.py
Expected physical values below are literals, not computed from the backend table.
"""
import copy
import math
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "blender"))
import ravenfield_adapter as adapter
from mathutils import Matrix, Vector


class AdapterContracts(unittest.TestCase):
    def assertVector(self, actual, expected, tolerance=1e-6):
        self.assertLessEqual((Vector(actual) - Vector(expected)).length, tolerance)

    def config(self):
        return {"sourceUnit": "cm", "handMeshIndices": [0, 1], "weaponBoneNames": ["tag_weapon"]}

    def test_valid_config_defaults_and_explicit_units(self):
        for unit in ("cm", "m", "ft"):
            config = self.config()
            config["sourceUnit"] = unit
            adapter.validate_config(config)
            self.assertEqual(config["idleFrame"], 0)
            self.assertEqual(config["handScale"], 1)
            self.assertEqual(config["left"]["position"], [0, 0, 0])
            self.assertEqual(config["right"]["rotation"], [0, 0, 0])

    def test_units_produce_same_physical_camera_relative_pose(self):
        # Camera origin is one foot on each axis; the sample lies another
        # foot in +X, two feet in +Y and three feet in +Z. All outputs in m.
        for unit, one_foot in (("cm", 30.48), ("m", 0.3048), ("ft", 1.0)):
            with self.subTest(unit=unit):
                camera = Matrix.Translation(Vector((one_foot, one_foot, one_foot)))
                sample = Matrix.Translation(Vector((2 * one_foot, 3 * one_foot, 4 * one_foot)))
                rest = {"j_shoulder_le": Matrix.Translation(Vector((one_foot, 0, 0))),
                        "j_shoulder_ri": Matrix.Translation(Vector((-one_foot, 0, 0)))}
                transform = adapter.source_transform(rest, {"tag_camera": camera}, adapter.UNIT_FACTORS[unit])
                self.assertVector((transform @ sample).translation, (0.3048, 0.6096, 0.9144))
                self.assertVector(transform @ camera.translation, (0, 0, 0))

    def test_yaw_is_camera_relative_and_tag_view_fallback(self):
        rest = {"j_shoulder_le": Matrix.Translation(Vector((0, 1, 0))),
                "j_shoulder_ri": Matrix.Translation(Vector((0, -1, 0)))}
        transform = adapter.source_transform(rest, {"tag_view": Matrix.Translation(Vector((2, 3, 4)))}, 1)
        self.assertVector(transform @ Vector((2, 4, 4)), (1, 0, 0))
        self.assertVector(transform @ Vector((3, 3, 4)), (0, -1, 0))

    def test_both_palms_are_finite_right_handed_orthonormal(self):
        for sign in (-1, 1):
            q = adapter.palm_frame(Vector((0, 0, 0)), Vector((sign * 0.04, 0.1, 0.01)),
                                   Vector((-sign * 0.04, 0.1, 0.01)))
            matrix = q.to_matrix()
            self.assertTrue(all(math.isfinite(v) for row in matrix for v in row))
            self.assertAlmostEqual(matrix.determinant(), 1, places=6)
            for i in range(3):
                self.assertAlmostEqual(matrix.col[i].length, 1, places=6)
                for j in range(i):
                    self.assertAlmostEqual(matrix.col[i].dot(matrix.col[j]), 0, places=6)

    def test_two_bone_solver_preserves_lengths(self):
        shoulder, upper, lower = Vector((0, 0, 0)), 0.3, 0.25
        cases = [(Vector((0.4, 0, 0)), Vector((0, 0, 1)), 0),
                 (Vector((2, 0, 0)), Vector((0, 0, 1)), 0),
                 (Vector((0.01, 0, 0)), Vector((0, 0, 1)), 0),
                 (Vector((0, 0, 0.4)), Vector((0, 0, 1)), 0),
                 (Vector((0.4, 0, 0)), Vector((0, 0, 1)), 90),
                 (Vector((0.4, 0, 0)), Vector((0, 0, 1)), -90)]
        results = []
        for wrist, pole, swivel in cases:
            with self.subTest(wrist=tuple(wrist), swivel=swivel):
                fitted, elbow = adapter.solve_elbow(shoulder, wrist, pole, upper, lower, swivel)
                self.assertTrue(all(math.isfinite(v) for point in (fitted, elbow) for v in point))
                self.assertAlmostEqual((elbow - fitted).length, upper, places=5)
                self.assertAlmostEqual((wrist - elbow).length, lower, places=5)
                results.append((fitted, elbow))
        self.assertVector(results[0][0], shoulder)
        self.assertGreater((results[1][0] - shoulder).length, 1)
        self.assertVector(results[4][0], shoulder)
        self.assertVector(results[5][0], shoulder)
        self.assertAlmostEqual(results[4][1].y, -results[5][1].y, places=6)
        self.assertGreater(abs(results[4][1].y), 0.1)

    def test_invalid_config_is_rejected(self):
        changes = [{"mode": "unknown"}, {"mode": "library"}, {"mode": "library", "clips": []},
                   {"mode": "library", "clips": [{"name": "Idle", "path": "a.cast"}], "referenceClip": 1},
                   {"mode": "library", "clips": [{"name": "Idle", "path": "a.cast"}, {"name": "Idle", "path": "b.cast"}]},
                   {"sourceUnit": "in"}, {"idleFrame": -1}, {"idleFrame": True},
                   {"handScale": -1}, {"handScale": 0}, {"handScale": float("nan")},
                   {"handScale": float("inf")}, {"handScale": True},
                   {"handMeshIndices": []}, {"handMeshIndices": [0, 0]},
                   {"handMeshIndices": [-1]}, {"handMeshIndices": [True]},
                   {"weaponBoneNames": []}, {"weaponBoneNames": [""]},
                   {"left": {"position": [float("nan"), 0, 0]}},
                   {"right": {"rotation": [0, float("inf"), 0]}},
                   {"left": {"position": [0, 0]}},
                   {"left": {"elbowSwivel": 181}}, {"right": {"fingerCurl": -91}}]
        for changeset in changes:
            with self.subTest(changes=changeset):
                config = self.config()
                config.update(copy.deepcopy(changeset))
                with self.assertRaises(ValueError):
                    adapter.validate_config(config)

    def test_degenerate_geometry_is_rejected(self):
        with self.assertRaises(ValueError):
            adapter.palm_frame(Vector((0, 0, 0)), Vector((0, 0, 0)), Vector((0, 0, 0)))
        with self.assertRaises(ValueError):
            adapter.palm_frame(Vector((0, 0, 0)), Vector((0, 1, 0)), Vector((0, 2, 0)))
        with self.assertRaises(ValueError):
            adapter.solve_elbow(Vector((0, 0, 0)), Vector((0, 0, 0)), Vector((0, 1, 0)), 0.3, 0.25, 0)

    def test_elbow_plane_survives_a_straight_source_singularity(self):
        previous = Vector((0, 1, 0))
        for tiny in (1e-8, 0, -1e-8):
            shoulder, elbow = adapter.solve_elbow(Vector((0, 0, 0)), Vector((0.4, 0, 0)),
                Vector((0.2, tiny, 0)), 0.3, 0.25, 0, previous)
            self.assertGreater(elbow.y, 0.1)
            self.assertAlmostEqual((elbow - shoulder).length, 0.3, places=6)
            self.assertAlmostEqual((elbow - Vector((0.4, 0, 0))).length, 0.25, places=6)

    def test_animation_world_bake_and_quaternion_hemisphere(self):
        import bpy
        from mathutils import Quaternion
        from ravenfield_animation import set_world_pose
        bpy.ops.wm.read_factory_settings(use_empty=True)
        data = bpy.data.armatures.new("test")
        rig = bpy.data.objects.new("test", data)
        bpy.context.scene.collection.objects.link(rig)
        adapter.activate(rig)
        bpy.ops.object.mode_set(mode="EDIT")
        root = data.edit_bones.new("root")
        root.head, root.tail = (0, 0, 0), (0, 1, 0)
        child = data.edit_bones.new("child")
        child.head, child.tail = (0, 1, 0), (0, 2, 0)
        child.parent = root
        bpy.ops.object.mode_set(mode="OBJECT")
        previous, last = {}, None
        for degrees in range(175, 187, 2):
            parent = Matrix.LocRotScale(Vector((1, 2, 3)), Quaternion((0, 0, 1), math.radians(degrees)), Vector((1, 1, 1)))
            expected = {"root": parent, "child": parent @ data.bones["child"].matrix_local @ Matrix.Rotation(0.3, 4, "X")}
            set_world_pose(rig, expected, previous)
            for name in expected:
                self.assertLess(max(abs(expected[name][i][j] - rig.pose.bones[name].matrix[i][j]) for i in range(4) for j in range(4)), 1e-5)
            current = rig.pose.bones["root"].rotation_quaternion.copy()
            if last is not None:
                self.assertGreater(current.dot(last), 0.99)
            last = current

    def test_palm_rigid_fit_recovers_motion_without_scale(self):
        from ravenfield_contact import rigid_fit
        from mathutils import Quaternion
        points = [Vector(v) for v in ((0, 0, 0), (.025, 0, 0), (0, .035, .003), (.025, .035, 0))]
        pivot = Vector((-.03, -.02, .01))
        expected = Quaternion((0, 1, 0), math.radians(12))
        translation = Vector((.007, -.003, .004))
        targets = [pivot + expected @ (p-pivot) + translation for p in points]
        rotation, shift, limited = rigid_fit(points, targets, pivot)
        self.assertFalse(limited)
        for point, target in zip(points, targets):
            self.assertVector(pivot + rotation @ (point-pivot) + shift, target)
        self.assertAlmostEqual(rotation.to_matrix().determinant(), 1, places=5)

    def test_palm_limits_and_invalid_samples(self):
        from ravenfield_contact import rigid_fit, MAX_ROTATION_DEG, MAX_SHIFT_M
        from mathutils import Quaternion
        points = [Vector(v) for v in ((0, 0, 0), (.02, 0, 0), (0, .03, 0), (.02, .03, .002))]
        targets = [Quaternion((0, 1, 0), 1.2) @ p + Vector((.5, 0, 0)) for p in points]
        rotation, shift, limited = rigid_fit(points, targets, Vector())
        self.assertTrue(limited)
        self.assertLessEqual(math.degrees(rotation.angle), MAX_ROTATION_DEG + .001)
        self.assertLessEqual(shift.length, MAX_SHIFT_M + 1e-7)
        for actual, wanted in (([(0, 0, 0)]*4, [(0, 0, 0)]*4),
                               ([(math.nan, 0, 0)]*4, points)):
            with self.assertRaises(ValueError): rigid_fit(actual, wanted, Vector())
        with self.assertRaises(ValueError): rigid_fit(points, points, Vector(), [1, -1, 1, 1])
        config = self.config()
        config['contactFit'] = 'yes'
        with self.assertRaises(ValueError): adapter.validate_config(config)

    def test_palm_side_is_anatomical_and_mirrors(self):
        from ravenfield_contact import palm_side
        wrist, index, pinky = Vector(), Vector((.03, .08, 0)), Vector((-.03, .08, 0))
        self.assertEqual(palm_side(wrist, index, pinky, Vector((.04, .02, -.012))), -1)
        self.assertEqual(palm_side(wrist, index, pinky, Vector((.04, .02, .012))), 1)
        with self.assertRaises(ValueError): palm_side(wrist, index, pinky, Vector((.04, .02, 0)))

    def test_library_ranges_hold_gaps_and_preserve_actions(self):
        import bpy
        from ravenfield_animation import compose_timeline
        actions = []
        for name, values in (("first", (0, 1)), ("second", (100, 101, 102))):
            action = bpy.data.actions.new(name)
            curve = action.fcurves.new('pose.bones["root"].location', index=0)
            for frame, value in enumerate(values, 1):
                curve.keyframe_points.insert(frame, value)
            actions.append(action)
        clips = [{"name": "first", "firstFrame": 1, "frameCount": 2}, {"name": "second", "firstFrame": 13, "frameCount": 3}]
        timeline = compose_timeline(actions, clips)
        curve = timeline.fcurves[0]
        self.assertEqual(curve.evaluate(2), 1)
        self.assertEqual(curve.evaluate(12), 1)
        self.assertEqual(curve.evaluate(13), 100)
        self.assertEqual(curve.evaluate(15), 102)
        self.assertEqual(tuple(actions[1].frame_range), (1, 3))

    def test_local_hand_fit_is_opt_in_and_requires_palm_fit(self):
        config = self.config()
        adapter.validate_config(config)
        self.assertFalse(config['localHandFit'])
        for value in ('yes', 1, None):
            config = self.config() | {'localHandFit': value}
            with self.assertRaises(ValueError): adapter.validate_config(config)
        with self.assertRaises(ValueError):
            adapter.validate_config(self.config() | {'localHandFit': True, 'contactFit': False})

    def test_web_control_exact_small_moves_zero_and_hard_limit(self):
        import numpy as np
        from ravenfield_shape import bounded_control_offsets
        a = np.array([[.7, .1, .1, .1], [.1, .7, .1, .1], [.1, .1, .7, .1]])
        for desired in (np.zeros((3, 3)), np.eye(3)*.003):
            offsets, limited = bounded_control_offsets(a, desired)
            self.assertFalse(limited)
            np.testing.assert_allclose(a@offsets, desired, atol=1e-9)
        desired = np.eye(3)*.1
        offsets, limited = bounded_control_offsets(a, desired)
        self.assertTrue(limited)
        self.assertLessEqual(np.linalg.norm(offsets, axis=1).max(), .030000001)
        self.assertLess(np.linalg.norm(a@offsets-desired, axis=1).max(), .1)
        for bad in (np.zeros((3, 4)), np.full((3, 4), np.nan), np.ones((2, 4))):
            with self.assertRaises(ValueError): bounded_control_offsets(bad, desired)

    def test_web_control_equivariant_to_world_rotation(self):
        import numpy as np
        from ravenfield_shape import bounded_control_offsets
        a = np.array([[.7, .1, .1, .1], [.1, .7, .1, .1], [.1, .1, .7, .1]])
        wanted = np.eye(3)*.05
        rotation = np.array(Matrix.Rotation(.72, 3, 'Y'))
        first, _ = bounded_control_offsets(a, wanted)
        second, _ = bounded_control_offsets(a, wanted@rotation.T)
        np.testing.assert_allclose(second, first@rotation.T, atol=1e-8)

    def test_local_system_welds_seams_and_pins_exterior_and_non_hand(self):
        import numpy as np
        from ravenfield_shape import local_system
        points = np.array([[0, 0, 0], [.01, 0, 0], [0, .01, 0], [0, 0, 0], [1, 0, 0]])
        _, weld, active, lookup, h = local_system(points, [(0, 1, 2), (3, 2, 4)], [0, 0, 0], .05,
                                                 [True, False, True, True, True])
        self.assertEqual(weld[0], weld[3])
        self.assertNotIn(int(weld[1]), lookup)
        self.assertNotIn(int(weld[4]), lookup)
        self.assertEqual(len(active), 2)
        self.assertGreater(np.linalg.eigvalsh(h).min(), 0)


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(AdapterContracts)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if not result.wasSuccessful():
        raise RuntimeError("RAVENFIELD_BACKEND_CONTRACTS_FAIL")
    print(f"RAVENFIELD_BACKEND_CONTRACTS_PASS tests={result.testsRun}")
