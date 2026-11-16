using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    [InitializeOnLoad] public static class StartAreaVerification
    {
        const string Key="RRStartArea";static string root;static int phase,checks,frames;static double deadline;static GameSession previous,current;static int requests;static bool offline;
        static StartAreaVerification()
        {
            if(!SessionState.GetBool(Key,false))return;root=SessionState.GetString(Key+"Root","");
            PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");CountryPacks.ExtraRoot=Path.Combine(root,"Packs");CountryUpdateService.OverrideFolder=Path.Combine(root,"Updates");CountryUpdateService.AllowVerification=true;
            CountryUpdateService.FetchOverride=(url,path,max,token)=>Task.Run(()=>{token.ThrowIfCancellationRequested();if(offline)throw new IOException("Fixture network unavailable");File.WriteAllText(path,"sequenceNumber=2\ntimestamp=2026-11-05T00\\:00\\:00Z\n");},token);
            deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");root=Path.GetFullPath("Builds/Verification/start-area-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
            PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
            var place=new WorldPlace{name="Start fixture",latitude=52.1,longitude=-3.1,country_code="GB",timezone="Europe/London",drivingSide=-1,size=3000};
            string source=GlobalRegionVerification.Fixture(place),folder=Path.Combine(root,"Packs/Fixture");Directory.CreateDirectory(folder);string database=Path.Combine(folder,"map.sqlite");File.WriteAllBytes(database,Array.Empty<byte>());
            using(var db=new NativeMapDatabase(database,true))
            {
                foreach(var sql in new[]{"CREATE TABLE features(id INTEGER PRIMARY KEY,kind INTEGER,tags TEXT,geometry BLOB)","CREATE VIRTUAL TABLE bounds USING rtree(id,minLon,maxLon,minLat,maxLat)","CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT)","INSERT INTO metadata VALUES('osmosis_replication_sequence_number','2')"})db.Execute(sql);
                foreach(var item in JObject.Parse(source)["elements"].Where(e=>e["tags"]?["highway"]!=null))
                {
                    var geometry=(JArray)item["geometry"];using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
                    {
                        writer.Write(geometry.Count);for(int i=0;i<geometry.Count;i++){writer.Write((long)item["nodes"][i]);writer.Write((int)Math.Round((double)geometry[i]["lat"]*1e7));writer.Write((int)Math.Round((double)geometry[i]["lon"]*1e7));}
                        using(var put=db.Prepare("INSERT INTO features VALUES(?1,0,?2,?3)")){put.Number(1,(long)item["id"]).Text(2,item["tags"].ToString()).Bytes(3,stream.ToArray());put.Step();}
                    }
                    using(var put=db.Prepare("INSERT INTO bounds VALUES(?1,?2,?3,?4,?5)")){put.Number(1,(long)item["id"]).Number(2,geometry.Min(g=>(double)g["lon"])).Number(3,geometry.Max(g=>(double)g["lon"])).Number(4,geometry.Min(g=>(double)g["lat"])).Number(5,geometry.Max(g=>(double)g["lat"]));put.Step();}
                }
            }
            File.WriteAllText(Path.Combine(folder,"pack.json"),new JObject{{"name","StartFixture"},{"country","GB"},{"version","2"},{"mapReady",true},{"terrainReady",true},{"completeNodes",true},{"bounds",new JArray(-3.2,52,-3,52.2)},{"tiles",new JArray()},{"replication",new JObject{{"osmosis_replication_base_url","https://download.geofabrik.de/europe/united-kingdom/wales-updates"}}}}.ToString());
            var region=GlobalRegions.Convert(place,source,new float[17*17],17,3000);region.datasetVersion="StartFixture/1";region.datasetSource="https://download.geofabrik.de/europe/united-kingdom/wales-updates";region.id=RegionCache.Id(place)+"-p1";RegionCache.Save(region,source);
            var profile=new PlayerProfile{region=region.id,credits=4321,quality=0,volume=.23f,rumble=0,livingScenery=false};profile.garage=RoadRush.Core.GarageRules.CreateDefault();profile.garage.Active.engine=2;profile.garage.Active.damage=27;profile.Save();
            SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string text){if(!value)throw new Exception("START FAIL "+text);checks++;Debug.Log("START PASS "+text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Start-area test timeout phase "+phase);if(!EditorApplication.isPlaying)return;
                current=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(current)||LocationLoading.Busy)return;current.Updates.Automatic=false;
                if(phase==0){previous=current;Check(current.Menu&&!current.Started,"career opens paused before first-drive preflight");current.ResumeDrive();phase=1;return;}
                if(phase==1)
                {
                    if(ReferenceEquals(current,previous)||!current.Started)return;
                    Check(current.World.Route.Region.datasetVersion=="StartFixture/2","first drive prepares and activates newer offline area before driving");
                    Check(!current.Menu&&current.Profile.region==current.World.Route.Region.id,"refreshed area resumes automatically after scene reload");
                    Check(current.Economy.Credits==4321&&current.Profile.garage.Active.engine==2&&Math.Abs(current.Car.Damage-27)<.01f&&Math.Abs(current.Profile.volume-.23f)<.001f,"credits, upgrades, damage and settings survive area refresh");
                    Check(Directory.GetFiles(RegionCache.Folder,"*-p1.json").Length==1,"previous career area remains available for rollback");
                    requests=current.Updates.SourceRequests;Check(requests==1,"fresh source state is checked once before driving");
                    previous=current;current.SetMenu(true);current.ResumeDrive();phase=2;return;
                }
                if(phase==2)
                {
                    if(current.PreparingStart)return;if(++frames<10)return;
                    Check(ReferenceEquals(current,previous)&&current.Updates.SourceRequests==requests&&!current.Menu,"resuming an active drive pins the map and performs no new source request");
                    current.SetMenu(true);offline=true;UnityEngine.Object.DestroyImmediate(current.Updates.gameObject);current.SelectRegion(current.Profile.region);phase=3;return;
                }
                if(phase==3)
                {
                    if(ReferenceEquals(current,previous))return;previous=current;current.ResumeDrive();phase=4;return;
                }
                if(phase==4)
                {
                    if(!current.Started)return;
                    Check(ReferenceEquals(current,previous)&&!current.Menu&&current.World.Route.Region.datasetVersion=="StartFixture/2","unavailable network starts directly from the saved map without reloading");
                    Check(current.Updates.SourceRequests==1,"offline start makes one bounded source attempt");
                    string oldStorage=PlayerProfile.StorageOverride,oldRegion=current.Profile.region,oldStart=current.Profile.startRegion;bool oldCustom=current.Profile.customStart;float oldX=current.Profile.startX,oldZ=current.Profile.startZ;
                    try{PlayerProfile.StorageOverride=root;current.StartAt("dee-valley",Vector3.zero);}finally{PlayerProfile.StorageOverride=oldStorage;}
                    Check(!LocationLoading.Busy&&current.Profile.region==oldRegion&&current.Profile.startRegion==oldStart&&current.Profile.customStart==oldCustom&&current.Profile.startX==oldX&&current.Profile.startZ==oldZ,"failed location save retains the active area and prior selected start");
                    Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/start-area.txt","PASS "+checks+" start/resume/version/career checks\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);CountryUpdateService.FetchOverride=null;CountryUpdateService.AllowVerification=false;EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
