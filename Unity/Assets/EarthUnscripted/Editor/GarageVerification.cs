using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class GarageVerification
    {
        const string Key="EarthUnscriptedGarageVerification";static GameSession session;static int stage,checks;static double wallStart;static float began,stockSpeed;static Vector3 roadPosition;static Quaternion roadRotation;
        static GarageVerification(){if(!SessionState.GetBool(Key,false))return;PlayerProfile.StorageOverride=SessionState.GetString(Key+"Path","");wallStart=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
        public static void Run()
        {ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");SessionState.SetString(Key+"Path",Path.GetFullPath("Builds/Verification/garage-"+DateTime.UtcNow.Ticks+".json"));SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        static void Check(bool value,string text){if(!value)throw new Exception("GARAGE FAIL: "+text);checks++;Debug.Log("GARAGE PASS: "+text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-wallStart>150)throw new Exception("Garage verification timed out");if(!EditorApplication.isPlaying)return;
                if(stage==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;
                    foreach(var traffic in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
                    session.Input.VerificationInput=true;session.TowToGarage();session.Economy.AddCredits(40000);string starter=session.Profile.garage.activeId;
                    float power=session.Car.PowerFactor;
                    Check(session.GarageChange((g,c)=>GarageRules.Upgrade(g,c,0)),"engine upgrade commits at the physical garage");
                    Check(session.Car.PowerFactor>power*1.1f,"fitted engine changes the vehicle controller's power");
                    Check(session.GarageChange((g,c)=>GarageRules.Upgrade(g,c,2))&&session.Car.TyreFactor>1,"fitted tyres change actual grip tuning");
                    Check(session.GarageChange((g,c)=>GarageRules.Style(g,c,2,1,1,1)),"appearance transaction saves without losing upgrades");
                    session.Car.ReplicaDamage(24);session.Car.Visual.Dent(session.Car.transform.TransformPoint(new Vector3(0,.8f,2.15f)),18,session.Car.transform.TransformDirection(Vector3.back));float dent=session.Car.Visual.DeformationExtent;session.Save();
                    Check(session.GarageChange((g,c)=>GarageRules.Buy(g,c,1,false)),"a second car can be bought while retaining the first");
                    Check(session.Profile.garage.cars.Count==2&&session.Car.Body.mass==1480,"new model has its own physical mass and ownership");
                    Check(session.GarageChange((g,c)=>GarageRules.Select(g,c,starter)),"stored starter can be reselected");
                    Check(session.Car.Damage==24&&Mathf.Abs(session.Car.Visual.DeformationExtent-dent)<.01f&&session.Profile.garage.Active.engine==1,"per-car condition, dents and parts restore after switching");
                    var loaded=PlayerProfile.Load();Check(loaded.garage.cars.Count==2&&loaded.garage.Active.damage==24&&loaded.credits==session.Economy.Credits,"garage and credits reload consistently from the same save");
                    Check(File.Exists(PlayerProfile.StorageOverride+".bak"),"atomic save retains a previous career backup");
                    string path=PlayerProfile.StorageOverride;string blocker=path+"-blocker";File.WriteAllText(blocker,"isolated write-failure test");int balance=session.Economy.Credits;int level=session.Profile.garage.Active.gearing;
                    try{PlayerProfile.StorageOverride=blocker+"/save.json";Check(!session.GarageChange((g,c)=>GarageRules.Upgrade(g,c,1)),"failed disk save rejects the purchase");Check(session.Economy.Credits==balance&&session.Profile.garage.Active.gearing==level,"failed save leaves credits and fitted parts unchanged");}
                    finally{PlayerProfile.StorageOverride=path;File.Delete(blocker);}
                    Check(session.GarageChange((g,c)=>GarageRules.Buy(g,c,2,true))&&session.Profile.garage.cars.Count==2&&!session.Profile.garage.cars.Exists(c=>c.id==starter),"trade replaces only the exchanged car");
                    session.Car.ReplicaDamage(30);session.Car.Visual.Dent(session.Car.transform.TransformPoint(new Vector3(0,.8f,-2.15f)),18,session.Car.transform.forward);balance=session.Economy.Credits;
                    Check(session.GarageChange((g,c)=>GarageRules.Repair(g,c))&&session.Economy.Credits==balance&&session.Car.Damage==0&&session.Car.Visual.DeformationExtent<.001f,"free repair atomically preserves credits and restores body condition");
                    var r=session.World.Route;float d=r.Length*.08f;roadPosition=r.At(d,-2.7f);roadRotation=r.Rotation(d);
                    BeginAcceleration(new OwnedCar());stage=1;return;
                }
                if(stage==1)
                {
                    if(Time.time-began<4)return;stockSpeed=session.Car.AbsoluteSpeed;session.SetMenu(true);BeginAcceleration(new OwnedCar{engine=2,tyres=2});stage=2;
                }
                else if(stage==2)
                {
                    if(Time.time-began<4)return;float tuned=session.Car.AbsoluteSpeed;Debug.Log($"GARAGE ACCELERATION: stock={stockSpeed:F2}m/s tuned={tuned:F2}m/s after4s");Check(tuned>stockSpeed*1.04f,"engine/tyre upgrades measurably improve physical acceleration");
                    session.SetMenu(true);session.Car.NetworkDriven=false;var active=session.Profile.garage.Active;session.Car.Visual.Rebuild(active);session.Car.ApplyTuning(active);
                    Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/garage.txt",$"PASS {checks} checks\nstock4s={stockSpeed}\ntuned4s={tuned}\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void BeginAcceleration(OwnedCar tuning)
        {session.Car.Visual.Rebuild(tuning);session.Car.ApplyTuning(tuning);session.Car.Recover(roadPosition,roadRotation);session.Car.NetworkDriven=true;session.Car.NetworkSteer=session.Car.NetworkBrake=session.Car.NetworkHandbrake=0;session.Car.NetworkThrottle=1;session.SetMenu(false);began=Time.time;}
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
