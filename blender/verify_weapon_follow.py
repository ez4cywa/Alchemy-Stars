"""Verify baked CAST grip invariance without launching Blender.

python verify_weapon_follow.py baseline.cast followed.cast idle-reference.cast [j_wrist_le]
Baseline must have following disabled and the followed hand's IK disabled.
idle-reference.cast is the base + hand poses, without layers, retaining the original IK settings.
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "third_party/cast/blender/io_scene_cast"))
from cast import Cast, Animation, Model


def walk(node):
    yield node
    for child in node.childNodes:
        yield from walk(child)


def multiply(a, b):
    x, y, z, w = a
    X, Y, Z, W = b
    return (w*X+x*W+y*Z-z*Y, w*Y-x*Z+y*W+z*X,
            w*Z+x*Y-y*X+z*W, w*W-x*X-y*Y-z*Z)


def inverse(q):
    length2 = sum(v*v for v in q)
    return tuple(v/length2 for v in (-q[0], -q[1], -q[2], q[3]))


def rotate(q, v):
    return multiply(multiply(q, (*v, 0)), inverse(q))[:3]


class Scene:
    def __init__(self, path):
        nodes = [n for root in Cast.load(path).Roots() for n in walk(root)]
        self.bones = next(n for n in nodes if isinstance(n, Model)).Skeleton().Bones()
        animation = next(n for n in nodes if isinstance(n, Animation))
        self.curves = {(c.NodeName(), c.KeyPropertyName()): c for c in animation.Curves()}
        self.frames = max(max(c.KeyFrameBuffer()) for c in animation.Curves()) + 1

    def value(self, name, prop, frame, default):
        curve = self.curves.get((name, prop))
        if curve is None:
            return default
        index = max(i for i, f in enumerate(curve.KeyFrameBuffer()) if f <= frame)
        values = curve.KeyValueBuffer()
        return values[index*4:index*4+4] if prop == "rq" else values[index]

    def world(self, name, frame):
        bone = next(b for b in self.bones if b.Name() == name)
        bind = bone.LocalPosition() or (0, 0, 0)
        position = tuple(self.value(name, prop, frame, bind[i]) for i, prop in enumerate(("tx", "ty", "tz")))
        rotation = self.value(name, "rq", frame, bone.LocalRotation() or (0, 0, 0, 1))
        if bone.ParentIndex() >= 0:
            pp, pq = self.world(self.bones[bone.ParentIndex()].Name(), frame)
            position = tuple(a+b for a, b in zip(pp, rotate(pq, position)))
            rotation = multiply(pq, rotation)
        return position, rotation


base, followed = (Scene(path) for path in sys.argv[1:3])
reference = Scene(sys.argv[3])
hand = sys.argv[4] if len(sys.argv) > 4 else "j_wrist_le"
assert base.frames == followed.frames
hp, hq = reference.world(hand, 0)
wp, wq = reference.world("tag_weapon", 0)
grip = rotate(inverse(hq), tuple(w-h for w, h in zip(wp, hp)))
grip_rotation = multiply(inverse(hq), wq)
max_error = 0
positions = []
for frame in range(followed.frames):
    hp, hq = followed.world(hand, frame)
    bp, bq = base.world(hand, frame)
    assert math.dist(hp, bp) < 0.002, ("Follow changed wrist animation", frame)
    wp, wq = followed.world("tag_weapon", frame)
    actual = rotate(inverse(hq), tuple(w-h for w, h in zip(wp, hp)))
    error = math.dist(grip, actual)
    max_error = max(error, max_error)
    assert error < 0.002, ("Grip position drift", frame, error)
    actual_rotation = multiply(inverse(hq), wq)
    dot = abs(sum(a*b for a, b in zip(actual_rotation, grip_rotation))) / math.sqrt(
        sum(a*a for a in actual_rotation) * sum(b*b for b in grip_rotation))
    assert abs(1-dot) < 0.00001, ("Grip rotation drift", frame)
    positions.append(wp)
motion = max(math.dist(positions[0], p) for p in positions)
assert motion > 1, "Weapon did not move"
print(f"PASS {followed.frames} frames: wrist unchanged, grip error {max_error:.8f}, weapon motion {motion:.5f}")
