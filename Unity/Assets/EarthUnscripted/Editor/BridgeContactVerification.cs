using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class BridgeContactVerification
    {
        const string Key="EUBridgeContact";static GameSession session;static Vector3 centre,side;static Quaternion rotation;static int phase,sign=1,contacts,checks;static float began,maxLateral;static double deadline;static GeoRoad bridge;
        static BridgeContactVerification(){if(!SessionState.GetBool(Key,false))return;PlayerProfile.StorageOverride=SessionState.GetString(Key+"Path","");deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update+=Tick;}
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");Directory.CreateDirectory("Builds/Verification");string path=Path.GetFullPath("Builds/Verification/bridge-contact-"+DateTime.UtcNow.Ticks+".json");File.WriteAllText(path,JsonUtility.ToJson(new PlayerProfile{region="dee-valley",quality=0,volume=0,rumble=0,livingScenery=false}));SessionState.SetString(Key+"Path",path);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string text){if(!value)throw new Exception("BRIDGE CONTACT FAIL: "+text);checks++;Debug.Log("BRIDGE CONTACT PASS: "+text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Bridge contact test timed out");if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;session.Input.VerificationInput=true;session.SetMenu(false);
                    foreach(var car in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))car.gameObject.SetActive(false);
                    var region=session.World.Route.Region;bridge=Array.Find(region.roads,r=>r.bridge&&r.name=="Llangollen Bridge");centre=region.RoadPoint(bridge,bridge.length*.5f,out var forward);side=Vector3.Cross(Vector3.up,forward).normalized;rotation=Quaternion.LookRotation(forward,Vector3.up);session.World.GeographicWorld.Refresh(centre,true);
                    session.Car.Crashed+=(_,__)=>contacts++;session.Car.NetworkDriven=true;session.Car.NetworkThrottle=session.Car.NetworkBrake=session.Car.NetworkSteer=session.Car.NetworkHandbrake=0;Position();phase=1;return;
                }
                if(phase==1)
                {if(Time.time-began<1.5f)return;session.Car.Body.linearVelocity=side*sign*7;began=Time.time;maxLateral=0;phase=2;return;}
                if(phase==2)
                {
                    maxLateral=Mathf.Max(maxLateral,Vector3.Dot(session.Car.Body.position-centre,side*sign));
                    if(Time.time-began<3)return;Debug.Log($"BRIDGE CONTACT METRICS side={sign} furthestCentre={maxLateral:F2} halfWidth={bridge.width*.5f:F2} contacts={contacts} damage={session.Car.Damage:F2}");
                    Check(contacts>0&&session.Car.Damage>0,"a real sideways vehicle impact contacts the bridge barrier");Check(maxLateral<bridge.width*.5f+.25f,"the barrier keeps the vehicle centre on the bridge");Check(session.Car.Body.position.y>centre.y-1,"the car remains on the deck after contact");
                    if(sign>0){sign=-1;Position();phase=1;return;}File.WriteAllText("Builds/Verification/bridge-contact.txt","PASS "+checks+" two-sided physical vehicle/barrier checks\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Position(){contacts=0;session.Car.Repair();session.Car.Recover(centre+side*sign*(bridge.width*.5f-2.5f),rotation);began=Time.time;Time.timeScale=3;}
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
