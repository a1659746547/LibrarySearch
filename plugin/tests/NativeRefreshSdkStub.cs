// Test-only SDK shape. This assembly is never included in an installer.
namespace DXP {
 public sealed class TestView {}
 public sealed class TestClient {public TestView GetCurrentView(){return new TestView();}}
 public interface ITestMember {string DM_FullPath();}
 public interface ITestDocument {ITestProject DM_Project();}
 public interface ITestProject {
  string DM_ProjectFullPath();int DM_LogicalDocumentCount();ITestMember DM_LogicalDocuments(int index);void DM_AddSourceDocument(string path);
 }
 public interface ITestWorkspace {ITestDocument DM_GetDocumentFromPath(string path);}
 sealed class TestMember:ITestMember {readonly string path;internal TestMember(string value){path=value;}string ITestMember.DM_FullPath(){return path;}}
 sealed class TestDocument:ITestDocument {ITestProject ITestDocument.DM_Project(){return TestProject.Instance;}}
 sealed class TestWorkspace:ITestWorkspace {ITestDocument ITestWorkspace.DM_GetDocumentFromPath(string path){return new TestDocument();}}
 public sealed class TestProject:ITestProject {
  internal static readonly TestProject Instance=new TestProject();public static string ProjectPath;public static bool FailAttach;
  public static readonly System.Collections.Generic.List<string> Events=new System.Collections.Generic.List<string>();
  public static readonly System.Collections.Generic.List<string> Members=new System.Collections.Generic.List<string>();
  string ITestProject.DM_ProjectFullPath(){return ProjectPath;}
  int ITestProject.DM_LogicalDocumentCount(){return Members.Count;}
  ITestMember ITestProject.DM_LogicalDocuments(int index){return new TestMember(Members[index]);}
  void ITestProject.DM_AddSourceDocument(string path){if(FailAttach)throw new System.IO.IOException("test attach failure");Members.Add(path);Events.Add("attach:"+path);}
 }
 public static class GlobalVars {static readonly TestClient client=new TestClient();public static TestClient Client {get{return client;}}public static ITestWorkspace DXPWorkSpace {get{return new TestWorkspace();}}}
 public static class Utils {
  public static int Calls,Thread;public static string Command,Parameters;public static bool Fail;
  public static void RunCommand(string command,string parameters,TestView view){Calls++;Thread=System.Threading.Thread.CurrentThread.ManagedThreadId;Command=command;Parameters=parameters;TestProject.Events.Add("refresh");if(Fail)throw new System.IO.IOException("test refresh failure");}
 }
}
