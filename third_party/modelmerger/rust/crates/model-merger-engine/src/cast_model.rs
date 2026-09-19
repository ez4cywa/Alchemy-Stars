use crate::MergeError;
use crate::domain::{Bone, Mesh, Model, ShapeDelta};
use crate::math::Vec3;
use cast_codec::{CastFile, CastNode, CastProperty, PropertyValues};
use std::collections::HashMap;
use std::path::Path;

const ROOT: u32 = u32::from_le_bytes(*b"root");
const MODEL: u32 = u32::from_le_bytes(*b"modl");
const SKELETON: u32 = u32::from_le_bytes(*b"skel");
const BONE: u32 = u32::from_le_bytes(*b"bone");
const MESH: u32 = u32::from_le_bytes(*b"mesh");
const MATERIAL: u32 = u32::from_le_bytes(*b"matl");
const FILE: u32 = u32::from_le_bytes(*b"file");
const BLEND_SHAPE: u32 = u32::from_le_bytes(*b"blsh");

pub(crate) fn decode_model(bytes: &[u8], path: &Path) -> Result<Model, MergeError> {
    decode_model_with_cancel(bytes, path, &|| false)
}

pub(crate) fn decode_model_with_cancel(
    bytes: &[u8],
    path: &Path,
    is_cancelled: &impl Fn() -> bool,
) -> Result<Model, MergeError> {
    let file = CastFile::decode_with_cancel(bytes, is_cancelled).map_err(|error| match error {
        cast_codec::CodecError::Cancelled => MergeError::Cancelled,
        error => MergeError::Codec(error),
    })?;
    check_decode_cancelled(is_cancelled)?;
    let model_node = file
        .roots
        .iter()
        .flat_map(|root| &root.children)
        .find(|node| node.identifier == MODEL)
        .ok_or_else(|| MergeError::InvalidModel("Cast file does not contain a model".into()))?;
    let name = path
        .file_stem()
        .and_then(|value| value.to_str())
        .ok_or_else(|| MergeError::InvalidModel("model file name is not valid UTF-8".into()))?
        .to_owned();

    let bones = model_node
        .children
        .iter()
        .find(|node| node.identifier == SKELETON)
        .map(|skeleton| {
            skeleton
                .children
                .iter()
                .filter(|node| node.identifier == BONE)
                .map(decode_bone)
                .collect::<Result<Vec<_>, _>>()
        })
        .transpose()?
        .unwrap_or_default();

    let material_nodes: Vec<&CastNode> = model_node
        .children
        .iter()
        .filter(|node| node.identifier == MATERIAL)
        .collect();
    let materials = material_nodes
        .iter()
        .map(|node| string_property(node, "n"))
        .collect::<Result<Vec<_>, _>>()?;
    let material_by_hash: HashMap<u64, usize> = material_nodes
        .iter()
        .enumerate()
        .map(|(index, node)| (node.hash, index))
        .collect();

    let blend_nodes: Vec<&CastNode> = model_node
        .children
        .iter()
        .filter(|node| node.identifier == BLEND_SHAPE)
        .collect();
    let mut shapes = Vec::with_capacity(blend_nodes.len());
    for blend in &blend_nodes {
        let name = string_property(blend, "n")?;
        if !shapes.contains(&name) {
            shapes.push(name);
        }
    }

    let mesh_nodes: Vec<&CastNode> = model_node
        .children
        .iter()
        .filter(|node| node.identifier == MESH)
        .collect();
    let mut meshes = Vec::with_capacity(mesh_nodes.len());
    for mesh_node in mesh_nodes {
        check_decode_cancelled(is_cancelled)?;
        meshes.push(decode_mesh(
            mesh_node,
            &material_by_hash,
            &blend_nodes,
            &shapes,
        )?);
    }

    Ok(Model {
        name,
        bones,
        meshes,
        materials,
        shapes,
    })
}

fn check_decode_cancelled(is_cancelled: &impl Fn() -> bool) -> Result<(), MergeError> {
    if is_cancelled() {
        Err(MergeError::Cancelled)
    } else {
        Ok(())
    }
}

fn decode_bone(node: &CastNode) -> Result<Bone, MergeError> {
    Ok(Bone {
        name: string_property(node, "n")?,
        parent: u32_property(node, "p")? as i32,
        local_position: vector3_property(node, "lp")?.into(),
        local_rotation: vector4_property(node, "lr")?.into(),
        global_position: optional_vector3_property(node, "wp")
            .unwrap_or_default()
            .into(),
        global_rotation: optional_vector4_property(node, "wr")
            .unwrap_or([0.0, 0.0, 0.0, 1.0])
            .into(),
        scale: optional_vector3_property(node, "s")
            .unwrap_or([1.0, 1.0, 1.0])
            .into(),
    })
}

fn decode_mesh(
    node: &CastNode,
    material_by_hash: &HashMap<u64, usize>,
    blends: &[&CastNode],
    shapes: &[String],
) -> Result<Mesh, MergeError> {
    let positions = vector3_values(node, "vp")?
        .iter()
        .copied()
        .map(Into::into)
        .collect::<Vec<_>>();
    let vertex_count = positions.len();
    let normals = vector3_values(node, "vn")?
        .iter()
        .copied()
        .map(Into::into)
        .collect::<Vec<_>>();
    if normals.len() != vertex_count {
        return Err(MergeError::InvalidModel(
            "normal buffer length does not match vertex count".into(),
        ));
    }
    let uvs = match property(node, "u0") {
        Some(PropertyValues::Vector2(values)) if values.len() == vertex_count => values.clone(),
        Some(_) => {
            return Err(MergeError::InvalidModel(
                "UV buffer has an unsupported type or length".into(),
            ));
        }
        None => vec![[0.0, 0.0]; vertex_count],
    };
    let colors = match property(node, "vc") {
        Some(PropertyValues::Integer32(values)) if values.len() == vertex_count => values
            .iter()
            .map(|value| {
                [
                    (*value & 0xff) as f32 / 255.0,
                    ((*value >> 8) & 0xff) as f32 / 255.0,
                    ((*value >> 16) & 0xff) as f32 / 255.0,
                    ((*value >> 24) & 0xff) as f32 / 255.0,
                ]
            })
            .collect(),
        Some(_) => {
            return Err(MergeError::InvalidModel(
                "color buffer has an unsupported type or length".into(),
            ));
        }
        None => vec![[1.0, 1.0, 1.0, 1.0]; vertex_count],
    };
    let weights_per_vertex = optional_unsigned_scalar(node, "mi")?.unwrap_or(0) as usize;
    let weight_bones = unsigned_values(node, "wb")?.unwrap_or_default();
    let weight_values = match property(node, "wv") {
        Some(PropertyValues::Float(values)) => values.clone(),
        Some(_) => {
            return Err(MergeError::InvalidModel(
                "invalid weight value buffer".into(),
            ));
        }
        None => Vec::new(),
    };
    let expected_weights = vertex_count
        .checked_mul(weights_per_vertex)
        .ok_or_else(|| MergeError::InvalidModel("weight count overflow".into()))?;
    if weight_bones.len() != expected_weights || weight_values.len() != expected_weights {
        return Err(MergeError::InvalidModel(
            "weight buffer length does not match vertex count".into(),
        ));
    }
    let faces = unsigned_values(node, "f")?
        .ok_or_else(|| MergeError::InvalidModel("mesh is missing face indices".into()))?;
    if faces.len() % 3 != 0 {
        return Err(MergeError::InvalidModel(
            "face index count is not divisible by three".into(),
        ));
    }
    let material_indices = match property(node, "m") {
        Some(PropertyValues::Integer64(values)) => values
            .first()
            .map(|hash| {
                material_by_hash
                    .get(hash)
                    .copied()
                    .map(|index| vec![index])
                    .ok_or_else(|| {
                        MergeError::InvalidModel("mesh references a missing material".into())
                    })
            })
            .transpose()?
            .unwrap_or_default(),
        Some(_) => return Err(MergeError::InvalidModel("invalid material buffer".into())),
        None => Vec::new(),
    };

    let mut shape_deltas = Vec::new();
    for blend in blends {
        if optional_u64_scalar(blend, "b")? != Some(node.hash) {
            continue;
        }
        let shape_name = string_property(blend, "n")?;
        let shape_index = shapes
            .iter()
            .position(|name| name == &shape_name)
            .ok_or_else(|| MergeError::InvalidModel("blend shape name is missing".into()))?;
        let indices = unsigned_values(blend, "vi")?
            .ok_or_else(|| MergeError::InvalidModel("blend shape is missing indices".into()))?;
        let targets = vector3_values(blend, "vp")?;
        if indices.len() != targets.len() {
            return Err(MergeError::InvalidModel(
                "blend shape index and position counts differ".into(),
            ));
        }
        for (index, target) in indices.iter().zip(targets) {
            let vertex_index = usize::try_from(*index)
                .map_err(|_| MergeError::InvalidModel("invalid blend vertex index".into()))?;
            let position = *positions.get(vertex_index).ok_or_else(|| {
                MergeError::InvalidModel("blend shape references a missing vertex".into())
            })?;
            shape_deltas.push(ShapeDelta {
                shape_index,
                vertex_index,
                delta: Vec3::from(*target) - position,
            });
        }
    }

    Ok(Mesh {
        positions,
        normals,
        tangents: vec![Vec3::default(); vertex_count],
        colors,
        uvs,
        faces,
        weights_per_vertex,
        weight_bones,
        weight_values,
        material_indices,
        shape_deltas,
    })
}

pub(crate) fn encode_model(model: &Model) -> Result<Vec<u8>, MergeError> {
    let mut model_children = Vec::new();
    model_children.push(encode_skeleton(model));
    let material_hashes: Vec<u64> = model.materials.iter().map(|name| fnv1a(name)).collect();
    model_children.extend(
        model
            .materials
            .iter()
            .zip(&material_hashes)
            .map(|(name, hash)| encode_material(name, *hash)),
    );
    for (index, mesh) in model.meshes.iter().enumerate() {
        let mesh_name = if index == 0 {
            "CastMesh".to_owned()
        } else {
            format!("CastMesh{index}")
        };
        let mesh_hash = fnv1a(&mesh_name);
        model_children.push(encode_mesh(mesh, mesh_hash, &material_hashes)?);
        model_children.extend(encode_blend_shapes(model, mesh, mesh_hash)?);
    }

    CastFile {
        version: 1,
        flags: 0,
        roots: vec![CastNode {
            identifier: ROOT,
            hash: 0,
            properties: Vec::new(),
            children: vec![CastNode {
                identifier: MODEL,
                hash: 0,
                properties: Vec::new(),
                children: model_children,
            }],
        }],
    }
    .encode()
    .map_err(MergeError::Codec)
}

fn encode_skeleton(model: &Model) -> CastNode {
    CastNode {
        identifier: SKELETON,
        hash: 0,
        properties: Vec::new(),
        children: model
            .bones
            .iter()
            .map(|bone| CastNode {
                identifier: BONE,
                hash: 0,
                properties: vec![
                    prop(
                        "lp",
                        PropertyValues::Vector3(vec![bone.local_position.into()]),
                    ),
                    prop(
                        "lr",
                        PropertyValues::Vector4(vec![bone.local_rotation.into()]),
                    ),
                    prop("n", PropertyValues::String(bone.name.clone())),
                    prop("p", PropertyValues::Integer32(vec![bone.parent as u32])),
                    prop("s", PropertyValues::Vector3(vec![bone.scale.into()])),
                    prop(
                        "wp",
                        PropertyValues::Vector3(vec![bone.global_position.into()]),
                    ),
                    prop(
                        "wr",
                        PropertyValues::Vector4(vec![bone.global_rotation.into()]),
                    ),
                ],
                children: Vec::new(),
            })
            .collect(),
    }
}

fn encode_material(name: &str, hash: u64) -> CastNode {
    let empty_hash = fnv1a("");
    CastNode {
        identifier: MATERIAL,
        hash,
        properties: vec![
            prop("albedo", PropertyValues::Integer64(vec![empty_hash])),
            prop("gloss", PropertyValues::Integer64(vec![empty_hash])),
            prop("n", PropertyValues::String(name.to_owned())),
            prop("normal", PropertyValues::Integer64(vec![empty_hash])),
            prop("specular", PropertyValues::Integer64(vec![empty_hash])),
            prop("t", PropertyValues::String("pbr".to_owned())),
        ],
        children: (0..4)
            .map(|_| CastNode {
                identifier: FILE,
                hash: empty_hash,
                properties: vec![prop("p", PropertyValues::String(String::new()))],
                children: Vec::new(),
            })
            .collect(),
    }
}

fn encode_mesh(mesh: &Mesh, hash: u64, material_hashes: &[u64]) -> Result<CastNode, MergeError> {
    if mesh.positions.len() != mesh.normals.len()
        || mesh.positions.len() != mesh.colors.len()
        || mesh.positions.len() != mesh.uvs.len()
    {
        return Err(MergeError::InvalidModel(
            "mesh vertex buffers have inconsistent lengths".into(),
        ));
    }
    let filtered_faces: Vec<u32> = mesh
        .faces
        .chunks_exact(3)
        .filter(|face| face[0] != face[1] && face[1] != face[2] && face[2] != face[0])
        .flatten()
        .copied()
        .collect();
    let faces = compact_unsigned(filtered_faces, mesh.positions.len());
    let weight_bones = compact_unsigned(mesh.weight_bones.clone(), model_bone_limit(mesh));
    let materials = mesh
        .material_indices
        .iter()
        .map(|index| {
            material_hashes.get(*index).copied().ok_or_else(|| {
                MergeError::InvalidModel("mesh references a missing material".into())
            })
        })
        .collect::<Result<Vec<_>, _>>()?;
    Ok(CastNode {
        identifier: MESH,
        hash,
        properties: vec![
            prop("f", faces),
            prop("m", PropertyValues::Integer64(materials)),
            prop(
                "mi",
                PropertyValues::Byte(vec![mesh.weights_per_vertex as u8]),
            ),
            prop("u0", PropertyValues::Vector2(mesh.uvs.clone())),
            prop("ul", PropertyValues::Byte(vec![1])),
            prop(
                "vc",
                PropertyValues::Integer32(mesh.colors.iter().map(pack_color).collect()),
            ),
            prop(
                "vn",
                PropertyValues::Vector3(mesh.normals.iter().copied().map(Into::into).collect()),
            ),
            prop(
                "vp",
                PropertyValues::Vector3(mesh.positions.iter().copied().map(Into::into).collect()),
            ),
            prop("wb", weight_bones),
            prop("wv", PropertyValues::Float(mesh.weight_values.clone())),
        ],
        children: Vec::new(),
    })
}

fn encode_blend_shapes(
    model: &Model,
    mesh: &Mesh,
    mesh_hash: u64,
) -> Result<Vec<CastNode>, MergeError> {
    let mut by_shape: Vec<Vec<&ShapeDelta>> = vec![Vec::new(); model.shapes.len()];
    for delta in &mesh.shape_deltas {
        if delta.delta != Vec3::default() {
            by_shape
                .get_mut(delta.shape_index)
                .ok_or_else(|| MergeError::InvalidModel("missing shape name".into()))?
                .push(delta);
        }
    }
    let mut result = Vec::new();
    for (shape_index, deltas) in by_shape.into_iter().enumerate() {
        if deltas.is_empty() {
            continue;
        }
        let mut indices = Vec::with_capacity(deltas.len());
        let mut positions = Vec::with_capacity(deltas.len());
        for delta in deltas {
            let base = *mesh.positions.get(delta.vertex_index).ok_or_else(|| {
                MergeError::InvalidModel("shape delta references a missing vertex".into())
            })?;
            indices.push(delta.vertex_index as u32);
            positions.push((base + delta.delta).into());
        }
        result.push(CastNode {
            identifier: BLEND_SHAPE,
            hash: 0,
            properties: vec![
                prop("b", PropertyValues::Integer64(vec![mesh_hash])),
                prop(
                    "n",
                    PropertyValues::String(model.shapes[shape_index].clone()),
                ),
                prop("ts", PropertyValues::Float(vec![1.0])),
                prop("vi", compact_unsigned(indices, mesh.positions.len())),
                prop("vp", PropertyValues::Vector3(positions)),
            ],
            children: Vec::new(),
        });
    }
    Ok(result)
}

fn property<'a>(node: &'a CastNode, name: &str) -> Option<&'a PropertyValues> {
    node.properties
        .iter()
        .find(|property| property.name == name)
        .map(|property| &property.values)
}

fn string_property(node: &CastNode, name: &str) -> Result<String, MergeError> {
    match property(node, name) {
        Some(PropertyValues::String(value)) => Ok(value.clone()),
        _ => Err(MergeError::InvalidModel(format!(
            "node is missing string property '{name}'"
        ))),
    }
}

fn u32_property(node: &CastNode, name: &str) -> Result<u32, MergeError> {
    optional_unsigned_scalar(node, name)?.ok_or_else(|| {
        MergeError::InvalidModel(format!("node is missing integer property '{name}'"))
    })
}

fn optional_unsigned_scalar(node: &CastNode, name: &str) -> Result<Option<u32>, MergeError> {
    let values = match property(node, name) {
        None => return Ok(None),
        Some(PropertyValues::Byte(values)) => values.first().copied().map(u32::from),
        Some(PropertyValues::Short(values)) => values.first().copied().map(u32::from),
        Some(PropertyValues::Integer32(values)) => values.first().copied(),
        Some(_) => {
            return Err(MergeError::InvalidModel(format!(
                "invalid integer property '{name}'"
            )));
        }
    };
    Ok(values)
}

fn optional_u64_scalar(node: &CastNode, name: &str) -> Result<Option<u64>, MergeError> {
    match property(node, name) {
        None => Ok(None),
        Some(PropertyValues::Integer64(values)) => Ok(values.first().copied()),
        Some(_) => Err(MergeError::InvalidModel(format!(
            "invalid u64 property '{name}'"
        ))),
    }
}

fn unsigned_values(node: &CastNode, name: &str) -> Result<Option<Vec<u32>>, MergeError> {
    match property(node, name) {
        None => Ok(None),
        Some(PropertyValues::Byte(values)) => {
            Ok(Some(values.iter().copied().map(u32::from).collect()))
        }
        Some(PropertyValues::Short(values)) => {
            Ok(Some(values.iter().copied().map(u32::from).collect()))
        }
        Some(PropertyValues::Integer32(values)) => Ok(Some(values.clone())),
        Some(_) => Err(MergeError::InvalidModel(format!(
            "invalid integer buffer '{name}'"
        ))),
    }
}

fn vector3_property(node: &CastNode, name: &str) -> Result<[f32; 3], MergeError> {
    optional_vector3_property(node, name).ok_or_else(|| {
        MergeError::InvalidModel(format!("node is missing vector3 property '{name}'"))
    })
}

fn vector4_property(node: &CastNode, name: &str) -> Result<[f32; 4], MergeError> {
    optional_vector4_property(node, name).ok_or_else(|| {
        MergeError::InvalidModel(format!("node is missing vector4 property '{name}'"))
    })
}

fn optional_vector3_property(node: &CastNode, name: &str) -> Option<[f32; 3]> {
    match property(node, name) {
        Some(PropertyValues::Vector3(values)) => values.first().copied(),
        _ => None,
    }
}

fn optional_vector4_property(node: &CastNode, name: &str) -> Option<[f32; 4]> {
    match property(node, name) {
        Some(PropertyValues::Vector4(values)) => values.first().copied(),
        _ => None,
    }
}

fn vector3_values<'a>(node: &'a CastNode, name: &str) -> Result<&'a [[f32; 3]], MergeError> {
    match property(node, name) {
        Some(PropertyValues::Vector3(values)) => Ok(values),
        _ => Err(MergeError::InvalidModel(format!(
            "invalid vector3 buffer '{name}'"
        ))),
    }
}

fn prop(name: &str, values: PropertyValues) -> CastProperty {
    CastProperty {
        name: name.to_owned(),
        values,
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

fn model_bone_limit(mesh: &Mesh) -> usize {
    mesh.weight_bones
        .iter()
        .copied()
        .max()
        .map_or(0, |value| value as usize + 1)
}

fn pack_color(value: &[f32; 4]) -> u32 {
    ((value[0] * 255.0) as u32 & 0xff)
        | (((value[1] * 255.0) as u32 & 0xff) << 8)
        | (((value[2] * 255.0) as u32 & 0xff) << 16)
        | (((value[3] * 255.0) as u32 & 0xff) << 24)
}

fn fnv1a(value: &str) -> u64 {
    value
        .as_bytes()
        .iter()
        .fold(0xcbf29ce484222325, |hash, byte| {
            (hash ^ u64::from(*byte)).wrapping_mul(0x100000001b3)
        })
}
