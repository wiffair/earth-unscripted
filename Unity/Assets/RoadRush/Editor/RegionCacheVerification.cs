using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class RegionCacheVerification
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Debug.Log("CACHE PASS "+message);}
        public static void Run()
        {
            string oldFolder=RegionCache.OverrideFolder,oldProfile=PlayerProfile.StorageOverride;
            try
            {
                string root=Path.Combine(Path.GetTempPath(),"rr-cache-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
                RegionCache.OverrideFolder=Path.Combine(root,"Regions");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");Directory.CreateDirectory(RegionCache.Folder);Directory.CreateDirectory(CareerStorage.Folder);
                Action<string,int> add=(id,age)=>{File.WriteAllText(RegionCache.PathFor(id),"{}");File.SetLastWriteTimeUtc(RegionCache.PathFor(id),DateTime.UtcNow.AddDays(-age));File.WriteAllText(Path.Combine(RegionCache.Folder,id+"-osm-source.txt"),new string('x',1024));};
                add("download-current-p10",10);add("download-current-p11",9);add("download-current-p12",8);
                add("download-slot",20);add("download-backup",20);add("download-start",20);add("download-slotbackup",20);add("download-new",0);
                for(int i=0;i<20;i++)add("download-disposable-"+i,30+i);
                File.WriteAllText(PlayerProfile.StoragePath,"{\"region\":\"download-current-p12\",\"startRegion\":\"download-start\"}");
                File.WriteAllText(PlayerProfile.StoragePath+".bak","{\"region\":\"download-backup\"}");
                File.WriteAllText(CareerStorage.PathFor(0),"{\"region\":\"download-slot\"}");File.WriteAllText(CareerStorage.PathFor(0)+".bak","{\"region\":\"download-slotbackup\"}");
                File.WriteAllText(Path.Combine(RegionCache.Folder,"world-packs.txt"),"never delete");
                var place=new WorldPlace{latitude=52.10001,longitude=-3.10001,size=3000,country_code="GB",timezone="Europe/London"};string origin=RegionCache.Id(place);place.latitude+=.00001;
                Check(origin!=RegionCache.Id(place)&&RegionCache.Safe(RegionCache.Id(place)),"nearby geographic origins no longer collide in a 100m cache bucket");
                string other=RegionCache.Id(place);place.drivingSide=1;Check(other!=RegionCache.Id(place),"driving-side overrides have distinct cached snapshots");
                string testFolder=RegionCache.OverrideFolder;RegionCache.OverrideFolder=null;
                Check(RegionCache.Folder==PlayerProfile.StorageOverride+".regions","isolated game profiles also isolate the region cache automatically");RegionCache.OverrideFolder=testFolder;
                int removed=RegionCacheMaintenance.Trim("download-new",7,10000000);
                Check(removed==21&&RegionCache.All().Length==7,"bounded cleanup removes oldest unreferenced areas");
                Check(RegionCache.Exists("download-current-p12")&&RegionCache.Exists("download-current-p11"),"current career and previous map snapshot survive");
                Check(!RegionCache.Exists("download-current-p10"),"obsolete unreferenced version can be reclaimed");
                Check(new[]{"download-slot","download-backup","download-start","download-slotbackup","download-new"}.All(RegionCache.Exists),"manual slots, backups, custom start and incoming area survive");
                Check(!File.Exists(Path.Combine(RegionCache.Folder,"download-disposable-19-osm-source.txt")),"evicted area's source companion is removed");
                Check(File.ReadAllText(Path.Combine(RegionCache.Folder,"world-packs.txt"))=="never delete","country-pack pointer remains untouched");
                Check(RegionCacheMaintenance.Trim("download-new",1,1)==0&&RegionCache.All().Length==7,"protected careers can exceed the disposable-cache budget");
                File.WriteAllText(CareerStorage.PathFor(1),"broken career");add("download-unchecked",50);
                Check(RegionCacheMaintenance.Trim("download-new",1,1)==0&&RegionCache.Exists("download-unchecked"),"unreadable career defers eviction safely");File.Delete(CareerStorage.PathFor(1));
                for(int i=0;i<18;i++)RegionCache.Save(new GeoRegion{id="download-extra-"+i},"source");
                Check(RegionCache.All().Length>16,"preparing a seventeenth area no longer fails");
                add("download-big",100);File.WriteAllText(Path.Combine(RegionCache.Folder,"download-big-osm-source.txt"),new string('x',20000));
                RegionCacheMaintenance.Trim("download-new",100,12000);
                Check(!RegionCache.Exists("download-big"),"storage budget accounts for source payloads");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/region-cache.txt","PASS "+checks+" disposable cache/career protection checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{RegionCache.OverrideFolder=oldFolder;PlayerProfile.StorageOverride=oldProfile;}
        }
    }
}
