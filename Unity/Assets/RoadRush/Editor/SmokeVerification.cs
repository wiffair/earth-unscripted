using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadRush.Editor
{
    [InitializeOnLoad]
    public static class SmokeVerification
    {
        static double began,wallStart;
        static int stage,errors,checkpoint,creditsBefore;
        static Gamepad pad;
        static GameSession session;
        static Vector3 start;
        const string Key="RoadRushSmoke";
        static SmokeVerification()
        {
            if(SessionState.GetBool(Key,false)){PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/test-profile.json");EditorApplication.update+=Tick;Application.logMessageReceived+=OnLog;wallStart=EditorApplication.timeSinceStartup;}
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void OnLog(string condition,string trace,LogType type){if(trace.Contains("UnityEditor.Search."))return;if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors++;}
        static void Require(bool condition,string message){if(!condition)throw new Exception("SMOKE FAIL: "+message);Debug.Log("SMOKE PASS: "+message);}
        static void Tick()
        {
            try
            {
                if(!EditorApplication.isPlaying)return;
                if(stage==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;
                    Require(session.World.Route.Length>3000,"Connected route length");
                    Require(session.Menu&&Time.timeScale==0,"Starts paused in main menu");
                    Require(Vector3.Distance(session.World.Route.At(0),session.World.Route.At(session.World.Route.Length))<.01f,"Route closes without a seam");
                    session.SetMenu(false);pad=InputSystem.AddDevice<Gamepad>();pad.MakeCurrent();session.Input.VerificationInput=true;session.Input.VerificationPad=pad;start=session.Car.transform.position;
                    began=Time.time;stage=1;
                }
                double elapsed=Time.time-began;
                if(stage==1)
                {
                    if(elapsed<1)return;
                    InputSystem.QueueDeltaStateEvent(pad.rightTrigger,.7f);
                    if(elapsed<5)return;
                    Require(session.Car.AbsoluteSpeed>2,"Gamepad trigger accelerates physical car");
                    Require(Vector3.Distance(start,session.Car.transform.position)>4,"Car moves through world");
                    Require(session.Car.transform.position.y>0&&session.Car.transform.position.y<50,"Suspension stays on terrain");
                    InputSystem.QueueDeltaStateEvent(pad.rightTrigger,0f);InputSystem.QueueDeltaStateEvent(pad.leftTrigger,1f);
                    stage=2;began=Time.time;
                }
                else if(stage==2)
                {
                    if(session.Car.Speed>.35f&&elapsed<4)return;
                    Require(session.Car.AbsoluteSpeed<2,"Gamepad brake stops car");
                    InputSystem.QueueDeltaStateEvent(pad.leftTrigger,0f);
                    session.StartRun();Require(session.Run.Active&&session.Run.Next==0,"Checkpoint challenge starts");
                    session.Recover();Require(!session.Run.Active,"Recovery cancels challenge");
                    session.Weather=1;session.Car.Repair();stage=3;began=Time.time;
                }
                else if(stage==3)
                {
                    if(elapsed<1)return;
                    Require(session.Car.Grip<1,"Rain changes tyre grip");
                    session.Weather=0;session.StartRun();checkpoint=0;creditsBefore=session.Economy.Credits;
                    InputSystem.QueueDeltaStateEvent(pad.leftTrigger,0f);InputSystem.QueueDeltaStateEvent(pad.rightTrigger,.2f);
                    PositionAtCheckpoint();stage=4;began=Time.time;
                }
                else if(stage==4)
                {
                    if(elapsed<2)return;
                    Require(session.Run.Next==checkpoint+1,"Checkpoint plane crossing "+(checkpoint+1));
                    checkpoint++;
                    if(checkpoint<8){PositionAtCheckpoint();began=Time.time;return;}
                    Require(session.Run.Finished&&!session.Run.Active,"Full challenge completes");
                    Require(session.Economy.Credits>=creditsBefore+400,"Challenge pays completion reward");
                    session.SetMenu(true);Require(Time.timeScale==0,"Pause freezes simulation");
                    session.Feedback.StopAll();Require(errors==0,"No game runtime errors");
                    Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/smoke-result.txt","PASS: boot, closed route, controller acceleration/braking, suspension, challenge start/recovery, all eight checkpoint crossings, completion reward, wet grip, pause. Hardware haptics not verified.\n");
                    Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
            if(EditorApplication.timeSinceStartup-wallStart>180){Debug.LogError("Smoke timed out");Finish(1);}
        }
        static void PositionAtCheckpoint()
        {
            float d=(checkpoint+1)*session.World.Route.Length/8-4;
            session.Car.Recover(session.World.Route.At(d,-2.7f),session.World.Route.Rotation(d));
            session.Car.Body.position-=Vector3.up*.55f;
            session.Car.Body.linearVelocity=session.World.Route.Forward(d)*6;
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
            if(pad!=null)InputSystem.RemoveDevice(pad);
            EditorApplication.Exit(code);
        }
    }
}
