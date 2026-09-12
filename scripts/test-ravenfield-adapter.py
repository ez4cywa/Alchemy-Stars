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
        changes = [{"sourceUnit": "in"}, {"idleFrame": -1}, {"idleFrame": True},
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


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(AdapterContracts)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if not result.wasSuccessful():
        raise RuntimeError("RAVENFIELD_BACKEND_CONTRACTS_FAIL")
    print(f"RAVENFIELD_BACKEND_CONTRACTS_PASS tests={result.testsRun}")
