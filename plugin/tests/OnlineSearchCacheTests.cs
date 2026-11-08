using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LibrarySearch;
class OnlineSearchCacheTests {
 static int checks;
 const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
 static object Field(object value,string name){return value.GetType().GetField(name,Hidden).GetValue(value);}
 static object Call(object value,string name,params object[] args){return value.GetType().GetMethod(name,Hidden).Invoke(value,args);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static void Wait(Func<bool> complete){var limit=DateTime.UtcNow.AddSeconds(10);while(!complete()){if(DateTime.UtcNow>limit)throw new Exception("UI timeout");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
 static ModelSearchPage Page(int page,int size){return new ModelSearchPage{Page=page,PageSize=size,Total=35,Items=Enumerable.Range((page-1)*size+1,size).Select(i=>new ModelCandidate{Code="C"+i,Model="PART "+i,Package="SOT-23",HasLibrary=true}).ToArray()};}
 [STAThread] static int Main(){try{
  Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
  var type=typeof(ModelSearchClient).Assembly.GetType("LibrarySearch.InlineModelResults");
  using(var form=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),ClientSize=new Size(420,1200)})
  using(var view=(Control)Activator.CreateInstance(type,true)){
   view.Dock=DockStyle.Fill;form.Controls.Add(view);form.Show();
   var requests=new List<string>();int failures=0;var started=new ManualResetEvent(false);bool cancelNext=true;
   type.GetField("SizedSearchProvider",Hidden).SetValue(view,new Func<string,int,CancellationToken,int,ModelSearchPage>((q,p,t,size)=>{
    lock(requests)requests.Add(q+"/"+p+"/"+size);
    if(q=="failure"&&failures++==0)throw new IOException("deliberate test failure");
    if(q=="cancel"&&cancelNext){cancelNext=false;started.Set();t.WaitHandle.WaitOne();t.ThrowIfCancellationRequested();}
    return Page(p,size);
   }));
   Action<string,int> search=(q,p)=>{Call(view,"Search",q,p);Wait(()=>!(bool)Field(view,"running"));};
   search("ESP32",1);var rows=(ListView)Field(view,"rows");
   Check(requests.Count==1&&requests[0]=="ESP32/1/10","tall panel requests ten items only");
   Check(rows.Items.Count==10&&rows.Items[0].SubItems[2].Text=="C1","first page shows the first ten components");
   Check(requests.Count==1,"first page does not prefetch a later page");
   search("ESP32",2);Check(requests.Count==2&&requests[1]=="ESP32/2/10","next page is requested only on navigation");
   Check(rows.Items[0].SubItems[2].Text=="C11","second page has distinct component identities");
   form.ClientSize=new Size(280,520);search("ESP32",1);Check(requests.Count==2&&rows.Items[0].SubItems[2].Text=="C1","previous page is reused after resizing");
   search(" esp32 ",1);Check(requests.Count==2,"case and surrounding whitespace reuse the same keyword");
   search("TP4057",1);Check(requests.Count==3,"different keywords keep separate cached results");
   search("failure",1);search("failure",1);Check(requests.Count==5&&rows.Items.Count==10,"failed request is retried instead of cached");
   Call(view,"Search","cancel",1);Wait(()=>started.WaitOne(0));Call(view,"CancelSearch");Wait(()=>!(bool)Field(view,"running"));
   search("cancel",1);Check(requests.Count==7&&rows.Items.Count==10,"cancelled request does not block or poison a later search");
   started.Dispose();form.Close();
  }
  var cacheType=typeof(ModelSearchClient).Assembly.GetType("LibrarySearch.ModelSearchPageCache");
  DateTime now=new DateTime(2026,11,7,0,0,0,DateTimeKind.Utc);
  var cache=Activator.CreateInstance(cacheType,Hidden,null,new object[]{new Func<DateTime>(()=>now),TimeSpan.FromMinutes(5),2},null);
  var original=Page(1,10);Call(cache,"Store","A",1,10,original);original.Items[0].Model="changed source";
  Check(((ModelSearchPage)Call(cache,"Get","A",1,10)).Items[0].Model=="PART 1","cache owns a snapshot of network data");
  var returned=(ModelSearchPage)Call(cache,"Get","A",1,10);returned.Items[0].Model="changed result";
  Check(((ModelSearchPage)Call(cache,"Get","A",1,10)).Items[0].Model=="PART 1","caller cannot mutate later cached results");
  Check(Call(cache,"Get","A",2,10)==null&&Call(cache,"Get","A",1,50)==null,"pages and page sizes are separate cache keys");
  Call(cache,"Store","B",1,10,Page(1,10));Call(cache,"Get","A",1,10);Call(cache,"Store","C",1,10,Page(1,10));
  Check(Call(cache,"Get","B",1,10)==null&&Call(cache,"Get","A",1,10)!=null,"least recently used page is evicted at capacity");
  now=now.AddMinutes(5);Check(Call(cache,"Get","A",1,10)==null,"cached pages expire after five minutes");
  Call(cache,"Store","A",1,10,Page(1,10));Check(Call(cache,"Get","A",1,10)!=null,"expired page can be fetched and cached again");
  Console.WriteLine("RESULT "+checks+" online search cache checks passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
