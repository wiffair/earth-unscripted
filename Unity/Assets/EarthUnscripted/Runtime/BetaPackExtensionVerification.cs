using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EarthUnscripted.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    // Explicit disposable-profile integration proof. Uses the real import UI,
    // purchase transaction, selected car, audio and world; never runs normally.
    public sealed class BetaPackExtensionVerification:MonoBehaviour
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        readonly JObject receipt=new JObject();
        GameSession session;DrivingUI ui;DrivingAudio audio;RecordedPackEngine recorded;
        bool saved,restored,handedOff,oldInput,oldUi,oldView,oldEnvironment,oldMenu;
        float oldEngineVolume,oldWiperRain,oldFov,oldPreviewYaw;
        int oldMode;Vector3 oldCameraPosition;Quaternion oldCameraRotation;Transform oldPreview;
        AudioConfiguration audioConfiguration;Radio.CarRadio radio;string radioControls;
        string fixture,expectedHash,configurationError;int expectedCost;
        object Get(object owner,string field)=>owner.GetType().GetField(field,Hidden).GetValue(owner);
        static string Argument(string name)
        {var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,name);if(at<0||at+1>=args.Length)throw new ArgumentException(name+" is required");return args[at+1];}
        void Awake()
        {
            Application.logMessageReceived+=Log;
            try
            {
                RequireIsolated();fixture=Path.GetFullPath(Argument("-earthUnscriptedVehiclePackFixture"));expectedHash=Argument("-earthUnscriptedVehiclePackExpectedSha256").ToLowerInvariant();
                if(expectedHash.Length!=64||expectedHash.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Expected pack SHA256 must contain64 hex digits");
                if(!int.TryParse(Argument("-earthUnscriptedVehiclePackExpectedCost"),NumberStyles.None,CultureInfo.InvariantCulture,out expectedCost)||expectedCost<=0||expectedCost>10000000)throw new ArgumentException("A positive affordable protocol cost is required");
                if(!float.TryParse(Argument("-earthUnscriptedBetaDriveMetres"),NumberStyles.Float,CultureInfo.InvariantCulture,out float metres)||metres<1200||metres>3000)throw new ArgumentException("Ordinary drive handoff requires an explicit1200–3000 metre smoke route");
            }
            catch(Exception error){configurationError=error.ToString();}
        }
        void RequireIsolated()
        {
            if(!SinglePlayerBeta.Enabled||SinglePlayerBeta.VerificationOutput==null)throw new InvalidOperationException("Explicit isolated beta smoke is required");
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"-earthUnscriptedSharedProfile")>=0)throw new InvalidOperationException("Pack extension proof requires a fresh profile, not a shared profile");
            if(!Guid.TryParseExact(Argument("-earthUnscriptedBetaSmoke"),"N",out var token))throw new ArgumentException("Fresh verification token required");
            string output=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"EarthUnscriptedBetaSmoke-"+token.ToString("N")));
            if(Path.GetFullPath(SinglePlayerBeta.VerificationOutput)!=output||Path.GetFullPath(SinglePlayerBeta.DataRoot)!=Path.Combine(output,"BetaData"))throw new InvalidOperationException("Disposable beta data paths are inconsistent");
            if(Path.GetFullPath(PlayerProfile.StoragePath)!=Path.Combine(output,"BetaData","beta-save.json")||Path.GetFullPath(VehiclePackLibrary.Folder)!=Path.Combine(output,"BetaData","VehiclePacks"))throw new InvalidOperationException("Save/import library escaped disposable profile");
        }
        void Log(string message,string stack,LogType type){if((type==LogType.Error||type==LogType.Exception)&&errors.Count<30)errors.Add(message+"\n"+stack);}
        void Require(bool value,string message)
        {
            if(SinglePlayerBeta.VerificationOutput!=null)File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"pack-extension-progress.json"),new JObject{{"check",message},{"passed",value}}.ToString());
            if(!value)throw new InvalidOperationException(message);checks.Add(message);
        }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Exercise());string failure=null;
            while(stack.Count>0)
            {
                bool more=false;object current=null;
                try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}
                catch(Exception error){failure=error.ToString();}
                if(failure!=null)break;if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            while(stack.Count>0)try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception error){failure=failure??error.ToString();}
            try{Restore();}catch(Exception error){failure=failure??error.ToString();}
            bool passed=failure==null&&errors.Count==0;
            receipt["passed"]=passed;receipt["failure"]=failure??"";receipt["checks"]=new JArray(checks);receipt["errors"]=new JArray(errors);receipt["version"]=Application.version;receipt["graphics"]=GameGraphics.Name;
            receipt["fixtureSha256"]=expectedHash;receipt["expectedCost"]=expectedCost;receipt["handoffToOrdinaryDrive"]=passed;
            if(SinglePlayerBeta.VerificationOutput!=null)
            {
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"pack-extension-checks.json"),receipt.ToString());
                if(!passed)File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),receipt.ToString());
            }
            if(!passed){Application.Quit(1);yield break;}
            handedOff=true;Application.logMessageReceived-=Log;
            new GameObject("Ordinary beta drive after pack extension checks").AddComponent<BetaSmokeVerification>();
            Destroy(gameObject);
        }
        static string Hash(string path){using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
        IEnumerator Exercise()
        {
            if(configurationError!=null)throw new InvalidOperationException(configurationError);
            double began=Time.realtimeSinceStartupAsDouble;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
            {
                if(SinglePlayerBeta.StartupError!=null)throw new IOException(SinglePlayerBeta.StartupError);
                if(session&&session.BootFailed)throw new IOException(session.BootStatus);
                if(Time.realtimeSinceStartupAsDouble-began>600)throw new TimeoutException("Pack extension world boot timed out");yield return null;
            }
            RequireIsolated();Require(FindObjectsByType<BetaSmokeVerification>(FindObjectsSortMode.None).Length==0,"Ordinary driver has not started concurrently");
            Require(session.World.AutomaticWorld&&session.World.AutomaticWorld.Ready&&!session.Car.NetworkDriven,"Real automatic world and normal human car booted");
            Require(!AudioListener.pause,"Live audio proof requires unpaused audio; omit -earthUnscriptedSilent");
            var builtins=GarageRules.Catalogue.Take(ImportedVehicleCatalogue.BuiltInCount).ToArray();
            var validation=Task.Run(()=>{if(Hash(fixture)!=expectedHash)throw new InvalidDataException("Fixture SHA256 mismatch before manifest use");var pack=VehiclePackFormat.Read(fixture);if(pack.ArchiveHash!=expectedHash)throw new InvalidDataException("Fixture changed during validation");return pack;});
            while(!validation.IsCompleted)yield return null;var specification=validation.GetAwaiter().GetResult();
            Require(specification.Settings.Price==expectedCost&&specification.EngineAudio!=null&&specification.Wipers!=null&&specification.Materials.Any(m=>m.Finish!=null),"Verified generic fixture declares expected cost, recorded engine, finish and authored wipers");
            string stableId=specification.Settings.Id;Require(ImportedVehicleCatalogue.Find(stableId)==null,"External fixture is not preactivated at boot; menu import must admit it");Require(!session.Profile.garage.cars.Any(c=>c.packVehicleId==stableId),"Fresh profile does not already own the fixture car");
            ui=FindFirstObjectByType<DrivingUI>();Require(ui,"Main driving UI is present");
            var menu=(VehiclePackMenu)Get(ui,"vehiclePacks");Require(menu!=null,"Using the main UI vehicle-pack menu");
            oldMenu=session.Menu;oldInput=session.Input.enabled;oldUi=ui.enabled;oldView=session.View.enabled;oldEnvironment=session.Conditions.enabled;oldEngineVolume=session.Profile.engineVolume;oldWiperRain=session.Car.Visual.WiperRain;
            oldMode=session.View.Mode;oldPreview=session.View.Preview;oldPreviewYaw=session.View.PreviewYaw;oldCameraPosition=session.View.transform.position;oldCameraRotation=session.View.transform.rotation;oldFov=session.View.GetComponent<Camera>().fieldOfView;
            audioConfiguration=AudioSettings.GetConfiguration();radio=session.GetComponent<Radio.CarRadio>();radioControls=RadioControls(radio);saved=true;
            session.SetMenu(true);session.Input.enabled=false;session.Input.Throttle=session.Input.Steer=0;session.Input.Brake=1;ui.enabled=false;
            menu.ImportArchive(fixture);var importing=(Task<VehiclePack>)Get(menu,"import");Require(importing!=null,"ImportArchive queued normal menu validation/install");began=Time.realtimeSinceStartupAsDouble;
            while(!importing.IsCompleted){menu.Poll();if(Time.realtimeSinceStartupAsDouble-began>90)throw new TimeoutException("Vehicle pack import timed out");yield return null;}
            var imported=importing.GetAwaiter().GetResult();Require(imported.ArchiveHash==expectedHash&&imported.Settings.Id==stableId,"Menu worker imported the exact verified fixture");menu.Poll();
            var entry=ImportedVehicleCatalogue.Find(stableId);var activePack=entry==null?null:VehiclePackLibrary.ForModel(entry.Model);
            Require(entry!=null&&activePack!=null&&activePack.ArchiveHash==expectedHash&&activePack.Settings.Price==expectedCost,"Menu Poll exposes the exact stable-ID pack");
            Require(builtins.Select((value,index)=>ReferenceEquals(value,GarageRules.Catalogue[index])).All(value=>value),"Built-in catalogue entries and indexes remain unchanged");
            session.Economy.Spend(session.Economy.Credits);session.Economy.AddCredits(expectedCost-1);session.Profile.credits=session.Economy.Credits;Require(session.TrySave(),"Insufficient-credit private profile checkpoint saved");
            string oldOwned=session.Profile.garage.activeId;int ownedCount=session.Profile.garage.cars.Count;int oldModel=session.Car.Metrics.Model;
            Require(!session.SelectPackVehicle(entry.Model)&&session.Notice.Contains("Not enough credits"),"Actual selection transaction refuses insufficient credits explicitly");
            Require(session.Economy.Credits==expectedCost-1&&session.Profile.credits==expectedCost-1&&session.Profile.garage.activeId==oldOwned&&session.Profile.garage.cars.Count==ownedCount&&session.Car.Metrics.Model==oldModel,"Refusal preserves money, active car and garage");
            session.Economy.AddCredits(1);session.Profile.credits=session.Economy.Credits;int before=session.Economy.Credits;
            Require(session.SelectPackVehicle(entry.Model),"Actual purchase selects pack car on supported real-world road: "+session.Notice);
            Require(before-session.Economy.Credits==expectedCost&&session.Profile.credits==session.Economy.Credits,"Actual purchase charged the exact CLI-specified cost");
            var owned=session.Profile.garage.Active;Require(owned.packVehicleId==stableId&&owned.model==entry.Model&&session.Car.Metrics.Model==entry.Model,"Selected physical car is bound to the stable pack ID");
            var probe=owned.Clone();probe.model=0;ImportedVehicleCatalogue.ResolveSavedCar(probe);Require(probe.model==entry.Model&&probe.packVehicleId==stableId,"Stable saved identity resolves independently of stored numeric model");
            var persisted=PlayerProfile.Load();Require(persisted.garage.Active.packVehicleId==stableId&&persisted.garage.Active.model==entry.Model&&persisted.credits==session.Economy.Credits,"Actual saved private profile reload retains purchase and stable identity");
            receipt["stableVehicleId"]=stableId;receipt["model"]=entry.Model;receipt["packVersion"]=entry.Settings.Version;receipt["packRevision"]=entry.Settings.Revision;receipt["chargedCredits"]=before-session.Economy.Credits;receipt["creditsBeforePurchase"]=before;receipt["creditsAfterPurchase"]=session.Economy.Credits;
            // Rebuild retires the former visual at the end of the frame.
            yield return null;
            var appearance=session.Car.Visual.GetComponentInChildren<VehiclePackAppearance>();Require(appearance,"Selected car uses actual imported appearance");CheckFinish(appearance,activePack);CheckWipers(appearance,activePack);
            audio=session.GetComponent<DrivingAudio>();Require(audio&&ReferenceEquals(audio.Car,session.Car),"Main DrivingAudio owns selected physical car");
            began=Time.realtimeSinceStartupAsDouble;while(!(recorded=audio.GetComponentInChildren<RecordedPackEngine>())||!recorded.Ready){if(Time.realtimeSinceStartupAsDouble-began>10)throw new TimeoutException("Recorded pack audio did not configure");yield return null;}
            Require(audio.UsesRecordedEngine&&ReferenceEquals(Get(audio,"enginePack"),activePack),"Main DrivingAudio selected the pack's recorded engine");
            session.Profile.engineVolume=Mathf.Max(.25f,oldEngineVolume);session.SetMenu(false);yield return new WaitForSecondsRealtime(.65f);
            var voices=recorded.GetComponentsInChildren<AudioSource>();Require(voices.Length==2&&voices.All(v=>v.clip&&v.loop&&v.isPlaying&&v.spatialBlend==0),"Exactly two owned recorded engine voices loop in the real session");
            var clips=voices.Select(v=>v.clip).ToArray();CheckVoice(voices.Single(v=>v.name=="Pack recorded exhaust"),activePack.EngineAudio.Exhaust);CheckVoice(voices.Single(v=>v.name=="Pack recorded mechanical"),activePack.EngineAudio.Mechanical);
            Require(voices.Any(v=>v.volume>0),"Recorded engine responds to nonzero engine gain");
            session.Profile.engineVolume=0;yield return new WaitForSecondsRealtime(.65f);Require(audio.UsesRecordedEngine&&voices.All(v=>v.volume<.0001f&&v.isPlaying),"Engine volume zero silences both voices without replacing playback");
            session.Profile.engineVolume=Mathf.Max(.25f,oldEngineVolume);yield return new WaitForSecondsRealtime(.3f);session.SetMenu(true);yield return new WaitForSecondsRealtime(.15f);
            var paused=voices.Select(v=>v.timeSamples).ToArray();Require(voices.All(v=>!v.isPlaying&&v.volume==0),"Production menu pause pauses both owned loops");yield return new WaitForSecondsRealtime(.2f);Require(voices.Select(v=>v.timeSamples).SequenceEqual(paused),"Paused loop cursors remain stationary");
            session.SetMenu(false);var advanced=new bool[2];began=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-began<.35){for(int i=0;i<2;i++)advanced[i]|=voices[i].timeSamples!=paused[i];yield return null;}
            Require(voices.Select((v,index)=>v.clip==clips[index]&&v.isPlaying&&advanced[index]).All(value=>value),"Menu resume continues the same owned clips");
            receipt["engineVoices"]=new JArray(voices.Select((v,index)=>new JObject{{"clip",v.clip.name},{"samples",v.clip.samples},{"frequency",v.clip.frequency},{"loop",v.loop},{"pausedCursor",paused[index]},{"resumedCursor",v.timeSamples}}));
            Require(SameAudioConfiguration(audioConfiguration,AudioSettings.GetConfiguration())&&ReferenceEquals(radio,session.GetComponent<Radio.CarRadio>())&&radioControls==RadioControls(radio),"Engine selection/gain/pause leaves radio controls and global DSP configuration unchanged");
            var camera=session.View.GetComponent<Camera>();session.View.enabled=false;camera.transform.position=session.Car.transform.TransformPoint(new Vector3(5,2.6f,6));camera.transform.rotation=Quaternion.LookRotation(session.Car.transform.TransformPoint(new Vector3(0,1,0))-camera.transform.position,Vector3.up);camera.fieldOfView=50;
            yield return Capture("pack-extension-front.png");session.View.Mode=2;session.View.Preview=null;session.View.enabled=true;yield return new WaitForSecondsRealtime(.4f);yield return Capture("pack-extension-driver.png");
            session.Conditions.enabled=false;var wipers=session.Car.Visual.Wipers;var rotations=wipers.Select(w=>w.localRotation).ToArray();var movement=new float[2];session.Car.Visual.WiperRain=1;began=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-began<1.1){for(int i=0;i<2;i++)movement[i]=Mathf.Max(movement[i],Quaternion.Angle(wipers[i].localRotation,rotations[i]));yield return null;}
            Require(movement.All(angle=>angle>5),"Both authored blades move through actual CarVisual sensor-driven animation");receipt["authoredWiperMovementDegrees"]=new JArray(movement);session.Car.Visual.WiperRain=oldWiperRain;session.Conditions.enabled=oldEnvironment;
            CheckWipers(appearance,activePack);Require(session.Car==audio.Car&&session.Profile.garage.Active.packVehicleId==stableId,"Same selected car is ready for ordinary smoke handoff");
            Require(errors.Count==0,"No runtime errors during pack extension checks");
        }
        static string RadioControls(Radio.CarRadio radio)
        {if(!radio)return "none";var s=radio.Settings;return s.station+"|"+s.volume.ToString("R",CultureInfo.InvariantCulture)+"|"+s.music.ToString("R",CultureInfo.InvariantCulture)+"|"+s.speech.ToString("R",CultureInfo.InvariantCulture)+"|"+s.jingles.ToString("R",CultureInfo.InvariantCulture);}
        static bool SameAudioConfiguration(AudioConfiguration a,AudioConfiguration b)=>a.sampleRate==b.sampleRate&&a.dspBufferSize==b.dspBufferSize&&a.speakerMode==b.speakerMode&&a.numRealVoices==b.numRealVoices&&a.numVirtualVoices==b.numVirtualVoices;
        void CheckVoice(AudioSource source,VehiclePackPcmData pcm)
        {
            Require(source.clip.samples==pcm.Samples.Length&&source.clip.frequency==pcm.SampleRate,"Owned PCM voice matches declared recording: "+source.name);var sample=new float[Mathf.Min(64,pcm.Samples.Length)];Require(source.clip.GetData(sample,0)&&sample.Select((value,index)=>Mathf.Abs(value-pcm.Samples[index])<.00001f).All(value=>value),"Owned PCM clip contains admitted samples: "+source.name);
        }
        void CheckFinish(VehiclePackAppearance appearance,VehiclePack pack)
        {
            var materials=(List<Material>)Get(appearance,"materials");int count=0;
            for(int i=0;i<pack.Materials.Length;i++)
            {
                var f=pack.Materials[i].Finish;if(f==null||!pack.Parts.Any(part=>part.Material==i))continue;count++;var m=materials[i];
                Require(appearance.GetComponentsInChildren<Renderer>(true).Any(renderer=>renderer.sharedMaterial==m),"Finished material is bound to admitted selected geometry "+i);
                Require(m.GetFloat("_CandyEnabled")==1&&m.GetVector("_CandyTransmission").Equals(new Vector4(f.Transmission.x,f.Transmission.y,f.Transmission.z,0))&&m.GetFloat("_CandyThickness")==f.Thickness&&m.GetFloat("_CandyFlakeStrength")==f.FlakeStrength&&m.GetFloat("_CandyFlakeScale")==f.FlakeScale&&m.GetFloat("_CandyClearcoat")==f.Clearcoat,"Selected appearance retains declared layered finish "+i);
            }
            Require(count>0,"At least one actual selected material uses the optional finish");receipt["finishedMaterialCount"]=count;
        }
        void CheckWipers(VehiclePackAppearance appearance,VehiclePack pack)
        {
            var visual=session.Car.Visual;Require(appearance.Wipers.Length==2&&ReferenceEquals(appearance.Wipers,visual.Wipers),"Selected visual uses exactly the two authored pack pivots");
            var travel=visual.GetComponentsInChildren<WindshieldWiperTravel>(true);Require(travel.Length==2&&travel.All(t=>visual.Wipers.Contains(t.transform)),"No generated duplicate wiper mechanism exists");
            for(int i=0;i<2;i++)Require(appearance.Wipers[i].localPosition.Equals(pack.Wipers.Pivots[i])&&appearance.Wipers[i].GetComponent<WindshieldWiperTravel>().ParkAngle==0&&appearance.Wipers[i].GetComponent<WindshieldWiperTravel>().EndAngle==pack.Wipers.SweepDegrees[i]&&appearance.Wipers[i].GetComponentsInChildren<MeshFilter>(true).Length==pack.Parts.Count(p=>p.Attachment=="wiper"+i),"Authored pivot/travel/attached meshes retained for blade "+i);
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.6f);
            yield return new WaitForEndOfFrame();string path=Path.Combine(SinglePlayerBeta.VerificationOutput,name);ScreenCapture.CaptureScreenshot(path);yield return new WaitForEndOfFrame();double began=Time.realtimeSinceStartupAsDouble;
            while(!File.Exists(path)||new FileInfo(path).Length==0){if(Time.realtimeSinceStartupAsDouble-began>10)throw new TimeoutException("Screenshot unavailable: "+name);yield return null;}checks.Add("Captured main-world production-lighting view: "+name);
        }
        void Restore()
        {
            if(!saved||restored||!session)return;restored=true;session.Profile.engineVolume=oldEngineVolume;session.Conditions.enabled=oldEnvironment;session.Car.Visual.WiperRain=oldWiperRain;
            session.Input.Throttle=session.Input.Brake=session.Input.Steer=0;session.Input.enabled=oldInput;ui.enabled=oldUi;
            session.View.Mode=oldMode;session.View.Preview=oldPreview;session.View.PreviewYaw=oldPreviewYaw;session.View.transform.SetPositionAndRotation(oldCameraPosition,oldCameraRotation);session.View.GetComponent<Camera>().fieldOfView=oldFov;session.View.enabled=oldView;session.SetMenu(oldMenu);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;if(!handedOff)Restore();}
    }
}
