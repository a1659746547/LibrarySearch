use easyeda2altium::{easyeda::parser::parse_footprint,convert::convert_footprint,slot_repair};
use altium::compound::CompoundFile;
use serde_json::json;

fn block(data:&[u8],pos:&mut usize)->(usize,usize){
    let len=(u32::from_le_bytes(data[*pos..*pos+4].try_into().unwrap())&0xffffff)as usize;
    *pos+=4;let start=*pos;*pos+=len;(start,len)
}

#[test]
fn slot_patch_changes_only_verified_angle_and_preserves_step_streams() {
    let root=std::env::temp_dir().join(format!("librarysearch-slot-contract-{}-{}",std::process::id(),std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&root).unwrap();
    for (index,(angle,rotation)) in [(0.0_f64,0.0),(90.0,0.0),(45.0,0.0),(135.0,30.0),(90.0,90.0)].into_iter().enumerate(){
        let dx=angle.to_radians().cos()*1.5;let dy=-angle.to_radians().sin()*1.5;
        let pad=format!("PAD~ELLIPSE~10~20~6~8~11~~1~0.5~~{rotation}~id~4~{} {} {} {}~Y",10.0-dx,20.0-dy,10.0+dx,20.0+dy);
        let cad=json!({"lcsc":{"number":"C1"},"packageDetail":{"dataStr":{"head":{"x":0,"y":0,"c_para":{"package":"SLOT"}},"shape":[pad]}}});
        let parsed=parse_footprint(&cad).unwrap();let component=convert_footprint(&parsed);
        let mut lib=altium::pcb::Library::default();lib.components.push(component);
        let mut cf=CompoundFile::open(lib.to_bytes().unwrap()).unwrap();
        let step=b"ISO-10303-21;HEADER;ENDSEC;DATA;ENDSEC;END-ISO-10303-21;";
        cf.write_stream("Library/Models/0",step).unwrap();
        let original=cf.into_bytes().unwrap();
        let mut cf=CompoundFile::open(original.clone()).unwrap();let before=cf.read_stream("SLOT/Data").unwrap();
        let mut pos=0;block(&before,&mut pos);assert_eq!(before[pos],2);pos+=1;
        for _ in 0..5 {block(&before,&mut pos);}
        let (shape,size)=block(&before,&mut pos);assert!(size>=596);
        let target=shape+267;let expected=(angle-rotation).rem_euclid(180.0);
        let library=root.join(format!("slot-{index}.PcbLib"));let source=root.join(format!("slot-{index}.json"));
        std::fs::write(&library,&original).unwrap();std::fs::write(&source,json!({"success":true,"result":cad}).to_string()).unwrap();
        assert_eq!(slot_repair::repair(&source,&library,"C1").unwrap(),1);
        let bytes=std::fs::read(&library).unwrap();let mut after_cf=CompoundFile::open(bytes.clone()).unwrap();let after=after_cf.read_stream("SLOT/Data").unwrap();
        assert_eq!(after_cf.read_stream("Library/Models/0").unwrap(),step);
        assert_eq!(before.len(),after.len());
        assert!(before.iter().zip(&after).enumerate().all(|(i,(a,b))|a==b||(target..target+8).contains(&i)));
        assert!((f64::from_le_bytes(after[target..target+8].try_into().unwrap())-expected).abs()<1e-8);
        slot_repair::repair(&source,&library,"C1").unwrap();assert_eq!(std::fs::read(&library).unwrap(),bytes);
        for invalid in ["", "1 2 1 3"] {
            let mut bad:serde_json::Value=serde_json::from_slice(&std::fs::read(&source).unwrap()).unwrap();
            let text=bad["result"]["packageDetail"]["dataStr"]["shape"][0].as_str().unwrap();
            let mut parts:Vec<String>=text.split('~').map(String::from).collect();parts[14]=invalid.into();
            bad["result"]["packageDetail"]["dataStr"]["shape"][0]=parts.join("~").into();
            std::fs::write(&source,bad.to_string()).unwrap();
            assert!(slot_repair::repair(&source,&library,"C1").is_err());assert_eq!(std::fs::read(&library).unwrap(),bytes);
            std::fs::write(&source,json!({"success":true,"result":cad}).to_string()).unwrap();
        }
    }
    std::fs::remove_dir_all(root).unwrap();
}
