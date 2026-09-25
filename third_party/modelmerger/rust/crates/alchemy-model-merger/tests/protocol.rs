use cast_codec::{CastFile, CastNode, CastProperty, PropertyValues};
use serde_json::{Value, json};
use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::{
    atomic::{AtomicU64, Ordering},
    mpsc,
};
use std::time::Duration;

struct Session {
    child: Child,
    input: Option<ChildStdin>,
    events: mpsc::Receiver<Value>,
}
impl Session {
    fn new() -> Self {
        let mut child = Command::new(env!("CARGO_BIN_EXE_alchemy-model-merger"))
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .unwrap();
        let input = child.stdin.take();
        let output = child.stdout.take().unwrap();
        let (sender, events) = mpsc::channel();
        std::thread::spawn(move || {
            for line in BufReader::new(output).lines() {
                let event =
                    serde_json::from_str(&line.unwrap()).expect("stdout must contain JSON only");
                if sender.send(event).is_err() {
                    break;
                }
            }
        });
        Self {
            child,
            input,
            events,
        }
    }
    fn send(&mut self, command: Value) {
        writeln!(self.input.as_mut().unwrap(), "{command}").unwrap();
        self.input.as_mut().unwrap().flush().unwrap();
    }
    fn until(&self, kind: &str) -> Value {
        loop {
            let event = self
                .events
                .recv_timeout(Duration::from_secs(15))
                .expect("timely protocol event");
            if event["event"] == kind {
                return event;
            }
            assert_ne!(event["event"], "error", "unexpected error: {event}");
        }
    }
    fn finish(&mut self, success: bool) {
        // The completed event, not stdin EOF, is the task completion handshake.
        assert_eq!(self.child.wait().unwrap().success(), success);
    }
}
impl Drop for Session {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}
struct Directory(PathBuf);
impl Directory {
    fn new() -> Self {
        static NEXT: AtomicU64 = AtomicU64::new(0);
        let path = std::env::temp_dir().join(format!(
            "alchemy-merger-protocol-{}-{}",
            std::process::id(),
            NEXT.fetch_add(1, Ordering::Relaxed)
        ));
        std::fs::create_dir_all(&path).unwrap();
        Self(path)
    }
}
impl Drop for Directory {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}
fn fixture(index: usize) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join(format!(
        "../../../tests/fixtures/rust-migration/golden-small/part-{index:02}.cast"
    ))
}
fn merge(directory: &Path, overwrite: bool) -> Value {
    json!({"command":"merge","input_files":[fixture(0),fixture(1)],
        "output_directory":directory,"output_file_name":"merged.cast","manual_root_file":null,"overwrite":overwrite})
}

#[test]
fn merge_requires_execute_and_reports_readable_result() {
    let directory = Directory::new();
    let mut session = Session::new();
    session.send(merge(&directory.0, false));
    let prepared = session.until("prepared");
    assert_eq!(prepared["output_exists"], false);
    assert!(!directory.0.join("merged.cast").exists());
    session.send(json!({"command":"execute"}));
    let completed = session.until("completed");
    assert_eq!(completed["part_count"], 2);
    assert_eq!(completed["bone_count"], 2);
    assert_eq!(completed["mesh_count"], 2);
    CastFile::decode(&std::fs::read(directory.0.join("merged.cast")).unwrap()).unwrap();
    session.finish(true);
}

#[test]
fn cancel_and_disconnection_after_prepare_leave_no_output() {
    for disconnect in [false, true] {
        let directory = Directory::new();
        let mut session = Session::new();
        session.send(merge(&directory.0, false));
        session.until("prepared");
        if disconnect {
            session.input.take();
        } else {
            session.send(json!({"command":"cancel"}));
        }
        assert_eq!(session.until("error")["code"], "cancelled");
        session.finish(false);
        assert_eq!(std::fs::read_dir(&directory.0).unwrap().count(), 0);
    }
}

#[test]
fn overwrite_preparation_preserves_old_file_until_execute() {
    let directory = Directory::new();
    let output = directory.0.join("merged.cast");
    std::fs::write(&output, b"original").unwrap();
    let mut refusal = Session::new();
    refusal.send(merge(&directory.0, false));
    assert_eq!(refusal.until("error")["code"], "OutputAlreadyExists");
    refusal.finish(false);
    let mut session = Session::new();
    session.send(merge(&directory.0, true));
    assert_eq!(session.until("prepared")["output_exists"], true);
    assert_eq!(std::fs::read(&output).unwrap(), b"original");
    session.send(json!({"command":"cancel"}));
    session.until("error");
    session.finish(false);
    assert_eq!(std::fs::read(&output).unwrap(), b"original");
}

#[test]
fn preview_uses_bounded_upstream_geometry() {
    let mut session = Session::new();
    session.send(json!({"command":"preview","file_path":fixture(0),"triangle_limit":1}));
    let event = session.until("preview");
    assert_eq!(event["displayed_triangle_count"], 1);
    assert_eq!(
        event["meshes"][0]["triangle_indices"]
            .as_array()
            .unwrap()
            .len(),
        3
    );
    assert_eq!(event["bounds"]["minimum"].as_array().unwrap().len(), 3);
    session.finish(true);
}

#[test]
fn execute_overwrite_authorization_is_independent_of_preparation() {
    for (exists_before_prepare, authorize) in [(true, false), (false, false), (true, true)] {
        let directory = Directory::new();
        let output = directory.0.join("merged.cast");
        if exists_before_prepare {
            std::fs::write(&output, b"original").unwrap();
        }
        let mut session = Session::new();
        session.send(merge(&directory.0, true));
        assert_eq!(
            session.until("prepared")["output_exists"],
            exists_before_prepare
        );
        if !exists_before_prepare {
            std::fs::write(&output, b"original").unwrap();
        }
        session.send(json!({"command":"execute","overwrite":authorize}));
        if authorize {
            session.until("completed");
            CastFile::decode(&std::fs::read(&output).unwrap()).unwrap();
        } else {
            assert_eq!(session.until("error")["code"], "OutputAlreadyExists");
            assert_eq!(std::fs::read(&output).unwrap(), b"original");
        }
        session.finish(authorize);
    }
}

#[test]
fn malformed_commands_and_missing_inputs_fail_cleanly() {
    for command in [
        json!({"command":"unknown"}),
        json!({"command":"execute"}),
        json!({"command":"inspect_ammunition","file_path":"missing.cast"}),
        json!({"command":"merge","input_files":[],"output_directory":"."}),
    ] {
        let mut session = Session::new();
        session.send(command);
        assert!(session.until("error")["message"].as_str().unwrap().len() > 0);
        session.finish(false);
    }
}

fn property(node: &mut CastNode, name: &str, values: PropertyValues) {
    if let Some(found) = node.properties.iter_mut().find(|p| p.name == name) {
        found.values = values;
    } else {
        node.properties.push(CastProperty {
            name: name.into(),
            values,
        });
    }
}
fn ammo_fixtures(directory: &Path) -> (PathBuf, PathBuf) {
    let base = CastFile::decode(&std::fs::read(fixture(0)).unwrap()).unwrap();
    let mut ammo = base.clone();
    let model = &mut ammo.roots[0].children[0];
    let skeleton = model
        .children
        .iter_mut()
        .find(|n| n.identifier == u32::from_le_bytes(*b"skel"))
        .unwrap();
    skeleton
        .children
        .retain(|n| n.identifier == u32::from_le_bytes(*b"bone"));
    skeleton.children.truncate(1);
    property(
        &mut skeleton.children[0],
        "n",
        PropertyValues::String("tag_ammo".into()),
    );
    property(
        &mut skeleton.children[0],
        "p",
        PropertyValues::Integer32(vec![u32::MAX]),
    );
    let ammo_path = directory.join("ammo.cast");
    std::fs::write(&ammo_path, ammo.encode().unwrap()).unwrap();
    let mut weapon = base;
    let model = &mut weapon.roots[0].children[0];
    model
        .children
        .retain(|n| n.identifier != u32::from_le_bytes(*b"mesh"));
    let skeleton = model
        .children
        .iter_mut()
        .find(|n| n.identifier == u32::from_le_bytes(*b"skel"))
        .unwrap();
    let template = skeleton.children[0].clone();
    skeleton.children.clear();
    for (i, (name, parent)) in [
        ("j_mag1", u32::MAX),
        ("j_ammo_01", 0),
        ("j_mag2", u32::MAX),
        ("j_ammo_99", u32::MAX),
    ]
    .into_iter()
    .enumerate()
    {
        let mut bone = template.clone();
        bone.hash += i as u64;
        property(&mut bone, "n", PropertyValues::String(name.into()));
        property(&mut bone, "p", PropertyValues::Integer32(vec![parent]));
        skeleton.children.push(bone);
    }
    let weapon_path = directory.join("weapon.cast");
    std::fs::write(&weapon_path, weapon.encode().unwrap()).unwrap();
    (weapon_path, ammo_path)
}

fn assemble_fixtures(directory: &Path) -> (PathBuf, PathBuf) {
    let arms_path = directory.join("arms.cast");
    std::fs::copy(fixture(0), &arms_path).unwrap();
    let weapon_path = directory.join("weapon.cast");
    std::fs::copy(fixture(1), &weapon_path).unwrap();
    (arms_path, weapon_path)
}

#[test]
fn arms_inspection_and_assembly_round_trip() {
    let directory = Directory::new();
    let (arms, weapon) = assemble_fixtures(&directory.0);
    let mut inspect = Session::new();
    inspect.send(json!({"command":"inspect_arms","file_path":arms}));
    let analysis = inspect.until("analysis");
    let bones = analysis["bones"].as_array().unwrap().clone();
    assert!(!bones.is_empty());
    inspect.finish(true);

    let output = directory.0.join("hawk_viewhands.cast");
    let mut assemble = Session::new();
    assemble.send(json!({"command":"assemble","arms":arms,"weapon":weapon,
        "output":output,"target_bone":bones[0]}));
    let completed = assemble.until("completed");
    assert_eq!(completed["target_bone"], bones[0]);
    assert!(completed["attached_meshes"].as_u64().unwrap() >= 1);
    assemble.finish(true);
    CastFile::decode(&std::fs::read(&output).unwrap()).unwrap();

    let mut again = Session::new();
    again.send(json!({"command":"assemble","arms":arms,"weapon":weapon,
        "output":output,"target_bone":null}));
    assert_eq!(again.until("error")["code"], "engine");
    again.finish(false);

    let mut missing_bone = Session::new();
    missing_bone.send(json!({"command":"assemble","arms":arms,"weapon":weapon,
        "output":directory.0.join("other.cast"),"target_bone":"j_missing"}));
    assert_eq!(missing_bone.until("error")["code"], "engine");
    missing_bone.finish(false);
}

#[test]
fn ammunition_inspect_fill_replicas_and_no_overwrite_round_trip() {
    let directory = Directory::new();
    let (weapon, ammunition) = ammo_fixtures(&directory.0);
    let mut inspect = Session::new();
    inspect.send(json!({"command":"inspect_ammunition","file_path":weapon}));
    let result = inspect.until("analysis");
    assert_eq!(result["magazines"][0]["name"], "j_mag1");
    assert_eq!(result["spare_magazines"][0], "j_mag2");
    assert_eq!(result["excluded_slots"][0], "j_ammo_99");
    inspect.finish(true);
    let output = directory.0.join("filled.cast");
    let request = json!({"command":"fill_ammunition","weapon":weapon,"ammunition":ammunition,
        "output":output,"magazines":["j_mag1"],"extra_slots":["j_ammo_99"],
        "replicas":[{"source":"j_mag1","target":"j_mag2"}]});
    let mut fill = Session::new();
    fill.send(request.clone());
    assert_eq!(fill.until("completed")["inserted"], 3);
    fill.finish(true);
    let bytes = std::fs::read(&output).unwrap();
    CastFile::decode(&bytes).unwrap();
    let mut again = Session::new();
    again.send(request);
    again.until("error");
    again.finish(false);
    assert_eq!(std::fs::read(output).unwrap(), bytes);
}
