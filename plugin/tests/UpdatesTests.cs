using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using LibrarySearch;
class UpdatesTests {
 static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
 static void Reject(Action action,string label){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,label);}
 static string Feed(string version,string channel){return "{\"current\":{\"version\":\""+version+"\",\"date\":\"2026-10-26\",\"channel\":\""+channel+"\",\"files\":[{\"id\":\"ad24\",\"file\":\"LibrarySearch-AD24-"+version+".zip\",\"size\":1024,\"sha256\":\""+new string('A',64)+"\"}]},\"releases\":[{\"version\":\""+version+"\",\"zh\":\"官网与插件关联，校验下载。\"}]}";}
 [STAThread] static int Main(string[] args){try{
  Check(UpdateClient.CompareVersions("0.6.1-dev.16","0.6.1-dev.8")>0,"numeric prerelease ordering");Check(UpdateClient.CompareVersions("0.6.1","0.6.1-dev.99")>0,"stable version after prerelease");Check(UpdateClient.CompareVersions("0.6.2-dev.1","0.6.1")>0,"compare core version first");Check(UpdateClient.CompareVersions("0.6.1-dev.16","0.6.1-dev.16")==0,"equal version");Reject(()=>UpdateClient.CompareVersions("../latest","0.6.1"),"reject invalid version");
  var release=UpdateClient.Parse(Feed("0.6.1-dev.17","beta"),24);
  Check(release.Notes.Contains("校验")&&release.DownloadUri.AbsoluteUri==UpdateClient.Site+"downloads/LibrarySearch-AD24-0.6.1-dev.17.zip","parse release and fixed official download origin");Check(release.MinAdMajor==24&&release.MaxAdMajor==24,"legacy compatibility defaults to matching AD");
  string stableFeed=Feed("0.6.1","stable");string stableCurrent=stableFeed.Substring(11,stableFeed.IndexOf(",\"releases\"",StringComparison.Ordinal)-11);string betaFeed=Feed("0.6.2-dev.1","beta");string dual=betaFeed.Substring(0,betaFeed.Length-1)+",\"channels\":{\"stable\":"+stableCurrent+"}}";
  Check(UpdateClient.Parse(dual,24,false).Version=="0.6.1","stable users retain stable channel while beta is current");Check(UpdateClient.Parse(dual,24,true).Version=="0.6.2-dev.1","beta users receive newer eligible channel");
  Check(UpdateClient.IsNewer(release,"0.6.1-dev.16",true),"beta channel accepts newer beta");Check(!UpdateClient.IsNewer(release,"0.6.1-dev.16",false),"stable preference rejects beta");Check(!UpdateClient.IsNewer(release,"0.6.1-dev.18",true),"never offer downgrade");Check(UpdateClient.IsNewer(UpdateClient.Parse(Feed("0.6.1","stable"),24),"0.6.1-dev.16",false),"stable channel accepts final release");
  Reject(()=>UpdateClient.Parse(Feed("0.6.1-dev.17","beta"),23),"reject missing AD target");Reject(()=>UpdateClient.Parse(Feed("0.6.1-dev.17","beta").Replace("LibrarySearch-AD24-0.6.1-dev.17.zip","../bad.zip"),24),"reject path traversal");Reject(()=>UpdateClient.Parse(Feed("0.6.1-dev.17","beta").Replace(new string('A',64),"invalid"),24),"require SHA256");Reject(()=>UpdateClient.Parse(Feed("0.6.1-dev.17","beta").Replace("1024","70000000"),24),"bound package size");Reject(()=>UpdateClient.Parse(Feed("0.6.1-dev.17","beta").Replace("\"id\":\"ad24\"","\"id\":\"ad24\",\"minAdMajor\":25"),24),"reject incompatible declared AD range");
  var state=new UpdateState();Check(state.Due(DateTime.UtcNow),"first automatic check is due");state.LastAttemptUtc=DateTime.UtcNow.ToString("o");Check(!state.Due(DateTime.UtcNow),"do not recheck within 24 hours");state.LastAttemptUtc=DateTime.UtcNow.AddHours(-25).ToString("o");Check(state.Due(DateTime.UtcNow),"daily check becomes due");state.Automatic=false;Check(!state.Due(DateTime.UtcNow),"automatic preference disables checks");
  Reject(()=>UpdateClient.Check(24,new CancellationToken(true)),"pre-cancel check");
  string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);string payload=Path.Combine(output,"checksum-test.zip");File.WriteAllBytes(payload,new byte[1024]);release.Size=1024;release.SHA256=LibraryStore.Hash(payload);UpdateClient.VerifyFile(payload,release);Check(true,"verify exact size and checksum");File.WriteAllBytes(payload,new byte[1025]);Reject(()=>UpdateClient.VerifyFile(payload,release),"reject wrong size");File.WriteAllBytes(payload,Enumerable.Repeat((byte)1,1024).ToArray());Reject(()=>UpdateClient.VerifyFile(payload,release),"reject corrupted contents");
  // Embedded update rendering is covered by CustomerFlowTests.
  if(args.Length>1&&args[1]=="--live"){
   var live=UpdateClient.Check(24,CancellationToken.None);Check(live.FileName.EndsWith(".zip")&&live.Size>1024,"read actual website feed");string downloaded=Path.Combine(output,"website-download.zip");UpdateClient.Download(live,downloaded,CancellationToken.None,p=>{});UpdateClient.VerifyFile(downloaded,live);Check(true,"download actual website package with SHA256 verification");
   string before=LibraryStore.Hash(downloaded);live.SHA256=new string('0',64);Reject(()=>UpdateClient.Download(live,downloaded,CancellationToken.None,p=>{}),"reject actual download with invalid expected checksum");Check(LibraryStore.Hash(downloaded)==before,"failed replacement preserves existing file");Check(!Directory.GetFiles(output,"*.part").Any(),"failed download cleans its temporary file");
  }
  Console.WriteLine("RESULT: "+checks+" checks passed");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
