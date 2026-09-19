use crate::math::{Quaternion, Vec3};
use crate::{MergeError, MergeObserver};

#[derive(Debug, Clone)]
pub(crate) struct Model {
    pub(crate) name: String,
    pub(crate) bones: Vec<Bone>,
    pub(crate) meshes: Vec<Mesh>,
    pub(crate) materials: Vec<String>,
    pub(crate) shapes: Vec<String>,
}

impl Model {
    pub(crate) fn has_bone(&self, name: &str) -> bool {
        self.bones.iter().any(|bone| bone.name == name)
    }

    pub(crate) fn generate_global_bones(&mut self) -> Result<(), MergeError> {
        for index in 0..self.bones.len() {
            let parent_index = self.bones[index].parent;
            if parent_index < 0 {
                self.bones[index].global_position = self.bones[index].local_position;
                self.bones[index].global_rotation = self.bones[index].local_rotation;
                continue;
            }

            let parent_index = usize::try_from(parent_index)
                .map_err(|_| MergeError::InvalidModel("invalid negative parent index".into()))?;
            if parent_index >= self.bones.len() {
                return Err(MergeError::InvalidModel(format!(
                    "bone '{}' references an invalid parent {parent_index}",
                    self.bones[index].name
                )));
            }
            let parent_position = self.bones[parent_index].global_position;
            let parent_rotation = self.bones[parent_index].global_rotation;
            let local_position = self.bones[index].local_position;
            let local_rotation = self.bones[index].local_rotation;
            self.bones[index].global_position =
                parent_position + parent_rotation.rotate(local_position);
            self.bones[index].global_rotation = parent_rotation.multiply(local_rotation);
        }
        Ok(())
    }
}

#[derive(Debug, Clone)]
pub(crate) struct Bone {
    pub(crate) name: String,
    pub(crate) parent: i32,
    pub(crate) local_position: Vec3,
    pub(crate) local_rotation: Quaternion,
    pub(crate) global_position: Vec3,
    pub(crate) global_rotation: Quaternion,
    pub(crate) scale: Vec3,
}

#[derive(Debug, Clone)]
pub(crate) struct Mesh {
    pub(crate) positions: Vec<Vec3>,
    pub(crate) normals: Vec<Vec3>,
    pub(crate) tangents: Vec<Vec3>,
    pub(crate) colors: Vec<[f32; 4]>,
    pub(crate) uvs: Vec<[f32; 2]>,
    pub(crate) faces: Vec<u32>,
    pub(crate) weights_per_vertex: usize,
    pub(crate) weight_bones: Vec<u32>,
    pub(crate) weight_values: Vec<f32>,
    pub(crate) material_indices: Vec<usize>,
    pub(crate) shape_deltas: Vec<ShapeDelta>,
}

#[derive(Debug, Clone)]
pub(crate) struct ShapeDelta {
    pub(crate) shape_index: usize,
    pub(crate) vertex_index: usize,
    pub(crate) delta: Vec3,
}

pub(crate) fn merge_model(
    root: &mut Model,
    mut model: Model,
    observer: &impl MergeObserver,
) -> Result<(), MergeError> {
    check_cancelled(observer)?;
    let mut bone_lookup =
        std::collections::HashMap::with_capacity(root.bones.len() + model.bones.len());
    for (index, bone) in root.bones.iter().enumerate() {
        bone_lookup.entry(bone.name.clone()).or_insert(index);
    }

    let mut source_to_root = Vec::with_capacity(model.bones.len());
    let mut added = Vec::with_capacity(model.bones.len());
    for bone in &model.bones {
        check_cancelled(observer)?;
        if let Some(&index) = bone_lookup.get(&bone.name) {
            source_to_root.push(index);
            added.push(false);
            continue;
        }
        let index = root.bones.len();
        root.bones.push(Bone {
            name: bone.name.clone(),
            parent: -1,
            local_position: bone.local_position,
            local_rotation: bone.local_rotation,
            global_position: bone.global_position,
            global_rotation: bone.global_rotation,
            scale: bone.scale,
        });
        bone_lookup.insert(bone.name.clone(), index);
        source_to_root.push(index);
        added.push(true);
    }

    for (source_index, source) in model.bones.iter().enumerate() {
        if !added[source_index] {
            continue;
        }
        let target_index = source_to_root[source_index];
        if source.parent >= 0 {
            let source_parent = usize::try_from(source.parent)
                .map_err(|_| MergeError::InvalidModel("invalid bone parent".into()))?;
            let target_parent = *source_to_root.get(source_parent).ok_or_else(|| {
                MergeError::InvalidModel(format!("bone '{}' has an invalid parent", source.name))
            })?;
            root.bones[target_index].parent = i32::try_from(target_parent)
                .map_err(|_| MergeError::InvalidModel("too many bones".into()))?;
        }
    }

    let mut shape_remap = Vec::with_capacity(model.shapes.len());
    for shape in model.shapes.drain(..) {
        let index = root
            .shapes
            .iter()
            .position(|current| current == &shape)
            .unwrap_or_else(|| {
                root.shapes.push(shape);
                root.shapes.len() - 1
            });
        shape_remap.push(index);
    }

    root.generate_global_bones()?;
    model.generate_global_bones()?;
    let (translation, rotation) = if let Some(source_root) = model.bones.first() {
        let target_root = &root.bones[source_to_root[0]];
        (
            target_root.global_position - source_root.global_position,
            target_root
                .global_rotation
                .multiply(source_root.global_rotation.inverse()),
        )
    } else {
        (Vec3::default(), Quaternion::IDENTITY)
    };

    let mut material_remap = Vec::with_capacity(model.materials.len());
    for material in model.materials.drain(..) {
        let index = root
            .materials
            .iter()
            .position(|current| current == &material)
            .unwrap_or_else(|| {
                root.materials.push(material);
                root.materials.len() - 1
            });
        material_remap.push(index);
    }

    for mut mesh in model.meshes.drain(..) {
        check_cancelled(observer)?;
        for position in &mut mesh.positions {
            *position = rotation.rotate(*position) + translation;
        }
        for normal in &mut mesh.normals {
            *normal = rotation.rotate(*normal);
        }
        for tangent in &mut mesh.tangents {
            *tangent = rotation.rotate(*tangent);
        }
        for bone_index in &mut mesh.weight_bones {
            let source_index = usize::try_from(*bone_index)
                .map_err(|_| MergeError::InvalidModel("invalid weight bone index".into()))?;
            *bone_index = u32::try_from(*source_to_root.get(source_index).ok_or_else(|| {
                MergeError::InvalidModel("weight references a missing bone".into())
            })?)
            .map_err(|_| MergeError::InvalidModel("too many bones".into()))?;
        }
        for material in &mut mesh.material_indices {
            *material = *material_remap.get(*material).ok_or_else(|| {
                MergeError::InvalidModel("mesh references a missing material".into())
            })?;
        }
        for delta in &mut mesh.shape_deltas {
            delta.shape_index = *shape_remap.get(delta.shape_index).ok_or_else(|| {
                MergeError::InvalidModel("mesh references a missing shape".into())
            })?;
            delta.delta = rotation.rotate(delta.delta);
        }
        root.meshes.push(mesh);
    }
    Ok(())
}

pub(crate) fn check_cancelled(observer: &impl MergeObserver) -> Result<(), MergeError> {
    if observer.is_cancelled() {
        Err(MergeError::Cancelled)
    } else {
        Ok(())
    }
}
