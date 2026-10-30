//! Same-length edits of pad hole-angle fields. Never reserialize the entire
//! footprint: STEP resources, text, masks and unknown records stay untouched.
use std::{io::{Read,Write,Seek,SeekFrom},path::Path};
use crate::{Result,Error,easyeda::{types::EeFootprintPad,parser::parse_footprint},convert::footprint::{slot_rotation,normalize_pad_number}};
use serde_json::Value;

fn problem(message:impl Into<String>)->Error{Error::Convert(message.into())}
fn u32_at(data:&[u8],at:usize)->Result<u32>{let b=data.get(at..at+4).ok_or_else(||problem("truncated PCB block"))?;Ok(u32::from_le_bytes(b.try_into().unwrap()))}
fn i32_at(data:&[u8],at:usize)->Result<i32>{Ok(u32_at(data,at)? as i32)}
fn f64_at(data:&[u8],at:usize)->Result<f64>{let b=data.get(at..at+8).ok_or_else(||problem("truncated PCB angle"))?;let v=f64::from_le_bytes(b.try_into().unwrap());if !v.is_finite(){return Err(problem("non-finite PCB angle"));}Ok(v)}
fn block<'a>(data:&'a[u8],position:&mut usize)->Result<(usize,&'a[u8])>{let size=(u32_at(data,*position)?&0x00ff_ffff)as usize;*position+=4;let start=*position;let bytes=data.get(start..start+size).ok_or_else(||problem("PCB block exceeds stream"))?;*position+=size;Ok((start,bytes))}
fn near(a:f64,b:f64)->bool{(a-b).abs()<0.002}

pub fn patch_stream(data:&[u8],pads:&[EeFootprintPad],ox:f64,oy:f64)->Result<(Vec<u8>,usize)>{
    let slots:Vec<_>=pads.iter().filter(|p|p.hole_length>0.0&&p.hole_radius>0.0).collect();
    if slots.is_empty(){return Ok((data.to_vec(),0));}
    let mut output=data.to_vec();let mut position=0;block(data,&mut position)?;
    let mut found=vec![false;slots.len()];let mut count=0;
    while position<data.len(){
        let kind=data[position];position+=1;
        if kind==2{
            let(_,name)=block(data,&mut position)?;let name=if let Some(&len)=name.first(){std::str::from_utf8(name.get(1..1+len as usize).ok_or_else(||problem("invalid pad name"))?).map_err(|_|problem("invalid pad name encoding"))?}else{return Err(problem("empty pad name block"));};
            for _ in 0..3{block(data,&mut position)?;}
            let(_,main)=block(data,&mut position)?;let(shape_start,shape)=block(data,&mut position)?;
            if main.len()<60{return Err(problem("unsupported pad main record"));}
            // Raw units = 10,000 units/mil; one source pixel = 10 mil.
            let x=i32_at(main,13)?as f64/100000.0;let y=i32_at(main,17)?as f64/100000.0;
            for(index,p)in slots.iter().enumerate(){
                if normalize_pad_number(&p.number)!=name||!near(x,p.center_x-ox)||!near(y,-(p.center_y-oy)){continue;}
                if found[index]{return Err(problem(format!("duplicate target pad {name}")));}
                if shape.len()<596||shape[262]!=2{return Err(problem(format!("unsupported slot record {name}")));}
                if !near(i32_at(main,45)?as f64/100000.0,p.hole_radius*2.0)||!near(i32_at(shape,263)?as f64/100000.0,p.hole_length){return Err(problem(format!("slot dimensions differ from source for pad {name}")));}
                let points=p.hole_points.split_whitespace().map(str::parse::<f64>).collect::<std::result::Result<Vec<_>,_>>().map_err(|_|problem("invalid source slot points"))?;
                if points.len()!=4||!near((points[0]+points[2])/2.0,p.center_x)||!near((points[1]+points[3])/2.0,p.center_y){return Err(problem(format!("offset or invalid slot {name}; manual review required")));}
                let angle=slot_rotation(p,f64_at(main,52)?)?;let offset=shape_start+267;
                output[offset..offset+8].copy_from_slice(&angle.to_le_bytes());found[index]=true;count+=1;
            }
        }else{
            let blocks=match kind{1|3|4|6|11|12=>1,5=>2,_=>return Err(problem(format!("unsupported PCB primitive {kind}")))};
            for _ in 0..blocks{block(data,&mut position)?;}
        }
    }
    if found.iter().any(|v|!*v){return Err(problem("some source slots were not found in the footprint; refusing partial repair"));}
    Ok((output,count))
}

pub fn repair(source:&Path,library:&Path,code:&str)->Result<usize>{
    let bytes=std::fs::read(source)?;if bytes.len()>64*1024*1024{return Err(problem("source JSON too large"));}
    let envelope:Value=serde_json::from_slice(&bytes)?;let cad=envelope.get("result").ok_or_else(||problem("missing source result"))?;
    if cad.pointer("/lcsc/number").and_then(Value::as_str)!=Some(code){return Err(problem("source C number does not match requested component"));}
    let fp=parse_footprint(cad)?;let slots=fp.pads.iter().filter(|p|p.hole_length>0.0&&p.hole_radius>0.0).count();if slots==0{return Ok(0);}
    let file=std::fs::OpenOptions::new().read(true).write(true).open(library)?;let mut cf=cfb::CompoundFile::open(file)?;
    let paths:Vec<_>=cf.walk().filter(|e|e.is_stream()).map(|e|e.path().to_path_buf()).filter(|p|p.file_name().is_some_and(|v|v=="Data")&&p.components().count()==3&&p.parent().and_then(Path::file_name).is_some_and(|v|v!="Library"&&v!="FileVersionInfo")).collect();
    if paths.len()!=1{return Err(problem("expected exactly one footprint data stream"));}
    let path=&paths[0];let mut before=Vec::new();cf.open_stream(path)?.read_to_end(&mut before)?;
    let(after,count)=patch_stream(&before,&fp.pads,fp.bbox_x,fp.bbox_y)?;
    if count!=slots{return Err(problem("slot repair count mismatch"));}
    if before!=after{let mut stream=cf.open_stream(path)?;stream.seek(SeekFrom::Start(0))?;stream.write_all(&after)?;stream.flush()?;}
    cf.flush()?;Ok(count)
}

#[cfg(test)]mod tests{
 use super::*;
 fn pad(points:&str)->EeFootprintPad{EeFootprintPad{number:"1".into(),center_x:1.0,center_y:2.0,hole_length:4.0,hole_radius:0.5,hole_points:points.into(),..Default::default()}}
 #[test]fn slot_directions_and_pad_rotation(){for(points,rotation,expected)in[("0 2 2 2",0.0,0.0),("1 1 1 3",0.0,90.0),("0 1 2 3",0.0,135.0),("0 3 2 1",30.0,15.0),("1 1 1 3",90.0,0.0),("1 3 1 1",0.0,90.0)]{assert!((slot_rotation(&pad(points),rotation).unwrap()-expected).abs()<1e-9);}}
 #[test]fn reject_invalid_slot_endpoints(){for points in["","1 2 1 2","NaN 0 1 2","1 2 3","a b c d"]{assert!(slot_rotation(&pad(points),0.0).is_err());}}
 #[test]fn no_slot_is_unchanged(){let data=vec![1,2,3];assert_eq!(patch_stream(&data,&[],0.0,0.0).unwrap(),(data,0));}
 #[test]fn malformed_stream_is_rejected(){assert!(patch_stream(&[255,0,0],&[pad("1 1 1 3")],0.0,0.0).is_err());}
}
