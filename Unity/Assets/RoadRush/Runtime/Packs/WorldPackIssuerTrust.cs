using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoadRush.Packs
{
    /// <summary>
    /// Explicit local trust in test-report issuers. Importing a pack or certificate never calls SetTrusted.
    /// Fingerprints identify public keys, not publisher names or proof of content accuracy/licensing.
    /// </summary>
    public sealed class WorldPackIssuerTrust
    {
        public const int MaximumIssuers = 256;
        public const int MaximumBytes = 32768;
        readonly string root, path;
        public WorldPackIssuerTrust(string libraryRoot)
        {
            if(string.IsNullOrWhiteSpace(libraryRoot))throw new ArgumentException("Local library required");
            root=Path.Combine(Path.GetFullPath(libraryRoot),"validations");path=Path.Combine(root,"issuers.json");
            WorldPackStore.CheckAncestors(path);
        }
        static bool Digest(string value)=>value!=null&&Regex.IsMatch(value,"\\A[a-f0-9]{64}\\z");
        public ISet<string> Load(CancellationToken token=default)
        {
            token.ThrowIfCancellationRequested();WorldPackStore.CheckAncestors(path);
            if(!File.Exists(path))return new HashSet<string>(StringComparer.Ordinal);
            byte[] bytes;
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                WorldPackPolicy.Require(input.Length>0&&input.Length<=MaximumBytes,"issuer settings byte budget");
                bytes=new byte[(int)input.Length];int offset=0;
                while(offset<bytes.Length)
                {token.ThrowIfCancellationRequested();int count=input.Read(bytes,offset,bytes.Length-offset);WorldPackPolicy.Require(count>0,"truncated issuer settings");offset+=count;}
                WorldPackPolicy.Require(input.ReadByte()==-1,"issuer settings length changed");
            }
            using(var reader=new JsonTextReader(new StringReader(new UTF8Encoding(false,true).GetString(bytes))) {MaxDepth=3,DateParseHandling=DateParseHandling.None})
            {
                var value=JObject.Load(reader,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                WorldPackPolicy.Require(!reader.Read()&&value.Count==2&&value["format"]?.Type==JTokenType.String&&(string)value["format"]=="RoadRushIssuerTrust1","issuer settings format");
                var keys=value["fingerprints"] as JArray;
                WorldPackPolicy.Require(keys!=null&&keys.Count<=MaximumIssuers&&keys.All(k=>k.Type==JTokenType.String&&Digest((string)k)),"issuer settings fingerprints");
                var list=keys.Select(k=>(string)k).ToArray();
                WorldPackPolicy.Require(list.Distinct(StringComparer.Ordinal).Count()==list.Length,"duplicate issuer fingerprint");
                return new HashSet<string>(list,StringComparer.Ordinal);
            }
        }
        /// <summary>Call only after a deliberate local trust/revoke action; not as part of import or peer sharing.</summary>
        public void SetTrusted(string fingerprint,bool trusted,CancellationToken token=default)
        {
            WorldPackPolicy.Require(Digest(fingerprint),"issuer fingerprint identity");token.ThrowIfCancellationRequested();
            WorldPackStore.CheckAncestors(root);Directory.CreateDirectory(root);WorldPackStore.CheckAncestors(root);
            string lockPath=Path.Combine(root,".issuer-trust.lock");WorldPackStore.CheckAncestors(lockPath);
            using(var gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                var keys=Load(token);bool changed=trusted?keys.Add(fingerprint):keys.Remove(fingerprint);
                if(!changed)return;
                WorldPackPolicy.Require(keys.Count<=MaximumIssuers,"issuer count budget");
                byte[] bytes=Encoding.UTF8.GetBytes(new JObject { ["format"]="RoadRushIssuerTrust1",
                    ["fingerprints"]=new JArray(keys.OrderBy(k=>k,StringComparer.Ordinal)) }.ToString(Formatting.None));
                WorldPackPolicy.Require(bytes.Length<=MaximumBytes,"issuer settings byte budget");
                string stage=Path.Combine(root,".issuers-"+Guid.NewGuid().ToString("N")+".tmp"),previous=path+".previous";
                try
                {
                    using(var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {output.Write(bytes,0,bytes.Length);output.Flush(true);}
                    token.ThrowIfCancellationRequested();WorldPackStore.CheckAncestors(path);WorldPackStore.CheckAncestors(previous);
                    if(File.Exists(path))File.Replace(stage,path,previous);else File.Move(stage,path);
                }
                finally {if(File.Exists(stage))File.Delete(stage);}
            }
        }
    }
}
