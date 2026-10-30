use altium::{compound::CompoundFile, pcb::Document};

fn streams(doc: &Document) -> (Vec<u8>, Vec<u8>) {
    let mut file = CompoundFile::open(doc.to_bytes().unwrap()).unwrap();
    (file.read_stream("UnionNames/Header").unwrap(), file.read_stream("UnionNames/Data").unwrap())
}

#[test]
fn empty_board_has_a_readable_union_name_count() {
    // Independent byte contract: the stream needs a u32 count even at zero.
    let (header, data) = streams(&Document::default());
    assert_eq!(header, 1u32.to_le_bytes());
    assert_eq!(data, 0u32.to_le_bytes());
}

#[test]
fn previously_generated_zero_byte_stub_is_normalized() {
    let mut doc = Document::default();
    doc.additional_streams.insert("UnionNames/Header".into(), vec![0; 4]);
    doc.additional_streams.insert("UnionNames/Data".into(), vec![]);
    assert_eq!(streams(&doc), (1u32.to_le_bytes().to_vec(), 0u32.to_le_bytes().to_vec()));
}

#[test]
fn existing_nonempty_union_payload_is_preserved() {
    let mut doc = Document::default();
    let opaque = vec![1, 0, 0, 0, 0x41, 0, 0x42, 0];
    doc.additional_streams.insert("UnionNames/Header".into(), vec![1, 0, 0, 0]);
    doc.additional_streams.insert("UnionNames/Data".into(), opaque.clone());
    assert_eq!(streams(&doc), (vec![1, 0, 0, 0], opaque));
}
