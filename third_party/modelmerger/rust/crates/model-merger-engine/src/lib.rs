pub mod ammunition;
mod cast_model;
mod domain;
mod math;
mod output;
mod preview;

pub use preview::{PreviewBounds, PreviewData, PreviewError, PreviewMesh, load_preview};

use cast_codec::{CastFile, CodecError};
use domain::{Model, check_cancelled, merge_model};
use std::fmt;
use std::fs;
use std::path::{Path, PathBuf};

pub const MINIMUM_PARTS: usize = 2;
pub const MAXIMUM_PARTS: usize = 15;

#[derive(Debug, Clone)]
pub struct MergeRequest {
    pub input_files: Vec<PathBuf>,
    pub output_directory: PathBuf,
    pub output_file_name: Option<String>,
    pub root_selection: RootSelection,
    pub overwrite: bool,
}

#[derive(Debug, Clone)]
pub enum RootSelection {
    Automatic,
    Manual(PathBuf),
}

#[derive(Debug, Clone, PartialEq)]
pub struct MergeResult {
    pub output_path: PathBuf,
    pub root_model_name: String,
    pub part_count: usize,
    pub bone_count: usize,
    pub mesh_count: usize,
    pub warnings: Vec<MergeWarning>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MergeWarningCode {
    NoAttachmentBone,
    UnconnectedHierarchy,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MergeValidationCode {
    InvalidPartCount,
    InvalidPath,
    MissingFile,
    UnsupportedExtension,
    DuplicateFile,
    InvalidOutputDirectory,
    InvalidOutputFileName,
    OutputAlreadyExists,
    ManualRootNotSelected,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MergeWarning {
    pub code: MergeWarningCode,
    pub model_name: String,
    pub root_model_name: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MergeStage {
    Validating,
    Loading,
    SelectingRoot,
    Merging,
    Saving,
    Verifying,
    Completed,
}

#[derive(Debug)]
pub enum MergeError {
    Validation {
        code: MergeValidationCode,
        message: String,
        path: Option<PathBuf>,
    },
    Io {
        path: PathBuf,
        source: std::io::Error,
    },
    Codec(CodecError),
    ModelRead {
        path: PathBuf,
        message: String,
    },
    InvalidModel(String),
    Cancelled,
}

impl fmt::Display for MergeError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Validation { message, .. } | Self::InvalidModel(message) => {
                formatter.write_str(message)
            }
            Self::Io { path, source } => write!(formatter, "{}: {source}", path.display()),
            Self::Codec(source) => write!(formatter, "Cast codec error: {source}"),
            Self::ModelRead { path, message } => write!(formatter, "{}: {message}", path.display()),
            Self::Cancelled => formatter.write_str("merge was cancelled"),
        }
    }
}

impl std::error::Error for MergeError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Io { source, .. } => Some(source),
            Self::Codec(source) => Some(source),
            _ => None,
        }
    }
}

pub trait MergeObserver {
    fn is_cancelled(&self) -> bool {
        false
    }

    fn on_progress(&self, _stage: MergeStage, _current: usize, _total: usize, _item: Option<&str>) {
    }
}

pub struct NoopObserver;

impl MergeObserver for NoopObserver {}

struct LoadedPart {
    path: PathBuf,
    model: Option<Model>,
}

pub struct PreparedMerge {
    loaded: Vec<LoadedPart>,
    root_index: usize,
    output_path: PathBuf,
    overwrite: bool,
}

impl PreparedMerge {
    pub fn output_path(&self) -> &Path {
        &self.output_path
    }

    /// Alchemy Stars integration: defer overwrite authorization until the host
    /// has claimed the resolved path and obtained confirmation after preparation.
    pub fn with_overwrite(mut self, overwrite: bool) -> Self {
        self.overwrite = overwrite;
        self
    }

    pub fn execute(mut self, observer: &impl MergeObserver) -> Result<MergeResult, MergeError> {
        check_cancelled(observer)?;
        if self.output_path.exists() && !self.overwrite {
            return Err(validation(
                MergeValidationCode::OutputAlreadyExists,
                format!("output already exists: {}", self.output_path.display()),
                Some(self.output_path.clone()),
            ));
        }

        let part_count = self.loaded.len();
        let connection_data = connection_data(&self.loaded);
        let mut root = self.loaded[self.root_index]
            .model
            .take()
            .expect("prepared root should contain a model");
        let root_name = root.name.clone();
        let mut merged = vec![false; part_count];
        merged[self.root_index] = true;
        let mut merged_count = 1;
        let merge_total = part_count - 1;
        let mut warnings = Vec::new();

        while merged_count < part_count {
            check_cancelled(observer)?;
            let mut progressed = false;
            let candidates: Vec<usize> = merged
                .iter()
                .enumerate()
                .filter_map(|(index, is_merged)| (!is_merged).then_some(index))
                .collect();
            for index in candidates {
                check_cancelled(observer)?;
                let model = self.loaded[index]
                    .model
                    .as_ref()
                    .expect("unmerged part should contain a model");
                let missing_root_bone = model
                    .bones
                    .first()
                    .is_some_and(|bone| !root.has_bone(&bone.name));
                if missing_root_bone && can_be_connected(index, &connection_data) {
                    continue;
                }
                if missing_root_bone {
                    warnings.push(MergeWarning {
                        code: MergeWarningCode::NoAttachmentBone,
                        model_name: model.name.clone(),
                        root_model_name: root.name.clone(),
                    });
                }
                observer.on_progress(
                    MergeStage::Merging,
                    merged_count - 1,
                    merge_total,
                    Some(&model.name),
                );
                let model = self.loaded[index]
                    .model
                    .take()
                    .expect("unmerged part should contain a model");
                merge_model(&mut root, model, observer)?;
                merged[index] = true;
                merged_count += 1;
                progressed = true;
            }

            if progressed {
                continue;
            }
            let index = merged
                .iter()
                .position(|is_merged| !is_merged)
                .expect("at least one unmerged model should remain");
            let model = self.loaded[index]
                .model
                .take()
                .expect("unmerged part should contain a model");
            warnings.push(MergeWarning {
                code: MergeWarningCode::UnconnectedHierarchy,
                model_name: model.name.clone(),
                root_model_name: root.name.clone(),
            });
            observer.on_progress(
                MergeStage::Merging,
                merged_count - 1,
                merge_total,
                Some(&model.name),
            );
            merge_model(&mut root, model, observer)?;
            merged[index] = true;
            merged_count += 1;
        }

        check_cancelled(observer)?;
        root.generate_global_bones()?;
        let output_name = self
            .output_path
            .file_name()
            .and_then(|value| value.to_str())
            .unwrap_or("merged.cast")
            .to_owned();
        observer.on_progress(MergeStage::Saving, 0, 1, Some(&output_name));
        let bytes = cast_model::encode_model(&root)?;
        let temporary_path = temporary_path(&self.output_path);
        let temporary = TemporaryOutput::new(temporary_path.clone());
        fs::write(&temporary_path, &bytes).map_err(|source| MergeError::Io {
            path: temporary_path.clone(),
            source,
        })?;
        check_cancelled(observer)?;

        observer.on_progress(MergeStage::Verifying, 0, 1, None);
        let verification_bytes = fs::read(&temporary_path).map_err(|source| MergeError::Io {
            path: temporary_path.clone(),
            source,
        })?;
        let verification = CastFile::decode(&verification_bytes).map_err(MergeError::Codec)?;
        let contains_model = verification.roots.iter().any(|root| {
            root.children
                .iter()
                .any(|child| child.identifier == u32::from_le_bytes(*b"modl"))
        });
        if !contains_model {
            return Err(MergeError::InvalidModel(
                "saved Cast file does not contain a model".into(),
            ));
        }
        check_cancelled(observer)?;

        commit_output(&temporary_path, &self.output_path, self.overwrite)?;
        temporary.disarm();
        observer.on_progress(MergeStage::Completed, 1, 1, Some(&output_name));

        Ok(MergeResult {
            output_path: self.output_path,
            root_model_name: root_name,
            part_count,
            bone_count: root.bones.len(),
            mesh_count: root.meshes.len(),
            warnings,
        })
    }
}

pub fn prepare(
    request: MergeRequest,
    observer: &impl MergeObserver,
) -> Result<PreparedMerge, MergeError> {
    observer.on_progress(MergeStage::Validating, 0, 1, None);
    check_cancelled(observer)?;
    let validated = validate(request)?;
    fs::create_dir_all(&validated.output_directory).map_err(|source| MergeError::Io {
        path: validated.output_directory.clone(),
        source,
    })?;

    let mut loaded = Vec::with_capacity(validated.input_files.len());
    for (index, path) in validated.input_files.iter().enumerate() {
        check_cancelled(observer)?;
        observer.on_progress(
            MergeStage::Loading,
            index,
            validated.input_files.len(),
            path.file_name().and_then(|value| value.to_str()),
        );
        let bytes = fs::read(path).map_err(|source| MergeError::Io {
            path: path.clone(),
            source,
        })?;
        let model =
            cast_model::decode_model(&bytes, path).map_err(|error| MergeError::ModelRead {
                path: path.clone(),
                message: error.to_string(),
            })?;
        loaded.push(LoadedPart {
            path: path.clone(),
            model: Some(model),
        });
    }

    observer.on_progress(MergeStage::SelectingRoot, 0, 1, None);
    let root_index = match validated.root_selection {
        RootSelection::Automatic => select_root(&loaded),
        RootSelection::Manual(path) => loaded
            .iter()
            .position(|part| paths_equal(&part.path, &path))
            .ok_or_else(|| {
                validation(
                    MergeValidationCode::ManualRootNotSelected,
                    "manual root is not a selected part",
                    Some(path),
                )
            })?,
    };
    let root_name = &loaded[root_index]
        .model
        .as_ref()
        .expect("loaded part should contain a model")
        .name;
    let output_name = validated
        .output_file_name
        .unwrap_or_else(|| format!("{root_name}.cast"));
    let output_path = validated.output_directory.join(output_name);
    if output_path.exists() && !validated.overwrite {
        return Err(validation(
            MergeValidationCode::OutputAlreadyExists,
            format!("output already exists: {}", output_path.display()),
            Some(output_path),
        ));
    }

    Ok(PreparedMerge {
        loaded,
        root_index,
        output_path,
        overwrite: validated.overwrite,
    })
}

struct ValidatedRequest {
    input_files: Vec<PathBuf>,
    output_directory: PathBuf,
    output_file_name: Option<String>,
    root_selection: RootSelection,
    overwrite: bool,
}

fn validate(request: MergeRequest) -> Result<ValidatedRequest, MergeError> {
    if !(MINIMUM_PARTS..=MAXIMUM_PARTS).contains(&request.input_files.len()) {
        return Err(validation(
            MergeValidationCode::InvalidPartCount,
            format!("a merge requires {MINIMUM_PARTS} to {MAXIMUM_PARTS} model parts"),
            None,
        ));
    }
    if request.output_directory.as_os_str().is_empty() {
        return Err(validation(
            MergeValidationCode::InvalidOutputDirectory,
            "choose a valid output folder",
            None,
        ));
    }
    let output_directory = absolute_path(&request.output_directory)?;
    let mut input_files = Vec::with_capacity(request.input_files.len());
    let mut seen = std::collections::HashSet::new();
    for input in request.input_files {
        if input.as_os_str().is_empty() {
            return Err(validation(
                MergeValidationCode::InvalidPath,
                "a Cast part path is invalid",
                Some(input),
            ));
        }
        let path = absolute_path(&input)?;
        if path
            .extension()
            .and_then(|value| value.to_str())
            .map(str::to_ascii_lowercase)
            != Some("cast".into())
        {
            return Err(validation(
                MergeValidationCode::UnsupportedExtension,
                format!("unsupported model format: {}", path.display()),
                Some(path),
            ));
        }
        if !path.is_file() {
            return Err(validation(
                MergeValidationCode::MissingFile,
                format!("Cast part does not exist: {}", path.display()),
                Some(path),
            ));
        }
        let key = path.to_string_lossy().to_ascii_lowercase();
        if !seen.insert(key) {
            return Err(validation(
                MergeValidationCode::DuplicateFile,
                format!("same Cast part selected more than once: {}", path.display()),
                Some(path),
            ));
        }
        input_files.push(path);
    }
    input_files.sort_by_key(|path| path.to_string_lossy().to_ascii_lowercase());

    let output_file_name = validate_output_name(request.output_file_name)?;
    let root_selection = match request.root_selection {
        RootSelection::Automatic => RootSelection::Automatic,
        RootSelection::Manual(path) => {
            let path = absolute_path(&path)?;
            if !input_files.iter().any(|input| paths_equal(input, &path)) {
                return Err(validation(
                    MergeValidationCode::ManualRootNotSelected,
                    "manual root is not a selected part",
                    Some(path),
                ));
            }
            RootSelection::Manual(path)
        }
    };
    Ok(ValidatedRequest {
        input_files,
        output_directory,
        output_file_name,
        root_selection,
        overwrite: request.overwrite,
    })
}

fn validate_output_name(name: Option<String>) -> Result<Option<String>, MergeError> {
    let Some(name) = name else { return Ok(None) };
    let name = name.trim();
    if name.is_empty() {
        return Ok(None);
    }
    let path = Path::new(name);
    if path.file_name().and_then(|value| value.to_str()) != Some(name)
        || name
            .chars()
            .any(|value| value < ' ' || r#"<>:"/\|?*"#.contains(value))
    {
        return Err(validation(
            MergeValidationCode::InvalidOutputFileName,
            "output file name must not contain a path",
            None,
        ));
    }
    match path.extension().and_then(|value| value.to_str()) {
        None => Ok(Some(format!("{name}.cast"))),
        Some(extension) if extension.eq_ignore_ascii_case("cast") => Ok(Some(name.to_owned())),
        Some(_) => Err(validation(
            MergeValidationCode::InvalidOutputFileName,
            "output file must use the .cast extension",
            None,
        )),
    }
}

fn validation(
    code: MergeValidationCode,
    message: impl Into<String>,
    path: Option<PathBuf>,
) -> MergeError {
    MergeError::Validation {
        code,
        message: message.into(),
        path,
    }
}

fn absolute_path(path: &Path) -> Result<PathBuf, MergeError> {
    if path.is_absolute() {
        Ok(path.to_path_buf())
    } else {
        std::env::current_dir()
            .map(|directory| directory.join(path))
            .map_err(|source| MergeError::Io {
                path: path.to_path_buf(),
                source,
            })
    }
}

fn paths_equal(left: &Path, right: &Path) -> bool {
    left.to_string_lossy()
        .eq_ignore_ascii_case(&right.to_string_lossy())
}

fn connection_data(parts: &[LoadedPart]) -> Vec<(Option<String>, Vec<String>)> {
    parts
        .iter()
        .map(|part| {
            let model = part
                .model
                .as_ref()
                .expect("loaded part should contain a model");
            (
                model.bones.first().map(|bone| bone.name.clone()),
                model.bones.iter().map(|bone| bone.name.clone()).collect(),
            )
        })
        .collect()
}

fn select_root(parts: &[LoadedPart]) -> usize {
    let connections = connection_data(parts);
    parts
        .iter()
        .enumerate()
        .find_map(|(index, part)| {
            let has_bones = !part
                .model
                .as_ref()
                .expect("loaded part should contain a model")
                .bones
                .is_empty();
            (has_bones && !can_be_connected(index, &connections)).then_some(index)
        })
        .unwrap_or(0)
}

fn can_be_connected(index: usize, models: &[(Option<String>, Vec<String>)]) -> bool {
    let Some(root_bone) = models[index].0.as_ref() else {
        return false;
    };
    models
        .iter()
        .enumerate()
        .any(|(other_index, (_, bones))| other_index != index && bones.contains(root_bone))
}

fn temporary_path(output: &Path) -> PathBuf {
    use std::sync::atomic::{AtomicU64, Ordering};
    static NEXT_ID: AtomicU64 = AtomicU64::new(0);
    let stem = output
        .file_stem()
        .and_then(|value| value.to_str())
        .unwrap_or("merged");
    output.with_file_name(format!(
        ".{stem}.{}.{:016x}.tmp.cast",
        std::process::id(),
        NEXT_ID.fetch_add(1, Ordering::Relaxed)
    ))
}

fn commit_output(temporary: &Path, output: &Path, overwrite: bool) -> Result<(), MergeError> {
    if !overwrite || !output.exists() {
        return fs::rename(temporary, output).map_err(|source| MergeError::Io {
            path: output.to_path_buf(),
            source,
        });
    }

    let backup = temporary_path(output).with_extension("backup");
    fs::rename(output, &backup).map_err(|source| MergeError::Io {
        path: output.to_path_buf(),
        source,
    })?;
    match fs::rename(temporary, output) {
        Ok(()) => {
            let _ = fs::remove_file(backup);
            Ok(())
        }
        Err(source) => {
            let _ = fs::rename(&backup, output);
            Err(MergeError::Io {
                path: output.to_path_buf(),
                source,
            })
        }
    }
}

struct TemporaryOutput {
    path: PathBuf,
    armed: std::cell::Cell<bool>,
}

impl TemporaryOutput {
    fn new(path: PathBuf) -> Self {
        Self {
            path,
            armed: std::cell::Cell::new(true),
        }
    }

    fn disarm(&self) {
        self.armed.set(false);
    }
}

impl Drop for TemporaryOutput {
    fn drop(&mut self) {
        if self.armed.get() {
            let _ = fs::remove_file(&self.path);
        }
    }
}
