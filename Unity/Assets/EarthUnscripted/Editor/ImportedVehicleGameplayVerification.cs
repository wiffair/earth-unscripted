using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EarthUnscripted.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class ImportedVehicleGameplayVerification
    {
        const string Key="EUImportedVehicleGameplay",Starter="appearance-starter",Other="appearance-other";
        static string root,realPath,realHash;static int phase,checks,credits;static double deadline;static float began,maxRoll,maxWiper;static Vector3 initial;static Quaternion rollRest,steeringRest,wiperRest;static GameSession session,previous;
        static ImportedVehicleGameplayVerification()
        {
            if(!SessionState.GetBool(Key,false))return;root=SessionState.GetString(Key+"Root","");realPath=SessionState.GetString(Key+"RealPath","");realHash=SessionState.GetString(Key+"RealHash","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");root=Path.GetFullPath("Builds/Verification/imported-gameplay-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
            var profile=new PlayerProfile{quality=0,volume=0,rumble=0,credits=4321,livingScenery=false,weatherMode=0,clockMode=0,fixedHour=14};
            profile.garage=new GarageState{activeId=Starter,cars=new List<OwnedCar>{new OwnedCar{id=Starter,model=0,engine=2,gearing=1,tyres=1,brakes=1,suspension=1,differential=1,paint=3,finish=2},new OwnedCar{id=Other,model=1,engine=1,paint=2,damage=11}}};
            File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(profile));realPath=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");realHash=File.Exists(realPath)?DataPackageRules.HashFile(realPath):"missing";
            SessionState.SetString(Key+"Root",root);SessionState.SetString(Key+"RealPath",realPath);SessionState.SetString(Key+"RealHash",realHash);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string message){if(!value)throw new Exception("IMPORTED GAMEPLAY FAIL: "+message);checks++;Debug.Log("IMPORTED GAMEPLAY PASS: "+message);}
        static void Input(float gas,float brake,float steer)
        {session.Input.Throttle=gas;session.Input.Brake=brake;session.Input.Steer=steer;session.Input.Handbrake=0;session.Input.PausePressed=session.Input.ResetPressed=session.Input.RepairPressed=session.Input.ChallengePressed=session.Input.CameraPressed=session.Input.LookBack=false;}
        static bool Wait(float seconds)=>Time.unscaledTime-began<seconds;
        static void Next(int next){phase=next;began=Time.unscaledTime;}
        static bool Tuning()=>session.Profile.garage.Active.engine==2&&session.Profile.garage.Active.tyres==1&&session.Profile.garage.Active.suspension==1&&Mathf.Abs(session.Car.PowerFactor-GarageRules.Power(session.Profile.garage.Active))<.00001f&&Mathf.Abs(session.Car.TyreFactor-GarageRules.Grip(session.Profile.garage.Active))<.00001f;
        static void SetupSession()
        {
            session.Input.enabled=false;session.Updates.Automatic=false;session.Updates.Enabled=false;
            foreach(var traffic in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Imported gameplay timed out phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||!session.Updates||LocationLoading.Busy)return;SetupSession();
                    Check(PlayerProfile.StoragePath.StartsWith(root,StringComparison.Ordinal)&&CareerStorage.Folder.StartsWith(root,StringComparison.Ordinal),"working profile and named career slots are isolated");
                    Check(session.Car.Visual.ImportedAppearance&&session.Profile.garage.Active.id==Starter,"actual starter gameplay builds the imported appearance");
                    Check(Tuning()&&session.Car.Body.mass==1350,"starter tuning and unchanged physics mass reach the actual vehicle");
                    Input(0,1,0);session.SetMenu(false);Next(1);return;
                }
                if(phase==1)
                {
                    Input(0,1,0);if(Wait(1.5f))return;
                    Check(session.Car.Grounded&&session.Car.WheelGrounded.Count(x=>x)>=2,"actual suspension grounds the imported car on the driveable road");
                    Check(session.Conditions.Daylight>.9f&&session.Car.Visual.Headlights.All(l=>l&&!l.enabled),"actual daytime environment parks both physical headlights");
                    initial=session.Car.Body.position;rollRest=session.Car.Visual.Wheels[1].localRotation;steeringRest=session.Car.Visual.SteeringWheel.localRotation;Input(.35f,0,0);Next(2);return;
                }
                if(phase==2)
                {
                    Input(.35f,0,0);maxRoll=Mathf.Max(maxRoll,Quaternion.Angle(rollRest,session.Car.Visual.Wheels[1].localRotation));if(Wait(2.5f))return;
                    Check(Vector3.Distance(initial,session.Car.Body.position)>1&&session.Car.AbsoluteSpeed>1,"actual throttle and tyre forces move the imported car");
                    Check(maxRoll>30,"actual wheel rolling updates imported geometry through the unchanged controller");
                    int displayedMph=int.TryParse(session.Car.Visual.DashboardReadout,out int mph)?mph:-1;int actualMph=Mathf.Clamp(Mathf.RoundToInt(session.Car.AbsoluteSpeed*DrivingMath.MphPerMetreSecond),0,999);
                    Check(displayedMph>0&&Mathf.Abs(displayedMph-actualMph)<=1&&session.Car.Visual.GetComponentsInChildren<TextMesh>().Any(t=>t.name=="Dashboard speed unit"&&t.text=="MPH"),"actual cockpit dashboard displays moving-car speed in MPH");Input(.1f,0,.65f);Next(3);return;
                }
                if(phase==3)
                {
                    Input(.1f,0,.65f);if(Wait(1.25f))return;var front=session.Car.Visual.Wheels[1];var rear=session.Car.Visual.Wheels[0];var right=front.localRotation*Vector3.right;var rearRight=rear.localRotation*Vector3.right;
                    float yaw=Mathf.Atan2(-right.z,right.x)*Mathf.Rad2Deg;Check(session.Car.Steering>5&&Mathf.Abs(yaw-session.Car.Steering)<.1f&&Mathf.Abs(rearRight.z)<.001f,"front geometry follows real steering while rear axles remain straight");
                    Check(Quaternion.Angle(steeringRest,session.Car.Visual.SteeringWheel.localRotation)>45,"cockpit steering wheel turns from actual control input");
                    var caliper=session.Car.Visual.GetComponentsInChildren<Transform>().Single(t=>t.name=="Fixed brake caliper 1");Check(Vector3.Distance(caliper.localPosition,front.localPosition)<.001f&&Quaternion.Angle(caliper.localRotation,Quaternion.Euler(0,session.Car.Steering,0))<.1f,"real suspension/steering moves brake calipers without wheel roll");Input(0,1,0);Next(4);return;
                }
                if(phase==4)
                {
                    Input(0,1,0);if(Wait(1.5f))return;
                    Check(Mathf.Abs(session.Car.Steering)<.1f&&Quaternion.Angle(steeringRest,session.Car.Visual.SteeringWheel.localRotation)<.1f,"released steering returns physics and cockpit geometry to centre");
                    session.Profile.fixedHour=1;session.View.Mode=2;Next(5);return;
                }
                if(phase==5)
                {
                    Input(0,1,0);if(Wait(.5f))return;
                    Check(session.Conditions.Daylight<.1f&&session.Car.Visual.Headlights.All(l=>l&&l.enabled),"actual night environment switches headlights on automatically");
                    Check(Mathf.Abs(session.Car.Visual.CockpitEye.x)<.01f&&Vector3.Distance(session.View.transform.position,session.Car.transform.TransformPoint(session.Car.Visual.CockpitEye))<.015f,"actual cockpit camera uses the concept car's centre eye anchor");
                    session.Profile.fixedHour=14;session.Weather=1;wiperRest=session.Car.Visual.Wipers[0].localRotation;Next(6);return;
                }
                if(phase==6)
                {
                    Input(0,1,0);maxWiper=Mathf.Max(maxWiper,Quaternion.Angle(wiperRest,session.Car.Visual.Wipers[0].localRotation));if(Wait(5.5f))return;
                    Check(session.Conditions.Rain>.2f&&session.Car.Visual.WiperRain>.2f,"simulated rain reaches automatic imported-car weather controls");
                    Check(maxWiper>25&&session.Car.Visual.Wipers.All(w=>Quaternion.Angle(w.localRotation,Quaternion.identity)>1),"both replacement wipers actually animate while raining");
                    Check(session.Conditions.Daylight>.9f&&session.Car.Visual.Headlights.All(l=>l&&l.enabled),"heavy daylight rain keeps automatic headlights on");
                    Check(session.Conditions.WeatherRequests==0&&session.Updates.SourceRequests==0,"isolated gameplay makes no weather or map provider requests");
                    session.SetMenu(true);session.Car.Recover(session.World.Garage.position,session.World.Route.Rotation(35));session.Car.ReplicaDamage(17);session.Car.Visual.Dent(session.Car.transform.TransformPoint(new Vector3(0,.7f,2.15f)),18,session.Car.transform.TransformDirection(Vector3.back));credits=session.Economy.Credits;
                    Check(session.Car.Visual.DeformationExtent>.1f&&session.Car.Visual.RecordedDents.Count==1,"isolated career has actual imported-mesh damage to preserve");
                    Check(session.GarageChange((g,c)=>GarageRules.Select(g,c,Other)),"real garage transaction selects the second owned vehicle");Next(7);return;
                }
                if(phase==7)
                {
                    if(Wait(.15f))return;Check(!session.Car.Visual.ImportedAppearance&&session.Car.Body.mass==1480&&session.Profile.garage.Active.id==Other,"selecting another model replaces appearance and applies its actual tuning");
                    Check(session.Profile.garage.cars.Single(c=>c.id==Starter).damage==17&&session.Profile.garage.cars.Single(c=>c.id==Starter).dents.Count==1,"inactive imported car retains its condition and saved dent");
                    Check(session.GarageChange((g,c)=>GarageRules.Select(g,c,Starter)),"real garage transaction selects the imported starter again");Next(8);return;
                }
                if(phase==8)
                {
                    if(Wait(.15f))return;Check(session.Car.Visual.ImportedAppearance&&Tuning()&&session.Car.Damage==17&&session.Car.Visual.DeformationExtent>.1f,"selection roundtrip restores imported mesh damage and purchased tuning");
                    Check(session.Economy.Credits==credits&&Vector4.Distance(session.Car.Visual.Body.sharedMaterial.color,CarColours.Paint[3])<.001f,"selection roundtrip preserves earned credits and selected paint");
                    Check(session.SaveCareer(0,"Imported appearance proof"),"named isolated career stores imported ownership, condition and tuning");
                    Check(session.FreeRepair("Isolated repair proof")&&session.Car.Damage==0&&session.Car.Visual.DeformationExtent==0,"free repair clears active damage without altering the saved career slot");
                    var snapshot=CareerStorage.Read(0);Check(snapshot!=null&&snapshot.garage.Active.damage==17&&snapshot.garage.Active.engine==2&&snapshot.credits==credits,"saved career snapshot retains its pre-repair condition and upgrades");
                    Check(session.GarageChange((g,c)=>GarageRules.Select(g,c,Other)),"second model can be active before loading the saved imported career");previous=session;Check(session.LoadCareer(0),"saved imported career loads through the real scene transition");Next(9);return;
                }
                if(phase==9)
                {
                    var current=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(current)||ReferenceEquals(current,previous)||LocationLoading.Busy)return;session=current;SetupSession();
                    Check(UnityEngine.Object.FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length==1,"career load leaves exactly one live gameplay session");
                    Check(session.Car.Visual.ImportedAppearance&&Tuning()&&session.Car.Damage==17&&session.Car.Visual.RecordedDents.Count==1&&session.Car.Visual.DeformationExtent>.1f,"career reload restores actual imported deformation, tuning and ownership");
                    Check(session.Economy.Credits==credits&&session.Profile.garage.cars.Count==2,"career reload preserves both owned cars and funds");
                    Check((File.Exists(realPath)?DataPackageRules.HashFile(realPath):"missing")==realHash,"real career file remains byte-for-byte unchanged");
                    File.WriteAllText("Builds/Verification/imported-vehicle-gameplay.txt",$"PASS {checks} actual playmode physics/steering/weather/camera/garage/career checks\nroot={root}\nmaxWheelRotation={maxRoll:F1}\nmaxWiperRotation={maxWiper:F1}\ncredits={credits}\nNative rendering and physical controller hardware remain separate acceptance checks.\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Tick;
            // Keep the isolated StorageOverride until process exit: GameSession's
            // quit-save must never target the real career.
            EditorApplication.Exit(code);
        }
    }
}
