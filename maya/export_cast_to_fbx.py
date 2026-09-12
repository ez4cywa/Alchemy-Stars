"""Convert an Alchemy Stars CAST package to FBX with the locally installed Maya.

Usage:
    mayapy.exe export_cast_to_fbx.py input.cast output.fbx cast_plugin_dir framerate
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

def main() -> int:
    if len(sys.argv) != 5:
        raise SystemExit(
            "Usage: mayapy export_cast_to_fbx.py <input.cast> <output.fbx> <cast_plugin_dir> <framerate>"
        )

    cast_path = Path(sys.argv[1]).resolve()
    fbx_path = Path(sys.argv[2]).resolve()
    plugin_dir = Path(sys.argv[3]).resolve()
    framerate = float(sys.argv[4])
    if not cast_path.is_file():
        raise FileNotFoundError(f"CAST input not found: {cast_path}")
    if not (plugin_dir / "castplugin.py").is_file():
        raise FileNotFoundError(f"CAST plug-in not found: {plugin_dir}")

    sys.path.insert(0, str(plugin_dir))
    import maya.standalone

    maya.standalone.initialize(name="python")
    try:
        import maya.cmds as cmds
        import maya.mel as mel
        import castplugin
        from cast import Cast, Metadata

        metadata = next((m for root in Cast.load(str(cast_path)).Roots()
                         for m in root.ChildrenOfType(Metadata)), None)
        source_up = (metadata.UpAxis() if metadata else None) or "y"
        if source_up == "x":
            raise ValueError("Maya cannot preserve an X-up scene. Use the Blender FBX backend or select Y-up/Z-up output.")
        if source_up not in ("y", "z"):
            raise ValueError("Unsupported CAST up axis: " + source_up)

        castplugin.sceneSettings["importMerge"] = False
        castplugin.sceneSettings["importReset"] = False
        castplugin.sceneSettings["importAtTime"] = False
        castplugin.sceneSettings["importSkin"] = True
        castplugin.sceneSettings["importLooping"] = True
        castplugin.sceneSettings["importAxis"] = True
        castplugin.utilityCreateProgress = lambda status="", maximum=0: None
        castplugin.utilityStepProgress = lambda instance, status="": None
        castplugin.utilityEndProgress = lambda instance: None
        castplugin.importMaterialNode = lambda path, material: "lambert1"

        cmds.file(new=True, force=True)
        cmds.upAxis(ax=source_up, rv=True)
        cmds.currentUnit(linear="cm")
        cmds.loadPlugin("shaderFXPlugin", quiet=True)
        if not cmds.objExists("lambert1SG"):
            cmds.sets(renderable=True, noSurfaceShader=True, empty=True, name="lambert1SG")
        castplugin.importCast(str(cast_path))

        cmds.loadPlugin("fbxmaya", quiet=True)
        minimum = float(cmds.playbackOptions(query=True, minTime=True))
        maximum = float(cmds.playbackOptions(query=True, maxTime=True))
        if framerate > 0:
            try:
                cmds.currentUnit(time=f"{framerate:g}fps", updateAnimation=False)
            except RuntimeError:
                pass

        fbx_path.parent.mkdir(parents=True, exist_ok=True)
        mel.eval("FBXResetExport;")
        mel.eval(f"FBXExportUpAxis {source_up};")
        mel.eval("FBXExportBakeComplexAnimation -v true;")
        mel.eval(f"FBXExportBakeComplexStart -v {minimum:g};")
        mel.eval(f"FBXExportBakeComplexEnd -v {maximum:g};")
        mel.eval("FBXExportBakeComplexStep -v 1;")
        mel.eval("FBXExportApplyConstantKeyReducer -v false;")
        mel.eval("FBXExportQuaternion -v resample;")
        mel.eval("FBXExportSkins -v true;")
        mel.eval("FBXExportShapes -v true;")
        mel.eval("FBXExportAnimationOnly -v false;")
        mel.eval("FBXExportInputConnections -v true;")
        mel.eval(f"FBXExport -f {json.dumps(fbx_path.as_posix())};")

        if not fbx_path.is_file() or fbx_path.stat().st_size == 0:
            raise RuntimeError(f"Maya did not create the FBX output: {fbx_path}")
        print(json.dumps({
            "mayaVersion": cmds.about(version=True),
            "sourceUpAxis": source_up,
            "fbxUpAxis": source_up,
            "input": str(cast_path),
            "output": str(fbx_path),
            "playbackRange": [minimum, maximum],
            "framerate": framerate,
        }, ensure_ascii=False))
        return 0
    finally:
        maya.standalone.uninitialize()


if __name__ == "__main__":
    raise SystemExit(main())
