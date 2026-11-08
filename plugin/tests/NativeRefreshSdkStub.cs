// Test-only SDK shape. This assembly is never included in an installer.
namespace DXP {
 public sealed class TestView {}
 public sealed class TestClient {public TestView GetCurrentView(){return new TestView();}}
 public static class GlobalVars {static readonly TestClient client=new TestClient();public static TestClient Client {get{return client;}}}
 public static class Utils {
  public static int Calls,Thread;public static string Command,Parameters;public static bool Fail;
  public static void RunCommand(string command,string parameters,TestView view){Calls++;Thread=System.Threading.Thread.CurrentThread.ManagedThreadId;Command=command;Parameters=parameters;if(Fail)throw new System.IO.IOException("test refresh failure");}
 }
}
