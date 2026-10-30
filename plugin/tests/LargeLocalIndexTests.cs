using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;
class LargeLocalIndexTests {
 public sealed class Fixture {public string IndexPath,Input,Cache;public int Count,Preview,Segments;public long SearchBytes;}
 static int checks;
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(o,a);}
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Wait(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(30);while(!done()){if(DateTime.UtcNow>until)throw new Exception("Large library UI wait timed out");Application.DoEvents();Thread.Sleep(2);}Application.DoEvents();}
 [STAThread] static int Main(string[] args){try{
  Directory.CreateDirectory(args[0]);var fixture=Json.Read<Fixture>(args[2]);string converter=Path.Combine(args[1],"online","bin","easyeda2altium.exe");
  string originalHash=LibraryStore.Hash(fixture.IndexPath);var sourceFiles=Directory.GetFiles(fixture.Input);var sourceHashes=sourceFiles.Select(LibraryStore.Hash).ToArray();
  var opened=AutomaticLocalIndex.Open(fixture.IndexPath,converter);
  Check(opened.Data.SchemaVersion==3&&opened.Catalog!=null,"segmented catalog opens after restart");Check(opened.Records.Length==500&&opened.Catalog.Count==250001,"AD loads a preview while preserving full record count");Check(fixture.SearchBytes>64L*1024*1024,"test covers search data larger than the old JSON limit");
  var clock=Stopwatch.StartNew();var tail=opened.Catalog.Search("part_0250000",0,CancellationToken.None);Console.WriteLine("BENCH native 250001 records, late exact name, elapsed_ms="+clock.ElapsedMilliseconds);
  Check(tail.Total==1&&tail.Records[0].Name=="PART_0250000","search finds records beyond the former 200000 cutoff");
  var description=opened.Catalog.Search("末尾",1,CancellationToken.None);Check(description.Total==1&&description.Records[0].ShortDescription.Contains("末尾"),"Chinese description search reaches the full catalog");
  var many=opened.Catalog.Search("QFN32",0,CancellationToken.None);Check(many.Total==250001&&many.Records.Length==500,"all matches are counted while only 500 rows return to AD");Check(many.Records.Select(r=>r.Name).SequenceEqual(Enumerable.Range(0,500).Select(i=>"PART_"+i.ToString("D7"))),"top results remain globally ordered across segments");
  var footprints=opened.Catalog.Search("precision",2,CancellationToken.None);Check(footprints.Total==125000&&footprints.Records.All(r=>r.Kind=="Footprint"),"classification searches all segments");
  using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{opened.Catalog.Search("QFN32",0,cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Check(true,"cancelled catalog query does not launch work");}}
  var buildAt=typeof(AutomaticLocalIndex).GetMethod("BuildAt",BindingFlags.Static|BindingFlags.NonPublic);
  var built=(LocalIndexBuild)buildAt.Invoke(null,new object[]{new[]{fixture.Input},converter,CancellationToken.None,new Action<string>(s=>{}),fixture.IndexPath,false,fixture.Cache});
  Check(built.Data.RecordCount==250001&&built.Data.CacheHits==2,"automatic rescan reuses every source without the 200000 failure");Check(built.Catalog.Search("part_0250000",0,CancellationToken.None).Total==1,"rescan preserves complete searchable data");
  Check(LibraryStore.Hash(fixture.IndexPath)==originalHash&&sourceFiles.Select(LibraryStore.Hash).SequenceEqual(sourceHashes),"scan and query leave original libraries and previous index unchanged");
  Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Control.CheckForIllegalCrossThreadCalls=true;
  using(var form=new Form{ShowInTaskbar=false,Location=new Point(-20000,-20000),StartPosition=FormStartPosition.Manual,ClientSize=new Size(420,850)})using(var panel=new LibraryPanel(args[1],new UserSettings{UsageNoticeVersion=1,LibraryRoot=Path.Combine(args[0],"empty-online"),LocalIndex=built.IndexPath,LocalLibraryRoots=new[]{fixture.Input}})){
   form.Controls.Add(panel);form.Show();((System.Windows.Forms.Timer)Get(panel,"updateTimer")).Stop();Wait(()=>(bool)Get(panel,"loaded")&&!(bool)Get(panel,"busy"));Set(panel,"lastLocalScan",DateTime.UtcNow);var source=Get(panel,"source");source.GetType().GetProperty("SelectedIndex").SetValue(source,2,null);Wait(()=>Get(panel,"filterCancellation")==null);
   Check(((Label)Get(panel,"resultCount")).Text.Contains("250001"),"panel initial count describes the whole library");
   var query=(TextBox)Get(panel,"query");query.Text="part_0250000";Call(panel,"SearchRequested",false,false,null);Wait(()=>Get(panel,"filterCancellation")==null&&((LibraryRecord[])Get(panel,"results")).Length==1);Check(((LibraryRecord[])Get(panel,"results"))[0].Name=="PART_0250000","panel searches the full catalog rather than only its preview");
   Check(!((Control)Get(panel,"progress")).Visible&&!((Control)Get(panel,"cancel")).Visible,"catalog completion clears progress and cancellation state");
   using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,panel.ClientRectangle);bitmap.Save(Path.Combine(args[0],"large-library-result.png"));}form.Close();
  }
  Console.WriteLine(checks+" large local library checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
