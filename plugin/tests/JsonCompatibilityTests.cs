using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
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
  Console.WriteLine("STYLEKEY "+SymbolAppearance.Classic().Key());
  Console.WriteLine("RESULT 6 JSON compatibility checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
