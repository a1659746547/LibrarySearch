using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibrarySearch;
class PanelRenderingTests {
 static int checks;
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 static void CheckColumns(ListView rows,string name){
  rows.Refresh();Application.DoEvents();
  Check(rows.Columns.Cast<ColumnHeader>().Sum(c=>c.Width)<=rows.ClientSize.Width,name+" columns fit actual client area");
  Check((GetWindowLongPtr(rows.Handle,-16).ToInt64()&0x00100000)==0,name+" has no native horizontal scrollbar");
 }
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(o,a);}
 static void Check(bool v,string n){if(!v)throw new Exception(n);checks++;Console.WriteLine("PASS "+n);}
 static void Wait(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(10);while(!done()){if(DateTime.UtcNow>end)throw new Exception("UI timeout");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
 static void Save(Control c,string file){using(var b=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(b,c.ClientRectangle);b.Save(file);}}
 static void CheckRows(ListView list){
  using(var b=new Bitmap(list.Width,list.Height)){
   list.DrawToBitmap(b,list.ClientRectangle);
   for(int i=0;i<Math.Min(4,list.Items.Count);i++){
    var r=list.GetItemRect(i);int pixels=0;
    for(int y=Math.Max(0,r.Top+4);y<Math.Min(b.Height,r.Bottom-4);y++)for(int x=8;x<Math.Min(100,b.Width);x++){
     var c=b.GetPixel(x,y);if(c.R>160&&c.G>160&&c.B>160)pixels++;
    }
    Check(pixels>6,"row "+i+" remains painted after resize/filter");
   }
  }
 }
 [STAThread] static int Main(string[] args){try{
  Directory.CreateDirectory(args[0]);Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
  using(var form=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),ClientSize=new Size(360,900)})
  using(var panel=new LibraryPanel(args[1],new UserSettings{UsageNoticeVersion=1,LibraryRoot=Path.Combine(args[0],"isolated")})){
   form.Controls.Add(panel);form.Show();((System.Windows.Forms.Timer)Get(panel,"updateTimer")).Stop();Wait(()=>(bool)Get(panel,"loaded")&&!(bool)Get(panel,"busy"));
   var records=Enumerable.Range(0,80).Select(i=>new LibraryRecord{Code="C"+(100+i),Name="PART "+i,Kind="Symbol",Description="Description "+i,RelatedName="TO-220"}).ToArray();
   Set(panel,"downloaded",records);Get(panel,"source").GetType().GetProperty("SelectedIndex").SetValue(Get(panel,"source"),1,null);Wait(()=>((LibraryRecord[])Get(panel,"results")).Length==80);
   var list=(ListView)Get(panel,"list");list.EnsureVisible(70);Application.DoEvents();Call(panel,"SetResults",(object)records.Take(4).ToArray());Application.DoEvents();
   Console.WriteLine("STATE count="+list.Items.Count+" virtual="+list.VirtualListSize+" visible="+list.Visible+" bounds="+list.Bounds+" top="+(list.TopItem==null?"null":list.TopItem.Index.ToString())+" first="+list.GetItemRect(0));
   Check(list.TopItem!=null&&list.TopItem.Index==0,"short filtered list resets the previous scroll origin");
   foreach(var size in new[]{new Size(360,900),new Size(280,520),new Size(420,1000),new Size(360,900)}){
    form.ClientSize=size;Application.DoEvents();list.SelectedIndices.Clear();list.Items[2].Selected=true;Application.DoEvents();list.Invalidate();list.Update();CheckRows(list);CheckColumns(list,"downloaded "+size.Width);
   }
   Save(panel,Path.Combine(args[0],"downloaded-after-resize.png"));
   Get(panel,"source").GetType().GetProperty("SelectedIndex").SetValue(Get(panel,"source"),0,null);Application.DoEvents();
   var view=Get(panel,"modelResults");int requested=0;
   Set(view,"SizedSearchProvider",new Func<string,int,CancellationToken,int,ModelSearchPage>((q,p,t,count)=>{requested=count;return new ModelSearchPage{Page=p,PageSize=count,Total=100,Items=Enumerable.Range(0,count).Select(i=>new ModelCandidate{Code="C"+(p*count+i),Model="IRF3710 "+i,Manufacturer="Infineon",Package="TO-220AB",HasLibrary=true}).ToArray()};}));
   var query=(TextBox)Get(panel,"query");query.Text="IRF3710";Call(panel,"SearchRequested",false,false,null);Wait(()=>!(bool)Get(view,"running"));var rows=(ListView)Get(view,"rows");
   Check(requested>10,"tall panel requests more than ten online results");
   Check(rows.GetItemRect(rows.Items.Count-1).Bottom>=rows.ClientSize.Height-46,"online rows fill the result viewport");
   int stable=requested;form.ClientSize=new Size(280,520);Application.DoEvents();Call(view,"Search","IRF3710",2);Wait(()=>!(bool)Get(view,"running"));Check(requested==stable,"resizing while paging preserves page boundaries");Call(view,"Search","IRF3710",1);Wait(()=>!(bool)Get(view,"running"));Check(requested==stable,"previous page uses the same page size");
   foreach(int width in new[]{360,280,420,220,360}){
    form.ClientSize=new Size(width,900);Application.DoEvents();
    foreach(int count in new[]{2,50,3,50}){
     int requestedCount=count;
     Set(view,"SearchProvider",new Func<string,int,CancellationToken,ModelSearchPage>((q,p,t)=>new ModelSearchPage{Page=p,PageSize=50,Total=100,Items=Enumerable.Range(0,requestedCount).Select(i=>new ModelCandidate{Code="C"+(100+i),Model="IRF3710 "+i,Manufacturer="Infineon",Package="TO-220AB",HasLibrary=true}).ToArray()}));
     Call(view,"Search","SCROLL"+width+"X"+count,1);Wait(()=>!(bool)Get(view,"running"));
     CheckColumns(rows,"online "+width+"/"+count);
    }
   }
   form.ClientSize=new Size(360,900);Application.DoEvents();Save(panel,Path.Combine(args[0],"online-adaptive-page.png"));form.Close();
  }
  Console.WriteLine(checks+" rendering and paging checks passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
