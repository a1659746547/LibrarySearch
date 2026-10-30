using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;

#if NET6_0_OR_GREATER
// Preserve the legacy JSON object/array contract on modern Altium runtimes.
namespace System.Web.Script.Serialization {
 public sealed class JavaScriptSerializer {
  public int MaxJsonLength {get;set;} = 2097152;
  public int RecursionLimit {get;set;} = 100;
  System.Text.Json.JsonSerializerOptions Options {get{return new System.Text.Json.JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true,MaxDepth=RecursionLimit,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};}}
  void Validate(string json){if(json==null||json.Length>MaxJsonLength)throw new System.IO.IOException("JSON exceeds size limit");}
  public string Serialize(object value){string json=System.Text.Json.JsonSerializer.Serialize(value,value==null?typeof(object):value.GetType(),Options);Validate(json);return json;}
  public T Deserialize<T>(string json){Validate(json);return System.Text.Json.JsonSerializer.Deserialize<T>(json,Options);}
  public object DeserializeObject(string json){Validate(json);using(var doc=System.Text.Json.JsonDocument.Parse(json,new System.Text.Json.JsonDocumentOptions{MaxDepth=RecursionLimit})){return ConvertElement(doc.RootElement);}}
  static object ConvertElement(System.Text.Json.JsonElement e){
   switch(e.ValueKind){
    case System.Text.Json.JsonValueKind.Object:var d=new System.Collections.Generic.Dictionary<string,object>();foreach(var p in e.EnumerateObject())d.Add(p.Name,ConvertElement(p.Value));return d;
    case System.Text.Json.JsonValueKind.Array:var a=new System.Collections.Generic.List<object>();foreach(var v in e.EnumerateArray())a.Add(ConvertElement(v));return a.ToArray();
    case System.Text.Json.JsonValueKind.String:return e.GetString();
    case System.Text.Json.JsonValueKind.Number:long n;if(e.TryGetInt64(out n))return n;return e.GetDouble();
    case System.Text.Json.JsonValueKind.True:return true;
    case System.Text.Json.JsonValueKind.False:return false;
    default:return null;
   }
  }
 }
}
#endif

namespace LibrarySearch {
 public static class ReleaseInfo {
  public const string Version="1.1.2";
  public const string Title="LibrarySearch";
  public const string Acquisition="source-pin-width-slot-path-5";
  public const string Format="ad-appearance-step-4";
  public const string StepFormat="ad-body-pin-curves-text-step-3";
  public const string PreviousFormat="ad-body-pin-curves-text-2";
  public static string HostTarget {get;private set;}
  public static int AdMajor {get;private set;}
  static ReleaseInfo(){HostTarget="独立预览（未加载 AD 宿主）";}
  public static void ConfigureHost(string target){var match=Regex.Match(target??"",@"\AAD([0-9]+)(?:\s|\z)");int major;AdMajor=match.Success&&int.TryParse(match.Groups[1].Value,out major)?major:0;HostTarget=target;}
  public static string ProfileRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LibrarySearch");}}
  public static string DefaultLibraryRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"LibrarySearch");}}
 }
 public sealed class SymbolAppearance {
  public string Mode {get;set;}
  public string Background {get;set;}
  public string BodyFill {get;set;}
  public string BorderColor {get;set;}
  public string TextColor {get;set;}
  public string PinNumberColor {get;set;}
  public string AccentColor {get;set;}
  public string FontFamily {get;set;}
  public int PinNameSize {get;set;}
  public int PinNumberSize {get;set;}
  public int LabelSize {get;set;}
  public int LineWidth {get;set;}
  public int CornerRadius {get;set;}
  public bool FillBody {get;set;}
  public bool RoundedCorners {get;set;}
  public bool HideDuplicatePinNames {get;set;}
  public bool ShowComment {get;set;}
  public string Label {get{return Mode=="Classic"?"经典 AD":Mode=="Custom"?"自定义":"现代浅色";}}
  public SymbolAppearance(){Mode="Modern";Background="#FFFFFF";BodyFill="#EBF2FA";BorderColor="#333333";TextColor="#333333";PinNumberColor="#666666";AccentColor="#9B3030";FontFamily="Arial";PinNameSize=8;PinNumberSize=7;LabelSize=9;LineWidth=1;CornerRadius=20;FillBody=true;RoundedCorners=true;HideDuplicatePinNames=false;ShowComment=false;}
  public SymbolAppearance Copy(){return (SymbolAppearance)MemberwiseClone();}
  public static SymbolAppearance Classic(){return new SymbolAppearance{Mode="Classic",BodyFill="#FFFFB0",BorderColor="#800000",TextColor="#000080",PinNumberColor="#000000",FontFamily="Times New Roman",PinNameSize=10,PinNumberSize=10,LabelSize=10,LineWidth=0,RoundedCorners=false,HideDuplicatePinNames=false,ShowComment=true};}
  public static SymbolAppearance Effective(SymbolAppearance source){
   if(source==null)return Classic();
   if(source.Mode=="Modern")return new SymbolAppearance();
   if(source.Mode=="Classic")return Classic();
   var result=source.Copy();if(result.Mode!="Custom")throw new IOException("符号外观模式无效。");
   foreach(string value in new[]{result.Background,result.BodyFill,result.BorderColor,result.TextColor,result.PinNumberColor,result.AccentColor})if(value==null||!Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$"))throw new IOException("颜色请使用 #RRGGBB 格式。");
   if(result.Background!="#FFFFFF"&&result.Background!="#F8F9FA")throw new IOException("预览底色请选择白色或浅灰。");
   if(string.IsNullOrWhiteSpace(result.FontFamily)||result.FontFamily.Length>80||result.FontFamily.Any(c=>char.IsControl(c)||c=='|'))throw new IOException("字体名称无效。");
   if(new[]{result.PinNameSize,result.PinNumberSize,result.LabelSize}.Any(n=>n<5||n>14)||result.LineWidth<0||result.LineWidth>2||result.CornerRadius<10||result.CornerRadius>50)throw new IOException("外观尺寸超出允许范围。");
   result.BodyFill=result.BodyFill.ToUpperInvariant();result.BorderColor=result.BorderColor.ToUpperInvariant();result.TextColor=result.TextColor.ToUpperInvariant();result.PinNumberColor=result.PinNumberColor.ToUpperInvariant();result.AccentColor=result.AccentColor.ToUpperInvariant();result.FontFamily=result.FontFamily.Trim();return result;
  }
  public string Key(){using(var hash=SHA256.Create()){return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this)))).Replace("-","").ToLowerInvariant();}}
 }

 public sealed class UserSettings {
  public int UsageNoticeVersion {get;set;}
  public SymbolAppearance Appearance {get;set;}
  public string LibraryRoot {get;set;}
  public string LocalIndex {get;set;}
  public string[] LocalLibraryRoots {get;set;}
  public UserSettings(){Appearance=SymbolAppearance.Classic();LibraryRoot=ReleaseInfo.DefaultLibraryRoot;LocalIndex="";LocalLibraryRoots=new string[0];}
  public static UserSettings Load(){
   try{string path=Path.Combine(ReleaseInfo.ProfileRoot,"settings.json");if(File.Exists(path)){var settings=Json.Read<UserSettings>(path);if(settings!=null&&!string.IsNullOrWhiteSpace(settings.LibraryRoot)&&Path.IsPathRooted(settings.LibraryRoot)){settings.LibraryRoot=Path.GetFullPath(settings.LibraryRoot);try{settings.Appearance=SymbolAppearance.Effective(settings.Appearance);}catch{settings.Appearance=SymbolAppearance.Classic();}if(settings.LocalLibraryRoots==null)settings.LocalLibraryRoots=new string[0];return settings;}}}
   catch(Exception error){UserLog.Write("Settings load",error);}
   return new UserSettings();
  }
  public void Save(){Directory.CreateDirectory(ReleaseInfo.ProfileRoot);Json.WriteAtomic(Path.Combine(ReleaseInfo.ProfileRoot,"settings.json"),this);}
 }
 public static class Json {
  public static T Read<T>(string path){return new JavaScriptSerializer{MaxJsonLength=64*1024*1024,RecursionLimit=128}.Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));}
  public static void WriteAtomic(string path,object value){
   string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(temporary,new JavaScriptSerializer{MaxJsonLength=64*1024*1024,RecursionLimit=128}.Serialize(value),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
   finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 }
 public static class UserLog {
  static readonly object Gate=new object();
  public static string Folder {get{return Path.Combine(ReleaseInfo.ProfileRoot,"Logs");}}
  public static void Write(string message){try{lock(Gate){Directory.CreateDirectory(Folder);File.AppendAllText(Path.Combine(Folder,"librarysearch-"+DateTime.Now.ToString("yyyyMMdd")+".log"),DateTime.Now.ToString("o")+" ["+ReleaseInfo.Version+"] "+message+Environment.NewLine,Encoding.UTF8);}}catch{}}
  public static void Write(string stage,Exception error){Write(stage+": "+error);}
 }
 public sealed class ComponentMetadata {
  public string DescriptionVersion {get;set;}
  public string DescriptionSource {get;set;}
  public string Description {get;set;}
  public string PackageName {get;set;}
  public string ManufacturerPart {get;set;}
 }
 public sealed class LibraryPackage {
  public bool DescriptionRefreshRequired {get;set;}
  public string Format {get;set;}
  public string Code {get;set;}
  public string SymbolName {get;set;}
  public string FootprintName {get;set;}
  public string RelativeFolder {get;set;}
  public string SymbolHash {get;set;}
  public string FootprintHash {get;set;}
  public string DownloadedUtc {get;set;}
  public string Warning {get;set;}
  public string Release {get;set;}
  public string DescriptionVersion {get;set;}
  public string DescriptionSource {get;set;}
  public string Description {get;set;}
  public string PackageName {get;set;}
  public string ManufacturerPart {get;set;}
  public string AcquisitionVersion {get;set;}
  public string PinGeometryVersion {get;set;}
  public string AppearanceKey {get;set;}
  public SymbolAppearance Appearance {get;set;}
  public string Model3dStatus {get;set;}
  public string Model3dLabel {get{return Model3dStatus=="embedded"?"含 3D":"";}}
 }
 public sealed class LibraryRecord {
  public string Name;
  public string Kind;
  public string FilePath;
  public string SourcePath;
  public string Code;
  public string RelatedName;
  public LibraryPackage Package;
  public string LibraryRoot;
  public string Description;
  public string PackageName;
  public string ShortDescription {get{return string.IsNullOrWhiteSpace(Description)?"暂无简介":Description;}}
  public string KindLabel {get{return Kind=="Symbol"?"原理图符号":"PCB 封装";}}
 }
 public sealed class FetchResult {
  public LibraryRecord[] Records;
  public bool Reused;
 }
 public sealed class LibrarySearchMatch {
  public LibraryRecord[] Records;
  public int Total;
 }
 public static class LibraryRecordSearch {
  static bool Contains(string value,string text){return value!=null&&value.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0;}
  public static LibrarySearchMatch Find(LibraryRecord[] rows,string text,int kind,bool ignoreText,CancellationToken cancellation){
   cancellation.ThrowIfCancellationRequested();text=(text??"").Trim();var visible=new List<LibraryRecord>(Math.Min(500,rows.Length));int total=0;
   string selectedKind=kind==1?"Symbol":"Footprint";
   for(int index=0;index<rows.Length;index++){
    if((index&63)==0)cancellation.ThrowIfCancellationRequested();var row=rows[index];
    if(row==null||(kind!=0&&row.Kind!=selectedKind))continue;
    if(!ignoreText&&text.Length>0&&!Contains(row.Name,text)&&!Contains(row.Code,text)&&!Contains(row.RelatedName,text)&&!Contains(row.Description,text)&&!Contains(row.PackageName,text))continue;
    total++;if(visible.Count<500)visible.Add(row);
   }
   cancellation.ThrowIfCancellationRequested();return new LibrarySearchMatch{Records=visible.ToArray(),Total=total};
  }
 }
 public sealed class ProjectFiles {
  public string SymbolPath;
  public string FootprintPath;
 }
 public sealed class LocalIndexEntry {
  public string DescriptionVersion {get;set;}
  public string DescriptionSource {get;set;}
  public string Description {get;set;}
  public string Name {get;set;}
  public string Kind {get;set;}
  public string Library {get;set;}
  public string Source {get;set;}
  public string Models {get;set;}
 }
 public sealed class LocalIndexData {
  public int SchemaVersion {get;set;}
  public string[] Roots {get;set;}
  public int ScannedFiles {get;set;}
  public int CacheHits {get;set;}
  public LocalIndexEntry[] Records {get;set;}
  public string[] Warnings {get;set;}
  public LocalSourceStamp[] Sources {get;set;}
  public int RecordCount {get;set;}
  public string RecordsFile {get;set;}
 }
 public sealed class LocalSourceStamp {
  public string Path {get;set;}
  public long Length {get;set;}
  public string Modified {get;set;}
 }
 public sealed class LocalIndexBuild {
  public string IndexPath;
  public LocalIndexData Data;
  public LibraryRecord[] Records;
  public LocalLibraryCatalog Catalog;
 }
 public sealed class LocalCatalogSearchResult {public int SchemaVersion;public int Total;public LocalIndexEntry[] Records;}
 public sealed class LocalLibraryCatalog {
  readonly string index,converter;
  public int Count {get;private set;}
  internal LocalLibraryCatalog(string path,string executable,int count){index=path;converter=executable;Count=count;}
  public LibrarySearchMatch Search(string text,int kind,CancellationToken cancellation){
   cancellation.ThrowIfCancellationRequested();
   if(string.IsNullOrWhiteSpace(converter)||!File.Exists(converter))throw new FileNotFoundException("本地库搜索程序缺失，请安装完整新版插件。",converter);
   string parent=Path.Combine(Path.GetDirectoryName(index),"Search");Directory.CreateDirectory(parent);string run=LibraryStore.ContainedPath(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
   try{
    string request=Path.Combine(run,"request.json"),output=Path.Combine(run,"result.json");Json.WriteAtomic(request,new{Index=index,Query=text??"",Kind=kind});
    var info=new ProcessStartInfo(converter,"--search-index "+AutomaticLocalIndex.Quote(request)+" --output "+AutomaticLocalIndex.Quote(output)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,WorkingDirectory=run,StandardErrorEncoding=Encoding.UTF8};
    using(var process=Process.Start(info)){
     if(process==null)throw new IOException("无法启动本地库搜索。");var errors=process.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
     try{while(!process.WaitForExit(100)){cancellation.ThrowIfCancellationRequested();if(timer.ElapsedMilliseconds>120000)throw new TimeoutException("本地库搜索耗时过长，请重试。");}cancellation.ThrowIfCancellationRequested();string error=errors.GetAwaiter().GetResult();if(process.ExitCode!=0)throw new IOException("本地库搜索未完成："+AutomaticLocalIndex.FailureReason(error));}
     finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch{}}
    }
    cancellation.ThrowIfCancellationRequested();if(!File.Exists(output)||new FileInfo(output).Length>64*1024*1024)throw new IOException("本地库搜索结果缺失或过大。");
    var result=Json.Read<LocalCatalogSearchResult>(output);if(result==null||result.SchemaVersion!=1||result.Total<0||result.Records==null||result.Records.Length>500||result.Total<result.Records.Length)throw new IOException("本地库搜索结果格式无效。");
    var records=AutomaticLocalIndex.Rows(new LocalIndexData{SchemaVersion=3,Records=result.Records},false,cancellation);
    return new LibrarySearchMatch{Records=records,Total=result.Total};
   }finally{string boundary=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(Path.GetFullPath(run).StartsWith(boundary,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(run)){try{Directory.Delete(run,true);}catch(Exception error){UserLog.Write("Local search staging cleanup",error);}}}
  }
 }
 public static class AutomaticLocalIndex {
  public static LibraryRecord[] Load(string path){
   return Open(path,null).Records;
  }
  public static LocalIndexBuild Open(string path,string converter){
   if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return new LocalIndexBuild{Records=new LibraryRecord[0]};
   if(!Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase))return new LocalIndexBuild{IndexPath=path,Records=LibraryStore.LoadLocalIndex(path)};
   if(new FileInfo(path).Length>64*1024*1024)throw new IOException("本地库清单过大，请重新扫描以使用分段缓存。");
   var data=Json.Read<LocalIndexData>(path);return OpenData(path,converter,data,false,default(CancellationToken));
  }
  static LocalIndexBuild OpenData(string path,string converter,LocalIndexData data,bool verify,CancellationToken cancellation){
   var records=Rows(data,verify,cancellation);LocalLibraryCatalog catalog=null;
   if(data.SchemaVersion==3){if(data.RecordCount<data.Records.Length||data.Records.Length>500||data.RecordsFile!="search-records.ndjson")throw new IOException("本地库分段清单格式无效，请重新扫描。");if(data.RecordCount>data.Records.Length)catalog=new LocalLibraryCatalog(path,converter,data.RecordCount);}
   return new LocalIndexBuild{IndexPath=path,Data=data,Records=records,Catalog=catalog};
  }
  internal static LibraryRecord[] Rows(LocalIndexData data,bool verifyLibraries=true,CancellationToken cancellation=default(CancellationToken)){
   if(data==null||(data.SchemaVersion!=1&&data.SchemaVersion!=2&&data.SchemaVersion!=3)||data.Records==null)throw new IOException("本地库索引格式无效，请重新选择目录扫描。");
   var rows=new List<LibraryRecord>();var libraries=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
   foreach(var item in data.Records){
    cancellation.ThrowIfCancellationRequested();
    if(item==null||string.IsNullOrWhiteSpace(item.Name)||item.Name.IndexOfAny(new[]{'|','\r','\n','\t','\0'})>=0||(item.Kind!="Symbol"&&item.Kind!="Footprint")||string.IsNullOrWhiteSpace(item.Library)||!Path.IsPathRooted(item.Library))continue;
    string extension=Path.GetExtension(item.Library);
    if(!extension.Equals(item.Kind=="Symbol"?".SchLib":".PcbLib",StringComparison.OrdinalIgnoreCase))continue;
    bool valid;if(!libraries.TryGetValue(item.Library,out valid)){
     try{if(verifyLibraries)LibraryStore.CheckLibrary(item.Library);valid=true;}catch(Exception error){UserLog.Write("Skip local library "+item.Library,error);valid=false;}
     libraries[item.Library]=valid;
    }
    if(!valid)continue;
    string original=string.IsNullOrWhiteSpace(item.Source)?item.Library:item.Source;
    rows.Add(new LibraryRecord{Name=item.Name,Kind=item.Kind,FilePath=item.Library,SourcePath=original,Code=Path.GetFileNameWithoutExtension(original),RelatedName=item.Models??"",Description=LibraryStore.CleanDescription(item.Description),PackageName=item.Kind=="Symbol"?item.Models:item.Name});
   }
   return rows.ToArray();
  }
  internal static string Quote(string value){if(string.IsNullOrEmpty(value)||value.IndexOfAny(new[]{'"','\r','\n','\0'})>=0)throw new IOException("索引路径包含不支持的字符。");return "\""+value+"\"";}
  internal static string FailureReason(string error){var match=Regex.Match(error??"",@"(?m)^\[easyeda2altium\] error:\s*(?:invalid argument:\s*)?([^\r\n]+)");string text=match.Success?match.Groups[1].Value.Trim():(error??"").Trim();if(text.Length==0)text="扫描程序异常退出，请导出反馈。";return text.Length>500?text.Substring(0,500):text;}
  public static LocalIndexBuild Build(string[] roots,string converter,CancellationToken cancellation,Action<string> progress,string previousIndex=null,bool force=false){
   return BuildAt(roots,converter,cancellation,progress,previousIndex,force,Path.Combine(ReleaseInfo.ProfileRoot,"LocalLibraries"));
  }
  internal static LocalIndexBuild BuildAt(string[] roots,string converter,CancellationToken cancellation,Action<string> progress,string previousIndex,bool force,string parent){
   if(roots==null||roots.Length==0||roots.Length>32)throw new IOException("请选择本地元件库目录。");
   foreach(string root in roots)if(string.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root)||!Directory.Exists(root))throw new DirectoryNotFoundException("本地元件库目录不可访问："+root);
   if(!File.Exists(converter))throw new FileNotFoundException("自动索引程序缺失，请安装完整新版插件。",converter);
   cancellation.ThrowIfCancellationRequested();
   string run=LibraryStore.ContainedPath(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
   bool completed=false;
   try{
    string request=Path.Combine(run,"request.json"),output=Path.Combine(run,"index.json");
    Json.WriteAtomic(request,new{Roots=roots,CacheDirectory=Path.Combine(run,"Extracted"),PreviousIndex=previousIndex,Force=force});
    var info=new ProcessStartInfo(converter,"--index-request "+Quote(request)+" --output "+Quote(output)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=run,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    using(var process=Process.Start(info)){
     if(process==null)throw new IOException("无法启动自动索引程序。");
     var updateTimer=Stopwatch.StartNew();long lastUpdate=-100;
     process.OutputDataReceived+=(sender,args)=>{long now=updateTimer.ElapsedMilliseconds;if(!string.IsNullOrWhiteSpace(args.Data)&&now-lastUpdate>=100){lastUpdate=now;progress(args.Data);}};process.BeginOutputReadLine();
     var errors=process.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
     try{
      while(!process.WaitForExit(150)){cancellation.ThrowIfCancellationRequested();if(timer.ElapsedMilliseconds>15*60*1000)throw new TimeoutException("扫描时间过长，请选择更具体的元件库目录。");}
      cancellation.ThrowIfCancellationRequested();process.WaitForExit();string error=errors.GetAwaiter().GetResult();
      if(process.ExitCode!=0){UserLog.Write("Local indexing failed: "+error);throw new IOException("本地库扫描未完成："+FailureReason(error)+"；已有缓存保留。");}
     }finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch(Exception error){UserLog.Write("Stop local indexer",error);}}
    }
    cancellation.ThrowIfCancellationRequested();
    if(!File.Exists(output)||new FileInfo(output).Length>64*1024*1024)throw new IOException("本地库扫描结果缺失或过大，请缩小扫描目录。");
    var data=Json.Read<LocalIndexData>(output);var records=Rows(data,true,cancellation);
    cancellation.ThrowIfCancellationRequested();
    foreach(string warning in data.Warnings??new string[0])UserLog.Write("Local indexing: "+warning);
    if(records.Length==0&&data.ScannedFiles>0&&(data.Warnings??new string[0]).Length>0)throw new IOException("没有读到可用元件："+FailureReason(string.Join(Environment.NewLine,data.Warnings))+"；已有缓存保留。");
    if(data.Roots==null||!data.Roots.SequenceEqual(roots,StringComparer.OrdinalIgnoreCase))throw new IOException("本地库缓存与所选目录不匹配。");
    var built=OpenData(output,converter,data,false,cancellation);completed=true;return built;
   }finally{
    if(!completed&&Directory.Exists(run)){
     string prefix=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
     if(Path.GetFullPath(run).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){try{Directory.Delete(run,true);}catch(Exception error){UserLog.Write("Local index staging cleanup",error);}}
    }
    if(!completed){
     string segments=LibraryStore.ContainedPath(parent,"Segments"),pattern="segment-"+Path.GetFileName(run)+"-*.json";
     if(Directory.Exists(segments)&&(File.GetAttributes(segments)&FileAttributes.ReparsePoint)==0){foreach(string file in Directory.GetFiles(segments,pattern)){try{if((File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.Delete(file);}catch(Exception error){UserLog.Write("Local index segment cleanup",error);}}}
    }
   }
  }
 }

 public sealed class CacheRemovalResult {public int Versions;public string ResidualPath;}

 public sealed class BatchCacheRemovalResult {public string[] DeletedCodes,FailedCodes,Errors,ResidualPaths;public int Versions;public bool Cancelled;}

 public sealed class LibraryStore {
  static readonly object IndexGate=new object();
  readonly string root;
  public string Root {get{return root;}}
  public LibraryStore(string libraryRoot){if(string.IsNullOrWhiteSpace(libraryRoot)||!Path.IsPathRooted(libraryRoot))throw new ArgumentException("请选择完整的库目录路径。");string full=Path.GetFullPath(libraryRoot);root=full.Equals(Path.GetPathRoot(full),StringComparison.OrdinalIgnoreCase)?full:full.TrimEnd(Path.DirectorySeparatorChar);}
  public static string Normalize(string input){string code=(input??"").Trim().ToUpperInvariant();if(!Regex.IsMatch(code,@"\AC[0-9]{1,12}\z"))throw new ArgumentException("请输入完整的嘉立创 C 编号，例如 C7473159。");return code;}
  public static string Hash(string path){using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(path)){return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}}
  public static void CheckLibrary(string path){byte[] expected={0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1};using(var stream=File.OpenRead(path)){foreach(byte value in expected)if(stream.ReadByte()!=value)throw new IOException("库文件损坏或格式不正确："+Path.GetFileName(path));}}
  public static string ContainedPath(string parent,string relative){
   if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative))throw new IOException("库索引路径无效。");
   string prefix=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   string result=Path.GetFullPath(Path.Combine(prefix,relative));
   if(!result.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("库索引指向了库目录之外。");
   return result;
  }
  string IndexPath(string code){return Path.Combine(root,"Index",Normalize(code)+".json");}
  public LibraryRecord[] Records(LibraryPackage package,bool verifyHash){
   if(package==null||(package.Format!=ReleaseInfo.Format&&package.Format!=ReleaseInfo.StepFormat&&package.Format!=ReleaseInfo.PreviousFormat)||Normalize(package.Code)!=package.Code)throw new IOException("库索引版本需要更新，请重新获取该元件。");
   ValidateName(package.SymbolName);ValidateName(package.FootprintName);
   string folder=ContainedPath(root,package.RelativeFolder);
   string symbol=Path.Combine(folder,package.Code+".SchLib"),footprint=Path.Combine(folder,package.Code+".PcbLib");
   CheckLibrary(symbol);CheckLibrary(footprint);
   if(verifyHash&&(Hash(symbol)!=package.SymbolHash||Hash(footprint)!=package.FootprintHash))throw new IOException("下载库的内容发生变化，请重新获取该元件。");
   return new[]{new LibraryRecord{Name=package.SymbolName,Kind="Symbol",FilePath=symbol,Code=package.Code,RelatedName=package.FootprintName,Package=package,LibraryRoot=root,Description=CleanDescription(package.Description),PackageName=string.IsNullOrWhiteSpace(package.PackageName)?package.FootprintName:package.PackageName},new LibraryRecord{Name=package.FootprintName,Kind="Footprint",FilePath=footprint,Code=package.Code,RelatedName=package.SymbolName,Package=package,LibraryRoot=root,Description=CleanDescription(package.Description),PackageName=string.IsNullOrWhiteSpace(package.PackageName)?package.FootprintName:package.PackageName}};
  }
  public LibraryRecord[] LoadDownloaded(){
   var records=new List<LibraryRecord>();string index=Path.Combine(root,"Index");
   if(!Directory.Exists(index))return records.ToArray();
   foreach(string file in Directory.GetFiles(index,"C*.json")){
    try{var package=Json.Read<LibraryPackage>(file);if(Path.GetFileNameWithoutExtension(file)!=package.Code)throw new IOException("索引编号不匹配。");records.AddRange(Records(package,false));}
    catch(Exception error){UserLog.Write("Skip invalid downloaded index "+Path.GetFileName(file),error);}
   }
   return records.OrderBy(record=>record.Code).ThenBy(record=>record.Kind).ToArray();
  }
  public static LibraryRecord[] LoadLocalIndex(string path){
   if(string.IsNullOrWhiteSpace(path))return new LibraryRecord[0];
   var records=new List<LibraryRecord>();
   foreach(string line in File.ReadLines(path,Encoding.UTF8)){
    string[] fields=line.Split('\t');if(fields.Length<4||(fields[1]!="Symbol"&&fields[1]!="Footprint"))continue;
    if(!Path.IsPathRooted(fields[2])||fields[0].IndexOf('|')>=0||fields[2].IndexOf('|')>=0)continue;
    string extension=Path.GetExtension(fields[2]);if(!extension.Equals(fields[1]=="Symbol"?".SchLib":".PcbLib",StringComparison.OrdinalIgnoreCase))continue;
    records.Add(new LibraryRecord{Name=fields[0],Kind=fields[1],FilePath=fields[2],Code="本地",RelatedName=fields[3]});
   }
   return records.ToArray();
  }
  public static string CleanDescription(string value){if(string.IsNullOrWhiteSpace(value))return "";value=Regex.Replace(value,"<[^>]*>"," ");value=System.Net.WebUtility.HtmlDecode(value);value=Regex.Replace(value,@"[\s\p{C}]+"," ").Trim();return value.Length>4000?value.Substring(0,4000):value;}
  static void ValidateName(string name){if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\t','\r','\n','|'})>=0)throw new IOException("库名称包含不支持的字符。");}
  public FetchResult Fetch(string input,string converter,bool refresh,CancellationToken cancellation,Action<string> progress,SymbolAppearance appearance=null,bool include3d=false){
   var style=SymbolAppearance.Effective(appearance);string styleKey=style.Key();
   string code=Normalize(input);cancellation.ThrowIfCancellationRequested();
   progress("正在检查本机已下载元件…");
   if(!refresh&&File.Exists(IndexPath(code))){
    try{var stored=Json.Read<LibraryPackage>(IndexPath(code));if(stored.Format!=ReleaseInfo.Format||stored.AppearanceKey!=styleKey||stored.PinGeometryVersion!="svg-line-1"||stored.AcquisitionVersion!=ReleaseInfo.Acquisition||(include3d&&stored.Model3dStatus!="embedded"))throw new IOException("将按修正后的引脚几何与所选外观重新获取。");var cached=Records(stored,true);cancellation.ThrowIfCancellationRequested();return new FetchResult{Records=cached,Reused=true};}
    catch(OperationCanceledException){throw;}
    catch(Exception error){UserLog.Write("Download again "+code,error);}
   }
   if(!File.Exists(converter))throw new FileNotFoundException("元件处理文件缺失，请重新安装完整安装包。",converter);
   string run=Path.Combine(root,".staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
   try{
    string output=Path.Combine(run,code);progress(include3d?"正在补齐 "+code+" 的 3D 模型，可取消…":"正在获取 "+code+" 的符号与封装…");
    string profile=Path.Combine(run,"appearance.json");Json.WriteAtomic(profile,style);
    string log=RunConverter(converter,code,output,run,cancellation,profile,Path.Combine(root,"SourceCache"),refresh,include3d);
    if(!Regex.IsMatch(log,@"(?m)^\s*appearance: applied\s*$"))throw new IOException("转换器版本不匹配：缺少外观状态，请重新安装完整包。");
    var symbol=Regex.Match(log,@"(?m)^\s*symbol: (.+) \([0-9]+ pins\)\s*$");var footprint=Regex.Match(log,@"(?m)^\s*footprint: (.+) \([0-9]+ pads\)\s*$");
    if(!symbol.Success||!footprint.Success)throw new IOException("官方数据未返回完整的符号和封装；请导出反馈日志。");
    string symbolName=symbol.Groups[1].Value.Trim(),footprintName=footprint.Groups[1].Value.Trim();ValidateName(symbolName);ValidateName(footprintName);
    CheckLibrary(output+".SchLib");CheckLibrary(output+".PcbLib");cancellation.ThrowIfCancellationRequested();
    progress("正在保存符号、封装和来源信息…");
    string revision=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
    var model=Regex.Match(log,@"(?m)^\s*model3d: (embedded|available|unavailable|download-failed|invalid)\s*$");
    if(!model.Success)throw new IOException("转换器版本不匹配：缺少 3D 状态，请重新安装完整包。");
    string metadataPath=Path.ChangeExtension(output,"metadata.json");
    if(!File.Exists(metadataPath)||new FileInfo(metadataPath).Length>65536)throw new IOException("元件简介数据缺失或过大，请安装完整新版。");
    var metadata=Json.Read<ComponentMetadata>(metadataPath);if(metadata==null)throw new IOException("元件简介数据无效。");
    var package=new LibraryPackage{DescriptionRefreshRequired=refresh,AcquisitionVersion=ReleaseInfo.Acquisition,DescriptionVersion=metadata.DescriptionVersion,DescriptionSource=metadata.DescriptionSource,PinGeometryVersion="svg-line-1",Description=CleanDescription(metadata.Description),PackageName=CleanDescription(metadata.PackageName),ManufacturerPart=CleanDescription(metadata.ManufacturerPart),Appearance=style,AppearanceKey=styleKey,Model3dStatus=model.Groups[1].Value,Format=ReleaseInfo.Format,Code=code,SymbolName=symbolName,FootprintName=footprintName,RelativeFolder=Path.Combine("Components",code,revision),SymbolHash=Hash(output+".SchLib"),FootprintHash=Hash(output+".PcbLib"),DownloadedUtc=DateTime.UtcNow.ToString("o"),Release=ReleaseInfo.Version,Warning=log.IndexOf("warning:",StringComparison.OrdinalIgnoreCase)>=0?"部分图形暂不支持，请先查看来源库并核对引脚和外形。":""};
    File.WriteAllText(Path.Combine(run,"SOURCE.txt"),"嘉立创EDA/EasyEDA官方库 (JLCEDA/EasyEDA Official Library)\r\nhttps://lceda.cn/\r\nhttps://easyeda.com/\r\n"+code+"\r\nLibrarySearch "+ReleaseInfo.Version+"\r\n符号外观："+style.Label+"。\r\n符号与封装；"+package.Model3dLabel+"。STEP 嵌入 PcbLib，随封装库复制。\r\n",Encoding.UTF8);
    File.WriteAllText(Path.Combine(run,"converter.log"),log,Encoding.UTF8);Json.WriteAtomic(Path.Combine(run,"component.json"),package);
    string destination=ContainedPath(root,package.RelativeFolder);Directory.CreateDirectory(Path.GetDirectoryName(destination));Directory.Move(run,destination);
    Directory.CreateDirectory(Path.Combine(root,"Index"));lock(IndexGate){Json.WriteAtomic(IndexPath(code),package);}
    return new FetchResult{Records=Records(package,true),Reused=false};
   }finally{if(Directory.Exists(run)){try{Directory.Delete(run,true);}catch(Exception error){UserLog.Write("Staging cleanup",error);}}}
  }
  public BatchCacheRemovalResult DeleteDownloadedBatch(IEnumerable<string> inputs,CancellationToken cancellation,Action<string> progress){
   if(inputs==null)throw new ArgumentNullException("inputs");var codes=inputs.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(code=>code).ToArray();
   var deleted=new List<string>();var failed=new List<string>();var errors=new List<string>();var residuals=new List<string>();var result=new BatchCacheRemovalResult();
   for(int index=0;index<codes.Length;index++){
    if(cancellation.IsCancellationRequested){result.Cancelled=true;break;}
    string code=codes[index];if(progress!=null)progress("正在删除 "+(index+1)+" / "+codes.Length+"："+code);
    try{var removed=DeleteDownloaded(code);deleted.Add(code);result.Versions+=removed.Versions;if(!string.IsNullOrEmpty(removed.ResidualPath))residuals.Add(removed.ResidualPath);}
    catch(Exception error){failed.Add(code);errors.Add(code+"："+error.Message);UserLog.Write("Batch cache removal "+code,error);}
   }
   result.DeletedCodes=deleted.ToArray();result.FailedCodes=failed.ToArray();result.Errors=errors.ToArray();result.ResidualPaths=residuals.ToArray();return result;
  }

  public CacheRemovalResult DeleteDownloaded(string input){
   string code=Normalize(input);
   lock(IndexGate){
    string index=IndexPath(code);if(!File.Exists(index))throw new IOException("该元件已不在下载缓存中。");
    var current=Json.Read<LibraryPackage>(index);if(current==null||current.Code!=code)throw new IOException("缓存索引与编号不匹配。");
    Records(current,true);
    string components=ContainedPath(root,"Components"),folder=ContainedPath(components,code);
    if(!Directory.Exists(folder))throw new IOException("下载缓存目录不存在。");
    foreach(string path in new[]{components,folder})if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("不能删除重定向的缓存目录。");
    if(Directory.GetFiles(folder).Length!=0)throw new IOException("缓存目录中含额外文件，未删除。");
    var allowed=new HashSet<string>(new[]{code+".SchLib",code+".PcbLib",code+".metadata.json","component.json","SOURCE.txt","converter.log","appearance.json"},StringComparer.OrdinalIgnoreCase);
    var versions=Directory.GetDirectories(folder);if(versions.Length==0)throw new IOException("没有找到可识别的下载版本。");
    foreach(string version in versions){
     if((File.GetAttributes(version)&FileAttributes.ReparsePoint)!=0||Directory.GetDirectories(version).Length!=0)throw new IOException("缓存中含额外目录，未删除。");
     string manifest=Path.Combine(version,"component.json");if(!File.Exists(manifest))throw new IOException("发现非插件生成的目录，未删除。");
     var package=Json.Read<LibraryPackage>(manifest);
     if(package==null||package.Code!=code||!string.Equals(ContainedPath(root,package.RelativeFolder),version,StringComparison.OrdinalIgnoreCase))throw new IOException("缓存版本目录校验失败，未删除。");
     Records(package,true);
     foreach(string file in Directory.GetFiles(version)){
      if(!allowed.Contains(Path.GetFileName(file))||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("缓存中含额外文件，未删除。");
      // Reject an open library before moving its containing directory.
      using(File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None)){}
     }
    }
    string deletionRoot=ContainedPath(root,".deleting");if(Directory.Exists(deletionRoot)&&(File.GetAttributes(deletionRoot)&FileAttributes.ReparsePoint)!=0)throw new IOException("缓存删除暂存目录被重定向，未删除。");string staging=ContainedPath(deletionRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);string moved=Path.Combine(staging,code),movedIndex=Path.Combine(staging,code+".json");
    try{Directory.Move(folder,moved);try{File.Move(index,movedIndex);}catch{Directory.Move(moved,folder);throw;}}
    catch{if(Directory.Exists(staging)&&!Directory.EnumerateFileSystemEntries(staging).Any())Directory.Delete(staging);throw;}
    var result=new CacheRemovalResult{Versions=versions.Length,ResidualPath=""};
    try{
     foreach(string version in Directory.GetDirectories(moved)){foreach(string file in Directory.GetFiles(version))File.Delete(file);Directory.Delete(version);}
     Directory.Delete(moved);File.Delete(movedIndex);Directory.Delete(staging);
    }catch(Exception error){UserLog.Write("Cache removal residual "+staging,error);result.ResidualPath=staging;}
    UserLog.Write("Removed downloaded "+code+" versions="+result.Versions);return result;
   }
  }

  public LibraryPackage EnrichDescription(LibraryRecord record,string converter,CancellationToken cancellation){return EnrichDescription(record,converter,cancellation,false);}
  public LibraryPackage EnrichDescription(LibraryRecord record,string converter,CancellationToken cancellation,bool refresh){
   refresh=refresh||(record.Package!=null&&record.Package.DescriptionRefreshRequired);
   if(record.Package==null)return null;string code=Normalize(record.Code);string run=ContainedPath(Path.Combine(root,".staging"),"description-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
   string output=Path.Combine(run,"description.json"),cache=Path.Combine(root,"SourceCache");
   try{
    var info=new ProcessStartInfo(converter,"--describe "+code+(refresh?" --refresh-description":"")+" --cache-dir \""+cache+"\" --output \""+output+"\""){WorkingDirectory=run,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    using(var process=Process.Start(info)){
     if(process==null)return null;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
     try{while(!process.WaitForExit(100)){cancellation.ThrowIfCancellationRequested();if(timer.ElapsedMilliseconds>23000)throw new TimeoutException("元件简介获取超时");}cancellation.ThrowIfCancellationRequested();string log=stderr.GetAwaiter().GetResult()+stdout.GetAwaiter().GetResult();UserLog.Write("Description "+code+" elapsed_ms="+timer.ElapsedMilliseconds+Environment.NewLine+log);if(process.ExitCode!=0)return null;}
     finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(2000);}}catch{}}
    }
    if(!File.Exists(output)||new FileInfo(output).Length>65536)return null;var metadata=Json.Read<ComponentMetadata>(output);string description=CleanDescription(metadata==null?null:metadata.Description);if(description.Length==0)return null;
    lock(IndexGate){
     cancellation.ThrowIfCancellationRequested();if(!File.Exists(IndexPath(code)))return null;var current=Json.Read<LibraryPackage>(IndexPath(code));if(current.RelativeFolder!=record.Package.RelativeFolder)return null;
     current.Description=description;current.DescriptionVersion=metadata.DescriptionVersion;current.DescriptionSource=metadata.DescriptionSource;current.DescriptionRefreshRequired=false;string folder=ContainedPath(root,current.RelativeFolder);Json.WriteAtomic(Path.Combine(folder,"component.json"),current);Json.WriteAtomic(IndexPath(code),current);return current;
    }
   }finally{if(Directory.Exists(run))Directory.Delete(run,true);}
  }
  static string RunConverter(string converter,string code,string output,string workingDirectory,CancellationToken cancellation,string appearancePath,string cacheDirectory,bool refreshSource,bool include3d){
   string helper=Path.Combine(Path.GetDirectoryName(converter),"footprint-repair.exe");
   string sourceLog=RunRepairHelper(helper,"--prepare --lcsc-id "+code+" --cache-dir \""+cacheDirectory+"\""+(refreshSource?" --refresh":""),workingDirectory,cancellation);
   string snapshot=Path.Combine(workingDirectory,"cad-source.json");
   try{
   cancellation.ThrowIfCancellationRequested();
   File.Copy(Path.Combine(cacheDirectory,code+".json"),snapshot,false);
   var info=new ProcessStartInfo(converter,"--lcsc_id "+code+" --symbol --footprint "+(include3d?"--3d ":"--3d-cached ")+(refreshSource?"--refresh-description ":"")+"--cad-source \""+snapshot+"\" --cache-dir \""+cacheDirectory+"\" --appearance \""+appearancePath+"\" --output \""+output+"\""){WorkingDirectory=workingDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   using(var process=Process.Start(info)){
    if(process==null)throw new IOException("无法启动在线转换器。");
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
    try{
     while(!process.WaitForExit(150)){cancellation.ThrowIfCancellationRequested();if(timer.ElapsedMilliseconds>180000)throw new TimeoutException("获取超过 180 秒，请检查网络后重试。");}
     cancellation.ThrowIfCancellationRequested();string log=stderr.GetAwaiter().GetResult()+stdout.GetAwaiter().GetResult();
     UserLog.Write("Converter "+code+" exit="+process.ExitCode+" elapsed_ms="+timer.ElapsedMilliseconds+Environment.NewLine+log);
     if(process.ExitCode!=0)throw new IOException("在线获取失败，请检查 C 编号和网络；详情可通过“设置 → 导出反馈”查看。");
     if(!Regex.IsMatch(log,@"(?m)^\s*source-input: pinned-cad-1\s*$"))throw new IOException("元件处理版本不匹配，请安装完整新版。");
     string repairLog=RunRepairHelper(helper,"--lcsc-id "+code+" --source \""+snapshot+"\" --library \""+output+".PcbLib\"",workingDirectory,cancellation);
     return sourceLog+log+repairLog;
    }finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch(Exception error){UserLog.Write("Stop converter",error);}}
   }
   }finally{if(File.Exists(snapshot))File.Delete(snapshot);}
  }
  static string RunRepairHelper(string executable,string arguments,string workingDirectory,CancellationToken cancellation){
   cancellation.ThrowIfCancellationRequested();if(!File.Exists(executable))throw new FileNotFoundException("缺少封装长孔修复模块，请安装完整新版。",executable);
   var info=new ProcessStartInfo(executable,arguments){WorkingDirectory=workingDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   using(var process=Process.Start(info)){
    if(process==null)throw new IOException("无法启动封装修复模块。");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
    try{
     while(!process.WaitForExit(150)){cancellation.ThrowIfCancellationRequested();if(timer.ElapsedMilliseconds>90000)throw new TimeoutException("来源获取或封装核对超时，请重试。");}
     cancellation.ThrowIfCancellationRequested();string log=stderr.GetAwaiter().GetResult()+stdout.GetAwaiter().GetResult();UserLog.Write("Footprint repair exit="+process.ExitCode+Environment.NewLine+log);
     if(process.ExitCode!=0)throw new IOException("来源获取或封装长孔核对失败，本次结果未保存。请导出日志核对，避免使用未修复的封装。");return log;
    }finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch(Exception error){UserLog.Write("Stop footprint repair",error);}}
   }
  }
  public static ProjectFiles CopyToProject(LibraryRecord record,string projectPath){
   if(record.Package==null)throw new ArgumentException("该记录不是在线元件。");
   if(record.Package.AcquisitionVersion!=ReleaseInfo.Acquisition)throw new IOException("此在线库尚未按新版核对长孔，请在设置中重新获取该 C 编号后再放置。旧工程库保持原样。");
   if(!File.Exists(projectPath)||!Path.GetExtension(projectPath).Equals(".PrjPcb",StringComparison.OrdinalIgnoreCase))throw new IOException("请先保存目标 PCB 工程（PrjPcb）。");
   var source=new LibraryStore(record.LibraryRoot);var rows=source.Records(record.Package,true);var package=record.Package;
   string revision=package.SymbolHash.Substring(0,12)+package.FootprintHash.Substring(0,12);
   string folder=Path.Combine(Path.GetDirectoryName(projectPath),"Libraries","LibrarySearch",package.Code,revision);
   var result=new ProjectFiles{SymbolPath=Path.Combine(folder,package.Code+".SchLib"),FootprintPath=Path.Combine(folder,package.Code+".PcbLib")};
   if(!Directory.Exists(folder)){
    string staging=folder+"-"+Guid.NewGuid().ToString("N")+".tmp";Directory.CreateDirectory(staging);
    try{
     File.Copy(rows[0].FilePath,Path.Combine(staging,package.Code+".SchLib"));File.Copy(rows[1].FilePath,Path.Combine(staging,package.Code+".PcbLib"));
     File.Copy(Path.Combine(Path.GetDirectoryName(rows[0].FilePath),"SOURCE.txt"),Path.Combine(staging,"SOURCE.txt"));
     File.WriteAllText(Path.Combine(staging,"README.txt"),"LibrarySearch "+ReleaseInfo.Version+"\r\n"+package.Code+"\r\nSymbol: "+package.SymbolName+"\r\nFootprint: "+package.FootprintName+"\r\n请随工程一起保留此目录。\r\n",Encoding.UTF8);
     try{Directory.Move(staging,folder);}catch(IOException){if(!Directory.Exists(folder))throw;}
    }finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
   }
   if(Hash(result.SymbolPath)!=package.SymbolHash||Hash(result.FootprintPath)!=package.FootprintHash)throw new IOException("工程内的库文件已被修改，请保留原文件后处理冲突："+folder);
   return result;
  }
 }
}
