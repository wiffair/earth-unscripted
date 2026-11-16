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
    /// <summary>Explicit disposable-profile proof for recorded VW playback across
    /// 40 mph and parked car selection. Captures real per-source DSP buffers,
    /// checks their live gains/loop state and uses actual save/selection methods.</summary>
    public sealed class BetaAudioSwapVerification : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        readonly List<string> checks = new List<string>();
        readonly JArray samples = new JArray();
        readonly JObject result = new JObject();
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
        void Check(bool value,string message)
        {
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"progress.json"),new JObject{["check"]=message,["passed"]=value}.ToString());
            if(!value)throw new InvalidOperationException(message);checks.Add(message);
        }
        IEnumerator Start()
        {
            string failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0)
            {
                bool more=false;object current=null;
                try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}
                catch(Exception error){failure=error.ToString();}
                if(failure!=null)break;
                if(!more){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}
                yield return current;
            }
            bool passed=failure==null&&errors.Count==0;
            result["passed"]=passed;result["failure"]=failure;result["checks"]=new JArray(checks);
            result["errors"]=new JArray(errors);result["audioSamples"]=samples;result["version"]=Application.version;
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),result.ToString());
            Application.Quit(passed?0:1);
        }
        IEnumerator Exercise()
        {
            GameSession session=null;double began=Time.realtimeSinceStartupAsDouble;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
            {
                if(session&&session.BootFailed)throw new IOException(session.BootStatus);
                if(Time.realtimeSinceStartupAsDouble-began>600)throw new TimeoutException("World startup");
                yield return null;
            }
            session.SetMenu(true);session.Input.enabled=false;session.View.enabled=false;
            var ui=FindFirstObjectByType<DrivingUI>();if(ui)ui.enabled=false;
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            var car=session.Car;car.Body.linearVelocity=car.Body.angularVelocity=Vector3.zero;
            // Move only the disposable service marker: this exercises the real
            // not-at-garage gate without loading a remote area or fake car body.
            session.World.Garage.position+=Vector3.right*10000;
            Check(!session.AtGarage,"Fixture is outside Harbor Auto");
            var camper=ImportedVehicleCatalogue.Find("earthunscripted.vw-splittie");
            var hekla=ImportedVehicleCatalogue.Find("earthunscripted.hekla-formula-offroad");
            int credits=session.Economy.Credits;
            Check(session.SelectPackVehicle(camper.Model),"Free camper can be selected while parked outside Harbor Auto");
            string camperId=session.Profile.garage.activeId;
            Check(session.SelectPackVehicle(hekla.Model),"Imported Hekla can be selected at the same parked location");
            var parked=car.Body.position;
            Check(session.SelectOwnedVehicle(camperId),"My Garage owned-car API swaps to the exact camper identity away from Harbor Auto");
            Check(FlatDistance(parked,car.Body.position)<.02f&&session.Economy.Credits==credits,"Parked swap preserves XZ and credits");
            // Two examples of the same built-in model must not select the first
            // one accidentally. Use real Core ownership transactions as setup.
            session.Economy.AddCredits(50000);
            var purchase=GarageRules.Buy(session.Profile.garage,session.Economy.Credits,0,false);
            session.Profile.garage=purchase.Garage;session.Profile.credits=purchase.Credits;
            session.Economy.Spend(session.Economy.Credits-purchase.Credits);Check(session.Profile.Save(),"Disposable duplicate-model ownership setup saved");
            string secondCoupe=purchase.Garage.activeId;
            session.Profile.garage.Active.engine=2;
            Check(session.SelectOwnedVehicle(secondCoupe),"Owned-car API selects the requested duplicate-model ID");
            Check(session.Profile.garage.activeId==secondCoupe&&Mathf.Abs(car.PowerFactor-GarageRules.Power(session.Profile.garage.Active))<.001f,"Requested car's upgrades reach physics");
            Check(session.SelectOwnedVehicle(camperId),"Return to camper retains its saved identity");
            parked=car.Body.position;var heading=car.Body.rotation;
            car.Body.linearVelocity=car.transform.forward*3;
            Check(session.SelectOwnedVehicle(secondCoupe)&&session.Profile.garage.activeId==secondCoupe,"Paused travelling-car swap succeeds without Harbor Auto");
            Check(session.SelectOwnedVehicle(camperId),"Restore camper after paused travelling-car swap");
            car.Body.linearVelocity=Vector3.zero;
            var upgrade=GarageRules.UpgradePrice(session.Profile.garage.Active,0);
            Check(!session.GarageChange((g,c)=>GarageRules.Upgrade(g,c,0)),"Fitting upgrades still requires Harbor Auto");
            Check(!session.SelectOwnedVehicle("missing-car-id"),"Unknown saved car ID cannot create a car");
            var reload=PlayerProfile.Load();Check(reload.garage.Active.id==camperId,"Parked selection survives save/reload");

            // Use the real camera and pack mesh, then retain native screenshots.
            // Looking through each windscreen must not intersect a visible exterior
            // panel. MeshCollider.Raycast honours the same one-sided triangle faces
            // as rasterisation; these colliders exist only in the disposable proof.
            var eye = car.transform.TransformPoint(car.Visual.CockpitEye);
            var ray = new Ray(eye, car.transform.forward);
            int blockers = 0;
            foreach (var filter in car.Visual.GetComponentsInChildren<MeshFilter>())
            {
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                if (collider.Raycast(ray, out var hit, 4)) blockers++;
                Destroy(collider);
            }
            Check(blockers == 0, "Driver eye has an unobstructed forward windscreen ray through the actual VW meshes");
            session.SetMenu(false);session.enabled=false;car.enabled=false;car.Body.useGravity=false;
            session.View.Preview=null;session.View.enabled=true;
            foreach (int mode in new[]{2,1,5,0})
            {
                session.View.Mode=mode;
                for(int frame=0;frame<20;frame++){Hold(car,parked,heading,0);yield return null;}
                if(mode==1||mode==5)
                {
                    float front=car.transform.InverseTransformPoint(session.View.transform.position).z;
                    Check(front>=car.Visual.FrontCameraExtent+.14f, $"Camera mode {mode} sits beyond the complete front mesh including badge");
                }
                ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"vw-camera-"+mode+".png"));
                yield return new WaitForEndOfFrame();yield return null;
            }
            session.View.enabled=false;

            var audio=FindFirstObjectByType<DrivingAudio>();audio.Input=null;audio.Profile.volume=.65f;
            car.NetworkDriven=true;car.NetworkThrottle=1;car.StartLocked=false;
            session.SetMenu(false);session.enabled=false;car.enabled=false;car.Body.useGravity=false;
            yield return null;yield return null;
            Check(audio.UsesRecordedEngine,"Camper uses recorded VW audio in the actual driving scene");
            var recorded=audio.GetComponentInChildren<CamperRecordedEngine>();
            var sources=recorded.GetComponentsInChildren<AudioSource>();
            Check(sources.Length==2&&sources.All(s=>s.clip&&s.loop),"Both recorded clips are loaded and looping");
            var probes=sources.Select(s=>s.gameObject.AddComponent<BetaRecordedAudioProbe>()).ToArray();
            var cases=new[]{0f,20f,35f,38f,38.8f,39f,39.2f,40f,42f,45f,50f,55f,60f};
            foreach(float throttle in new[]{1f,0f})foreach(float mph in cases)
            {
                car.NetworkThrottle=throttle;
                double start=Time.realtimeSinceStartupAsDouble;
                while(Time.realtimeSinceStartupAsDouble-start<.32)
                {Hold(car,parked,heading,mph);yield return null;}
                foreach(var probe in probes)probe.ResetWindow();
                start=Time.realtimeSinceStartupAsDouble;
                // A short waveform slice varies with combustion phase/recording
                // envelope. Compare a complete loaded clip around the old wrap,
                // keeping the same strict output threshold rather than accepting
                // an arbitrary dip from a 140 ms slice.
                bool thresholdBand=throttle==1&&mph>=35&&mph<=45;
                double captureSeconds=thresholdBand?sources.Max(source=>source.clip.length/source.pitch)+.1:.25;
                while(Time.realtimeSinceStartupAsDouble-start<captureSeconds)
                {Hold(car,parked,heading,mph);yield return null;}
                double power=0;long frames=0;
                for(int index=0;index<probes.Length;index++)
                {var energy=probes[index].Energy(out long count);frames+=count;power+=count>0?energy/count*sources[index].volume*sources[index].volume:0;}
                double rms=Math.Sqrt(power);
                samples.Add(new JObject{["mph"]=mph,["throttle"]=throttle,["rpm"]=audio.EngineRpm,["rms"]=rms,["dspSamples"]=frames,
                    ["idleGain"]=sources[0].volume,["loadGain"]=sources[1].volume,["allPlaying"]=sources.All(s=>s.isPlaying)});
                Check(frames>0&&rms>.005&&sources.All(s=>s.isPlaying),$"Recorded output remains audible at {mph} mph / throttle {throttle}");
            }
            var band=samples.OfType<JObject>().Where(s=>(float)s["throttle"]==1&&(float)s["mph"]>=35&&(float)s["mph"]<=45).ToArray();
            double minimum=band.Min(s=>(double)s["rms"]),maximum=band.Max(s=>(double)s["rms"]);
            Check(minimum/maximum>.7,"No major output collapse across the former 39–40 mph wrap");
            result["thresholdBandMinimumRms"]=minimum;result["thresholdBandMaximumRms"]=maximum;
            // Remain above the threshold beyond a complete source-loop duration.
            double loopStart=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-loopStart<6.5){Hold(car,parked,heading,45);yield return null;}
            Check(sources.All(s=>s.isPlaying)&&sources.Sum(s=>s.volume)>.1f,"Recorded engine continues beyond a full loop at 45 mph");
            Time.timeScale=0;yield return null;yield return null;
            Check(sources.All(s=>s.volume==0),"Pause still mutes recorded engine immediately");
        }
        static void Hold(VehicleController car,Vector3 position,Quaternion rotation,float mph)
        {car.Body.position=position;car.Body.rotation=rotation;car.Body.linearVelocity=rotation*Vector3.forward*(mph/DrivingMath.MphPerMetreSecond);car.Body.angularVelocity=Vector3.zero;}
        static float FlatDistance(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    }

    /// <summary>Reads Unity's actual source DSP buffers without allocating on the
    /// audio thread. Gain weighting is measured separately on the main thread.</summary>
    public sealed class BetaRecordedAudioProbe : MonoBehaviour
    {
        readonly object gate=new object();double energy;long count;
        void OnAudioFilterRead(float[] data,int channels)
        {double sum=0;foreach(float sample in data)sum+=sample*sample;lock(gate){energy+=sum;count+=data.Length;}}
        public void ResetWindow(){lock(gate){energy=0;count=0;}}
        public double Energy(out long samples){lock(gate){samples=count;return energy;}}
    }
}
