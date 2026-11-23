using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace LibrarySearch {
 internal static class UnifiedLibrary {
  static readonly object gate=new object();static string converter;
  internal static bool Enabled {get{return !string.IsNullOrWhiteSpace(converter);}}
  internal static void Configure(string executable){converter=File.Exists(executable)?Path.GetFullPath(executable):null;}
  internal static string SymbolName(LibraryPackage p){if(p.SymbolHash==null||p.SymbolHash.Length<12||p.FootprintHash==null||p.FootprintHash.Length<12)throw new IOException("缓存校验信息不完整，请重新获取元件。");return "LS_"+LibraryStore.Normalize(p.Code)+"_"+p.SymbolHash.Substring(0,12)+p.FootprintHash.Substring(0,12);}
  internal static string FootprintName(LibraryPackage p){if(p.FootprintHash==null||p.FootprintHash.Length<20)throw new IOException("缓存校验信息不完整，请重新获取元件。");return "LS_"+LibraryStore.Normalize(p.Code)+"_"+p.FootprintHash.Substring(0,20);}
  public sealed class MergeInput {public string Code,SymbolName,SymbolAlias,FootprintName,FootprintAlias,SymbolPath,FootprintPath;}
  public sealed class MergeSummary {public int SchemaVersion,Symbols,Footprints,Models;}
  public sealed class State {public int SchemaVersion;public string Signature,SymbolHash,FootprintHash;public int Symbols,Footprints,Models;public string[] SkippedCodes;}
  public sealed class Journal {public State Previous,Next;}
  sealed class ChangedLibraryException:IOException {
   internal ChangedLibraryException(string path):base("库文件与生成记录不一致，已保留原文件："+path){}
  }
  sealed class FolderLock:IDisposable {
   readonly Mutex mutex;
   internal FolderLock(string folder,CancellationToken token){
    mutex=new Mutex(false,"Local\\LibrarySearchUnified_"+Digest(Path.GetFullPath(folder).ToUpperInvariant()));
    try{if(WaitHandle.WaitAny(new WaitHandle[]{mutex,token.WaitHandle})==1){mutex.Dispose();throw new OperationCanceledException(token);}}
    catch(AbandonedMutexException){} // Recover a journal left by the previous process.
   }
   public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
  }
  static string Digest(string value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
  static string Library(string folder,bool symbol,string stem="LibrarySearch"){return Path.Combine(folder,stem+"."+(symbol?"SchLib":"PcbLib"));}
  static string Backup(string folder,bool symbol){return Path.Combine(folder,"rollback."+(symbol?"SchLib":"PcbLib"));}
  static string StatePath(string folder){return Path.Combine(folder,"state.json");}
  static bool IsRedirected(string path){
   try{return (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;}
   catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
  }
  static void CheckManagedPaths(string folder){
   // The chosen library/project root and its ancestors may legitimately be
   // junctions (moved Desktop/Documents). Only plugin-owned children are guarded.
   if(IsRedirected(folder))throw new IOException("总库子目录指向其他位置，未修改文件："+folder);
   foreach(string name in new[]{"LibrarySearch.SchLib","LibrarySearch.PcbLib","state.json","pending.json","rollback.SchLib","rollback.PcbLib"}){
    string path=Path.Combine(folder,name);if(IsRedirected(path))throw new IOException("总库文件指向其他位置，未修改文件："+path);
   }
  }
  static string ProjectFolder(string projectPath){
   string folder=Path.GetDirectoryName(Path.GetFullPath(projectPath));
   foreach(string child in new[]{"Libraries","LibrarySearch","Unified"}){
    folder=Path.Combine(folder,child);
    if(IsRedirected(folder))throw new IOException("工程库子目录指向其他位置，未修改文件："+folder);
   }
   return folder;
  }
  static bool Matches(string path,string hash){return File.Exists(path)&&hash!=null&&LibraryStore.Hash(path).Equals(hash,StringComparison.OrdinalIgnoreCase);}
  static State ReadState(string folder){var path=StatePath(folder);if(!File.Exists(path))return null;var state=Json.Read<State>(path);if(state==null||state.SchemaVersion!=1)throw new IOException("总库记录格式无效，请保留文件并导出反馈。");return state;}
  static void Recover(string folder,string stem="LibrarySearch"){
   CheckManagedPaths(folder);
   string pending=Path.Combine(folder,"pending.json");if(!File.Exists(pending))return;
   var journal=Json.Read<Journal>(pending);if(journal==null||journal.Next==null||journal.Next.SchemaVersion!=1)throw new IOException("总库回滚记录无效，请导出反馈。");
   foreach(bool symbol in new[]{false,true}){
    string path=Library(folder,symbol,stem),next=symbol?journal.Next.SymbolHash:journal.Next.FootprintHash;
    string previous=journal.Previous==null?null:(symbol?journal.Previous.SymbolHash:journal.Previous.FootprintHash);
    if(previous!=null&&Matches(path,previous))continue;
    if(File.Exists(path)&&!Matches(path,next))throw new IOException("总库文件已被外部修改，未覆盖；请导出反馈。");
    if(previous==null){if(File.Exists(path))File.Delete(path);}
    else{if(!Matches(Backup(folder,symbol),previous))throw new IOException("总库回滚副本缺失，请导出反馈。");File.Copy(Backup(folder,symbol),path,true);}
   }
   if(journal.Previous==null){if(File.Exists(StatePath(folder)))File.Delete(StatePath(folder));}
   else Json.WriteAtomic(StatePath(folder),journal.Previous);
   File.Delete(pending);foreach(bool symbol in new[]{false,true})if(File.Exists(Backup(folder,symbol)))File.Delete(Backup(folder,symbol));
  }
  static State VerifyState(string folder){
   Recover(folder);var state=ReadState(folder);
   if(state==null){if(File.Exists(Library(folder,true))||File.Exists(Library(folder,false)))throw new IOException("总库位置已有其他库文件，未覆盖。请调整保存目录或导出反馈。");}
   else foreach(bool symbol in new[]{true,false}){
    string path=Library(folder,symbol);
    if(!File.Exists(path))throw new IOException("总库文件缺失："+path);
    if(!Matches(path,symbol?state.SymbolHash:state.FootprintHash))throw new ChangedLibraryException(path);
   }
   return state;
  }
  static void Commit(string folder,string generated,State previous,State next,string stem){
   Directory.CreateDirectory(folder);
   foreach(bool symbol in new[]{false,true})if(previous!=null){
    string path=Library(folder,symbol,stem),hash=symbol?previous.SymbolHash:previous.FootprintHash;
    if(!Matches(path,hash))throw new ChangedLibraryException(path);
    File.Copy(path,Backup(folder,symbol),true);
    if(!Matches(Backup(folder,symbol),hash))throw new ChangedLibraryException(path);
   }
   string pending=Path.Combine(folder,"pending.json");Json.WriteAtomic(pending,new Journal{Previous=previous,Next=next});
   try{
    foreach(bool symbol in new[]{false,true}){
     string input=Path.ChangeExtension(generated,symbol?"SchLib":"PcbLib"),path=Library(folder,symbol,stem);
     if(previous!=null && (symbol?previous.SymbolHash:previous.FootprintHash)==(symbol?next.SymbolHash:next.FootprintHash))continue;
     if(File.Exists(path))File.Replace(input,path,null);else File.Move(input,path);
    }
    Json.WriteAtomic(StatePath(folder),next);File.Delete(pending);
   }catch{Recover(folder,stem);throw;}
   foreach(bool symbol in new[]{false,true})if(File.Exists(Backup(folder,symbol)))File.Delete(Backup(folder,symbol));
  }
  static State Build(string folder,string signature,MergeInput[] entries,State previous,CancellationToken token,Action<string> progress,string[] skippedCodes=null,string stem="LibrarySearch"){
   Directory.CreateDirectory(folder);string run=Path.Combine(folder,".m-"+Guid.NewGuid().ToString("N").Substring(0,16));Directory.CreateDirectory(run);
   try{
    string request=Path.Combine(run,"request.json"),output=Path.Combine(run,"LibrarySearch");
    // Private staging request has no readers until the process starts. Avoid
    // atomic-write suffixes here: legacy AD hosts still use MAX_PATH limits.
    File.WriteAllText(request,new LibrarySearch.Serialization.JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(new {ModelNamespace=Guid.NewGuid().ToString("N"),Entries=entries}),new UTF8Encoding(false));
    if(progress!=null)progress("正在更新已下载元件总库…");
    var info=new ProcessStartInfo(converter,"--merge-libraries "+AutomaticLocalIndex.Quote(request)+" --output "+AutomaticLocalIndex.Quote(output)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=run,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    using(var process=Process.Start(info)){
     var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
     try{
      while(!process.WaitForExit(100)){token.ThrowIfCancellationRequested();if(timer.Elapsed.TotalMinutes>5)throw new IOException("整理总库耗时过长，请导出反馈。");}
      token.ThrowIfCancellationRequested();string log=stderr.GetAwaiter().GetResult()+stdout.GetAwaiter().GetResult();
      UserLog.Write("Unified library exit="+process.ExitCode+" elapsed_ms="+timer.ElapsedMilliseconds+" "+log);
      if(process.ExitCode!=0)throw new IOException("总库更新未完成；原总库保留。请关闭 AD 中打开的总库文件后重试，或导出反馈。");
     }finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch{}}
    }
    var summary=Json.Read<MergeSummary>(output+".merged.json");if(summary==null||summary.SchemaVersion!=1||summary.Symbols<0||summary.Footprints<0||summary.Models<0)throw new IOException("总库转换结果不完整。");
    LibraryStore.CheckLibrary(output+".SchLib");LibraryStore.CheckLibrary(output+".PcbLib");
    var next=new State{SchemaVersion=1,Signature=signature,SymbolHash=LibraryStore.Hash(output+".SchLib"),FootprintHash=LibraryStore.Hash(output+".PcbLib"),Symbols=summary.Symbols,Footprints=summary.Footprints,Models=summary.Models,SkippedCodes=skippedCodes??new string[0]};
    token.ThrowIfCancellationRequested();Commit(folder,output,previous,next,stem);return next;
   }finally{try{if(Directory.Exists(run))Directory.Delete(run,true);}catch(Exception error){UserLog.Write("Unified library staging cleanup",error);}}
  }
  static string Folder(LibraryStore store){return LibraryStore.ContainedPath(store.Root,"Unified");}
  static MergeInput[] CollectInputs(LibraryStore store,CancellationToken token,out string signature,out string[] skipped){
   var inputs=new List<MergeInput>();var invalid=new List<string>();var key=new StringBuilder("unified-library-7-pcb-storage\n");string index=Path.Combine(store.Root,"Index");
   foreach(string file in Directory.Exists(index)?Directory.GetFiles(index,"C*.json").OrderBy(p=>p,StringComparer.OrdinalIgnoreCase):Enumerable.Empty<string>()){
    token.ThrowIfCancellationRequested();string code=Path.GetFileNameWithoutExtension(file);
    try{
     var package=Json.Read<LibraryPackage>(file);if(package==null||code!=package.Code)throw new IOException("已下载编号索引不匹配。");
     var records=store.Records(package,true);
     inputs.Add(new MergeInput{Code=package.Code,SymbolName=package.SymbolName,SymbolAlias=SymbolName(package),FootprintName=package.FootprintName,FootprintAlias=FootprintName(package),SymbolPath=records[0].FilePath,FootprintPath=records[1].FilePath});
     key.Append(package.Code).Append('|').Append(package.SymbolHash).Append('|').Append(package.FootprintHash).Append('|').Append(package.SymbolName).Append('|').Append(package.FootprintName).Append('\n');
    }catch(Exception error){
     if(error is OperationCanceledException||error is OutOfMemoryException)throw;
     invalid.Add(code);key.Append("needs-refetch|").Append(code).Append('\n');UserLog.Write("Excluded invalid downloaded pair "+code+"; original files retained",error);
    }
   }
   skipped=invalid.ToArray();signature=Digest(key.ToString());
   if(inputs.Count==0&&skipped.Length>0)throw new IOException("以下缓存需要重新获取："+string.Join("、",skipped.Take(8))+"。原文件已保留。");
   return inputs.ToArray();
  }
  static State EnsureLocked(LibraryStore store,CancellationToken token,Action<string> progress){
   token.ThrowIfCancellationRequested();string folder=Folder(store);Recover(folder);var previous=ReadState(folder);string key;string[] skipped;
   var inputs=CollectInputs(store,token,out key,out skipped);
   if(previous!=null&&previous.Signature==key&&File.Exists(Library(folder,true))&&File.Exists(Library(folder,false)))return previous;
   VerifyState(folder);return Build(folder,key,inputs,previous,token,progress,skipped);
  }
  internal static void Ensure(LibraryStore store,CancellationToken token,Action<string> progress){
   if(!Enabled)return;lock(gate)using(new FolderLock(Folder(store),token)){EnsureLocked(store,token,progress);}
  }
  internal static string RepairDownloaded(LibraryStore store,CancellationToken token,Action<string> progress){
   if(!Enabled)throw new IOException("元件处理文件缺失，请安装完整版本。");
   string folder=Folder(store);
   lock(gate)using(new FolderLock(folder,token)){
    token.ThrowIfCancellationRequested();CheckManagedPaths(folder);
    if(File.Exists(Path.Combine(folder,"pending.json")))throw new IOException("存在未完成的库更新，请关闭 AD 后重试或导出诊断信息。");
    bool sch=File.Exists(Library(folder,true)),pcb=File.Exists(Library(folder,false));
    if(sch!=pcb)throw new IOException("总库文件不完整，请保留目录并导出诊断信息。");
    string signature;string[] skipped;var inputs=CollectInputs(store,token,out signature,out skipped);
    var actual=sch?new State{SchemaVersion=1,Signature="repair-backup",SymbolHash=LibraryStore.Hash(Library(folder,true)),FootprintHash=LibraryStore.Hash(Library(folder,false))}:null;
    string backupRoot=Path.Combine(folder,"Backups");CheckManagedPaths(backupRoot);Directory.CreateDirectory(backupRoot);
    string backup=Path.Combine(backupRoot,"repair-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(backup);
    foreach(string name in new[]{"LibrarySearch.SchLib","LibrarySearch.PcbLib","state.json"}){
     string source=Path.Combine(folder,name);if(File.Exists(source))File.Copy(source,Path.Combine(backup,name));
    }
    if(actual!=null&&(!Matches(Path.Combine(backup,"LibrarySearch.SchLib"),actual.SymbolHash)||!Matches(Path.Combine(backup,"LibrarySearch.PcbLib"),actual.FootprintHash)||!Matches(Library(folder,true),actual.SymbolHash)||!Matches(Library(folder,false),actual.FootprintHash)))throw new IOException("备份时总库发生变化，请关闭打开的库文件后重试。");
    token.ThrowIfCancellationRequested();
    try{Build(folder,signature,inputs,actual,token,progress,skipped);}
    catch{
     if(actual!=null&&Matches(Library(folder,true),actual.SymbolHash)&&Matches(Library(folder,false),actual.FootprintHash)){
      string stateBackup=Path.Combine(backup,"state.json");if(File.Exists(stateBackup))File.Copy(stateBackup,StatePath(folder),true);else if(File.Exists(StatePath(folder)))File.Delete(StatePath(folder));
     }
     throw;
    }
    UserLog.Write("Downloaded unified libraries rebuilt; backup="+backup);return backup;
   }
  }
  internal static LibraryRecord ViewRecord(LibraryRecord record){
   if(record.Package==null||!Enabled)return record;
   var store=new LibraryStore(record.LibraryRoot);string folder=Folder(store);
   store.Records(record.Package,true);
   lock(gate)using(new FolderLock(folder,CancellationToken.None)){
    EnsureLocked(store,CancellationToken.None,null);VerifyState(folder);
    return new LibraryRecord{Name=record.Name,Kind=record.Kind,FilePath=Library(folder,record.Kind=="Symbol"),Code=record.Code,RelatedName=record.RelatedName,Package=record.Package,LibraryRoot=record.LibraryRoot,Description=record.Description,PackageName=record.PackageName};
   }
  }
  // A project library saved in AD can contain edits that the converter cannot
  // round-trip. Keep it in place and give new placements distinct library names.
  static string ProjectPartId(LibraryPackage package){
   return LibraryStore.Normalize(package.Code)+"_"+Digest("project-part-1|"+package.SymbolHash+"|"+package.FootprintHash).Substring(0,16);
  }
  static ProjectFiles PreparePart(string projectTotal,LibraryRecord record,LibraryRecord[] selected){
   var package=record.Package;
   string code=LibraryStore.Normalize(package.Code),id=ProjectPartId(package);
   string parts=Path.Combine(Path.GetDirectoryName(projectTotal),"Parts"),folder=Path.Combine(parts,id);
   string symbol="S_"+id,footprint="P_"+id;
   CheckManagedPaths(parts);CheckManagedPaths(folder);
   string sch=Path.Combine(folder,id+".SchLib"),pcb=Path.Combine(folder,id+".PcbLib");
   if(IsRedirected(sch)||IsRedirected(pcb))throw new IOException("独立元件库指向其他位置："+folder);
   string signature="pcb-storage-2-"+id;
   var input=new MergeInput{Code=code,SymbolPath=selected[0].FilePath,SymbolName=package.SymbolName,SymbolAlias=symbol,
    FootprintPath=selected[1].FilePath,FootprintName=package.FootprintName,FootprintAlias=footprint};
   if(!Directory.Exists(folder)){
    Directory.CreateDirectory(parts);
    string stage=Path.Combine(parts,".p-"+Guid.NewGuid().ToString("N").Substring(0,12));
    Directory.CreateDirectory(stage);
    try{
     Build(stage,signature,new[]{input},null,CancellationToken.None,null,stem:id);
     Directory.Move(stage,folder);
    }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
   }
   Recover(folder,id);
   var state=ReadState(folder);
   if(state==null||(state.Signature!=id&&state.Signature!=signature)||!Matches(sch,state.SymbolHash)||!Matches(pcb,state.FootprintHash))
    throw new IOException("工程中的独立元件库已变化，请核对文件："+folder);
   if(state.Signature==id)state=Build(folder,signature,new[]{input},state,CancellationToken.None,null,stem:id);
   return new ProjectFiles{SymbolPath=sch,FootprintPath=pcb,Revision=state.SymbolHash+state.FootprintHash,
    PlacementName=record.Kind=="Symbol"?symbol:footprint};
  }
  internal static ProjectFiles CopyToProject(LibraryRecord record,string projectPath){
   var store=new LibraryStore(record.LibraryRoot);string global=Folder(store);
   UserLog.Write("Prepare project total: "+projectPath);
   string folder=ProjectFolder(projectPath);
   lock(gate)using(new FolderLock(global,CancellationToken.None)){
    var current=EnsureLocked(store,CancellationToken.None,null);VerifyState(global);var selected=store.Records(record.Package,true);
    using(new FolderLock(folder,CancellationToken.None)){
     string existingPart=Path.Combine(Path.GetDirectoryName(folder),"Parts",ProjectPartId(record.Package));
     if(record.UseProjectPart||Directory.Exists(existingPart))return PreparePart(folder,record,selected);
     State previous;
     try{previous=VerifyState(folder);}
     catch(ChangedLibraryException error){
      UserLog.Write("Preserving edited project library; using a separate pair for "+record.Code+": "+error.Message);
      return PreparePart(folder,record,selected);
     }
     var ready=previous;string signature="project-6-pcb-storage-"+current.SymbolHash+current.FootprintHash+record.Package.SymbolHash+record.Package.FootprintHash;
     if(previous==null||previous.Signature!=signature){
      var package=record.Package;
      var inputs=new List<MergeInput>{new MergeInput{Code=package.Code,SymbolName=package.SymbolName,SymbolAlias=SymbolName(package),FootprintName=package.FootprintName,FootprintAlias=FootprintName(package),SymbolPath=selected[0].FilePath,FootprintPath=selected[1].FilePath},new MergeInput{SymbolPath=Library(global,true),FootprintPath=Library(global,false)}};
      // Keep parts and old 3D revisions already used by this project, even if
      // their source cache was deleted or upgraded in the global download store.
      if(previous!=null)inputs.Add(new MergeInput{SymbolPath=Library(folder,true),FootprintPath=Library(folder,false)});
      ready=Build(folder,signature,inputs.ToArray(),previous,CancellationToken.None,null);
     }
     return new ProjectFiles{SymbolPath=Library(folder,true),FootprintPath=Library(folder,false),Revision=ready.SymbolHash+ready.FootprintHash};
    }
   }
  }
 }
}
