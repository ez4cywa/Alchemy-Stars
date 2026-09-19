use model_merger_engine::{
    MergeError, MergeObserver, MergeRequest, MergeStage, RootSelection, ammunition,
};
use serde::Deserialize;
use serde_json::{Value, json};
use std::io::{self, BufRead, Write};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, mpsc};

#[derive(Deserialize)]
#[serde(tag = "command", rename_all = "snake_case", deny_unknown_fields)]
enum Command {
    Merge {
        input_files: Vec<PathBuf>,
        output_directory: PathBuf,
        #[serde(default)]
        output_file_name: Option<String>,
        #[serde(default)]
        manual_root_file: Option<PathBuf>,
        #[serde(default)]
        overwrite: bool,
    },
    InspectAmmunition {
        file_path: PathBuf,
    },
    FillAmmunition {
        weapon: PathBuf,
        ammunition: PathBuf,
        output: PathBuf,
        magazines: Vec<String>,
        #[serde(default)]
        extra_slots: Vec<String>,
        #[serde(default)]
        replicas: Vec<Replica>,
    },
    Preview {
        file_path: PathBuf,
        #[serde(default = "triangle_limit")]
        triangle_limit: usize,
    },
    Execute {
        #[serde(default)]
        overwrite: bool,
    },
    Cancel,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Replica {
    source: String,
    target: String,
}
fn triangle_limit() -> usize {
    250_000
}

struct Observer {
    cancelled: Arc<AtomicBool>,
}
impl MergeObserver for Observer {
    fn is_cancelled(&self) -> bool {
        self.cancelled.load(Ordering::SeqCst)
    }
    fn on_progress(&self, stage: MergeStage, current: usize, total: usize, item: Option<&str>) {
        if emit(
            json!({"event":"progress", "stage":format!("{stage:?}"), "current":current,
            "total":total,"item":item}),
        )
        .is_err()
        {
            self.cancelled.store(true, Ordering::SeqCst);
        }
    }
}

fn emit(value: Value) -> Result<(), String> {
    let mut out = io::stdout().lock();
    serde_json::to_writer(&mut out, &value).map_err(|e| e.to_string())?;
    out.write_all(b"\n")
        .and_then(|_| out.flush())
        .map_err(|e| e.to_string())
}

fn read_command(reader: &mut impl BufRead) -> Result<Option<Command>, String> {
    // Bound untrusted protocol input without allocating an unbounded line.
    let mut bytes = Vec::new();
    loop {
        let buffer = reader.fill_buf().map_err(|e| e.to_string())?;
        if buffer.is_empty() {
            if bytes.is_empty() {
                return Ok(None);
            }
            break;
        }
        let length = buffer
            .iter()
            .position(|b| *b == b'\n')
            .map_or(buffer.len(), |n| n + 1);
        if bytes.len() + length > 1_048_576 {
            return Err("JSON command exceeds 1 MiB".into());
        }
        bytes.extend_from_slice(&buffer[..length]);
        reader.consume(length);
        if bytes.last() == Some(&b'\n') {
            break;
        }
    }
    serde_json::from_slice(&bytes)
        .map(Some)
        .map_err(|e| format!("Invalid JSON command: {e}"))
}

fn engine_error(error: MergeError) -> (String, String) {
    let code = match &error {
        MergeError::Cancelled => "cancelled".into(),
        MergeError::Validation { code, .. } => format!("{code:?}"),
        _ => "engine".into(),
    };
    (code, error.to_string())
}

fn run(
    first: Command,
    controls: mpsc::Receiver<Command>,
    observer: &Observer,
) -> Result<(), (String, String)> {
    let output = match first {
        Command::Merge {
            input_files,
            output_directory,
            output_file_name,
            manual_root_file,
            overwrite,
        } => {
            let prepared = model_merger_engine::prepare(
                MergeRequest {
                    input_files,
                    output_directory,
                    output_file_name,
                    overwrite,
                    root_selection: manual_root_file
                        .map_or(RootSelection::Automatic, RootSelection::Manual),
                },
                observer,
            )
            .map_err(engine_error)?;
            emit(
                json!({"event":"prepared", "output_path":prepared.output_path(),
                "output_exists":prepared.output_path().exists()}),
            )
            .map_err(|e| ("protocol".into(), e))?;
            let overwrite_authorized = match controls.recv() {
                Ok(Command::Execute { overwrite }) if !observer.is_cancelled() => overwrite,
                _ => {
                    return Err((
                        "cancelled".into(),
                        "merge was cancelled before execution".into(),
                    ));
                }
            };
            let result = prepared
                .with_overwrite(overwrite_authorized)
                .execute(observer)
                .map_err(engine_error)?;
            json!({"event":"completed","output_path":result.output_path,
                "root_model_name":result.root_model_name,"part_count":result.part_count,
                "bone_count":result.bone_count,"mesh_count":result.mesh_count,
                "warnings":result.warnings.iter().map(|w| format!("{w:?}")).collect::<Vec<_>>()})
        }
        Command::InspectAmmunition { file_path } => {
            let result = ammunition::inspect(&file_path, observer).map_err(engine_error)?;
            json!({"event":"analysis", "magazines":result.magazines.iter().map(|m|
                json!({"name":m.name,"slots":m.slots,"occupied":m.occupied})).collect::<Vec<_>>(),
                "excluded_slots":result.excluded_slots,"spare_magazines":result.spare_magazines})
        }
        Command::FillAmmunition {
            weapon,
            ammunition,
            output,
            magazines,
            extra_slots,
            replicas,
        } => {
            let result = ammunition::fill(
                ammunition::FillRequest {
                    weapon,
                    ammunition,
                    output,
                    magazines,
                    extra_slots,
                    replicas: replicas
                        .into_iter()
                        .map(|r| ammunition::MagazineReplica {
                            source: r.source,
                            target: r.target,
                        })
                        .collect(),
                },
                observer,
            )
            .map_err(engine_error)?;
            json!({"event":"completed","output_path":result.output,"inserted":result.inserted,"skipped":result.skipped})
        }
        Command::Preview {
            file_path,
            triangle_limit,
        } => {
            let data =
                model_merger_engine::load_preview(&file_path, triangle_limit.min(250_000), || {
                    observer.is_cancelled()
                })
                .map_err(|e| {
                    let code = if matches!(e, model_merger_engine::PreviewError::Cancelled) {
                        "cancelled"
                    } else {
                        "engine"
                    };
                    (code.into(), e.to_string())
                })?;
            json!({"event":"preview","file_path":data.file_path,"model_name":data.model_name,
                "source_mesh_count":data.source_mesh_count,"source_vertex_count":data.source_vertex_count,
                "source_triangle_count":data.source_triangle_count,"displayed_triangle_count":data.displayed_triangle_count,
                "is_simplified":data.is_simplified,"bounds":{"minimum":data.bounds.minimum,"maximum":data.bounds.maximum},
                "meshes":data.meshes.into_iter().map(|m| json!({"positions":m.positions,"normals":m.normals,
                    "triangle_indices":m.triangle_indices})).collect::<Vec<_>>()})
        }
        _ => return Err(("protocol".into(), "First command must select a task".into())),
    };
    emit(output).map_err(|e| ("protocol".into(), e))
}

fn main() {
    let cancelled = Arc::new(AtomicBool::new(false));
    let protocol_error = Arc::new(Mutex::new(None));
    let (sender, receiver) = mpsc::channel();
    let read_cancelled = cancelled.clone();
    let read_error = protocol_error.clone();
    std::thread::spawn(move || {
        let mut stdin = io::stdin().lock();
        let mut first = true;
        loop {
            match read_command(&mut stdin) {
                Ok(Some(command)) => {
                    if !first && !matches!(command, Command::Cancel | Command::Execute { .. }) {
                        *read_error.lock().unwrap() =
                            Some("Only execute or cancel may follow the initial command".into());
                        read_cancelled.store(true, Ordering::SeqCst);
                        break;
                    }
                    first = false;
                    if matches!(command, Command::Cancel) {
                        read_cancelled.store(true, Ordering::SeqCst);
                    }
                    if sender.send(command).is_err() {
                        break;
                    }
                }
                Ok(None) => {
                    read_cancelled.store(true, Ordering::SeqCst);
                    break;
                }
                Err(error) => {
                    *read_error.lock().unwrap() = Some(error);
                    read_cancelled.store(true, Ordering::SeqCst);
                    break;
                }
            }
        }
    });
    let observer = Observer { cancelled };
    let result = match receiver.recv() {
        Ok(first) => run(first, receiver, &observer),
        Err(_) => Err(("protocol".into(), "No initial command received".into())),
    };
    if let Err((mut code, mut message)) = result {
        if let Some(error) = protocol_error.lock().unwrap().take() {
            code = "protocol".into();
            message = error;
        }
        let _ = emit(json!({"event":"error","code":code,"message":message}));
        std::process::exit(1);
    }
}
