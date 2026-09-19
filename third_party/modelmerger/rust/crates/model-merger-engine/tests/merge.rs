use cast_codec::{CastFile, PropertyValues};
use model_merger_engine::{
    MergeObserver, MergeRequest, MergeStage, NoopObserver, RootSelection, prepare,
};
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};

#[test]
fn connected_csharp_golden_parts_merge_into_a_readable_cast_model() {
    let fixture_directory = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../tests/fixtures/rust-migration/golden-small");
    let output_directory = TestDirectory::new();
    let request = MergeRequest {
        input_files: vec![
            fixture_directory.join("part-00.cast"),
            fixture_directory.join("part-01.cast"),
        ],
        output_directory: output_directory.path.clone(),
        output_file_name: Some("rust-merged.cast".to_owned()),
        root_selection: RootSelection::Automatic,
        overwrite: false,
    };

    let prepared = prepare(request, &NoopObserver).expect("golden parts should prepare");
    assert_eq!(
        output_directory.path.join("rust-merged.cast"),
        prepared.output_path()
    );
    let result = prepared
        .execute(&NoopObserver)
        .expect("golden parts should merge");

    assert_eq!("part-00", result.root_model_name);
    assert_eq!(2, result.part_count);
    assert_eq!(2, result.bone_count);
    assert_eq!(2, result.mesh_count);
    let bytes = std::fs::read(&result.output_path).expect("merged output should exist");
    let file = CastFile::decode(&bytes).expect("merged output should be a readable Cast file");
    let model = file.roots[0]
        .children
        .iter()
        .find(|node| node.identifier == u32::from_le_bytes(*b"modl"))
        .expect("merged output should contain a model");
    let skeleton = model
        .children
        .iter()
        .find(|node| node.identifier == u32::from_le_bytes(*b"skel"))
        .expect("merged output should contain a skeleton");
    assert_eq!(
        2,
        skeleton
            .children
            .iter()
            .filter(|node| node.identifier == u32::from_le_bytes(*b"bone"))
            .count()
    );
    assert_eq!(
        2,
        model
            .children
            .iter()
            .filter(|node| node.identifier == u32::from_le_bytes(*b"mesh"))
            .count()
    );

    let materials: Vec<_> = model
        .children
        .iter()
        .filter(|node| node.identifier == u32::from_le_bytes(*b"matl"))
        .map(|node| string_property(node, "n"))
        .collect();
    assert_eq!(vec!["shared-material", "material-01"], materials);
    let blends: Vec<_> = model
        .children
        .iter()
        .filter(|node| node.identifier == u32::from_le_bytes(*b"blsh"))
        .map(|node| string_property(node, "n"))
        .collect();
    assert_eq!(vec!["shape-00", "shape-01"], blends);
    let position_checksums: Vec<f32> = model
        .children
        .iter()
        .filter(|node| node.identifier == u32::from_le_bytes(*b"mesh"))
        .map(|node| match property(node, "vp") {
            PropertyValues::Vector3(values) => values.iter().flatten().sum(),
            _ => panic!("mesh positions should be vector3 values"),
        })
        .collect();
    assert_eq!(vec![24.0, 48.0], position_checksums);
}

#[test]
fn automatic_root_merges_the_supported_maximum_of_fifteen_connected_parts() {
    let fixture_directory = chain_fixture_directory();
    let output_directory = TestDirectory::new();
    let result = prepare(
        MergeRequest {
            input_files: chain_inputs(&fixture_directory, 15),
            output_directory: output_directory.path.clone(),
            output_file_name: Some("fifteen.cast".to_owned()),
            root_selection: RootSelection::Automatic,
            overwrite: false,
        },
        &NoopObserver,
    )
    .expect("fifteen parts should prepare")
    .execute(&NoopObserver)
    .expect("fifteen parts should merge");

    assert_eq!("part-00", result.root_model_name);
    assert_eq!(15, result.part_count);
    assert_eq!(15, result.bone_count);
    assert_eq!(15, result.mesh_count);
}

#[test]
fn manual_root_is_preserved_for_an_eight_part_merge() {
    let fixture_directory = chain_fixture_directory();
    let inputs = chain_inputs(&fixture_directory, 8);
    let output_directory = TestDirectory::new();
    let result = prepare(
        MergeRequest {
            input_files: inputs.clone(),
            output_directory: output_directory.path.clone(),
            output_file_name: None,
            root_selection: RootSelection::Manual(inputs[3].clone()),
            overwrite: false,
        },
        &NoopObserver,
    )
    .expect("manual root should prepare")
    .execute(&NoopObserver)
    .expect("eight parts should merge");

    assert_eq!("part-03", result.root_model_name);
    assert_eq!(
        output_directory.path.join("part-03.cast"),
        result.output_path
    );
    assert_eq!(8, result.mesh_count);
}

#[test]
fn cancellation_before_save_leaves_no_output_or_temporary_file() {
    let fixture_directory = chain_fixture_directory();
    let output_directory = TestDirectory::new();
    let observer = CancelOnSaving::default();
    let prepared = prepare(
        MergeRequest {
            input_files: chain_inputs(&fixture_directory, 8),
            output_directory: output_directory.path.clone(),
            output_file_name: Some("cancelled.cast".to_owned()),
            root_selection: RootSelection::Automatic,
            overwrite: false,
        },
        &observer,
    )
    .expect("merge should prepare");

    assert!(prepared.execute(&observer).is_err());
    assert!(!output_directory.path.join("cancelled.cast").exists());
    assert_eq!(
        0,
        std::fs::read_dir(&output_directory.path).unwrap().count()
    );
}

#[test]
fn progress_reports_the_full_ordered_pipeline() {
    let fixture_directory = chain_fixture_directory();
    let output_directory = TestDirectory::new();
    let observer = RecordingObserver::default();
    prepare(
        MergeRequest {
            input_files: chain_inputs(&fixture_directory, 2),
            output_directory: output_directory.path.clone(),
            output_file_name: Some("progress.cast".to_owned()),
            root_selection: RootSelection::Automatic,
            overwrite: false,
        },
        &observer,
    )
    .unwrap()
    .execute(&observer)
    .unwrap();

    let stages = observer.stages.lock().unwrap();
    let expected = [
        MergeStage::Validating,
        MergeStage::Loading,
        MergeStage::SelectingRoot,
        MergeStage::Merging,
        MergeStage::Saving,
        MergeStage::Verifying,
        MergeStage::Completed,
    ];
    for stage in expected {
        assert!(stages.contains(&stage), "missing progress stage {stage:?}");
    }
    assert!(
        stages
            .windows(2)
            .all(|pair| pipeline_rank(pair[0]) <= pipeline_rank(pair[1]))
    );
}

#[test]
fn existing_output_requires_explicit_overwrite() {
    let fixture_directory = chain_fixture_directory();
    let output_directory = TestDirectory::new();
    let output = output_directory.path.join("existing.cast");
    std::fs::write(&output, b"old").unwrap();
    let request = |overwrite| MergeRequest {
        input_files: chain_inputs(&fixture_directory, 2),
        output_directory: output_directory.path.clone(),
        output_file_name: Some("existing.cast".to_owned()),
        root_selection: RootSelection::Automatic,
        overwrite,
    };

    assert!(prepare(request(false), &NoopObserver).is_err());
    prepare(request(true), &NoopObserver)
        .unwrap()
        .execute(&NoopObserver)
        .unwrap();
    assert_ne!(b"old", std::fs::read(output).unwrap().as_slice());
}

#[test]
fn part_count_outside_two_through_fifteen_is_rejected() {
    let fixture_directory = chain_fixture_directory();
    let output_directory = TestDirectory::new();
    for count in [1, 16] {
        let request = MergeRequest {
            input_files: (0..count)
                .map(|index| fixture_directory.join(format!("part-{:02}.cast", index % 15)))
                .collect(),
            output_directory: output_directory.path.clone(),
            output_file_name: None,
            root_selection: RootSelection::Automatic,
            overwrite: false,
        };
        assert!(prepare(request, &NoopObserver).is_err());
    }
}

fn chain_fixture_directory() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../tests/fixtures/rust-migration/golden-chain-15")
}

fn chain_inputs(directory: &Path, count: usize) -> Vec<PathBuf> {
    (0..count)
        .map(|index| directory.join(format!("part-{index:02}.cast")))
        .collect()
}

fn property<'a>(node: &'a cast_codec::CastNode, name: &str) -> &'a PropertyValues {
    &node
        .properties
        .iter()
        .find(|property| property.name == name)
        .unwrap_or_else(|| panic!("missing property {name}"))
        .values
}

fn string_property<'a>(node: &'a cast_codec::CastNode, name: &str) -> &'a str {
    match property(node, name) {
        PropertyValues::String(value) => value,
        _ => panic!("property {name} should be a string"),
    }
}

fn pipeline_rank(stage: MergeStage) -> usize {
    match stage {
        MergeStage::Validating => 0,
        MergeStage::Loading => 1,
        MergeStage::SelectingRoot => 2,
        MergeStage::Merging => 3,
        MergeStage::Saving => 4,
        MergeStage::Verifying => 5,
        MergeStage::Completed => 6,
    }
}

#[derive(Default)]
struct CancelOnSaving {
    cancelled: AtomicBool,
}

impl MergeObserver for CancelOnSaving {
    fn is_cancelled(&self) -> bool {
        self.cancelled.load(Ordering::Relaxed)
    }

    fn on_progress(&self, stage: MergeStage, _current: usize, _total: usize, _item: Option<&str>) {
        if stage == MergeStage::Saving {
            self.cancelled.store(true, Ordering::Relaxed);
        }
    }
}

#[derive(Default)]
struct RecordingObserver {
    stages: Mutex<Vec<MergeStage>>,
}

impl MergeObserver for RecordingObserver {
    fn on_progress(&self, stage: MergeStage, _current: usize, _total: usize, _item: Option<&str>) {
        self.stages.lock().unwrap().push(stage);
    }
}

struct TestDirectory {
    path: PathBuf,
}

impl TestDirectory {
    fn new() -> Self {
        static NEXT_ID: AtomicUsize = AtomicUsize::new(0);
        let path = std::env::temp_dir().join(format!(
            "model-merger-rust-engine-{}-{}",
            std::process::id(),
            NEXT_ID.fetch_add(1, Ordering::Relaxed)
        ));
        std::fs::create_dir_all(&path).expect("test directory should be created");
        Self { path }
    }
}

impl Drop for TestDirectory {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.path);
    }
}
