"""Fit Ravenfield view hands to one evaluated COD idle pose.

Run with Blender --background --factory-startup --disable-autoexec --python-exit-code 1.
The input is the engine's complete, evaluated, Z-up CAST, NOT a raw animation.
This adapter never changes a source file or the user's Blender preferences.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
import tarfile
import tempfile
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector


UNIT_FACTORS = {"cm": 0.01, "m": 1.0, "ft": 0.3048}
RF_TEMPLATE = "Assets/RFTools/Models/Character/Hands no IK for custom models.blend"
RF_FALLBACK = "Assets/RFTools/Models/Character/Hands.blend"
SUFFIXES = (".blend", ".fbx", ".report.json", ".preview.png")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def number(value, name, minimum=-1e4, maximum=1e4):
    require(isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(value) and minimum <= value <= maximum, f"Invalid {name}: {value}")
    return float(value)


def vector3(value, name):
    require(isinstance(value, list) and len(value) == 3, f"{name} requires X/Y/Z")
    return Vector([number(v, name) for v in value])


def validate_config(config):
    require(config.get("mode", "pose") in ("pose", "animation", "library"), "Mode must be pose, animation or library")
    config.setdefault("mode", "pose")
    config.setdefault("contactFit", True)
    require(type(config['contactFit']) is bool, 'contactFit must be a boolean')
    config.setdefault('localHandFit', False)
    require(type(config['localHandFit']) is bool, 'localHandFit must be a boolean')
    require(not config['localHandFit'] or config['contactFit'], 'localHandFit requires contactFit')
    require(config.get("sourceUnit", "cm") in UNIT_FACTORS, "Source unit must be cm, m or ft")
    config.setdefault("sourceUnit", "cm")
    frame = config.get("idleFrame", 0)
    require(isinstance(frame, int) and not isinstance(frame, bool) and frame >= 0, "Idle frame must be a nonnegative integer")
    config["idleFrame"] = frame
    config["handScale"] = number(config.get("handScale", 1), "handScale", 0.1, 10)
    for side in ("left", "right"):
        setting = config.setdefault(side, {})
        vector3(setting.setdefault("position", [0, 0, 0]), side + " position")
        vector3(setting.setdefault("rotation", [0, 0, 0]), side + " rotation")
        setting["elbowSwivel"] = number(setting.get("elbowSwivel", 0), side + " elbowSwivel", -180, 180)
        setting["fingerCurl"] = number(setting.get("fingerCurl", 0), side + " fingerCurl", -90, 90)
    indices = config.get("handMeshIndices")
    require(isinstance(indices, list) and indices and all(type(i) is int and i >= 0 for i in indices)
            and len(set(indices)) == len(indices), "Explicit handMeshIndices are required")
    names = config.get("weaponBoneNames")
    require(isinstance(names, list) and names and all(isinstance(n, str) and n for n in names),
            "Explicit weaponBoneNames are required")
    if config["mode"] == "library":
        clips = config.get("clips")
        require(isinstance(clips, list) and clips, "Animation library requires clips")
        require(all(isinstance(c, dict) and isinstance(c.get("name"), str) and c["name"].strip()
                    and isinstance(c.get("path"), str) and c["path"] for c in clips), "Invalid library clip")
        require(len({c["name"] for c in clips}) == len(clips), "Library clip names must be unique")
        index = config.get("referenceClip", 0)
        require(type(index) is int and 0 <= index < len(clips), "Invalid library reference clip")


def extract_rf_source(source, temporary):
    if source.suffix.lower() == ".blend":
        return source, source.name
    require(source.suffix.lower() == ".unitypackage", "RF source must be .blend or .unitypackage")
    # Read only the selected known asset. Never extract member-supplied paths.
    with tarfile.open(source, "r:*") as archive:
        matches = {}
        for member in archive:
            if member.isfile() and member.name.endswith("/pathname") and member.size < 8192:
                path = archive.extractfile(member).read().decode("utf-8-sig").strip().replace("\\", "/")
                if path in (RF_TEMPLATE, RF_FALLBACK):
                    require(path not in matches, "Duplicate Ravenfield hand template in package")
                    matches[path] = member.name.rsplit("/", 1)[0] + "/asset"
        path = next((p for p in (RF_TEMPLATE, RF_FALLBACK) if p in matches), None)
        require(path is not None, "Package does not contain the Ravenfield Hands template")
        member = archive.getmember(matches[path])
        require(member.isfile() and member.size <= 128 * 1024 * 1024, "Invalid RF hand asset")
        destination = temporary / "rf-hands.blend"
        destination.write_bytes(archive.extractfile(member).read())
        return destination, path


def descendants(node):
    yield node
    for child in node.childNodes:
        yield from descendants(child)


def resolve_material_files(model, hand_indices, source, config, report, file_type):
    needed = {f for i, mesh in enumerate(model.Meshes()) if i not in hand_indices and mesh.Material()
              for f in descendants(mesh.Material()) if isinstance(f, file_type)}
    directories = [source.parent] + [Path(p) for p in config.get("sourceDirectories", [])]
    missing = set()
    for node in descendants(model):
        if not isinstance(node, file_type):
            continue
        path = node.Path()
        if not path:
            if node in needed:
                missing.add("<empty texture reference>")
            continue
        original = Path(path.replace("\\", "/"))
        candidates = [original] if original.is_absolute() else [root / original for root in directories]
        existing = sorted({p.resolve() for p in candidates if p.is_file()})
        if len(existing) > 1:
            digests = {hashlib.sha256(p.read_bytes()).digest() for p in existing}
            if len(digests) > 1:
                existing = []  # An ambiguous texture is not silently selected from another model.
        if existing:
            node.SetPath(str(existing[0]))
        elif node in needed:
            missing.add(path)
    report["missingTextureReferences"] = sorted(missing)
    if missing:
        report["warnings"].append("部分武器贴图未提供或路径不明确；已保留材质与几何，预览使用中性色 / Some weapon textures are unavailable or ambiguous; preview uses neutral colors")


def load_cast(source, temporary, config, report):
    directory = Path(__file__).resolve().parent
    plugin = next((p for p in (directory.parent / "third_party/cast/blender", directory.parent / "BlenderPlugin")
                   if (p / "io_scene_cast/__init__.py").is_file()), None)
    require(plugin is not None, "Bundled Blender CAST plugin is missing")
    sys.path.insert(0, str(plugin))
    import io_scene_cast
    from io_scene_cast.cast import Cast, Model, Animation, Metadata, File
    cast = Cast.load(str(source))
    models = [m for r in cast.Roots() for m in r.ChildrenOfType(Model)]
    animations = [a for r in cast.Roots() for a in r.ChildrenOfType(Animation)]
    axes = [m.UpAxis() for r in cast.Roots() for m in r.ChildrenOfType(Metadata)]
    require(axes == ["z"], "RF adapter requires the engine's evaluated Z-up CAST")
    require(len(models) == 1 and len(animations) == 1, "RF adapter requires one complete model and one evaluated animation")
    meshes = models[0].Meshes()
    hand_indices = set(config["handMeshIndices"])
    require(max(hand_indices) < len(meshes) and len(hand_indices) < len(meshes), "Hand mesh indices do not match the merged model")
    resolve_material_files(models[0], hand_indices, source, config, report, File)
    for i, mesh in enumerate(meshes):
        mesh.SetName(f"COD_ReferenceHands_{i:03d}" if i in hand_indices else f"COD_Weapon_{i:03d}")
    # Stable names assigned only in this throwaway copy disambiguate unnamed CAST meshes.
    prepared = temporary / "prepared.cast"
    cast.save(str(prepared))
    io_scene_cast.register()
    result = bpy.ops.import_scene.cast(filepath=str(prepared), import_merge=False, import_reset=True,
                                       import_ik=False, import_constraints=False, import_skin=True)
    require("FINISHED" in result, "CAST import failed")
    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    require(len(rigs) == 1, "Expected one imported COD armature")
    rig = rigs[0]
    scene = bpy.context.scene
    frame = scene.frame_start + config["idleFrame"]
    require(frame <= scene.frame_end, f"Idle frame {config['idleFrame']} exceeds the evaluated animation length")
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    weapon_meshes = [o for o in scene.objects if o.type == "MESH" and o.name.startswith("COD_Weapon_")]
    require(weapon_meshes, "No weapon meshes remain after excluding COD reference hands")
    for name in config["weaponBoneNames"]:
        require(name in rig.data.bones, f"Missing mapped weapon bone: {name}")
    return rig, weapon_meshes, frame


def armature_pose(rig):
    return {b.name: rig.matrix_world @ b.matrix.copy() for b in rig.pose.bones}


def armature_rest(rig):
    return {b.name: rig.matrix_world @ b.matrix_local.copy() for b in rig.data.bones}


def palm_frame(wrist, index, pinky):
    longitudinal = (index + pinky) * 0.5 - wrist
    require(longitudinal.length > 1e-6, "Degenerate palm length")
    y = longitudinal.normalized()
    x = index - pinky
    x -= y * x.dot(y)
    require(x.length > 1e-6, "Degenerate palm width")
    x.normalize()
    z = x.cross(y).normalized()
    return Matrix((x, y, z)).transposed().to_quaternion()


def solve_elbow(shoulder, wrist, pole, upper, lower, swivel, previous_plane=None):
    """Two-link length-preserving solve; a short RF elbow segment belongs to the forearm."""
    delta = wrist - shoulder
    require(delta.length > 1e-6, "Shoulder and wrist coincide")
    direction = delta.normalized()
    distance = min(upper + lower - 1e-5, max(abs(upper - lower) + 1e-5, delta.length))
    # The grip is authoritative. Move the shoulder only when the RF arm cannot reach.
    fitted_shoulder = wrist - direction * distance
    plane = pole - fitted_shoulder
    plane -= direction * plane.dot(direction)
    # A nearly straight source elbow has no reliable bend direction. Transport
    # the preceding plane through that singularity instead of choosing a new side.
    if previous_plane is not None and plane.length < 0.01 * max(upper, lower):
        transported = previous_plane - direction * previous_plane.dot(direction)
        if transported.length > 1e-6:
            plane = transported
    if plane.length < 1e-6:
        plane = Vector((0, 0, -1)) - direction * direction.dot(Vector((0, 0, -1)))
    if plane.length < 1e-6:
        plane = Vector((1, 0, 0)) - direction * direction.x
    plane.normalize()
    plane = Quaternion(direction, math.radians(swivel)) @ plane
    along = (upper * upper - lower * lower + distance * distance) / (2 * distance)
    height = math.sqrt(max(0, upper * upper - along * along))
    return fitted_shoulder, fitted_shoulder + along * direction + height * plane


def set_pose(rig, name, position, rotation):
    bone = rig.pose.bones[name]
    bone.rotation_mode = "QUATERNION"
    bone.matrix = Matrix.LocRotScale(position, rotation, Vector((1, 1, 1)))
    bpy.context.view_layer.update()


def aim_rotation(rest_rotation, direction):
    require(direction.length > 1e-8, "Zero-length bone target")
    return (rest_rotation @ Vector((0, 1, 0))).rotation_difference(direction.normalized()) @ rest_rotation


def activate(obj):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    obj.hide_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def load_rf(path, hand_scale):
    with bpy.data.libraries.load(str(path), link=False) as (available, loaded):
        loaded.objects = available.objects
    objects = [o for o in loaded.objects if o]
    rigs = [o for o in objects if o.type == "ARMATURE"]
    require(len(rigs) == 1, "RF template must contain one hand armature")
    rig = rigs[0]
    meshes = [o for o in objects if o.type == "MESH" and
              any(m.type == "ARMATURE" and m.object == rig for m in o.modifiers)]
    require(meshes, "RF template has no skinned hand mesh")
    for obj in [rig] + meshes:
        bpy.context.scene.collection.objects.link(obj)
        obj.animation_data_clear()
    for p in rig.pose.bones:
        for c in list(p.constraints):
            p.constraints.remove(c)
        p.matrix_basis.identity()
    bpy.context.view_layer.update()
    # Mirror first: RF ships only one side's base vertices and uses mirrored vertex groups.
    for mesh in meshes:
        activate(mesh)
        for modifier in list(mesh.modifiers):
            if modifier.type != "ARMATURE":
                bpy.ops.object.modifier_apply(modifier=modifier.name)
    transform = Matrix.Scale(hand_scale, 4)
    mesh_world = {m: transform @ m.matrix_world.copy() for m in meshes}
    rig_world = transform @ rig.matrix_world.copy()
    for mesh in meshes:
        mesh.parent = None
        mesh.data.transform(mesh_world[mesh])
        mesh.matrix_world = Matrix.Identity(4)
    rig.data.transform(rig_world)
    rig.parent = None
    rig.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for suffix in ("L", "R"):
        for name in (f"Arm.{suffix}", f"Arm.{suffix}.001", f"Wrist.{suffix}", f"Hand.{suffix}",
                     *[f"{finger}.{suffix}{end}" for finger in ("Index", "Pinky", "Thumb") for end in ("", ".002", ".001")]):
            require(name in rig.data.bones, f"Unsupported RF template: missing {name}")
    return rig, meshes


def source_transform(rest, pose, factor):
    # Match the template's +X-left/-Y-forward camera plane, not a per-weapon guessed yaw.
    left = rest["j_shoulder_le"].translation - rest["j_shoulder_ri"].translation
    left.z = 0
    require(left.length > 1e-6, "Cannot determine the source shoulder left axis")
    yaw = Matrix.Rotation(-math.atan2(left.y, left.x), 4, "Z")
    origin = pose.get("tag_camera", pose.get("tag_view"))
    require(origin is not None, "COD reference requires tag_camera or tag_view for view-space alignment")
    return yaw @ Matrix.Scale(factor, 4) @ Matrix.Translation(-origin.translation)


def fit_hands(rf, cod_pose, cod_rest, config, report, continuity=None, corrections=None):
    rest = armature_rest(rf)
    for side, suffix, cod_side in (("left", "L", "le"), ("right", "R", "ri")):
        setting = config[side]
        source_wrist = cod_pose[f"j_wrist_{cod_side}"].translation
        target_wrist = source_wrist + vector3(setting["position"], side)
        source_palm = palm_frame(source_wrist, cod_pose[f"j_index_{cod_side}_1"].translation,
                                 cod_pose[f"j_pinky_{cod_side}_1"].translation)
        rf_palm = palm_frame(rest[f"Hand.{suffix}"].translation, rest[f"Index.{suffix}"].translation,
                             rest[f"Pinky.{suffix}"].translation)
        source_rest_palm = palm_frame(cod_rest[f"j_wrist_{cod_side}"].translation,
                                     cod_rest[f"j_index_{cod_side}_1"].translation,
                                     cod_rest[f"j_pinky_{cod_side}_1"].translation)
        rest_alignment = source_rest_palm @ rf_palm.inverted()
        user_rotation = Euler([math.radians(x) for x in setting["rotation"]], "XYZ").to_quaternion()
        finger_rotation = user_rotation.copy()
        if corrections:
            rotation, shift = corrections[side]
            target_wrist += shift
            user_rotation = rotation @ user_rotation
        palm_alignment = user_rotation @ source_palm @ rf_palm.inverted()
        arm, elbow_part, forearm, hand = f"Arm.{suffix}", f"Arm.{suffix}.001", f"Wrist.{suffix}", f"Hand.{suffix}"
        upper = rf.data.bones[arm].length
        lower = rf.data.bones[elbow_part].length + rf.data.bones[forearm].length
        source_shoulder = cod_pose[f"j_shoulder_{cod_side}"].translation
        shoulder, elbow = solve_elbow(source_shoulder, target_wrist,
                                      cod_pose[f"j_elbow_{cod_side}"].translation,
                                      upper, lower, setting["elbowSwivel"],
                                      continuity.get(side) if continuity is not None else None)
        if continuity is not None:
            axis = (target_wrist - shoulder).normalized()
            plane = elbow - shoulder
            plane -= axis * plane.dot(axis)
            if plane.length > 1e-6:
                # Store the pre-swivel plane so the user adjustment is applied once.
                continuity[side] = Quaternion(axis, -math.radians(setting["elbowSwivel"])) @ plane.normalized()
        for name, position, direction in (
                (arm, shoulder, elbow - shoulder),
                (elbow_part, elbow, target_wrist - elbow),
                (forearm, elbow + (target_wrist - elbow).normalized() * rf.data.bones[elbow_part].length,
                 target_wrist - elbow)):
            set_pose(rf, name, position, aim_rotation(palm_alignment @ rest[name].to_quaternion(), direction))
        set_pose(rf, hand, target_wrist, palm_alignment @ rest[hand].to_quaternion())
        mapping = {}
        for finger in ("Index", "Pinky", "Thumb"):
            rf_names = [f"{finger}.{suffix}{end}" for end in ("", ".002", ".001")]
            source_names = [f"j_{finger.lower()}_{cod_side}_{i}" for i in (1, 2, 3)]
            for i, (rf_name, cod_name) in enumerate(zip(rf_names, source_names)):
                if i < 2:
                    direction = cod_pose[source_names[i + 1]].translation - cod_pose[cod_name].translation
                    rest_direction = cod_rest[source_names[i + 1]].translation - cod_rest[cod_name].translation
                else:
                    # Extrapolate the anatomical distal axis, not Blender's arbitrary display tail.
                    rest_direction = cod_rest[cod_name].translation - cod_rest[source_names[i - 1]].translation
                    direction = cod_pose[cod_name].to_quaternion() @ cod_rest[cod_name].to_quaternion().inverted() @ rest_direction
                direction = finger_rotation @ direction
                curl = Quaternion(finger_rotation @ source_palm @ Vector((1, 0, 0)), math.radians(setting["fingerCurl"]) * (i + 1))
                direction = curl @ direction
                # Keep the original RF knuckle offsets and segment lengths; do not move fingers to COD joints.
                position = rf.pose.bones[rf_name].matrix.translation.copy() if i == 0 else rf.pose.bones[rf_names[i - 1]].tail.copy()
                # Transport the authored bone orientation from a fixed anatomical
                # rest alignment. Re-solving a shortest swing from the moving palm
                # can flip the distal finger's roll near a 180-degree opposition.
                rest_rotation = aim_rotation(rest_alignment @ rest[rf_name].to_quaternion(), rest_direction)
                transported = cod_pose[cod_name].to_quaternion() @ cod_rest[cod_name].to_quaternion().inverted() @ rest_rotation
                rotation = aim_rotation(curl @ finger_rotation @ transported, direction)
                set_pose(rf, rf_name, position, rotation)
                mapping[rf_name] = cod_name
        wrist_error = (rf.pose.bones[hand].matrix.translation - target_wrist).length
        require(wrist_error < 0.0001, f"{side} wrist fit failed")
        correction = (shoulder - source_shoulder).length
        report[side] = {"wristErrorM": wrist_error, "shoulderCorrectionM": correction,
                        "targetWristM": list(target_wrist), "upperLengthM": upper,
                        "forearmLengthM": lower, "fingerMap": mapping}
        if correction > 0.001:
            report["warnings"].append(f"{side} RF shoulder moved {correction:.4f} m to preserve arm length and grip")


def unscaled(matrix, factor):
    # Remove only the explicit global unit factor, retaining genuine animated bone scale.
    return matrix @ Matrix.Scale(1 / factor, 4)


def combine_rigs(rf, rf_meshes, cod, weapon_meshes, transform, factor, config):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    reference_vertices = {}
    for obj in rf_meshes + weapon_meshes:
        evaluated = obj.evaluated_get(depsgraph)
        geometry = evaluated.to_mesh()
        world = evaluated.matrix_world if obj in rf_meshes else transform @ evaluated.matrix_world
        reference_vertices[obj.name] = [world @ v.co for v in geometry.vertices]
        evaluated.to_mesh_clear()
    rf_pose, rf_rest = armature_pose(rf), armature_rest(rf)
    cod_pose, cod_rest = armature_pose(cod), armature_rest(cod)
    data = bpy.data.armatures.new("Ravenfield Adapted Skeleton")
    result = bpy.data.objects.new("RF_Arms_Weapon", data)
    bpy.context.scene.collection.objects.link(result)
    activate(result)
    bpy.ops.object.mode_set(mode="EDIT")
    root = data.edit_bones.new("RF_Root")
    root.head, root.tail = (0, 0, 0), (0, 0, 0.05)
    root.use_deform = True
    rf_names = {b.name for b in rf.data.bones if b.use_deform}
    weapon_names = set(config["weaponBoneNames"])
    name_map = {n: ("COD_" + n if n in rf_names or n == "RF_Root" else n) for n in weapon_names}
    require(len(set(name_map.values())) == len(name_map), "Weapon bone names collide with RF output bones")
    expected = {"RF_Root": Matrix.Identity(4)}
    for source, names, bind, poses, remap in (
            (rf, rf_names, rf_rest, rf_pose, {n: n for n in rf_names}),
            (cod, weapon_names, cod_rest, cod_pose, name_map)):
        for name in sorted(names):
            b = data.edit_bones.new(remap[name])
            matrix = bind[name] if source == rf else unscaled(transform @ bind[name], factor)
            b.length = max(0.001, source.data.bones[name].length * (factor if source == cod else 1))
            # A newly created zero-length edit bone cannot retain a matrix's direction.
            # Give it length BEFORE assigning its rest matrix.
            b.matrix = matrix
            b.use_deform = True
            expected[b.name] = poses[name] if source == rf else unscaled(transform @ poses[name], factor)
        for name in names:
            parent = source.data.bones[name].parent
            data.edit_bones[remap[name]].parent = data.edit_bones[remap[parent.name]] if parent and parent.name in names else root
    bpy.ops.object.mode_set(mode="OBJECT")
    for mesh in weapon_meshes:
        world = transform @ mesh.matrix_world.copy()
        mesh.parent = None
        mesh.data.transform(world)
        mesh.matrix_world = Matrix.Identity(4)
        used = {mesh.vertex_groups[g.group].name for vertex in mesh.data.vertices for g in vertex.groups if g.weight > 1e-7}
        require(used <= weapon_names, "Weapon mesh is weighted to unmapped bones: " + ", ".join(sorted(used - weapon_names)))
        for group in mesh.vertex_groups:
            if group.name in name_map:
                group.name = name_map[group.name]
    for mesh in rf_meshes + weapon_meshes:
        mesh.parent = result
        mesh.matrix_parent_inverse = Matrix.Identity(4)
        for modifier in mesh.modifiers:
            if modifier.type == "ARMATURE":
                modifier.object = result
    # Parent-first world-space assignment preserves each RF bind matrix and baked weapon transform.
    pending = list(result.pose.bones)
    assigned = set()
    while pending:
        ready = [b for b in pending if b.parent is None or b.parent.name in assigned]
        require(ready, "Cyclic output hierarchy")
        for bone in ready:
            bone.rotation_mode = "QUATERNION"
            bone.matrix = expected[bone.name]
            bpy.context.view_layer.update()
            for frame in (1, 2):
                bone.keyframe_insert("location", frame=frame)
                bone.keyframe_insert("rotation_quaternion", frame=frame)
                bone.keyframe_insert("scale", frame=frame)
            assigned.add(bone.name)
            pending.remove(bone)
    retained = {result, *rf_meshes, *weapon_meshes}
    for obj in list(bpy.context.scene.objects):
        if obj not in retained:
            bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 2
    scene.frame_set(1)
    bpy.context.view_layer.update()
    max_error = max((result.pose.bones[n].matrix.translation - m.translation).length for n, m in expected.items())
    require(max_error < 0.0002, f"Output skeleton bake changed world coordinates: {max_error}")
    matrix_error = max(abs(result.pose.bones[n].matrix[i][j] - m[i][j]) for n, m in expected.items() for i in range(4) for j in range(4))
    require(matrix_error < 0.0002, f"Output skeleton bake changed world matrices: {matrix_error}")
    depsgraph = bpy.context.evaluated_depsgraph_get()
    skinning_error = 0.0
    for obj in rf_meshes + weapon_meshes:
        evaluated = obj.evaluated_get(depsgraph)
        geometry = evaluated.to_mesh()
        require(len(geometry.vertices) == len(reference_vertices[obj.name]), f"Output vertex count changed: {obj.name}")
        error = max((evaluated.matrix_world @ v.co - wanted).length for v, wanted in zip(geometry.vertices, reference_vertices[obj.name]))
        skinning_error = max(skinning_error, error)
        evaluated.to_mesh_clear()
        require(error < 0.0002, f"Output skinning changed evaluated mesh {obj.name}: {error} m")
    return result, rf_meshes + weapon_meshes, {"maxBakePositionErrorM": max_error,
        "maxBakeMatrixError": matrix_error, "maxSkinningVertexErrorM": skinning_error, "skinningVerified": True}


def configure_scene(rig, meshes):
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.length_unit = "METERS"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = 30
    scene.render.fps_base = 1.0
    rig.show_in_front = True
    rig.data.display_type = "STICK"
    # Preserve source materials. Workbench preview is deliberately neutral when textures are unavailable.
    for mesh in meshes:
        mesh.color = (0.58, 0.68, 0.77, 1) if not mesh.name.startswith("COD_Weapon_") else (0.19, 0.22, 0.25, 1)
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.background_type = "WORLD"
    scene.world = scene.world or bpy.data.worlds.new("RF Preview World")
    scene.world.color = (0.045, 0.052, 0.063)
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    depsgraph = bpy.context.evaluated_depsgraph_get()
    vertices = [o.matrix_world @ Vector(p) for m in meshes for o in [m.evaluated_get(depsgraph)] for p in o.bound_box]
    minimum = Vector([min(v[i] for v in vertices) for i in range(3)])
    maximum = Vector([max(v[i] for v in vertices) for i in range(3)])
    rf_vertices = [o.matrix_world @ Vector(p) for m in meshes if not m.name.startswith("COD_Weapon_")
                   for o in [m.evaluated_get(depsgraph)] for p in o.bound_box]
    # Frame the grip, not spare magazines stored far below the source weapon.
    # All source parts remain untouched in both deliverable models.
    focus_minimum = minimum.copy()
    focus_minimum.z = max(focus_minimum.z, min(v.z for v in rf_vertices) - 0.1)
    center = (focus_minimum + maximum) * 0.5
    radius = max((maximum - focus_minimum).length * 0.65, 0.3)
    camera_data = bpy.data.cameras.new("RF Inspection Camera")
    camera = bpy.data.objects.new("RF Inspection Camera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = center + Vector((-1.25, -0.7, 0.9)).normalized() * radius * 2.2
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = radius * 2.1
    scene.camera = camera
    camera.hide_set(True)
    # An explicit template-style first-person camera is available in the editable file.
    fp_data = bpy.data.cameras.new("RF First Person")
    fp = bpy.data.objects.new("RF First Person", fp_data)
    scene.collection.objects.link(fp)
    fp.rotation_euler = (Vector((0, -1, 0))).to_track_quat("-Z", "Y").to_euler()
    fp_data.lens = 14.406464576721191
    fp_data.clip_start = 0.01
    fp.hide_set(True)
    return {"minimum": list(minimum), "maximum": list(maximum), "previewFocusMinimum": list(focus_minimum)}


def export(rig, meshes, output, report):
    scene = bpy.context.scene
    activate(rig)
    for mesh in meshes:
        mesh.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(output) + ".fbx", use_selection=True,
        object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        use_space_transform=True, bake_space_transform=False, use_mesh_modifiers=True,
        mesh_smooth_type="OFF", primary_bone_axis="X", secondary_bone_axis="-Y",
        armature_nodetype="NULL", use_armature_deform_only=True, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode="AUTO")
    report["fbxSettings"] = {"forward": "-Z", "up": "Y", "unit": "m", "scale": 1,
        "applyScalings": "FBX_ALL", "primaryBoneAxis": "X", "secondaryBoneAxis": "-Y",
        "deformOnly": True, "leafBones": False, "simplify": 0}
    rig["ravenfield_adapter"] = json.dumps(report, ensure_ascii=False)
    for image in bpy.data.images:
        if image.has_data and not image.packed_file:
            image.pack()
    # Save a useful modeling view without changing bind pose or hiding the hand meshes.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                area.spaces.active.region_3d.view_perspective = "CAMERA"
    bpy.ops.wm.save_as_mainfile(filepath=str(output) + ".blend")
    rig.hide_render = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(output) + ".preview.png"
    bpy.ops.render.render(write_still=True)
    Path(str(output) + ".report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--rf", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    sources = {p.resolve() for p in (args.input, args.rf, args.config)}
    destinations = [Path(str(args.output.resolve()) + suffix) for suffix in SUFFIXES]
    require(not sources.intersection(destinations), "Outputs must not overwrite inputs")
    require(all(p.is_file() for p in sources), "An input file is missing")
    config = json.loads(args.config.read_text(encoding="utf-8-sig"))
    validate_config(config)
    if config["mode"] == "library":
        sources.update(Path(c["path"]).resolve() for c in config["clips"])
        require(args.input.resolve() == Path(config["clips"][config.get("referenceClip", 0)]["path"]).resolve(),
                "--input must match the library reference clip")
        require(all(p.is_file() for p in sources) and not sources.intersection(destinations), "Invalid library input/output paths")
    hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    report = {"schemaVersion": 1, "mode": "idle-calibration-pose", "sourceUnit": config["sourceUnit"],
              "sourceToMeters": UNIT_FACTORS[config["sourceUnit"]], "rfUnit": "m", "warnings": [],
              "inputHashes": hashes, "config": config, "blender": bpy.app.version_string}
    report["sourceAxisAssumptions"] = config.get("sourceAxisAssumptions", [])
    for assumption in report["sourceAxisAssumptions"]:
        report["warnings"].append(
            f"未标记武器/附件按 {assumption['axis'].upper()}-up 处理 / Untagged model interpreted as "
            f"{assumption['axis'].upper()}-up: {assumption['path']}")
    with tempfile.TemporaryDirectory(prefix="alchemy-rf-") as work:
        temporary = Path(work)
        cod, weapon_meshes, frame = load_cast(args.input.resolve(), temporary, config, report)
        original_pose, original_rest = armature_pose(cod), armature_rest(cod)
        required = [f"j_{joint}_{side}" for side in ("le", "ri") for joint in ("shoulder", "elbow", "wrist")]
        required += [f"j_{finger}_{side}_{i}" for side in ("le", "ri") for finger in ("index", "pinky", "thumb") for i in (1, 2, 3)]
        require(all(n in original_pose for n in required), "Unsupported COD skeleton; missing " + ", ".join(n for n in required if n not in original_pose))
        factor = UNIT_FACTORS[config["sourceUnit"]]
        transform = source_transform(original_rest, original_pose, factor)
        cod_pose = {n: transform @ m for n, m in original_pose.items()}
        cod_rest = {n: transform @ m for n, m in original_rest.items()}
        length = (cod_rest["j_shoulder_le"].translation - cod_rest["j_elbow_le"].translation).length
        require(0.08 <= length <= 0.8,
                f"COD upper arm is {length:.3f} m with sourceUnit={config['sourceUnit']}; check the numeric input unit before fitting")
        rf_path, template = extract_rf_source(args.rf.resolve(), temporary)
        rf, rf_meshes = load_rf(rf_path, config["handScale"])
        report.update(template=template, evaluatedSourceFrame=frame, sourceUpperArmM=length,
                      viewTransform=[list(row) for row in transform], outputFrames=[1, 2],
                      warningScope="A fitted idle pose, not a full animation retarget or an in-game validation")
        if config["mode"] == "pose":
            if config['contactFit']:
                sys.path.insert(0, str(Path(__file__).resolve().parent))
                from ravenfield_contact import PalmContactFitter, contact_warning
                source_meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH'
                                 and o.name.startswith('COD_ReferenceHands_')]
                if config['localHandFit']:
                    from ravenfield_shape import prepare_grip
                    contact = prepare_grip(cod,rf,source_meshes,rf_meshes,transform,cod_pose,cod_rest,config,report)
                else:
                    contact = PalmContactFitter(cod, rf, source_meshes, rf_meshes, transform)
                detail = contact.fit(cod_pose, cod_rest, config, report)
                warning = contact_warning(detail)
                if warning: report['warnings'].append(warning)
            else:
                fit_hands(rf, cod_pose, cod_rest, config, report)
            rig, meshes, verification = combine_rigs(rf, rf_meshes, cod, weapon_meshes, transform, factor, config)
        else:
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            from ravenfield_animation import bake_sequence
            rig, meshes, verification = bake_sequence(rf, rf_meshes, cod, weapon_meshes, transform, factor,
                                                       config, report, args.input.resolve(), temporary)
        report.update(bones=len(rig.data.bones), meshes=len(meshes), **verification)
        report["boundsM"] = configure_scene(rig, meshes)
        export(rig, meshes, args.output.resolve(), report)
    require(all(p.is_file() and p.stat().st_size for p in destinations), "Output package is incomplete")
    require(all(hashlib.sha256(Path(p).read_bytes()).hexdigest() == digest for p, digest in hashes.items()), "An input was modified")
    status = 'RAVENFIELD_ADAPTER_REVIEW_REQUIRED' if report.get('palmContact', {}).get('passed') is False else 'RAVENFIELD_ADAPTER_PASS'
    print(status + ' ' + json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
