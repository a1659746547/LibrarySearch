using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LibrarySearch {
 public sealed class UpdateState {
  public bool Automatic {get;set;}
  public bool IncludeBeta {get;set;}
  public string LastAttemptUtc {get;set;}
  public string NotifiedVersion {get;set;}
  public UpdateState(){Automatic=true;IncludeBeta=ReleaseInfo.Version.Contains("-");LastAttemptUtc="";NotifiedVersion="";}
  public static UpdateState Load(){try{string path=Path.Combine(ReleaseInfo.ProfileRoot,"updates.json");if(File.Exists(path))return Json.Read<UpdateState>(path)??new UpdateState();}catch(Exception error){UserLog.Write("Update preferences",error);}return new UpdateState();}
  public void Save(){Directory.CreateDirectory(ReleaseInfo.ProfileRoot);Json.WriteAtomic(Path.Combine(ReleaseInfo.ProfileRoot,"updates.json"),this);}
  public bool Due(DateTime utc){DateTime last;return Automatic&&(!DateTime.TryParse(LastAttemptUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out last)||utc<last.ToUniversalTime()||(utc-last.ToUniversalTime()).TotalHours>=24);}
 }
 public sealed class WebsiteRelease {
  public string Version,Date,Channel,FileName,SHA256,Notes;
  public long Size;
  public int MinAdMajor,MaxAdMajor;
  public Uri DownloadUri {get{return new Uri(UpdateClient.Site+"downloads/"+Uri.EscapeDataString(FileName));}}
 }
 public static class UpdateClient {
  public const string Site="https://ad.kn21.xyz/";
  public const int MaxPackageBytes=64*1024*1024;
  static string Value(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):"";}
  static Dictionary<string,object> Object(Dictionary<string,object> row,string key){object value;return row.TryGetValue(key,out value)?value as Dictionary<string,object>:null;}
  static bool HasAdTarget(Dictionary<string,object> row,int adMajor){object files;return row.TryGetValue("files",out files)&&files is object[]&&((object[])files).OfType<Dictionary<string,object>>().Any(file=>Value(file,"id")=="ad"+adMajor);}
  static string[] VersionParts(string value){if(!Regex.IsMatch(value??"",@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[a-z0-9]+(?:\.[a-z0-9]+)*)?\z")||(value??"").Length>80)throw new IOException("官网版本号格式不正确。");return value.Split(new[]{'-'},2);}
  static int NumberCompare(string a,string b){a=a.TrimStart('0');b=b.TrimStart('0');int length=a.Length.CompareTo(b.Length);return length!=0?length:string.CompareOrdinal(a,b);}
  public static int CompareVersions(string a,string b){
   var x=VersionParts(a);var y=VersionParts(b);var ax=x[0].Split('.');var by=y[0].Split('.');for(int i=0;i<3;i++){int result=NumberCompare(ax[i],by[i]);if(result!=0)return result;}
   if(x.Length==1||y.Length==1)return x.Length==y.Length?0:x.Length==1?1:-1;
   ax=x[1].Split('.');by=y[1].Split('.');for(int i=0;i<Math.Min(ax.Length,by.Length);i++){bool an=ax[i].All(char.IsDigit),bn=by[i].All(char.IsDigit);int result=an&&bn?NumberCompare(ax[i],by[i]):an!=bn?(an?-1:1):string.CompareOrdinal(ax[i],by[i]);if(result!=0)return result;}return ax.Length.CompareTo(by.Length);
  }
  public static WebsiteRelease Parse(string json,int adMajor,bool includeBeta=true){
   var root=new JavaScriptSerializer{MaxJsonLength=1024*1024,RecursionLimit=32}.DeserializeObject(json) as Dictionary<string,object>;var current=root==null?null:Object(root,"current");if(current==null)throw new IOException("官网版本数据不完整。");
   var channels=Object(root,"channels");var candidates=new List<Dictionary<string,object>>{current};
   if(channels!=null)foreach(string key in new[]{"stable","beta"}){var candidate=Object(channels,key);if(candidate!=null){if(Value(candidate,"channel")!=key)throw new IOException("官网通道数据不一致。");candidates.Add(candidate);}}
   var allowed=candidates.Where(row=>includeBeta||(Value(row,"channel")=="stable"&&!Value(row,"version").Contains("-"))).Where(row=>HasAdTarget(row,adMajor)).ToArray();
   if(allowed.Length==0)throw new IOException("官网尚未提供当前通道中兼容 AD"+adMajor+" 的安装包。");
   if(allowed.Length>0){current=allowed[0];foreach(var candidate in allowed.Skip(1))if(CompareVersions(Value(candidate,"version"),Value(current,"version"))>0)current=candidate;}
   string version=Value(current,"version");VersionParts(version);string channel=Value(current,"channel");if(channel!="stable"&&channel!="beta")throw new IOException("官网版本通道无法识别。");
   object raw;if(!current.TryGetValue("files",out raw)||!(raw is object[]))throw new IOException("官网缺少下载文件。");
   var files=((object[])raw).OfType<Dictionary<string,object>>().Where(row=>Value(row,"id")=="ad"+adMajor).ToArray();if(files.Length!=1)throw new IOException("官网尚未提供 AD"+adMajor+" 的唯一兼容安装包。");var file=files[0];
   string filename=Value(file,"file"),hash=Value(file,"sha256");long size;
   bool universal=filename=="LibrarySearch-"+version+".zip";
   if((filename!="LibrarySearch-AD"+adMajor+"-"+version+".zip"&&!universal)||!Regex.IsMatch(hash,@"\A[0-9a-fA-F]{64}\z")||!long.TryParse(Value(file,"size"),out size)||size<1024||size>MaxPackageBytes)throw new IOException("官网安装包名称、大小或校验值无效。");
   if(universal||file.ContainsKey("adMajors")){
    object supported;if(!file.TryGetValue("adMajors",out supported)||!(supported is object[]))throw new IOException("多版本安装包缺少明确的 AD 版本清单。");
    var majors=new List<int>();foreach(object item in (object[])supported){int value;if(!int.TryParse(Convert.ToString(item),out value)||value<18||majors.Contains(value))throw new IOException("官网 AD 版本清单无效。");majors.Add(value);}if(!majors.Contains(adMajor))throw new IOException("多版本安装包不支持当前 AD。");
   }
   int min=adMajor,max=adMajor;
   if(file.ContainsKey("minAdMajor")&&!int.TryParse(Value(file,"minAdMajor"),out min))throw new IOException("官网兼容范围无效。");if(file.ContainsKey("maxAdMajor")&&!int.TryParse(Value(file,"maxAdMajor"),out max))throw new IOException("官网兼容范围无效。");if(min<1||max<min||adMajor<min||adMajor>max)throw new IOException("官网安装包不兼容当前 AD 主版本。");
   string notes="";if(root.TryGetValue("releases",out raw)&&raw is object[])foreach(var item in (object[])raw){var row=item as Dictionary<string,object>;if(row!=null&&Value(row,"version")==version){notes=Value(row,"zh");break;}}
   if(notes.Length>4000)notes=notes.Substring(0,4000);
   return new WebsiteRelease{Version=version,Date=Value(current,"date"),Channel=channel,FileName=filename,SHA256=hash.ToUpperInvariant(),Size=size,MinAdMajor=min,MaxAdMajor=max,Notes=notes};
  }
  public static bool IsNewer(WebsiteRelease release,string installed,bool includeBeta){return (includeBeta||(release.Channel=="stable"&&!release.Version.Contains("-")))&&CompareVersions(release.Version,installed)>0;}
  static HttpWebRequest Request(Uri uri){if(uri.Scheme!="https"||uri.Host!="ad.kn21.xyz"||!uri.IsDefaultPort||uri.UserInfo.Length!=0)throw new IOException("更新地址必须来自官网 HTTPS。");ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.UserAgent="LibrarySearch/"+ReleaseInfo.Version;request.Timeout=20000;request.ReadWriteTimeout=20000;return request;}
  public static WebsiteRelease Check(int adMajor,CancellationToken cancellation,bool includeBeta=true){
   cancellation.ThrowIfCancellationRequested();var request=Request(new Uri(Site+"assets/releases.json"));request.Headers[HttpRequestHeader.CacheControl]="no-cache";
   try{using(cancellation.Register(request.Abort))using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream,Encoding.UTF8)){
    if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("官网版本接口返回异常状态。");var body=new StringBuilder();var buffer=new char[4096];int count;while((count=reader.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();if(body.Length+count>1024*1024)throw new IOException("官网版本数据过大。");body.Append(buffer,0,count);}cancellation.ThrowIfCancellationRequested();return Parse(body.ToString(),adMajor,includeBeta);
   }}catch(WebException error){cancellation.ThrowIfCancellationRequested();throw new IOException("暂时无法连接官网，请稍后检查更新。",error);}
  }
  public static void VerifyFile(string path,WebsiteRelease release){if(new FileInfo(path).Length!=release.Size||!LibraryStore.Hash(path).Equals(release.SHA256,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包 SHA-256 或大小不符，已停止使用本次下载，请重新检查更新。");}
  public static void Download(WebsiteRelease release,string destination,CancellationToken cancellation,Action<int> progress){
   cancellation.ThrowIfCancellationRequested();if(release.Size<1024||release.Size>MaxPackageBytes)throw new IOException("安装包大小超限。");destination=Path.GetFullPath(destination);string temporary=destination+"."+Guid.NewGuid().ToString("N")+".part";var request=Request(release.DownloadUri);var clock=Stopwatch.StartNew();
   try{
    using(cancellation.Register(request.Abort))using(var response=(HttpWebResponse)request.GetResponse())using(var input=response.GetResponseStream())using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
     if(response.StatusCode!=HttpStatusCode.OK||(response.ContentLength>=0&&response.ContentLength!=release.Size))throw new IOException("官网安装包大小已变化，请重新检查更新。");
     byte[] buffer=new byte[65536];int count;long total=0;while((count=input.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>120||total+count>release.Size)throw new IOException("下载超时或安装包大小超限。");output.Write(buffer,0,count);total+=count;progress((int)(total*100/release.Size));}
    }
    cancellation.ThrowIfCancellationRequested();VerifyFile(temporary,release);cancellation.ThrowIfCancellationRequested();if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
   }catch(WebException error){cancellation.ThrowIfCancellationRequested();throw new IOException("下载未完成，请检查网络后重试。",error);}
   finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
  public static void OpenPage(string relative){Process.Start(new ProcessStartInfo(Site+relative){UseShellExecute=true});}
 }
 internal sealed class UpdatePage : UserControl {
  readonly WebsiteRelease release;readonly PanelButton download=new PanelButton(),close=new PanelButton();readonly Label state=new Label();CancellationTokenSource operation;
  public event Action CloseRequested;
  public UpdatePage(WebsiteRelease value){
   release=value;BackColor=PanelTheme.Surface;ForeColor=PanelTheme.Ink;
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(4,8,4,4),ColumnCount=1,RowCount=5};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(var height in new[]{66,0,92,42,40})layout.RowStyles.Add(height==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,height));Controls.Add(layout);
   layout.Controls.Add(new Label{Text="新版本 "+release.Version+"\r\n发布日期 "+release.Date,Dock=DockStyle.Fill},0,0);
   layout.Controls.Add(new TextBox{Text=string.IsNullOrWhiteSpace(release.Notes)?"改进使用体验。":release.Notes,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=PanelTheme.Input,ForeColor=PanelTheme.Ink,BorderStyle=BorderStyle.None},0,1);
   layout.Controls.Add(new Label{Text="安装包 "+(release.Size/1048576.0).ToString("F2")+" MB\r\n下载后会自动校验文件。请完整解压，保存工程并关闭 AD，再运行 Install.cmd。",Dock=DockStyle.Fill},0,2);
   state.Dock=DockStyle.Fill;state.AutoEllipsis=true;state.ForeColor=PanelTheme.Muted;layout.Controls.Add(state,0,3);
   var buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));close.Text="返回";close.Dock=DockStyle.Fill;close.Kind=PanelButtonKind.Command;download.Text="下载安装包";download.Dock=DockStyle.Fill;download.Kind=PanelButtonKind.Primary;buttons.Controls.Add(close,0,0);buttons.Controls.Add(download,1,0);layout.Controls.Add(buttons,0,4);
   close.Click+=(s,e)=>{if(CloseRequested!=null)CloseRequested();};download.Click+=(s,e)=>Download();
  }
  void Post(Action action){if(IsDisposed||Disposing||!IsHandleCreated)return;try{BeginInvoke(new Action(delegate{if(!IsDisposed&&!Disposing)action();}));}catch(InvalidOperationException){}}
  void Download(){if(operation!=null)return;string path;using(var save=new SaveFileDialog{Title="保存安装包",FileName=release.FileName,Filter="ZIP 安装包 (*.zip)|*.zip",OverwritePrompt=true}){if(save.ShowDialog(this)!=DialogResult.OK)return;path=save.FileName;}
   download.Enabled=false;close.Text="取消下载";operation=new CancellationTokenSource();var pending=operation;
   ThreadPool.QueueUserWorkItem(delegate{
    string message;
    try{UpdateClient.Download(release,path,pending.Token,percent=>Post(()=>state.Text="正在下载 "+percent+"%…"));message="安装包已保存并通过校验。\r\n"+path;}
    catch(OperationCanceledException){message="已取消下载。";}
    catch(Exception error){UserLog.Write("Update download",error);message="下载未完成，请检查网络后重试。";}
    pending.Dispose();Post(delegate{operation=null;state.Text=message;download.Enabled=true;close.Text="返回";});
   });
  }
  protected override void Dispose(bool disposing){if(disposing&&operation!=null){try{operation.Cancel();}catch(ObjectDisposedException){}}base.Dispose(disposing);}
 }
}
