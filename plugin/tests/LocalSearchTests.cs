using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;
class LocalSearchTests {
 static int checks;
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(o,a);}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static void Wait(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>end)throw new Exception("UI wait timed out");Application.DoEvents();Thread.Sleep(2);}Application.DoEvents();}
 static void Click(object o){Call(o,"OnClick",EventArgs.Empty);Application.DoEvents();}
 static LibraryRecord[] Rows(int count){return Enumerable.Range(0,count).Select(i=>new LibraryRecord{Name="RESISTOR_"+i,Code="LOCAL_"+i,Kind=i%2==0?"Symbol":"Footprint",Description="精密电阻 current sensing",RelatedName="R0603",PackageName="0603"}).ToArray();}
 static LibraryRecord[] Legacy(LibraryRecord[] rows,string text,int kind){return rows.Where(r=>(kind==0||r.Kind==(kind==1?"Symbol":"Footprint"))&&(text.Length==0||r.Name.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0||r.Code.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0||r.RelatedName.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0||(r.Description??"").IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0||(r.PackageName??"").IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();}
 [STAThread] static int Main(string[] args){try{
  var rows=Rows(200000);
  foreach(string term in new[]{"","resistor_199999","local_198","精密","sensing","0603","missing"})foreach(int kind in new[]{0,1,2}){
   var expected=Legacy(rows,term,kind);var actual=LibraryRecordSearch.Find(rows,term,kind,false,CancellationToken.None);
   Check(actual.Total==expected.Length&&actual.Records.SequenceEqual(expected.Take(500)),"search parity "+term+" kind="+kind);
  }
  var empty=LibraryRecordSearch.Find(new[]{new LibraryRecord{Kind="Symbol"}},"anything",0,false,CancellationToken.None);Check(empty.Total==0,"missing optional metadata is safe");
  using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{LibraryRecordSearch.Find(rows,"",0,false,cancelled.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){Check(true,"obsolete search can cancel before traversing records");}}
  var clock=Stopwatch.StartNew();for(int i=0;i<10;i++)LibraryRecordSearch.Find(rows,"resistor_199999",0,false,CancellationToken.None);Console.WriteLine("BENCH 200000 rows, rare keyword, mean_ms="+(clock.Elapsed.TotalMilliseconds/10).ToString("F2"));
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Control.CheckForIllegalCrossThreadCalls=true;
  var profileSettings=Path.Combine(ReleaseInfo.ProfileRoot,"settings.json");string before=File.Exists(profileSettings)?File.ReadAllText(profileSettings):null;
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
  using(var form=new Form{ShowInTaskbar=false,Location=new Point(-20000,-20000),StartPosition=FormStartPosition.Manual,ClientSize=new Size(420,800)})using(var panel=new LibraryPanel(args[1],new UserSettings{LibraryRoot=Path.Combine(output,"isolated-library"),LocalLibraryRoots=new[]{Path.Combine(output,"isolated-roots")},UsageNoticeVersion=1})){
   form.Controls.Add(panel);form.Show();((System.Windows.Forms.Timer)Get(panel,"updateTimer")).Stop();Wait(()=>(bool)Get(panel,"loaded")&&!(bool)Get(panel,"busy"));
   Set(panel,"local",rows);int scans=0;
   Set(panel,"LocalScanOverride",new Func<string[],CancellationToken,Action<string>,string,bool,LocalIndexBuild>((roots,t,p,old,force)=>{Interlocked.Increment(ref scans);t.WaitHandle.WaitOne();t.ThrowIfCancellationRequested();return null;}));
   var source=Get(panel,"source");source.GetType().GetProperty("SelectedIndex").SetValue(source,2,null);Wait(()=>scans==1);
   Check((bool)Get(panel,"localScanning")&&!(bool)Get(panel,"busy"),"directory scan does not block cached search");
   Check(((Control)Get(panel,"query")).Enabled&&((Control)Get(panel,"search")).Enabled,"input and search remain enabled during refresh");
   var query=(TextBox)Get(panel,"query");query.Text="resistor_199999";Call(panel,"SearchRequested",false,false,null);Wait(()=>((LibraryRecord[])Get(panel,"results")).Length==1);
   Check(((LibraryRecord[])Get(panel,"results"))[0].Name=="RESISTOR_199999"&&(bool)Get(panel,"localScanning"),"find cached part before background scan finishes");
   Call(panel,"StartLocalScan",new[]{Path.Combine(output,"isolated-roots")},false);Check(scans==1,"repeated refresh does not queue another scan");
   query.Text="resistor_12345";Call(panel,"FilterResults");query.Text="local_199998";Call(panel,"FilterResults");Wait(()=>((LibraryRecord[])Get(panel,"results")).Length==1&&((LibraryRecord[])Get(panel,"results"))[0].Code=="LOCAL_199998");Check(true,"latest query wins during rapid typing");
   Click(Get(panel,"cancel"));Wait(()=>!(bool)Get(panel,"localScanning"));Check(((Control)Get(panel,"query")).Enabled&&((LibraryRecord[])Get(panel,"local")).Length==200000,"cancel refresh preserves searchable cache");
   Set(panel,"LocalScanOverride",new Func<string[],CancellationToken,Action<string>,string,bool,LocalIndexBuild>((r,t,p,old,f)=>{throw new IOException("isolated inaccessible directory");}));Call(panel,"StartLocalScan",new[]{Path.Combine(output,"isolated-roots")},false);Wait(()=>!(bool)Get(panel,"localScanning"));
   Check(((Control)Get(panel,"query")).Enabled&&((LibraryRecord[])Get(panel,"local")).Length==200000,"refresh failure preserves cache and usable search");
   Set(panel,"lastLocalScan",DateTime.UtcNow);source.GetType().GetProperty("SelectedIndex").SetValue(source,0,null);Call(panel,"SetCandidateMode",true);source.GetType().GetProperty("SelectedIndex").SetValue(source,2,null);Wait(()=>((LibraryRecord[])Get(panel,"results")).Length==1);
   Check(((Control)Get(panel,"list")).Visible&&!((Control)Get(panel,"modelResults")).Visible,"switching from online results restores local list");
   Check(!(bool)Get(panel,"localScanning"),"recent directory check is not repeated on every source switch");
   Check(((System.Windows.Forms.Timer)Get(panel,"debounce")).Interval==100,"typing delay reduced to 100ms");
   form.Close();
  }
  Check((File.Exists(profileSettings)?File.ReadAllText(profileSettings):null)==before,"tests do not modify user settings");
  Console.WriteLine(checks+" local search checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
