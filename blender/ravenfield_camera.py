"""Independent, unskinned camera deltas in the RF reference coordinate system.

Sample evaluated world matrices, not isolated local curves: animated parents must
contribute exactly once. One reference pose is shared by the entire library.
"""
import math

from mathutils import Matrix, Quaternion, Vector


BONE_NAME = "RF_CameraMotion"


def rotation_degrees(q):
    # atan2 keeps sub-degree recoil measurable when float32 w rounds to 1.
    return math.degrees(2 * math.atan2(Vector((q.x, q.y, q.z)).length, abs(q.w)))


class CameraMotion:
    def __init__(self, reference_pose, config):
        self.enabled = config.get("cameraMotion", True)
        self.node = next((n for n in ("tag_camera", "tag_cambone", "tag_view") if n in reference_pose), None)
        self.translation_scale = config.get("cameraTranslationScale", 1.0)
        self.rotation_scale = config.get("cameraRotationScale", 1.0)
        for value in (self.translation_scale, self.rotation_scale):
            if not math.isfinite(value) or not 0 <= value <= 2:
                raise ValueError("Camera strength must be finite and between 0 and 2")
        self.reference = reference_pose[self.node].copy() if self.node else None

    @property
    def active(self):
        return self.enabled and self.node is not None

    def sample(self, pose):
        if not self.active:
            return Matrix.Identity(4)
        current = pose[self.node]
        translation = (current.translation - self.reference.translation) * self.translation_scale
        rotation = (current.to_quaternion().normalized()
                    @ self.reference.to_quaternion().normalized().inverted()).normalized()
        # q and -q are the same rotation; scale the shortest rotation, not a 360° arc.
        if rotation.w < 0:
            rotation.negate()
        vector = Vector((rotation.x, rotation.y, rotation.z))
        angle = 2 * math.atan2(vector.length, rotation.w)
        rotation = Quaternion(vector.normalized(), angle * self.rotation_scale) if vector.length > 1e-12 else Quaternion()
        return Matrix.Translation(translation) @ rotation.to_matrix().to_4x4()

    def summary(self, samples):
        positions = [m.translation for m in samples]
        rotations = [m.to_quaternion().normalized() for m in samples]
        displacement = max(((p - positions[0]).length for p in positions), default=0.0)
        rotation_span = max((rotation_degrees(q @ rotations[0].inverted())
                             for q in rotations), default=0.0)
        status = ("disabled" if not self.enabled else "missing-node" if not self.node
                  else "animated" if displacement > 1e-7 or rotation_span > 1e-4 else "constant")
        return {"status": status, "sourceNode": self.node,
                "outputBone": BONE_NAME if self.active else None,
                "sampleCount": len(samples), "translationScale": self.translation_scale,
                "rotationScale": self.rotation_scale,
                "maxTranslationFromFirstM": displacement, "maxRotationFromFirstDegrees": rotation_span,
                "maxOffsetM": max((p.length for p in positions), default=0.0),
                "maxOffsetDegrees": max((rotation_degrees(q)
                                         for q in rotations), default=0.0),
                "basis": "Evaluated world pose relative to the fixed library reference; Z-up meters",
                "note": "Constant may include a static offset; no procedural recoil or FOV is synthesized"}


def add_bone(rig):
    import bpy
    import ravenfield_adapter as rf
    rf.require(BONE_NAME not in rig.data.bones, "Reserved camera output bone name collides")
    rf.activate(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    bone = rig.data.edit_bones.new(BONE_NAME)
    bone.head, bone.tail = (0, 0, 0), (0, .05, 0)
    bone.matrix = Matrix.Identity(4)
    bone.parent = rig.data.edit_bones["RF_Root"]
    # FBX's deform-only filter includes this node, but no vertex is weighted to it.
    bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")


def attach_preview(rig, camera):
    if BONE_NAME not in rig.data.bones:
        return
    constraint = camera.constraints.new("CHILD_OF")
    constraint.name = "RF camera motion (independent of weapon skin)"
    constraint.target = rig
    constraint.subtarget = BONE_NAME
    constraint.inverse_matrix = Matrix.Identity(4)
