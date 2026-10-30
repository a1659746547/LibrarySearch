using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DXP;
using RT_ClientServerInterface;

namespace CSharpPlugin {
 [ComVisible(true),ClassInterface(ClassInterfaceType.AutoDispatch)]
 public class PluginFactory {
  public object InvokePluginFactory(IClient client){
   string system=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
   string core=Path.Combine(system,LibrarySearch.HostRuntime.CoreFileName);
   if(!File.Exists(core))throw new FileNotFoundException("LibrarySearch shared core is missing. Reinstall the matching complete package.",core);
   Assembly.LoadFrom(core);
   return CreatePlugin();
  }
  [MethodImpl(MethodImplOptions.NoInlining)]
  static object CreatePlugin(){try{LibrarySearch.ReleaseInfo.ConfigureHost("AD"+LibrarySearch.HostRuntime.AdMajor+" / SDK "+LibrarySearch.HostRuntime.SdkVersion);LibrarySearch.UserLog.Write("Factory entered; host="+LibrarySearch.ReleaseInfo.HostTarget);return new LibrarySearch.SearchModule();}catch(Exception error){LibrarySearch.UserLog.Write("Factory",error);throw;}}
 }
}
namespace LibrarySearch {
 [ComVisible(true),ClassInterface(ClassInterfaceType.AutoDispatch)]
 public class SearchModule : ServerModule {
  IServerView panel;
  bool registered;
  public SearchModule():base("LibrarySearch"){UserLog.Write("Module initialized");}
  protected override IServerDocument NewDocumentInstance(string kind,string filename){return null;}
  protected override void InitializeCommands(){((CommandLauncher)CommandLauncher).RegisterCommand("ShowSearch",ShowSearch,null);}
  void EnsurePanelRegistered(){if(registered)return;if(GlobalVars.Client.GetServerViewFromName("LibrarySearch")==null)GlobalVars.Client.AddServerView(CreateServerView("LibrarySearch"));registered=true;}
  public override void ApplicationIdle(){if(registered)return;try{EnsurePanelRegistered();}catch(Exception error){UserLog.Write("Panel registration",error);registered=true;}}
  void ShowSearch(IServerDocumentView view,StringBuilder parameters){try{EnsurePanelRegistered();GlobalVars.GUIManager.SetPanelVisibleInCurrentForm("LibrarySearch",true);GlobalVars.GUIManager.SetPanelActiveInCurrentForm("LibrarySearch");}catch(Exception error){UserLog.Write("Show panel",error);MessageBox.Show(error.Message,ReleaseInfo.Title);}}
  public override IServerView CreateServerView(string name){if(panel==null){panel=new SearchPanelView();AddServerView(panel);}return panel;}
 }
 public class SearchPanelView : ServerPanelView {public SearchPanelView():base(new SearchForm(),"LibrarySearch","LibrarySearch") {}}
 public sealed class SearchForm : ServerPanelForm {
  readonly LibraryPanel panel;
  bool operationPending;
  public SearchForm(){
   Text="LibrarySearch";Width=380;Height=720;
   string system=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
   panel=new LibraryPanel(Path.Combine(system,"LibrarySearchBeta",ReleaseInfo.Version));panel.PlaceRequested+=PlaceRecord;panel.PlacementContext=CapturePlacementContext;panel.OpenRequested+=OpenLibrary;Controls.Add(panel);
  }
  string CapturePlacementContext(string kind){
   var view=GlobalVars.Client.GetCurrentView();var document=view==null?null:view.GetOwnerDocument();string expected=kind=="Symbol"?"SCH":"PCB";
   if(document==null||!string.Equals(document.GetKind(),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(kind=="Symbol"?"请先打开并激活工程中的原理图。":"请先打开并激活工程中的 PCB。");
   string target=document.GetFileName();var workspace=GlobalVars.DXPWorkSpace;var member=workspace==null?null:workspace.DM_GetDocumentFromPath(target);var project=member==null?null:member.DM_Project();string path=project==null?"":project.DM_ProjectFullPath();
   if(!File.Exists(target)||!File.Exists(path)||!Path.GetExtension(path).Equals(".PrjPcb",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先打开 PCB 工程，将当前文档加入工程，并保存全部文件。");
   return target+"\n"+path+"\n"+expected;
  }
  void Post(Action action){if(IsDisposed||Disposing||!IsHandleCreated)return;try{BeginInvoke(new Action(delegate{if(!IsDisposed&&!Disposing)action();}));}catch(InvalidOperationException){}}
  void PlaceRecord(LibraryRecord record){
   if(operationPending)return;
   try{
    if(record.Name.IndexOf('|')>=0||record.FilePath.IndexOf('|')>=0)throw new IOException("名称或路径含 AD 命令分隔符，无法放置。");
    var view=GlobalVars.Client.GetCurrentView();var document=view==null?null:view.GetOwnerDocument();string kind=record.Kind=="Symbol"?"SCH":"PCB";
    if(document==null||!string.Equals(document.GetKind(),kind,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(record.Kind=="Symbol"?"请先打开并激活工程中的原理图（SchDoc）。":"请先打开并激活工程中的 PCB（PcbDoc）。");
    string target=document.GetFileName(),projectPath="";
    if(record.Package!=null){
     var workspace=GlobalVars.DXPWorkSpace;var targetDocument=workspace==null?null:workspace.DM_GetDocumentFromPath(target);var project=targetDocument==null?null:targetDocument.DM_Project();projectPath=project==null?"":project.DM_ProjectFullPath();
     if(!File.Exists(target)||string.IsNullOrEmpty(projectPath)||!File.Exists(projectPath)||!Path.GetExtension(projectPath).Equals(".PrjPcb",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先创建或打开 PCB 工程，将当前文档加入工程，并执行“保存全部”。在线元件的配套库将保存在工程目录中。");
    }
    operationPending=true;panel.SetNativeBusy(true,record.Package==null?"正在检查本地库…":"正在将符号和封装保存到工程…");string savedProject=projectPath;
    ThreadPool.QueueUserWorkItem(delegate{
     ProjectFiles copies=null;Exception failure=null;
     try{if(!File.Exists(record.FilePath))throw new FileNotFoundException("来源库不存在，请重新获取或检查本地库目录。",record.FilePath);if(record.Package!=null)copies=LibraryStore.CopyToProject(record,savedProject);}
     catch(Exception error){failure=error;}
     Post(delegate{
      try{
       if(failure!=null)throw failure;
       var current=GlobalVars.Client.GetCurrentView();var currentDocument=current==null?null:current.GetOwnerDocument();
       if(currentDocument==null||document.GetBeingClosed()||!string.Equals(currentDocument.GetFileName(),target,StringComparison.OrdinalIgnoreCase)||!string.Equals(currentDocument.GetKind(),kind,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("当前文档已改变，请激活目标文档后重新放置。");
       string library=record.FilePath;
       if(copies!=null){
        var targetDocument=GlobalVars.DXPWorkSpace.DM_GetDocumentFromPath(target);var project=targetDocument==null?null:targetDocument.DM_Project();
        if(project==null||!string.Equals(project.DM_ProjectFullPath(),savedProject,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("目标工程已改变，请重新选择放置目标。");
        foreach(string path in new[]{copies.SymbolPath,copies.FootprintPath}){
         bool found=false;for(int index=0;index<project.DM_LogicalDocumentCount();index++){var member=project.DM_LogicalDocuments(index);if(member!=null&&string.Equals(member.DM_FullPath(),path,StringComparison.OrdinalIgnoreCase)){found=true;break;}}
         if(!found){UserLog.Write("Project attach begin "+Path.GetFileName(path));project.DM_AddSourceDocument(path);UserLog.Write("Project attach returned "+Path.GetFileName(path));}
        }
        library=record.Kind=="Symbol"?copies.SymbolPath:copies.FootprintPath;
       }
       if(library.IndexOf('|')>=0)throw new IOException("工程路径包含 AD 命令分隔符，请调整工程位置。");
       panel.SetNativeBusy(true,"在画布点击位置；按 Esc 结束放置。");current.SetFocus();UserLog.Write("Placement begin "+record.Code+" "+record.Kind+" "+record.Name);
       string command=record.Kind=="Symbol"?"IntegratedLibrary:PlaceLibraryComponent":"PCB:PlaceComponent";
       string parameters=record.Kind=="Symbol"?"LibReference="+record.Name+"|Library="+library:"FileName="+library+"|Footprint="+record.Name+"|Pattern="+record.Name+"|ShowDialog=False";
       Utils.RunCommand(command,parameters,current);UserLog.Write("Placement returned "+record.Code+" "+record.Kind);panel.SetNativeBusy(false,"放置结束，请保存工程。");
      }catch(Exception error){UserLog.Write("Placement",error);panel.SetNativeBusy(false,"未能放置元件。");panel.ShowUserMessage("无法放置",error.Message);}
      finally{operationPending=false;}
     });
    });
   }catch(Exception error){UserLog.Write("Placement preparation",error);panel.SetStatus("请检查当前文档后重新放置。",true);panel.ShowUserMessage("放置元件",error.Message);}
  }
  void OpenLibrary(LibraryRecord record){
   if(operationPending)return;
   string message="查看库文件后，请重新激活目标原理图或 PCB 再放置。";
   try{if(!File.Exists(record.FilePath))throw new FileNotFoundException("库文件不存在，请重新获取元件。",record.FilePath);operationPending=true;panel.SetNativeBusy(true,"正在 AD 中打开库文件…");var document=GlobalVars.Client.OpenDocument(record.Kind=="Symbol"?"SCHLIB":"PCBLIB",record.FilePath);if(document==null)throw new IOException("AD 未返回库文档。");GlobalVars.Client.ShowDocument(document);UserLog.Write("Open library "+record.Code+" "+record.Kind);panel.SetStatus("已打开来源库，请核对外形、编号及连接点。",false);}
   catch(Exception error){UserLog.Write("Open library",error);message="打开失败："+error.Message;}
   finally{operationPending=false;panel.SetNativeBusy(false,message);}
  }
 }
}
