//! Arms (viewhands) + weapon assembly: the weapon's root bone (`j_gun`) is
//! spliced into the arms skeleton as a zero-offset child of `tag_weapon`,
//! preserving the weapon's bone hierarchy and skin weights.
#[cfg(test)]
#[path = "armature_tests.rs"]
mod tests;
use crate::{MergeError, MergeObserver, MergeStage, cast_model, domain::Model, math::Vec3};
use cast_codec::{CastFile, CastNode, CastProperty, PropertyValues};
use std::{
    collections::{HashMap, HashSet},
    path::{Path, PathBuf},
};

#[derive(Debug, Clone)]
pub struct AssembleRequest {
    pub arms: PathBuf,
    pub weapon: PathBuf,
    pub output: PathBuf,
    /// Explicit arms bone name; auto-detected when absent.
    pub target_bone: Option<String>,
}

#[derive(Debug, Clone)]
pub struct AssembleResult {
    pub output: PathBuf,
    pub target_bone: String,
    pub attached_meshes: usize,
}

fn invalid(message: impl Into<String>) -> MergeError {
    MergeError::InvalidModel(message.into())
}

/// Known weapon part-type segments used to derive the weapon code from a
/// file stem: the segment before the last part-type segment.
const PART_TYPES: &[&str] = &[
    "rec", "barl", "mag", "bolt", "grip", "muz", "muzzle", "stck", "stock", "hand", "charge",
    "tube", "body", "trig",
];

/// Derives the weapon code from a file stem, e.g.
/// `att_sat_vm_ar_eagle_rec_LOD0` -> `eagle`. Strips the `_LOD<n>` suffix and
/// trailing numeric / `_v<n>` segments, then takes the segment before the last
/// known part-type segment; falls back to the whole stem.
pub fn weapon_code(stem: &str) -> String {
    let mut segments: Vec<&str> = stem.split('_').collect();
    if let Some(last) = segments.last() {
        let lowered = last.to_ascii_lowercase();
        if let Some(digits) = lowered.strip_prefix("lod")
            && !digits.is_empty()
            && digits.bytes().all(|b| b.is_ascii_digit())
        {
            segments.pop();
        }
    }
    while let Some(last) = segments.last() {
        let lowered = last.to_ascii_lowercase();
        let numeric = !last.is_empty() && last.bytes().all(|b| b.is_ascii_digit());
        let version = lowered
            .strip_prefix('v')
            .is_some_and(|rest| !rest.is_empty() && rest.bytes().all(|b| b.is_ascii_digit()));
        if numeric || version {
            segments.pop();
        } else {
            break;
        }
    }
    for index in (1..segments.len()).rev() {
        if PART_TYPES.contains(&segments[index].to_ascii_lowercase().as_str()) {
            return segments[index - 1].to_owned();
        }
    }
    segments.join("_")
}

fn read(path: &Path, observer: &impl MergeObserver) -> Result<(CastFile, Model), MergeError> {
    crate::domain::check_cancelled(observer)?;
    if !path
        .extension()
        .is_some_and(|s| s.eq_ignore_ascii_case("cast"))
    {
        return Err(invalid("Expected a .cast model"));
    }
    let bytes = std::fs::read(path).map_err(|source| MergeError::Io {
        path: path.into(),
        source,
    })?;
    let raw = CastFile::decode_with_cancel(&bytes, || observer.is_cancelled())
        .map_err(MergeError::Codec)?;
    let mut model =
        cast_model::decode_model_with_cancel(&bytes, path, &|| observer.is_cancelled())?;
    if names_duplicate_or_too_many(&model) {
        return Err(invalid("Duplicate bone names or excessive skeleton size"));
    }
    resolve_globals(&mut model, observer)?;
    Ok((raw, model))
}

fn names_duplicate_or_too_many(model: &Model) -> bool {
    let names: HashSet<_> = model.bones.iter().map(|b| &b.name).collect();
    names.len() != model.bones.len() || model.bones.len() > 16384
}

/// Resolves global bone transforms iteratively so file ordering never matters;
/// rejects cycles, scaled bones and invalid transforms.
fn resolve_globals(model: &mut Model, observer: &impl MergeObserver) -> Result<(), MergeError> {
    let mut resolved = vec![false; model.bones.len()];
    for _ in 0..=model.bones.len() {
        crate::domain::check_cancelled(observer)?;
        for i in 0..model.bones.len() {
            if resolved[i] {
                continue;
            }
            let b = &model.bones[i];
            if b.scale != Vec3(1.0, 1.0, 1.0) {
                return Err(invalid(
                    "Scaled skeletons are not supported for arm assembly",
                ));
            }
            let p = b.parent;
            if p < -1 || p >= model.bones.len() as i32 {
                return Err(invalid("Invalid bone parent"));
            }
            if p >= 0 && !resolved[p as usize] {
                continue;
            }
            let (position, rotation) = if p == -1 {
                (b.local_position, b.local_rotation)
            } else {
                let parent = &model.bones[p as usize];
                (
                    parent.global_position + parent.global_rotation.rotate(b.local_position),
                    parent.global_rotation.multiply(b.local_rotation),
                )
            };
            let q: [f32; 4] = rotation.into();
            let v: [f32; 3] = position.into();
            if !v.iter().chain(q.iter()).all(|v| v.is_finite())
                || (q.iter().map(|x| x * x).sum::<f32>() - 1.0).abs() > 0.002
            {
                return Err(invalid("Invalid bone transform"));
            }
            model.bones[i].global_position = position;
            model.bones[i].global_rotation = rotation;
            resolved[i] = true;
        }
        if resolved.iter().all(|v| *v) {
            return Ok(());
        }
    }
    Err(invalid("Cyclic skeleton hierarchy"))
}

pub fn inspect_arms(path: &Path, observer: &impl MergeObserver) -> Result<Vec<String>, MergeError> {
    let (_, model) = read(path, observer)?;
    Ok(model.bones.iter().map(|bone| bone.name.clone()).collect())
}

/// Picks the arms bone that receives the weapon root: `tag_weapon` first
/// (the game convention inside `tag_torso`), then any weapon-ish bone, then
/// the first bone.
fn detect_target_bone(arms: &Model) -> String {
    if let Some(bone) = arms
        .bones
        .iter()
        .find(|bone| bone.name == "tag_weapon")
        .map(|bone| bone.name.clone())
    {
        return bone;
    }
    let contains = |needle: &str| {
        arms.bones
            .iter()
            .find(|bone| bone.name.to_ascii_lowercase().contains(needle))
            .map(|bone| bone.name.clone())
    };
    if let Some(name) = contains("tag_weapon") {
        return name;
    }
    if let Some(name) = contains("weapon") {
        return name;
    }
    arms.bones
        .first()
        .map(|bone| bone.name.clone())
        .unwrap_or_default()
}

fn model_node(file: &mut CastFile) -> Result<&mut CastNode, MergeError> {
    let mut nodes = file
        .roots
        .iter_mut()
        .flat_map(|r| &mut r.children)
        .filter(|n| n.identifier == u32::from_le_bytes(*b"modl"));
    let node = nodes.next().ok_or_else(|| invalid("No model node"))?;
    if nodes.next().is_some() {
        return Err(invalid("Multiple model nodes are not supported"));
    }
    if node
        .properties
        .iter()
        .any(|p| matches!(p.name.as_str(), "p" | "r" | "s"))
    {
        return Err(invalid(
            "Model-level transforms must be applied before assembly",
        ));
    }
    Ok(node)
}

fn resolve_texture_paths(node: &mut CastNode, directory: &Path) {
    if node.identifier == u32::from_le_bytes(*b"file") {
        for p in &mut node.properties {
            if p.name == "p"
                && let PropertyValues::String(value) = &mut p.values
                && !value.is_empty()
                && Path::new(value).is_relative()
            {
                *value = directory.join(&*value).to_string_lossy().into_owned();
            }
        }
    }
    for child in &mut node.children {
        resolve_texture_paths(child, directory);
    }
}

fn collect_hashes(node: &CastNode, used: &mut HashSet<u64>) {
    used.insert(node.hash);
    for child in &node.children {
        collect_hashes(child, used);
    }
}

fn allocate_hashes(
    node: &CastNode,
    used: &mut HashSet<u64>,
    mapping: &mut HashMap<u64, u64>,
    next: &mut u64,
) {
    mapping.entry(node.hash).or_insert_with(|| {
        while !used.insert(*next) {
            *next += 1;
        }
        let h = *next;
        *next += 1;
        h
    });
    for child in &node.children {
        allocate_hashes(child, used, mapping, next);
    }
}

fn remap(node: &mut CastNode, mapping: &HashMap<u64, u64>) {
    if let Some(h) = mapping.get(&node.hash) {
        node.hash = *h;
    }
    for prop in &mut node.properties {
        if let PropertyValues::Integer64(values) = &mut prop.values {
            for value in values {
                if let Some(h) = mapping.get(value) {
                    *value = *h;
                }
            }
        }
    }
    for child in &mut node.children {
        remap(child, mapping);
    }
}

fn set(node: &mut CastNode, name: &str, values: PropertyValues) {
    if let Some(p) = node.properties.iter_mut().find(|p| p.name == name) {
        p.values = values;
    } else {
        node.properties.push(CastProperty {
            name: name.into(),
            values,
        });
    }
}

fn weight_indices(node: &CastNode) -> Option<Vec<u32>> {
    let values = node
        .properties
        .iter()
        .find(|p| p.name == "wb")?
        .values
        .clone();
    match values {
        PropertyValues::Byte(values) => Some(values.iter().map(|v| u32::from(*v)).collect()),
        PropertyValues::Short(values) => Some(values.iter().map(|v| u32::from(*v)).collect()),
        PropertyValues::Integer32(values) => Some(values),
        _ => None,
    }
}

fn compact_unsigned(values: Vec<u32>, upper_bound: usize) -> PropertyValues {
    if upper_bound <= u8::MAX as usize {
        PropertyValues::Byte(values.into_iter().map(|value| value as u8).collect())
    } else if upper_bound <= u16::MAX as usize {
        PropertyValues::Short(values.into_iter().map(|value| value as u16).collect())
    } else {
        PropertyValues::Integer32(values)
    }
}

/// Unique bone name: colliding weapon bones (e.g. the weapon's `j_gun` versus
/// the arms' own `j_gun`) get a `_wpn` suffix.
fn unique_name(base: &str, used: &mut HashSet<String>) -> String {
    if used.insert(base.to_owned()) {
        return base.to_owned();
    }
    let mut candidate = format!("{base}_wpn");
    let mut suffix = 1;
    while !used.insert(candidate.clone()) {
        candidate = format!("{base}_wpn{suffix}");
        suffix += 1;
    }
    candidate
}

pub fn assemble(
    request: AssembleRequest,
    observer: &impl MergeObserver,
) -> Result<AssembleResult, MergeError> {
    observer.on_progress(MergeStage::Loading, 0, 2, None);
    if request.output.exists() {
        return Err(invalid("Output already exists; choose a new file name"));
    }
    crate::validate_output_name(
        request
            .output
            .file_name()
            .map(|s| s.to_string_lossy().into_owned()),
    )?;
    if !request
        .output
        .extension()
        .is_some_and(|s| s.eq_ignore_ascii_case("cast"))
    {
        return Err(invalid("Output must end in .cast"));
    }
    let (mut arms_raw, mut arms) = read(&request.arms, observer)?;
    observer.on_progress(MergeStage::Loading, 1, 2, None);
    let (mut weapon_raw, weapon) = read(&request.weapon, observer)?;
    if weapon.bones.is_empty() || weapon.meshes.is_empty() {
        return Err(invalid(
            "The weapon must contain meshes and a skeleton to attach",
        ));
    }
    let vertex_count: usize = weapon.meshes.iter().map(|m| m.positions.len()).sum();
    if weapon.meshes.len() > 512 || vertex_count > 5_000_000 {
        return Err(invalid(
            "The weapon exceeds the 5 million vertex / 512 mesh limit",
        ));
    }
    let target_bone = match &request.target_bone {
        Some(name) => {
            if !arms.bones.iter().any(|bone| &bone.name == name) {
                return Err(invalid(format!(
                    "The arms skeleton has no bone named {name}"
                )));
            }
            name.clone()
        }
        None => detect_target_bone(&arms),
    };
    let target_index = arms
        .bones
        .iter()
        .position(|bone| bone.name == target_bone)
        .expect("target bone was validated above");

    // Splice the weapon skeleton under the target bone: the root bone is
    // zeroed onto the target (the game convention for tag_weapon), child
    // bones keep their local transforms.
    let arms_bone_count = arms.bones.len();
    let mut used_names: HashSet<String> = arms.bones.iter().map(|b| b.name.clone()).collect();
    let mut name_map = Vec::with_capacity(weapon.bones.len());
    for bone in &weapon.bones {
        name_map.push(unique_name(&bone.name, &mut used_names));
    }
    let anchor_offset = arms_bone_count;
    for (index, bone) in weapon.bones.iter().enumerate() {
        let parent: i32 = if index == 0 || bone.parent < 0 {
            target_index as i32
        } else {
            (anchor_offset + bone.parent as usize) as i32
        };
        let (local_position, local_rotation) = if index == 0 {
            (Vec3::default(), crate::math::Quaternion::IDENTITY)
        } else {
            (bone.local_position, bone.local_rotation)
        };
        arms.bones.push(crate::domain::Bone {
            name: name_map[index].clone(),
            parent,
            local_position,
            local_rotation,
            global_position: Vec3::default(),
            global_rotation: crate::math::Quaternion::IDENTITY,
            scale: Vec3(1.0, 1.0, 1.0),
        });
    }
    resolve_globals(&mut arms, observer)?;
    let merged_bone_count = arms.bones.len();

    let weapon_source = model_node(&mut weapon_raw)?;
    resolve_texture_paths(
        weapon_source,
        request.weapon.parent().unwrap_or(Path::new(".")),
    );
    let mut used = HashSet::new();
    for root in &arms_raw.roots {
        collect_hashes(root, &mut used);
    }
    let mut next = 0x41524d5700000001;
    let destination = model_node(&mut arms_raw)?;
    resolve_texture_paths(destination, request.arms.parent().unwrap_or(Path::new(".")));

    // Append the weapon materials, then meshes / blend shapes with remapped
    // hashes and re-indexed skin weights.
    let mut mapping = HashMap::new();
    for node in weapon_source
        .children
        .iter()
        .filter(|n| n.identifier == u32::from_le_bytes(*b"matl"))
    {
        allocate_hashes(node, &mut used, &mut mapping, &mut next);
    }
    for node in weapon_source
        .children
        .iter()
        .filter(|n| n.identifier == u32::from_le_bytes(*b"matl"))
    {
        let mut node = node.clone();
        remap(&mut node, &mapping);
        destination.children.push(node);
    }
    let weapon_nodes: Vec<&CastNode> = weapon_source
        .children
        .iter()
        .filter(|n| matches!(&n.identifier.to_le_bytes(), b"mesh" | b"blsh"))
        .collect();
    for node in &weapon_nodes {
        allocate_hashes(node, &mut used, &mut mapping, &mut next);
    }
    for (index, node) in weapon_nodes.iter().enumerate() {
        crate::domain::check_cancelled(observer)?;
        observer.on_progress(
            MergeStage::Merging,
            index,
            weapon_nodes.len(),
            Some(&target_bone),
        );
        let mut node = (*node).clone();
        remap(&mut node, &mapping);
        if node.identifier == u32::from_le_bytes(*b"mesh") {
            let count = node
                .properties
                .iter()
                .find_map(|p| {
                    if p.name == "vp" {
                        if let PropertyValues::Vector3(v) = &p.values {
                            Some(v.len())
                        } else {
                            None
                        }
                    } else {
                        None
                    }
                })
                .unwrap_or(0);
            let weighted = weight_indices(&node).filter(|weights| !weights.is_empty());
            match weighted {
                Some(weights) => {
                    // Keep the original weights-per-vertex layout untouched;
                    // only the bone indices shift into the merged skeleton.
                    let remapped: Vec<u32> = weights
                        .into_iter()
                        .map(|value| value as usize + anchor_offset)
                        .map(|value| value as u32)
                        .collect();
                    set(
                        &mut node,
                        "wb",
                        compact_unsigned(remapped, merged_bone_count),
                    );
                }
                None => {
                    // Unweighted weapon meshes anchor rigidly to the spliced root.
                    set(
                        &mut node,
                        "wb",
                        compact_unsigned(vec![anchor_offset as u32; count], merged_bone_count),
                    );
                    set(&mut node, "wv", PropertyValues::Float(vec![1.0; count]));
                    set(&mut node, "mi", PropertyValues::Byte(vec![1]));
                }
            }
            let mesh_name = format!("assembled_{}_{}", name_map[0], node.hash);
            set(&mut node, "n", PropertyValues::String(mesh_name));
        }
        destination.children.push(node);
    }

    // Append the weapon bone nodes with the spliced hierarchy and recomputed
    // global transforms.
    let skeleton = destination
        .children
        .iter_mut()
        .find(|node| node.identifier == u32::from_le_bytes(*b"skel"))
        .ok_or_else(|| invalid("The arms model has no skeleton"))?;
    for index in 0..weapon.bones.len() {
        let bone = &arms.bones[anchor_offset + index];
        let mut node = CastNode {
            identifier: u32::from_le_bytes(*b"bone"),
            hash: 0,
            properties: vec![],
            children: vec![],
        };
        while !used.insert(next) {
            next += 1;
        }
        node.hash = next;
        next += 1;
        set(&mut node, "n", PropertyValues::String(bone.name.clone()));
        set(
            &mut node,
            "p",
            PropertyValues::Integer32(vec![bone.parent as u32]),
        );
        set(
            &mut node,
            "lp",
            PropertyValues::Vector3(vec![bone.local_position.into()]),
        );
        set(
            &mut node,
            "lr",
            PropertyValues::Vector4(vec![bone.local_rotation.into()]),
        );
        set(
            &mut node,
            "wp",
            PropertyValues::Vector3(vec![bone.global_position.into()]),
        );
        set(
            &mut node,
            "wr",
            PropertyValues::Vector4(vec![bone.global_rotation.into()]),
        );
        set(&mut node, "s", PropertyValues::Vector3(vec![[1.0; 3]]));
        skeleton.children.push(node);
    }

    observer.on_progress(MergeStage::Saving, 0, 1, None);
    let bytes = arms_raw.encode().map_err(MergeError::Codec)?;
    let temporary_path = crate::temporary_path(&request.output);
    let temporary = crate::TemporaryOutput::new(temporary_path.clone());
    std::fs::write(&temporary_path, &bytes).map_err(|source| MergeError::Io {
        path: temporary_path.clone(),
        source,
    })?;
    observer.on_progress(MergeStage::Verifying, 0, 1, None);
    let (_, verified) = read(&temporary_path, observer)?;
    if verified.meshes.len() != arms.meshes.len() + weapon.meshes.len()
        || verified.bones.len() != merged_bone_count
    {
        return Err(invalid("Output verification failed"));
    }
    crate::domain::check_cancelled(observer)?;
    crate::output::publish_new(&temporary_path, &request.output).map_err(|source| {
        MergeError::Io {
            path: request.output.clone(),
            source,
        }
    })?;
    drop(temporary);
    observer.on_progress(MergeStage::Completed, 1, 1, None);
    Ok(AssembleResult {
        output: request.output,
        target_bone,
        attached_meshes: weapon.meshes.len(),
    })
}
