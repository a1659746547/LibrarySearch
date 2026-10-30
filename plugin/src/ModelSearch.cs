using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LibrarySearch {
 public sealed class ModelCandidate {
  public string Code,Model,Manufacturer,Package,Description;
  public bool HasLibrary;
 }
 public sealed class ModelSearchPage {
  public ModelCandidate[] Items;
  public int Total,Page;
  public int PageSize=ModelSearchClient.PageSize;
 }
 public static class ModelSearchClient {
  public const int PageSize=10;
  public static bool IsCode(string value){return Regex.IsMatch((value??"").Trim(),@"\AC[0-9]{1,12}\z",RegexOptions.IgnoreCase);}
  public static string ValidateQuery(string value){
   string query=(value??"").Trim();
   if(query.Length<2||query.Length>100||query.Any(char.IsControl))throw new ArgumentException("请输入 2–100 个字符的型号、厂家或 C 编号。");
   return query;
  }
  static string Text(Dictionary<string,object> row,params string[] keys){
   foreach(string key in keys){object value;if(row.TryGetValue(key,out value)&&value!=null){string text=Convert.ToString(value).Trim();if(text.Length>0)return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]*>","")),@"[\r\n\t]+"," ");}}
   return "";
  }
  public static ModelSearchPage Parse(string json,int page){return Parse(json,page,PageSize);}
  public static ModelSearchPage Parse(string json,int page,int pageSize){
   var root=new JavaScriptSerializer{MaxJsonLength=8*1024*1024,RecursionLimit=128}.DeserializeObject(json) as Dictionary<string,object>;
   object raw;int status;
   if(root==null||!root.TryGetValue("code",out raw)||!int.TryParse(Convert.ToString(raw),out status)||status!=200)throw new IOException("型号搜索服务未返回成功结果，请稍后重试，或直接输入 C 编号。");
   if(!root.TryGetValue("result",out raw))throw new IOException("型号搜索结果格式已变化。");
   var result=raw as Dictionary<string,object>;
   if(result==null||!result.TryGetValue("productList",out raw)||!(raw is object[]))throw new IOException("型号搜索结果格式已变化。");
   var items=new List<ModelCandidate>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var item in (object[])raw){
    var row=item as Dictionary<string,object>;if(row==null)continue;
    string code=Text(row,"code","number").ToUpperInvariant();if(!IsCode(code)||!seen.Add(code))continue;
    string available=Text(row,"hasDevice");
    items.Add(new ModelCandidate{Code=code,Model=Text(row,"model","mpn"),Manufacturer=Text(row,"brandName","manufacturer"),Package=Text(row,"standard","package"),Description=Text(row,"name","desc"),HasLibrary=available.Length>0&&!available.Equals("false",StringComparison.OrdinalIgnoreCase)&&available!="0"});
   }
   int total;if(!result.TryGetValue("total",out raw)||!int.TryParse(Convert.ToString(raw),out total)||total<0)throw new IOException("型号搜索结果缺少总数，请稍后重试。");
   return new ModelSearchPage{Items=items.Take(pageSize).ToArray(),Total=total,Page=page,PageSize=pageSize};
  }
  public static ModelSearchPage Search(string keyword,int page,CancellationToken cancellation){return Search(keyword,page,cancellation,PageSize);}
  public static ModelSearchPage Search(string keyword,int page,CancellationToken cancellation,int pageSize){
   if(pageSize<10||pageSize>50)throw new ArgumentOutOfRangeException("pageSize");
   keyword=ValidateQuery(keyword);if(page<1||page>1000)throw new ArgumentOutOfRangeException("page");cancellation.ThrowIfCancellationRequested();
   // Older .NET host applications may retain a TLS 1.0 default. Preserve their flags and enable TLS 1.2.
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   var request=(HttpWebRequest)WebRequest.Create("https://pro.lceda.cn/api/eda/product/search?keyword="+Uri.EscapeDataString(keyword)+"&type=3&currPage="+page+"&pageSize="+pageSize);
   request.Method="GET";request.UserAgent="Mozilla/5.0 LibrarySearch/"+ReleaseInfo.Version;request.Accept="application/json";request.Timeout=25000;request.ReadWriteTimeout=25000;request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
   try{
    using(cancellation.Register(request.Abort))using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream,Encoding.UTF8)){
     var buffer=new char[8192];var body=new StringBuilder();int count;
     while((count=reader.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();if(body.Length+count>8*1024*1024)throw new IOException("型号搜索响应过大，请缩小关键词范围。");body.Append(buffer,0,count);}
     cancellation.ThrowIfCancellationRequested();return Parse(body.ToString(),page,pageSize);
    }
   }catch(WebException error){cancellation.ThrowIfCancellationRequested();throw new IOException("搜索连接超时，请检查网络后重试。",error);}
  }
 }
}
