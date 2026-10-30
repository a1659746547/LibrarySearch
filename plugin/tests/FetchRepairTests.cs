using System;
using System.IO;
using System.Linq;
using System.Threading;
using LibrarySearch;
class FetchRepairTests {
 static int Main(string[] args){try{
  var store=new LibraryStore(Path.GetFullPath(args[0]));
  foreach(string code in new[]{"C50380974","C7473159"}){
   var fetched=store.Fetch(code,Path.GetFullPath(args[1]),false,CancellationToken.None,Console.WriteLine,SymbolAppearance.Classic(),false);
   if(fetched.Records.Length!=2||!fetched.Records.Any(x=>x.Kind=="Symbol")||!fetched.Records.Any(x=>x.Kind=="Footprint")||fetched.Records[0].Package.AcquisitionVersion!=ReleaseInfo.Acquisition)throw new Exception("Incomplete repaired package");
   foreach(var item in fetched.Records){LibraryStore.CheckLibrary(item.FilePath);Console.WriteLine("PASS: "+item.Code+" "+item.Kind+" "+item.Name);}
   var cached=store.Fetch(code,"missing-converter.exe",false,CancellationToken.None,Console.WriteLine,SymbolAppearance.Classic(),false);if(!cached.Reused)throw new Exception("Updated cache was not reused");Console.WriteLine("PASS: offline repaired cache reuse "+code);
   if(code=="C50380974"){
    var record=fetched.Records[0];record.Package.AcquisitionVersion="source-pin-width-3";bool rejected=false;try{LibraryStore.CopyToProject(record,Path.Combine(args[0],"not-an-actual-project.PrjPcb"));}catch(IOException error){rejected=error.Message.Contains("长孔");}if(!rejected)throw new Exception("Old cache placement guard failed");Console.WriteLine("PASS: old unverified online cache cannot be placed");record.Package.AcquisitionVersion=ReleaseInfo.Acquisition;
    var refreshed=store.Fetch(code,Path.GetFullPath(args[1]),true,CancellationToken.None,Console.WriteLine,SymbolAppearance.Classic(),false);
    if(refreshed.Reused||refreshed.Records[0].Package.RelativeFolder==record.Package.RelativeFolder||!File.Exists(record.FilePath))throw new Exception("Refresh did not preserve old version");Console.WriteLine("PASS: refresh official source, repaired new version, old library retained");
   }
  }
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
