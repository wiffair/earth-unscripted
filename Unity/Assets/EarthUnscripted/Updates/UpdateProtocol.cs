using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EarthUnscripted.Updates
{
    // Shared by the Unity client and the native installer. No Unity/UI calls.
    public static class UpdateProtocol
    {
        public const int MaximumMetadata=2*1024*1024;
        public static readonly Regex Id=new Regex("^[a-z0-9][a-z0-9._-]{0,100}$",RegexOptions.CultureInvariant);
        public static JObject Json(byte[] bytes)
        {
            if(bytes.Length>MaximumMetadata)throw new InvalidDataException("Update metadata is too large.");
            using(var text=new StringReader(new UTF8Encoding(false,true).GetString(bytes)))
            using(var reader=new JsonTextReader(text){DateParseHandling=DateParseHandling.None})
                {var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});while(reader.Read())Require(reader.TokenType==JsonToken.Comment,"Trailing update metadata is not permitted.");return result;}
        }
        public static JObject ReadSigned(byte[] envelope,long minimumSequence=0)
        {return ReadSigned(envelope,new RSAParameters{Modulus=Convert.FromBase64String(UpdateTrust.Modulus),Exponent=Convert.FromBase64String(UpdateTrust.Exponent)},UpdateTrust.KeyId,minimumSequence);}
        public static JObject ReadSigned(byte[] envelope,RSAParameters key,string keyId,long minimumSequence)
        {
            var outer=Json(envelope);
            Require((string)outer["format"]=="EarthUnscriptedSignedUpdates"&&(int?)outer["schemaVersion"]==1&&(string)outer["keyId"]==keyId,"Unrecognised update signature/key.");
            byte[] payload=Convert.FromBase64String((string)outer["payload"]??""),signature=Convert.FromBase64String((string)outer["signature"]??"");
            using(var rsa=new RSACryptoServiceProvider()){rsa.ImportParameters(key);Require(rsa.VerifyData(payload,CryptoConfig.MapNameToOID("SHA256"),signature),"The update signature is invalid.");}
            var data=Json(payload);
            Require((string)data["format"]=="EarthUnscriptedUpdates"&&(int?)data["schemaVersion"]==1,"Unsupported update catalogue.");
            long sequence=(long?)data["sequence"]??-1;Require(sequence>=minimumSequence&&sequence>0,"An older update catalogue was rejected.");
            DateTime issued=DateTime.Parse((string)data["issuedUtc"],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            DateTime expires=DateTime.Parse((string)data["expiresUtc"],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            Require(issued<=DateTime.UtcNow.AddMinutes(15)&&expires>DateTime.UtcNow&&expires>issued&&expires-issued<=TimeSpan.FromDays(100),"Update catalogue has expired or has an invalid date.");
            var game=(JObject)data["game"];VersionParts((string)game["version"]);
            Require((string)game["channel"]=="beta","Wrong update channel.");
            var downloads=(JArray)game["downloads"];Require(downloads.Count==2,"Both graphics/platform packages are required.");
            foreach(string target in new[]{"macOS-arm64","Windows-x64"})
            {var items=downloads.OfType<JObject>().Where(item=>(string)item["target"]==target).ToArray();Require(items.Length==1,"Missing or duplicate update target.");ValidateArtifact(items[0],2L*1024*1024*1024);}
            Require(data["packs"] is JArray,"Managed pack catalogue is missing.");var packs=(JArray)data["packs"];Require(packs.Count<=100,"Too many managed packs.");var ids=new HashSet<string>();
            foreach(var pack in packs.OfType<JObject>())
            {
                string id=(string)pack["id"],kind=(string)pack["kind"];Require(Id.IsMatch(id??"")&&ids.Add(id),"Invalid or duplicate pack identity.");
                Require(kind=="vehicle"||kind=="radio","Unsupported managed pack kind.");VersionParts((string)pack["version"]);VersionParts((string)pack["minimumGameVersion"]);
                ValidateArtifact((JObject)pack["artifact"],kind=="vehicle"?32L*1024*1024:512L*1024*1024);
                if(pack["deltaRecipe"] is JObject recipe)ValidateArtifact(recipe,MaximumMetadata);
                foreach(var hash in pack["approvedAudioSha256"] as JArray??new JArray())Require(HashText((string)hash),"Invalid audio approval hash.");
            }
            return data;
        }
        public static void ValidateArtifact(JObject item,long limit)
        {
            Require(item!=null,"Missing update artifact.");long count=(long?)item["bytes"]??0;
            Require(count>0&&count<=limit&&HashText((string)item["sha256"]),"Invalid artifact size/hash.");ApprovedUrl((string)item["url"]);
        }
        public static Uri ApprovedUrl(string value,bool redirected=false)
        {
            Require(Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&string.IsNullOrEmpty(uri.UserInfo),"Updates require an approved HTTPS download.");
            if(!redirected)Require(uri.Host=="github.com"&&uri.AbsolutePath.StartsWith("/wiffair/earth-unscripted/releases/download/",StringComparison.Ordinal),"Unapproved update download host/path.");
            else Require(new[]{"github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","github-releases.githubusercontent.com"}.Contains(uri.Host),"Unapproved download redirect.");
            return uri;
        }
        public static bool HashText(string value)=>Regex.IsMatch(value??"","^[a-f0-9]{64}$");
        public static string Hash(byte[] value){using(var hash=SHA256.Create())return Hex(hash.ComputeHash(value));}
        public static string HashFile(string path){using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return Hex(hash.ComputeHash(stream));}
        static string Hex(byte[] data)=>BitConverter.ToString(data).Replace("-","").ToLowerInvariant();
        public static void VerifyFile(string path,JObject item)
        {Require(new FileInfo(path).Length==(long)item["bytes"]&&HashFile(path)==(string)item["sha256"],"Downloaded update failed its integrity check.");}
        public static int[] VersionParts(string value)
        {
            var match=Regex.Match(value??"",@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-beta\.([1-9]\d*))?$");
            Require(match.Success,"Invalid update version.");return new[]{int.Parse(match.Groups[1].Value),int.Parse(match.Groups[2].Value),int.Parse(match.Groups[3].Value),match.Groups[4].Success?int.Parse(match.Groups[4].Value):int.MaxValue};
        }
        public static int CompareVersion(string a,string b)
        {var left=VersionParts(a);var right=VersionParts(b);for(int i=0;i<left.Length;i++){int order=left[i].CompareTo(right[i]);if(order!=0)return order;}return 0;}
        public static string SafeRelative(string value)
        {
            Require(!string.IsNullOrEmpty(value)&&value.Length<=500&&!Path.IsPathRooted(value)&&!value.Any(char.IsControl)&&value.IndexOf('\\')<0&&value.IndexOf(':')<0,"Unsafe archive path.");
            string path=value.TrimEnd('/');Require(path.Split('/').All(part=>part.Length>0&&part!="."&&part!=".."&&!part.EndsWith(".")&&!part.EndsWith(" ")&&!Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)",RegexOptions.IgnoreCase)),"Unsafe archive name.");return path;
        }
        public static string Contained(string root,string relative)
        {string path=Path.GetFullPath(Path.Combine(root,SafeRelative(relative)));Require(path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Archive escaped staging.");return path;}
    public static void CopyBounded(Stream input, Stream output, long expected)
    {
        UpdateProtocol.Require(expected >= 0, "Invalid extracted file size.");
        byte[] buffer = new byte[65536]; long total = 0; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            UpdateProtocol.Require(count <= expected - total, "Extracted file exceeds its declared size.");
            output.Write(buffer, 0, count); total += count;
        }
        UpdateProtocol.Require(total == expected, "Incomplete extracted file.");
    }
        public static string ArchiveLink(ZipArchiveEntry entry)
        {
            Require(entry.Length > 0 && entry.Length <= 4096, "Invalid symbolic-link length.");
            using(var input=entry.Open())using(var output=new MemoryStream())
            {CopyBounded(input,output,entry.Length);return new UTF8Encoding(false,true).GetString(output.ToArray());}
        }
        public static void CheckArchive(string zipPath,long maximumExpandedBytes,bool allowSymlinks)
        {
            using(var zip=ZipFile.OpenRead(zipPath))
            {
                Require(zip.Entries.Count<=100000,"Archive has too many entries.");long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var links=new Dictionary<string,string>();
                foreach(var entry in zip.Entries)
                {
                    string name=SafeRelative(entry.FullName);Require(seen.Add(name),"Archive has duplicate or case-conflicting paths.");
                    total=checked(total+entry.Length);Require(total<=maximumExpandedBytes&&entry.Length<=maximumExpandedBytes,"Expanded archive exceeds its signed size limit.");
                    if(((entry.ExternalAttributes>>16)&0xf000)==0xa000)
                    {
                        Require(allowSymlinks&&entry.Length<=4096,"Symbolic links are not permitted here.");
                        {string target=ArchiveLink(entry);Require(!string.IsNullOrEmpty(target)&&!Path.IsPathRooted(target)&&!target.Any(char.IsControl)&&!target.Contains("\\")&&!target.Contains(":"),"Unsafe archive symbolic link.");links.Add(name,target);}
                    }
                }
                foreach(var entry in zip.Entries)
                {
                    string name=SafeRelative(entry.FullName);
                    foreach(string link in links.Keys)Require(!name.StartsWith(link+"/",StringComparison.OrdinalIgnoreCase),"Archive writes below a symbolic link.");
                }
                foreach(var item in links)
                {
                    string target=Path.GetFullPath(Path.Combine("/earth-stage",Path.GetDirectoryName(item.Key)??"",item.Value));
                    Require(target.StartsWith("/earth-stage/",StringComparison.Ordinal),"Symbolic link escapes staging.");
                    string relative=target.Substring("/earth-stage/".Length).Replace('\\','/');
                    // Reject link chains; direct internal framework/file links suffice.
                    Require(!links.Keys.Any(link=>relative.Equals(link,StringComparison.OrdinalIgnoreCase)||relative.StartsWith(link+"/",StringComparison.OrdinalIgnoreCase)),"Archive symbolic-link chains are not supported.");
                }
            }
        }
        public static void AtomicText(string path,string text)
        {Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".partial";File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        public static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    }
}
