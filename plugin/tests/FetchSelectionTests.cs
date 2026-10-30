using System;
using System.IO;
using System.Linq;
using System.Threading;
using LibrarySearch;
class FetchSelectionTests {
 static int Main(string[] args){try{
  var page=ModelSearchClient.Search("TP4057",1,CancellationToken.None);
  var selected=page.Items.Single(x=>x.Code=="C7473159");
  var store=new LibraryStore(Path.GetFullPath(args[0]));
  var fetched=store.Fetch(selected.Code,Path.GetFullPath(args[1]),false,CancellationToken.None,Console.WriteLine,SymbolAppearance.Effective(null),false);
  if(fetched.Records.Length!=2||!fetched.Records.Any(x=>x.Kind=="Symbol")||!fetched.Records.Any(x=>x.Kind=="Footprint"))throw new Exception("Missing paired libraries");
  foreach(var item in fetched.Records){LibraryStore.CheckLibrary(item.FilePath);Console.WriteLine("PASS: "+item.Code+" "+item.Kind+" "+item.Name);}
  var cached=store.Fetch(selected.Code,"missing-converter.exe",false,CancellationToken.None,Console.WriteLine,SymbolAppearance.Effective(null),false);
  if(!cached.Reused)throw new Exception("Cache was not reused");
  Console.WriteLine("PASS: model search -> chosen C7473159 -> paired conversion -> offline reuse");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
