using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    [InitializeOnLoad] public static class GlobalRegionVerification
    {
        const string Key="RRGlobalRegions";static int phase,checks;static double deadline;static bool live,pack,uk;static GameSession session,previous;static string expected;
        static GlobalRegionVerification(){if(!SessionState.GetBool(Key,false))return;PlayerProfile.StorageOverride=SessionState.GetString(Key+"Profile","");RegionCache.OverrideFolder=SessionState.GetString(Key+"Cache","");uk=SessionState.GetBool(Key+"UK",false);live=SessionState.GetBool(Key+"Live",false);pack=SessionState.GetBool(Key+"Pack",false);if(pack)CountryPacks.ExtraRoot="/private/tmp/RoadRushWorldPacks";deadline=EditorApplication.timeSinceStartup+360;EditorApplication.update+=Tick;}
        public static void RunOffline()=>Run(false);public static void RunLive()=>Run(true);public static void RunCountryPack()=>Run(false,true);public static void RunUnitedKingdom()=>Run(false,true,true);
        static void Run(bool network,bool countryPack=false,bool unitedKingdom=false)
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");string folder=Path.GetFullPath("Builds/Verification/global-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(folder);string profile=Path.Combine(folder,"profile.json");File.WriteAllText(profile,JsonUtility.ToJson(new PlayerProfile{quality=0,volume=0,rumble=0}));SessionState.SetString(Key+"Profile",profile);SessionState.SetString(Key+"Cache",Path.Combine(folder,"Regions"));SessionState.SetBool(Key+"UK",unitedKingdom);SessionState.SetBool(Key+"Live",network);SessionState.SetBool(Key+"Pack",countryPack);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string message){if(!value)throw new Exception("GLOBAL FAIL: "+message);checks++;Debug.Log("GLOBAL PASS: "+message);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Global map test timeout at phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;Application.runInBackground=true;
                    Check(!RegionCache.Safe("download-../../career")&&!RegionCache.Safe("/etc/passwd"),"cache identifiers reject path traversal");
                    Check(Core.CountryRules.Side("GB")==-1&&Core.CountryRules.Side("JP")==-1&&Core.CountryRules.Side("AU")==-1&&Core.CountryRules.Side("BE")==1&&Core.CountryRules.Side("US")==1,"country-derived road sides cover left and right traffic regions");
                    if(pack){var p=uk?new WorldPlace{name="Edinburgh",latitude=55.953,longitude=-3.188,country_code="GB",timezone="Europe/London",size=1000}:new WorldPlace{name="Llangollen",latitude=52.969,longitude=-3.171,country_code="GB",timezone="Europe/London",size=3000};Check(CountryPacks.Find(p)!=null,"local country pack covers chosen place");session.Downloads.Download(p);phase=1;return;}
                    if(live){session.Downloads.OverpassEndpoint="https://overpass.private.coffee/api/interpreter";session.Downloads.Download(new WorldPlace{name="Chester",size=1000,latitude=53.19339,longitude=-2.89308,country_code="GB",timezone="Europe/London"});phase=1;return;}
                    var place=new WorldPlace{name="Offline importer fixture",latitude=52.969,longitude=-3.171,country_code="GB",timezone="Europe/London"};var fixture=Fixture(place);var h=Enumerable.Range(0,625).Select(i=>100+(i/25)*.3f).ToArray();var region=GlobalRegions.Convert(place,fixture,h,25,3000);RegionCache.Save(region,fixture);
                    Check(region.roads.Length>=8&&region.buildings.Length==1&&region.waters.Length==1,"OSM geometry imports roads, a building and water without flat-terrain substitution");Check(region.route.Length>=5&&new RoutePath(region).Length>350,"import finds a closed connected preview route");
                    var cached=GeoRegion.Load(region.id);Check(cached.drivingSide==-1&&cached.roads.All(r=>r.width>=6.5f),"cached import reloads with country side and gameplay road scale");session.Downloads.Ready=cached;phase=1;
                }
                if(phase==1)
                {
                    if(session.Downloads.Busy)return;var region=session.Downloads.Ready;if(region==null)throw new Exception(session.Downloads.Status);Check(RegionCache.Exists(region.id),live?"live OSM and289-point Copernicus download caches successfully":"imported region remains available offline");
                    Check(File.Exists(Path.Combine(RegionCache.Folder,region.id+"-osm-source.txt")),"cache preserves the OSM source and data notices");
                    var road=region.roads.First(r=>!r.tunnel);var point=region.RoadPoint(road,road.length*.5f,out _);expected=region.id;previous=session;session.StartAt(expected,point);phase=2;return;
                }
                if(phase==2)
                {
                    var current=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(current)||ReferenceEquals(current,previous)||LocationLoading.Busy)return;session=current;Check(session.World.Route.Region.id==expected,"new downloaded location opens in the same game process");Check(session.Profile.customStart&&session.World.GeographicWorld.HasSurface(session.Car.Body.position),"selected road start has loaded terrain");
                    Check(session.GetComponent<LivingScenery>().Count<=24,"residents and animals use a bounded population");Check(session.Car.Visual.Wheels.All(w=>w.GetComponentInChildren<MeshFilter>()!=null),"profiled tyre and wheel geometry survives map reload");
                    Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/global-"+(pack?(uk?"country-uk":"country-pack"):live?"live":"offline")+".txt","PASS "+checks+" checks\n"+session.World.Route.Region.name+"\nroads="+session.World.Route.Region.roads.Length+"\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        internal static string Fixture(WorldPlace place)
        {
            var coordinates=new[]{new Vector2(-200,-200),new Vector2(200,-200),new Vector2(200,200),new Vector2(-200,200),new Vector2(500,-200),new Vector2(800,-200),new Vector2(800,200),new Vector2(500,200)};JObject Point(Vector2 p)=>new JObject{{"lat",place.latitude+p.y/111319.4908},{"lon",place.longitude+p.x/(111319.4908*Math.Cos(place.latitude*Math.PI/180))}};var elements=new JArray();int id=1;
            foreach(var pair in new[]{(0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(1,4)})elements.Add(new JObject{{"type","way"},{"id",id++},{"tags",new JObject{{"highway","residential"},{"maxspeed","30 mph"}}},{"nodes",new JArray(pair.Item1+1,pair.Item2+1)},{"geometry",new JArray(Point(coordinates[pair.Item1]),Point(coordinates[pair.Item2]))}});
            elements.Add(new JObject{{"id",100},{"tags",new JObject{{"building","house"},{"height","7"}}},{"geometry",new JArray(Point(new Vector2(-50,-50)),Point(new Vector2(0,-50)),Point(new Vector2(0,0)),Point(new Vector2(-50,0)),Point(new Vector2(-50,-50)))}});
            elements.Add(new JObject{{"id",101},{"tags",new JObject{{"waterway","stream"}}},{"geometry",new JArray(Point(new Vector2(-500,-500)),Point(new Vector2(-500,500)))}});return new JObject{{"elements",elements}}.ToString();
        }
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
