using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using LibrarySearch;
using LibrarySearch.Serialization;
class Default3dTests {
 static int checks;static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static object Get(object o,string n){return o.GetType().GetField(n,flags).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,flags).SetValue(o,v);}
 static void Call(object o,string n,params object[] args){o.GetType().GetMethod(n,flags).Invoke(o,args);}
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Wait(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>end)throw new Exception("UI timeout");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
 static Control Page(object host){return ((Control)Get(host,"body")).Controls[0];}
 [STAThread] static int Main(string[] args){try{
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);var serializer=new JavaScriptSerializer();
  Check(new UserSettings().Download3dByDefault,"new settings enable 3D acquisition by default");
  Check(serializer.Deserialize<UserSettings>("{\"LibraryRoot\":\"D:\\\\Parts\"}").Download3dByDefault,"old settings without the option migrate to enabled");
  Check(!serializer.Deserialize<UserSettings>("{\"Download3dByDefault\":false}").Download3dByDefault,"explicit saved false survives deserialization");
  string settingsPath=Path.Combine(root,"settings.json");Json.WriteAtomic(settingsPath,new UserSettings{Download3dByDefault=false});Check(!Json.Read<UserSettings>(settingsPath).Download3dByDefault,"disabled option persists through JSON write and read");
  var store=new LibraryStore(Path.Combine(root,"no-model-cache"));string folder=Path.Combine(store.Root,"Components","C9","fixture");Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(store.Root,"Index"));byte[] ole={0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1};string sch=Path.Combine(folder,"C9.SchLib"),pcb=Path.Combine(folder,"C9.PcbLib");File.WriteAllBytes(sch,ole);File.WriteAllBytes(pcb,ole);
  var style=SymbolAppearance.Classic();var package=new LibraryPackage{Code="C9",Format=ReleaseInfo.Format,AcquisitionVersion=ReleaseInfo.Acquisition,PinGeometryVersion="svg-line-1",AppearanceKey=style.Key(),SymbolName="R",FootprintName="R0603",RelativeFolder=Path.Combine("Components","C9","fixture"),SymbolHash=LibraryStore.Hash(sch),FootprintHash=LibraryStore.Hash(pcb),Model3dStatus="unavailable"};Json.WriteAtomic(Path.Combine(store.Root,"Index","C9.json"),package);
  Check(store.Fetch("C9","missing.exe",false,CancellationToken.None,m=>{},style,true).Reused,"known unavailable 3D does not repeatedly redownload a ready pair");
  var settings=new UserSettings{UsageNoticeVersion=1,LibraryRoot=Path.Combine(root,"ui-library")};bool? requested=null;int saves=0;
  using(var form=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Left=-20000,Top=-20000,ClientSize=new Size(340,650)})using(var panel=new LibraryPanel(args[1],settings)){
   form.Controls.Add(panel);var hiddenHandle=panel.Handle;var listHandle=((ListView)Get(panel,"list")).Handle;((System.Windows.Forms.Timer)Get(panel,"updateTimer")).Stop();Wait(()=>(bool)Get(panel,"loaded")&&!(bool)Get(panel,"busy"));object host=Get(panel,"pageHost");
   Set(panel,"SaveSettingsOverride",new Action<UserSettings>(s=>{Json.WriteAtomic(settingsPath,s);saves++;}));
   Set(panel,"FetchOptionsOverride",new Func<string,bool,CancellationToken,FetchResult>((code,include3d,token)=>{requested=include3d;return new FetchResult{Records=new[]{new LibraryRecord{Name="TEST",Kind="Symbol",Code=code,RelatedName="PKG",Description="test",LibraryRoot=settings.LibraryRoot,FilePath=Path.Combine(settings.LibraryRoot,"fixture.SchLib"),Package=new LibraryPackage{Code=code,SymbolName="TEST",PackageName="PKG",DescriptionVersion="product-cn-1",Model3dStatus="unavailable"}}}};}));
   Action<bool> acquire=explicit3d=>{requested=null;Call(panel,"AcquireComponent",false,explicit3d,null,false,"C1");Wait(()=>!(bool)Get(panel,"busy"));};acquire(false);Check(requested==true,"ordinary acquisition requests 3D by default");Check(((Control)Get(panel,"status")).Text=="元件已就绪。","ordinary acquisition does not display no-3D notices");
   Call(panel,"ShowMore");var page=Page(host);var box=(CheckBox)Get(page,"download3d");Check(box.Checked&&box.Parent!=null,"3D option is present and checked in settings");box.Checked=false;
   foreach(int section in new[]{1,2,3,0})Call(page,"ShowSection",section);Check(!box.IsDisposed&&!box.Checked,"switching settings categories preserves the unchecked option");Call(host,"ClosePage");Check(saves==1&&!settings.Download3dByDefault&&!Json.Read<UserSettings>(settingsPath).Download3dByDefault,"leaving settings saves the disabled choice");
   acquire(false);Check(requested==false,"ordinary acquisition respects the disabled choice");acquire(true);Check(requested==true,"manual 3D acquisition remains available when default is disabled");
   Call(panel,"ShowMore");page=Page(host);box=(CheckBox)Get(page,"download3d");Check(!box.Checked,"reopening settings restores the disabled choice");
   using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,panel.ClientRectangle);bitmap.Save(Path.Combine(root,"settings-3d.png"));}
   box.Checked=true;Call(host,"ClosePage");Check(saves==2&&settings.Download3dByDefault,"3D acquisition can be enabled again");acquire(false);Check(requested==true,"reenabled preference applies to the next ordinary acquisition");
   Call(panel,"ShowMore");page=Page(host);((CheckBox)Get(page,"download3d")).Checked=false;Set(panel,"SaveSettingsOverride",new Action<UserSettings>(s=>{throw new IOException("test save failure");}));Call(host,"ClosePage");Check(settings.Download3dByDefault,"failed settings write retains the previous effective preference");
   form.Close();
  }
  Check(Json.Read<UserSettings>(settingsPath).Download3dByDefault,"last successful choice remains saved after panel disposal");
  Console.WriteLine("RESULT "+checks+" default 3D checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
