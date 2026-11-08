using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;

// Offline integration against a real paired SchLib/PcbLib fixture. No AD or network.
class UnifiedLibraryTests {
 public sealed class State {public int SchemaVersion,Symbols,Footprints,Models;public string Signature,SymbolHash,FootprintHash;}
 static int checks;static Type manager=typeof(LibraryStore).Assembly.GetType("LibrarySearch.UnifiedLibrary",true);
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
 static void Call(string method,params object[] args){try{manager.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static void Ensure(LibraryStore store){Call("Ensure",store,CancellationToken.None,null);}
 static State Status(string folder){return Json.Read<State>(Path.Combine(folder,"state.json"));}
 static string Sch(string folder){return Path.Combine(folder,"LibrarySearch.SchLib");}
 static string Pcb(string folder){return Path.Combine(folder,"LibrarySearch.PcbLib");}
 static string Quote(string value){return "\""+value+"\"";}
 static bool Fails(Action action){try{action();return false;}catch(IOException){return true;}}
 static void Run(string exe,string arguments){using(var p=Process.Start(new ProcessStartInfo(exe,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(30000)){p.Kill();throw new Exception("index fixture timeout");}if(p.ExitCode!=0)throw new Exception(output.Result+error.Result);}}
 static LibraryPackage Add(LibraryStore store,string code,string symbol,string footprint,string source){
  string relative=Path.Combine("Components",code,"fixture"),folder=Path.Combine(store.Root,relative);Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(store.Root,"Index"));
  string sch=Path.Combine(folder,code+".SchLib"),pcb=Path.Combine(folder,code+".PcbLib");File.Copy(Path.Combine(source,"fixture.SchLib"),sch);File.Copy(Path.Combine(source,"fixture.PcbLib"),pcb);
  var style=SymbolAppearance.Classic();var package=new LibraryPackage{Code=code,Format=ReleaseInfo.Format,AcquisitionVersion=ReleaseInfo.Acquisition,PinGeometryVersion="svg-line-1",SymbolName=symbol,FootprintName=footprint,RelativeFolder=relative,SymbolHash=LibraryStore.Hash(sch),FootprintHash=LibraryStore.Hash(pcb),Appearance=style,AppearanceKey=style.Key(),PackageName="fixture-package",Model3dStatus="embedded",DownloadedUtc=DateTime.UtcNow.ToString("o")};
  File.WriteAllText(Path.Combine(folder,"SOURCE.txt"),"offline test fixture");Json.WriteAtomic(Path.Combine(folder,"component.json"),package);Json.WriteAtomic(Path.Combine(store.Root,"Index",code+".json"),package);return package;
 }
 [STAThread] static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),distribution=Path.GetFullPath(args[1]),fixture=Path.GetFullPath(args[2]);Directory.CreateDirectory(root);
  string converter=Path.Combine(distribution,"online","bin","easyeda2altium.exe"),source=Path.Combine(root,"input");Directory.CreateDirectory(source);
  File.Copy(Path.Combine(fixture,"C15127-paired.SchLib"),Path.Combine(source,"fixture.SchLib"));File.Copy(Path.Combine(fixture,"C15127-paired.PcbLib"),Path.Combine(source,"fixture.PcbLib"));
  string indexRun=Path.Combine(root,"index-cache","run");Directory.CreateDirectory(indexRun);
  string index=Path.Combine(indexRun,"fixture-index.json"),request=Path.Combine(root,"index-request.json");Json.WriteAtomic(request,new{Roots=new[]{source},CacheDirectory=Path.Combine(indexRun,"extract")});Run(converter,"--index-request "+Quote(request)+" --output "+Quote(index));
  var entries=Json.Read<LocalIndexData>(index).Records;Check(entries!=null&&entries.Length==2,"actual paired fixture indexes as symbol and footprint");
  string symbol=entries.Single(r=>r.Kind=="Symbol").Name,footprint=entries.Single(r=>r.Kind=="Footprint").Name;
  var store=new LibraryStore(Path.Combine(root,"downloads"));var a=Add(store,"C1",symbol,footprint,source);var b=Add(store,"C2",symbol,footprint,source);Call("Configure",converter);Ensure(store);
  string global=Path.Combine(store.Root,"Unified");var state=Status(global);Check(state.Symbols==2&&state.Footprints==2,"all downloaded codes share one global library pair");Check(state.Models==2,"embedded models with duplicate input IDs remain present");
  var rows=store.LoadDownloaded();Check(rows.Length==4&&rows[0].Name!=rows[2].Name,"same human model names have distinct internal identities");Check(rows.All(r=>r.DisplayName==(r.Kind=="Symbol"?symbol:"fixture-package")),"list labels retain readable source model and package names");
  Check(LibraryRecordSearch.Find(rows,symbol,0,false,CancellationToken.None).Total==4,"human model names remain searchable after aggregation");
  using(var form=new Form{ShowInTaskbar=false,Left=-20000,Top=-20000})using(var panel=new LibraryPanel(distribution,new UserSettings{LibraryRoot=store.Root,UsageNoticeVersion=1})){
   LibraryRecord opened=null;panel.OpenRequested+=r=>opened=r;form.Controls.Add(panel);form.Show();
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;((System.Windows.Forms.Timer)typeof(LibraryPanel).GetField("updateTimer",flags).GetValue(panel)).Stop();var deadline=DateTime.UtcNow.AddSeconds(15);
   while(!(bool)typeof(LibraryPanel).GetField("loaded",flags).GetValue(panel)){if(DateTime.UtcNow>deadline)throw new Exception("panel load timeout");Application.DoEvents();Thread.Sleep(5);}
   typeof(LibraryPanel).GetMethod("RequestOpenLibrary",flags).Invoke(panel,new object[]{rows[0]});
   while(opened==null){if(DateTime.UtcNow>deadline)throw new Exception("open library timeout");Application.DoEvents();Thread.Sleep(5);}
   Check(opened.FilePath==(rows[0].Kind=="Symbol"?Sch(global):Pcb(global))&&opened.Name==rows[0].Name,"view-library action opens the unified total instead of another per-code library");form.Close();
  }
  Check(LibraryStore.Hash(store.Records(a,true)[0].FilePath)==a.SymbolHash&&LibraryStore.Hash(store.Records(a,true)[1].FilePath)==a.FootprintHash,"aggregation does not modify original cache libraries");
  string oldSch=LibraryStore.Hash(Sch(global)),oldPcb=LibraryStore.Hash(Pcb(global));Ensure(store);Check(oldSch==LibraryStore.Hash(Sch(global))&&oldPcb==LibraryStore.Hash(Pcb(global)),"unchanged downloads reuse the existing total without rewriting");
  string project=Path.Combine(root,"project","test.PrjPcb");Directory.CreateDirectory(Path.GetDirectoryName(project));File.WriteAllText(project,"[Design]");
  var first=LibraryStore.CopyToProject(store.Records(a,true)[0],project);var second=LibraryStore.CopyToProject(store.Records(b,true)[0],project);string projectFolder=Path.GetDirectoryName(first.SymbolPath);
  Check(first.SymbolPath==second.SymbolPath&&first.FootprintPath==second.FootprintPath,"every placement uses the same fixed project library paths");Check(Directory.GetFiles(Path.Combine(root,"project"),"*.SchLib",SearchOption.AllDirectories).Length==1&&Directory.GetFiles(Path.Combine(root,"project"),"*.PcbLib",SearchOption.AllDirectories).Length==1,"project contains only one symbol library and one footprint library");
  Check(Status(projectFolder).Symbols==2&&Status(projectFolder).Models==2,"project total contains all current downloads and 3D resources");
  var removed=store.DeleteDownloadedBatch(new[]{"C1"},CancellationToken.None,null);Check(removed.DeletedCodes.Length==1&&removed.UnifiedError==null&&Status(global).Symbols==1,"batch cache deletion automatically refreshes the global total");
  LibraryStore.CopyToProject(store.Records(b,true)[0],project);Check(Status(projectFolder).Symbols==2&&Status(projectFolder).Models==2,"updating a project retains already used parts deleted from cache");
  byte[] original=File.ReadAllBytes(Sch(global));using(var file=new FileStream(Sch(global),FileMode.Append))file.WriteByte(123);string edited=LibraryStore.Hash(Sch(global));
  Check(Fails(()=>LibraryStore.CopyToProject(store.Records(b,true)[0],project))&&LibraryStore.Hash(Sch(global))==edited,"external edits to the total are detected and preserved");File.WriteAllBytes(Sch(global),original);
  oldSch=LibraryStore.Hash(Sch(global));oldPcb=LibraryStore.Hash(Pcb(global));Add(store,"C3",symbol,footprint,source);
  using(File.Open(Sch(global),FileMode.Open,FileAccess.Read,FileShare.None))Check(Fails(()=>Ensure(store)),"a library locked by another application blocks replacement");
  Check(oldSch==LibraryStore.Hash(Sch(global))&&oldPcb==LibraryStore.Hash(Pcb(global)),"failed replacement preserves the previous complete pair");Ensure(store);Check(Status(global).Symbols==2,"retry after releasing a library lock completes the update");
  using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool caught=false;try{Call("Ensure",store,cancelled.Token,null);}catch(OperationCanceledException){caught=true;}Check(caught,"cancelled aggregation stops before changing libraries");}
  var previous=Status(global);File.Copy(Sch(global),Path.Combine(global,"rollback.SchLib"),true);File.Copy(Pcb(global),Path.Combine(global,"rollback.PcbLib"),true);
  var next=new State{SchemaVersion=1,Signature="interrupted",SymbolHash=previous.SymbolHash,FootprintHash=LibraryStore.Hash(Path.Combine(source,"fixture.PcbLib"))};Json.WriteAtomic(Path.Combine(global,"pending.json"),new{Previous=previous,Next=next});File.Copy(Path.Combine(source,"fixture.PcbLib"),Pcb(global),true);
  Ensure(store);Check(LibraryStore.Hash(Pcb(global))==previous.FootprintHash&&LibraryStore.Hash(Sch(global))==previous.SymbolHash&&!File.Exists(Path.Combine(global,"pending.json")),"an interrupted paired-file transaction rolls back before reuse");
  removed=store.DeleteDownloadedBatch(new[]{"C2","C3"},CancellationToken.None,null);Check(removed.DeletedCodes.Length==2&&removed.UnifiedError==null&&Status(global).Symbols==0&&Status(global).Footprints==0,"deleting the final cache entries publishes a valid empty total");
  Check(Status(projectFolder).Symbols==2&&File.Exists(first.SymbolPath)&&File.Exists(first.FootprintPath),"deleting all downloaded cache leaves project libraries intact");
  Console.WriteLine("RESULT "+checks+" unified library checks passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
