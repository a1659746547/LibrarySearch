using System;
using System.IO;
using System.Linq;
using LibrarySearch;
class RuntimeDependencyTests {
 static int Main(string[] args){try{
  var page=ModelSearchClient.Parse("{\"code\":200,\"result\":{\"total\":1,\"productList\":[{\"code\":\"C2858491\",\"model\":\"ESP32-C3FH4\",\"hasDevice\":true}]}}",1);
  if(page.Items.Length!=1||page.Items[0].Model!="ESP32-C3FH4")throw new Exception("Search failed");
  if(typeof(Json).Assembly.GetReferencedAssemblies().Any(reference=>reference.Name=="System.Web.Extensions"))throw new Exception("Legacy web dependency remains");
  Directory.CreateDirectory(args[0]);string path=Path.Combine(args[0],"settings.json");
  Json.WriteAtomic(path,new UserSettings{LibraryRoot=args[0],Appearance=SymbolAppearance.Classic()});
  if(Json.Read<UserSettings>(path).LibraryRoot!=args[0])throw new Exception("Settings failed");
  Console.WriteLine("PASS search and settings without System.Web.Extensions; CLR "+Environment.Version);return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
