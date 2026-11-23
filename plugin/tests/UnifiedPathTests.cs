using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using LibrarySearch;
class UnifiedPathTests {
 static int checks;static string root;static Type manager=typeof(LibraryStore).Assembly.GetType("LibrarySearch.UnifiedLibrary",true);
 static object Call(string name,params object[] args){try{return manager.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
 static void Link(string link,string target){
  link=Path.GetFullPath(link);target=Path.GetFullPath(target);string boundary=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!link.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||!target.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||link.IndexOfAny("\"&|<>^%!\r\n".ToCharArray())>=0||target.IndexOfAny("\"&|<>^%!\r\n".ToCharArray())>=0)throw new Exception("Junction fixture must remain inside the isolated test root");
  Directory.CreateDirectory(Path.GetDirectoryName(link));Directory.CreateDirectory(target);
  var info=new ProcessStartInfo("cmd.exe","/d /c mklink /J \""+link+"\" \""+target+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  using(var process=Process.Start(info)){string message=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd();if(!process.WaitForExit(10000)||process.ExitCode!=0)throw new Exception("Junction fixture failed: "+message);}
  if((File.GetAttributes(link)&FileAttributes.ReparsePoint)==0)throw new Exception("Fixture is not a junction");
 }
 static LibraryStore Store(string path,string fixture){
  var store=new LibraryStore(path);string relative=Path.Combine("Components","C1","v1"),folder=Path.Combine(path,relative);Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(path,"Index"));
  string sch=Path.Combine(folder,"C1.SchLib"),pcb=Path.Combine(folder,"C1.PcbLib");File.Copy(Path.Combine(fixture,"C15127-paired.SchLib"),sch);File.Copy(Path.Combine(fixture,"C15127-paired.PcbLib"),pcb);
  var package=new LibraryPackage{Code="C1",Format=ReleaseInfo.Format,SymbolName="AO3401A",FootprintName="LS_C15127_bcb5ad45d19a46aba15735f35828e8ab",RelativeFolder=relative,SymbolHash=LibraryStore.Hash(sch),FootprintHash=LibraryStore.Hash(pcb)};
  Json.WriteAtomic(Path.Combine(path,"Index","C1.json"),package);return store;
 }
 static string Project(string directory){Directory.CreateDirectory(directory);string path=Path.Combine(directory,"Demo.PrjPcb");File.WriteAllText(path,"[Design]");return path;}
 static bool Blocked(Action action){try{action();return false;}catch(IOException error){return error.Message.Contains("指向其他位置");}}
 static bool Untouched(string folder){return Directory.GetFiles(folder).Select(Path.GetFileName).SequenceEqual(new[]{"marker.txt"})&&Directory.GetDirectories(folder).Length==0&&File.ReadAllText(Path.Combine(folder,"marker.txt"))=="original";}
 static string Target(string name){string target=Path.Combine(root,name);Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"marker.txt"),"original");return target;}
 static int Main(string[] args){try{
  root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string distribution=Path.GetFullPath(args[1]),fixture=Path.GetFullPath(args[2]);Call("Configure",Path.Combine(distribution,"online","bin","easyeda2altium.exe"));
  var store=Store(Path.Combine(root,"cache"),fixture);var record=store.LoadDownloaded().First(r=>r.Kind=="Symbol");var originals=Directory.GetFiles(store.Root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>LibraryStore.Hash(path));
  string parent=Path.Combine(root,"moved-desktop"),physical=Path.Combine(root,"desktop-physical");Link(parent,physical);string project=Project(Path.Combine(parent,"工程 空格"));
  var files=(ProjectFiles)Call("CopyToProject",record,project);
  Check(File.Exists(files.SymbolPath)&&File.Exists(files.FootprintPath),"project below a migrated Desktop ancestor can prepare both libraries");
  Check(File.Exists(Path.Combine(physical,"工程 空格","Libraries","LibrarySearch","Unified","LibrarySearch.PcbLib")),"project libraries resolve to the expected physical migrated directory");
  var repeated=(ProjectFiles)Call("CopyToProject",record,project);Check(repeated.Revision==files.Revision,"repeated preparation through the same migrated path reuses the verified pair");
  string selected=Path.Combine(root,"selected-project"),selectedTarget=Path.Combine(root,"selected-physical");Link(selected,selectedTarget);var direct=(ProjectFiles)Call("CopyToProject",record,Project(selected));Check(File.Exists(direct.SymbolPath)&&File.Exists(direct.FootprintPath),"an explicitly selected project root may itself be a junction");
  string cacheLink=Path.Combine(root,"selected-cache"),cachePhysical=Path.Combine(root,"cache-physical");Link(cacheLink,cachePhysical);var linkedStore=Store(cacheLink,fixture);Call("Ensure",linkedStore,CancellationToken.None,null);var view=(LibraryRecord)Call("ViewRecord",linkedStore.LoadDownloaded()[0]);Check(File.Exists(view.FilePath),"an explicitly selected download root may itself be a junction");
  foreach(string stage in new[]{"Libraries","LibrarySearch","Unified"}){
   string directory=Path.Combine(root,"block-"+stage),child=Path.Combine(directory,"Libraries");if(stage!="Libraries")child=Path.Combine(child,"LibrarySearch");if(stage=="Unified")child=Path.Combine(child,"Unified");
   string target=Target("target-"+stage);Link(child,target);string p=Project(directory);
   Check(Blocked(delegate{Call("CopyToProject",record,p);})&&Untouched(target),"plugin-owned "+stage+" junction is blocked without touching its target");
  }
  var owned=Store(Path.Combine(root,"owned-cache"),fixture);string redirected=Target("target-global");Link(Path.Combine(owned.Root,"Unified"),redirected);Check(Blocked(delegate{Call("Ensure",owned,CancellationToken.None,null);})&&Untouched(redirected),"redirecting the download total itself remains blocked");
  string backupTarget=Target("target-backups");Link(Path.Combine(store.Root,"Unified","Backups"),backupTarget);Check(Blocked(delegate{Call("RepairDownloaded",store,CancellationToken.None,null);})&&Untouched(backupTarget),"redirecting the managed backup folder remains blocked");
  var fileStore=Store(Path.Combine(root,"file-cache"),fixture);string fileTarget=Target("target-file");Link(Path.Combine(fileStore.Root,"Unified","LibrarySearch.PcbLib"),fileTarget);Check(Blocked(delegate{Call("Ensure",fileStore,CancellationToken.None,null);})&&Untouched(fileTarget),"a redirected managed filename is blocked even when File.Exists returns false");
  Check(originals.All(pair=>File.Exists(pair.Key)&&LibraryStore.Hash(pair.Key)==pair.Value),"all original downloaded libraries and indexes remain unchanged");
  Console.WriteLine("RESULT "+checks+" unified path checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
