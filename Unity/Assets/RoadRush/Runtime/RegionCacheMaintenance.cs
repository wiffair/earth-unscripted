using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    // Only disposable, generated area files are eligible. Country databases,
    // careers and provider settings are never part of this budget.
    public static class RegionCacheMaintenance
    {
        public const int MaximumAreas=64;
        public const long MaximumBytes=512L*1024*1024;
        public static string Status {get;private set;}="";
        static string Family(string id)=>Regex.Replace(id,@"-p[0-9]+$","");
        static IEnumerable<string> Files(string id)
        {
            var path=RegionCache.PathFor(id);
            return new[]{path,path+".bak",RegionCache.SourcePath(id)};
        }
        static long Bytes(string id)=>Files(id).Where(File.Exists).Sum(p=>new FileInfo(p).Length);
        static void ProtectProfile(string path,HashSet<string> pinned)
        {
            foreach(var file in new[]{path,path+".bak"})
            {
                if(!File.Exists(file))continue;
                if(new FileInfo(file).Length>4000000)throw new InvalidDataException("Career is too large to check safely");
                var profile=JObject.Parse(File.ReadAllText(file));
                foreach(var key in new[]{"region","startRegion"})
                {string id=(string)profile[key];if(RegionCache.Safe(id))pinned.Add(id);}
            }
        }
        public static int Trim(string incoming,int maximumAreas=MaximumAreas,long maximumBytes=MaximumBytes)
        {
            try
            {
                var ids=RegionCache.All();var pinned=new HashSet<string>(StringComparer.Ordinal);
                if(RegionCache.Safe(incoming))pinned.Add(incoming);
                ProtectProfile(PlayerProfile.StoragePath,pinned);
                for(int slot=0;slot<5;slot++)ProtectProfile(CareerStorage.PathFor(slot),pinned);
                foreach(var session in UnityEngine.Object.FindObjectsByType<GameSession>(FindObjectsSortMode.None))
                {
                    string id=session.World?.Route?.Region?.id;if(RegionCache.Safe(id))pinned.Add(id);
                    if(RegionCache.Safe(session.Profile?.region))pinned.Add(session.Profile.region);
                    if(RegionCache.Safe(session.Profile?.startRegion))pinned.Add(session.Profile.startRegion);
                    if(session.Downloads?.Ready!=null&&RegionCache.Safe(session.Downloads.Ready.id))pinned.Add(session.Downloads.Ready.id);
                }
                // Retain a previous area snapshot beside every referenced area.
                foreach(var family in pinned.Select(Family).Distinct().ToArray())
                    foreach(var id in ids.Where(id=>Family(id)==family).OrderByDescending(id=>File.GetLastWriteTimeUtc(RegionCache.PathFor(id))).Take(2))pinned.Add(id);
                long bytes=ids.Sum(Bytes);int count=ids.Length,removed=0;
                foreach(var id in ids.Where(id=>!pinned.Contains(id)).OrderBy(id=>File.GetLastWriteTimeUtc(RegionCache.PathFor(id))))
                {
                    if(count<=maximumAreas&&bytes<=maximumBytes)break;
                    long size=Bytes(id);foreach(var file in Files(id))if(File.Exists(file))File.Delete(file);
                    count--;bytes-=size;removed++;
                }
                Status=count+" cached areas · "+(bytes/1048576)+" MB"+(count>maximumAreas||bytes>maximumBytes?" · saved/active areas protected":"");return removed;
            }
            catch(Exception e)
            {
                // An unreadable career must never make its map eligible for eviction.
                Status="Cache cleanup deferred · "+e.Message;return 0;
            }
        }
    }
}
