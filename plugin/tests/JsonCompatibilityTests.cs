using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using LibrarySearch.Serialization;
using LibrarySearch;
class JsonCompatibilityTests {
 public sealed class Row {public string Name {get;set;} public string[] Names {get;set;} public int Number; public bool Active {get;set;}}
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static int Main(string[] args){try{
  var serializer=new JavaScriptSerializer{MaxJsonLength=1024*1024,RecursionLimit=32};
  var row=serializer.Deserialize<Row>("{\"Name\":\"元件\\\\符号\",\"Names\":[\"A\",\"B\"],\"Number\":17,\"Active\":true}");
  Check(row.Name=="元件\\符号"&&row.Number==17&&row.Active&&row.Names.Length==2,"existing JSON properties and public fields load");
  var round=serializer.Deserialize<Row>(serializer.Serialize(row));Check(round.Number==17&&round.Name==row.Name,"JSON fields survive write and read");
  var root=(Dictionary<string,object>)serializer.DeserializeObject("{\"array\":[{\"value\":31}],\"boolean\":true,\"nothing\":null}");
  var item=(Dictionary<string,object>)((object[])root["array"])[0];Check(Convert.ToInt32(item["value"])==31&&(bool)root["boolean"]&&root["nothing"]==null,"legacy dictionary and object-array API shape retained");
  bool rejected=false;try{new JavaScriptSerializer{MaxJsonLength=8}.DeserializeObject("{\"tooLong\":true}");}catch{rejected=true;}Check(rejected,"oversize JSON rejected");
  rejected=false;try{serializer.DeserializeObject("{bad}");}catch{rejected=true;}Check(rejected,"malformed JSON rejected");
  string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);string settings=Path.Combine(folder,"settings.json");
  Json.WriteAtomic(settings,new UserSettings{LibraryRoot=folder,Appearance=SymbolAppearance.Classic()});var restored=Json.Read<UserSettings>(settings);Check(restored.LibraryRoot==folder&&restored.Appearance.BodyFill=="#FFFFB0","settings and symbol appearance round trip");
  Check(!typeof(Json).Assembly.GetReferencedAssemblies().Any(reference=>reference.Name=="System.Web.Extensions"),"core has no System.Web.Extensions dependency");
  var defaults=serializer.Deserialize<UserSettings>("{\"LibraryRoot\":\"D:\\\\Parts\",\"futureField\":true}");Check(defaults.Appearance!=null&&defaults.LocalLibraryRoots.Length==0,"older settings retain constructor defaults and ignore unknown fields");
  var insensitive=serializer.Deserialize<Row>("{\"name\":\"test\",\"number\":23}");Check(insensitive.Name=="test"&&insensitive.Number==23,"property and field names remain case insensitive");
  string special="中文 \"引脚\" \\ 路径\r\n\t<>&' \ud83d\udd0c";Check(serializer.Deserialize<string>(serializer.Serialize(special))==special,"Unicode and JSON escaping round trip");
  var keys=new Dictionary<string,object>{{"",1},{"a b",2},{"a:b",3},{"<tag>",4}};var keyRound=(Dictionary<string,object>)serializer.DeserializeObject(serializer.Serialize(keys));Check(keys.Keys.All(key=>Convert.ToInt32(keyRound[key])==Convert.ToInt32(keys[key])),"non-XML property names round trip");
  var request=(Dictionary<string,object>)serializer.DeserializeObject(serializer.Serialize(new{Index="D:\\元件\\local.json",Query="ESP32",Kind="Symbol"}));Check((string)request["Query"]=="ESP32"&&(string)request["Kind"]=="Symbol","anonymous native-converter requests serialize");
  rejected=false;try{new JavaScriptSerializer{RecursionLimit=3}.DeserializeObject("{\"a\":{\"b\":{\"c\":{\"d\":1}}}}");}catch{rejected=true;}Check(rejected,"excessive JSON nesting rejected");
  rejected=false;try{serializer.DeserializeObject("{\"a\":1} {\"b\":2}");}catch{rejected=true;}Check(rejected,"trailing JSON rejected");
  rejected=false;try{new JavaScriptSerializer{MaxJsonLength=8}.Serialize(new{Value="too long"});}catch{rejected=true;}Check(rejected,"oversize serialized JSON rejected");
  var model=ModelSearchClient.Parse("{\"code\":200,\"result\":{\"total\":1,\"productList\":[{\"code\":\"C2858491\",\"model\":\"ESP32-C3FH4\",\"brandName\":\"乐鑫\",\"hasDevice\":true}]}}",1);Check(model.Items.Length==1&&model.Items[0].Code=="C2858491"&&model.Items[0].HasLibrary,"ESP32 keyword response parses without web extensions");
  Console.WriteLine("STYLEKEY "+SymbolAppearance.Classic().Key());
  Console.WriteLine("RESULT 16 JSON compatibility checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
