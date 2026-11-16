using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class PantsideDriveVerification
    {
        const string Key="EUPantsideDrive";static GameSession session;static RaceDriver driver;static double deadline;static int stage,recoveries;static float began,travel,previous,peakRoll;static string root;
        static PantsideDriveVerification()
        {
            if(!SessionState.GetBool(Key,false))return;root=SessionState.GetString(Key+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"drive-profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Tick;
        }
        public static void Run()=>RunAt("Builds/Verification/Pantside");
        public static void RunDetail()=>RunAt("Builds/Verification/PantsideDetail");
        static void RunAt(string folder)
        {
            ProjectSetup.Configure();root=Path.GetFullPath(folder);RegionCache.OverrideFolder=Path.Combine(root,"Regions");string selected=Path.Combine(root,"profile.json");string id=File.Exists(selected)?JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(selected)).region:RegionCache.All().Single();
            File.WriteAllText(Path.Combine(root,"drive-profile.json"),JsonUtility.ToJson(new PlayerProfile{region=id,quality=0,volume=0,rumble=0,livingScenery=false}));
            EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Pantside drive timed out");if(!EditorApplication.isPlaying)return;
                if(stage==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;foreach(var traffic in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);Application.runInBackground=true;session.Input.VerificationInput=true;session.SetMenu(false);session.Car.NetworkDriven=true;
                    var route=RoadDemoRoute.Build(session.World.Route.Region,Vector3.zero);session.Car.Recover(route.At(0,route.LaneOffset(0)),route.Rotation(0));session.World.GeographicWorld.Refresh(session.Car.Body.position,true);
                    driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Route=route;driver.RoadLane=true;driver.Difficulty=0;driver.Recovered+=_=>recoveries++;
                    began=Time.time;Time.timeScale=4;stage=1;return;
                }
                float progress=driver.Route.Closest(session.Car.Body.position,out _,out _),delta=Core.DrivingMath.SignedRouteDelta(previous,progress,driver.Route.Length);previous=progress;if(Math.Abs(delta)<20)travel+=Math.Max(0,delta);
                peakRoll=Math.Max(peakRoll,Math.Abs(Vector3.SignedAngle(Vector3.up,session.Car.transform.up,session.Car.transform.forward)));
                if(Time.time-began<30)return;
                string metrics=$"duration={Time.time-began:F2}s distance={travel:F1}m damage={session.Car.Damage:F2} recoveries={recoveries} wanted={session.Wanted:F1} peakRoll={peakRoll:F1}deg street={session.CurrentRoad?.name}";Debug.Log("PANTSIDE DRIVE "+metrics);
                if(travel<120||session.Car.Damage>5||recoveries!=0||session.Wanted>0||peakRoll>35)throw new Exception("Pantside route needs correction: "+metrics);
                File.WriteAllText(Path.Combine(root,"drive-check.txt"),"PASS isolated30second road drive from Newlyn Road, NP11 5DB\n"+metrics+"\nFinal video deferred until next working release.\n");Finish(0);
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
