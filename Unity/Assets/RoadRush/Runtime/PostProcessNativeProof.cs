using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace RoadRush
{
    // Opt-in, isolated profile; synchronous same-frame A/B never advances simulation.
    public sealed class PostProcessNativeProof : MonoBehaviour
    {
        const string Flag="-roadRushPostProcessProof";
        static string prefix,realSave,realHash;static bool previewOff;
        const double WatchdogSeconds=240;
        readonly List<string> firstErrors=new List<string>();int runtimeErrors;bool completed;double deadline;Report activeReport;
        void OnEnable(){deadline=Time.realtimeSinceStartupAsDouble+WatchdogSeconds;Application.logMessageReceived+=RecordError;}
        void OnDestroy(){Application.logMessageReceived-=RecordError;}
        void RecordError(string message,string trace,LogType kind)
        {
            if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;
            if(runtimeErrors<int.MaxValue)runtimeErrors++;if(firstErrors.Count<16){string line=message??"";firstErrors.Add(line.Substring(0,Math.Min(1600,line.Length)));}
        }
        void Update(){if(!completed&&Time.realtimeSinceStartupAsDouble>=deadline)Finish(activeReport??new Report{status="FAIL"},"Real-time postprocess proof watchdog expired after240seconds");}
        void Finish(Report report,string failure=null)
        {
            if(completed)return;completed=true;
            if(failure!=null){report.status="FAIL";report.error=failure;}
            report.runtimeErrorCount=runtimeErrors;report.firstRuntimeErrors=firstErrors.ToArray();report.runInBackground=Application.runInBackground;
            if(runtimeErrors>0)report.status="FAIL";
            foreach(var field in typeof(Report).GetFields(BindingFlags.Instance|BindingFlags.Public))
                if(field.FieldType==typeof(float)&&!Finite((float)field.GetValue(report))){field.SetValue(report,0f);report.status="FAIL";report.error="Nonfinite diagnostic metric: "+field.Name;}
            if(report.error!=null&&report.error.Length>4096)report.error=report.error.Substring(0,4096);
            try{File.WriteAllText(prefix+".json",JsonUtility.ToJson(report,true));}
            finally{Application.Quit(report.status=="PASS"?0:1);}
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,Flag);if(at<0||at+1>=args.Length)return;
            Application.runInBackground=true;
            prefix=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(Path.GetDirectoryName(prefix));previewOff=Array.IndexOf(args,"-roadRushPostProcessOff")>=0;
            realSave=Path.Combine(Application.persistentDataPath,"road-rush-save.json");realHash=File.Exists(realSave)?Core.DataPackageRules.HashFile(realSave):null;
            PlayerProfile.StorageOverride=prefix+"-profile.json";
            var data=new PlayerProfile{region="spa",regionTimeZone="Europe/Brussels",quality=2,clarity=0,volume=0,rumble=0,weatherMode=0,clockMode=0,fixedHour=14};data.Save();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch(){if(!string.IsNullOrEmpty(prefix))new GameObject("Isolated postprocess native proof").AddComponent<PostProcessNativeProof>();}
        [Serializable] sealed class Report
        {
            public string status,error,version,graphicsDevice,graphicsAPI,renderer,profile,limitations;
            public bool rendererPostProcessEnabled,cameraPostProcessing,cameraHDR,pipelineHDR,realCareerUnchanged,sameFrameState,uiEnabled,previewOff,runInBackground;
            public float hour,rain,wetness,exposure,bloom,vignette,meanDifference,toneOnlyMeanDifference,offClippedFraction,onClippedFraction;
            public int width,height,quality,frameCount,changedPixels,uiCount,bodyCount,runtimeErrorCount;public string tonemapping;public string[] firstRuntimeErrors;public StageVolume[] volumeStages;
        }
        [Serializable] sealed class StageVolume
        {
            public string stage,expectedMode,modeBefore,modeAfter,trigger;
            public int frameBefore,frameAfter,layerMask;public bool forcedUpdate,usesCameraStack;
        }
        static Texture2D Capture(Camera camera,UniversalAdditionalCameraData extra,bool on,int width,int height,string label,TonemappingMode expectedMode,List<StageVolume> stages)
        {
            bool before=extra.renderPostProcessing;var active=RenderTexture.active;var manager=VolumeManager.instance;var previousStack=manager.stack;var requestStack=extra.volumeStack??previousStack;
            var trigger=extra.volumeTrigger?extra.volumeTrigger:camera.transform;
            var stage=new StageVolume{stage=label,expectedMode=expectedMode.ToString(),frameBefore=Time.frameCount,layerMask=extra.volumeLayerMask.value,trigger=trigger.name,usesCameraStack=extra.volumeStack!=null};stages.Add(stage);
            var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var image=new Texture2D(width,height,TextureFormat.RGB24,false,false);
            try
            {
                // URP SingleCameraRequest calls RenderSingleCameraInternal directly and
                // omits UpdateVolumeFramework; explicitly blend this camera's real volumes.
                if(requestStack==null)throw new InvalidOperationException("Volume stack not initialized");
                manager.Update(requestStack,trigger,extra.volumeLayerMask);manager.stack=requestStack;stage.forcedUpdate=true;
                stage.modeBefore=requestStack.GetComponent<Tonemapping>().mode.value.ToString();
                if(stage.modeBefore!=stage.expectedMode)throw new Exception("Requested tone not blended before "+label+": "+stage.modeBefore);
                extra.renderPostProcessing=on;var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
                if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new InvalidOperationException("URP single-camera render requests unsupported");
                RenderPipeline.SubmitRenderRequest(camera,request);stage.modeAfter=manager.stack.GetComponent<Tonemapping>().mode.value.ToString();stage.frameAfter=Time.frameCount;
                if(stage.modeAfter!=stage.expectedMode||stage.frameAfter!=stage.frameBefore)throw new Exception("Tone/frame changed inside "+label);
                RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0,false);image.Apply(false,false);return image;
            }
            catch{Destroy(image);throw;}
            finally{extra.renderPostProcessing=before;manager.stack=previousStack;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}
        }
        IEnumerator Start()
        {
            Application.runInBackground=true;GameSession session=null;double sessionDeadline=Time.realtimeSinceStartupAsDouble+40;
            while(!(session=FindAnyObjectByType<GameSession>())||!session.BootReady||!session.View||!session.Conditions){if(completed)yield break;if(Time.realtimeSinceStartupAsDouble>sessionDeadline){Finish(new Report{status="FAIL"},"game session timeout");yield break;}yield return null;}
            for(int i=0;i<5;i++){if(completed)yield break;yield return null;}if(completed)yield break;
            var report=new Report{status="FAIL",version=Application.version,graphicsDevice=SystemInfo.graphicsDeviceName,graphicsAPI=SystemInfo.graphicsDeviceType.ToString(),profile=PlayerProfile.StoragePath,previewOff=previewOff,limitations="Synchronous same-frame single-camera world render requests exclude IMGUI. URP SingleCameraRequest omits UpdateVolumeFramework, so this diagnostic explicitly blends this camera's trigger/layer mask into its active stack before every request, records/asserts stack mode before/after, and restores the previous stack reference. A third render holds PP/AA/bloom/vignette enabled and toggles only the owned tone mode None versus ACES; its pixel difference checks actual tone effect. UI component state is checked and an ordinary screen capture is saved separately. This is render-path/pixel verification, not a performance measurement or proof of preferred colour style. No simulation/lighting/weather/clock parameters are changed; a separate isolated profile selects the existing Spa daylight fixture."};
            var stages=new List<StageVolume>();activeReport=report;Texture2D off=null,on=null,toneNone=null;var camera=session.View.GetComponent<Camera>();var extra=camera.GetComponent<UniversalAdditionalCameraData>();bool original=extra.renderPostProcessing;
            try
            {
                if(Path.GetFullPath(PlayerProfile.StoragePath)!=prefix+"-profile.json")throw new Exception("Unisolated proof profile");
                extra.renderPostProcessing=!previewOff;var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var renderer=extra.scriptableRenderer;var field=typeof(UniversalRenderer).GetProperty("postProcessEnabled",BindingFlags.Instance|BindingFlags.NonPublic);
                report.renderer=renderer?.GetType().Name;report.rendererPostProcessEnabled=renderer is UniversalRenderer&&field!=null&&(bool)field.GetValue(renderer);
                report.cameraPostProcessing=extra.renderPostProcessing;report.cameraHDR=camera.allowHDR;report.pipelineHDR=pipeline&&pipeline.supportsHDR;report.quality=session.Profile.quality;report.width=1280;report.height=800;report.frameCount=Time.frameCount;
                var ui=FindObjectsByType<DrivingUI>(FindObjectsSortMode.None);report.uiCount=ui.Length;report.uiEnabled=ui.Length==1&&ui[0].enabled;report.bodyCount=FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
                var body=session.Car.Body;var position=body.position;var rotation=body.rotation;var velocity=body.linearVelocity;var angular=body.angularVelocity;float fixedTime=Time.fixedTime,time=Time.time,scale=Time.timeScale,hour=session.Conditions.Hour,rain=session.Conditions.Rain,wetness=session.Conditions.Wetness;var cp=camera.transform.position;var cr=camera.transform.rotation;float fov=camera.fieldOfView;bool hdr=camera.allowHDR;var target=camera.targetTexture;var sun=session.World.Sun;var sunRotation=sun.transform.rotation;float sunIntensity=sun.intensity;
                off=Capture(camera,extra,false,report.width,report.height,"postprocessing-off",TonemappingMode.ACES,stages);
                var atmosphere=session.World.GetComponentInChildren<WorldAtmosphere>();if(!atmosphere||!atmosphere.OwnedProfile.TryGet<Tonemapping>(out var ownedTone))throw new Exception("Owned tone component missing");
                var originalMode=ownedTone.mode.value;
                try{ownedTone.mode.Override(TonemappingMode.None);toneNone=Capture(camera,extra,true,report.width,report.height,"postprocessing-on-tone-none",TonemappingMode.None,stages);}
                finally{ownedTone.mode.Override(originalMode);VolumeManager.instance.Update(extra.volumeStack??VolumeManager.instance.stack,extra.volumeTrigger?extra.volumeTrigger:camera.transform,extra.volumeLayerMask);}
                on=Capture(camera,extra,true,report.width,report.height,"postprocessing-on-aces",TonemappingMode.ACES,stages);
                var stack=VolumeManager.instance.stack;var tone=stack.GetComponent<Tonemapping>();report.tonemapping=tone.mode.value.ToString();report.exposure=stack.GetComponent<ColorAdjustments>().postExposure.value;report.bloom=stack.GetComponent<Bloom>().intensity.value;report.vignette=stack.GetComponent<Vignette>().intensity.value;report.hour=hour;report.rain=rain;report.wetness=wetness;
                report.sameFrameState=report.frameCount==Time.frameCount&&body.position==position&&body.rotation==rotation&&body.linearVelocity==velocity&&body.angularVelocity==angular&&Time.fixedTime==fixedTime&&Time.time==time&&Time.timeScale==scale&&session.Conditions.Hour==hour&&session.Conditions.Rain==rain&&session.Conditions.Wetness==wetness&&camera.transform.position==cp&&camera.transform.rotation==cr&&camera.fieldOfView==fov&&camera.allowHDR==hdr&&camera.targetTexture==target&&sun.transform.rotation==sunRotation&&sun.intensity==sunIntensity&&ui.Length==FindObjectsByType<DrivingUI>(FindObjectsSortMode.None).Length&&ui[0].enabled&&report.bodyCount==FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
                var a=off.GetPixels32();var b=on.GetPixels32();var neutral=toneNone.GetPixels32();double toneDifference=0;for(int i=0;i<b.Length;i++)toneDifference+=Math.Abs(neutral[i].r-b[i].r)+Math.Abs(neutral[i].g-b[i].g)+Math.Abs(neutral[i].b-b[i].b);report.toneOnlyMeanDifference=(float)(toneDifference/(b.Length*3*255));double total=0;int clippedOff=0,clippedOn=0,changed=0;for(int i=0;i<a.Length;i++){int delta=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);total+=delta;if(delta>6)changed++;if(a[i].r>=253&&a[i].g>=253&&a[i].b>=253)clippedOff++;if(b[i].r>=253&&b[i].g>=253&&b[i].b>=253)clippedOn++;}
                report.meanDifference=(float)(total/(a.Length*3*255));report.changedPixels=changed;report.offClippedFraction=clippedOff/(float)a.Length;report.onClippedFraction=clippedOn/(float)a.Length;
                File.WriteAllBytes(prefix+"-tone-none.png",toneNone.EncodeToPNG());File.WriteAllBytes(prefix+"-off.png",off.EncodeToPNG());File.WriteAllBytes(prefix+"-on.png",on.EncodeToPNG());
                report.realCareerUnchanged=realHash==(File.Exists(realSave)?Core.DataPackageRules.HashFile(realSave):null);
                if(!Finite(report.hour)||!Finite(report.rain)||!Finite(report.wetness)||!Finite(report.exposure)||!Finite(report.bloom)||!Finite(report.vignette)||!Finite(report.meanDifference)||!Finite(report.toneOnlyMeanDifference)||!Finite(report.offClippedFraction)||!Finite(report.onClippedFraction))throw new Exception("Nonfinite proof metrics");
                if(!report.rendererPostProcessEnabled||!report.cameraHDR||!report.pipelineHDR||report.tonemapping!="ACES"||!report.sameFrameState||!report.uiEnabled||!report.realCareerUnchanged||report.meanDifference<.001f||report.toneOnlyMeanDifference<.0005f||changed<a.Length/100)throw new Exception("Renderer/profile/pixel/isolation invariant failed; inspect report");
                report.status="PASS";
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally{report.volumeStages=stages.ToArray();extra.renderPostProcessing=!previewOff;if(off)Destroy(off);if(on)Destroy(on);if(toneNone)Destroy(toneNone);}
            ScreenCapture.CaptureScreenshot(prefix+"-screen.png");yield return new WaitForEndOfFrame();yield return null;
            extra.renderPostProcessing=original;Finish(report);
        }
    }
}
