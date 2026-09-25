use crate::cast_model;
use crate::domain::{MaterialInfo, MaterialSlotValue};
use crate::math::Vec3;
use std::collections::{HashMap, HashSet};
use std::fmt;
use std::fs::File;
use std::io::Read;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, PartialEq)]
pub struct PreviewData {
    pub file_path: PathBuf,
    pub model_name: String,
    pub source_mesh_count: usize,
    pub source_vertex_count: usize,
    pub source_triangle_count: usize,
    pub displayed_triangle_count: usize,
    pub is_simplified: bool,
    pub bounds: PreviewBounds,
    pub meshes: Vec<PreviewMesh>,
    pub materials: Vec<PreviewMaterial>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct PreviewMaterial {
    pub name: String,
    pub profile: PreviewMaterialProfile,
    pub albedo: Option<PathBuf>,
    pub nog: Option<PathBuf>,
    pub opacity: Option<PathBuf>,
    pub base_color: Option<[f32; 4]>,
}

/// Render profiles mirroring the ez4cywa COD shader research
/// (`scripts/profiles.json` plus the optic glass master group).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PreviewMaterialProfile {
    Generic,
    Weapon,
    Glass,
    Skin,
    HairCard,
    Eye,
    Cornea,
    Tearline,
    Oral,
    Cloth,
    Overlay,
}

/// Techset and material-name rules transcribed from the shader project's
/// `profiles.json` (they are content hashes and match across exports of the
/// same game materials).
const SKIN_TECHSETS: &[&str] = &["techset_7c5b347b60d04806"];
const HAIR_TECHSETS: &[&str] = &["techset_3aab5a7986d884a7", "techset_638b48a267927efa"];
const OVERLAY_TECHSETS: &[&str] = &["techset_3aa45554dfc644e0"];
const EYE_MATERIALS: &[&str] = &["material_5b519c79a23de53d"];
const CORNEA_MATERIALS: &[&str] = &["material_3c5852033a98fa18"];
const TEARLINE_MATERIALS: &[&str] = &["material_3e715c391e870b93"];
const ORAL_MATERIALS: &[&str] = &["material_6aa474a8a6fc0d71"];

impl PreviewMaterialProfile {
    /// Classifies a material by name and asset (file stem), following the
    /// research project's first-match-wins rule order.
    pub fn classify(name: &str, asset: &str) -> Self {
        Self::classify_with(name, asset, None, &[])
    }

    /// Full rule set: techsets from `_mat_info` tables and resolved texture
    /// paths (a "lens"/"glass" texture marks optic glass) refine the
    /// name-based heuristics for hashed material names.
    pub fn classify_with(
        name: &str,
        asset: &str,
        techset: Option<&str>,
        texture_paths: &[&str],
    ) -> Self {
        let lowered = name.to_ascii_lowercase();
        let asset_lowered = asset.to_ascii_lowercase();
        // Glass must win over the weapon rule: optic lenses inside weapon
        // assets render as thin-wall glass, not metal. Lens/glass hints in
        // texture names identify hashed lens materials.
        let texture_hint_glass = texture_paths.iter().any(|path| {
            let lowered_path = path.to_ascii_lowercase();
            lowered_path.contains("glass") || lowered_path.contains("lens")
        });
        if lowered.contains("glass") || lowered.contains("lens") || texture_hint_glass {
            return Self::Glass;
        }
        let weapon_name = ["wpn_", "_vm_", "_attachment", "attachment_"]
            .iter()
            .any(|hint| lowered.contains(hint));
        let weapon_asset = ["wpn_", "vm_", "attachment"]
            .iter()
            .any(|hint| asset_lowered.contains(hint));
        if weapon_name || weapon_asset {
            return Self::Weapon;
        }
        if let Some(techset) = techset {
            if SKIN_TECHSETS.contains(&techset) {
                return Self::Skin;
            }
            if HAIR_TECHSETS.contains(&techset) {
                return Self::HairCard;
            }
            if OVERLAY_TECHSETS.contains(&techset) {
                return Self::Overlay;
            }
        }
        if EYE_MATERIALS.iter().any(|hint| lowered.contains(hint)) {
            return Self::Eye;
        }
        if CORNEA_MATERIALS.iter().any(|hint| lowered.contains(hint)) {
            return Self::Cornea;
        }
        if TEARLINE_MATERIALS.iter().any(|hint| lowered.contains(hint)) {
            return Self::Tearline;
        }
        if ORAL_MATERIALS.iter().any(|hint| lowered.contains(hint)) {
            return Self::Oral;
        }
        // Character sub-profile name hints (generalized from the research
        // asset classifications in profiles.json).
        if lowered.contains("skin") {
            return Self::Skin;
        }
        if lowered.contains("hair") {
            return Self::HairCard;
        }
        if lowered.contains("cornea") {
            return Self::Cornea;
        }
        if lowered.contains("tear") {
            return Self::Tearline;
        }
        if lowered.contains("eye") || lowered.contains("iris") {
            return Self::Eye;
        }
        if lowered.contains("oral") || lowered.contains("teeth") || lowered.contains("gum") {
            return Self::Oral;
        }
        if lowered.contains("cloth")
            || ["_body_mp_", "_head_mp_"]
                .iter()
                .any(|hint| asset_lowered.contains(hint))
        {
            return Self::Cloth;
        }
        Self::Generic
    }
}

/// Parsed `_mat_info/<material>.txt` sidecar: techset plus the
/// `unk_semantic_XX,image_stem` table (47 = color, 48 = packed NOG,
/// 4a = opacity coverage; `$`-prefixed stems are programmatic sentinels).
struct MatInfo {
    techset: Option<String>,
    semantics: Vec<(String, String)>,
}

fn load_mat_info(cast_dir: &Path, material_name: &str) -> Option<MatInfo> {
    let path = cast_dir
        .join("_mat_info")
        .join(format!("{}.txt", material_name.replace('/', "_")));
    let text = std::fs::read_to_string(path).ok()?;
    let mut techset = None;
    let mut semantics = Vec::new();
    for line in text.lines() {
        let line = line.trim();
        if let Some(value) = line.strip_prefix("Techset:") {
            techset = Some(value.trim().to_owned());
        } else if let Some(value) = line.strip_prefix("unk_semantic_")
            && let Some((semantic, stem)) = value.split_once(',')
        {
            semantics.push((semantic.trim().to_owned(), stem.trim().to_owned()));
        }
    }
    Some(MatInfo { techset, semantics })
}

fn semantic_stem<'a>(table: &'a MatInfo, semantic: &str) -> Option<&'a str> {
    table
        .semantics
        .iter()
        .find(|(key, _)| key == semantic)
        .map(|(_, stem)| stem.as_str())
        .filter(|stem| !stem.starts_with('$'))
}

/// Finds the texture for a semantic stem: matching cast slot file first
/// (cross-validation, as in the shader project's cast_spec.py), then the
/// exported `_images/<stem>.png` fallback.
fn resolve_semantic_texture(
    table: &MatInfo,
    semantic: &str,
    slots: &[(String, MaterialSlotValue)],
    cast_dir: &Path,
) -> Option<PathBuf> {
    let stem = semantic_stem(table, semantic)?;
    for (_, value) in slots {
        if let Some(path) = value.file_path()
            && Path::new(path)
                .file_stem()
                .is_some_and(|candidate| candidate == stem)
        {
            return Some(PathBuf::from(path));
        }
    }
    let fallback = cast_dir.join("_images").join(format!("{stem}.png"));
    fallback.is_file().then_some(fallback)
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PreviewBounds {
    pub minimum: [f32; 3],
    pub maximum: [f32; 3],
}

#[derive(Debug, Clone, PartialEq)]
pub struct PreviewMesh {
    pub positions: Vec<[f32; 3]>,
    pub normals: Vec<[f32; 3]>,
    pub uvs: Vec<[f32; 2]>,
    pub material_index: Option<usize>,
    pub triangle_indices: Vec<u32>,
}

#[derive(Debug)]
pub enum PreviewError {
    InvalidTriangleLimit,
    InvalidPath(PathBuf),
    MissingFile(PathBuf),
    UnsupportedFormat(PathBuf),
    Io {
        path: PathBuf,
        source: std::io::Error,
    },
    ModelRead {
        path: PathBuf,
        message: String,
    },
    NoGeometry(PathBuf),
    Cancelled,
}

impl fmt::Display for PreviewError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::InvalidTriangleLimit => formatter.write_str("triangle limit must be positive"),
            Self::InvalidPath(path) => write!(formatter, "{} is not a valid path", path.display()),
            Self::MissingFile(path) => write!(formatter, "{} does not exist", path.display()),
            Self::UnsupportedFormat(path) => {
                write!(formatter, "{} is not a Cast file", path.display())
            }
            Self::Io { path, source } => write!(formatter, "{}: {source}", path.display()),
            Self::ModelRead { path, message } => write!(formatter, "{}: {message}", path.display()),
            Self::NoGeometry(path) => {
                write!(
                    formatter,
                    "{} does not contain previewable geometry",
                    path.display()
                )
            }
            Self::Cancelled => formatter.write_str("preview was cancelled"),
        }
    }
}

impl std::error::Error for PreviewError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Io { source, .. } => Some(source),
            _ => None,
        }
    }
}

pub fn load_preview(
    path: &Path,
    triangle_limit: usize,
    is_cancelled: impl Fn() -> bool,
) -> Result<PreviewData, PreviewError> {
    if triangle_limit == 0 {
        return Err(PreviewError::InvalidTriangleLimit);
    }
    check_cancelled(&is_cancelled)?;
    if path.as_os_str().is_empty() {
        return Err(PreviewError::InvalidPath(path.to_path_buf()));
    }
    if !path.is_file() {
        return Err(PreviewError::MissingFile(path.to_path_buf()));
    }
    if path
        .extension()
        .and_then(|value| value.to_str())
        .is_none_or(|value| !value.eq_ignore_ascii_case("cast"))
    {
        return Err(PreviewError::UnsupportedFormat(path.to_path_buf()));
    }
    let bytes = read_file(path, &is_cancelled)?;
    let model =
        cast_model::decode_model_with_cancel(&bytes, path, &is_cancelled).map_err(|error| {
            match error {
                crate::MergeError::Cancelled => PreviewError::Cancelled,
                error => PreviewError::ModelRead {
                    path: path.to_path_buf(),
                    message: error.to_string(),
                },
            }
        })?;
    check_cancelled(&is_cancelled)?;

    let source_mesh_count = model.meshes.len();
    let source_vertex_count = model.meshes.iter().map(|mesh| mesh.positions.len()).sum();
    let source_triangle_count = model.meshes.iter().map(|mesh| mesh.faces.len() / 3).sum();
    let valid_triangle_counts = model
        .meshes
        .iter()
        .map(|mesh| count_valid_triangles(mesh, &is_cancelled))
        .collect::<Result<Vec<_>, _>>()?;
    let valid_triangle_count = valid_triangle_counts.iter().sum::<usize>();
    if valid_triangle_count == 0 {
        return Err(PreviewError::NoGeometry(path.to_path_buf()));
    }
    let quotas = allocate_triangle_quotas(
        &valid_triangle_counts,
        triangle_limit.min(valid_triangle_count),
    );
    let mut meshes = Vec::new();
    for ((mesh, valid_triangle_count), quota) in
        model.meshes.iter().zip(valid_triangle_counts).zip(quotas)
    {
        check_cancelled(&is_cancelled)?;
        if quota == 0 {
            continue;
        }

        let material_index = mesh.material_indices.first().copied();
        let selected_ordinals = select_triangle_ordinals(valid_triangle_count, quota);
        let mut vertex_map = HashMap::new();
        let mut positions = Vec::new();
        let mut normals = Vec::new();
        let mut uvs = Vec::new();
        let mut triangle_indices = Vec::new();
        let mut valid_ordinal = 0;
        for face in mesh.faces.chunks_exact(3) {
            check_cancelled(&is_cancelled)?;
            let Some(source_indices) = valid_triangle(mesh, face) else {
                continue;
            };
            if selected_ordinals.contains(&valid_ordinal) {
                for source_index in source_indices {
                    let preview_index = if let Some(index) = vertex_map.get(&source_index) {
                        *index
                    } else {
                        let preview_index = u32::try_from(positions.len())
                            .expect("preview vertex count should fit in u32");
                        let position = mesh.positions[source_index];
                        let normal = mesh.normals[source_index];
                        positions.push(position.into());
                        normals.push(if finite(normal) {
                            normal.into()
                        } else {
                            [0.0; 3]
                        });
                        uvs.push(mesh.uvs.get(source_index).copied().unwrap_or([0.0, 0.0]));
                        vertex_map.insert(source_index, preview_index);
                        preview_index
                    };
                    triangle_indices.push(preview_index);
                }
            }
            valid_ordinal += 1;
        }
        if !triangle_indices.is_empty() {
            meshes.push(PreviewMesh {
                positions,
                normals,
                uvs,
                material_index,
                triangle_indices,
            });
        }
    }

    if meshes.is_empty() {
        return Err(PreviewError::NoGeometry(path.to_path_buf()));
    }
    let displayed_triangle_count = meshes
        .iter()
        .map(|mesh| mesh.triangle_indices.len() / 3)
        .sum();
    let bounds = calculate_bounds(&meshes);
    let cast_dir = path.parent().unwrap_or(Path::new(""));
    let materials = model
        .materials
        .iter()
        .map(|material| preview_material(material, &model.name, cast_dir))
        .collect();
    Ok(PreviewData {
        file_path: path.to_path_buf(),
        model_name: model.name,
        source_mesh_count,
        source_vertex_count,
        source_triangle_count,
        displayed_triangle_count,
        is_simplified: displayed_triangle_count < source_triangle_count,
        bounds,
        meshes,
        materials,
    })
}

/// Resolves material textures and profile, mirroring the shader project's
/// cast_spec.py: `_mat_info` semantics first (47 = color, 48 = packed NOG,
/// 4a = opacity), named cast slots second (albedo/diffuse/basecolor,
/// normal/nog, opacity).
fn preview_material(material: &MaterialInfo, asset: &str, cast_dir: &Path) -> PreviewMaterial {
    let table = load_mat_info(cast_dir, &material.name);
    let file_slot = |names: &[&str]| {
        material
            .slots
            .iter()
            .find(|(slot, value)| {
                value.file_path().is_some()
                    && names.iter().any(|name| slot.eq_ignore_ascii_case(name))
            })
            .and_then(|(_, value)| value.file_path())
            .map(PathBuf::from)
    };
    let color_slot = |names: &[&str]| {
        material
            .slots
            .iter()
            .find(|(slot, value)| {
                matches!(value, MaterialSlotValue::Color(_))
                    && names.iter().any(|name| slot.eq_ignore_ascii_case(name))
            })
            .and_then(|(_, value)| match value {
                MaterialSlotValue::Color(rgba) => Some(*rgba),
                MaterialSlotValue::File(_) => None,
            })
    };
    let semantic = |semantic: &str| {
        table
            .as_ref()
            .and_then(|table| resolve_semantic_texture(table, semantic, &material.slots, cast_dir))
    };
    let albedo_names = ["albedo", "diffuse", "basecolor"];
    let albedo = semantic("47").or_else(|| file_slot(&albedo_names));
    let base_color = if albedo.is_none() {
        color_slot(&albedo_names)
    } else {
        None
    };
    let nog = semantic("48").or_else(|| file_slot(&["normal", "nog"]));
    let opacity = semantic("4a").or_else(|| file_slot(&["opacity"]));
    let texture_hints: Vec<&str> = material
        .slots
        .iter()
        .filter_map(|(_, value)| value.file_path())
        .collect();
    PreviewMaterial {
        name: material.name.clone(),
        profile: PreviewMaterialProfile::classify_with(
            &material.name,
            asset,
            table.as_ref().and_then(|table| table.techset.as_deref()),
            &texture_hints,
        ),
        albedo,
        nog,
        opacity,
        base_color,
    }
}

fn read_file(path: &Path, is_cancelled: &impl Fn() -> bool) -> Result<Vec<u8>, PreviewError> {
    let mut file = File::open(path).map_err(|source| PreviewError::Io {
        path: path.to_path_buf(),
        source,
    })?;
    let capacity = file
        .metadata()
        .ok()
        .and_then(|metadata| usize::try_from(metadata.len()).ok())
        .unwrap_or(0);
    let mut bytes = Vec::with_capacity(capacity);
    let mut buffer = [0; 64 * 1024];
    loop {
        check_cancelled(is_cancelled)?;
        let count = file.read(&mut buffer).map_err(|source| PreviewError::Io {
            path: path.to_path_buf(),
            source,
        })?;
        if count == 0 {
            return Ok(bytes);
        }
        bytes.extend_from_slice(&buffer[..count]);
    }
}

fn valid_triangle(mesh: &crate::domain::Mesh, face: &[u32]) -> Option<[usize; 3]> {
    let first = usize::try_from(face[0]).ok()?;
    let second = usize::try_from(face[1]).ok()?;
    let third = usize::try_from(face[2]).ok()?;
    [first, second, third]
        .iter()
        .all(|index| {
            mesh.positions
                .get(*index)
                .is_some_and(|position| finite(*position))
        })
        .then_some([first, second, third])
}

fn count_valid_triangles(
    mesh: &crate::domain::Mesh,
    is_cancelled: &impl Fn() -> bool,
) -> Result<usize, PreviewError> {
    let mut count = 0;
    for face in mesh.faces.chunks_exact(3) {
        check_cancelled(is_cancelled)?;
        if valid_triangle(mesh, face).is_some() {
            count += 1;
        }
    }
    Ok(count)
}

fn allocate_triangle_quotas(triangle_counts: &[usize], budget: usize) -> Vec<usize> {
    let total = triangle_counts.iter().sum::<usize>();
    if budget >= total {
        return triangle_counts.to_vec();
    }

    let mut quotas = vec![0; triangle_counts.len()];
    let mut remainders = Vec::with_capacity(triangle_counts.len());
    let mut allocated = 0;
    for (index, count) in triangle_counts.iter().copied().enumerate() {
        let exact = budget as f64 * count as f64 / total as f64;
        quotas[index] = exact.floor() as usize;
        allocated += quotas[index];
        remainders.push((index, exact - quotas[index] as f64));
    }
    remainders.sort_by(|left, right| {
        right
            .1
            .total_cmp(&left.1)
            .then_with(|| left.0.cmp(&right.0))
    });
    for (index, _) in remainders.into_iter().take(budget - allocated) {
        quotas[index] += 1;
    }
    quotas
}

fn select_triangle_ordinals(source_triangle_count: usize, quota: usize) -> HashSet<usize> {
    (0..quota)
        .map(|index| index * source_triangle_count / quota)
        .collect()
}

fn finite(value: Vec3) -> bool {
    value.0.is_finite() && value.1.is_finite() && value.2.is_finite()
}

fn calculate_bounds(meshes: &[PreviewMesh]) -> PreviewBounds {
    let mut minimum = [f32::INFINITY; 3];
    let mut maximum = [f32::NEG_INFINITY; 3];
    for point in meshes.iter().flat_map(|mesh| &mesh.positions) {
        for axis in 0..3 {
            minimum[axis] = minimum[axis].min(point[axis]);
            maximum[axis] = maximum[axis].max(point[axis]);
        }
    }
    PreviewBounds { minimum, maximum }
}

fn check_cancelled(is_cancelled: &impl Fn() -> bool) -> Result<(), PreviewError> {
    if is_cancelled() {
        Err(PreviewError::Cancelled)
    } else {
        Ok(())
    }
}
