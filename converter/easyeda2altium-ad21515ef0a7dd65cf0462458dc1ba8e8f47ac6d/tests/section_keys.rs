//! Byte contracts independent of the production SectionKeys reader/writer.
//! AD / AltiumSharp: i32 count, then TWO [u32 size][u8 len][bytes] blocks.
use altium::{pcb,compound::CompoundFile};
const NAME:&str="LS_C365735_514e894f5fc1cbd19ecb";
const KEY:&str="P00000007";
fn library()->Vec<u8>{
 let mut lib=pcb::Library::default();lib.components.push(pcb::Component::new(NAME));lib.section_keys.insert(NAME.into(),KEY.into());lib.to_bytes().unwrap()
}
fn sized(value:&str)->Vec<u8>{let mut b=((value.len()+1) as u32).to_le_bytes().to_vec();b.push(value.len() as u8);b.extend_from_slice(value.as_bytes());b}
fn expected()->Vec<u8>{let mut b=1u32.to_le_bytes().to_vec();b.extend(sized(NAME));b.extend(sized(KEY));b}
fn replace(table:&[u8])->Vec<u8>{let mut cf=CompoundFile::open(library()).unwrap();cf.write_stream("SectionKeys",table).unwrap();cf.into_bytes().unwrap()}
#[test]fn writer_matches_ad_two_sized_blocks_instead_of_self_roundtrip(){
 let mut cf=CompoundFile::open(library()).unwrap();assert_eq!(cf.read_stream("SectionKeys").unwrap(),expected());
}
#[test]fn reader_accepts_an_independently_encoded_ad_section_table(){
 let lib=pcb::Library::from_bytes(replace(&expected())).unwrap();assert_eq!(lib.components.len(),1);assert_eq!(lib.components[0].name,NAME);assert_eq!(lib.section_keys[NAME],KEY);
}
#[test]fn rc3_bare_pascal_recovery_always_rewrites_to_ad_format(){
 let mut old=1u32.to_le_bytes().to_vec();old.push(NAME.len() as u8);old.extend_from_slice(NAME.as_bytes());old.extend(sized(KEY));
 let recovered=pcb::Library::from_bytes(replace(&old)).unwrap();assert_eq!(recovered.components.len(),1);
 let mut cf=CompoundFile::open(recovered.to_bytes().unwrap()).unwrap();assert_eq!(cf.read_stream("SectionKeys").unwrap(),expected());
}
#[test]fn truncated_and_oversized_tables_cannot_be_accepted_as_recovery(){
 let mut b=expected();b.truncate(8);assert!(pcb::Library::from_bytes(replace(&b)).is_err());
 let mut b=expected();b[4..8].copy_from_slice(&0xfffffff0u32.to_le_bytes());assert!(pcb::Library::from_bytes(replace(&b)).is_err());
 let mut b=expected();b[4..8].copy_from_slice(&1u32.to_le_bytes());assert!(pcb::Library::from_bytes(replace(&b)).is_err());
 let mut b=expected();b.pop();assert!(pcb::Library::from_bytes(replace(&b)).is_err());
}
#[test]fn extra_bytes_and_duplicate_references_are_rejected(){
 let mut b=expected();b.push(0);assert!(pcb::Library::from_bytes(replace(&b)).is_err());
 let mut b=expected();b[0]=2;b.extend(sized(NAME));b.extend(sized(KEY));assert!(pcb::Library::from_bytes(replace(&b)).is_err());
}
