using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using LibrarySearch;
// Optional integration test: a read-only copy of a downloaded store created by rc.3.
class UnifiedRecoveryTests {
 public sealed class State {public int Symbols,Footprints,Models;public string Signature;}
 static int checks;static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Copy(string source,string target){Directory.CreateDirectory(target);foreach(var p in Directory.GetFiles(source))File.Copy(p,Path.Combine(target,Path.GetFileName(p)));foreach(var p in Directory.GetDirectories(source))Copy(p,Path.Combine(target,Path.GetFileName(p)));}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string cache=Path.Combine(root,"downloads");Copy(Path.GetFullPath(args[2]),cache);
  string global=Path.Combine(cache,"Unified"),stateFile=Path.Combine(global,"state.json");var before=Json.Read<State>(stateFile);string oldPcb=LibraryStore.Hash(Path.Combine(global,"LibrarySearch.PcbLib"));
  var store=new LibraryStore(cache);string[] index=Directory.GetFiles(Path.Combine(cache,"Index"),"*.json");var hashes=index.Select(LibraryStore.Hash).ToArray();
  string project=Path.Combine(root,"project","test.PrjPcb"),projectLib=Path.Combine(root,"project","Libraries","LibrarySearch","Unified");Directory.CreateDirectory(Path.GetDirectoryName(project));File.WriteAllText(project,"[Design]");Copy(global,projectLib);
  var manager=typeof(LibraryStore).Assembly.GetType("LibrarySearch.UnifiedLibrary",true);var flags=BindingFlags.Static|BindingFlags.NonPublic;
  manager.GetMethod("Configure",flags).Invoke(null,new object[]{Path.Combine(Path.GetFullPath(args[1]),"online","bin","easyeda2altium.exe")});
  manager.GetMethod("Ensure",flags).Invoke(null,new object[]{store,CancellationToken.None,null});var after=Json.Read<State>(stateFile);
  Check(after.Symbols==before.Symbols&&after.Footprints==before.Footprints&&after.Models==before.Models,"rc3 store migration retains every downloaded symbol, footprint and model");
  Check(LibraryStore.Hash(Path.Combine(global,"LibrarySearch.PcbLib"))!=oldPcb&&after.Signature!=before.Signature,"existing rc3 total is rebuilt automatically without redownloading");
  Check(index.Select(LibraryStore.Hash).SequenceEqual(hashes),"recovery leaves original package indices and download identities untouched");
  var records=store.LoadDownloaded();Check(records.Length==after.Symbols+after.Footprints,"all downloaded records remain available after recovery");
  var selected=records.First(r=>r.Kind=="Symbol"&&r.Code=="C365735");LibraryStore.CopyToProject(selected,project);
  var projectState=Json.Read<State>(Path.Combine(projectLib,"state.json"));Check(projectState.Signature.StartsWith("project-3-")&&projectState.Symbols==after.Symbols,"a project containing the old rc3 total is rebuilt before placement");
  Check(store.Records(selected.Package,true).Length==2,"original ES8316 source pair retains its recorded hashes");
  Console.WriteLine("RESULT "+checks+" unified recovery checks passed");Console.WriteLine("RECOVERED "+global);Console.WriteLine("RECOVERED "+projectLib);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
