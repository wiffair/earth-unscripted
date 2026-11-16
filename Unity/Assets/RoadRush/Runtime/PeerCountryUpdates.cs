using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using RoadRush.Core;
namespace RoadRush
{
    public static class PeerCountryUpdates
    {
        static readonly object pinLock=new object();static readonly Dictionary<string,int> pins=new Dictionary<string,int>(StringComparer.Ordinal);
        sealed class Lease : IDisposable
        {
            string[] paths;public Lease(string[] paths){this.paths=paths;}
            public void Dispose(){lock(pinLock){if(paths==null)return;foreach(string path in paths){if(pins[path]==1)pins.Remove(path);else pins[path]--;}paths=null;}}
        }
        public static IDisposable Pin(params string[] paths)
        {
            var canonical=paths.Select(Path.GetFullPath).Distinct().ToArray();lock(pinLock){foreach(string path in canonical){pins.TryGetValue(path,out int count);pins[path]=count+1;}}return new Lease(canonical);
        }
        public static int TrimApplied(string root,string countryFolder,long targetBytes=PeerUpdateRules.MaximumStagedBytes)
        {
            if(!Directory.Exists(root))return 0;var pack=new CountryPack(countryFolder);if(!long.TryParse(pack.Version,out long installed))return 0;
            long used=Directory.EnumerateFiles(root,"*.osc.gz",SearchOption.AllDirectories).Take(4097).Sum(p=>new FileInfo(p).Length);if(used<=targetBytes)return 0;
            string directory=Path.Combine(root,pack.Id);if(!Directory.Exists(directory))return 0;var candidates=new List<Tuple<long,string>>();
            foreach(string header in Directory.EnumerateFiles(directory,"*.json").Take(4096))
            {
                try
                {
                    if(new FileInfo(header).Length>DataPackageRules.MaximumHeaderBytes)continue;var item=JsonConvert.DeserializeObject<DataFileManifest>(File.ReadAllText(header));
                    if(item==null||item.pack!=pack.Id||item.kind!="map-diff"||!DataPackageRules.Valid(item)||!long.TryParse(item.toVersion,out long version)||version>installed||item.id!=pack.Id+"-"+version.ToString(CultureInfo.InvariantCulture))continue;
                    candidates.Add(Tuple.Create(version,item.id));
                }
                catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is JsonException){}
            }
            int removed=0;foreach(var candidate in candidates.OrderBy(c=>c.Item1))
            {
                if(used<=targetBytes)break;string path=Path.GetFullPath(Path.Combine(directory,candidate.Item2+".osc.gz"));
                lock(pinLock)
                {
                    if(pins.ContainsKey(path))continue;
                    try
                    {
                        long bytes=File.Exists(path)?new FileInfo(path).Length:0;File.Delete(path);used-=bytes;
                        File.Delete(Path.Combine(directory,candidate.Item2+".json"));File.Delete(Path.Combine(directory,candidate.Item2+".state.txt"));removed++;
                    }
                    catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }
            return removed;
        }
        public static PeerFileSource[] Catalogue(string root,string[] packFolders,DataSigningKey key,CancellationToken token,PeerUpdateInterest[] interests=null)
        {
            var groups=new List<List<PeerFileSource>>();long inspectedBytes=0;
            foreach(string folder in packFolders.Take(8))
            {
                token.ThrowIfCancellationRequested();var pack=new CountryPack(folder);string directory=Path.Combine(root,pack.Id);if(!Directory.Exists(directory))continue;
                var interest=interests==null?new PeerUpdateInterest{pack=pack.Id,version=pack.Version}:interests.FirstOrDefault(x=>x.pack==pack.Id);
                if(!PeerUpdateRules.ValidInterest(interest))continue;long start=long.Parse(interest.version,CultureInfo.InvariantCulture);var chain=new List<PeerFileSource>();
                // Direct canonical lookups give this guest its next contiguous
                // window. Enumeration order and newer unrelated files cannot hide it.
                for(long version=start+1;version<=start+PeerUpdateRules.MaximumOffers&&version<=999999999;version++)
                {
                    token.ThrowIfCancellationRequested();string id=pack.Id+"-"+version.ToString(CultureInfo.InvariantCulture),header=Path.Combine(directory,id+".json"),state=Path.Combine(directory,id+".state.txt"),path=Path.Combine(directory,id+".osc.gz");
                    try
                    {
                        if(!File.Exists(header)||new FileInfo(header).Length>DataPackageRules.MaximumHeaderBytes||!File.Exists(state)||new FileInfo(state).Length>16384)break;
                        var item=JsonConvert.DeserializeObject<DataFileManifest>(File.ReadAllText(header));if(item==null||item.id!=id)break;
                        var date=ReplicationState.Parse(File.ReadAllText(state));if(date.sequence!=version)break;
                        item.format=2;item.timestamp=DateTimeOffset.Parse(date.timestamp,CultureInfo.InvariantCulture).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ",CultureInfo.InvariantCulture);
                        if(!PeerUpdateRules.Matches(item,pack.Id,interest.version,pack.ReplicationSource))break;
                        inspectedBytes+=item.bytes;if(inspectedBytes>PeerUpdateRules.MaximumStagedBytes)break;
                        if(!DataPackageRules.FileMatches(path,item))break;lock(key)key.Sign(item);chain.Add(new PeerFileSource{manifest=item,path=path});
                    }
                    catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is JsonException||e is InvalidDataException||e is FormatException){break;}
                }
                groups.Add(chain);
            }
            // Several installed countries share the same bounded offer budget.
            var result=new List<PeerFileSource>();for(int i=0;i<PeerUpdateRules.MaximumOffers;i++)foreach(var chain in groups)if(i<chain.Count){result.Add(chain[i]);if(result.Count==PeerUpdateRules.MaximumOffers)return result.ToArray();}
            return result.ToArray();
        }
        static void Atomic(string destination,Action<string> write)
        {
            string temporary=destination+"."+Guid.NewGuid().ToString("N")+".partial";
            try{write(temporary);if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public static string Stage(PeerFileResult received,string countryFolder,string root,string fingerprint)
        {
            var pack=new CountryPack(countryFolder);var item=received.manifest;
            if(!PeerUpdateRules.Matches(item,pack.Id,pack.Version,pack.ReplicationSource)||!DataPackageRules.VerifySignature(item,fingerprint)||!DataPackageRules.FileMatches(received.path,item))throw new InvalidDataException("Peer update does not match the installed country and source");
            Directory.CreateDirectory(root);TrimApplied(root,countryFolder,Math.Max(0,PeerUpdateRules.MaximumStagedBytes-item.bytes));long used=0;int count=0;
            foreach(string file in Directory.EnumerateFiles(root,"*.osc.gz",SearchOption.AllDirectories))
            {if(++count>800)throw new InvalidDataException("Staged update cache needs maintenance");used+=new FileInfo(file).Length;if(used>PeerUpdateRules.MaximumStagedBytes)throw new InvalidDataException("Staged update cache is full");}
            string directory=Path.Combine(root,pack.Id);Directory.CreateDirectory(directory);string path=DataPackageRules.PathFor(directory,item);
            long previous=File.Exists(path)?new FileInfo(path).Length:0;if(used-previous+item.bytes>PeerUpdateRules.MaximumStagedBytes)throw new InvalidDataException("Staged update cache is full");
            if(!DataPackageRules.FileMatches(path,item))Atomic(path,p=>File.Copy(received.path,p));
            // Header is the commit marker. Never synthesize state from unsigned metadata.
            Atomic(Path.Combine(directory,item.id+".state.txt"),p=>File.WriteAllText(p,"sequenceNumber="+item.toVersion+"\ntimestamp="+item.timestamp+"\n"));
            Atomic(Path.Combine(directory,item.id+".json"),p=>File.WriteAllText(p,JsonConvert.SerializeObject(item)));
            return path;
        }
    }
}
