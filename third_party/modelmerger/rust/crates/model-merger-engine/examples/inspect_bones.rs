use cast_codec::{CastFile, CastNode};
fn walk(node: &CastNode) {
    if node.identifier == u32::from_le_bytes(*b"mesh") {
        for p in &node.properties {
            if p.name == "vp"
                && let cast_codec::PropertyValues::Vector3(v) = &p.values
            {
                println!("MESH vertices={}", v.len());
            }
            if p.name == "wb"
                && let cast_codec::PropertyValues::Integer32(v) = &p.values
            {
                println!(
                    "BINDINGS {:?}",
                    v.iter().collect::<std::collections::HashSet<_>>()
                );
            }
        }
    }
    if node.identifier == u32::from_le_bytes(*b"bone") {
        println!("{:?}", node.properties);
    }
    for child in &node.children {
        walk(child);
    }
}
fn main() {
    for path in std::env::args().skip(1) {
        println!("FILE {path}");
        let file = CastFile::decode(&std::fs::read(path).unwrap()).unwrap();
        for root in &file.roots {
            walk(root);
        }
    }
}
