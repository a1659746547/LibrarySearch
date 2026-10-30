use clap::Parser;
use std::path::PathBuf;
#[derive(Parser)]struct Args{
 #[arg(long)]source:Option<PathBuf>,
 #[arg(long)]library:Option<PathBuf>,
 #[arg(long)]lcsc_id:String,
 #[arg(long)]prepare:bool,
 #[arg(long)]cache_dir:Option<PathBuf>,
 #[arg(long)]refresh:bool,
}
#[tokio::main]async fn main(){let args=Args::parse();if args.lcsc_id.len()<2||args.lcsc_id.len()>13||!args.lcsc_id.starts_with('C')||!args.lcsc_id[1..].chars().all(|c|c.is_ascii_digit()){eprintln!("invalid C number");std::process::exit(1);}
 let result=if args.prepare{if let Some(cache)=&args.cache_dir{easyeda2altium::source_cache::prepare(cache,&args.lcsc_id,args.refresh).await.map(|_|0)}else{Err(easyeda2altium::Error::InvalidArg("--cache-dir is required".into()))}}else if let(Some(source),Some(library))=(&args.source,&args.library){easyeda2altium::slot_repair::repair(source,library,&args.lcsc_id)}else{Err(easyeda2altium::Error::InvalidArg("--source and --library are required".into()))};
 match result{Ok(count)=>{if args.prepare{eprintln!("source-cache: ready");}else{eprintln!("slot-repair: verified {count} slots");}},Err(error)=>{eprintln!("slot-repair error: {error}");std::process::exit(1);}}
}
