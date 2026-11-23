using System;
using System.IO;
using System.Reflection;
using System.Collections;
using LibrarySearch;
class ProjectLibraryBindingTests {
 static int checks;static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static int Main(string[] args){try{
  var sdk=Assembly.LoadFrom(Path.GetFullPath(args[1]));var project=sdk.GetType("DXP.TestProject");var utils=sdk.GetType("DXP.Utils");
  string root=Path.GetFullPath(args[0]),path=Path.Combine(root,"test.PrjPcb");Directory.CreateDirectory(root);
  project.GetField("ProjectPath").SetValue(null,path);
  var members=(IList)project.GetField("Members").GetValue(null);var events=(IList)project.GetField("Events").GetValue(null);
  var type=typeof(LibraryStore).Assembly.GetType("LibrarySearch.HostLibraryRefresh",true);var refresh=type.GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic);var manager=Activator.CreateInstance(type,true);
  var files=new ProjectFiles{SymbolPath=Path.Combine(root,"LibrarySearch.SchLib"),FootprintPath=Path.Combine(root,"LibrarySearch.PcbLib"),Revision="one"};
  Func<bool> run=()=>{var arguments=refresh.GetParameters().Length==3?new object[]{files,null,Path.Combine(root,"test.SchDoc")+"\n"+path+"\nSCH"}:new object[]{files,null};return (bool)refresh.Invoke(manager,arguments);};
  Check(run()&&members.Count==2,"both project libraries are registered before first refresh");
  Check(events.Count==3&&((string)events[0]).StartsWith("attach:")&&((string)events[1]).StartsWith("attach:")&&(string)events[2]=="refresh","attachment order precedes native refresh");
  events.Clear();Check(!run()&&events.Count==0&&members.Count==2,"unchanged attached pair is reused");
  members.RemoveAt(1);events.Clear();Check(run()&&members.Count==2&&(string)events[1]=="refresh","removed footprint registration is restored even with unchanged revision");
  members.Clear();project.GetField("FailAttach").SetValue(null,true);events.Clear();bool failed=false;try{run();}catch(TargetInvocationException){failed=true;}
  Check(failed&&!events.Contains("refresh"),"failed attachment blocks refresh and placement preparation");
  project.GetField("FailAttach").SetValue(null,false);Check(run()&&members.Count==2,"attachment failure is retryable");
  project.GetField("ProjectPath").SetValue(null,Path.Combine(root,"other.PrjPcb"));events.Clear();failed=false;try{run();}catch(TargetInvocationException){failed=true;}
  Check(failed&&events.Count==0,"changed target project cannot attach to another project");
  Console.WriteLine("RESULT "+checks+" project binding checks passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
