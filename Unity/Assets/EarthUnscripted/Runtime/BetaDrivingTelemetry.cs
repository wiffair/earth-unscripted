using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace EarthUnscripted
{
    /// <summary>
    /// Bounded beta-only evidence of wall time versus simulated travel. Samples
    /// do not change physics or player input. Reports are written off-thread to
    /// the beta's own data folder, never to a live career or preferences file.
    /// </summary>
    public sealed class BetaDrivingTelemetry : MonoBehaviour
    {
        [Serializable] sealed class Sample
        {
            public double wallSeconds, simulationSeconds;
            public float frameMilliseconds, speedMph, x, y, z;
            public bool streamingBrake;
            public int gcCollections, wheelContacts;
            public long monoHeapBytes,monoUsedBytes;
            public float graphicsWaitMilliseconds,shaderCreateMilliseconds;
            public string[] contactSurfaces;public bool startLocked,parkingBrake;public float throttle,brake;
        }
        [Serializable] sealed class Report
        {
            public double drivingWallSeconds, drivingSimulationSeconds;
            public float worstFrameMilliseconds;
            public int hitchesOver100ms, hitchesOver333ms;
            public double maximumGenerationSliceMilliseconds;
            public string maximumGenerationSliceStage;
            public int generationSlicesOver8ms;
            public int reusedGeometryChunks, pendingGeometryChunks, warmGeometryChunks;
            public long warmGeometryEstimatedBytes,finishedCacheHits,finishedCacheBytesRead,finishedCacheBytesWritten;
            public double streamingBrakeSeconds;
            public WorkTelemetry.Event[] slowOperations;
            public List<NativeTiming> nativeTimings=new List<NativeTiming>();
            public long managedBytes,queryIndexSegmentCopies,queryIndexUnboundedSegmentCopies;
            public int completedBuildingJobs;public double maximumBuildingWorkerMilliseconds;
            public int queryIndexFallbackBuilds,distantTerrainBuilds;public double latestDistantTerrainBuildMilliseconds;
            public bool macDrivingActivity;
            public int prewarmedGraphicsStates,capturedGraphicsStates;
            public Report Snapshot(){var copy=(Report)MemberwiseClone();copy.recent=new List<Sample>(recent);copy.hitches=new List<Sample>(hitches);copy.nativeTimings=new List<NativeTiming>();foreach(var item in nativeTimings)copy.nativeTimings.Add(new NativeTiming{name=item.name,maximumMilliseconds=item.maximumMilliseconds});return copy;}
            public List<Sample> recent = new List<Sample>();
            public List<Sample> hitches = new List<Sample>();
        }
        [Serializable] sealed class NativeTiming {public string name;public double maximumMilliseconds;}
        sealed class NativeCounter {public NativeTiming timing;public ProfilerRecorder recorder;}
        readonly List<NativeCounter> nativeCounters=new List<NativeCounter>();
        readonly Report report = new Report();
        GameSession session;
        Task writing;
        double lastWall, lastSimulation, nextWrite, nextSample;
        string path;bool refreshedNativeCounters;

        void Start()
        {
            path=Path.Combine(SinglePlayerBeta.DataRoot,"driving-timing.json");
            lastWall=Time.realtimeSinceStartupAsDouble;lastSimulation=Time.timeAsDouble;
            RegisterNativeCounters();
        }
        void RegisterNativeCounters()
        {
            if(SinglePlayerBeta.VerificationOutput!=null)
            {
                var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
                foreach(var handle in handles)
                {
                    var description=ProfilerRecorderHandle.GetDescription(handle);string name=description.Name;
                    if(description.UnitType.ToString()!="TimeNanoseconds" || !(name.Contains("GC.")||name.Contains("Physics.")||name.Contains("Gfx.Wait")||name=="WaitForTargetFPS"||name=="Camera.Render"||name.Contains("Shader.Create")||name.Contains("CreateGraphics")||name.Contains("UploadMesh")||name.Contains("Texture.Upload")||name=="Main Thread"||name=="PlayerLoop"||name.Contains("WaitForJob")||name.Contains("Semaphore.Wait")||name.Contains("ScriptRunDelayed")))continue;
                    if(nativeCounters.Exists(counter=>counter.timing.name==name))continue;
                    var timing=new NativeTiming{name=name};report.nativeTimings.Add(timing);
                    nativeCounters.Add(new NativeCounter{timing=timing,recorder=ProfilerRecorder.StartNew(description.Category,name,1)});
                }
            }
        }
        void OnApplicationQuit()
        {
            // Preserve the final seconds of bounded evidence. This synchronous
            // flush happens only on quit, outside ordinary driving frames.
            if(string.IsNullOrEmpty(path))return;
            try{writing?.GetAwaiter().GetResult();File.WriteAllText(path,JsonUtility.ToJson(report,true));}
            catch(IOException){}
        }
        float NativeMilliseconds(string name){foreach(var c in nativeCounters)if(c.timing.name==name&&c.recorder.Valid)return (float)(c.recorder.LastValue/1e6);return 0;}
        void OnDestroy(){foreach(var counter in nativeCounters)counter.recorder.Dispose();}
        void LateUpdate()
        {
            if(!session)session=FindFirstObjectByType<GameSession>();
            double wall=Time.realtimeSinceStartupAsDouble,simulation=Time.timeAsDouble;
            float milliseconds=(float)((wall-lastWall)*1000);
            if(session && session.BootReady && !session.Menu && !session.PreparingStart)
            {
                // Some native markers register only after physics/rendering
                // starts. Discover them once more after the real world is ready.
                if(!refreshedNativeCounters){RegisterNativeCounters();refreshedNativeCounters=true;}
                foreach(var counter in nativeCounters)
                    if(counter.recorder.Valid)counter.timing.maximumMilliseconds=Math.Max(counter.timing.maximumMilliseconds,counter.recorder.LastValue/1e6);
                report.drivingWallSeconds+=wall-lastWall;
                report.drivingSimulationSeconds+=simulation-lastSimulation;
                if(session.Car.StreamingBrake)report.streamingBrakeSeconds+=wall-lastWall;
                report.worstFrameMilliseconds=Mathf.Max(report.worstFrameMilliseconds,milliseconds);
                if(milliseconds>100)report.hitchesOver100ms++;
                if(milliseconds>333)report.hitchesOver333ms++;
                if(wall>=nextSample || milliseconds>100)
                {
                    nextSample=wall+.5;var position=session.Car.Body.position;
                    report.recent.Add(new Sample {wallSeconds=wall,simulationSeconds=simulation,
                        frameMilliseconds=milliseconds,speedMph=session.Car.AbsoluteSpeed*Core.DrivingMath.MphPerMetreSecond,
                        x=position.x,y=position.y,z=position.z,streamingBrake=session.Car.StreamingBrake,gcCollections=GC.CollectionCount(0),
                        monoHeapBytes=UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong(),monoUsedBytes=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
                        graphicsWaitMilliseconds=NativeMilliseconds("Gfx.WaitForPresentOnGfxThread"),shaderCreateMilliseconds=NativeMilliseconds("Shader.CreateGPUProgram"),
                        wheelContacts=Array.FindAll(session.Car.WheelGrounded,v=>v).Length,contactSurfaces=(string[])session.Car.ContactSurfaceNames.Clone(),
                        startLocked=session.Car.StartLocked,parkingBrake=session.Car.ParkingBrakeApplied,throttle=session.Car.Input?session.Car.Input.Throttle:0,brake=session.Car.Input?session.Car.Input.Brake:0});
                    if(milliseconds>100){report.hitches.Add(report.recent[report.recent.Count-1]);if(report.hitches.Count>32)report.hitches.RemoveAt(0);}
                    if(report.recent.Count>128)report.recent.RemoveAt(0);
                }
            }
            lastWall=wall;lastSimulation=simulation;
            if(wall>=nextWrite && (writing==null || writing.IsCompleted))
            {
                var renderer=session && session.World?.AutomaticWorld ? session.World.AutomaticWorld.Renderer : null;
                if(renderer)
                {
                    report.completedBuildingJobs=renderer.CompletedBuildingJobs;report.maximumBuildingWorkerMilliseconds=renderer.MaximumBuildingWorkerMilliseconds;
                    report.maximumGenerationSliceMilliseconds=renderer.MaximumGenerationSliceMilliseconds;
                    report.maximumGenerationSliceStage=renderer.MaximumGenerationSliceStage;
                    report.generationSlicesOver8ms=renderer.GenerationSlicesOver8ms;
                    report.reusedGeometryChunks=renderer.ReusedGeometryChunks;
                    report.pendingGeometryChunks=renderer.PendingGeometryChunks;
                    report.warmGeometryChunks=renderer.WarmGeometryChunks;
                    report.warmGeometryEstimatedBytes=renderer.WarmGeometryEstimatedBytes;
                    report.finishedCacheHits=renderer.FinishedCacheHits;report.finishedCacheBytesRead=renderer.FinishedCacheBytesRead;report.finishedCacheBytesWritten=renderer.FinishedCacheBytesWritten;
                    report.queryIndexFallbackBuilds=renderer.QueryIndexFallbackBuilds;report.queryIndexSegmentCopies=renderer.QueryIndexSegmentCopies;report.queryIndexUnboundedSegmentCopies=renderer.QueryIndexUnboundedSegmentCopies;report.distantTerrainBuilds=renderer.DistantTerrainBuildCount;report.latestDistantTerrainBuildMilliseconds=renderer.DistantTerrainLatestBuildMilliseconds;
                }
                report.macDrivingActivity=MacDrivingActivity.Active;
                report.managedBytes=GC.GetTotalMemory(false);
                report.slowOperations=Array.FindAll(WorkTelemetry.Capture(),entry=>entry.milliseconds>8||entry.allocatedBytes>=1024*1024||entry.allocationCount>=1024);
                var warmup=session?session.GetComponent<DrivingGraphicsWarmup>():null;report.prewarmedGraphicsStates=warmup?warmup.LoadedStates:0;report.capturedGraphicsStates=warmup?warmup.CapturedStates:0;
                nextWrite=wall+5;var snapshot=report.Snapshot();string destination=path;
                writing=Task.Run(()=> {try{File.WriteAllText(destination,JsonUtility.ToJson(snapshot,true));}catch(IOException){} });
            }
        }
    }
}
