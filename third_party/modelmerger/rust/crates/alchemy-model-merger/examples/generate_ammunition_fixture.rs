//! Deterministic synthetic CAST data for host end-to-end smoke tests (no game assets).
use cast_codec::{CastFile, CastNode, CastProperty, PropertyValues};
use std::path::{Path, PathBuf};

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

fn main() {
    let destination = PathBuf::from(
        std::env::args_os()
            .nth(1)
            .expect("output directory argument required"),
    );
    std::fs::create_dir_all(&destination).unwrap();
    let fixture = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../tests/fixtures/rust-migration/golden-small/part-00.cast");
    let base = CastFile::decode(&std::fs::read(fixture).unwrap()).unwrap();
    for is_ammo in [true, false] {
        let mut file = base.clone();
        let model = &mut file.roots[0].children[0];
        if !is_ammo {
            model
                .children
                .retain(|n| n.identifier != u32::from_le_bytes(*b"mesh"));
        }
        let skeleton = model
            .children
            .iter_mut()
            .find(|n| n.identifier == u32::from_le_bytes(*b"skel"))
            .unwrap();
        let template = skeleton
            .children
            .iter()
            .find(|n| n.identifier == u32::from_le_bytes(*b"bone"))
            .unwrap()
            .clone();
        skeleton.children.clear();
        let bones = if is_ammo {
            vec![("tag_ammo", u32::MAX)]
        } else {
            vec![
                ("j_mag1", u32::MAX),
                ("j_ammo_01", 0),
                ("j_mag2", u32::MAX),
                ("j_ammo_99", u32::MAX),
            ]
        };
        for (index, (name, parent)) in bones.into_iter().enumerate() {
            let mut bone = template.clone();
            bone.hash += index as u64;
            property(&mut bone, "n", PropertyValues::String(name.into()));
            property(&mut bone, "p", PropertyValues::Integer32(vec![parent]));
            skeleton.children.push(bone);
        }
        let path = destination.join(if is_ammo { "ammo.cast" } else { "weapon.cast" });
        // A fixture command must never silently replace user assets.
        let mut output = std::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&path)
            .unwrap();
        std::io::Write::write_all(&mut output, &file.encode().unwrap()).unwrap();
        println!("{}", path.display());
    }
}
