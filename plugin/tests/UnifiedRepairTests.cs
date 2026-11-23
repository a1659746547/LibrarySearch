using System;
using System.IO;
using System.Reflection;
using System.Threading;
using LibrarySearch;
class UnifiedRepairTests {
 static int checks;static Type manager=typeof(LibraryStore).Assembly.GetType("LibrarySearch.UnifiedLibrary",true);
 static object Call(string name,params object[] args){try{return manager.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),distribution=Path.GetFullPath(args[1]),source=Path.GetFullPath(args[2]);
  Directory.CreateDirectory(root);var store=new LibraryStore(Path.Combine(root,"downloads"));string folder=Path.Combine(store.Root,"Components","C15127","fixture");Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(store.Root,"Index"));
  string sch=Path.Combine(folder,"C15127.SchLib"),pcb=Path.Combine(folder,"C15127.PcbLib");File.Copy(Path.Combine(source,"C15127-paired.SchLib"),sch);File.Copy(Path.Combine(source,"C15127-paired.PcbLib"),pcb);
  var package=new LibraryPackage{Code="C15127",Format=ReleaseInfo.Format,SymbolName="AO3401A",FootprintName="LS_C15127_bcb5ad45d19a46aba15735f35828e8ab",RelativeFolder=Path.Combine("Components","C15127","fixture"),SymbolHash=LibraryStore.Hash(sch),FootprintHash=LibraryStore.Hash(pcb),AcquisitionVersion=ReleaseInfo.Acquisition,PinGeometryVersion="svg-line-1"};
  Json.WriteAtomic(Path.Combine(store.Root,"Index","C15127.json"),package);Call("Configure",Path.Combine(distribution,"online","bin","easyeda2altium.exe"));
  Call("Ensure",store,CancellationToken.None,null);string unified=Path.Combine(store.Root,"Unified"),total=Path.Combine(unified,"LibrarySearch.SchLib"),state=Path.Combine(unified,"state.json");
  using(var stream=File.Open(total,FileMode.Append,FileAccess.Write))stream.WriteByte(123);
  string edited=LibraryStore.Hash(total),recorded=LibraryStore.Hash(state);bool blocked=false;try{Call("ViewRecord",store.LoadDownloaded()[0]);}catch(IOException){blocked=true;}
  Check(blocked&&LibraryStore.Hash(total)==edited,"ordinary use preserves and reports externally modified totals");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{Call("RepairDownloaded",store,cancel.Token,null);}catch(OperationCanceledException){stopped=true;}Check(stopped&&LibraryStore.Hash(total)==edited&&LibraryStore.Hash(state)==recorded,"cancelled repair cannot alter libraries or their state");}
  string backup=(string)Call("RepairDownloaded",store,CancellationToken.None,null);
  Check(Directory.Exists(backup)&&LibraryStore.Hash(Path.Combine(backup,"LibrarySearch.SchLib"))==edited,"explicit repair preserves the exact edited library in a persistent backup");
  Check(LibraryStore.Hash(Path.Combine(backup,"state.json"))==recorded,"repair archives the original state record");
  Check(LibraryStore.Hash(total)!=edited,"repair publishes a regenerated total from verified download sources");
  Call("ViewRecord",store.LoadDownloaded()[0]);Check(true,"repaired total passes ordinary verification");
  Check(LibraryStore.Hash(sch)==package.SymbolHash&&LibraryStore.Hash(pcb)==package.FootprintHash,"repair does not rewrite source downloads");
  string healthy=LibraryStore.Hash(total);File.WriteAllBytes(sch,new byte[]{1,2,3});bool failed=false;try{Call("RepairDownloaded",store,CancellationToken.None,null);}catch(IOException){failed=true;}
  Check(failed&&LibraryStore.Hash(total)==healthy,"invalid source cannot replace the active total");
  Console.WriteLine("RESULT "+checks+" unified repair checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
