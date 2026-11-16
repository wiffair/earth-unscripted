using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class GroundingDriveVerification
    {
        const string Key="EUSurfaceDrive";static GameSession session;static RaceDriver driver;static int stage,recoveries;static bool reverse;static double deadline;static float start,previous,travel,maxVertical,peakRoll,maxSeparation;
        static GroundingDriveVerification(){if(!SessionState.GetBool(Key,false))return;reverse=SessionState.GetBool(Key+"Reverse",false);PlayerProfile.StorageOverride=SessionState.GetString(Key+"Path","");deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;}
        public static void RunReverse(){SessionState.SetBool(Key+"Reverse",true);Run();}
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");Directory.CreateDirectory("Builds/Verification");string path=Path.GetFullPath("Builds/Verification/ground-drive-"+DateTime.UtcNow.Ticks+".json");File.WriteAllText(path,JsonUtility.ToJson(new PlayerProfile{region="dee-valley",quality=0,volume=0,rumble=0,livingScenery=false}));SessionState.SetString(Key+"Path",path);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Surface driving timed out");if(!EditorApplication.isPlaying)return;
                if(stage==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;
                    Application.runInBackground=true;session.Input.VerificationInput=true;session.SetMenu(false);var route=reverse?new RoutePath(session.World.Route.Region,true):session.World.Route;float spawn=reverse?route.Length-1050:35;session.Car.Recover(route.At(spawn,0),route.Rotation(spawn));session.Car.NetworkDriven=true;
                    foreach(var other in UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None))if(other!=session.Car){foreach(var collider in other.GetComponentsInChildren<Collider>())collider.enabled=false;other.gameObject.SetActive(false);}
                    foreach(var traffic in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
                    driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Route=route;driver.Session=session;driver.Difficulty=0;driver.Recovered+=_=>recoveries++;
                    previous=driver.Route.Closest(session.Car.Body.position,out _,out _);start=Time.time;Time.timeScale=4;stage=1;return;
                }
                float progress=driver.Route.Closest(session.Car.Body.position,out _,out float separation);float delta=Core.DrivingMath.SignedRouteDelta(previous,progress,driver.Route.Length);previous=progress;if(Mathf.Abs(delta)<20)travel+=Mathf.Max(0,delta);
                maxSeparation=Mathf.Max(maxSeparation,separation);if(Time.time-start>2)maxVertical=Mathf.Max(maxVertical,Mathf.Abs(session.Car.Body.linearVelocity.y));peakRoll=Mathf.Max(peakRoll,Mathf.Abs(Vector3.SignedAngle(Vector3.up,session.Car.transform.up,session.Car.transform.forward)));
                if(Time.time-start>150||travel>1000)
                {
                    string metrics=$"distance={travel:F1}m recoveries={recoveries} damage={session.Car.Damage:F2} maxVertical={maxVertical:F2}m/s peakRoll={peakRoll:F1}degrees maxSeparation={maxSeparation:F2}m";Debug.Log("GROUND DRIVE "+metrics);
                    if(travel<1000||recoveries!=0||session.Car.Damage>5||maxVertical>10||peakRoll>35)throw new Exception("GROUND DRIVE FAIL "+metrics);
                    File.WriteAllText("Builds/Verification/grounding-drive"+(reverse?"-reverse":"")+".txt","PASS physically driven1000m through Dee Valley\n"+metrics);Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);SessionState.SetBool(Key+"Reverse",false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
