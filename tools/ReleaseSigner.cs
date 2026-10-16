using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

// Maintainer tool only. No private key, signer or key-generation code ships in the app.
internal static class ReleaseSigner
{
    static int Main(string[] args) {
        try {
            if(args.Length<2)throw new Exception("Usage: ReleaseSigner init|public|sign KEYFILE [JSONFILE]");
            string path=Path.GetFullPath(args[1]);
            using(var rsa=new RSACryptoServiceProvider(3072)) {
                rsa.PersistKeyInCsp=false;
                if(args[0]=="init") {
                    if(File.Exists(path))throw new Exception("Key already exists; refusing to rotate it.");
                    string folder=Path.GetDirectoryName(path);Directory.CreateDirectory(folder);
                    var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
                    acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                    Directory.SetAccessControl(folder,acl);
                    byte[] key=rsa.ExportCspBlob(true);
                    try { byte[] protectedKey=ProtectedData.Protect(key,null,DataProtectionScope.CurrentUser);using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))f.Write(protectedKey,0,protectedKey.Length); }
                    finally { Array.Clear(key,0,key.Length); }
                } else {
                    byte[] key=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
                    try { rsa.ImportCspBlob(key); }finally { Array.Clear(key,0,key.Length); }
                }
                string xml=rsa.ToXmlString(false);
                if(args[0]=="init"||args[0]=="public")Console.WriteLine(xml);
                else if(args[0]=="sign"&&args.Length==3) {
                    var json=new JavaScriptSerializer();var obj=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));
                    string product=(string)obj["product"],version=(string)obj["version"];
                    if(product!="WSG-Stream-Feeder"||Convert.ToInt32(obj["schema"])!=1)throw new Exception("Wrong release identity.");
                    byte[] message;
                    if(obj.ContainsKey("files")) {
                        var files=new Dictionary<string,string>();foreach(var item in (Dictionary<string,object>)obj["files"])files.Add(item.Key,(string)item.Value);
                        message=ReleaseTrust.InventoryMessage(product,version,files);
                    } else message=ReleaseTrust.ReleaseMessage(product,version,(string)obj["sha256"],Convert.ToInt64(obj["size"]));
                    obj["keyId"]=ReleaseTrust.Digest(Encoding.UTF8.GetBytes(xml));
                    obj["signature"]=Convert.ToBase64String(rsa.SignData(message,CryptoConfig.MapNameToOID("SHA256")));
                    File.WriteAllText(args[2],json.Serialize(obj)+"\n",new UTF8Encoding(false));
                } else throw new Exception("Invalid signer operation.");
            }
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e.Message);return 1; }
    }
}
