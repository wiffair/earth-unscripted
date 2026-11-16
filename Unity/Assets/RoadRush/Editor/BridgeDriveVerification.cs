using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    [InitializeOnLoad] public static class BridgeDriveVerification
    {
        const string Key="RRBridgeDrive";static GameSession session;static RaceDriver driver;static RoutePath route;static bool reverse,entered,exited;static int phase,recoveries,checks;static double deadline;static float previous,travel,began,maxVertical,maxRoll;
        static BridgeDriveVerification(){if(!SessionState.GetBool(Key,false))return;PlayerProfile.StorageOverride=SessionState.GetString(Key+"Profile","");deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Tick;}
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");Directory.CreateDirectory("Builds/Verification");string path=Path.GetFullPath("Builds/Verification/bridge-drive-"+DateTime.UtcNow.Ticks+".json");File.WriteAllText(path,JsonUtility.ToJson(new PlayerProfile{region="dee-valley",quality=0,volume=0,rumble=0,livingScenery=false}));SessionState.SetString(Key+"Profile",path);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static RoutePath BuildRoute(GeoRegion region,bool backwards)
        {
            var bridge=region.roads.First(r=>r.bridge&&r.name=="Llangollen Bridge");var used=new HashSet<GeoRoad>{bridge};
            List<Vector3> Extend(string node,Vector3 origin,Vector3 direction)
            {
                var result=new List<Vector3>();float distance=0;
                while(distance<110)
                {
                    var next=region.roads.Where(r=>!used.Contains(r)&&!r.tunnel&&(r.start==node||r.end==node)).OrderByDescending(r=>Vector3.Dot((r.start==node?r.points[1]-origin:r.points[r.points.Length-2]-origin).normalized,direction)).FirstOrDefault();if(next==null)break;
                    used.Add(next);var points=next.start==node?next.points:next.points.Reverse().ToArray();
                    foreach(var point in points.Skip(1)){distance+=Vector3.Distance(origin,point);direction=(point-origin).normalized;origin=point;result.Add(point);}node=next.start==node?next.end:next.start;
                }
                if(distance<70)throw new Exception("Insufficient mapped bridge approach");return result;
            }
            var before=Extend(bridge.start,bridge.points[0],(bridge.points[0]-bridge.points[1]).normalized);before.Reverse();
            var after=Extend(bridge.end,bridge.points.Last(),(bridge.points.Last()-bridge.points[bridge.points.Length-2]).normalized);
            var copy=JsonUtility.FromJson<GeoRegion>(JsonUtility.ToJson(region));copy.route=before.Concat(bridge.points).Concat(after).ToArray();copy.Prepare();return new RoutePath(copy,backwards);
        }
        static void Begin()
        {
            route=BuildRoute(session.World.Route.Region,reverse);driver.Route=route;session.Car.Repair();session.Car.Recover(route.At(20,0),route.Rotation(20));session.World.GeographicWorld.Refresh(session.Car.Body.position,true);
            previous=20;travel=maxVertical=maxRoll=0;recoveries=0;entered=exited=false;began=Time.time;Time.timeScale=4;
        }
        static void Check(bool condition,string text){if(!condition)throw new Exception("BRIDGE DRIVE FAIL "+text);checks++;Debug.Log("BRIDGE DRIVE PASS "+text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Bridge driving timed out");if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;Application.runInBackground=true;session.Input.VerificationInput=true;session.SetMenu(false);session.Car.NetworkDriven=true;
                    foreach(var traffic in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
                    driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Difficulty=0;driver.Recovered+=_=>recoveries++;Begin();phase=1;return;
                }
                float progress=route.Closest(session.Car.Body.position,out _,out _);float delta=Core.DrivingMath.SignedRouteDelta(previous,progress,route.Length);previous=progress;if(Mathf.Abs(delta)<20)travel+=Mathf.Max(0,delta);
                var road=session.World.Route.Region.Nearest(session.Car.Body.position,out _,out _);if(road.bridge&&road.name=="Llangollen Bridge")entered=true;else if(entered)exited=true;
                if(Time.time-began>2)maxVertical=Mathf.Max(maxVertical,Mathf.Abs(session.Car.Body.linearVelocity.y));maxRoll=Mathf.Max(maxRoll,Mathf.Abs(Vector3.SignedAngle(Vector3.up,session.Car.transform.up,session.Car.transform.forward)));
                if(travel<route.Length-50&&Time.time-began<100)return;
                string metrics=$"reverse={reverse} distance={travel:F1}/{route.Length:F1}m damage={session.Car.Damage:F2} recoveries={recoveries} vertical={maxVertical:F2}m/s roll={maxRoll:F1}deg";Debug.Log("BRIDGE DRIVE METRICS "+metrics);
                Check(entered&&exited,"vehicle physically crosses the bridge and both approaches");Check(travel>=route.Length-50&&recoveries==0,"crossing completes without recovery");Check(session.Car.Damage<5,"approach joins do not cause a damaging impact");Check(maxVertical<10&&maxRoll<35,"bridge crossing remains stable");
                if(!reverse){reverse=true;Begin();return;}
                File.WriteAllText("Builds/Verification/bridge-drive.txt","PASS "+checks+" physical two-direction approach/deck checks\n"+metrics);Finish(0);
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
