using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    [InitializeOnLoad] public static class PoliceGameplayVerification
    {
        const string Key="RoadRushPoliceGameplay";static GameSession session;static PoliceDriver patrol;static int stage,checks;static float began;static double wallStart;static Vector3 startPatrol;
        static PoliceGameplayVerification()
        {
            if(!SessionState.GetBool(Key,false))return;
            PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/police-gameplay-profile.json");wallStart=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
        }
        public static void Run(){ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        static void Check(bool value,string message){if(!value)throw new Exception("PATROL FAIL: "+message);checks++;Debug.Log("PATROL PASS: "+message);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-wallStart>150)throw new Exception("Patrol verification timed out");if(!EditorApplication.isPlaying)return;
                if(stage==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;
                    foreach(var t in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
                    {if(t.ManagedPolice&&patrol==null){patrol=t.ManagedPolice;patrol.enabled=false;}else t.gameObject.SetActive(false);}
                    session.Input.VerificationInput=true;session.SetMenu(false);session.Car.NetworkDriven=true;
                    float d=session.World.Route.Length*.43f;session.Car.Recover(session.World.Route.At(d,-2.7f),session.World.Route.Rotation(d));session.Car.Body.linearVelocity=session.Car.transform.forward*20;
                    PlacePatrol(d+35);began=Time.time;stage=1;return;
                }
                if(stage==1)
                {
                    if(Time.time-began<.15f)return;
                    Check(session.Limit==30&&session.OnRoad,"speed observer uses the player's current village limit");
                    Check(patrol.CanObserveSpeeding(),"nearby visible speeding vehicle is detected");
                    var velocity=session.Car.Body.linearVelocity;session.Car.Body.linearVelocity=session.Car.transform.forward*6;
                    Check(!patrol.CanObserveSpeeding(),"legal speed does not trigger patrol");session.Car.Body.linearVelocity=velocity;
                    var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.name="Temporary radar sightline obstruction";obstacle.transform.position=(patrol.Car.Body.position+session.Car.Body.position)*.5f+Vector3.up*2;obstacle.transform.localScale=new Vector3(8,6,8);Physics.SyncTransforms();
                    Check(!patrol.CanObserveSpeeding(),"building/obstacle blocks a speed observation");UnityEngine.Object.DestroyImmediate(obstacle);Physics.SyncTransforms();
                    var saved=patrol.Car.Body.position;patrol.Car.Body.position=session.Car.Body.position+session.Car.transform.forward*120;Physics.SyncTransforms();Check(!patrol.CanObserveSpeeding(),"distant observer cannot see speeding everywhere");patrol.Car.Body.position=saved;
                    // A controlled grass pursuit, with both vehicles already physical.
                    float d=session.World.Route.Length*.72f;session.Car.Recover(session.World.Route.At(d,27),session.World.Route.Rotation(d));session.Car.Body.linearVelocity=Vector3.zero;
                    PlacePatrol(d-35);patrol.enabled=true;patrol.BeginPursuit();startPatrol=patrol.Car.Body.position;began=Time.time;stage=2;
                }
                else if(stage==2)
                {
                    if(Time.time-began<10)return;
                    session.World.Route.Closest(patrol.Car.Body.position,out _,out float lateral);
                    Check(lateral>session.World.Route.Width(patrol.Marker.Distance)*.5f+2,"patrol physically leaves asphalt to follow onto grass");
                    Check(Vector3.Distance(patrol.Car.Body.position,session.Car.Body.position)<35,"off-road pursuit closes on the player");
                    Check(Vector3.Distance(startPatrol,patrol.Car.Body.position)>15,"off-road patrol travelled through the world");
                    patrol.Disengage();startPatrol=patrol.Car.Body.position;began=Time.time;stage=3;
                }
                else if(stage==3)
                {
                    if(Time.time-began<1.5f)return;
                    Check(patrol.gameObject.activeInHierarchy&&patrol.State==PoliceDriver.Duty.Disengaging,"resolved patrol remains visible during disengagement");
                    Check(Vector3.Distance(startPatrol,patrol.Car.Body.position)<35,"disengagement does not teleport or remove the car");
                    Check(patrol.CooldownUntil>Time.time+60,"same patrol has an encounter cooldown");
                    Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/police-gameplay.txt","PASS "+checks+" checks\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void PlacePatrol(float d)
        {
            var r=session.World.Route;patrol.Car.Body.position=r.At(d,-r.Width(d)*.5f-3.2f)+Vector3.up*.15f;patrol.Car.Body.rotation=r.Rotation(d);patrol.Car.Body.linearVelocity=Vector3.zero;patrol.Car.Body.angularVelocity=Vector3.zero;
        }
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
