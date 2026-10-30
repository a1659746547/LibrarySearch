use altium::{compound::CompoundFile,sch::Library};
use easyeda2altium::{convert::symbol::convert_symbol,easyeda::{parser::parse_symbol_shape,types::EeSymbol}};

fn source()->EeSymbol {let mut s=EeSymbol::default();s.info.name="PinStateWidth".into();s.info.prefix="R?".into();s}
fn records(library:&Library)->Vec<(u8,Vec<u8>)>{
 let mut cf=CompoundFile::open(library.to_bytes().unwrap()).unwrap();let data=cf.read_stream("PinStateWidth/Data").unwrap();let mut offset=0;let mut out=vec![];
 while offset<data.len(){let size=u32::from_le_bytes(data[offset..offset+4].try_into().unwrap());let length=(size&0x00ff_ffff) as usize;out.push(((size>>24) as u8,data[offset+4..offset+4+length].to_vec()));offset+=4+length;}
 out
}
fn library(source:&EeSymbol)->Library{let mut library=Library::default();library.components.push(convert_symbol(source,&[],false));library}
fn field(text:&str,key:&str)->i32{text.split('|').filter_map(|pair|pair.split_once('=')).find(|(name,_)|name.eq_ignore_ascii_case(key)).map(|(_,v)|v.trim_end_matches('\0').parse().unwrap()).unwrap_or(0)}

#[test]
fn normal_pin_binary_marker_and_source_state_are_independent(){
 let mut source=source();
 for shape in [
  "P~show~0~1~0~0~180~pin1~0^^0~0^^M0 0 h10~#000^^0~12~0~0~1~start~~~#000^^0~5~-2~0~1~end~~~#000^^false~7~0^^false~M10 3L13 0L10 -3",
  "P~show~1~2~40~0~0~pin2~0^^40~0^^M40 0 h-10~#000^^1~28~0~0~IN~end~~~#000^^1~35~-2~0~2~start~~~#000^^0~33~0^^0~M30 -3L27 0L30 3",
  "P~hide~4~3~0~20~180~pin3~0^^0~20^^M0 20 h10~#000^^0~12~20~0~PWR~start~~~#000^^0~5~18~0~3~end~~~#000^^1~7~20^^1~M10 23L13 20L10 17",
 ] {parse_symbol_shape(shape,&mut source);}
 let library=library(&source);let rows=records(&library);let pins:Vec<_>=rows.iter().filter(|(flag,_)|*flag==1).collect();assert_eq!(pins.len(),3);
 for ((_,body),electrical) in pins.iter().zip([4,0,7]){let n=body[12] as usize;assert_eq!(body[13+n],1,"AD normal pin FormalType must be one");assert_eq!(body[14+n],electrical,"electrical source mapping is unchanged");}
 assert_eq!(&pins[0].1[8..12],&[0,0,0,0],"false decorations must stay normal");assert_eq!(pins[0].1[15]&0x18,0,"source-hidden labels stay hidden");
 assert_eq!(pins[1].1[15]&0x18,0x18,"visible source labels stay visible");assert_eq!(&pins[2].1[8..12],&[3,1,0,0],"only explicit source clock/inversion decorations survive");assert_eq!(pins[2].1[15]&4,4,"source hidden-pin flag is encoded in binary flags");
 let reopened=Library::from_bytes(library.to_bytes().unwrap()).unwrap();assert!(reopened.components[0].pins[2].is_hidden);
}

#[test]
fn source_strokes_write_ad_small_medium_large_instead_of_zero(){
 for width in [1,2,3] {
  let mut source=source();for shape in [format!("R~10~-4~~~20~8~#880000~{width}~0~none~rect~0"),format!("PL~0 0 10 0~#880000~{width}~0~none~line~0"),format!("A~M10,0 A10,10 0 0 1 0,10~~#880000~{width}~0~none~arc~0"),format!("C~0~0~4~#880000~{width}~0~none~circle~0"),format!("E~0~0~4~3~#880000~{width}~0~none~ellipse~0")]{parse_symbol_shape(&shape,&mut source);}
  let rows=records(&library(&source));let mut checked=0;
  for(flag,bytes) in rows {if flag!=0{continue;}let text=String::from_utf8(bytes).unwrap();if [6,8,12,14].contains(&field(&text,"RECORD")){assert_eq!(field(&text,"LINEWIDTH"),width,"native AD width enum includes Zero=0: {text}");checked+=1;}}
  assert_eq!(checked,5);
 }
}
