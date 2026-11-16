using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EarthUnscripted.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class NightLightingVerification
    {
        const string Flag="EUNightLightingVerification";static string root,realPath,realHash;static double deadline,began;static int phase,checks;static GameSession session;static Quaternion[] wiperRest;static float maxWiper;
        static readonly MethodInfo update=typeof(EnvironmentController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
        static NightLightingVerification()
        {
            if(!SessionState.GetBool(Flag,false))return;root=SessionState.GetString(Flag+"Root","");realPath=SessionState.GetString(Flag+"Real","");realHash=SessionState.GetString(Flag+"Hash","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");deadline=EditorApplication.timeSinceStartup+150;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");root=Path.GetFullPath("Builds/Verification/night-lighting-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"Regions"));
            File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(new PlayerProfile{quality=0,volume=0,rumble=0,credits=4321,livingScenery=false,weatherMode=0,clockMode=0,fixedHour=14}));realPath=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");realHash=File.Exists(realPath)?DataPackageRules.HashFile(realPath):"missing";
            SessionState.SetString(Flag+"Root",root);SessionState.SetString(Flag+"Real",realPath);SessionState.SetString(Flag+"Hash",realHash);SessionState.SetBool(Flag,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string description){if(!value)throw new Exception("NIGHT LIGHTING FAIL: "+description);checks++;Debug.Log("NIGHT LIGHTING PASS: "+description);}
        static void Apply(float hour,float clouds=.2f){session.Conditions.NetworkConditions(hour,0,0,clouds,3);update.Invoke(session.Conditions,null);}
        struct Lighting
        {
            public float sun,moon,reflection,exposure;public Color sky,equator,ground,skyGround,fog;public Vector3 sunRay,moonRay;
        }
        static Lighting Snapshot()=>new Lighting{sun=session.World.Sun.intensity,moon=session.World.Moon.intensity,reflection=RenderSettings.reflectionIntensity,exposure=RenderSettings.skybox.GetFloat("_Exposure"),sky=RenderSettings.ambientSkyColor,equator=RenderSettings.ambientEquatorColor,ground=RenderSettings.ambientGroundColor,skyGround=RenderSettings.skybox.GetColor("_GroundColor"),fog=RenderSettings.fogColor,sunRay=session.World.Sun.transform.forward,moonRay=session.World.Moon.transform.forward};
        static float ColourDistance(Color a,Color b)=>Vector4.Distance(a,b);
        static bool Near(Lighting a,Lighting b)=>Mathf.Abs(a.sun-b.sun)<.002f&&Mathf.Abs(a.moon-b.moon)<.002f&&Mathf.Abs(a.reflection-b.reflection)<.002f&&Mathf.Abs(a.exposure-b.exposure)<.002f&&ColourDistance(a.sky,b.sky)<.002f&&ColourDistance(a.ground,b.ground)<.002f&&ColourDistance(a.skyGround,b.skyGround)<.002f&&ColourDistance(a.fog,b.fog)<.002f;
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Night lighting test timed out phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||!session.Updates||LocationLoading.Busy)return;session.Input.enabled=false;session.Updates.Automatic=session.Updates.Enabled=false;session.Car.StartLocked=true;
                    foreach(var traffic in UnityEngine.Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
                    Check(PlayerProfile.StoragePath.StartsWith(root,StringComparison.Ordinal),"profile, careers and region preparation are isolated");
                    Check(session.World.Sun&&session.World.Moon&&session.World.Sun!=session.World.Moon&&session.World.Sun.type==LightType.Directional&&session.World.Moon.type==LightType.Directional,"world creates separate directional sun and moon");
                    Apply(14);var day=Snapshot();Check(session.Conditions.Daylight>.99f&&day.sun>1.8f&&day.sunRay.y<-.1f&&day.moon==0&&RenderSettings.sun==session.World.Sun,"daytime sun lights from above and moon is dark");
                    Check(session.Car.Visual.Headlights.All(l=>l&&!l.enabled),"automatic headlights are off on a dry bright day");
                    Apply(0);var night=Snapshot();Check(session.Conditions.Daylight<.01f&&night.sun==0&&night.moon>.015f&&night.moonRay.y<-.1f&&RenderSettings.sun==session.World.Moon,"midnight moon lights from above and below-ground sun emits no light");
                    Check(night.sky.grayscale<day.sky.grayscale*.05f&&night.equator.grayscale<day.equator.grayscale*.05f&&night.ground.grayscale<day.ground.grayscale*.05f,"all three ambient hemispheres become dark at night");
                    Check(night.ground.b>night.ground.r&&night.skyGround.b>night.skyGround.r,"night ambient ground and procedural sky ground have cool colours");
                    Check(night.exposure<day.exposure*.05f&&night.skyGround.grayscale<day.skyGround.grayscale*.05f,"procedural sky exposure and ground lose the bright daytime wash");
                    Check(night.reflection<day.reflection*.05f,"night reflection energy is scaled down from the cached daytime environment");
                    Check(session.Car.Visual.Headlights.All(l=>l&&l.enabled&&l.intensity>=100),"automatic physical headlights remain bright at night");
                    bool horizon=true,dominant=true,finite=true;for(int i=0;i<=720;i++)
                    {
                        Apply(i/30f);var current=Snapshot();horizon&=(current.sun<.00001f||current.sunRay.y<.00001f)&&(current.moon<.00001f||current.moonRay.y<.00001f);dominant&=RenderSettings.sun==(current.moon>current.sun?session.World.Moon:session.World.Sun);finite&=!float.IsNaN(current.sun)&&!float.IsNaN(current.moon)&&!float.IsNaN(current.exposure);
                    }
                    Check(horizon&&finite,"all721 cycle samples keep every illuminated source above the horizon with finite output");Check(dominant,"URP main directional follows the dominant source throughout the cycle");
                    bool continuous=true;foreach(float hour in new[]{0f,6f,18f,24f}){Apply(hour-.0001f);var left=Snapshot();Apply(hour+.0001f);continuous&=Near(left,Snapshot());}Check(continuous,"dawn, dusk and midnight boundaries have continuous intensity, ambient, sky and reflection values");
                    Apply(0,0);var clear=Snapshot();Apply(0,1);var covered=Snapshot();Check(covered.moon<clear.moon*.3f&&covered.reflection<clear.reflection,"cloud cover dims moonlight and reflected environment");
                    session.Conditions.NetworkConditions(2.5f,.6f,.8f,.9f,22,.003f);update.Invoke(session.Conditions,null);Check(Mathf.Abs(session.Conditions.Hour-2.5f)<.00001f&&session.Conditions.Rain==.6f&&session.Conditions.Wetness==.8f&&session.Conditions.Clouds==.9f&&session.Conditions.Wind==22&&Mathf.Abs(session.Conditions.Fog-.003f)<.000001f,"LAN host time, rain, wetness, cloud, wind and fog remain exact after lighting update");
                    // Exercise the actual fixed rain preset, not dynamic weather or
                    // a network override posing as rain; permit ordinary accumulation.
                    session.Conditions.NetworkConditions(1,0,0,.2f,0);session.Conditions.NetworkOverride=false;session.Profile.fixedHour=1;session.Profile.clockMode=0;session.Weather=1;session.Input.Throttle=0;session.Input.Brake=1;session.Input.Steer=0;session.SetMenu(false);wiperRest=session.Car.Visual.Wipers.Select(w=>w.localRotation).ToArray();began=EditorApplication.timeSinceStartup;phase=1;return;
                }
                if(phase==1)
                {
                    for(int i=0;i<wiperRest.Length;i++)maxWiper=Mathf.Max(maxWiper,Quaternion.Angle(wiperRest[i],session.Car.Visual.Wipers[i].localRotation));if(EditorApplication.timeSinceStartup-began<7)return;
                    Check(session.Weather==1&&!session.Conditions.NetworkOverride&&session.Conditions.Rain>.2f&&session.Conditions.Wetness>.02f,"actual fixed rain preset accumulates rain and road wetness at night");
                    Check(session.Car.Visual.Headlights.All(l=>l&&l.enabled)&&session.Car.Visual.WiperRain>.2f&&maxWiper>20,"rainy night keeps automatic headlights on and physically sweeps wipers");
                    Check(session.World.Sun.intensity==0&&session.World.Moon.transform.forward.y<0&&RenderSettings.sun==session.World.Moon,"rainy night still uses an above-horizon moon with the sun off");
                    Check(session.Conditions.WeatherRequests==0&&session.Updates.SourceRequests==0,"isolated lighting proof made no weather/map source requests");
                    Check((File.Exists(realPath)?DataPackageRules.HashFile(realPath):"missing")==realHash,"real career file remains byte-for-byte unchanged");
                    File.WriteAllText("Builds/Verification/night-lighting.txt","PASS "+checks+" actual Unity lighting/cycle/horizon/ambient/sky/reflection/continuity/LAN/rain/headlight/wiper/isolation checks\nApproximate game cycle; no astronomical accuracy or native rendering/performance claim.\n"+root+"\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
