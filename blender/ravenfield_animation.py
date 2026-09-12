"""Bake RF clips on one shared bind skeleton, retaining independently editable actions.

The sequence seam owns sampling, quaternion continuity, per-frame skin verification,
and RF's single-Scene clip layout. Source objects survive until verification finishes.
"""
from __future__ import annotations

import hashlib
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix

import ravenfield_adapter as rf


def read_clip(path):
    from io_scene_cast.cast import Cast, Model, Animation, Metadata
    cast = Cast.load(str(path))
    models = [m for root in cast.Roots() for m in root.ChildrenOfType(Model)]
    animations = [a for root in cast.Roots() for a in root.ChildrenOfType(Animation)]
    axes = [m.UpAxis() for root in cast.Roots() for m in root.ChildrenOfType(Metadata)]
    rf.require(len(models) == len(animations) == 1 and axes == ["z"], "Each clip needs a complete evaluated Z-up CAST")
    rf.require(abs(animations[0].Framerate() - 30) < 1e-5, "RF clips must be evaluated at 30 FPS")
    model = models[0]
    digest = hashlib.sha256()
    digest.update(repr([(b.Name(), b.ParentIndex(), b.LocalPosition(), b.LocalRotation(), b.Scale())
                        for b in model.Skeleton().Bones()]).encode())
    for mesh in model.Meshes():
        for method in ("VertexPositionBuffer", "FaceBuffer", "VertexWeightBoneBuffer", "VertexWeightValueBuffer"):
            value = getattr(mesh, method)()
            digest.update(np.asarray(value).tobytes())
    return cast, digest.digest()


def import_action(path, rig, temporary, signature):
    from io_scene_cast.cast import Model
    cast, actual = read_clip(path)
    rf.require(actual == signature, "Library clips do not share the same model/bind skeleton")
    for root in cast.Roots():
        root.childNodes = [n for n in root.childNodes if not isinstance(n, Model)]
    prepared = temporary / "clip-animation.cast"
    cast.save(str(prepared))
    rf.activate(rig)
    result = bpy.ops.import_scene.cast(filepath=str(prepared), import_merge=False, import_reset=True,
                                      import_time=False, import_ik=False, import_constraints=False, import_skin=True)
    rf.require("FINISHED" in result, "Could not import evaluated clip")
    return rig.animation_data.action


def shared_output(rig, rf_meshes, cod, weapon_meshes, transform, factor, config):
    data = bpy.data.armatures.new("Ravenfield Animation Skeleton")
    result = bpy.data.objects.new("RF_Arms_Weapon", data)
    bpy.context.scene.collection.objects.link(result)
    rf.activate(result)
    bpy.ops.object.mode_set(mode="EDIT")
    root = data.edit_bones.new("RF_Root")
    root.head, root.tail = (0, 0, 0), (0, 0, 0.05)
    root.use_deform = True
    rf_names = {b.name for b in rig.data.bones if b.use_deform}
    weapon_names = set(config["weaponBoneNames"])
    remap = {name: ("COD_" + name if name in rf_names or name == "RF_Root" else name) for name in weapon_names}
    rf.require(len(set(remap.values())) == len(remap), "Weapon bone names collide")
    for source, names, mapping in ((rig, rf_names, {n: n for n in rf_names}), (cod, weapon_names, remap)):
        bind = rf.armature_rest(source)
        for name in sorted(names):
            bone = data.edit_bones.new(mapping[name])
            bone.length = max(0.001, source.data.bones[name].length * (factor if source == cod else 1))
            bone.matrix = rf.unscaled(transform @ bind[name], factor) if source == cod else bind[name]
            bone.use_deform = True
        for name in names:
            parent = source.data.bones[name].parent
            data.edit_bones[mapping[name]].parent = data.edit_bones[mapping[parent.name]] if parent and parent.name in names else root
    bpy.ops.object.mode_set(mode="OBJECT")
    pairs = []
    for source in rf_meshes + weapon_meshes:
        name = source.name
        source.name = name + "__SOURCE"
        mesh = source.copy()
        mesh.data = source.data.copy()
        mesh.name = name
        mesh.animation_data_clear()
        bpy.context.scene.collection.objects.link(mesh)
        world = (transform if source in weapon_meshes else Matrix.Identity(4)) @ source.matrix_world
        mesh.parent = None
        mesh.data.transform(world)
        mesh.matrix_world = Matrix.Identity(4)
        if source in weapon_meshes:
            used = {mesh.vertex_groups[g.group].name for v in mesh.data.vertices for g in v.groups if g.weight > 1e-7}
            rf.require(used <= weapon_names, "Unmapped weapon skin weights")
            for group in mesh.vertex_groups:
                if group.name in remap:
                    group.name = remap[group.name]
        mesh.parent = result
        mesh.matrix_parent_inverse = Matrix.Identity(4)
        for modifier in mesh.modifiers:
            if modifier.type == "ARMATURE":
                modifier.object = result
        pairs.append((source, mesh, transform if source in weapon_meshes else Matrix.Identity(4)))
    return result, pairs, rf_names, remap


def set_world_pose(rig, expected, previous):
    """Resolve local channels from complete world matrices, then update once."""
    for bone in rig.pose.bones:
        local = bone.bone.matrix_local.inverted() @ expected[bone.name]
        if bone.parent:
            local = (bone.bone.matrix_local.inverted() @ bone.parent.bone.matrix_local
                     @ expected[bone.parent.name].inverted() @ expected[bone.name])
        location, rotation, scale = local.decompose()
        if bone.name in previous and rotation.dot(previous[bone.name]) < 0:
            rotation.negate()
        bone.rotation_mode = "QUATERNION"
        bone.location, bone.rotation_quaternion, bone.scale = location, rotation, scale
        previous[bone.name] = rotation.copy()
    bpy.context.view_layer.update()


def vertices(obj, graph, transform):
    evaluated = obj.evaluated_get(graph)
    geometry = evaluated.to_mesh()
    try:
        points = np.empty(len(geometry.vertices) * 3, dtype=np.float64)
        geometry.vertices.foreach_get("co", points)
        points = points.reshape((-1, 3))
        world = np.asarray(transform @ evaluated.matrix_world, dtype=np.float64)
        return points @ world[:3, :3].T + world[:3, 3]
    finally:
        evaluated.to_mesh_clear()


def verify_frame(rig, pairs, expected):
    position = max((rig.pose.bones[n].matrix.translation - m.translation).length for n, m in expected.items())
    matrix = max(abs(rig.pose.bones[n].matrix[i][j] - m[i][j]) for n, m in expected.items() for i in range(4) for j in range(4))
    rf.require(matrix < 0.0002 and position < 0.0002, f"Animation world-matrix bake changed the pose: {matrix}")
    graph = bpy.context.evaluated_depsgraph_get()
    skin = 0.0
    for source, result, transform in pairs:
        wanted = vertices(source, graph, transform)
        actual = vertices(result, graph, Matrix.Identity(4))
        rf.require(actual.shape == wanted.shape, "Animation mesh topology changed")
        error = float(np.linalg.norm(actual - wanted, axis=1).max(initial=0))
        rf.require(math.isfinite(error) and error < 0.0002, f"Animation skin changed: {result.name}: {error} m")
        skin = max(skin, error)
    return position, matrix, skin


def key_pose(rig, frame):
    for bone in rig.pose.bones:
        for channel in ("location", "rotation_quaternion", "scale"):
            bone.keyframe_insert(channel, frame=frame, group=bone.name)


def compose_timeline(actions, clips):
    action = bpy.data.actions.new("RF Library Timeline")
    action.use_fake_user = True
    curves = {}
    for clip, source in zip(clips, actions):
        offset = clip["firstFrame"] - 1
        for curve in source.fcurves:
            key = (curve.data_path, curve.array_index)
            target = curves.get(key)
            if target is None:
                target = action.fcurves.new(*key[:1], index=key[1], action_group=curve.group.name if curve.group else "")
                curves[key] = target
            for point in curve.keyframe_points:
                item = target.keyframe_points.insert(point.co.x + offset, point.co.y, options={"FAST"})
                item.interpolation = "CONSTANT" if int(point.co.x) == clip["frameCount"] else "LINEAR"
        marker = action.pose_markers.new(clip["name"])
        marker.frame = clip["firstFrame"]
    for curve in curves.values():
        curve.update()
    return action


def bake_sequence(rig, rf_meshes, cod, weapon_meshes, transform, factor, config, report, source, temporary):
    scene = bpy.context.scene
    _, signature = read_clip(source)
    definitions = config["clips"] if config["mode"] == "library" else [{"name": config.get("clipName", source.stem), "path": str(source)}]
    result, pairs, rf_names, remap = shared_output(rig, rf_meshes, cod, weapon_meshes, transform, factor, config)
    rest = {n: transform @ m for n, m in rf.armature_rest(cod).items()}
    result.animation_data_create()
    actions, clips = [], []
    maxima = [0.0, 0.0, 0.0]
    cursor = 1
    for definition in definitions:
        original = import_action(Path(definition["path"]), cod, temporary, signature)
        start, end = (int(round(v)) for v in original.frame_range)
        count = max(2, end - start + 1)
        # Evaluate the source and destination at the same time during verification.
        for curve in original.fcurves:
            for point in curve.keyframe_points:
                for coordinate in (point.co, point.handle_left, point.handle_right):
                    coordinate.x += 1 - start
            curve.update()
        action = bpy.data.actions.new(definition["name"])
        action.use_fake_user = True
        result.animation_data.action = action
        previous, continuity, prior_world = {}, {}, None
        first_world = None
        diagnostics = {"maxBoneStepDegrees": 0.0, "maxElbowStepM": 0.0, "maxWristErrorM": 0.0, "maxShoulderCorrectionM": 0.0,
                       "maxSourceHandBoneStepDegrees": 0.0}
        previous_source = None
        for frame in range(1, count + 1):
            scene.frame_set(frame)
            for bone in rig.pose.bones:
                bone.matrix_basis.identity()
            bpy.context.view_layer.update()
            cod_pose = {n: transform @ m for n, m in rf.armature_pose(cod).items()}
            fitted = {"warnings": []}
            rf.fit_hands(rig, cod_pose, rest, config, fitted, continuity)
            expected = {"RF_Root": Matrix.Identity(4)}
            rf_pose = rf.armature_pose(rig)
            expected.update((n, rf_pose[n]) for n in rf_names)
            expected.update((remap[n], rf.unscaled(cod_pose[n], factor)) for n in remap)
            set_world_pose(result, expected, previous)
            errors = verify_frame(result, pairs, expected)
            maxima = [max(a, b) for a, b in zip(maxima, errors)]
            key_pose(result, frame)
            if prior_world is not None:
                for n in rf_names:
                    dot = min(1.0, abs(expected[n].to_quaternion().normalized().dot(prior_world[n].to_quaternion().normalized())))
                    angle = math.degrees(2 * math.acos(dot))
                    if angle > diagnostics["maxBoneStepDegrees"]:
                        diagnostics.update(maxBoneStepDegrees=angle, maxBoneStepFrame=frame, maxBoneStepName=n)
                diagnostics["maxElbowStepM"] = max(diagnostics["maxElbowStepM"], *[
                    (expected[n].translation - prior_world[n].translation).length for n in ("Arm.L.001", "Arm.R.001")])
                for n, matrix in cod_pose.items():
                    if n.startswith(("j_wrist_", "j_index_", "j_pinky_", "j_thumb_")):
                        dot = min(1.0, abs(matrix.to_quaternion().normalized().dot(previous_source[n].to_quaternion().normalized())))
                        diagnostics["maxSourceHandBoneStepDegrees"] = max(diagnostics["maxSourceHandBoneStepDegrees"], math.degrees(2 * math.acos(dot)))
            for side in ("left", "right"):
                diagnostics["maxWristErrorM"] = max(diagnostics["maxWristErrorM"], fitted[side]["wristErrorM"])
                diagnostics["maxShoulderCorrectionM"] = max(diagnostics["maxShoulderCorrectionM"], fitted[side]["shoulderCorrectionM"])
            if first_world is None:
                first_world = {n: m.copy() for n, m in expected.items()}
            prior_world = expected
            previous_source = cod_pose
            if frame == 1 or frame == count or frame % 30 == 0:
                print(f"RF_BAKE {definition['name']} {frame}/{count}", flush=True)
        for curve in action.fcurves:
            for point in curve.keyframe_points:
                point.interpolation = "LINEAR"
        diagnostics["firstLastPositionGapM"] = max((prior_world[n].translation - first_world[n].translation).length for n in rf_names)
        clip = {"name": definition["name"], "actionName": action.name, "firstFrame": cursor, "lastFrame": cursor + count - 1,
                "takeName": "Scene", "frameCount": count, "sourceFrames": [start, end], "loopTime": False, **diagnostics}
        clips.append(clip)
        actions.append(action)
        cursor += count + 10
    timeline = compose_timeline(actions, clips) if config["mode"] == "library" else actions[0]
    keep_actions = {*actions, timeline}
    keep_objects = {result, *[mesh for _, mesh, _ in pairs]}
    for obj in list(scene.objects):
        if obj not in keep_objects:
            bpy.data.objects.remove(obj, do_unlink=True)
    for action in list(bpy.data.actions):
        if action not in keep_actions:
            bpy.data.actions.remove(action)
    result.animation_data.action = timeline
    scene.name = "Scene"
    scene.frame_start, scene.frame_end = 1, clips[-1]["lastFrame"] if config["mode"] == "library" else clips[0]["frameCount"]
    preview_clip = clips[config.get("referenceClip", 0)] if config["mode"] == "library" else clips[0]
    preview_frame = preview_clip["firstFrame"] + config["idleFrame"]
    scene.frame_set(min(preview_frame, preview_clip["lastFrame"]))
    report.update(mode="animation-library" if config["mode"] == "library" else "full-animation",
                  clips=clips, timelineAction=timeline.name, outputFrames=[scene.frame_start, scene.frame_end],
                  fps=30, previewFrame=scene.frame_current, sourceFrameAlignment="Fixed reference-frame view transform for every clip",
                  warningScope="Baked skeletal animation; no automatic collision correction, Unity events or in-game validation")
    return result, [mesh for _, mesh, _ in pairs], {"maxBakePositionErrorM": maxima[0], "maxBakeMatrixError": maxima[1],
            "maxSkinningVertexErrorM": maxima[2], "skinningVerified": True, "verifiedFrames": sum(c["frameCount"] for c in clips)}
