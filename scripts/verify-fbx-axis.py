"""Maya regression: compare CAST and FBX world-space joints + skinned vertices.

mayapy scripts/verify-fbx-axis.py --blender D:/blender/blender.exe
Uses temporary files only. Tests both export backends, Y/Z and missing metadata.
"""
import argparse
import json
import math
from pathlib import Path
import subprocess
import struct
import sys
import tempfile

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "third_party/cast/maya"))
from cast import Cast


def fbx_up_axis(path):
    """Read GlobalSettings/Properties70/P:UpAxis, not a renderer's scene axis."""
    with path.open("rb") as stream:
        assert stream.read(23) == b"Kaydara FBX Binary  \x00\x1a\x00"
        wide = struct.unpack("<I", stream.read(4))[0] >= 7500
        header = "<QQQB" if wide else "<IIIB"
        size = struct.calcsize(header)

        def find(end, wanted):
            while stream.tell() + size <= end:
                finish, count, length, name_length = struct.unpack(header, stream.read(size))
                if not finish:
                    return None
                name = stream.read(name_length)
                if name == wanted[0]:
                    if len(wanted) > 1:
                        stream.seek(length, 1)
                        value = find(finish, wanted[1:])
                        if value is not None:
                            return value
                    else:
                        properties = []
                        for _ in range(count):
                            kind = stream.read(1)
                            if kind == b"S":
                                properties.append(stream.read(struct.unpack("<I", stream.read(4))[0]))
                            elif kind == b"I":
                                properties.append(struct.unpack("<i", stream.read(4))[0])
                            else:
                                break
                        if properties and properties[0] == b"UpAxis":
                            return properties[-1]
                stream.seek(finish)
            return None
        return find(path.stat().st_size, [b"GlobalSettings", b"Properties70", b"P"])


def fixture(path, axis):
    cast = Cast()
    root = cast.CreateRoot()
    if axis:
        root.CreateMetadata().SetUpAxis(axis)
    model = root.CreateModel()
    model.SetName("axis_fixture")
    skeleton = model.CreateSkeleton()
    for name, parent, local, world in [
        ("root", -1, (1, 2, 3), (1, 2, 3)),
        ("tip", 0, (2, 4, 1), (3, 6, 4)),
    ]:
        bone = skeleton.CreateBone()
        bone.SetName(name)
        bone.SetParentIndex(parent)
        bone.SetLocalPosition(local)
        bone.SetWorldPosition(world)
        bone.SetLocalRotation((0, 0, 0, 1))
        bone.SetWorldRotation((0, 0, 0, 1))
        bone.SetScale((1, 1, 1))
    mesh = model.CreateMesh()
    mesh.SetName("triangle")
    mesh.SetSkinningMethod("quaternion")
    # Asymmetric geometry and root translation/rotation catch all axis swaps.
    mesh.CreateProperty("vp", "3v").values = [1, 2, 3, 3, 6, 4, 4, 6, 5]
    mesh.CreateProperty("vn", "3v").values = [0, 1, 0] * 3
    mesh.CreateProperty("f", "b").values = [0, 1, 2]
    mesh.CreateProperty("mi", "b").values = [1]
    mesh.CreateProperty("wb", "b").values = [0, 1, 1]
    mesh.CreateProperty("wv", "f").values = [1, 1, 1]
    animation = root.CreateAnimation()
    animation.SetFramerate(30)
    for name in ("root", "tip"):
        for channel, values in (("tx", [1, 2, 4] if name == "root" else [2, 2, 2]),
                                ("ty", [2, 4, 7] if name == "root" else [4, 4, 4]),
                                ("tz", [3, 6, 8] if name == "root" else [1, 1, 1]),
                                ("rq", [(0, 0, math.sin(a / 2), math.cos(a / 2))
                                        for a in (0, 0.2, 0.4)])):
            curve = animation.CreateCurve()
            curve.SetNodeName(name)
            curve.SetKeyPropertyName(channel)
            curve.SetMode("absolute")
            curve.SetKeyFrameBuffer([0, 1, 2])
            if channel == "rq":
                curve.SetVec4KeyValueBuffer(values)
            else:
                curve.SetFloatKeyValueBuffer(values)
    cast.save(str(path))


def snapshot(cmds):
    result = {}
    for frame in range(3):
        cmds.currentTime(frame)
        joints = {name: cmds.xform(name, q=True, ws=True, matrix=True) for name in ("root", "tip")}
        meshes = cmds.ls(type="mesh", noIntermediate=True, long=True)
        assert len(meshes) == 1, meshes
        vertices = cmds.xform(meshes[0] + ".vtx[*]", q=True, ws=True, translation=True)
        result[frame] = (joints, vertices)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--blender", required=True)
    parser.add_argument("--backend", choices=("blender", "maya", "both"), default="both")
    args = parser.parse_args()
    import maya.standalone
    maya.standalone.initialize(name="python")
    try:
        import maya.cmds as cmds
        import maya.mel as mel
        import castplugin
        castplugin.utilityCreateProgress = lambda *a, **k: None
        castplugin.utilityStepProgress = lambda *a, **k: None
        castplugin.utilityEndProgress = lambda *a, **k: None
        castplugin.importMaterialNode = lambda *a: "lambert1"
        castplugin.sceneSettings.update(importMerge=False, importReset=False, importAtTime=False,
                                        importAxis=True, importSkin=True, importIK=False, importConstraints=False)
        cmds.loadPlugin("fbxmaya", quiet=True)
        with tempfile.TemporaryDirectory(prefix="alchemy-axis-") as temporary:
            for axis in ("y", "z", None):
                source = Path(temporary) / ((axis or "missing") + ".cast")
                fixture(source, axis)
                cmds.file(new=True, force=True)
                cmds.upAxis(ax="y", rv=True)
                castplugin.importCast(str(source))
                expected_axis = cmds.upAxis(q=True, ax=True)
                expected = snapshot(cmds)
                for backend in (("blender", "maya") if args.backend == "both" else (args.backend,)):
                    target = source.with_suffix("." + backend + ".fbx")
                    command = ([args.blender, "--background", "--factory-startup", "--python-exit-code", "1",
                                "--python", str(REPO / "blender/convert_cast.py"), "--", str(source), str(target),
                                "--verify", "--blend", str(source.with_suffix(".blend"))]
                               if backend == "blender" else
                               [sys.executable, str(REPO / "maya/export_cast_to_fbx.py"), str(source), str(target),
                                str(REPO / "third_party/cast/maya"), "30"])
                    process = subprocess.run(command, capture_output=True, text=True, errors="replace")
                    assert process.returncode == 0, process.stdout[-4000:] + process.stderr[-4000:]
                    assert fbx_up_axis(target) == (1 if expected_axis == "y" else 2), "FBX axis metadata differs from CAST"
                    if backend == "blender":
                        reimport = subprocess.run([args.blender, "--background", "--factory-startup", "--python-exit-code", "1",
                                                   "--python", str(REPO / "blender/verify_fbx.py"), "--",
                                                   str(source.with_suffix(".blend")), str(target)],
                                                  capture_output=True, text=True, errors="replace")
                        assert reimport.returncode == 0, reimport.stdout[-4000:] + reimport.stderr[-4000:]
                    cmds.file(new=True, force=True)
                    cmds.upAxis(ax=expected_axis, rv=True)
                    cmds.currentUnit(time="ntsc")
                    mel.eval("FBXResetImport;")
                    mel.eval(f'FBXImport -f {json.dumps(target.as_posix())};')
                    actual = snapshot(cmds)
                    assert all(len(expected[f][1]) == len(actual[f][1]) == 9 for f in range(3)), "Vertex count changed"
                    error = max(abs(a - b) for frame in range(3)
                                for left, right in [(expected[frame][0][joint], actual[frame][0][joint]) for joint in ("root", "tip")]
                                + [(expected[frame][1], actual[frame][1])]
                                for a, b in zip(left, right))
                    print(json.dumps(dict(backend=backend, source_axis=axis, maya_axis=expected_axis,
                                          max_world_error=error)), flush=True)
                    assert error < 0.002, f"{backend}/{axis}: CAST vs FBX world transforms/skin differ: {error}"
    finally:
        maya.standalone.uninitialize()


if __name__ == "__main__":
    main()
