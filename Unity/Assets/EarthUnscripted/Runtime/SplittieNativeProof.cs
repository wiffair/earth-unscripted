using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Explicit standalone camper check. Never runs in ordinary gameplay.
    /// Owns only its fresh /private/tmp output; never loads a player save.</summary>
    public sealed class SplittieNativeProof : MonoBehaviour
    {
        const string Identifier = "com.earthunscripted.splittieqa";
        string output;
        readonly List<string> errors = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate() { if (Application.identifier == Identifier) GameSession.SuppressAutomaticLaunch = true; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { if (Application.identifier == Identifier) new GameObject("Splittie isolated check").AddComponent<SplittieNativeProof>(); }
        IEnumerator Start()
        {
            output = Path.Combine("/private/tmp", "SplittieQA-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);
            Application.runInBackground=true;Time.timeScale=1;Time.fixedDeltaTime=.01f;
            Physics.simulationMode=SimulationMode.FixedUpdate;
            InvokeRepeating(nameof(Heartbeat),1,1);
            Application.logMessageReceived += Capture;
            var sequence = Exercise();
            while (true)
            {
                object value = null; bool more = false;
                try { more = sequence.MoveNext(); if (more) value = sequence.Current; }
                catch (Exception exception) { errors.Add(exception.ToString()); }
                if (errors.Count > 0 || !more) break;
                yield return value;
            }
            File.WriteAllText(Path.Combine(output,"result.json"), new JObject { ["passed"] = errors.Count == 0, ["errors"] = new JArray(errors), ["output"] = output }.ToString());
            Debug.Log("SPLITTIE_QA_RESULT " + output);
            Application.Quit(errors.Count == 0 ? 0 : 1);
        }
        void Heartbeat() { Debug.Log("SPLITTIE_QA_TICK time="+Time.time+" scale="+Time.timeScale); if(Time.realtimeSinceStartup>90) { File.WriteAllText(Path.Combine(output,"timeout.txt"),"Native check exceeded 90 seconds");Application.Quit(2); } }
        void Capture(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) errors.Add(message + "\n" + stack); }
        IEnumerator Exercise()
        {
            VehiclePackLibrary.Initialize(output, Path.Combine(Application.streamingAssetsPath,"VehiclePacks"));
            var pack = VehiclePackFormat.Read(Path.Combine(Application.streamingAssetsPath,"VehiclePacks/VW-Splittie-1.2.0.eucar"));
            if (pack.Settings.Price != 0) throw new Exception("Camper must be free");
            var entry = ImportedVehicleCatalogue.Find(pack.Settings.Id);
            if (entry == null) throw new Exception("Camper not in catalogue");
            Geometry.Box("QA ground", null, new Vector3(0,-.5f,0),new Vector3(300,1,300),Geometry.Material("QA ground",new Color(.18f,.22f,.20f)),true);
            var root = new GameObject("VW Splittie");root.layer=8;
            root.transform.position=new Vector3(0,entry.Metric.RestRootHeight(entry.Settings.Mass),0);
            root.AddComponent<Rigidbody>();var chassis=root.AddComponent<BoxCollider>();
            chassis.center=new Vector3(0,.86f,0);chassis.size=new Vector3(1.8f,.85f,4);
            var visual=root.AddComponent<CarVisual>();visual.Build(new Color(.19f,.59f,.49f),true,entry.Model);
            var car=root.AddComponent<VehicleController>();car.Visual=visual;car.NetworkDriven=true;
            car.ApplyTuning(new OwnedCar { model=entry.Model,packVehicleId=entry.Settings.Id });
            var audio = root.AddComponent<DrivingAudio>();audio.Car=car;audio.Profile=new PlayerProfile { volume=.6f };
            var camera=new GameObject("QA camera").AddComponent<Camera>();camera.gameObject.AddComponent<AudioListener>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.23f,.29f,.31f);camera.fieldOfView=40;
            var sun=new GameObject("QA sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(45,-35,0);
            RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            for(int frame=0;frame<200;frame++)yield return new WaitForFixedUpdate();
            if(!car.WheelGrounded.All(v=>v))throw new Exception("Not all camper wheels grounded");
            var particles=root.GetComponentInChildren<ParticleSystem>();
            if(!particles || particles.particleCount==0)throw new Exception("Tailpipe smoke not emitting");
            if(car.EngineLayout!="flat4-aircooled" || audio.EngineRpm<800 || !audio.UsesRecordedEngine)throw new Exception("Flat-four sound not active");
            camera.transform.position=root.transform.position+new Vector3(5,3.2f,6);camera.transform.LookAt(root.transform.position+Vector3.up*1.1f);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"front.png"));yield return null;
            camera.transform.position=root.transform.position+new Vector3(-5,2.8f,-6);camera.transform.LookAt(root.transform.position+Vector3.up*1.1f);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"rear.png"));yield return null;
            var start=car.Body.position;car.NetworkThrottle=1;float peak=0;
            for(int frame=0;frame<500;frame++){peak=Mathf.Max(peak,car.AbsoluteSpeed);yield return new WaitForFixedUpdate();}
            float distance=Vector3.Distance(start,car.Body.position);
            if(distance<5 || peak<2 || Vector3.Dot(root.transform.up,Vector3.up)<.9f)throw new Exception("Camper failed stable acceleration");
            File.WriteAllText(Path.Combine(output,"metrics.json"),new JObject { ["priceCredits"]=pack.Settings.Price,["distanceMetres"]=distance,["peakMetresPerSecond"]=peak,["smokeParticles"]=particles.particleCount,["engineRpm"]=audio.EngineRpm,["recordedEngine"]=audio.UsesRecordedEngine,["wheelContacts"]=car.WheelGrounded.Count(v=>v) }.ToString());
        }
        void OnDestroy(){Application.logMessageReceived-=Capture;}
    }
}
