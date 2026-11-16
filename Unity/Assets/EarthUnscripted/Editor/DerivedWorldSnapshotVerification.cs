using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    // Reconstruct actual gameplay worlds, including generated garage/pit roads.
    // Raw GeoRegion roundtrips alone cannot detect a second generated approach.
    [InitializeOnLoad] public static class DerivedWorldSnapshotVerification
    {
        const string Key="EUDerivedWorldSnapshot";
        static readonly string[] Worlds={"dee-valley","silverstone","spa"};
        static string root,expectedEpoch,alias;static int phase,index,checks,expectedRoads;
        static Vector3 expectedPad;static GameSession current,previous;static double deadline;
        static DerivedWorldSnapshotVerification()
        {
            if(!SessionState.GetBool(Key,false))return;
            root=SessionState.GetString(Key+"Root","");
            PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
            deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");
            root=Path.GetFullPath("Builds/Verification/derived-world-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
            var profile=new PlayerProfile{region=Worlds[0],credits=4321,quality=0,volume=.23f,rumble=0,livingScenery=false,wheelName="Derived-world regression wheel",wheelStrength=.31f,steerPath="/Gamepad/leftStick/x",steerLeft=-.81f,steerCentre=.04f,steerRight=.84f};
            var owned=profile.garage.Active;owned.id="main-regression-car";profile.garage.activeId=owned.id;owned.model=1;owned.engine=2;owned.paint=4;owned.damage=27;
            profile.garage.cars.Add(new OwnedCar{id="spare-regression-car",model=2,paint=3,engine=1});
            File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(profile));
            SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);GameSession.AutoStartCircuit=false;EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string message)
        {if(!value)throw new Exception("DERIVED SNAPSHOT FAIL: "+message);checks++;Debug.Log("DERIVED SNAPSHOT PASS: "+message);}
        static bool NewSession()
        {
            var found=UnityEngine.Object.FindAnyObjectByType<GameSession>();
            if(!EditorBootReadiness.Ready(found)||ReferenceEquals(found,previous)||LocationLoading.Busy)return false;
            current=found;current.SetMenu(true);
            Check(UnityEngine.Object.FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length==1,"transition leaves exactly one gameplay session");return true;
        }
        static void Career(string world)
        {
            var p=current.Profile;var active=p.garage.Active;
            Check(current.Economy.Credits==4321&&p.garage.cars.Count==2&&active.id=="main-regression-car"&&active.model==1&&active.engine==2&&active.paint==4&&Math.Abs(active.damage-27)<.001f&&Math.Abs(current.Car.Damage-27)<.001f&&p.garage.cars.Exists(c=>c.id=="spare-regression-car"&&c.model==2&&c.engine==1),world+" preserves credits, both owned cars, upgrades, styling and damage");
            Check(Math.Abs(p.volume-.23f)<.0001f&&p.rumble==0&&p.quality==0&&!p.livingScenery&&p.wheelName=="Derived-world regression wheel"&&Math.Abs(p.wheelStrength-.31f)<.0001f&&p.steerPath=="/Gamepad/leftStick/x"&&Math.Abs(p.steerLeft+.81f)<.0001f&&Math.Abs(p.steerCentre-.04f)<.0001f&&Math.Abs(p.steerRight-.84f)<.0001f,world+" preserves audio, graphics and steering calibration settings");
        }
        static void RoadCounts(GeoRegion region,string label)
        {
            Check(Array.FindAll(region.roads,r=>r.id=="fictional-service-approach").Length==1,label+" has exactly one generated garage approach");
            Check(Array.FindAll(region.roads,r=>r.id=="fictional-pit-lane").Length==(region.circuit?1:0),label+" has the expected single pit lane or none");
        }
        static void ImportAndSwitch()
        {
            var region=current.World.Route.Region;Check(region!=null&&region.id==Worlds[index],"built source world is "+Worlds[index]);RoadCounts(region,Worlds[index]+" source");Career(Worlds[index]+" source");
            expectedEpoch=WorldSnapshotCodec.Epoch(region);expectedRoads=region.roads.Length;expectedPad=region.ServicePad;
            string json=WorldSnapshotCodec.CanonicalJson(region),path=Path.Combine(root,Worlds[index]+"-built-world.json");File.WriteAllText(path,json,new UTF8Encoding(false));
            using(var key=new DataSigningKey())
            {
                var manifest=new DataFileManifest{id="built-"+Worlds[index],kind="region-snapshot",pack="earthunscripted-worlds",fromVersion=WorldSnapshotCodec.NetworkId(region),toVersion=expectedEpoch,source="Built gameplay world regression",licence="ODbL-1.0; Copernicus DEM",bytes=new FileInfo(path).Length,sha256=DataPackageRules.HashFile(path)};key.Sign(manifest);
                var imported=WorldSnapshotCodec.Import(path,manifest,key.Fingerprint);alias=imported.id;
            }
            Check(RegionCache.Safe(alias)&&alias!=Worlds[index],"signed built world is stored under an isolated local alias");
            previous=current;GameSession.AutoStartCircuit=false;Check(current.SelectRegion(alias),"shared alias enters the real scene transition");phase=1;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Derived-world reconstruction timed out at phase "+phase+" world "+index);
                if(!EditorApplication.isPlaying)return;
                if(phase==0||phase==2)
                {if(!NewSession())return;Application.runInBackground=true;ImportAndSwitch();return;}
                if(phase==1)
                {
                    if(!NewSession())return;var rebuilt=current.World.Route.Region;
                    Check(rebuilt!=null&&rebuilt.id==alias&&WorldSnapshotCodec.NetworkId(rebuilt)==Worlds[index],"rebuilt scene uses the alias and retains canonical host identity");
                    Check(WorldEpoch.Matches(WorldSnapshotCodec.Epoch(rebuilt),expectedEpoch),Worlds[index]+" complete gameplay epoch is unchanged after rebuilding");
                    Check(rebuilt.roads.Length==expectedRoads,Worlds[index]+" reconstruction never appends another derived road");RoadCounts(rebuilt,Worlds[index]+" rebuilt");
                    Check(rebuilt.HasServicePad&&Vector3.Distance(rebuilt.ServicePad,expectedPad)<.001f,Worlds[index]+" garage support pad is reconstructed at the same position");Career(Worlds[index]+" rebuilt");
                    index++;if(index==Worlds.Length){File.WriteAllText("Builds/Verification/derived-world-snapshot.txt","PASS "+checks+" actual gameplay-world signature/reload/idempotence/career checks\nDee Valley, Silverstone and Spa\n");Finish(0);return;}
                    previous=current;GameSession.AutoStartCircuit=false;Check(current.SelectRegion(Worlds[index]),"next built-in source world opens in the same process");phase=2;
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
