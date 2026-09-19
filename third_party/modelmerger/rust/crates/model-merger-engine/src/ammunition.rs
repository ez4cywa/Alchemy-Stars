//! Rigid model instances attached to explicit magazine ammunition bones.
#[cfg(test)]
#[path = "ammunition_tests.rs"]
mod tests;
use crate::{MergeError, MergeObserver, MergeStage, cast_model, domain::Model, math::Vec3};
use cast_codec::{CastFile, CastNode, CastProperty, PropertyValues};
use std::{
    collections::{HashMap, HashSet},
    path::{Path, PathBuf},
};

#[derive(Debug, Clone)]
pub struct Magazine {
    pub name: String,
    pub slots: Vec<String>,
    pub occupied: Vec<String>,
}

#[derive(Debug, Clone)]
pub struct Analysis {
    pub magazines: Vec<Magazine>,
    pub excluded_slots: Vec<String>,
    /// Recognized magazine bones with no ammunition placement bones of their own.
    pub spare_magazines: Vec<String>,
}

#[derive(Debug, Clone)]
pub struct MagazineReplica {
    pub source: String,
    pub target: String,
}

#[derive(Debug, Clone)]
pub struct FillRequest {
    pub weapon: PathBuf,
    pub ammunition: PathBuf,
    pub output: PathBuf,
    pub magazines: Vec<String>,
    /// Explicit opt-in for numbered ammunition bones outside magazine subtrees.
    pub extra_slots: Vec<String>,
    /// Opt-in copies of a source magazine's local ammunition layout.
    pub replicas: Vec<MagazineReplica>,
}

#[derive(Debug, Clone)]
pub struct FillResult {
    pub output: PathBuf,
    pub inserted: usize,
    pub skipped: usize,
}

fn invalid(message: impl Into<String>) -> MergeError {
    MergeError::InvalidModel(message.into())
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
    // Resolve parents recursively instead of relying on file ordering; reject cycles/scales.
    let mut resolved = vec![false; model.bones.len()];
    let names: HashSet<_> = model.bones.iter().map(|b| &b.name).collect();
    if names.len() != model.bones.len() || model.bones.len() > 16384 {
        return Err(invalid("Duplicate bone names or excessive skeleton size"));
    }
    for _ in 0..=model.bones.len() {
        crate::domain::check_cancelled(observer)?;
        for i in 0..model.bones.len() {
            if resolved[i] {
                continue;
            }
            let b = &model.bones[i];
            if b.scale != Vec3(1.0, 1.0, 1.0) {
                return Err(invalid(
                    "Scaled skeletons are not supported for ammunition placement",
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
            return Ok((raw, model));
        }
    }
    Err(invalid("Cyclic skeleton hierarchy"))
}

fn numbered(name: &str, prefix: &str) -> bool {
    name.strip_prefix(prefix)
        .is_some_and(|s| !s.is_empty() && s.bytes().all(|b| b.is_ascii_digit()))
}

fn magazine_bone(name: &str) -> bool {
    ["j_mag", "tag_clip", "tag_mag", "j_magazine", "tag_magazine"]
        .iter()
        .any(|prefix| {
            name == *prefix || numbered(name, prefix) || numbered(name, &format!("{prefix}_"))
        })
}

fn analyze(model: &Model) -> Analysis {
    let mut magazines: Vec<Magazine> = Vec::new();
    let mut excluded_slots = Vec::new();
    for (index, bone) in model.bones.iter().enumerate() {
        if !numbered(&bone.name, "j_ammo_")
            && !numbered(&bone.name, "tag_ammo_")
            && !numbered(&bone.name, "cmm_ammo_")
        {
            continue;
        }
        let mut parent = bone.parent;
        let mut magazine = None;
        while parent >= 0 {
            let b = &model.bones[parent as usize];
            if magazine_bone(&b.name) {
                magazine = Some(b.name.clone());
                break;
            }
            parent = b.parent;
        }
        let Some(name) = magazine else {
            excluded_slots.push(bone.name.clone());
            continue;
        };
        let at = magazines
            .iter()
            .position(|g| g.name == name)
            .unwrap_or_else(|| {
                magazines.push(Magazine {
                    name,
                    slots: Vec::new(),
                    occupied: Vec::new(),
                });
                magazines.len() - 1
            });
        magazines[at].slots.push(bone.name.clone());
        if model.meshes.iter().any(|m| {
            m.weight_bones
                .iter()
                .zip(&m.weight_values)
                .any(|(b, w)| *b as usize == index && *w > 0.0)
        }) {
            magazines[at].occupied.push(bone.name.clone());
        }
    }
    for group in &mut magazines {
        group.slots.sort_by_key(|name| {
            name.rsplit('_')
                .next()
                .and_then(|s| s.parse::<u64>().ok())
                .unwrap_or(0)
        });
    }
    let spare_magazines = model
        .bones
        .iter()
        .filter(|bone| {
            magazine_bone(&bone.name) && !magazines.iter().any(|group| group.name == bone.name)
        })
        .filter(|candidate| {
            !magazines.iter().any(|group| {
                let mut parent = model
                    .bones
                    .iter()
                    .find(|bone| bone.name == group.name)
                    .unwrap()
                    .parent;
                while parent >= 0 {
                    let bone = &model.bones[parent as usize];
                    if bone.name == candidate.name {
                        return true;
                    }
                    parent = bone.parent;
                }
                false
            })
        })
        .map(|bone| bone.name.clone())
        .collect();
    Analysis {
        magazines,
        excluded_slots,
        spare_magazines,
    }
}

fn replica_layout(
    model: &mut Model,
    analysis: &Analysis,
    replicas: &[MagazineReplica],
) -> Result<Vec<crate::domain::Bone>, MergeError> {
    let mut result = Vec::new();
    let mut seen = HashSet::new();
    for replica in replicas {
        let source = analysis
            .magazines
            .iter()
            .find(|magazine| magazine.name == replica.source)
            .ok_or_else(|| invalid("Select a source magazine with ammunition bones"))?;
        if !analysis.spare_magazines.contains(&replica.target) || !seen.insert(&replica.target) {
            return Err(invalid("Select each empty spare magazine only once"));
        }
        if result.len() + source.slots.len() > 512 || model.bones.len() + source.slots.len() > 16384
        {
            return Err(invalid(
                "Spare magazine placement exceeds the bone / slot limit",
            ));
        }
        let source_bone = model
            .bones
            .iter()
            .find(|bone| bone.name == source.name)
            .unwrap()
            .clone();
        let target_index = model
            .bones
            .iter()
            .position(|bone| bone.name == replica.target)
            .unwrap();
        let target = model.bones[target_index].clone();
        let inverse = source_bone.global_rotation.inverse();
        for name in &source.slots {
            let slot = model.bones.iter().find(|bone| &bone.name == name).unwrap();
            let local_position = inverse.rotate(slot.global_position - source_bone.global_position);
            let local_rotation = inverse.multiply(slot.global_rotation);
            let mut suffix = model.bones.len();
            let name = loop {
                let name = format!("cmm_ammo_{suffix}");
                if !model.bones.iter().any(|bone| bone.name == name) {
                    break name;
                }
                suffix += 1;
            };
            let bone = crate::domain::Bone {
                name,
                parent: target_index as i32,
                local_position,
                local_rotation,
                global_position: target.global_position
                    + target.global_rotation.rotate(local_position),
                global_rotation: target.global_rotation.multiply(local_rotation),
                scale: Vec3(1.0, 1.0, 1.0),
            };
            model.bones.push(bone.clone());
            result.push(bone);
        }
    }
    Ok(result)
}

pub fn inspect(path: &Path, observer: &impl MergeObserver) -> Result<Analysis, MergeError> {
    let (mut raw, model) = read(path, observer)?;
    model_node(&mut raw)?;
    Ok(analyze(&model))
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
    // Model-space transformations would otherwise be applied twice by importers.
    if node
        .properties
        .iter()
        .any(|p| matches!(p.name.as_str(), "p" | "r" | "s"))
    {
        return Err(invalid(
            "Model-level transforms must be applied before filling",
        ));
    }
    Ok(node)
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

pub fn fill(request: FillRequest, observer: &impl MergeObserver) -> Result<FillResult, MergeError> {
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
    let (mut raw, mut model) = read(&request.weapon, observer)?;
    observer.on_progress(MergeStage::Loading, 1, 2, None);
    let (mut ammo_raw, ammo) = read(&request.ammunition, observer)?;
    if ammo.bones.len() != 1 || ammo.bones[0].name != "tag_ammo" || ammo.meshes.is_empty() {
        return Err(invalid(
            "Ammunition must contain meshes and one rigid tag_ammo bone",
        ));
    }
    let analysis = analyze(&model);
    let selected: HashSet<_> = request.magazines.iter().collect();
    if (selected.is_empty() && request.extra_slots.is_empty() && request.replicas.is_empty())
        || selected
            .iter()
            .any(|name| !analysis.magazines.iter().any(|m| &m.name == *name))
        || request
            .extra_slots
            .iter()
            .any(|name| !analysis.excluded_slots.contains(name))
    {
        return Err(invalid("Select a detected magazine"));
    }
    let mut skipped = 0;
    let mut targets: Vec<_> = analysis
        .magazines
        .iter()
        .filter(|m| selected.contains(&m.name))
        .flat_map(|m| {
            skipped += m.occupied.len();
            m.slots.iter().filter(|s| !m.occupied.contains(s)).cloned()
        })
        .collect();
    for name in &analysis.excluded_slots {
        if !request.extra_slots.contains(name) {
            continue;
        }
        let index = model.bones.iter().position(|b| &b.name == name).unwrap();
        let occupied = model.meshes.iter().any(|m| {
            m.weight_bones
                .iter()
                .zip(&m.weight_values)
                .any(|(b, w)| *b as usize == index && *w > 0.0)
        });
        if occupied {
            skipped += 1;
        } else {
            targets.push(name.clone());
        }
    }
    let replica_bones = replica_layout(&mut model, &analysis, &request.replicas)?;
    targets.extend(replica_bones.iter().map(|bone| bone.name.clone()));
    if targets.is_empty() {
        return Err(invalid(
            "No empty ammunition bones in the selected magazines",
        ));
    }
    let vertex_count: usize = ammo.meshes.iter().map(|m| m.positions.len()).sum();
    if targets.len() > 512
        || vertex_count
            .checked_mul(targets.len())
            .is_none_or(|n| n > 5_000_000)
    {
        return Err(invalid(
            "Ammunition instances exceed the 5 million vertex / 512 slot limit",
        ));
    }
    let source = model_node(&mut ammo_raw)?;
    resolve_texture_paths(
        source,
        request.ammunition.parent().unwrap_or(Path::new(".")),
    );
    let mut used = HashSet::new();
    for root in &raw.roots {
        collect_hashes(root, &mut used);
    }
    let mut next = 0x43414d4d00000001;
    let destination = model_node(&mut raw)?;
    if !replica_bones.is_empty() {
        let skeleton = destination
            .children
            .iter_mut()
            .find(|node| node.identifier == u32::from_le_bytes(*b"skel"))
            .ok_or_else(|| invalid("No skeleton for spare magazine placement"))?;
        for bone in &replica_bones {
            while !used.insert(next) {
                next += 1;
            }
            let mut node = CastNode {
                identifier: u32::from_le_bytes(*b"bone"),
                hash: next,
                properties: vec![],
                children: vec![],
            };
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
    }
    resolve_texture_paths(
        destination,
        request.weapon.parent().unwrap_or(Path::new(".")),
    );
    let mut material_mapping = HashMap::new();
    for n in source
        .children
        .iter()
        .filter(|n| n.identifier == u32::from_le_bytes(*b"matl"))
    {
        allocate_hashes(n, &mut used, &mut material_mapping, &mut next);
    }
    for n in source
        .children
        .iter()
        .filter(|n| n.identifier == u32::from_le_bytes(*b"matl"))
    {
        let mut n = n.clone();
        remap(&mut n, &material_mapping);
        destination.children.push(n);
    }
    for (i, name) in targets.iter().enumerate() {
        crate::domain::check_cancelled(observer)?;
        observer.on_progress(MergeStage::Merging, i, targets.len(), Some(name));
        let bone_index = model.bones.iter().position(|b| &b.name == name).unwrap();
        let target = &model.bones[bone_index];
        let anchor = &ammo.bones[0];
        let rotation = target
            .global_rotation
            .multiply(anchor.global_rotation.inverse());
        let transform = |v: [f32; 3]| -> [f32; 3] {
            (target.global_position + rotation.rotate(Vec3::from(v) - anchor.global_position))
                .into()
        };
        let mut mapping = material_mapping.clone();
        for n in source
            .children
            .iter()
            .filter(|n| matches!(&n.identifier.to_le_bytes(), b"mesh" | b"blsh"))
        {
            allocate_hashes(n, &mut used, &mut mapping, &mut next);
        }
        for n in source
            .children
            .iter()
            .filter(|n| matches!(&n.identifier.to_le_bytes(), b"mesh" | b"blsh"))
        {
            let mut n = n.clone();
            remap(&mut n, &mapping);
            for p in &mut n.properties {
                if let PropertyValues::Vector3(values) = &mut p.values {
                    if p.name == "vp" {
                        for v in values {
                            *v = transform(*v);
                        }
                    } else if p.name == "vn" {
                        for v in values {
                            *v = rotation.rotate((*v).into()).into();
                        }
                    }
                }
            }
            if n.identifier == u32::from_le_bytes(*b"mesh") {
                let count = n
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
                let mesh_name = format!("filled_{name}_{}", n.hash);
                set(&mut n, "n", PropertyValues::String(mesh_name));
                set(&mut n, "mi", PropertyValues::Byte(vec![1]));
                set(
                    &mut n,
                    "wb",
                    PropertyValues::Integer32(vec![bone_index as u32; count]),
                );
                set(&mut n, "wv", PropertyValues::Float(vec![1.0; count]));
            }
            destination.children.push(n);
        }
    }
    observer.on_progress(MergeStage::Saving, 0, 1, None);
    let bytes = raw.encode().map_err(MergeError::Codec)?;
    let temporary_path = crate::temporary_path(&request.output);
    let temporary = crate::TemporaryOutput::new(temporary_path.clone());
    std::fs::write(&temporary_path, &bytes).map_err(|source| MergeError::Io {
        path: temporary_path.clone(),
        source,
    })?;
    observer.on_progress(MergeStage::Verifying, 0, 1, None);
    let (_, verified) = read(&temporary_path, observer)?;
    if verified.meshes.len() != model.meshes.len() + targets.len() * ammo.meshes.len() {
        return Err(invalid("Output mesh verification failed"));
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
    Ok(FillResult {
        output: request.output,
        inserted: targets.len(),
        skipped,
    })
}
