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
            public bool streamingBrake;public float readinessSpeedLimitMph,supportedAheadMetres;public string coverageReason;
            public int gcCollections, wheelContacts;
            public long monoHeapBytes,monoUsedBytes;
            public float graphicsWaitMilliseconds,shaderCreateMilliseconds;
            public int pendingRoadChunks,pendingBuildingVisuals,pendingBuildingColliders,activeRoadBakes;
            public double sourceSeconds,sourceIdleSeconds,mainSliceMilliseconds,chunkSeconds,lastQueueToReadySeconds,cpuMainMilliseconds,cpuRenderMilliseconds,gpuMilliseconds;
            public string sourceStage,buildStage;public bool frameTimingAvailable;
            public string[] contactSurfaces;public bool startLocked,parkingBrake;public float throttle,brake;
        }
        [Serializable] sealed class Report
        {
            public double drivingWallSeconds, drivingSimulationSeconds,firstDrivingRealtimeSeconds;
            public int[] frameHistogramMilliseconds=new int[1001];public long measuredFrames;
            public string frameHistogramDefinition="One bin per millisecond, floor(frame ms); final bin is >=1000ms. Includes all driving frames, rather than only the bounded recent samples.";
            public float worstFrameMilliseconds;
            public int hitchesOver100ms, hitchesOver333ms;
            public double currentGenerationSliceMilliseconds;public string currentGenerationSliceStage;
            public double maximumGenerationSliceMilliseconds;
            public string maximumGenerationSliceStage;
            public int generationSlicesOver8ms;
            public int reusedGeometryChunks, pendingGeometryChunks, warmGeometryChunks;
            public long warmGeometryEstimatedBytes,finishedCacheHits,finishedCacheBytesRead,finishedCacheBytesWritten;
            public double streamingBrakeSeconds,roadReadinessCapSeconds,roadReadinessStopSeconds;
            public string readinessCounterDefinition="Cap seconds count a finite road-readiness speed cap, including soft slowing without StreamingBrake. Stop seconds count cap below 0.5mph with actual speed below 0.25m/s. These are wall seconds during driving.";
            public WorkTelemetry.Event[] slowOperations;
            public List<NativeTiming> nativeTimings=new List<NativeTiming>();
            public long managedBytes,queryIndexSegmentCopies,queryIndexUnboundedSegmentCopies,queryIndexUniqueSegments,queryIndexMembershipSlots;
            public int completedBuildingJobs;public double maximumBuildingWorkerMilliseconds;
            public int roadQueryUniqueSegments,roadQueryCells,facadeReusedGroups;
            public long roadQueryMemberships,roadQueryMembershipSlots,facadeInactiveMatrixBytes;
            public int preparedAreaFileLoads;public long preparedAreaVerifiedBytes;
            public int queryIndexFallbackBuilds,distantTerrainBuilds;public double latestDistantTerrainBuildMilliseconds;
            public bool macDrivingActivity,lightweightTiming,detailedCapture;public int droppedWorkEvents;
            public string timingUnits="Milliseconds; negative memory/native-counter values mean unavailable. Frame timing availability is explicit. Samples are bounded; slow-operation scopes can overlap and must not be summed.";
            public int prewarmedGraphicsStates,capturedGraphicsStates,loadedGraphicsStates,completedWarmupGraphicsStates;
            public bool graphicsWarmupCompleted;
            public string graphicsWarmupOrigin;
            public Report Snapshot(){var copy=(Report)MemberwiseClone();copy.frameHistogramMilliseconds=(int[])frameHistogramMilliseconds.Clone();copy.recent=new List<Sample>(recent);copy.hitches=new List<Sample>(hitches);copy.nativeTimings=new List<NativeTiming>();foreach(var item in nativeTimings)copy.nativeTimings.Add(new NativeTiming{name=item.name,maximumMilliseconds=item.maximumMilliseconds});return copy;}
            public List<Sample> recent = new List<Sample>();
            public List<Sample> hitches = new List<Sample>();
        }
        [Serializable] sealed class NativeTiming {public string name;public double maximumMilliseconds;}
        sealed class NativeCounter {public NativeTiming timing;public ProfilerRecorder recorder;}
        static readonly bool ForceLightweight=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedLightweightTiming")>=0;
        static readonly bool ForceDetailed=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedDetailedTiming")>=0;
        bool Lightweight;
        static bool SelectLightweight(bool isolatedProof,bool forceLightweight,bool forceDetailed)
            =>forceLightweight||(!isolatedProof&&!forceDetailed);
        readonly List<NativeCounter> nativeCounters=new List<NativeCounter>();
        readonly Report report = new Report();
        GameSession session;
        Task writing;
        double lastWall, lastSimulation, nextWrite, nextSample;
        string path;bool refreshedNativeCounters;
        readonly FrameTiming[] frameTimings=new FrameTiming[1];
        void SetCaptureMode(bool detailed)
        {
            WorkTelemetry.SetRuntimeEnabled(detailed);report.detailedCapture=detailed;
            bool lightweight=!detailed;if(Lightweight==lightweight)return;
            Lightweight=lightweight;report.lightweightTiming=lightweight;report.detailedCapture=detailed;
            WorkTelemetry.SetRuntimeEnabled(detailed);
            foreach(var counter in nativeCounters)counter.recorder.Dispose();nativeCounters.Clear();report.nativeTimings.Clear();
            refreshedNativeCounters=false;if(detailed)RegisterNativeCounters();
        }

        void Start()
        {
            if(!SinglePlayerBeta.OwnsSession){enabled=false;return;}
            Lightweight=SelectLightweight(SinglePlayerBeta.VerificationOutput!=null,ForceLightweight,ForceDetailed);
            path=Path.Combine(SinglePlayerBeta.DataRoot,"driving-timing.json");
            lastWall=Time.realtimeSinceStartupAsDouble;lastSimulation=Time.timeAsDouble;
            report.lightweightTiming=Lightweight;RegisterNativeCounters();
        }
        void RegisterNativeCounters()
        {
            if(Lightweight)return;
            if(!Lightweight)
            {
                var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
                foreach(var handle in handles)
                {
                    var description=ProfilerRecorderHandle.GetDescription(handle);string name=description.Name;
                    if(description.UnitType.ToString()!="TimeNanoseconds" || !(name.Contains("GC.")||name.Contains("Physics.")||name.Contains("Gfx.Wait")||name=="WaitForTargetFPS"||name=="Camera.Render"||name.Contains("Shader.Create")||name.Contains("CreateGraphics")||name.Contains("UploadMesh")||name.Contains("Texture.Upload")||name=="Main Thread"||name=="PlayerLoop"||name.Contains("WaitForJob")||name.Contains("Semaphore.Wait")||name.Contains("ScriptRunDelayed")))continue;
                    if(nativeCounters.Exists(counter=>counter.timing.name==name))continue;
                    var timing=new NativeTiming{name=name};report.nativeTimings.Add(timing);
                    try{nativeCounters.Add(new NativeCounter{timing=timing,recorder=ProfilerRecorder.StartNew(description.Category,name,1)});}
                    catch(InvalidOperationException){report.nativeTimings.Remove(timing);}
                }
            }
        }
        void OnApplicationQuit()
        {
            // Preserve the final seconds of bounded evidence. This synchronous
            // flush happens only on quit, outside ordinary driving frames.
            if(string.IsNullOrEmpty(path)||report.drivingWallSeconds<=0)return;
            try{if(Lightweight)CaptureSummary();writing?.GetAwaiter().GetResult();File.WriteAllText(path,JsonUtility.ToJson(report,true));}
            catch(IOException){}
        }
        float NativeMilliseconds(string name){foreach(var c in nativeCounters)if(c.timing.name==name&&c.recorder.Valid)return (float)(c.recorder.LastValue/1e6);return -1;}
        void OnDestroy(){foreach(var counter in nativeCounters)counter.recorder.Dispose();WorkTelemetry.SetRuntimeEnabled(false);}
        void LateUpdate()
        {
            if(!session)session=FindFirstObjectByType<GameSession>();
            SetCaptureMode(!ForceLightweight&&(ForceDetailed||(SinglePlayerBeta.VerificationOutput!=null)|| (session&&session.Profile!=null&&session.Profile.detailedPerformanceCapture)));
            double wall=Time.realtimeSinceStartupAsDouble,simulation=Time.timeAsDouble;
            float milliseconds=(float)((wall-lastWall)*1000);
            if(session && session.BootReady && !session.Menu && !session.PreparingStart)
            {
                // Some native markers register only after physics/rendering
                // starts. Discover them once more after the real world is ready.
                if(!refreshedNativeCounters){RegisterNativeCounters();refreshedNativeCounters=true;}
                foreach(var counter in nativeCounters)
                    if(counter.recorder.Valid)counter.timing.maximumMilliseconds=Math.Max(counter.timing.maximumMilliseconds,counter.recorder.LastValue/1e6);
                if(report.measuredFrames==0)report.firstDrivingRealtimeSeconds=wall;
                report.measuredFrames++;report.frameHistogramMilliseconds[Mathf.Clamp((int)milliseconds,0,1000)]++;
                report.drivingWallSeconds+=wall-lastWall;
                report.drivingSimulationSeconds+=simulation-lastSimulation;
                if(session.Car.StreamingBrake)report.streamingBrakeSeconds+=wall-lastWall;
                var host=session.World?.AutomaticWorld;float readiness=host?host.PreparationSpeedLimitMph:float.PositiveInfinity;
                if(!float.IsInfinity(readiness)){report.roadReadinessCapSeconds+=wall-lastWall;if(readiness<.5f&&session.Car.AbsoluteSpeed<.25f)report.roadReadinessStopSeconds+=wall-lastWall;}
                report.worstFrameMilliseconds=Mathf.Max(report.worstFrameMilliseconds,milliseconds);
                if(milliseconds>100)report.hitchesOver100ms++;
                if(milliseconds>333)report.hitchesOver333ms++;
                if(wall>=nextSample || milliseconds>100)
                {
                    nextSample=wall+.5;var position=session.Car.Body.position;
                    var world=session.World?.AutomaticWorld?.Renderer;
                    bool frameTimingAvailable=false;
                    if(!Lightweight){FrameTimingManager.CaptureFrameTimings();frameTimingAvailable=FrameTimingManager.GetLatestTimings(1,frameTimings)>0;}
                    report.recent.Add(new Sample {wallSeconds=wall,simulationSeconds=simulation,
                        frameMilliseconds=milliseconds,speedMph=session.Car.AbsoluteSpeed*Core.DrivingMath.MphPerMetreSecond,
                        x=position.x,y=position.y,z=position.z,
                        pendingRoadChunks=world?world.PendingGeometryChunks:0,pendingBuildingVisuals=world?world.PendingSceneryChunks:0,pendingBuildingColliders=world?world.PendingBuildingCollisions:0,activeRoadBakes=world?world.ActiveRoadColliderBakes:0,
                        sourceSeconds=world?world.DataPreparationSeconds:0,sourceIdleSeconds=world?world.DataWorkerIdleSeconds:0,sourceStage=world?world.DataPreparationStage:"",buildStage=world?world.CurrentBuildStage:"",
                        mainSliceMilliseconds=world?world.CurrentGenerationSliceMilliseconds:0,chunkSeconds=world?world.CurrentChunkSeconds:0,lastQueueToReadySeconds=world?world.LastChunkReadySeconds:0,
                        frameTimingAvailable=frameTimingAvailable,cpuMainMilliseconds=frameTimingAvailable?frameTimings[0].cpuMainThreadFrameTime:-1,cpuRenderMilliseconds=frameTimingAvailable?frameTimings[0].cpuRenderThreadFrameTime:-1,gpuMilliseconds=frameTimingAvailable&&frameTimings[0].gpuFrameTime>0?frameTimings[0].gpuFrameTime:-1,
                        readinessSpeedLimitMph=float.IsInfinity(readiness)?-1:readiness,supportedAheadMetres=host?host.SupportedAheadMetres:0,coverageReason=host?host.CoverageReason:"",streamingBrake=session.Car.StreamingBrake,gcCollections=GC.CollectionCount(0),
                        monoHeapBytes=Lightweight?-1:UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong(),monoUsedBytes=Lightweight?-1:UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
                        graphicsWaitMilliseconds=NativeMilliseconds("Gfx.WaitForPresentOnGfxThread"),shaderCreateMilliseconds=NativeMilliseconds("Shader.CreateGPUProgram"),
                        wheelContacts=Array.FindAll(session.Car.WheelGrounded,v=>v).Length,contactSurfaces=(string[])session.Car.ContactSurfaceNames.Clone(),
                        startLocked=session.Car.StartLocked,parkingBrake=session.Car.ParkingBrakeApplied,throttle=session.Car.Input?session.Car.Input.Throttle:0,brake=session.Car.Input?session.Car.Input.Brake:0});
                    if(milliseconds>100){report.hitches.Add(report.recent[report.recent.Count-1]);if(report.hitches.Count>32)report.hitches.RemoveAt(0);}
                    if(report.recent.Count>128)report.recent.RemoveAt(0);
                }
            }
            lastWall=wall;lastSimulation=simulation;
            if(report.drivingWallSeconds>0 && !Lightweight && wall>=nextWrite && (writing==null || writing.IsCompleted))
            {
                CaptureSummary();
                nextWrite=wall+5;var snapshot=report.Snapshot();string destination=path;
                writing=Task.Run(()=> {try{File.WriteAllText(destination,JsonUtility.ToJson(snapshot,true));}catch(IOException){} });
            }
        }
        // A normal-like proof retains frame/physical evidence without native
        // profiler counters, heap-stat locks or periodic diagnostic writes.
        internal string ExportSnapshot(){CaptureSummary();return JsonUtility.ToJson(report,true);}
        void CaptureSummary()
        {
                var renderer=session && session.World?.AutomaticWorld ? session.World.AutomaticWorld.Renderer : null;
                if(renderer)
                {
                    report.preparedAreaFileLoads=renderer.PreparedAreaFileLoads;report.preparedAreaVerifiedBytes=renderer.PreparedAreaVerifiedBytes;
                    report.completedBuildingJobs=renderer.CompletedBuildingJobs;report.maximumBuildingWorkerMilliseconds=renderer.MaximumBuildingWorkerMilliseconds;
                    report.currentGenerationSliceMilliseconds=renderer.CurrentGenerationSliceMilliseconds;report.currentGenerationSliceStage=renderer.CurrentGenerationSliceStage;
                    report.maximumGenerationSliceMilliseconds=renderer.MaximumGenerationSliceMilliseconds;
                    report.maximumGenerationSliceStage=renderer.MaximumGenerationSliceStage;
                    report.generationSlicesOver8ms=renderer.GenerationSlicesOver8ms;
                    report.reusedGeometryChunks=renderer.ReusedGeometryChunks;
                    report.pendingGeometryChunks=renderer.PendingGeometryChunks;
                    report.warmGeometryChunks=renderer.WarmGeometryChunks;
                    report.warmGeometryEstimatedBytes=renderer.WarmGeometryEstimatedBytes;
                    report.finishedCacheHits=renderer.FinishedCacheHits;report.finishedCacheBytesRead=renderer.FinishedCacheBytesRead;report.finishedCacheBytesWritten=renderer.FinishedCacheBytesWritten;
                    report.queryIndexFallbackBuilds=renderer.QueryIndexFallbackBuilds;report.queryIndexSegmentCopies=renderer.QueryIndexSegmentCopies;report.queryIndexUnboundedSegmentCopies=renderer.QueryIndexUnboundedSegmentCopies;report.queryIndexUniqueSegments=renderer.QueryIndexUniqueSegments;report.queryIndexMembershipSlots=renderer.QueryIndexMembershipSlots;report.distantTerrainBuilds=renderer.DistantTerrainBuildCount;report.latestDistantTerrainBuildMilliseconds=renderer.DistantTerrainLatestBuildMilliseconds;
                }
                var region=session?session.World?.Route?.Region:null;
                if(region!=null){report.roadQueryUniqueSegments=region.RoadQueryUniqueSegments;report.roadQueryCells=region.RoadQueryCellCount;report.roadQueryMemberships=region.RoadQueryMemberships;report.roadQueryMembershipSlots=region.RoadQueryMembershipSlots;}
                var details=session?FindFirstObjectByType<AutomaticBuildingDetails>():null;
                if(details){report.facadeReusedGroups=details.ReusedFacadeGroups;report.facadeInactiveMatrixBytes=details.InactiveMatrixBytes;}
                report.macDrivingActivity=MacDrivingActivity.Active;report.detailedCapture=!Lightweight;report.droppedWorkEvents=WorkTelemetry.Dropped;
                report.managedBytes=Lightweight?-1:GC.GetTotalMemory(false);
                report.slowOperations=Array.FindAll(WorkTelemetry.Capture(),entry=>entry.milliseconds>8||entry.allocatedBytes>=1024*1024||entry.allocationCount>=1024);
                var warmup=session?session.GetComponent<DrivingGraphicsWarmup>():null;
                report.loadedGraphicsStates=warmup?warmup.LoadedStates:0;
                report.completedWarmupGraphicsStates=warmup?warmup.CompletedWarmupStates:0;
                report.prewarmedGraphicsStates=report.completedWarmupGraphicsStates;
                report.graphicsWarmupCompleted=warmup&&warmup.WarmupCompleted;
                report.graphicsWarmupOrigin=warmup?warmup.CollectionOrigin:"none";
                report.capturedGraphicsStates=warmup?warmup.CapturedStates:0;
        }
    }
}
