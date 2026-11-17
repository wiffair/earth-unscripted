using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Explicit, disposable-profile cached-start timing capture. The
    /// existing renderer owns all geometry; reflection reads its diagnostic
    /// counters without changing build order, coverage or frame scheduling.</summary>
    public sealed class BetaStartupVerification : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        readonly JArray stages = new JArray();
        readonly List<double> frameMilliseconds = new List<double>();
        double lastFrame, nextSample;
        bool finished;

        void Awake()
        {
            if (SinglePlayerBeta.VerificationOutput == null)
                throw new InvalidOperationException("Isolated startup proof required");
            Application.logMessageReceived += Log;
            lastFrame = Time.realtimeSinceStartupAsDouble;
        }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                errors.Add(text + "\n" + stack);
        }
        static object Field(object owner, string name)
        {
            return owner?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
        }
        static int Count(object report, string name)
        {
            return report == null ? 0 : (int)report.GetType().GetField(name).GetValue(report);
        }
        void Update()
        {
            if (finished) return;
            double now = Time.realtimeSinceStartupAsDouble;
            frameMilliseconds.Add((now - lastFrame) * 1000);
            lastFrame = now;
            var session = FindFirstObjectByType<GameSession>();
            if (!session) return;
            var renderer = FindFirstObjectByType<NewbridgePrototype.NewbridgeWorld>();
            var report = Field(renderer, "report");
            if (now >= nextSample || session.BootReady || session.BootFailed)
            {
                nextSample = now + 2;
                var stage = new JObject
                {
                    ["seconds"] = now, ["frame"] = Time.frameCount,
                    ["status"] = session.BootStatus,
                    ["chunks"] = Count(report, "generatedChunks"),
                    ["pending"] = renderer ? renderer.PendingGeometryChunks : 0,
                    ["areas"] = renderer ? renderer.ResidentAreas.Count() : 0
                };
                stages.Add(stage);
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "progress.json"), stage.ToString());
            }
            if (session.BootFailed || now > 600)
            { Finish(session, report, renderer, session.BootFailed ? session.BootStatus : "Startup timeout"); return; }
            if (session.BootReady && renderer && renderer.FinishedCachePendingWrites == 0){finished=true;StartCoroutine(CaptureReady(session,report,renderer));}
        }
        IEnumerator CaptureReady(GameSession session,object report,object renderer)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null){Finish(session,report,renderer,null);yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta9FeedbackSmoke")>=0)
            {
                int w=Screen.width,h=Screen.height;var mode=Screen.fullScreenMode;var settings=new DisplayResolutionMenu();
                bool requested=settings.Preview(session.Profile,1280,720,0);yield return new WaitForSecondsRealtime(1);
                bool preview=requested&&Screen.width==1280&&Screen.height==720;
                settings.Revert();yield return new WaitForSecondsRealtime(1);bool reverted=Screen.width==w&&Screen.height==h;
                settings.Preview(session.Profile,1280,720,0);yield return new WaitForSecondsRealtime(16);settings.Update();yield return new WaitForSecondsRealtime(1);
                bool timedOut=!settings.Pending&&Screen.width==w&&Screen.height==h;
                settings.Preview(session.Profile,1280,720,0);yield return new WaitForSecondsRealtime(1);bool confirmed=settings.Confirm(session.Profile);var loaded=PlayerProfile.Load();
                bool persisted=confirmed&&loaded.displayWidth==1280&&loaded.displayHeight==720;
                session.Profile.displayWidth=w;session.Profile.displayHeight=h;session.Profile.displayMode=mode==FullScreenMode.Windowed?0:1;session.Profile.Save();Screen.SetResolution(w,h,mode);yield return new WaitForSecondsRealtime(1);
                var configuration=AudioSettings.GetConfiguration();
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"feedback.json"),new JObject{["preview"]=preview,["reverted"]=reverted,["timedRollback"]=timedOut,["persisted"]=persisted,["engineVolume"]=session.Profile.engineVolume,["dspBufferSize"]=configuration.dspBufferSize}.ToString());
                if(!preview||!reverted||!timedOut||!persisted)errors.Add("Native display resolution confirmation/rollback failed");
            }
            yield return null;yield return new WaitForEndOfFrame();
            var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
            try{File.WriteAllBytes(Path.Combine(SinglePlayerBeta.VerificationOutput,"startup-view.png"),screenshot.EncodeToPNG());}
            finally{Destroy(screenshot);}
            Finish(session,report,renderer,null);
        }
        void WriteV8Preview()
        {
            const int rate=24000,count=rate*6;var synth=new Core.EngineSoundSynth();
            using(var stream=new BinaryWriter(File.Create(Path.Combine(SinglePlayerBeta.VerificationOutput,"v8-preview.wav"))))
            {
                stream.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));stream.Write(36+count*2);stream.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));stream.Write(16);stream.Write((short)1);stream.Write((short)1);stream.Write(rate);stream.Write(rate*2);stream.Write((short)2);stream.Write((short)16);stream.Write(System.Text.Encoding.ASCII.GetBytes("data"));stream.Write(count*2);
                for(int i=0;i<count;i++){float sample=synth.Sample(900+4700*i/(float)count,.75f,rate);if(float.IsNaN(sample)||Mathf.Abs(sample)>1)throw new InvalidDataException("V8 sample invalid");stream.Write((short)(sample*24000));}
            }
        }
        void Finish(GameSession session, object report, object renderer, string failure)
        {
            if(failure==null&&Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta32Smoke")>=0)
            {try{Beta32RuntimeVerification.Run(session);}catch(Exception error){failure=error.ToString();}}
            finished = true;
            var ordered = frameMilliseconds.OrderBy(value => value).ToArray();
            var patrols=FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None);
            float patrolGap=patrols.Length==0||!session.Car?0:patrols.Min(p=>Vector3.Distance(p.Car.Body.position,session.Car.Body.position));
            int expectedPatrols=session&&session.World?(session.World.IsCircuit?0:session.World.ScopedSession800!=null?
                VehiclePopulation.ActualVehicleRosterDomain.ScopedRoadPoliceSlots:VehiclePopulation.ActualVehicleRosterDomain.RoadPoliceSlots):-1;
            if(failure==null && (!session.ClearStartVerified || patrols.Length!=expectedPatrols || expectedPatrols>0&&patrolGap<85))failure="Clear relocation/patrol spacing check failed";
            try{if(failure==null)WriteV8Preview();}catch(Exception error){errors.Add(error.ToString());}
            if(failure==null && Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaJunctionSmoke")>=0)
            {
                var world=FindFirstObjectByType<NewbridgePrototype.NewbridgeWorld>();
                var junctions=world.InspectJunctionGeometry(session.Car.Body.position,120,true);
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"junction-geometry.json"),JsonUtility.ToJson(junctions,true));
                if(junctions.roadColliderSamples==0 || junctions.failures.Count>0)failure="Junction surface proof failed: "+string.Join("; ",junctions.failures);
            }
            if(failure==null && Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaJunctionSmoke")>=0)
            {
                var world=FindFirstObjectByType<NewbridgePrototype.NewbridgeWorld>();int samples=0,blocked=0;var seen=new HashSet<string>();
                foreach(var map in world.ResidentAreas)foreach(var feature in map.features)
                {
                    if(feature.category!="rail")continue;
                    foreach(var point in feature.points)
                    {
                        if(Vector3.Distance(point,session.Car.Body.position)>130||!world.HasGeometryAt(point))continue;
                        string key=Mathf.RoundToInt(point.x*100)+":"+Mathf.RoundToInt(point.z*100);if(!seen.Add(key))continue;
                        samples++;
                        var hits=Physics.OverlapCapsule(point+Vector3.up*1.3f,point+Vector3.up*4.1f,1.0f,~(1<<8),QueryTriggerInteraction.Ignore);
                        if(hits.Any(h=>h.name=="Mapped building massing"||h.name=="Mapped roofs"))blocked++;
                    }
                }
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"transport-geometry.json"),new JObject{["railSamples"]=samples,["buildingBlockedRailSamples"]=blocked,["signalObjects"]=FindObjectsByType<NewbridgePrototype.TrafficControlVisual>(FindObjectsSortMode.None).Length,["sunShadowStrength"]=session.World.Sun.shadowStrength,["ambientGround"]=JObject.FromObject(new {r=RenderSettings.ambientGroundColor.r,g=RenderSettings.ambientGroundColor.g,b=RenderSettings.ambientGroundColor.b})}.ToString());
                if(blocked>0)failure="Station building still blocks railway clearance: "+blocked;
            }
            bool passed = failure == null && errors.Count == 0;
            var result = new JObject
            {
                ["passed"] = passed, ["failure"] = failure, ["errors"] = new JArray(errors),
                ["version"] = Application.version, ["readySeconds"] = Time.realtimeSinceStartupAsDouble,
                ["frames"] = Time.frameCount, ["quality"] = session.Profile.quality,
                ["vSync"] = QualitySettings.vSyncCount, ["targetFrameRate"] = Application.targetFrameRate,
                ["startX"] = session.Profile.startX, ["startZ"] = session.Profile.startZ,
                ["frameMedianMs"] = ordered[ordered.Length / 2],
                ["frameP95Ms"] = ordered[(int)(ordered.Length * .95)],
                ["frameMaximumMs"] = ordered[ordered.Length - 1],
                ["stages"] = stages, ["workerCache"] = Path.Combine(Application.persistentDataPath, "AutomaticWorldCache"),
                ["geometry"] = report == null ? null : JObject.Parse(JsonUtility.ToJson(report)),
                ["areaLoads"] = JArray.FromObject((IEnumerable)Field(renderer, "areaLoads")??Array.Empty<object>()),
                ["clearStartVerified"] = session.ClearStartVerified,
                ["nearestPatrolMetres"] = patrolGap,["patrolCount"]=patrols.Length,["expectedPatrolCount"]=expectedPatrols,
                ["spawnX"] = session.Car?session.Car.Body.position.x:0, ["spawnZ"] = session.Car?session.Car.Body.position.z:0,
                ["finishedCacheHits"] = session.World?.AutomaticWorld?.Renderer?.FinishedCacheHits??0,
                ["finishedCacheBytesRead"] = session.World?.AutomaticWorld?.Renderer?.FinishedCacheBytesRead??0,
                ["finishedCacheBytesWritten"] = session.World?.AutomaticWorld?.Renderer?.FinishedCacheBytesWritten??0,
                ["bootTiming"] = JObject.Parse(JsonUtility.ToJson(BootTelemetry.Capture()))
            };
            if (passed && Array.IndexOf(Environment.GetCommandLineArgs(), "-earthUnscriptedBetaAlignmentSmoke") >= 0)
            {
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "startup.json"), result.ToString());
                gameObject.AddComponent<BetaShorelineAlignmentVerification>();
                return;
            }
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "result.json"), result.ToString());
            Application.Quit(passed ? 0 : 1);
        }
    }
}
