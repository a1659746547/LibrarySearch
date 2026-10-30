using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using LibrarySearch;
class ResultScrollBarTests {
 static int checks;
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(o,a);}
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 [STAThread] static int Main(string[] args){try{
  Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Directory.CreateDirectory(args[0]);
  using(var form=new Form{ShowInTaskbar=false,Location=new Point(-20000,-20000),StartPosition=FormStartPosition.Manual,ClientSize=new Size(340,600)})using(var frame=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(45,45,45)})using(var list=new ResultList{Bounds=new Rectangle(4,4,332,592),View=View.Details,BorderStyle=BorderStyle.None,VirtualMode=true}){
   form.Controls.Add(frame);frame.Controls.Add(list);list.Columns.Add("Name",210);list.RetrieveVirtualItem+=(s,e)=>e.Item=new ListViewItem("Part "+e.ItemIndex);list.DrawSubItem+=(s,e)=>{e.DrawDefault=true;};list.VirtualListSize=500;form.Show();list.Refresh();Application.DoEvents();var bar=(Control)Get(list,"scrollBar");Call(bar,"RefreshFromOwner");
   Check(bar.Visible&&bar.Width>0&&bar.Height>0,"dark scrollbar appears when results exceed visible rows");
   Check(bar.Parent==frame&&bar.Bounds.Right<=frame.Width,"scrollbar stays within the result frame");
   using(var image=new Bitmap(bar.Width,bar.Height)){bar.DrawToBitmap(image,bar.ClientRectangle);int bright=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var pixel=image.GetPixel(x,y);if(pixel.R>190&&pixel.G>190&&pixel.B>190)bright++;}Check(bright==0,"scrollbar surface contains no white track or border");image.Save(Path.Combine(args[0],"dark-scrollbar.png"));}
   using(var composite=new Bitmap(frame.Width,frame.Height)){frame.DrawToBitmap(composite,frame.ClientRectangle);int bright=0;for(int y=bar.Top;y<bar.Bottom;y++)for(int x=bar.Left;x<bar.Right;x++){var pixel=composite.GetPixel(x,y);if(pixel.R>190&&pixel.G>190&&pixel.B>190)bright++;}Check(bright==0,"native nonclient painting cannot overwrite dark scrollbar in the full panel");composite.Save(Path.Combine(args[0],"composited-scrollbar.png"));}
   Call(bar,"ScrollTo",250);Application.DoEvents();Check(list.TopItem.Index>=249&&list.TopItem.Index<=251,"dark thumb scrolls native virtual rows");
   Call(bar,"ScrollTo",0);Application.DoEvents();Check(list.TopItem.Index==0,"scroll can return to first row");
   list.Visible=false;Application.DoEvents();Check(!bar.Visible,"scrollbar hides with its result list");list.Visible=true;list.Refresh();Application.DoEvents();Check(bar.Visible,"scrollbar returns with its list");
   list.VirtualListSize=2;list.Refresh();Application.DoEvents();Check(!bar.Visible,"scrollbar disappears when rows fit");list.VirtualListSize=500;list.Refresh();Application.DoEvents();Check(bar.Visible,"scrollbar updates after new results");
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,form.ClientRectangle);bitmap.Save(Path.Combine(args[0],"result-list-with-dark-scrollbar.png"));}form.Close();
  }
  Console.WriteLine(checks+" scrollbar checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
