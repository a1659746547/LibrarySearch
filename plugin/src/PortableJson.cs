#if !NET6_0_OR_GREATER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace LibrarySearch.Serialization {
 public sealed class JavaScriptSerializer {
  public int MaxJsonLength {get;set;}
  public int RecursionLimit {get;set;}
  public JavaScriptSerializer(){MaxJsonLength=2097152;RecursionLimit=100;}
  public object DeserializeObject(string json){
   if(json==null)throw new ArgumentNullException("json");
   if(json.Length>MaxJsonLength)throw new IOException("JSON exceeds size limit");
   ValidateEnvelope(json);
   var quotas=new XmlDictionaryReaderQuotas{MaxDepth=RecursionLimit,MaxStringContentLength=MaxJsonLength,MaxArrayLength=MaxJsonLength};
   using(var reader=JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json),quotas)){
    reader.MoveToContent();object value=ReadValue(reader);
    if(reader.MoveToContent()!=XmlNodeType.None)throw new IOException("Unexpected trailing JSON content");
    return value;
   }
  }
  static void ValidateEnvelope(string json){
   string content=json.Trim();if(content.Length==0)throw new IOException("Empty JSON");
   bool structured=content[0]=='{'||content[0]=='[',quoted=false,escaped=false;int depth=0;
   if(!structured&&content[0]!='"'){if(content.IndexOfAny(new[]{' ','\t','\r','\n'})>=0)throw new IOException("Unexpected trailing JSON content");return;}
   for(int index=0;index<content.Length;index++){
    char character=content[index];
    if(quoted){if(escaped)escaped=false;else if(character=='\\')escaped=true;else if(character=='"')quoted=false;}
    else if(character=='"')quoted=true;
    else if(character=='{'||character=='[')depth++;
    else if(character=='}'||character==']')depth--;
    if(!quoted&&depth==0){if(index!=content.Length-1)throw new IOException("Unexpected trailing JSON content");return;}
   }
   throw new IOException("Incomplete JSON");
  }
  static object ReadValue(XmlDictionaryReader reader){
   string kind=reader.GetAttribute("type");
   if(kind=="object"||kind=="array"){
    var fields=new Dictionary<string,object>();var items=new List<object>();bool empty=reader.IsEmptyElement;reader.ReadStartElement();
    if(!empty){
     while(reader.MoveToContent()==XmlNodeType.Element){
      string name=reader.NamespaceURI=="item"?reader.GetAttribute("item"):reader.LocalName;
      object value=ReadValue(reader);if(kind=="array")items.Add(value);else fields[name]=value;
     }
     reader.ReadEndElement();
    }
    return kind=="array"?(object)items.ToArray():fields;
   }
   string text=reader.ReadElementContentAsString();
   if(kind=="null")return null;
   if(kind=="boolean")return bool.Parse(text);
   if(kind=="number"){
    long integer;if(long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out integer))return integer;
    double number=double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
    if(double.IsInfinity(number)||double.IsNaN(number))throw new IOException("Invalid JSON number");return number;
   }
   return text;
  }
  public T Deserialize<T>(string json){return (T)Bind(DeserializeObject(json),typeof(T));}
  static object Bind(object value,Type target){
   if(value==null){if(target.IsValueType&&Nullable.GetUnderlyingType(target)==null)throw new IOException("Null for non-nullable JSON value");return null;}
   if(target.IsInstanceOfType(value))return value;
   Type underlying=Nullable.GetUnderlyingType(target);if(underlying!=null)return Bind(value,underlying);
   if(target.IsArray){
    var source=value as object[];if(source==null)throw new IOException("Expected JSON array");
    Type element=target.GetElementType();Array result=Array.CreateInstance(element,source.Length);
    for(int index=0;index<source.Length;index++)result.SetValue(Bind(source[index],element),index);return result;
   }
   var fields=value as Dictionary<string,object>;
   if(fields!=null){
    object result=Activator.CreateInstance(target);var names=new Dictionary<string,object>(fields,StringComparer.OrdinalIgnoreCase);
    foreach(var property in target.GetProperties(BindingFlags.Instance|BindingFlags.Public)){
     object member;if(property.GetIndexParameters().Length==0&&property.GetSetMethod()!=null&&names.TryGetValue(property.Name,out member))property.SetValue(result,Bind(member,property.PropertyType),null);
    }
    foreach(var field in target.GetFields(BindingFlags.Instance|BindingFlags.Public)){
     object member;if(!field.IsInitOnly&&names.TryGetValue(field.Name,out member))field.SetValue(result,Bind(member,field.FieldType));
    }
    return result;
   }
   if(target.IsEnum)return Enum.ToObject(target,Convert.ChangeType(value,Enum.GetUnderlyingType(target),CultureInfo.InvariantCulture));
   return Convert.ChangeType(value,target,CultureInfo.InvariantCulture);
  }
  public string Serialize(object value){var output=new StringBuilder();WriteValue(output,value,0);return output.ToString();}
  void WriteValue(StringBuilder output,object value,int depth){
   if(depth>=RecursionLimit)throw new IOException("JSON exceeds depth limit");
   if(value==null)output.Append("null");
   else if(value is string||value is char)WriteString(output,Convert.ToString(value,CultureInfo.InvariantCulture));
   else if(value is bool)output.Append((bool)value?"true":"false");
   else if(value.GetType().IsEnum||value is byte||value is sbyte||value is short||value is ushort||value is int||value is uint||value is long||value is ulong||value is decimal||value is double||value is float){
    string number=value.GetType().IsEnum?Convert.ToInt64(value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture);
    if(number=="NaN"||number.Contains("Infinity"))throw new IOException("Invalid JSON number");output.Append(number);
   }else if(value is IDictionary){
    output.Append('{');bool first=true;foreach(DictionaryEntry entry in (IDictionary)value){if(!(entry.Key is string))throw new IOException("JSON keys must be strings");WriteMember(output,(string)entry.Key,entry.Value,depth,ref first);}output.Append('}');
   }else if(value is IEnumerable){
    output.Append('[');bool first=true;foreach(object item in (IEnumerable)value){if(!first)output.Append(',');first=false;WriteValue(output,item,depth+1);}output.Append(']');
   }else{
    output.Append('{');bool first=true;
    foreach(var field in value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))WriteMember(output,field.Name,field.GetValue(value),depth,ref first);
    foreach(var property in value.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public))if(property.GetIndexParameters().Length==0&&property.GetGetMethod()!=null)WriteMember(output,property.Name,property.GetValue(value,null),depth,ref first);
    output.Append('}');
   }
   if(output.Length>MaxJsonLength)throw new IOException("JSON exceeds size limit");
  }
  void WriteMember(StringBuilder output,string name,object value,int depth,ref bool first){if(!first)output.Append(',');first=false;WriteString(output,name);output.Append(':');WriteValue(output,value,depth+1);}
  static void WriteString(StringBuilder output,string value){
   output.Append('"');foreach(char character in value){
    switch(character){
     case '"':output.Append("\\\"");break;
     case '\\':output.Append("\\\\");break;
     case '\b':output.Append("\\b");break;
     case '\f':output.Append("\\f");break;
     case '\n':output.Append("\\n");break;
     case '\r':output.Append("\\r");break;
     case '\t':output.Append("\\t");break;
     default:if(character<32||character=='\''||character=='<'||character=='>'||char.IsSurrogate(character))output.Append("\\u").Append(((int)character).ToString("x4",CultureInfo.InvariantCulture));else output.Append(character);break;
    }
   }output.Append('"');
  }
 }
}
#endif
