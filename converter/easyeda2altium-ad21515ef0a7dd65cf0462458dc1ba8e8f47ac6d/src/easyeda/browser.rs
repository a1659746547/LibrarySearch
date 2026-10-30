//! Optional Windows browser fallback for a public product page requiring JS.
//! Uses a fresh isolated profile, bounded runtime/output, and a kill-on-close
//! job so cancelling the converter cannot leave browser children behind.
use std::path::Path;

#[cfg(not(windows))]
pub fn public_product_page(_url:&str,_cache:Option<&Path>)->Option<String>{None}

#[cfg(windows)]
pub fn public_product_page(url:&str,cache:Option<&Path>)->Option<String>{
    use std::{fs,process::{Command,Stdio},time::{Duration,Instant,SystemTime,UNIX_EPOCH},os::windows::{process::CommandExt,io::AsRawHandle}};
    if !url.starts_with("https://item.szlcsc.com/") || !url.ends_with(".html") {return None;}
    let browser=["ProgramFiles(x86)","ProgramFiles","LOCALAPPDATA"].iter().filter_map(std::env::var_os)
        .map(|base|std::path::PathBuf::from(base).join("Microsoft/Edge/Application/msedge.exe")).find(|path|path.is_file())?;
    let parent=cache.map(Path::to_path_buf).unwrap_or_else(std::env::temp_dir).join(".description-browser");
    let parent=if parent.is_absolute(){parent}else{std::env::current_dir().ok()?.join(parent)};
    fs::create_dir_all(&parent).ok()?;
    let stamp=SystemTime::now().duration_since(UNIX_EPOCH).ok()?.as_nanos();
    let folder=parent.join(format!("{}-{stamp}",std::process::id()));fs::create_dir(&folder).ok()?;
    struct Temporary(std::path::PathBuf);impl Drop for Temporary {fn drop(&mut self){let _=std::fs::remove_dir_all(&self.0);}}
    let _temporary=Temporary(folder.clone());
    let job=windows_job::Job::new()?;
    let output=folder.join("page.html");let file=fs::File::create(&output).ok()?;
    let mut child=Command::new(browser).args(["--headless=new","--no-first-run","--no-default-browser-check","--disable-gpu","--disable-extensions","--disable-background-networking","--dump-dom","--timeout=10000","--virtual-time-budget=5000"])
        .arg(format!("--user-data-dir={}",folder.join("profile").display())).arg(url)
        .current_dir(&folder).stdin(Stdio::null()).stdout(Stdio::from(file)).stderr(Stdio::null()).creation_flags(0x08000000).spawn().ok()?;
    if !job.assign(child.as_raw_handle()) {let _=child.kill();let _=child.wait();return None;}
    let timer=Instant::now();let mut success=false;
    while timer.elapsed()<Duration::from_secs(14) {
        if fs::metadata(&output).map(|m|m.len()>2*1024*1024).unwrap_or(false){break;}
        match child.try_wait(){Ok(Some(status))=>{success=status.success();break;},Ok(None)=>std::thread::sleep(Duration::from_millis(100)),Err(_)=>break}
    }
    // Close the entire job before profile cleanup, including on a timeout.
    drop(job);let _=child.wait();
    if !success || fs::metadata(&output).ok()?.len()>2*1024*1024 {return None;}
    fs::read_to_string(output).ok()
}

#[cfg(windows)]
mod windows_job {
    use std::ffi::c_void;
    type Handle=*mut c_void;
    #[repr(C)] #[derive(Default)] struct Basic {process_time:i64,job_time:i64,flags:u32,min_working:usize,max_working:usize,active:u32,affinity:usize,priority:u32,scheduling:u32}
    #[repr(C)] #[derive(Default)] struct Extended {basic:Basic,io:[u64;6],process_memory:usize,job_memory:usize,peak_process:usize,peak_job:usize}
    #[link(name="kernel32")] unsafe extern "system" {
        fn CreateJobObjectW(attributes:*mut c_void,name:*const u16)->Handle;
        fn SetInformationJobObject(job:Handle,class:i32,info:*const c_void,len:u32)->i32;
        fn AssignProcessToJobObject(job:Handle,process:Handle)->i32;
        fn CloseHandle(handle:Handle)->i32;
    }
    pub struct Job(Handle);
    impl Job {
        pub fn new()->Option<Self>{unsafe {
            let handle=CreateJobObjectW(std::ptr::null_mut(),std::ptr::null());if handle.is_null(){return None;}
            let mut info=Extended::default();info.basic.flags=0x2000;
            if SetInformationJobObject(handle,9,&info as *const _ as *const c_void,std::mem::size_of::<Extended>() as u32)==0{CloseHandle(handle);return None;}
            Some(Self(handle))
        }}
        pub fn assign(&self,process:Handle)->bool{unsafe {AssignProcessToJobObject(self.0,process)!=0}}
    }
    impl Drop for Job {fn drop(&mut self){unsafe {CloseHandle(self.0);}}}
}
