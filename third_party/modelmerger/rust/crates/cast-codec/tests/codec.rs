use cast_codec::{
    CastFile, CastNode, CastProperty, CodecError, DecodeLimits, DecodeResource, PropertyValues,
};
use std::path::Path;

#[test]
fn decode_minimal_root_preserves_header_and_node_identity() {
    let bytes = [
        b'c', b'a', b's', b't', 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, b'r', b'o', b'o', b't', 24, 0,
        0, 0, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    let file = CastFile::decode(&bytes).expect("minimal Cast file should decode");

    assert_eq!(1, file.version);
    assert_eq!(0, file.flags);
    assert_eq!(1, file.roots.len());
    assert_eq!(u32::from_le_bytes(*b"root"), file.roots[0].identifier);
    assert_eq!(0x0102_0304_0506_0708, file.roots[0].hash);
    assert!(file.roots[0].properties.is_empty());
    assert!(file.roots[0].children.is_empty());
}

#[test]
fn string_property_round_trips_without_changing_the_wire_format() {
    let bytes = [
        b'c', b'a', b's', b't', 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, b'm', b'o', b'd', b'l', 38, 0,
        0, 0, 8, 7, 6, 5, 4, 3, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, b's', 0, 1, 0, 1, 0, 0, 0, b'n',
        b'b', b'o', b'd', b'y', 0,
    ];

    let file = CastFile::decode(&bytes).expect("string property should decode");
    let property = &file.roots[0].properties[0];

    assert_eq!("n", property.name);
    assert_eq!(PropertyValues::String("body".to_owned()), property.values);
    assert_eq!(
        bytes,
        file.encode().expect("Cast file should encode").as_slice()
    );
}

#[test]
fn csharp_golden_model_round_trips_with_all_model_property_shapes() {
    let fixture = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../tests/fixtures/rust-migration/golden-small/part-00.cast");
    let bytes = std::fs::read(fixture).expect("C# golden Cast fixture should exist");

    let file = CastFile::decode(&bytes).expect("C# golden Cast fixture should decode");

    let root = &file.roots[0];
    let model = child(root, *b"modl");
    let skeleton = child(model, *b"skel");
    let first_bone = child(skeleton, *b"bone");
    assert_eq!(
        Some(&PropertyValues::String("root".to_owned())),
        property(first_bone, "n")
    );
    assert_eq!(
        Some(&PropertyValues::Integer32(vec![u32::MAX])),
        property(first_bone, "p")
    );

    let mesh = child(model, *b"mesh");
    assert!(
        matches!(property(mesh, "vp"), Some(PropertyValues::Vector3(values)) if values.len() == 12)
    );
    assert!(
        matches!(property(mesh, "vn"), Some(PropertyValues::Vector3(values)) if values.len() == 12)
    );
    assert!(
        matches!(property(mesh, "f"), Some(PropertyValues::Byte(values)) if values.len() == 12)
    );
    assert!(
        matches!(property(mesh, "wv"), Some(PropertyValues::Float(values)) if values.len() == 12)
    );
    assert!(
        matches!(property(mesh, "m"), Some(PropertyValues::Integer64(values)) if values.len() == 1)
    );
    assert!(
        matches!(property(mesh, "u0"), Some(PropertyValues::Vector2(values)) if values.len() == 12)
    );
    assert!(
        matches!(property(first_bone, "lr"), Some(PropertyValues::Vector4(values)) if values.len() == 1)
    );
    assert_eq!(
        bytes,
        file.encode()
            .expect("C# golden Cast fixture should re-encode")
    );
}

#[test]
fn every_truncated_prefix_of_a_golden_file_is_rejected() {
    let fixture = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../tests/fixtures/rust-migration/golden-small/part-00.cast");
    let bytes = std::fs::read(fixture).unwrap();

    for length in 0..bytes.len() {
        assert!(
            CastFile::decode(&bytes[..length]).is_err(),
            "truncated prefix of length {length} unexpectedly decoded"
        );
    }
}

#[test]
fn trailing_data_and_incorrect_node_size_are_rejected() {
    let mut bytes = [
        b'c', b'a', b's', b't', 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, b'r', b'o', b'o', b't', 24, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ]
    .to_vec();
    bytes.push(0xff);
    assert_eq!(Err(CodecError::TrailingData), CastFile::decode(&bytes));

    bytes.pop();
    bytes[20] = 23;
    assert_eq!(
        Err(CodecError::InvalidNodeSize(23)),
        CastFile::decode(&bytes)
    );
}

#[test]
fn excessive_node_nesting_is_rejected() {
    let mut node = CastNode {
        identifier: u32::from_le_bytes(*b"root"),
        hash: 0,
        properties: Vec::new(),
        children: Vec::new(),
    };
    for _ in 0..256 {
        node = CastNode {
            identifier: u32::from_le_bytes(*b"root"),
            hash: 0,
            properties: Vec::new(),
            children: vec![node],
        };
    }
    let bytes = CastFile {
        version: 1,
        flags: 0,
        roots: vec![node],
    }
    .encode()
    .unwrap();

    assert_eq!(Err(CodecError::NestingTooDeep), CastFile::decode(&bytes));
}

#[test]
fn cancellable_decode_stops_during_a_large_property() {
    let bytes = CastFile {
        version: 1,
        flags: 0,
        roots: vec![CastNode {
            identifier: u32::from_le_bytes(*b"root"),
            hash: 0,
            properties: vec![CastProperty {
                name: "data".to_owned(),
                values: PropertyValues::Integer32(vec![0; 20_000]),
            }],
            children: Vec::new(),
        }],
    }
    .encode()
    .unwrap();
    let mut checks = 0;

    let error = CastFile::decode_with_cancel(&bytes, || {
        checks += 1;
        checks >= 5
    })
    .unwrap_err();

    assert_eq!(CodecError::Cancelled, error);
}

#[test]
fn configurable_decode_budgets_reject_excessive_allocations_before_decoding() {
    let bytes = CastFile {
        version: 1,
        flags: 0,
        roots: vec![CastNode {
            identifier: u32::from_le_bytes(*b"root"),
            hash: 0,
            properties: vec![CastProperty {
                name: "data".to_owned(),
                values: PropertyValues::Integer32(vec![1, 2]),
            }],
            children: Vec::new(),
        }],
    }
    .encode()
    .unwrap();

    let node_error = CastFile::decode_with_limits(
        &bytes,
        DecodeLimits {
            max_nodes: 0,
            ..DecodeLimits::default()
        },
    )
    .unwrap_err();
    assert_eq!(
        CodecError::ResourceLimitExceeded(DecodeResource::Nodes),
        node_error
    );

    let property_error = CastFile::decode_with_limits(
        &bytes,
        DecodeLimits {
            max_properties: 0,
            ..DecodeLimits::default()
        },
    )
    .unwrap_err();
    assert_eq!(
        CodecError::ResourceLimitExceeded(DecodeResource::Properties),
        property_error
    );

    let value_error = CastFile::decode_with_limits(
        &bytes,
        DecodeLimits {
            max_value_bytes: 4,
            ..DecodeLimits::default()
        },
    )
    .unwrap_err();
    assert_eq!(
        CodecError::ResourceLimitExceeded(DecodeResource::ValueBytes),
        value_error
    );
}

fn child(parent: &CastNode, identifier: [u8; 4]) -> &CastNode {
    parent
        .children
        .iter()
        .find(|node| node.identifier == u32::from_le_bytes(identifier))
        .expect("expected child node")
}

fn property<'a>(node: &'a CastNode, name: &str) -> Option<&'a PropertyValues> {
    node.properties
        .iter()
        .find(|property| property.name == name)
        .map(|property| &property.values)
}
