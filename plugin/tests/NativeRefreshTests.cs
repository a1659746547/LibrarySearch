using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;
class NativeRefreshTests {
 static int checks;static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Get(object target,string name){return target.GetType().GetField(name,flags).GetValue(target);}
 static void Set(object target,string name,object value){target.GetType().GetField(name,flags).SetValue(target,value);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,flags).Invoke(target,args);}
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Wait(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(20);while(!done()){if(DateTime.UtcNow>end)throw new Exception("UI wait timeout");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
 [STAThread] static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);var sdk=Assembly.LoadFrom(Path.GetFullPath(args[2]));var utils=sdk.GetType("DXP.Utils");Func<int> calls=()=> (int)utils.GetField("Calls").GetValue(null);int ui=Thread.CurrentThread.ManagedThreadId,placed=0,prepared=0;string revision="one",project=Path.Combine(root,"test.PrjPcb");string context=Path.Combine(root,"test.SchDoc")+"\n"+project+"\nSCH";
  using(var form=new Form{ShowInTaskbar=false,Left=-20000,Top=-20000,StartPosition=FormStartPosition.Manual})using(var panel=new LibraryPanel(args[1],new UserSettings{LibraryRoot=Path.Combine(root,"downloads"),UsageNoticeVersion=1})){
   form.Controls.Add(panel);form.Show();((System.Windows.Forms.Timer)Get(panel,"updateTimer")).Stop();Wait(()=>(bool)Get(panel,"loaded")&&!(bool)Get(panel,"busy"));
   var record=new LibraryRecord{Name="U_TEST",Kind="Symbol",Code="C1",RelatedName="PKG",Description="test",Package=new LibraryPackage{SymbolName="TEST",PackageName="PKG"}};
   Call(panel,"SetResults",new object[]{new[]{record}});((ListView)Get(panel,"list")).SelectedIndices.Add(0);Application.DoEvents();
   panel.PlacementContext=kind=>context;panel.PlaceRequested+=r=>{Check(calls()>0,"native refresh completed before the SDK placement callback");placed++;};
   Func<LibraryRecord,string,ProjectFiles> prepare=(r,p)=>{prepared++;if(Thread.CurrentThread.ManagedThreadId==ui)throw new Exception("preparation ran on UI thread");return new ProjectFiles{SymbolPath=Path.Combine(root,"project","LibrarySearch.SchLib"),FootprintPath=Path.Combine(root,"project","LibrarySearch.PcbLib"),Revision=revision};};Set(panel,"PreparePlacementOverride",prepare);
   Action request=()=>{Call(panel,"RequestPlace");Wait(()=>!(bool)Get(panel,"busy"));};request();
   Check(placed==1&&prepared==1&&calls()==1,"first placement prepares then refreshes exactly once");Check((int)utils.GetField("Thread").GetValue(null)==ui,"native library refresh runs on the UI thread");
   Check((string)utils.GetField("Command").GetValue(null)=="IntegratedLibrary:RefreshInstalledLibraries"&&(string)utils.GetField("Parameters").GetValue(null)=="AllLibraries=True","refresh uses the registered AD process and documented parameter");
   request();Check(placed==2&&calls()==1,"unchanged total reuses the refreshed content without a second refresh");
   revision="after-delete-and-reacquire";request();Check(placed==3&&calls()==2,"cache deletion and reacquisition trigger refresh when project total changes");
   revision="with-new-model";request();Check(placed==4&&calls()==3,"adding 3D or a new revision invalidates the library cache");
   revision="failed";utils.GetField("Fail").SetValue(null,true);request();Check(placed==4&&calls()==4&&((Control)Get(panel,"status")).Text.Contains("未能放置"),"failed native refresh blocks placement and reports an error");
   utils.GetField("Fail").SetValue(null,false);request();Check(placed==5&&calls()==5,"a failed refresh is retried instead of being remembered as successful");
   int before=placed;Set(panel,"PreparePlacementOverride",new Func<LibraryRecord,string,ProjectFiles>((r,p)=>{context="changed\n"+project+"\nSCH";return prepare(r,p);}));request();Check(placed==before&&calls()==5,"switching target context during preparation aborts before refresh and placement");
   Set(panel,"PreparePlacementOverride",new Func<LibraryRecord,string,ProjectFiles>((r,p)=>{throw new IOException("test source missing");}));request();Check(placed==before&&calls()==5,"missing cache or failed library preparation cannot issue a false placement");
   Set(panel,"PreparePlacementOverride",prepare);context=Path.Combine(root,"test.SchDoc")+"\n"+project+"\nSCH";revision="last";request();Check(placed==before+1&&calls()==6,"actions remain usable after a failed preparation");
   form.Close();
  }
  Console.WriteLine("RESULT "+checks+" native refresh checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
