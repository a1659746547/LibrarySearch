using System;
using System.IO;
class ContractDescriptionStub {
 static int Main(string[] a){if(Array.IndexOf(a,"--refresh-description")<0)return 1;int p=Array.IndexOf(a,"--output");File.WriteAllText(a[p+1],"{\"Description\":\"CURRENT-SUPPLIER-DESCRIPTION\",\"DescriptionVersion\":\"product-cn-1\"}");return 0;}
}
