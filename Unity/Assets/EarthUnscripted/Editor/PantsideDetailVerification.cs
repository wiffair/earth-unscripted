using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class PantsideDetailVerification
    {
        const string Key="EUPantsideDetail";
        static string root;static int checks;static double deadline;static bool entered;
        static PantsideDetailVerification()
        {
            if(!SessionState.GetBool(Key,false))return;
            root=SessionState.GetString(Key+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
            deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Tick;
        }
        static void Check(bool value,string message){if(!value)throw new Exception("PANTSIDE DETAIL FAIL "+message);checks++;Debug.Log("PANTSIDE DETAIL PASS "+message);}
        public static void Run()
        {
            try
            {
                root=Path.GetFullPath("Builds/Verification/PantsideDetail");Directory.CreateDirectory(root);
                PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
                var densePlace=new WorldPlace{name="Dense import regression",latitude=52.969,longitude=-3.171,country_code="GB",timezone="Europe/London"};
                var dense=Newtonsoft.Json.Linq.JObject.Parse(GlobalRegionVerification.Fixture(densePlace));var elements=(Newtonsoft.Json.Linq.JArray)dense["elements"];var house=elements.First(e=>(string)e["tags"]?["building"]=="house");for(int i=0;i<1100;i++){var clone=(Newtonsoft.Json.Linq.JObject)house.DeepClone();clone["id"]=2000+i;elements.Add(clone);}
                Newtonsoft.Json.Linq.JObject P(float x,float z)=>new Newtonsoft.Json.Linq.JObject{{"lon",densePlace.longitude+x/(111319.4908*Math.Cos(densePlace.latitude*Math.PI/180))},{"lat",densePlace.latitude+z/111319.4908}};
                elements.Add(new Newtonsoft.Json.Linq.JObject{{"id",9000},{"tags",new Newtonsoft.Json.Linq.JObject{{"natural","wood"}}},{"geometry",new Newtonsoft.Json.Linq.JArray(P(-2000,-1000),P(-500,-1000),P(-500,-500),P(-2000,-500),P(-2000,-1000))}});
                var denseRegion=GlobalRegions.Convert(densePlace,dense.ToString(),new float[17*17],17,3000);
                Check(denseRegion.buildings.Length==650&&denseRegion.areas.Length==1&&denseRegion.areas[0].kind=="wood","building limit does not turn skipped houses into grass or hide woodland");
                Check(denseRegion.Land(-900,-750)==2&&denseRegion.areas[0].points.All(p=>Math.Abs(p.x)<=1495.1f&&Math.Abs(p.z)<=1495.1f),"woodland crossing a region edge is clipped and retained as forest cover");
                var original=File.Exists("Builds/Verification/Pantside/region-id.txt")?Path.Combine("Builds/Verification/Pantside/Regions",File.ReadAllText("Builds/Verification/Pantside/region-id.txt")+".json"):Directory.GetFiles("Builds/Verification/Pantside/Regions","download-*.json").First();
                var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(original));int roads=region.roads.Length;
                Check(region.buildings.Count(b=>Centre(b.points).magnitude<200)==0,"baseline data reproduces the local house gap");
                Check(!new PlayerProfile().detailedScenery,"high-detail scenery is optional and defaults off");
                var pack=new DetailBuildingPack("/private/tmp/EarthUnscriptedDetailCompiled/PantsideBuildings");
                var place=new WorldPlace{latitude=region.latitude,longitude=region.longitude,country_code="GB",size=3000};
                Check(pack.Intersects(place),"compiled open-data pack covers Pantside");
                string bad=Path.Combine(root,"corrupt-pack");Directory.CreateDirectory(bad);File.Copy("/private/tmp/EarthUnscriptedDetailCompiled/PantsideBuildings/buildings-pack.json",Path.Combine(bad,"buildings-pack.json"),true);File.WriteAllText(Path.Combine(bad,"buildings.json"),"corrupt");bool rejected=false;try{new DetailBuildingPack(bad);}catch(InvalidDataException){rejected=true;}Check(rejected,"corrupted optional geometry is refused before scene mutation");
                var badMeta=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(bad,"buildings-pack.json")));var badPayload=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("/private/tmp/EarthUnscriptedDetailCompiled/PantsideBuildings/buildings.json"));badPayload["buildings"][0]["height"]=-5;File.WriteAllText(Path.Combine(bad,"buildings.json"),badPayload.ToString());badMeta["bytes"]=new FileInfo(Path.Combine(bad,"buildings.json")).Length;badMeta["sha256"]=EarthUnscripted.Core.DataPackageRules.HashFile(Path.Combine(bad,"buildings.json"));File.WriteAllText(Path.Combine(bad,"buildings-pack.json"),badMeta.ToString());int before=region.buildings.Length;Check(!new DetailBuildingPack(bad).ApplySafely(region)&&region.buildings.Length==before,"hash-consistent invalid detail falls back to unchanged standard buildings");
                int added=pack.Apply(region);Check(added>=400,"imports complete bounded OS outlines with relative LiDAR heights");
                Check(region.buildings.Count(b=>Centre(b.points).magnitude<200)>100,"houses now surround the postcode start");
                int buildings=region.buildings.Length;pack.Apply(region);Check(region.buildings.Length==buildings,"reapplying the same pack does not duplicate buildings");
                Check(region.roads.Length==roads&&region.heights.Length==65*65,"building enrichment preserves authoritative road and terrain data");
                string id=DetailBuildingPacks.CacheId(region.id,new[]{pack});Check(RegionCache.Safe(id)&&id!=region.id,"detailed and standard cache identities stay separate");
                region.id=id;RegionCache.Save(region,"Independent OS OpenMap Local / Welsh Government LiDAR compiled scenery proof");
                var loaded=GeoRegion.Load(id);Check(loaded.buildings.Length==buildings&&loaded.attribution.Contains("Ordnance Survey"),"cached detail preserves geometry and source attribution");
                int renderable=loaded.buildings.Count(b=>Centre(b.points).magnitude<250&&!loaded.FootprintOverlapsRoad(b.points));
                Check(renderable>120,"road-clearance filtering retains substantial local housing");
                string epoch=WorldSnapshotCodec.Epoch(loaded);var imported=WorldSnapshotCodec.Decode(WorldSnapshotCodec.CanonicalJson(loaded),WorldSnapshotCodec.NetworkId(loaded),epoch);
                Check(WorldSnapshotCodec.Epoch(imported)==epoch,"enriched geometry survives the LAN canonical snapshot codec");
                File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(new PlayerProfile{region=id,credits=4321,quality=1,volume=0,rumble=0,livingScenery=false,detailedScenery=true,customStart=true,startRegion=id,startX=0,startZ=0}));
                ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);SessionState.SetInt(Key+"Checks",checks);EditorApplication.EnterPlaymode();
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static Vector2 Centre(Vector3[] pp){var p=Vector2.zero;for(int i=0;i<pp.Length-1;i++)p+=new Vector2(pp[i].x,pp[i].z);return p/Math.Max(1,pp.Length-1);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Pantside detail gameplay scene timed out");
                if(!EditorApplication.isPlaying)return;
                if(entered)return;var session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||session.World?.GeographicWorld==null)return;entered=true;checks=SessionState.GetInt(Key+"Checks",0);
                session.SetMenu(true);var region=session.World.Route.Region;session.World.GeographicWorld.Refresh(Vector3.zero,true);
                var visible=session.World.GeographicWorld.GetComponentsInChildren<MeshFilter>().Where(m=>m.name.StartsWith("Building os-",StringComparison.Ordinal)).ToArray();
                Check(visible.Length>200,"actual gameplay chunks create more than 200 OS building meshes");
                Check(visible.All(m=>m.sharedMesh&&m.sharedMesh.vertexCount>0),"actual building meshes contain renderable walls");
                Check(session.Economy.Credits==4321&&session.Profile.detailedScenery,"isolated scene retains career funds and optional detail setting");
                File.WriteAllText(Path.Combine(root,"result.txt"),$"PASS {checks} checks\nsceneOSBuildings={visible.Length}\nregionBuildings={region.buildings.Length}\nprofile={PlayerProfile.StorageOverride}\n");
                SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(0);
            }
            catch(Exception e){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
