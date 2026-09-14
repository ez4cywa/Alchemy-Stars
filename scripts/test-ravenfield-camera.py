"""Blender --background --factory-startup --python-exit-code 1 --python this_file."""
import math
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'blender'))
import bpy
from mathutils import Matrix, Vector
from ravenfield_camera import CameraMotion, BONE_NAME, add_bone, attach_preview


class CameraContracts(unittest.TestCase):
    def assertMatrix(self, a, b):
        self.assertLess(max(abs(a[i][j]-b[i][j]) for i in range(4) for j in range(4)), 2e-6)

    def test_reference_identity_and_parent_rotation_translation(self):
        # A fixed local camera under a moving parent must still produce motion.
        local = Matrix.Translation((0, 0, 2)) @ Matrix.Rotation(.7, 4, 'Y')
        capture = CameraMotion({'tag_camera': local}, {})
        self.assertMatrix(capture.sample({'tag_camera': local}), Matrix.Identity(4))
        parent = Matrix.Translation((.1, -.2, .3)) @ Matrix.Rotation(.25, 4, 'Z')
        expected = Matrix.Translation((.1, -.2, .3)) @ Matrix.Rotation(.25, 4, 'Z')
        self.assertMatrix(capture.sample({'tag_camera': parent @ local}), expected)

    def test_units_once_and_strengths(self):
        for factor in (.01, .3048, 1):
            base = Matrix.Scale(factor, 4)
            current = base @ Matrix.Translation((1/factor, 0, 0)) @ Matrix.Rotation(.2, 4, 'X')
            capture = CameraMotion({'tag_camera': base}, {'cameraTranslationScale': .5, 'cameraRotationScale': 2})
            self.assertMatrix(capture.sample({'tag_camera': current}),
                              Matrix.Translation((.5, 0, 0)) @ Matrix.Rotation(.4, 4, 'X'))

    def test_disabled_missing_constant_and_invalid(self):
        for config, reference, status in [({'cameraMotion': False}, {'tag_camera': Matrix.Identity(4)}, 'disabled'),
                                           ({}, {}, 'missing-node'), ({}, {'tag_view': Matrix.Identity(4)}, 'constant')]:
            capture = CameraMotion(reference, config)
            samples = [capture.sample(reference)] * 3
            self.assertEqual(capture.summary(samples)['status'], status)
        for value in (-1, 2.1, float('nan'), float('inf')):
            with self.assertRaises(ValueError):
                CameraMotion({}, {'cameraRotationScale': value})
        capture = CameraMotion({'tag_camera': Matrix.Identity(4)}, {'cameraTranslationScale': 0, 'cameraRotationScale': 0})
        self.assertMatrix(capture.sample({'tag_camera': Matrix.Translation((1, 2, 3)) @ Matrix.Rotation(1, 4, 'Z')}), Matrix.Identity(4))

    def test_summary_uses_shared_reference_not_per_clip_reset(self):
        capture = CameraMotion({'tag_camera': Matrix.Identity(4)}, {})
        samples = [capture.sample({'tag_camera': Matrix.Translation((.1, 0, 0))})] * 4
        summary = capture.summary(samples)
        self.assertEqual(summary['status'], 'constant')
        self.assertAlmostEqual(summary['maxOffsetM'], .1, places=6)
        samples.append(capture.sample({'tag_camera': Matrix.Rotation(.01, 4, 'Y')}))
        self.assertEqual(capture.summary(samples)['status'], 'animated')

    def test_tiny_recoil_is_not_rounded_away(self):
        capture = CameraMotion({'tag_camera': Matrix.Identity(4)}, {})
        samples = [Matrix.Identity(4), capture.sample({'tag_camera': Matrix.Rotation(math.radians(.005), 4, 'X')})]
        self.assertAlmostEqual(capture.summary(samples)['maxOffsetDegrees'], .005, places=5)
        self.assertEqual(capture.summary([samples[1]] * 5)['status'], 'constant')

    def test_bone_noninterference_and_preview_follow(self):
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.object.delete(use_global=False)
        data = bpy.data.armatures.new('Test')
        rig = bpy.data.objects.new('Test', data)
        bpy.context.scene.collection.objects.link(rig)
        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        root = data.edit_bones.new('RF_Root'); root.head=(0, 0, 0); root.tail=(0, 0, .05)
        hand = data.edit_bones.new('Hand.L'); hand.head=(.1, 0, 0); hand.tail=(.1, .1, 0); hand.parent=root
        bpy.ops.object.mode_set(mode='OBJECT')
        original = data.bones['Hand.L'].matrix_local.copy()
        add_bone(rig)
        self.assertMatrix(data.bones['Hand.L'].matrix_local, original)
        self.assertEqual(data.bones['Hand.L'].parent.name, 'RF_Root')
        self.assertMatrix(data.bones[BONE_NAME].matrix_local, Matrix.Identity(4))
        camera = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
        bpy.context.scene.collection.objects.link(camera)
        camera.rotation_euler = Vector((0, -1, 0)).to_track_quat('-Z', 'Y').to_euler()
        bpy.context.view_layer.update()
        neutral = camera.matrix_world.copy()
        attach_preview(rig, camera)
        bpy.context.view_layer.update()
        self.assertMatrix(camera.matrix_world, neutral)
        delta = Matrix.Translation((.1, .2, .3)) @ Matrix.Rotation(.2, 4, 'X')
        rig.pose.bones[BONE_NAME].matrix = delta
        bpy.context.view_layer.update()
        self.assertMatrix(camera.matrix_world, delta @ neutral)


suite = unittest.defaultTestLoader.loadTestsFromTestCase(CameraContracts)
if not unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful():
    raise RuntimeError('Camera motion contracts failed')
