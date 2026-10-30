using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;
class ConverterContractTests {
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);}
 static void Wait(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>deadline)throw new Exception("UI timeout");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
 [STAThread] static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),converter=Path.GetFullPath(args[1]),fixture=Path.GetFullPath(args[2]),descriptionFake=Path.GetFullPath(args[3]);
  Directory.CreateDirectory(root);Environment.SetEnvironmentVariable("LS_CONTRACT_SOURCE",fixture);
  string trace=Path.Combine(root,"trace.txt");Environment.SetEnvironmentVariable("LS_CONTRACT_TRACE",trace);
  var store=new LibraryStore(Path.Combine(root,"library"));Directory.CreateDirectory(Path.Combine(store.Root,"SourceCache"));
  string descriptionPath=Path.Combine(store.Root,"SourceCache","C7473159.description.json");
  Json.WriteAtomic(descriptionPath,new {Version="product-cn-1",Description="OLD-SUPPLIER-DESCRIPTION",DescriptionSource="https://item.szlcsc.com/example.html"});
  var first=store.Fetch("C7473159",converter,false,CancellationToken.None,m=>{});
  Check(first.Records.Length==2&&!first.Reused,"real restored converter emits paired native libraries through desktop pipeline");
  Check(first.Records[0].Description=="OLD-SUPPLIER-DESCRIPTION","ordinary fetch retains cached supplier description");
  Check(!Directory.GetFiles(Path.GetDirectoryName(first.Records[0].FilePath),"cad-source.json").Any(),"private CAD snapshot is removed before publishing a reusable/deletable version");
  Check(File.ReadAllText(trace).Contains("PINNED_REPAIR"),"slot verification consumes private CAD snapshot despite changed shared cache");
  Check(store.Fetch("C7473159","missing.exe",false,CancellationToken.None,m=>{}).Reused,"ready pair works without either executable or network");
  var refreshed=store.Fetch("C7473159",converter,true,CancellationToken.None,m=>{});
  Check(!refreshed.Reused&&refreshed.Records[0].Package.RelativeFolder!=first.Records[0].Package.RelativeFolder,"manual refresh publishes a new pair and preserves old version");
  Check(refreshed.Records[0].Description!="OLD-SUPPLIER-DESCRIPTION"&&refreshed.Records[0].Package.DescriptionVersion!="product-cn-1","manual refresh cannot silently certify old supplier text as current");
  string sch=LibraryStore.Hash(refreshed.Records[0].FilePath),pcb=LibraryStore.Hash(refreshed.Records[1].FilePath);
  Check(store.LoadDownloaded()[0].Package.DescriptionRefreshRequired,"supplier refresh intent survives reload and retry");
  var enriched=store.EnrichDescription(store.LoadDownloaded()[0],descriptionFake,CancellationToken.None);
  Check(enriched!=null&&enriched.Description=="CURRENT-SUPPLIER-DESCRIPTION","background refresh passes supplier-cache bypass to describe command");
  Check(sch==LibraryStore.Hash(refreshed.Records[0].FilePath)&&pcb==LibraryStore.Hash(refreshed.Records[1].FilePath),"description refresh never alters CAD library bytes");
  using(var form=new Form{ShowInTaskbar=false,Left=-20000,Top=-20000,Width=380,Height=700})using(var panel=new LibraryPanel(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(converter))),new UserSettings{LibraryRoot=store.Root,UsageNoticeVersion=1})){
   form.Controls.Add(panel);form.Show();((System.Windows.Forms.Timer)Field(panel,"updateTimer")).Stop();Wait(()=>(bool)Field(panel,"loaded")&&!(bool)Field(panel,"busy"));
   object inline=Field(panel,"modelResults");inline.GetType().GetField("SearchProvider",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(inline,new Func<string,int,CancellationToken,ModelSearchPage>((q,p,t)=>{throw new Exception("NETWORK SEARCH MUST NOT RUN FOR CACHED CODE");}));
   ((TextBox)Field(panel,"query")).Text=" c7473159 ";Call(panel,"SearchRequested",false,false,null);Wait(()=>!(bool)Field(panel,"busy"));
   Check(!(bool)Field(panel,"candidateMode")&&((LibraryRecord[])Field(panel,"results")).Length==2,"exact downloaded C number bypasses online search and returns cached pair");
   form.Close();
  }
  var removed=store.DeleteDownloaded("C7473159");Check(removed.Versions==2,"snapshot-based conversion remains compatible with guarded batch-cache deletion");
  Console.WriteLine("RESULT "+checks+" converter contract checks passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
