use super::*;
use crate::{
    NoopObserver,
    domain::{Bone, Mesh},
    math::Quaternion,
};
fn bone(name: &str, parent: i32, position: Vec3) -> Bone {
    Bone {
        name: name.into(),
        parent,
        local_position: position,
        local_rotation: Quaternion::IDENTITY,
        global_position: position,
        global_rotation: Quaternion::IDENTITY,
        scale: Vec3(1.0, 1.0, 1.0),
    }
}
fn mesh() -> Mesh {
    Mesh {
        positions: vec![
            Vec3(3.0, 0.0, 0.0),
            Vec3(2.0, 1.0, 0.0),
            Vec3(2.0, 0.0, 1.0),
        ],
        normals: vec![Vec3(1.0, 0.0, 0.0); 3],
        tangents: vec![Vec3::default(); 3],
        colors: vec![[1.0; 4]; 3],
        uvs: vec![[0.0; 2]; 3],
        faces: vec![0, 1, 2],
        weights_per_vertex: 1,
        weight_bones: vec![0; 3],
        weight_values: vec![1.0; 3],
        material_indices: vec![],
        shape_deltas: vec![],
    }
}
#[test]
fn groups_follow_ancestry_and_exclude_non_magazine_ammo() {
    let model = Model {
        name: "weapon".into(),
        bones: vec![
            bone("j_mag1", -1, Vec3::default()),
            bone("j_ammo_01", 0, Vec3::default()),
            bone("j_ammo_17", -1, Vec3::default()),
            bone("j_ammo_helper", 0, Vec3::default()),
        ],
        meshes: vec![],
        materials: vec![],
        shapes: vec![],
    };
    let result = analyze(&model);
    assert_eq!(result.magazines.len(), 1);
    assert_eq!(result.magazines[0].slots, vec!["j_ammo_01"]);
    assert_eq!(result.excluded_slots, vec!["j_ammo_17"]);
}

#[test]
fn magazine_wrapper_is_not_offered_as_a_spare() {
    let model = Model {
        name: "magazine".into(),
        bones: vec![
            bone("tag_clip", -1, Vec3::default()),
            bone("j_mag1", 0, Vec3::default()),
            bone("j_ammo_01", 1, Vec3::default()),
            bone("j_mag2", 0, Vec3::default()),
        ],
        meshes: vec![],
        materials: vec![],
        shapes: vec![],
    };
    assert_eq!(analyze(&model).spare_magazines, vec!["j_mag2"]);
}
#[test]
fn fill_rotates_about_source_anchor_binds_and_preserves_original_nodes() {
    let directory = std::env::temp_dir().join(format!("ammo-placement-{}", std::process::id()));
    std::fs::create_dir_all(&directory).unwrap();
    let weapon = directory.join("weapon.cast");
    let ammo_path = directory.join("ammo.cast");
    let output = directory.join("filled.cast");
    let mut target = bone("j_ammo_01", 0, Vec3(10.0, 0.0, 0.0));
    target.local_rotation = Quaternion(
        0.0,
        0.0,
        std::f32::consts::FRAC_1_SQRT_2,
        std::f32::consts::FRAC_1_SQRT_2,
    );
    let model = Model {
        name: "weapon".into(),
        bones: vec![
            bone("j_mag1", -1, Vec3::default()),
            target,
            bone("j_ammo_17", -1, Vec3(5.0, 0.0, 0.0)),
        ],
        meshes: vec![],
        materials: vec![],
        shapes: vec![],
    };
    let ammo = Model {
        name: "ammo".into(),
        bones: vec![bone("tag_ammo", -1, Vec3(2.0, 0.0, 0.0))],
        meshes: vec![mesh()],
        materials: vec![],
        shapes: vec![],
    };
    let original = cast_model::encode_model(&model).unwrap();
    std::fs::write(&weapon, &original).unwrap();
    std::fs::write(&ammo_path, cast_model::encode_model(&ammo).unwrap()).unwrap();
    let request = FillRequest {
        weapon: weapon.clone(),
        ammunition: ammo_path.clone(),
        output: output.clone(),
        magazines: vec!["j_mag1".into()],
        extra_slots: vec![],
        replicas: vec![],
    };
    let result = fill(request.clone(), &NoopObserver).unwrap();
    assert_eq!(result.inserted, 1);
    let (mut raw, decoded) = read(&output, &NoopObserver).unwrap();
    let v = decoded.meshes[0].positions[0];
    assert!((v.0 - 10.0).abs() < 0.0001 && (v.1 - 1.0).abs() < 0.0001);
    assert_eq!(decoded.meshes[0].weight_bones, vec![1; 3]);
    assert_eq!(decoded.meshes[0].weight_values, vec![1.0; 3]);
    let mut original_raw = CastFile::decode(&original).unwrap();
    assert_eq!(
        model_node(&mut raw).unwrap().children[0],
        model_node(&mut original_raw).unwrap().children[0]
    );
    assert!(fill(request, &NoopObserver).is_err());
    let repeat = FillRequest {
        weapon: output,
        ammunition: ammo_path,
        output: directory.join("repeat.cast"),
        magazines: vec!["j_mag1".into()],
        extra_slots: vec![],
        replicas: vec![],
    };
    assert!(fill(repeat, &NoopObserver).is_err());
    let extra_output = directory.join("extra.cast");
    let extra_request = FillRequest {
        weapon: weapon.clone(),
        ammunition: directory.join("ammo.cast"),
        output: extra_output.clone(),
        magazines: vec![],
        extra_slots: vec!["j_ammo_17".into(), "j_ammo_17".into()],
        replicas: vec![],
    };
    assert_eq!(
        fill(extra_request.clone(), &NoopObserver).unwrap().inserted,
        1
    );
    let (_, extra_model) = read(&extra_output, &NoopObserver).unwrap();
    assert_eq!(extra_model.meshes[0].weight_bones, vec![2; 3]);
    assert!(
        fill(
            FillRequest {
                weapon: extra_output,
                output: directory.join("extra-repeat.cast"),
                ..extra_request.clone()
            },
            &NoopObserver
        )
        .is_err()
    );
    assert!(
        fill(
            FillRequest {
                output: directory.join("invalid.cast"),
                extra_slots: vec!["j_mag1".into()],
                ..extra_request.clone()
            },
            &NoopObserver
        )
        .is_err()
    );
    assert_eq!(
        fill(
            FillRequest {
                output: directory.join("both.cast"),
                magazines: vec!["j_mag1".into()],
                ..extra_request
            },
            &NoopObserver
        )
        .unwrap()
        .inserted,
        2
    );
    assert_eq!(std::fs::read(&weapon).unwrap(), original);
    std::fs::remove_dir_all(directory).unwrap();
}
#[test]
fn cancelled_inspection_never_reads_the_file() {
    struct Cancel;
    impl MergeObserver for Cancel {
        fn is_cancelled(&self) -> bool {
            true
        }
    }
    assert!(matches!(
        inspect(Path::new("missing.cast"), &Cancel),
        Err(MergeError::Cancelled)
    ));
}

#[test]
fn spare_magazine_copies_local_layout_and_remains_bound_to_its_own_bone() {
    let directory = std::env::temp_dir().join(format!("ammo-replica-{}", std::process::id()));
    std::fs::create_dir_all(&directory).unwrap();
    let weapon = directory.join("weapon.cast");
    let ammo_path = directory.join("ammo.cast");
    let output = directory.join("filled.cast");
    let mut source = bone("j_mag1", -1, Vec3(5.0, 0.0, 0.0));
    source.local_rotation = Quaternion(
        0.0,
        0.0,
        std::f32::consts::FRAC_1_SQRT_2,
        std::f32::consts::FRAC_1_SQRT_2,
    );
    let mut spare = bone("j_mag2", -1, Vec3(10.0, 0.0, 0.0));
    spare.local_rotation = Quaternion(0.0, 0.0, 1.0, 0.0);
    let model = Model {
        name: "weapon".into(),
        bones: vec![source, bone("j_ammo_01", 0, Vec3(2.0, 0.0, 0.0)), spare],
        meshes: vec![],
        materials: vec![],
        shapes: vec![],
    };
    let ammo = Model {
        name: "ammo".into(),
        bones: vec![bone("tag_ammo", -1, Vec3(2.0, 0.0, 0.0))],
        meshes: vec![mesh()],
        materials: vec![],
        shapes: vec![],
    };
    let original = cast_model::encode_model(&model).unwrap();
    std::fs::write(&weapon, &original).unwrap();
    std::fs::write(&ammo_path, cast_model::encode_model(&ammo).unwrap()).unwrap();
    assert_eq!(
        inspect(&weapon, &NoopObserver).unwrap().spare_magazines,
        vec!["j_mag2"]
    );
    let request = FillRequest {
        weapon: weapon.clone(),
        ammunition: ammo_path,
        output: output.clone(),
        magazines: vec![],
        extra_slots: vec![],
        replicas: vec![MagazineReplica {
            source: "j_mag1".into(),
            target: "j_mag2".into(),
        }],
    };
    let mut duplicate = request.clone();
    duplicate.replicas.push(duplicate.replicas[0].clone());
    assert!(fill(duplicate, &NoopObserver).is_err());
    assert!(!output.exists());
    assert_eq!(fill(request.clone(), &NoopObserver).unwrap().inserted, 1);
    let (_, filled) = read(&output, &NoopObserver).unwrap();
    assert_eq!(filled.bones.len(), 4);
    assert_eq!(filled.bones[3].parent, 2);
    assert_eq!(filled.meshes[0].weight_bones, vec![3; 3]);
    let position = filled.meshes[0].positions[0];
    assert!((position.0 - 7.0).abs() < 0.0001 && position.1.abs() < 0.0001);
    let analysis = inspect(&output, &NoopObserver).unwrap();
    assert!(analysis.spare_magazines.is_empty());
    assert_eq!(
        analysis
            .magazines
            .iter()
            .find(|magazine| magazine.name == "j_mag2")
            .unwrap()
            .occupied
            .len(),
        1
    );
    assert!(
        fill(
            FillRequest {
                weapon: output.clone(),
                output: directory.join("repeat.cast"),
                ..request
            },
            &NoopObserver
        )
        .is_err()
    );
    assert_eq!(std::fs::read(&weapon).unwrap(), original);
    for file in ["weapon.cast", "ammo.cast", "filled.cast"] {
        std::fs::remove_file(directory.join(file)).unwrap();
    }
    std::fs::remove_dir(directory).unwrap();
}
