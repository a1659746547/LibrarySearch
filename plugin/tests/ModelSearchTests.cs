using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using LibrarySearch;

class ModelSearchTests {
 static int checks;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
 static void Reject(Action action,string label){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,label);}
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
 static void Set(object target,string name,object value){target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);}
 static void Call(object target,string name){target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);}
 [STAThread] static int Main(string[] args){try{
  Check(ModelSearchClient.IsCode(" c7473159 "),"C number routes directly");
  Check(!ModelSearchClient.IsCode("TP4057")&&!ModelSearchClient.IsCode("C7473159x"),"models do not route as C numbers");
  Reject(()=>ModelSearchClient.ValidateQuery(" "),"reject empty query");Reject(()=>ModelSearchClient.ValidateQuery(new string('x',101)),"reject long query");Reject(()=>ModelSearchClient.ValidateQuery("TP\n4057"),"reject control characters");
  Check(ModelSearchClient.ValidateQuery("  STM32F103C8T6  ")=="STM32F103C8T6","trim model");
  string json="{\"code\":200,\"result\":{\"total\":4,\"productList\":[{\"code\":\"C1\",\"model\":\"<b>TP4057</b>\",\"brandName\":\"A&amp;B\",\"standard\":\"SOT23-6\",\"hasDevice\":\"uuid\",\"description\":\"a\",\"Description\":\"b\"},{\"code\":\"C1\"},{\"code\":\"C2\",\"hasDevice\":false},{\"code\":\"../bad\"}]}}";
  var parsed=ModelSearchClient.Parse(json,1);
  Check(parsed.Items.Length==2&&parsed.Total==4,"deduplicate valid C numbers and retain total");
  Check(parsed.Items[0].Model=="TP4057"&&parsed.Items[0].Manufacturer=="A&B","strip highlight markup and decode text");
  Check(parsed.Items[0].HasLibrary&&!parsed.Items[1].HasLibrary,"available and unavailable source libraries");
  Reject(()=>ModelSearchClient.Parse("{\"code\":403}",1),"service failure is not empty success");Reject(()=>ModelSearchClient.Parse("{\"code\":200,\"result\":{}}",1),"schema changes are reported");
  Check(ModelSearchClient.Parse("{\"code\":200,\"result\":{\"total\":0,\"productList\":[]}}",1).Items.Length==0,"empty search");
  var token=new CancellationToken(true);Reject(()=>ModelSearchClient.Search("TP4057",1,token),"pre-cancelled request does not run");
  if(args.Length>0){
   var real=ModelSearchClient.Parse(File.ReadAllText(Path.Combine(args[0],"tp4057.json")),1);
   Check(real.Total>1&&real.Items.Length>1&&real.Items.All(x=>ModelSearchClient.IsCode(x.Code)),"parse actual TP4057 response");
   var page2=ModelSearchClient.Parse(File.ReadAllText(Path.Combine(args[0],"tp4057-page2.json")),2);
   Check(page2.Page==2&&page2.Items.Length>0&&!real.Items.Select(x=>x.Code).Intersect(page2.Items.Select(x=>x.Code)).Any(),"actual second page has distinct candidates");
   Check(ModelSearchClient.Parse(File.ReadAllText(Path.Combine(args[0],"stm32.json")),1).Items.Any(x=>x.Model.IndexOf("STM32F103C8T6",StringComparison.OrdinalIgnoreCase)>=0),"parse actual STM32 model search");
  }
  // Embedded result controls and direct placement are exercised by CustomerFlowTests.
  if(args.Length>1&&args[1]=="--live"){
   var live=ModelSearchClient.Search("TP4057",1,CancellationToken.None);Check(live.Items.Length>0,"actual .NET HTTPS search");
   var more=ModelSearchClient.Search("TP4057",2,CancellationToken.None);Check(more.Items.Length>0&&!live.Items.Select(x=>x.Code).Intersect(more.Items.Select(x=>x.Code)).Any(),"actual .NET currPage pagination");
   var empty=ModelSearchClient.Search("ZZZLSNORESULT20261026XYZ",1,CancellationToken.None);Check(empty.Total==0&&empty.Items.Length==0,"actual no-result response");
   var numbered=ModelSearchClient.Search("C7473159",1,CancellationToken.None);Check(numbered.Total==1&&numbered.Items[0].Code=="C7473159","actual C number uses the same product-search endpoint");
  }
  Console.WriteLine("RESULT: "+checks+" checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
