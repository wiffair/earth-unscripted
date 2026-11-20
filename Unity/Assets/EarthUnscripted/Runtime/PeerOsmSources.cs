using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
namespace EarthUnscripted
{
    // Raw public OSM only. Peers never transfer executable worker code or
    // process-specific SQLite indexes, and completion never replaces a world.
    public static class PeerOsmSources
    {
        public const int MaximumOffers=3;
        static bool Link(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
        public static bool Matches(DataFileManifest item)
            =>DataPackageRules.Valid(item)&&item.kind=="osm-source"&&DateTimeOffset.TryParse(item.timestamp,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)&&at<=DateTimeOffset.UtcNow&&at>DateTimeOffset.UtcNow.AddDays(-7);
        static void Atomic(string destination,Action<string> write)
        {
            string partial=destination+"."+Guid.NewGuid().ToString("N")+".partial";
            try{write(partial);if(File.Exists(destination))File.Replace(partial,destination,null);else File.Move(partial,destination);}
            finally{if(File.Exists(partial))File.Delete(partial);}
        }
        public static PeerFileSource[] Catalogue(string cacheRoot,string snapshotRoot,DataSigningKey key,CancellationToken token,string[] protectedPaths=null)
        {
            if(!Directory.Exists(cacheRoot)||Link(cacheRoot))return Array.Empty<PeerFileSource>();
            Directory.CreateDirectory(snapshotRoot);var result=new List<PeerFileSource>();
            foreach(string path in Directory.EnumerateFiles(cacheRoot,"*.osm.pbf").OrderByDescending(p=>File.GetLastWriteTimeUtc(p)).Take(64))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if(Link(path))continue;string metadata=path+".receipt.json";
                    if(!File.Exists(metadata)||Link(metadata)||new FileInfo(metadata).Length>32768)continue;
                    var receipt=JObject.Parse(File.ReadAllText(metadata));string url=(string)receipt["url"],sha=(string)receipt["sha256"];long bytes=(long?)receipt["bytes"]??0;
                    if(!DataPackageRules.OsmSourceUrl(url)||bytes<1||bytes>DataPackageRules.MaximumOsmSourceBytes||new FileInfo(path).Length!=bytes)continue;
                    double fetched=(double?)receipt["fetchedUTC"]??double.NaN;if(double.IsNaN(fetched)||double.IsInfinity(fetched))continue;
                    var at=DateTimeOffset.FromUnixTimeSeconds((long)fetched);
                    var manifest=new DataFileManifest{format=2,id="osm-"+sha,kind="osm-source",pack="osm-url-"+DataPackageRules.Hash(Encoding.UTF8.GetBytes(url)).Substring(0,32),fromVersion="raw-osm-pbf-v1",toVersion=sha,sha256=sha,source=url,bytes=bytes,timestamp=at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ",CultureInfo.InvariantCulture),licence="ODbL-1.0"};
                    if(!Matches(manifest))continue;
                    string snapshot=DataPackageRules.PathFor(snapshotRoot,manifest);
                    if(!DataPackageRules.FileMatches(snapshot,manifest))
                    {
                        if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(snapshotRoot))).AvailableFreeSpace<bytes+2_000_000_000L)break;
                        // Snapshot through an open immutable file handle. Cache
                        // LRU can retire its pathname without interrupting us.
                        Atomic(snapshot,partial=>
                        {
                            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                            using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                            {var buffer=new byte[65536];int n;long total=0;while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();total+=n;if(total>manifest.bytes)throw new InvalidDataException("Source grew during snapshot");output.Write(buffer,0,n);}}
                        });
                        if(!DataPackageRules.FileMatches(snapshot,manifest)){File.Delete(snapshot);continue;}
                    }
                    lock(key)key.Sign(manifest);result.Add(new PeerFileSource{manifest=manifest,path=snapshot});
                    if(result.Count==MaximumOffers)break;
                }
                catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is JsonException||e is ArgumentException||e is FormatException||e is OverflowException){}
            }
            // The connection is serial: catalogue replacement happens only
            // between transfers, so retain its current bounded offer window.
            var keep=new HashSet<string>(result.Select(x=>x.path).Concat(protectedPaths??Array.Empty<string>()),StringComparer.Ordinal);
            foreach(string old in Directory.EnumerateFiles(snapshotRoot,"*.osm.pbf"))if(!keep.Contains(old))try{File.Delete(old);}catch(IOException){}catch(UnauthorizedAccessException){}
            return result.ToArray();
        }
        static long Quota(string root)
        {
            try
            {
                var row=JObject.Parse(File.ReadAllText(Path.Combine(root,"..","storage-settings.json")));var token=row["osmGigabytes"];
                if(token==null||(token.Type!=JTokenType.Integer&&token.Type!=JTokenType.Float))return 5_000_000_000L;
                double value=(double)token;return double.IsNaN(value)||double.IsInfinity(value)?5_000_000_000L:(long)(Math.Max(.5,Math.Min(50,value))*1e9);
            }
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is JsonException||e is FormatException||e is InvalidCastException){return 5_000_000_000L;}
        }
        static bool ReceiptMatches(string metadata,DataFileManifest item,bool peer)
        {
            try
            {
                if(!File.Exists(metadata)||Link(metadata)||new FileInfo(metadata).Length>32768)return false;
                var row=JObject.Parse(File.ReadAllText(metadata));var stamp=row["fetchedUTC"];
                if(stamp==null||(stamp.Type!=JTokenType.Integer&&stamp.Type!=JTokenType.Float))return false;
                double time=(double)stamp,age=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000d-time;
                return (string)row["url"]==item.source&&(string)row["sha256"]==item.sha256&&(long?)row["bytes"]==item.bytes&&!double.IsNaN(age)&&age>=0&&age<7*86400&&(!peer||((string)row["origin"]=="authenticated-gameplay-peer"&&(string)row["licence"]=="ODbL-1.0"));
            }
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is JsonException||e is InvalidCastException||e is FormatException||e is OverflowException){return false;}
        }
        public static bool Cached(string root,DataFileManifest item)
        {
            if(!Matches(item))return false;string incoming=Path.Combine(root,"Incoming",item.sha256+".osm.pbf");
            if(File.Exists(incoming)&&!Link(incoming)&&ReceiptMatches(incoming+".receipt.json",item,true)&&DataPackageRules.FileMatches(incoming,item))return true;
            if(!Directory.Exists(root))return false;
            foreach(string path in Directory.EnumerateFiles(root,"*.osm.pbf").Take(64))
                try
                {
                    string metadata=path+".receipt.json";if(Link(path)||!File.Exists(metadata)||new FileInfo(metadata).Length>32768)continue;
                    if(ReceiptMatches(metadata,item,false)&&DataPackageRules.FileMatches(path,item))return true;
                }
                catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is JsonException){}
            return false;
        }
        public static string Stage(PeerFileResult received,string root,string fingerprint)
        {
            var item=received?.manifest;
            if(!Matches(item)||!DataPackageRules.VerifySignature(item,fingerprint)||!DataPackageRules.FileMatches(received.path,item))throw new InvalidDataException("Peer OSM source failed signed source/hash validation");
            Directory.CreateDirectory(root);if(Link(root))throw new InvalidDataException("OSM cache cannot be a link");
            string incoming=Path.Combine(root,"Incoming");Directory.CreateDirectory(incoming);if(Link(incoming))throw new InvalidDataException("OSM incoming cache cannot be a link");
            string destination=Path.Combine(incoming,item.sha256+".osm.pbf");
            if(!DataPackageRules.FileMatches(destination,item))
            {
                long used=0;int count=0;
                foreach(string directory in new[]{root,incoming})foreach(string path in Directory.EnumerateFiles(directory))
                {if(++count>20000)throw new IOException("OSM cache inventory is too large");if(!Link(path))used+=new FileInfo(path).Length;}
                if(used+item.bytes>Quota(root)||new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace<2_000_000_000L)throw new IOException("OSM cache quota or free-space reserve reached");
                // PeerData and world cache share the product volume. Move the
                // verified payload instead of keeping a second full copy.
                if(File.Exists(destination))File.Delete(destination);File.Move(received.path,destination);
            }
            double fetched=DateTimeOffset.Parse(item.timestamp,CultureInfo.InvariantCulture).ToUnixTimeSeconds();
            Atomic(destination+".receipt.json",p=>File.WriteAllText(p,new JObject{["url"]=item.source,["sha256"]=item.sha256,["bytes"]=item.bytes,["fetchedUTC"]=fetched,["origin"]="authenticated-gameplay-peer",["peerFingerprint"]=fingerprint,["licence"]=item.licence}.ToString(Formatting.None)));
            if(Path.GetFullPath(received.path)!=Path.GetFullPath(destination)&&File.Exists(received.path))File.Delete(received.path);
            return destination;
        }
    }
}
