using System;
using System.IO;
using LibrarySearch;
class ActualFeedTests {
 static int Main(string[] args){try{
  string json=File.ReadAllText(args[0]);int count=0;
  foreach(string value in args[2].Split(',')){
   int major=int.Parse(value);var release=UpdateClient.Parse(json,major,true);
   string expected=major==24?"LibrarySearch-AD24-"+args[1]+".zip":"LibrarySearch-"+args[1]+".zip";
   if(release.Version!=args[1]||release.FileName!=expected||release.DownloadUri.Host!="ad.kn21.xyz"||release.Size<1024)throw new Exception("Actual backend feed mismatch for AD"+major);
   count++;Console.WriteLine("PASS actual published fixture feed for AD"+major);
  }
  Console.WriteLine("RESULT "+count+" actual-feed interop checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
