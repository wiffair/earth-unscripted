using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    public sealed class DetailInventoryDiscoveryRequest
    {
        public readonly IReadOnlyList<string> SearchPaths;
        public readonly string ConfigPath;
        public DetailInventoryDiscoveryRequest(IEnumerable<string> searchPaths,string configPath)
        {SearchPaths=new ReadOnlyCollection<string>((searchPaths??Array.Empty<string>()).Take(DetailInventoryDiscovery.MaximumSearchPaths+1).ToArray());ConfigPath=configPath;}
    }
    public static class DetailInventoryDiscovery
    {
        public const int MaximumSearchPaths=16,MaximumCountryDirectoriesPerRoot=64,MaximumConfigBytes=16384,MaximumCountryManifestBytes=256000;
        internal sealed class Found
        {public DetailInventoryRoot[] Roots=Array.Empty<DetailInventoryRoot>();public readonly List<string> Issues=new List<string>();public bool Complete=true;}
        // Call on Unity's main thread. Captures strings only: no directory scan,
        // config read, pack.json read, country validator, SQLite or payload hash.
        public static DetailInventoryDiscoveryRequest CaptureSearchPaths()
        {
            string data=Application.dataPath,persistent=RoadRush.SinglePlayerBeta.ProductRoot;
            var paths=new List<string>();if(!string.IsNullOrEmpty(CountryPacks.ExtraRoot))paths.Add(CountryPacks.ExtraRoot);
            paths.Add(Path.Combine(persistent,"WorldPacks"));paths.Add(Path.Combine(Path.GetDirectoryName(data),"WorldPacks"));
            if(Application.platform==RuntimePlatform.OSXPlayer)paths.Add(Path.GetFullPath(Path.Combine(data,"../..","WorldPacks")));
            return new DetailInventoryDiscoveryRequest(paths,Path.Combine(RegionCache.Folder,"world-packs.txt"));
        }
        static string Clean(string s)=>new string((s??"").Take(220).Select(c=>char.IsControl(c)?' ':c).ToArray());
        static bool Present(string path)
        {
            try{return (File.GetAttributes(path)&FileAttributes.Directory)==0;}
            catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
        }
        static byte[] Read(string path,int maximum,CancellationToken token)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length<0||stream.Length>maximum)throw new InvalidDataException("Metadata exceeds inspection size limit");
                var bytes=new byte[(int)stream.Length];int at=0;
                while(at<bytes.Length){token.ThrowIfCancellationRequested();int n=stream.Read(bytes,at,Math.Min(32768,bytes.Length-at));if(n==0)throw new EndOfStreamException("Metadata changed while reading");at+=n;}
                if(stream.ReadByte()!=-1)throw new InvalidDataException("Metadata changed while reading");return bytes;
            }
        }
        internal static Found Discover(DetailInventoryDiscoveryRequest request,CancellationToken token)
        {
            var result=new Found();var paths=request.SearchPaths.ToList();var roots=new List<DetailInventoryRoot>();
            try
            {
                token.ThrowIfCancellationRequested();
                if(!string.IsNullOrEmpty(request.ConfigPath)&&File.Exists(request.ConfigPath))
                {
                    string text;using(var contents=new StreamReader(new MemoryStream(Read(request.ConfigPath,MaximumConfigBytes,token)),Encoding.UTF8,true))text=contents.ReadToEnd();
                    using(var reader=new StringReader(text)){string line;while((line=reader.ReadLine())!=null){token.ThrowIfCancellationRequested();if(!string.IsNullOrWhiteSpace(line))paths.Add(line);if(paths.Count>MaximumSearchPaths)break;}}
                }
                if(paths.Count>MaximumSearchPaths){result.Complete=false;result.Issues.Add("Country search-root limit reached; no partial discovery reported");return result;}
                foreach(string raw in paths.Where(p=>!string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).OrderBy(p=>p,StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();if(!Directory.Exists(raw))continue;string[] folders;
                    try
                    {
                        folders=Directory.EnumerateDirectories(raw).Take(MaximumCountryDirectoriesPerRoot+1).ToArray();
                        if(folders.Length>MaximumCountryDirectoriesPerRoot){result.Complete=false;result.Issues.Add("Country directory scan limit reached; root was not partially inspected");continue;}
                        Array.Sort(folders,StringComparer.Ordinal);
                    }
                    catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){result.Complete=false;result.Issues.Add(Clean(e.Message));continue;}
                    foreach(string folder in folders)
                    {
                        token.ThrowIfCancellationRequested();string manifest=Path.Combine(folder,"pack.json");
                        try
                        {
                            if(!Present(manifest))continue;
                            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(manifest)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked country metadata was not inspected");
                            var bytes=Read(manifest,MaximumCountryManifestBytes,token);JObject meta;
                            using(var text=new StreamReader(new MemoryStream(bytes),Encoding.UTF8,true))
                            using(var reader=new JsonTextReader(text){MaxDepth=64})meta=JObject.Load(reader);
                            string country=(string)meta["country"];if(country==null||!Regex.IsMatch(country,"^[A-Z]{2}$"))throw new InvalidDataException("Country metadata has no supported two-letter code");
                            // These are metadata/existence observations only; an explicit
                            // Prepare action must construct CountryPack and check Covers.
                            bool ready=(bool?)meta["mapReady"]==true&&(bool?)meta["terrainReady"]==true;
                            roots.Add(new DetailInventoryRoot(country,Path.Combine(folder,"Detail"),ready,File.Exists(Path.Combine(folder,"map.sqlite"))));
                            if(roots.Count>DetailPackInventory.MaximumRoots){result.Complete=false;result.Issues.Add("Installed country-root limit reached; no partial discovery reported");result.Roots=Array.Empty<DetailInventoryRoot>();return result;}
                        }
                        catch(OperationCanceledException){throw;}
                        catch(Exception e){result.Complete=false;result.Issues.Add("Country metadata could not be inspected: "+Clean(e.Message));}
                    }
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception e){result.Complete=false;result.Issues.Add("Country discovery incomplete: "+Clean(e.Message));}
            result.Roots=roots.ToArray();return result;
        }
    }
}
