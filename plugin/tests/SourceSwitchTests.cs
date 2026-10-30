using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;
class SourceSwitchTests {
 static int checks;
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(o,a);}
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Wait(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(10);while(!done()){if(DateTime.UtcNow>until)throw new Exception("UI wait timed out");Application.DoEvents();Thread.Sleep(2);}Application.DoEvents();}
 static void Source(object source,int index){source.GetType().GetProperty("SelectedIndex").SetValue(source,index,null);Application.DoEvents();}
 static ModelSearchPage Found(string name){return new ModelSearchPage{Page=1,Total=1,Items=new[]{new ModelCandidate{Code="C7473159",Model=name,Package="SOT-23-6",HasLibrary=true}}};}
 [STAThread] static int Main(string[] args){try{
  Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Control.CheckForIllegalCrossThreadCalls=true;Directory.CreateDirectory(args[0]);
  using(var oldStarted=new ManualResetEvent(false))using(var oldRelease=new ManualResetEvent(false))using(var oldFinished=new ManualResetEvent(false))using(var form=new Form{ShowInTaskbar=false,Location=new Point(-20000,-20000),StartPosition=FormStartPosition.Manual,ClientSize=new Size(420,850)})using(var panel=new LibraryPanel(args[1],new UserSettings{UsageNoticeVersion=1,LibraryRoot=Path.Combine(args[0],"isolated-library")})){
   form.Controls.Add(panel);form.Show();((System.Windows.Forms.Timer)Get(panel,"updateTimer")).Stop();Wait(()=>(bool)Get(panel,"loaded")&&!(bool)Get(panel,"busy"));var source=Get(panel,"source");var query=(TextBox)Get(panel,"query");var model=Get(panel,"modelResults");
   var tabs=(Control[])Get(source,"tabs");Check(tabs[0].Left<tabs[2].Left&&tabs[2].Left<tabs[1].Left,"visible source order is online, local, downloaded");Call(tabs[0],"OnKeyDown",new KeyEventArgs(Keys.Right));Check((int)source.GetType().GetProperty("SelectedIndex").GetValue(source,null)==2,"arrow navigation follows visible source order");Source(source,0);
   Set(panel,"downloaded",Enumerable.Range(0,20).Select(i=>new LibraryRecord{Name="R_"+i,Kind="Symbol",Code="C"+(i+1),RelatedName="0603"}).ToArray());
   Source(source,1);query.Text="R_12";Call(panel,"FilterResults");Wait(()=>((LibraryRecord[])Get(panel,"results")).Length==1);
   Source(source,0);query.Text="TP4057";Set(model,"SearchProvider",new Func<string,int,CancellationToken,ModelSearchPage>((q,p,t)=>Found(q)));Call(panel,"SearchRequested",false,false,null);Wait(()=>!(bool)Get(model,"running"));Check(((ListView)Get(model,"rows")).Items.Count==1,"online results loaded");
   Source(source,1);Wait(()=>((LibraryRecord[])Get(panel,"results")).Length==1);Check(query.Text=="R_12","downloaded query restored after online search");Check(!((Control)Get(panel,"progress")).Visible&&!((Control)Get(panel,"cancel")).Visible,"completed search leaves no loading state in downloaded tab");
   Source(source,2);Wait(()=>!((Control)Get(panel,"progress")).Visible);Check(!((Control)Get(panel,"cancel")).Visible&&((Control)Get(panel,"search")).Text=="选择目录","unconfigured local library shows folder action without pending operation");
   Source(source,0);Check(query.Text=="TP4057"&&((Control)model).Visible&&((ListView)Get(model,"rows")).Items.Count==1,"online query and results restored when returning");
   Set(model,"SearchProvider",new Func<string,int,CancellationToken,ModelSearchPage>((q,p,t)=>{if(q=="TPOLD"){oldStarted.Set();oldRelease.WaitOne();oldFinished.Set();return Found("STALE");}return Found("CURRENT");}));
   query.Text="TPOLD";Call(panel,"SearchRequested",false,false,null);Wait(()=>oldStarted.WaitOne(0));Check(((Control)source).Enabled,"source switching remains available during online search");
   Source(source,2);Check(!((Control)Get(panel,"progress")).Visible&&!((Control)Get(panel,"cancel")).Visible,"leaving running online search clears local loading state immediately");
   Source(source,0);query.Text="TPNEW";Call(panel,"SearchRequested",false,false,null);Wait(()=>!(bool)Get(model,"running"));oldRelease.Set();Wait(()=>oldFinished.WaitOne(0));for(int i=0;i<20;i++){Application.DoEvents();Thread.Sleep(2);}
   Check(((ListView)Get(model,"rows")).Items[0].Text=="CURRENT","late cancelled response cannot overwrite newer results");
   Check(!((Control)Get(panel,"progress")).Visible&&!((Control)Get(panel,"cancel")).Visible,"late cancelled response cannot leave stale progress visible");
   for(int i=0;i<4;i++){Source(source,1);Source(source,0);}Check(query.Text=="TPNEW"&&((Control)model).Visible,"repeated round trips preserve latest online results");
   Source(source,2);using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,panel.ClientRectangle);bitmap.Save(Path.Combine(args[0],"local-after-source-switch.png"));}form.Close();
  }
  Console.WriteLine(checks+" source switch checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
