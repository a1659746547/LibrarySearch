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
  public sealed class State {public int SchemaVersion;public string Signature,SymbolHash,FootprintHash;public int Symbols,Footprints,Models;}
  public sealed class Journal {public State Previous,Next;}
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
  static string Library(string folder,bool symbol){return Path.Combine(folder,"LibrarySearch."+(symbol?"SchLib":"PcbLib"));}
  static string Backup(string folder,bool symbol){return Path.Combine(folder,"rollback."+(symbol?"SchLib":"PcbLib"));}
  static string StatePath(string folder){return Path.Combine(folder,"state.json");}
  static void CheckManagedPaths(string folder){
   for(var current=new DirectoryInfo(Path.GetFullPath(folder));current!=null;current=current.Parent)
    if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("总库目录被重定向，未修改文件。请选择普通目录。");
   foreach(string name in new[]{"LibrarySearch.SchLib","LibrarySearch.PcbLib","state.json","pending.json","rollback.SchLib","rollback.PcbLib"}){
    string path=Path.Combine(folder,name);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("总库文件被重定向，未修改文件。");
   }
  }
  static bool Matches(string path,string hash){return File.Exists(path)&&hash!=null&&LibraryStore.Hash(path).Equals(hash,StringComparison.OrdinalIgnoreCase);}
  static State ReadState(string folder){var path=StatePath(folder);if(!File.Exists(path))return null;var state=Json.Read<State>(path);if(state==null||state.SchemaVersion!=1)throw new IOException("总库记录格式无效，请保留文件并导出反馈。");return state;}
  static void Recover(string folder){
   CheckManagedPaths(folder);
   string pending=Path.Combine(folder,"pending.json");if(!File.Exists(pending))return;
   var journal=Json.Read<Journal>(pending);if(journal==null||journal.Next==null||journal.Next.SchemaVersion!=1)throw new IOException("总库回滚记录无效，请导出反馈。");
   foreach(bool symbol in new[]{false,true}){
    string path=Library(folder,symbol),next=symbol?journal.Next.SymbolHash:journal.Next.FootprintHash;
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
   else if(!Matches(Library(folder,true),state.SymbolHash)||!Matches(Library(folder,false),state.FootprintHash))throw new IOException("总库已被外部修改，未覆盖。请保留修改后导出反馈。");
   return state;
  }
  static void Commit(string folder,string generated,State previous,State next){
   Directory.CreateDirectory(folder);
   foreach(bool symbol in new[]{false,true})if(previous!=null)File.Copy(Library(folder,symbol),Backup(folder,symbol),true);
   string pending=Path.Combine(folder,"pending.json");Json.WriteAtomic(pending,new Journal{Previous=previous,Next=next});
   try{
    foreach(bool symbol in new[]{false,true}){
     string input=Path.ChangeExtension(generated,symbol?"SchLib":"PcbLib"),path=Library(folder,symbol);
     if(File.Exists(path))File.Replace(input,path,null);else File.Move(input,path);
    }
    Json.WriteAtomic(StatePath(folder),next);File.Delete(pending);
   }catch{Recover(folder);throw;}
   foreach(bool symbol in new[]{false,true})if(File.Exists(Backup(folder,symbol)))File.Delete(Backup(folder,symbol));
  }
  static State Build(string folder,string signature,MergeInput[] entries,State previous,CancellationToken token,Action<string> progress){
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
    var next=new State{SchemaVersion=1,Signature=signature,SymbolHash=LibraryStore.Hash(output+".SchLib"),FootprintHash=LibraryStore.Hash(output+".PcbLib"),Symbols=summary.Symbols,Footprints=summary.Footprints,Models=summary.Models};
    token.ThrowIfCancellationRequested();Commit(folder,output,previous,next);return next;
   }finally{try{if(Directory.Exists(run))Directory.Delete(run,true);}catch(Exception error){UserLog.Write("Unified library staging cleanup",error);}}
  }
  static string Folder(LibraryStore store){return LibraryStore.ContainedPath(store.Root,"Unified");}
  static State EnsureLocked(LibraryStore store,CancellationToken token,Action<string> progress){
   token.ThrowIfCancellationRequested();string folder=Folder(store);Recover(folder);var previous=ReadState(folder);var packages=new List<LibraryPackage>();var signature=new StringBuilder("unified-library-3\n");
   string index=Path.Combine(store.Root,"Index");
   foreach(string file in Directory.Exists(index)?Directory.GetFiles(index,"C*.json").OrderBy(p=>p,StringComparer.OrdinalIgnoreCase):Enumerable.Empty<string>()){
    token.ThrowIfCancellationRequested();var package=Json.Read<LibraryPackage>(file);
    if(package==null||Path.GetFileNameWithoutExtension(file)!=package.Code)throw new IOException("已下载编号索引不匹配，原总库保留。");
    signature.Append(package.Code).Append('|').Append(package.SymbolHash).Append('|').Append(package.FootprintHash).Append('|').Append(package.SymbolName).Append('|').Append(package.FootprintName).Append('\n');
    packages.Add(package);
   }
   string key=Digest(signature.ToString());
   if(previous!=null&&previous.Signature==key&&File.Exists(Library(folder,true))&&File.Exists(Library(folder,false)))return previous;
   VerifyState(folder);var inputs=new List<MergeInput>();
   foreach(var package in packages){var records=store.Records(package,true);inputs.Add(new MergeInput{Code=package.Code,SymbolName=package.SymbolName,SymbolAlias=SymbolName(package),FootprintName=package.FootprintName,FootprintAlias=FootprintName(package),SymbolPath=records[0].FilePath,FootprintPath=records[1].FilePath});}
   return Build(folder,key,inputs.ToArray(),previous,token,progress);
  }
  internal static void Ensure(LibraryStore store,CancellationToken token,Action<string> progress){
   if(!Enabled)return;lock(gate)using(new FolderLock(Folder(store),token)){EnsureLocked(store,token,progress);}
  }
  internal static LibraryRecord ViewRecord(LibraryRecord record){
   if(record.Package==null||!Enabled)return record;
   var store=new LibraryStore(record.LibraryRoot);string folder=Folder(store);
   lock(gate)using(new FolderLock(folder,CancellationToken.None)){
    EnsureLocked(store,CancellationToken.None,null);VerifyState(folder);
    return new LibraryRecord{Name=record.Name,Kind=record.Kind,FilePath=Library(folder,record.Kind=="Symbol"),Code=record.Code,RelatedName=record.RelatedName,Package=record.Package,LibraryRoot=record.LibraryRoot,Description=record.Description,PackageName=record.PackageName};
   }
  }
  internal static ProjectFiles CopyToProject(LibraryRecord record,string projectPath){
   var store=new LibraryStore(record.LibraryRoot);string global=Folder(store);
   string folder=Path.Combine(Path.GetDirectoryName(projectPath),"Libraries","LibrarySearch","Unified");
   lock(gate)using(new FolderLock(global,CancellationToken.None)){
    var current=EnsureLocked(store,CancellationToken.None,null);VerifyState(global);var selected=store.Records(record.Package,true);
    using(new FolderLock(folder,CancellationToken.None)){
     var previous=VerifyState(folder);var ready=previous;string signature="project-3-"+current.SymbolHash+current.FootprintHash+record.Package.SymbolHash+record.Package.FootprintHash;
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
