using System;
using System.IO;
using System.Text;
using System.Linq;
using System.IO.Compression;

namespace LibrarySearch {
 public static class Diagnostics {
  static string Mask(string value){string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);return string.IsNullOrEmpty(profile)?value:value.Replace(profile,"%USERPROFILE%");}
  static void Add(ZipArchive archive,string name,string text){var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false)))writer.Write(Mask(text));}
  public static void Export(string target,string libraryRoot){
   string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{
    using(var file=File.Create(temporary))using(var archive=new ZipArchive(file,ZipArchiveMode.Create)){
     Add(archive,"反馈填写.txt","LibrarySearch "+ReleaseInfo.Version+"\r\n元件 C 编号：\r\n操作步骤：\r\n期望结果：\r\n实际结果：\r\nAD 完整版本：\r\n是否可重复：\r\n\r\n可另附截图。此包不会自动发送，不包含工程和库文件。\r\n日志可能包含文件名和用户自定义目录；发送前请检查。\r\n");
     Add(archive,"environment.txt","Version: "+ReleaseInfo.Version+"\r\nOS: "+Environment.OSVersion+"\r\n.NET: "+Environment.Version+"\r\nx64 process: "+Environment.Is64BitProcess+"\r\nLibrary directory: "+libraryRoot+"\r\nCreated: "+DateTime.Now.ToString("o")+"\r\n");
     if(Directory.Exists(UserLog.Folder))foreach(string path in Directory.GetFiles(UserLog.Folder,"*.log").OrderByDescending(path=>File.GetLastWriteTimeUtc(path)).Take(3)){
      using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){stream.Seek(Math.Max(0,stream.Length-512*1024),SeekOrigin.Begin);using(var reader=new StreamReader(stream,Encoding.UTF8))Add(archive,"logs/"+Path.GetFileName(path),reader.ReadToEnd());}
     }
    }
    if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 }
}
