using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;
class PanelLifecycleTests {
 static int checks;
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(o,a);}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static void Wait(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(10);while(!done()){if(DateTime.UtcNow>end)throw new Exception("UI wait timed out");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
 static void Root(LibraryPanel panel,string state){Check(((Control)Get(panel,"query")).Visible&&((Control)Get(panel,"source")).Visible&&((Control)Get(panel,"search")).Visible&&((Control)Get(panel,"resultsFrame")).Visible,state);}
 [STAThread] static int Main(string[] args){try{
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Control.CheckForIllegalCrossThreadCalls=true;
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
  using(var form=new Form{ShowInTaskbar=false,Location=new Point(-20000,-20000),StartPosition=FormStartPosition.Manual,ClientSize=new Size(420,800)})using(var panel=new LibraryPanel(args[1],new UserSettings{LibraryRoot=Path.Combine(output,"library"),UsageNoticeVersion=1})){
   form.Controls.Add(panel);form.Show();((System.Windows.Forms.Timer)Get(panel,"updateTimer")).Stop();Wait(()=>(bool)Get(panel,"loaded")&&!(bool)Get(panel,"busy"));var host=(Control)Get(panel,"pageHost");
   for(int i=0;i<3;i++){
    Call(panel,"ShowMore");Call(panel,"ChooseAppearance");Call(host,"ClosePage");Check(host.Visible,"appearance returns to settings "+i);Call(host,"ClosePage");Root(panel,"settings returns to usable search "+i);
   }
   Call(panel,"ShowMore");Call(panel,"ChooseAppearance");form.Hide();Call(host,"ClosePage");form.Show();Application.DoEvents();Check(host.Visible,"nested page survives hidden parent");Call(host,"ClosePage");Root(panel,"hidden nested return restores search");
   form.Hide();panel.ShowUserMessage("test","message completed while dock hidden");form.Show();Application.DoEvents();Check(host.Visible,"message opens after hidden parent restored");Call(host,"ClosePage");Root(panel,"hidden message return restores search");
   Call(panel,"ShowUsageNotice",false);Call(host,"ClosePage");Check(host.Visible,"usage notice returns to settings");Call(host,"ClosePage");Root(panel,"usage notice round trip restores search");
   Call(panel,"ShowMore");Call(panel,"ChooseAppearance");Call(host,"ClosePage");Call(host,"ClosePage");
   for(int i=0;i<4;i++){form.Hide();form.ClientSize=new Size(280+i*60,550+i*80);form.Show();Application.DoEvents();Root(panel,"dock hide and resize "+i);Check(((Control)Get(panel,"query")).Width>20,"search input remains sized "+i);}
   using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,panel.ClientRectangle);bitmap.Save(Path.Combine(output,"restored-panel.png"));}
   form.Close();
  }Console.WriteLine(checks+" lifecycle checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
