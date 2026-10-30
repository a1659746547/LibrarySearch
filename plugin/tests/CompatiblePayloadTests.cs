using System;
using System.IO;
using System.Reflection;
using LibrarySearch;
class CompatiblePayloadTests {
 [STAThread] static int Main(string[] a){try{
  string root=Path.GetFullPath(a[0]),parent=Path.Combine(root,"System","LibrarySearchBeta"),current=Path.Combine(parent,ReleaseInfo.Version),legacy=Path.Combine(parent,"1.1.0-beta.2");
  Directory.CreateDirectory(Path.Combine(current,"online","bin"));Directory.CreateDirectory(legacy);
  File.WriteAllText(Path.Combine(current,"Release.psd1"),ReleaseInfo.Version);File.WriteAllText(Path.Combine(current,"online","bin","easyeda2altium.exe"),"fixture-not-executed");
  var field=typeof(LibraryPanel).GetField("distribution",BindingFlags.Instance|BindingFlags.NonPublic);
  using(var panel=new LibraryPanel(legacy,new UserSettings{LibraryRoot=Path.Combine(root,"library"),UsageNoticeVersion=1})){
   if((string)field.GetValue(panel)!=current)throw new Exception("Old SDK adapter selected an old runtime directory");
  }
  Console.WriteLine("PASS legacy adapter resolves current runtime payload");
  using(var panel=new LibraryPanel(root,new UserSettings{LibraryRoot=Path.Combine(root,"library"),UsageNoticeVersion=1})){
   if((string)field.GetValue(panel)!=root)throw new Exception("Developer source directory was redirected");
  }
  Console.WriteLine("PASS ordinary developer/test directory stays explicit");
  if(typeof(LibraryStore).GetMethod("EnrichDescription",new[]{typeof(LibraryRecord),typeof(string),typeof(System.Threading.CancellationToken)})==null)throw new Exception("Original public signature removed");
  Console.WriteLine("PASS original three-argument API remains available");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
