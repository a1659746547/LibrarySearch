using System;
using System.IO;
using System.Diagnostics;
class ContractRepairStub {
 static string Arg(string[] a,string key){int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
 static int Main(string[] a){try{
  string code=Arg(a,"--lcsc-id"),cache=Arg(a,"--cache-dir"),trace=Environment.GetEnvironmentVariable("LS_CONTRACT_TRACE");
  if(Array.IndexOf(a,"--prepare")>=0){Directory.CreateDirectory(cache);File.Copy(Environment.GetEnvironmentVariable("LS_CONTRACT_SOURCE"),Path.Combine(cache,code+".json"),true);File.AppendAllText(trace,"PREPARE "+string.Join(" ",a)+Environment.NewLine);return 0;}
  string source=Arg(a,"--source");if(Path.GetFileName(source)!="cad-source.json")throw new Exception("repair was given mutable global source");
  string cacheRoot=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(source))),"SourceCache");
  File.WriteAllText(Path.Combine(cacheRoot,code+".json"),"{\"success\":true,\"result\":{\"lcsc\":{\"number\":\"C2\"}}}");
  File.AppendAllText(trace,"PINNED_REPAIR "+source+Environment.NewLine);
  string real=Environment.GetEnvironmentVariable("LS_CONTRACT_REPAIR");
  using(var child=Process.Start(new ProcessStartInfo(real,"--lcsc-id "+code+" --source \""+source+"\" --library \""+Arg(a,"--library")+"\""){UseShellExecute=false,CreateNoWindow=true})){child.WaitForExit();return child.ExitCode;}
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
