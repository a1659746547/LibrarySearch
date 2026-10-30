use altium::compound::CompoundFile;
use std::path::Path;
fn main()->Result<(),Box<dyn std::error::Error>>{
 let args:Vec<_>=std::env::args().collect();let mut cf=CompoundFile::open(std::fs::read(&args[1])?)?;
 std::fs::create_dir_all(&args[2])?;
 for entry in cf.list_children("")? {if entry.is_storage {println!("{}",entry.name);}}
 for path in &args[3..]{let bytes=cf.read_stream(path)?;std::fs::write(Path::new(&args[2]).join(path.replace('/',"_")),bytes)?;}
 Ok(())
}
