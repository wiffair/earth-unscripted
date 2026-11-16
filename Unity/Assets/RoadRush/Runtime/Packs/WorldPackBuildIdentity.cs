using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoadRush.Packs
{
    /// <summary>A freshly checked file snapshot; not an anti-cheat guarantee or a test certificate.</summary>
    public sealed class VerifiedGameBuild
    {
        public readonly string Sha256, EngineVersion, Platform;
        public readonly int FileCount;
        internal VerifiedGameBuild(string hash,string version,string platform,int count)
        {Sha256=hash;EngineVersion=version;Platform=platform;FileCount=count;}
    }
    /// <summary>
    /// Identity of an exact packaged runtime, including code, native engine and scene/resource files.
    /// The detached manifest avoids hashing itself or invalidating a signed Mac app bundle.
    /// A trusted test certificate binds this manifest's SHA256; the manifest alone grants no trust.
    /// Call Verify on an I/O worker when checking claims, not on the render thread.
    /// </summary>
    public static class WorldPackBuildIdentity
    {
        public const int MaximumManifestBytes=4*1024*1024;
        public const int MaximumFiles=8192;
        public const long MaximumFileBytes=2L*1024*1024*1024;
        public const long MaximumTotalBytes=16L*1024*1024*1024;
        static void Exact(JObject value,params string[] fields)
        {WorldPackPolicy.Require(value!=null&&value.Count==fields.Length&&fields.All(f=>value.Property(f)!=null),"build identity fields");}
        static string Text(JObject value,string key)
        {WorldPackPolicy.Require(value[key]?.Type==JTokenType.String,"build identity text "+key);return (string)value[key];}
        static string Portable(string path)
        {WorldPackPolicy.Require(WorldPackPolicy.SafePath(path),"unsafe build path");return path;}
        static byte[] ManifestBytes(string path,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();WorldPackStore.CheckAncestors(path);
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                WorldPackPolicy.Require(input.Length>0&&input.Length<=MaximumManifestBytes,"build manifest byte budget");
                var bytes=new byte[(int)input.Length];int offset=0;
                while(offset<bytes.Length)
                {token.ThrowIfCancellationRequested();int n=input.Read(bytes,offset,bytes.Length-offset);WorldPackPolicy.Require(n>0,"truncated build manifest");offset+=n;}
                WorldPackPolicy.Require(input.ReadByte()==-1,"build manifest length changed");return bytes;
            }
        }
        static string HashFile(string path,long length,CancellationToken token)
        {
            WorldPackStore.CheckAncestors(path);
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var sha=SHA256.Create())
            {
                WorldPackPolicy.Require(input.Length==length,"build file length mismatch");
                byte[] buffer=new byte[65536];long copied=0;
                while(true)
                {
                    token.ThrowIfCancellationRequested();int n=input.Read(buffer,0,buffer.Length);if(n==0)break;
                    copied+=n;WorldPackPolicy.Require(copied<=length,"build file grew");sha.TransformBlock(buffer,0,n,buffer,0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(),0,0);WorldPackPolicy.Require(copied==length,"build file truncated");
                return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
        }
        public static VerifiedGameBuild Verify(string runtimeRoot,string manifestPath,string expectedVersion,
            string expectedPlatform,string expectedEntrypoint,string expectedDataDirectory,CancellationToken token=default)
        {
            string root=Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar), manifest=Path.GetFullPath(manifestPath);
            WorldPackPolicy.Require(Directory.Exists(root)&&Path.GetDirectoryName(root)!=null,"dedicated runtime directory required");
            WorldPackStore.CheckAncestors(root);WorldPackPolicy.EngineVersion(expectedVersion);
            Portable(expectedEntrypoint);Portable(expectedDataDirectory);
            WorldPackPolicy.Require(expectedPlatform=="macos-arm64"||expectedPlatform=="windows-x64","supported build platform");
            byte[] bytes=ManifestBytes(manifest,token);JObject value;
            using(var reader=new JsonTextReader(new StringReader(new UTF8Encoding(false,true).GetString(bytes))) {MaxDepth=5,DateParseHandling=DateParseHandling.None})
            {
                value=JObject.Load(reader,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                WorldPackPolicy.Require(!reader.Read(),"trailing build manifest content");
            }
            Exact(value,"format","engineVersion","platform","entrypoint","dataDirectory","files");
            WorldPackPolicy.Require(Text(value,"format")=="RoadRushBuildIdentity1"&&Text(value,"engineVersion")==expectedVersion&&Text(value,"platform")==expectedPlatform,"build version/platform identity");
            WorldPackPolicy.Require(Text(value,"entrypoint")==expectedEntrypoint&&Text(value,"dataDirectory")==expectedDataDirectory,"build runtime roots differ");
            var files=value["files"] as JArray;
            WorldPackPolicy.Require(files!=null&&files.Count>0&&files.Count<=MaximumFiles,"build file count budget");
            var declared=new Dictionary<string,Tuple<long,string>>(StringComparer.Ordinal);
            var aliases=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
            foreach(var item in files)
            {
                token.ThrowIfCancellationRequested();var file=item as JObject;Exact(file,"path","bytes","sha256");
                string name=Portable(Text(file,"path")), hash=Text(file,"sha256");
                WorldPackPolicy.Require(Regex.IsMatch(hash,"\\A[a-f0-9]{64}\\z")&&file["bytes"]?.Type==JTokenType.Integer,"build file digest/length");
                long length=(long)file["bytes"];WorldPackPolicy.Require(length>=0&&length<=MaximumFileBytes,"build file byte budget");
                total=checked(total+length);WorldPackPolicy.Require(total<=MaximumTotalBytes&&aliases.Add(name),"build total/alias budget");
                declared.Add(name,Tuple.Create(length,hash));
            }
            WorldPackPolicy.Require(declared.ContainsKey(expectedEntrypoint)&&declared.Keys.Any(k=>k.StartsWith(expectedDataDirectory+"/",StringComparison.Ordinal)),"missing runtime executable/data");
            var seen=new HashSet<string>(StringComparer.Ordinal);var pending=new Stack<string>();pending.Push(root);int entries=0;
            while(pending.Count>0)
            {
                token.ThrowIfCancellationRequested();string directory=pending.Pop();WorldPackStore.CheckAncestors(directory);
                foreach(string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();WorldPackPolicy.Require(++entries<=MaximumFiles*4,"build directory inventory budget");
                    WorldPackStore.CheckAncestors(path);string relative=path.Substring(root.Length+1).Replace(Path.DirectorySeparatorChar,'/');Portable(relative);
                    if(Directory.Exists(path)){pending.Push(path);continue;}
                    // NSIS creates its uninstaller after installation; it is not loaded by the game.
                    if(Path.GetFullPath(path)==manifest || expectedPlatform=="windows-x64"&&relative=="Uninstall.exe")continue;
                    WorldPackPolicy.Require(declared.TryGetValue(relative,out var pin)&&seen.Add(relative),"unlisted build file: "+relative);
                    WorldPackPolicy.Require(HashFile(path,pin.Item1,token)==pin.Item2,"build file digest mismatch: "+relative);
                }
            }
            WorldPackPolicy.Require(seen.Count==declared.Count,"missing build files");
            // Re-read the detached descriptor so concurrent replacement cannot silently alter the claimed identity.
            WorldPackPolicy.Require(ManifestBytes(manifest,token).SequenceEqual(bytes),"build manifest changed during verification");
            return new VerifiedGameBuild(WorldPackPolicy.Hash(bytes),expectedVersion,expectedPlatform,seen.Count);
        }
    }
}
