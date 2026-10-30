//! Authors deliverable files only. Does not launch AD or execute acceptance tests.
use std::collections::BTreeMap;
use std::path::PathBuf;
use altium::{sch,pcb,intlib::{IntegratedLibrary,NamedLibrary}};

#[tokio::main]
async fn main()->Result<(),Box<dyn std::error::Error>>{
    let args:Vec<String>=std::env::args().collect();
    if args.len()!=3{return Err("expected <existing-fixture-directory> <new-output-directory>".into());}
    let fixtures=PathBuf::from(&args[1]);let output=PathBuf::from(&args[2]);
    if output.exists(){return Err("Refusing to overwrite an existing project directory".into());}
    let libraries=output.join("Libraries");std::fs::create_dir_all(&libraries)?;
    let codes=["C51933293","C7473159","C7433","C6964"];
    let mut integrated=IntegratedLibrary::default();
    for code in codes {
        for extension in ["SchLib","PcbLib"]{
            std::fs::copy(fixtures.join(format!("{code}.{extension}")),libraries.join(format!("{code}.{extension}")))?;
        }
        integrated.schematic_libraries.push(NamedLibrary{name:format!("{code}.SchLib"),library:sch::Library::read(libraries.join(format!("{code}.SchLib"))).await?});
        integrated.footprint_libraries.push(NamedLibrary{name:format!("{code}.PcbLib"),library:pcb::Library::read(libraries.join(format!("{code}.PcbLib"))).await?});
    }
    let mut schematic=sch::Document::default();
    schematic.header_parameters=Some(BTreeMap::from([
        ("HEADER".into(),"Protel for Windows - Schematic Capture Binary File Version 5.0".into()),
        ("WEIGHT".into(),"2".into()),
    ]));
    schematic.sheet_settings=Some(BTreeMap::from([
        ("SheetStyle".into(),"0".into()),("BorderOn".into(),"T".into()),
        ("SnapGridOn".into(),"T".into()),("SnapGridSize".into(),"10".into()),
        ("VisibleGridOn".into(),"T".into()),("VisibleGridSize".into(),"10".into()),
        ("AreaColor".into(),"16317695".into()),("UniqueID".into(),"LSAUTOT1".into()),
    ]));
    schematic.write(output.join("Test.SchDoc")).await?;
    pcb::Document::default().write(output.join("Test.PcbDoc")).await?;
    let intdir=output.join("IntegratedSample");std::fs::create_dir_all(&intdir)?;
    integrated.write(intdir.join("Samples.IntLib")).await?;
    let mut documents=vec!["Test.SchDoc".to_string(),"Test.PcbDoc".to_string()];
    for code in codes {documents.push(format!("Libraries\\{code}.SchLib"));documents.push(format!("Libraries\\{code}.PcbLib"));}
    let mut project="[Design]\r\nVersion=1.0\r\nHierarchyMode=0\r\n\r\n".to_string();
    for (i,path) in documents.iter().enumerate(){project.push_str(&format!("[Document{}]\r\nDocumentPath={}\r\nAnnotationEnabled=1\r\nAnnotateOrder={}\r\n\r\n",i+1,path,i));}
    std::fs::write(output.join("LibrarySearch-Test.PrjPcb"),project)?;
    println!("Authored blank test project, 4 library pairs and separate IntLib sample at {}",output.display());
    println!("No AD launch, document operations or acceptance tests performed.");
    Ok(())
}
