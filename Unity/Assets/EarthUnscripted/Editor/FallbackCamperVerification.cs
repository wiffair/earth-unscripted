using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;
namespace EarthUnscripted.Editor
{
    public sealed class FallbackCamperContacts : MonoBehaviour
    {
        public static readonly JArray Events=new JArray();
        void OnCollisionEnter(Collision c){Record(c);}void OnCollisionStay(Collision c){if(c.impulse.magnitude>500)Record(c);}
        void Record(Collision c){if(Events.Count>=200)Events.RemoveAt(0);Events.Add(new JObject{{"name",c.collider.name},{"impulse",c.impulse.magnitude},{"otherKinematic",c.rigidbody?c.rigidbody.isKinematic:false},{"otherSpeed",c.rigidbody?c.rigidbody.linearVelocity.magnitude:0},{"speed",GetComponent<Rigidbody>().linearVelocity.magnitude}});}
    }
    [InitializeOnLoad] public static class FallbackCamperVerification
    {
        const string Key="EUFallbackCamper";static GameSession session;static string root;static double deadline;static int phase;static float began,progress,lastDistance,travelled,peak,peakGap;static readonly JArray samples=new JArray();
        static FallbackCamperVerification()
        {
            if(!SessionState.GetBool(Key,false))return;
            root=SessionState.GetString(Key+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
            VehiclePackLibrary.Initialize(Path.Combine(root,"Data"),Path.Combine(Application.streamingAssetsPath,"VehiclePacks"));deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            root=Path.GetFullPath("Builds/Verification/fallback-camper-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
            VehiclePackLibrary.Initialize(Path.Combine(root,"Data"),Path.Combine(Application.streamingAssetsPath,"VehiclePacks"));
            var entry=Core.ImportedVehicleCatalogue.Find("earthunscripted.vw-splittie");
            var profile=new PlayerProfile{region="",quality=0,volume=0,rumble=0,livingScenery=false,weatherMode=0,clockMode=0,fixedHour=14};
            profile.garage=new Core.GarageState{activeId="camper-test",cars=new System.Collections.Generic.List<Core.OwnedCar>{new Core.OwnedCar{id="camper-test",model=entry.Model,packVehicleId=entry.Settings.Id}}};
            File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(profile));
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"com.earthunscripted.qa.fallbackcamper");
            EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Fallback camper test timeout");if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!session)return;if(session.BootFailed)throw new Exception(session.BootStatus);if(!session.BootReady)return;
                    var camper=Core.ImportedVehicleCatalogue.Find("earthunscripted.vw-splittie");if(camper==null)throw new Exception("Bundled camper missing");
                    if(session.Car.Metrics.Model!=camper.Model)throw new Exception("Camper was not restored");
                    session.Car.gameObject.AddComponent<FallbackCamperContacts>();
                    if(session.World.Route.Region!=null)throw new Exception("Default fallback world required");
                    session.Input.VerificationInput=true;session.Car.NetworkDriven=true;session.Car.NetworkThrottle=.65f;session.Car.NetworkBrake=session.Car.NetworkHandbrake=0;
                    session.SetMenu(false);began=Time.time;lastDistance=session.World.Route.Closest(session.Car.Body.position,out _,out _);Time.timeScale=5;phase=1;return;
                }
                var car=session.Car;var route=session.World.Route;var pos=car.Body.position;float d=route.Closest(pos,out _,out _);
                float delta=d-lastDistance;if(delta<-route.Length*.5f)delta+=route.Length;if(delta>route.Length*.5f)delta-=route.Length;travelled+=Mathf.Max(0,delta);lastDistance=d;
                float gap=pos.y-route.TerrainHeight(pos.x,pos.z);peak=Mathf.Max(peak,car.AbsoluteSpeed);peakGap=Mathf.Max(peakGap,gap);
                if(samples.Count<1000)samples.Add(new JObject{{"time",Time.time-began},{"distance",d},{"speed",car.AbsoluteSpeed},{"gap",gap},{"grounded",car.Grounded},{"hit",car.LastImpactObject}});
                if(car.AbsoluteSpeed>85||gap>20||pos.y<-100)throw new Exception("Camper left the world: speed="+car.AbsoluteSpeed+" gap="+gap+" hit="+car.LastImpactObject);
                var target=route.At(d+Mathf.Max(10,car.AbsoluteSpeed*1.1f),-2.5f);var local=car.transform.InverseTransformPoint(target);float angle=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
                car.NetworkSteer=Mathf.Clamp(angle/Mathf.Lerp(30,10,car.AbsoluteSpeed/48),-1,1);car.NetworkThrottle=.8f;car.NetworkBrake=car.AbsoluteSpeed>20?.15f:0;
                if(travelled>route.Length*1.02f||Time.time-began>200){Finish(true,null);return;}
            }
            catch(Exception error){Debug.LogException(error);Finish(false,error.ToString());}
        }
        static void Finish(bool passed,string error)
        {
            File.WriteAllText(Path.Combine(root,"result.json"),new JObject{{"passed",passed},{"error",error},{"travelled",travelled},{"peakSpeed",peak},{"peakGap",peakGap},{"samples",samples},{"contacts",FallbackCamperContacts.Events}}.ToString());
            Debug.Log("FALLBACK CAMPER RESULT "+root+" passed="+passed+" distance="+travelled+" speed="+peak+" gap="+peakGap);SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(passed?0:1);
        }
    }
}
