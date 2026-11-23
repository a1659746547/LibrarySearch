using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using LibrarySearch;
class UnifiedIsolationTests {
 public sealed class StateView {public int Symbols,Footprints;public string[] SkippedCodes;}
 static int checks;static Type manager=typeof(LibraryStore).Assembly.GetType("LibrarySearch.UnifiedLibrary",true);
 static object Call(string name,params object[] args){try{return manager.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
 static bool RefetchError(Action action,string code){try{action();return false;}catch(IOException error){return error.Message.Contains(code);}}
 static LibraryPackage Add(LibraryStore store,string fixture,string code){
  string relative=Path.Combine("Components",code,"fixture"),folder=Path.Combine(store.Root,relative);Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(store.Root,"Index"));
  string sch=Path.Combine(folder,code+".SchLib"),pcb=Path.Combine(folder,code+".PcbLib");File.Copy(Path.Combine(fixture,"C15127-paired.SchLib"),sch);File.Copy(Path.Combine(fixture,"C15127-paired.PcbLib"),pcb);
  var style=SymbolAppearance.Classic();var package=new LibraryPackage{Code=code,Format=ReleaseInfo.Format,SymbolName="AO3401A",FootprintName="LS_C15127_bcb5ad45d19a46aba15735f35828e8ab",RelativeFolder=relative,SymbolHash=LibraryStore.Hash(sch),FootprintHash=LibraryStore.Hash(pcb),AcquisitionVersion=ReleaseInfo.Acquisition,PinGeometryVersion="svg-line-1",Appearance=style,AppearanceKey=style.Key(),Model3dStatus="embedded"};
  Json.WriteAtomic(Path.Combine(store.Root,"Index",code+".json"),package);return package;
 }
 static void CheckOtherFailures(string root,string fixture){
  foreach(string fault in new[]{"changed-symbol","missing-symbol","missing-footprint","invalid-header","invalid-json","unsupported-format","mismatched-code","null-index"}){
   var store=new LibraryStore(Path.Combine(root,fault));Add(store,fixture,"C1");var bad=Add(store,fixture,"C2");
   string index=Path.Combine(store.Root,"Index","C2.json"),folder=Path.Combine(store.Root,bad.RelativeFolder),sch=Path.Combine(folder,"C2.SchLib"),pcb=Path.Combine(folder,"C2.PcbLib");
   switch(fault){
    case "changed-symbol":using(var stream=File.Open(sch,FileMode.Append,FileAccess.Write))stream.WriteByte(100);break;
    case "missing-symbol":File.Delete(sch);break;
    case "missing-footprint":File.Delete(pcb);break;
    case "invalid-header":File.WriteAllBytes(pcb,new byte[]{1,2,3});break;
    case "invalid-json":File.WriteAllText(index,"{ invalid");break;
    case "unsupported-format":bad.Format="unsupported";Json.WriteAtomic(index,bad);break;
    case "mismatched-code":bad.Code="C3";Json.WriteAtomic(index,bad);break;
    case "null-index":File.WriteAllText(index,"null");break;
   }
   var originals=Directory.GetFiles(store.Root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>LibraryStore.Hash(path));
   Call("Ensure",store,CancellationToken.None,null);var state=Json.Read<StateView>(Path.Combine(store.Root,"Unified","state.json"));
   Check(state.Symbols==1&&state.Footprints==1&&state.SkippedCodes.SequenceEqual(new[]{"C2"}),fault+": only the affected source is excluded");
   var fetched=store.Fetch("C1","missing-converter.exe",false,CancellationToken.None,delegate{},SymbolAppearance.Classic(),false);
   Check(fetched.Reused&&fetched.Records.Length==2,fault+": unrelated cached acquisition remains usable");
   Check(originals.All(pair=>File.Exists(pair.Key)&&LibraryStore.Hash(pair.Key)==pair.Value),fault+": source files and indexes remain unchanged");
  }
 }
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),distribution=Path.GetFullPath(args[1]),fixture=Path.GetFullPath(args[2]);Directory.CreateDirectory(root);
  var store=new LibraryStore(Path.Combine(root,"downloads"));var healthy=Add(store,fixture,"C99665");var bad=Add(store,fixture,"C2858491");
  string badIndex=Path.Combine(store.Root,"Index",bad.Code+".json"),badPcb=Path.Combine(store.Root,bad.RelativeFolder,bad.Code+".PcbLib"),goodPcb=Path.Combine(store.Root,healthy.RelativeFolder,healthy.Code+".PcbLib");
  using(var stream=File.Open(badPcb,FileMode.Append,FileAccess.Write))stream.WriteByte(123);string edited=LibraryStore.Hash(badPcb),indexHash=LibraryStore.Hash(badIndex);
  Call("Configure",Path.Combine(distribution,"online","bin","easyeda2altium.exe"));Call("Ensure",store,CancellationToken.None,null);
  string unified=Path.Combine(store.Root,"Unified"),statePath=Path.Combine(unified,"state.json");var state=Json.Read<StateView>(statePath);
  Check(state.Symbols==1&&state.Footprints==1,"a changed old footprint cannot block a healthy component's total libraries");
  Check(state.SkippedCodes.SequenceEqual(new[]{bad.Code}),"total state identifies only the affected component");
  var rows=store.LoadDownloaded();var selected=rows.First(row=>row.Code==healthy.Code&&row.Kind=="Symbol");var invalid=rows.First(row=>row.Code==bad.Code&&row.Kind=="Symbol");
  foreach(string previous in new[]{"source-pin-width-slot-path-6-paired-footprint","source-pin-width-slot-path-7-value"}){
   healthy.AcquisitionVersion=previous;
   var indexPath=Path.Combine(store.Root,"Index",healthy.Code+".json");
   Json.WriteAtomic(indexPath,healthy);string previousHash=LibraryStore.Hash(indexPath);
   var cached=store.Fetch(healthy.Code,"missing-converter.exe",false,CancellationToken.None,delegate{},SymbolAppearance.Classic(),true);
   Check(cached.Reused&&cached.Records[0].Name==selected.Name,"verified older pair reuses the same symbol alias offline: "+previous);
   var upgradeProject=Path.Combine(root,"upgrade-"+previous+".PrjPcb");File.WriteAllText(upgradeProject,"[Design]");
   var placed=LibraryStore.CopyToProject(cached.Records.First(row=>row.Kind=="Footprint"),upgradeProject);
   Check(File.Exists(placed.FootprintPath)&&File.Exists(placed.SymbolPath)&&LibraryStore.Hash(indexPath)==previousHash,"public placement accepts older pair and preserves its source index: "+previous);
  }
  Check(rows.Length==4&&rows.Where(row=>row.Code==bad.Code).All(row=>!string.IsNullOrEmpty(row.ValidationError)),"affected cached symbol and footprint stay visible with a refetch message");
  Check(rows.Where(row=>row.Code==healthy.Code).All(row=>string.IsNullOrEmpty(row.ValidationError)),"healthy downloaded records remain usable");
  Check(invalid.ValidationError.Contains(bad.Code)&&invalid.ValidationError.Contains("封装"),"validation message identifies the component and footprint");
  var reused=store.Fetch(healthy.Code,"missing-converter.exe",false,CancellationToken.None,delegate{},SymbolAppearance.Classic(),false);
  Check(reused.Reused&&reused.Records.Length==2,"fetching a healthy cached component succeeds without downloading again");
  var viewed=(LibraryRecord)Call("ViewRecord",selected);Check(File.Exists(viewed.FilePath),"a healthy component can open its regenerated total library");
  string project=Path.Combine(root,"project","test.PrjPcb");Directory.CreateDirectory(Path.GetDirectoryName(project));File.WriteAllText(project,"");
  var files=(ProjectFiles)Call("CopyToProject",selected,project);Check(File.Exists(files.SymbolPath)&&File.Exists(files.FootprintPath),"healthy symbol and footprint copy together into an independent project");
  Check(RefetchError(delegate{Call("ViewRecord",invalid);},bad.Code),"opening the affected component requests refetch instead of an absent alias");
  Check(RefetchError(delegate{Call("CopyToProject",invalid,project);},bad.Code),"placing the affected component remains blocked with its own code");
  string total=Path.Combine(unified,"LibrarySearch.PcbLib"),beforeTotal=LibraryStore.Hash(total),beforeState=LibraryStore.Hash(statePath);
  using(var stream=File.Open(goodPcb,FileMode.Append,FileAccess.Write))stream.WriteByte(99);
  Check(RefetchError(delegate{Call("Ensure",store,CancellationToken.None,null);},healthy.Code)&&LibraryStore.Hash(total)==beforeTotal&&LibraryStore.Hash(statePath)==beforeState,"when all sources need refetch the previous total and state remain intact");
  File.Copy(Path.Combine(fixture,"C15127-paired.PcbLib"),goodPcb,true);Call("Ensure",store,CancellationToken.None,null);
  string backup=(string)Call("RepairDownloaded",store,CancellationToken.None,null);
  Check(Directory.Exists(backup)&&Json.Read<StateView>(statePath).Symbols==1,"explicit total repair also isolates the affected old source");
  Check(LibraryStore.Hash(badPcb)==edited&&LibraryStore.Hash(badIndex)==indexHash,"automatic and explicit repair never rewrite affected originals or their index");
  File.Copy(Path.Combine(fixture,"C15127-paired.PcbLib"),badPcb,true);Call("Ensure",store,CancellationToken.None,null);state=Json.Read<StateView>(statePath);
  Check(state.Symbols==2&&state.Footprints==2&&state.SkippedCodes.Length==0,"a restored source rejoins totals automatically without changing its index");
  Check(store.LoadDownloaded().All(row=>string.IsNullOrEmpty(row.ValidationError)),"restored source no longer displays the refetch warning");
  File.Delete(badIndex);File.Delete(Path.Combine(store.Root,"Index",healthy.Code+".json"));Call("Ensure",store,CancellationToken.None,null);state=Json.Read<StateView>(statePath);
  Check(state.Symbols==0&&state.Footprints==0&&state.SkippedCodes.Length==0,"a genuinely empty cache still produces a valid empty total");
  string emptyState=LibraryStore.Hash(statePath);using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();bool stopped=false;try{Call("Ensure",store,cancellation.Token,null);}catch(OperationCanceledException){stopped=true;}Check(stopped&&LibraryStore.Hash(statePath)==emptyState,"cancellation remains cancellation and never replaces the active state");}
  CheckOtherFailures(Path.Combine(root,"other-failures"),fixture);
  Console.WriteLine("RESULT "+checks+" unified isolation checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
