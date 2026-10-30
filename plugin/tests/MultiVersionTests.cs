using System;
using System.IO;
using System.Linq;
using LibrarySearch;
class MultiVersionTests {
 static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Reject(Action action,string label){bool rejected=false;try{action();}catch(IOException){rejected=true;}Check(rejected,label);}
 static string File(int major,string version,bool universal,string majors){return "{\"id\":\"ad"+major+"\",\"file\":\"LibrarySearch-"+(universal?"":"AD"+major+"-")+version+".zip\",\"size\":1024,\"sha256\":\""+new string('A',64)+"\",\"minAdMajor\":"+major+",\"maxAdMajor\":"+major+(majors==null?"":",\"adMajors\":"+majors)+"}";}
 static string Release(string version,string files,string channel){return "{\"version\":\""+version+"\",\"channel\":\""+channel+"\",\"date\":\"2026-10-27\",\"files\":["+files+"]}";}
 static int Main(){try{
  string version="1.1.0-beta.2",majors="[18,19,20,21,24,25,26]";
  int[] targets={18,19,20,21,24,25,26};
  foreach(int major in targets){
   ReleaseInfo.ConfigureHost("AD"+major+" / SDK test");Check(ReleaseInfo.AdMajor==major,"runtime host major "+major);
   var feed="{\"current\":"+Release(version,string.Join(",",targets.Select(m=>File(m,version,true,majors))),"beta")+"}";
   Check(UpdateClient.Parse(feed,major).FileName=="LibrarySearch-"+version+".zip","universal update for AD"+major);
   Check(UpdateClient.Parse("{\"current\":"+Release(version,File(major,version,false,null),"beta")+"}",major).MinAdMajor==major,"legacy package for AD"+major);
  }
  ReleaseInfo.ConfigureHost("preview");Check(ReleaseInfo.AdMajor==0,"preview does not pretend to be AD24");
  Reject(()=>UpdateClient.Parse("{\"current\":"+Release(version,File(22,version,true,majors),"beta")+"}",22),"noncontiguous supported list rejects AD22");
  Reject(()=>UpdateClient.Parse("{\"current\":"+Release(version,File(18,version,true,null),"beta")+"}",18),"universal package requires explicit support list");
  Reject(()=>UpdateClient.Parse("{\"current\":"+Release(version,File(18,version,true,"[18,18]"),"beta")+"}",18),"duplicate support list rejected");
  string older=Release("1.1.0",File(18,"1.1.0",false,null),"stable"),newer=Release("1.2.0-beta.1",File(24,"1.2.0-beta.1",false,null),"beta");
  string channels="{\"current\":"+newer+",\"channels\":{\"stable\":"+older+",\"beta\":"+newer+"}}";
  Check(UpdateClient.Parse(channels,18,true).Version=="1.1.0","newer incompatible channel does not hide AD18 release");
  Console.WriteLine("RESULT "+checks+" multi-version checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
