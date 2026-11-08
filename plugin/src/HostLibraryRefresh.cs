using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace LibrarySearch {
 // The SDK adapters already expose PlacementContext and invoke PlaceRequested.
 // Keep their ABI unchanged: prepare files first, then refresh through the same
 // loaded DXP.Utils.RunCommand API used by every adapter, on the AD UI thread.
 internal sealed class HostLibraryRefresh {
  readonly Dictionary<string,string> refreshed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  static Assembly Sdk {get{return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="Altium.Dxp.Classes");}}
  internal static bool Available {get{return Sdk!=null;}}
  internal bool Refresh(ProjectFiles files,Action action=null){
   if(files==null||string.IsNullOrEmpty(files.SymbolPath)||string.IsNullOrEmpty(files.FootprintPath)||string.IsNullOrEmpty(files.Revision))throw new IOException("工程总库校验信息不完整，请重新获取元件。");
   string key=Path.GetFullPath(files.SymbolPath)+"|"+Path.GetFullPath(files.FootprintPath),previous;
   if(refreshed.TryGetValue(key,out previous)&&previous==files.Revision)return false;
   UserLog.Write("Refresh project libraries begin "+key);
   if(action!=null)action();else Execute();
   refreshed[key]=files.Revision;UserLog.Write("Refresh project libraries returned "+key);return true;
  }
  static void Execute(){
   var sdk=Sdk;if(sdk==null)throw new IOException("未找到 AD 库刷新接口。");
   var globals=sdk.GetType("DXP.GlobalVars",true);var getter=globals.GetMethod("get_Client",BindingFlags.Public|BindingFlags.Static);
   if(getter==null)throw new IOException("当前 AD 不支持库刷新接口。");
   try{
    object client=getter.Invoke(null,null);if(client==null)throw new IOException("AD 客户端尚未就绪。");
    var getView=getter.ReturnType.GetMethod("GetCurrentView");if(getView==null)throw new IOException("当前 AD 无法获取放置视图。");
    object view=getView.Invoke(client,null);if(view==null)throw new IOException("请先激活目标原理图或 PCB。");
    var command=sdk.GetType("DXP.Utils",true).GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name=="RunCommand"&&m.GetParameters().Length==3&&m.GetParameters()[0].ParameterType==typeof(string)&&m.GetParameters()[1].ParameterType==typeof(string)&&m.GetParameters()[2].ParameterType.IsInstanceOfType(view));
    if(command==null)throw new IOException("当前 AD 无法执行库刷新命令。");
    command.Invoke(null,new object[]{"IntegratedLibrary:RefreshInstalledLibraries","AllLibraries=True",view});
   }catch(TargetInvocationException error){throw new IOException("AD 刷新总库失败，请关闭打开的总库文档后重试。",error.InnerException??error);}
  }
  internal static string ProjectPath(string context,string kind){
   string[] parts=(context??"").Split('\n');
   if(parts.Length!=3||parts[2]!=(kind=="Symbol"?"SCH":"PCB")||!Path.IsPathRooted(parts[1])||!Path.GetExtension(parts[1]).Equals(".PrjPcb",StringComparison.OrdinalIgnoreCase))throw new IOException("无法确认目标工程，请激活工程中的原理图或 PCB 后重试。");
   return parts[1];
  }
 }
}
