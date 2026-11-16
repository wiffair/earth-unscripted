using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadRush.Editor
{
    [InitializeOnLoad]
    public static class DrivingFixVerification
    {
        const string Key="RoadRushDrivingFixes";
        static GameSession session;
        static TrafficCar patrol;
        static Gamepad pad;
        static double wallStart;
        static float began;
        static int stage,scenario,creditsBefore;
        static DrivingFixVerification()
        {
            if(!SessionState.GetBool(Key,false))return;
            PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/driving-fix-test-profile.json");
            wallStart=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Require(bool condition,string text){if(!condition)throw new Exception("FIX FAIL: "+text);Debug.Log("FIX PASS: "+text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-wallStart>150)throw new Exception("Fix verification timed out");
                if(!EditorApplication.isPlaying)return;
                if(stage==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;
                    VerifyRoad();
                    foreach(var t in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                        if(t.Police&&patrol==null)patrol=t;else t.gameObject.SetActive(false);
                    pad=InputSystem.AddDevice<Gamepad>();pad.MakeCurrent();session.Input.VerificationInput=true;session.Input.VerificationPad=pad;session.SetMenu(false);
                    session.Car.Recover(session.World.Route.At(session.World.Route.Length*.43f,-2.7f),session.World.Route.Rotation(session.World.Route.Length*.43f));
                    began=Time.time;stage=1;
                }
                float elapsed=Time.time-began;
                if(stage==1)
                {
                    if(elapsed<1)return;
                    InputSystem.QueueDeltaStateEvent(pad.leftTrigger,1f);began=Time.time;stage=2;
                }
                else if(stage==2)
                {
                    if(elapsed<2)return;
                    Require(session.Car.Reverse&&session.Car.Speed < -1,"Brake trigger engages physical reverse after stopping");
                    InputSystem.QueueDeltaStateEvent(pad.leftTrigger,0f);InputSystem.QueueDeltaStateEvent(pad.rightTrigger,1f);began=Time.time;stage=3;
                }
                else if(stage==3)
                {
                    if(elapsed<3)return;
                    Require(!session.Car.Reverse&&session.Car.Speed>1,"Accelerator stops reverse motion and drives forward");
                    InputSystem.QueueDeltaStateEvent(pad.rightTrigger,0f);BeginPoliceScenario();stage=4;
                }
                else if(stage==4)
                {
                    if(session.Wanted>0)
                    {
                        RequireDuring(elapsed<20,"Patrol must intercept scenario "+scenario);
                        return;
                    }
                    Require(session.Economy.Credits==creditsBefore-200,"Patrol intercepts and tickets scenario "+scenario+"; credits "+session.Economy.Credits+" expected "+(creditsBefore-200)+"; "+session.Notice);
                    scenario++;
                    if(scenario<4){BeginPoliceScenario();return;}
                    session.SetMenu(true);Directory.CreateDirectory("Builds/Verification");
                    File.WriteAllText("Builds/Verification/driving-fixes-result.txt","PASS: old town grass clipping reproduced; current terrain clear below full road; brake-to-reverse and accelerator-to-forward; patrol intercepts opposite lane, opposite direction, route seam and an overshot target.\n");
                    Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void RequireDuring(bool condition,string text){if(!condition)throw new Exception("FIX FAIL: "+text);}
        static void BeginPoliceScenario()
        {
            var r=session.World.Route;float d=scenario==2?10:r.Length*.43f;
            int direction=scenario==1?-1:1;float lateral=scenario==0?2.7f:-2.7f;
            session.Car.Recover(r.At(d,lateral),r.Rotation(d)*Quaternion.Euler(0,direction<0?180:0,0));
            session.Car.Body.position-=Vector3.up*.55f;
            session.Economy.AddCredits(1000);creditsBefore=session.Economy.Credits;
            // Physical patrols are now already present in the world. Place a real
            // test observer 50m behind instead of relying on the retired spawn warp.
            patrol.Direction=direction;patrol.Distance=Core.DrivingMath.Wrap(d-direction*50,r.Length);patrol.Lane=-direction*2.7f;
            var patrolBody=patrol.GetComponent<Rigidbody>();patrolBody.position=r.At(patrol.Distance,patrol.Lane)+Vector3.up*.15f;patrolBody.rotation=r.Rotation(patrol.Distance)*Quaternion.Euler(0,direction<0?180:0,0);patrolBody.linearVelocity=Vector3.zero;patrolBody.angularVelocity=Vector3.zero;
            typeof(GameSession).GetMethod("AlertPolice",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,null);
            if(scenario==0){patrol.Lane=-2.7f;patrol.GetComponent<Rigidbody>().position=r.At(patrol.Distance,patrol.Lane);}
            if(scenario==3){patrol.Distance=d+16;patrol.GetComponent<Rigidbody>().position=r.At(patrol.Distance,patrol.Lane);}
            Debug.Log("Pursuit setup "+scenario+": player "+session.Car.Body.position+" patrol "+patrol.PhysicsPosition+" route "+patrol.Distance+" direction "+patrol.Direction);
            began=Time.time;
        }
        static void VerifyRoad()
        {
            var r=session.World.Route;
            var ground=session.World.transform.Find("Valley terrain").GetComponent<MeshCollider>();
            var asphalt=session.World.transform.Find("Continuous road").GetComponent<MeshCollider>();
            Physics.SyncTransforms();float worst=float.NegativeInfinity;int covered=0,samples=0;
            for(float d=0;d<r.Length;d+=4)
                foreach(float side in new[]{-.95f,-.5f,0,.5f,.95f})
                {
                    var p=r.At(d,r.Width(d)*.5f*side);var ray=new Ray(p+Vector3.up*70,Vector3.down);
                    if(!asphalt.Raycast(ray,out var roadHit,150))throw new Exception("Road surface missing at "+d+" / "+side);
                    if(ground.Raycast(ray,out var groundHit,150))worst=Mathf.Max(worst,groundHit.point.y-roadHit.point.y);
                    if(d>r.Length*.32f&&d<r.Length*.52f&&OldInterpolatedGround(r,p.x,p.z)>roadHit.point.y)covered++;
                    samples++;
                }
            Require(covered>0,"Regression reproduces old grass coverage at "+covered+" town road samples");
            Require(worst<-.02f,"Terrain remains below asphalt across "+samples+" road samples; minimum clearance "+(-worst).ToString("F3")+"m");
        }
        static float OldInterpolatedGround(RoutePath r,float x,float z)
        {
            float gx=(x+960)/12,gz=(z+1200)/12;float tx=gx-Mathf.Floor(gx),tz=gz-Mathf.Floor(gz);
            float x0=Mathf.Floor(gx)*12-960,z0=Mathf.Floor(gz)*12-1200;
            float a=OldVertex(r,x0,z0),b=OldVertex(r,x0+12,z0),c=OldVertex(r,x0,z0+12),d=OldVertex(r,x0+12,z0+12);
            return tx+tz<=1?a*(1-tx-tz)+b*tx+c*tz:d*(tx+tz-1)+b*(1-tz)+c*(1-tx);
        }
        static float OldVertex(RoutePath r,float x,float z)
        {
            float d=r.Closest(new Vector3(x,0,z),out _,out float distance);
            float natural=-4+Mathf.PerlinNoise((x+2500)*.0018f,(z+2500)*.0018f)*36;
            return Mathf.Lerp(r.At(d).y-.17f,natural,Mathf.SmoothStep(0,1,Mathf.Clamp01((distance-4.5f-2)/38)));
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;
            if(pad!=null)InputSystem.RemoveDevice(pad);EditorApplication.Exit(code);
        }
    }
}
