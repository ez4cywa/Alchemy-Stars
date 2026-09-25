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
fn model(name: &str, bones: Vec<Bone>, meshes: Vec<Mesh>) -> Model {
    Model {
        name: name.into(),
        bones,
        meshes,
        materials: vec![],
        shapes: vec![],
    }
}
fn write_model(name: &str, model: &Model) -> std::path::PathBuf {
    let directory = std::env::temp_dir().join(format!("armature-{}-{name}", std::process::id()));
    std::fs::create_dir_all(&directory).unwrap();
    let path = directory.join(format!("{name}.cast"));
    std::fs::write(&path, cast_model::encode_model(model).unwrap()).unwrap();
    path
}

#[test]
fn weapon_code_extracts_the_segment_before_the_part_type() {
    assert_eq!("eagle", weapon_code("att_sat_vm_ar_eagle_rec_LOD0"));
    assert_eq!("anov94", weapon_code("vm_jup_jp36_ar_anov94_rec_4028"));
    assert_eq!(
        "anov94",
        weapon_code("vm_jup_jp36_ar_anov94_mag_30_545_4028")
    );
    assert_eq!("eagle", weapon_code("att_sat_vm_ar_eagle_rec"));
    // No known part-type segment: keep the whole (trimmed) stem.
    assert_eq!("att_rex_vm_holo", weapon_code("att_rex_vm_holo_01_v0"));
    assert_eq!("eagle", weapon_code("eagle"));
}

#[test]
fn assemble_splices_the_weapon_root_onto_tag_weapon() {
    // Arms: tag_origin + tag_weapon carrying a rotated child; the arms' own
    // j_gun collides with the weapon root and forces a rename.
    let mut arms = model(
        "arms",
        vec![
            bone("tag_origin", -1, Vec3::default()),
            bone("tag_weapon", 0, Vec3(5.0, 0.0, 0.0)),
            bone("j_gun", 1, Vec3(0.0, 1.0, 0.0)),
        ],
        vec![mesh()],
    );
    arms.bones[2].local_rotation = Quaternion(
        0.0,
        0.0,
        std::f32::consts::FRAC_1_SQRT_2,
        std::f32::consts::FRAC_1_SQRT_2,
    );
    let arms_path = write_model("arms", &arms);
    // Weapon: root j_gun (zeroed by the splice) with a child bone; the mesh
    // is weighted to the child bone so its index must be re-mapped.
    let weapon = model(
        "weapon",
        vec![
            bone("j_gun", -1, Vec3(9.0, 9.0, 9.0)),
            bone("j_bolt", 0, Vec3(1.0, 0.0, 0.0)),
        ],
        vec![mesh()],
    );
    let weapon_path = write_model("weapon", &weapon);
    let output = arms_path.parent().unwrap().join("eagle_viewhands.cast");

    let result = assemble(
        AssembleRequest {
            arms: arms_path.clone(),
            weapon: weapon_path.clone(),
            output: output.clone(),
            target_bone: None,
        },
        &NoopObserver,
    )
    .unwrap();

    assert_eq!("tag_weapon", result.target_bone);
    assert_eq!(1, result.attached_meshes);
    let bytes = std::fs::read(&output).unwrap();
    let merged = cast_model::decode_model(&bytes, &output).unwrap();
    // 3 arms bones + 2 weapon bones.
    assert_eq!(5, merged.bones.len());
    assert_eq!(2, merged.meshes.len());
    // The weapon root is renamed (the arms already have a j_gun), zeroed
    // onto tag_weapon as its child.
    let spliced = merged
        .bones
        .iter()
        .find(|bone| bone.name == "j_gun_wpn")
        .expect("weapon root should be renamed to avoid the collision");
    assert_eq!(1, spliced.parent);
    assert_eq!(Vec3::default(), spliced.local_position);
    assert_eq!(Quaternion::IDENTITY, spliced.local_rotation);
    // The weapon root's global transform equals tag_weapon's.
    assert_eq!(merged.bones[1].global_position, spliced.global_position);
    // The weapon child keeps its local transform and re-indexed parent.
    let bolt = merged
        .bones
        .iter()
        .find(|bone| bone.name == "j_bolt")
        .unwrap();
    assert_eq!(Vec3(1.0, 0.0, 0.0), bolt.local_position);
    let spliced_index = merged
        .bones
        .iter()
        .position(|b| b.name == "j_gun_wpn")
        .unwrap();
    assert_eq!(spliced_index as i32, bolt.parent);
    // Its global position chains from the spliced root.
    assert_eq!(
        spliced.global_position + spliced.global_rotation.rotate(bolt.local_position),
        bolt.global_position
    );
    // The attached mesh keeps its weights, re-indexed to the spliced bones.
    let attached = &merged.meshes[1];
    assert!(attached.weight_bones.iter().all(|b| *b as usize >= 3));
    // Original files stay untouched.
    assert!(arms_path.exists() && weapon_path.exists());
    let _ = std::fs::remove_dir_all(arms_path.parent().unwrap());
}

#[test]
fn unweighted_weapon_meshes_bind_to_the_spliced_root() {
    let mut weapon = model(
        "weapon",
        vec![bone("j_gun", -1, Vec3::default())],
        vec![mesh()],
    );
    weapon.meshes[0].weights_per_vertex = 0;
    weapon.meshes[0].weight_bones.clear();
    weapon.meshes[0].weight_values.clear();
    let arms_path = write_model(
        "arms5",
        &model(
            "arms",
            vec![bone("tag_weapon", -1, Vec3::default())],
            vec![],
        ),
    );
    let weapon_path = write_model("weapon5", &weapon);
    let output = arms_path.parent().unwrap().join("out5.cast");

    assemble(
        AssembleRequest {
            arms: arms_path.clone(),
            weapon: weapon_path,
            output: output.clone(),
            target_bone: None,
        },
        &NoopObserver,
    )
    .unwrap();

    let merged = cast_model::decode_model(&std::fs::read(&output).unwrap(), &output).unwrap();
    let attached = &merged.meshes[0];
    assert_eq!(1, attached.weights_per_vertex);
    assert!(attached.weight_bones.iter().all(|b| *b == 1));
    assert!(attached.weight_values.iter().all(|w| *w == 1.0));
    let _ = std::fs::remove_dir_all(arms_path.parent().unwrap());
}

#[test]
fn explicit_unknown_target_bone_is_rejected() {
    let arms_path = write_model(
        "arms2",
        &model(
            "arms",
            vec![bone("tag_origin", -1, Vec3::default())],
            vec![],
        ),
    );
    let weapon_path = write_model(
        "weapon2",
        &model(
            "weapon",
            vec![bone("j_gun", -1, Vec3::default())],
            vec![mesh()],
        ),
    );
    let output = arms_path.parent().unwrap().join("out.cast");

    let error = assemble(
        AssembleRequest {
            arms: arms_path,
            weapon: weapon_path,
            output,
            target_bone: Some("tag_missing".into()),
        },
        &NoopObserver,
    )
    .unwrap_err();

    assert!(matches!(error, MergeError::InvalidModel(message) if message.contains("tag_missing")));
}

#[test]
fn weapon_without_bones_is_rejected() {
    let arms_path = write_model(
        "arms3",
        &model(
            "arms",
            vec![bone("tag_origin", -1, Vec3::default())],
            vec![],
        ),
    );
    let weapon_path = write_model("weapon3", &model("weapon", vec![], vec![mesh()]));
    let output = arms_path.parent().unwrap().join("out3.cast");

    let error = assemble(
        AssembleRequest {
            arms: arms_path,
            weapon: weapon_path,
            output,
            target_bone: None,
        },
        &NoopObserver,
    )
    .unwrap_err();

    assert!(matches!(error, MergeError::InvalidModel(_)));
}

#[test]
fn existing_output_is_rejected() {
    let arms_path = write_model(
        "arms4",
        &model("arms", vec![bone("j_gun", -1, Vec3::default())], vec![]),
    );
    let weapon_path = write_model(
        "weapon4",
        &model(
            "weapon",
            vec![bone("j_gun", -1, Vec3::default())],
            vec![mesh()],
        ),
    );
    let output = arms_path.parent().unwrap().join("out4.cast");
    std::fs::write(&output, b"existing").unwrap();

    let error = assemble(
        AssembleRequest {
            arms: arms_path,
            weapon: weapon_path,
            output,
            target_bone: None,
        },
        &NoopObserver,
    )
    .unwrap_err();

    assert!(
        matches!(error, MergeError::InvalidModel(message) if message.contains("already exists"))
    );
}

#[test]
fn cancelled_inspection_stops_before_reading() {
    struct Cancelled;
    impl MergeObserver for Cancelled {
        fn is_cancelled(&self) -> bool {
            true
        }
    }
    let error = inspect_arms(std::path::Path::new("nonexistent.cast"), &Cancelled).unwrap_err();
    assert!(matches!(error, MergeError::Cancelled));
}
