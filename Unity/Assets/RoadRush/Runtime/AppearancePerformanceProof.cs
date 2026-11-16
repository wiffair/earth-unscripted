using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadRush.Core;
using UnityEngine;
using UnityEngine.Rendering;
namespace RoadRush
{
    // Explicit native developer proof only. Normal play never creates this object.
    public sealed class AppearancePerformanceProof : MonoBehaviour
    {
        const string Flag="-roadRushAppearanceProof";const int Samples=300;static double MinimumSeconds=20;static bool productionOnly,separatedObservers;static int proofFrameRate=-1,runtimeErrors;static string roadRegionFile;static double roadPreparationSeconds;static bool nightRain;static bool RoadMode=>roadRegionFile!=null;
        static string prefix,realPath,realHash,initialError;static bool requested;
        long surfaceProbeSamples;int surfaceProbeFailures;readonly List<string> surfaceProbeErrors=new List<string>();
        bool finished,captureEngineTimings,measuringSurfaceCoverage;int lastTimingCapture=-1;string timingError="";double started;GameSession session;RaceDriver assist;List<VehicleController> fleet;readonly List<Stage> stages=new List<Stage>();readonly List<string> checks=new List<string>();double sourceFingerprint;Color[] sourceColours;Material[] sourceMaterials;float dentMilliseconds;
        [Serializable] public sealed class TimingMetric
        {
            public bool available;public string status;public int samples,totalRecords,zeroRecords,invalidRecords;public float medianMs,p95Ms,p99Ms,minimumMs,maximumMs;
        }
        [Serializable] public sealed class EngineTiming
        {
            public string frameStartTimestamp;public int readFrameCount,readRenderedFrameCount;public double cpuFrameMs,cpuMainThreadMs,cpuRenderThreadMs,presentWaitMs,gpuFrameMs;
        }
        [Serializable] public sealed class Stage
        {
            public string name,frameTimingStatus,displayedFpsStatus="Not measured; renderer/update throughput is not a display presentation rate.";
            public int frames,width,height,importedCars,activeCarRenderers,activeCarMeshRenderers,firstFrameCount,lastFrameCount,firstRenderedFrameCount,lastRenderedFrameCount,discardedRepeatedFrameCounters,skippedRenderedFrames,renderFrameInterval;
            public long activeCarTriangles;public double measurementSeconds,observedRenderedThroughput,observedUpdateThroughput;
            public float medianFrameMs,p95FrameMs,medianWallClockThroughput,p05WallClockThroughput,p95WallClockThroughput,minimumWallClockThroughput,maximumWallClockThroughput,travelMetres,raceClockAdvanced,peakSpeed,damage;
            public bool frameTimingFeatureEnabled;public double[] sampleRealtimeSeconds;public float[] frameMilliseconds,unityUnscaledFrameMilliseconds;public int[] frameCounts,renderedFrameCounts,renderedFrameDeltas;public EngineTiming[] engineFrameTimings;
            public TimingMetric cpuFrame,cpuMainThread,cpuRenderThread,presentWait,gpuFrame;
            public long criticalSurfaceSamples;public int criticalSurfaceFailures;public string[] criticalSurfaceErrors;
            public FacadeSummary facades;public WoodlandSummary woodland;public GeoWorld.StreamingSnapshot streaming;public WorkTelemetry.Event[] workEvents;public int workEventsDropped;public float p99FrameMs;
        }
        [Serializable] public sealed class FacadeSummary
        {
            public int framesWithDetails,peakResidentFeatures,peakSubmittedWindows,peakSubmittedDoors,peakDrawSubmissions,activeChunkOverflow;
            public long peakSubmittedTriangles;
            public string interpretation="Decorative instances submitted to Unity, not visible pixels; shared windows/doors have no individual GameObjects or colliders.";
        }
        [Serializable] public sealed class WoodlandSummary
        {
            public int sourceTrees,peakResidentTrees,peakSubmittedTrees,peakNearTrees,peakMediumTrees,peakFarTrees,peakDrawSubmissions,framesWithDetailedTrees;
            public long peakSubmittedTriangles,totalAllocatedMemoryBefore,totalAllocatedMemoryAfter,graphicsDriverMemoryBefore,graphicsDriverMemoryAfter;
            public string interpretation="Submissions are instances/triangles handed to Unity, not visible pixel or executed GPU triangle counts. Memory is whole-player allocated/graphics-driver accounting, not a per-tree estimate; zero driver memory may be unavailable.";
        }
        [Serializable] public sealed class Report
        {
            public bool sessionPresent,bootReady,bootFailed,conditionsAvailable,profileAvailable,bootTelemetryEnabled;
            public string bootStatus,lastRecordedBootOperation,bootTelemetryCaptureError;
            public BootTelemetry.Bundle bootTiming;
            public string status,error,build,unity,platform,graphicsDevice,graphicsAPI,timingDefinition,limitations,profile,roadSource;public int runtimeErrorCount;public bool requestedNightRain;public float actualHour,actualRain,actualWetness;public int width,height,quality,unityQualityLevel,vSync,targetFrameRate,renderFrameInterval,checks;public double displayRefreshHz;public double requestedMeasurementSeconds,roadPreparationSeconds;public bool productionOnly,separatedObservers,realCareerUnchanged;public float dentMilliseconds;public string[] passed;public Stage[] stages;
        }
        static string Argument()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,Flag);return index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Prepare()
        {
            prefix=Argument();requested=prefix!=null;if(!requested)return;
            // Explicit QA only: allow asynchronous preparation before Start is
            // reached, even when the proof window initially lacks focus.
            Application.runInBackground=true;
            var args=Environment.GetCommandLineArgs();productionOnly=Array.IndexOf(args,"-roadRushProofProductionOnly")>=0;
            separatedObservers=Array.IndexOf(args,"-roadRushProofSeparatedObservers")>=0;if(separatedObservers)productionOnly=true;
            int capIndex=Array.IndexOf(args,"-roadRushProofFrameRate");
            if(capIndex>=0&&capIndex+1<args.Length&&int.TryParse(args[capIndex+1],out var cap)&&cap>=30&&cap<=240)proofFrameRate=cap;
            int roadAt=Array.IndexOf(args,"-roadRushProofRoadRegion");if(roadAt>=0&&roadAt+1<args.Length){roadRegionFile=Path.GetFullPath(args[roadAt+1]);productionOnly=true;separatedObservers=false;}
            nightRain=Array.IndexOf(args,"-roadRushProofNightRain")>=0;runtimeErrors=0;Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Error||type==LogType.Exception)runtimeErrors++;};
            int durationIndex=Array.IndexOf(args,"-roadRushProofSeconds");
            if(durationIndex>=0&&durationIndex+1<args.Length&&double.TryParse(args[durationIndex+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var duration)&&!double.IsNaN(duration)&&!double.IsInfinity(duration))MinimumSeconds=Math.Max(20,Math.Min(90,duration));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(prefix));realPath=Path.Combine(Application.persistentDataPath,"road-rush-save.json");realHash=File.Exists(realPath)?DataPackageRules.HashFile(realPath):"missing";
                string profile=prefix+"-profile.json";if(string.Equals(Path.GetFullPath(profile),Path.GetFullPath(realPath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Proof profile must be isolated");
                // This fixture is generated independently; never copy the real career.
                PlayerProfile.StorageOverride=profile;BootTelemetry.EnableIsolated();var data=new PlayerProfile{region="spa",regionTimeZone="Europe/Brussels",quality=2,clarity=0,volume=0,rumble=0,credits=4321,weatherMode=0,clockMode=0,fixedHour=14,trackMode=2,raceLaps=3,raceDifficulty=1,livingScenery=true};
                if(nightRain){data.weatherMode=1;data.fixedHour=1.5f;}
                data.garage=new GarageState{activeId="appearance-performance",cars=new List<OwnedCar>{new OwnedCar{id="appearance-performance",model=0,paint=3,engine=0,tyres=0,brakes=0}}};
                if(RoadMode)
                {
                    if(!File.Exists(roadRegionFile)||new FileInfo(roadRegionFile).Length>DataPackageRules.MaximumFileBytes)throw new InvalidDataException("Road proof source unavailable or oversized");
                    var input=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(roadRegionFile));string canonical=WorldSnapshotCodec.CanonicalJson(input);
                    var region=WorldSnapshotCodec.Decode(canonical,WorldSnapshotCodec.NetworkId(input),WorldSnapshotCodec.Epoch(input));region.id="download-appearance-road-proof";region.networkMapId="";
                    RegionCache.OverrideFolder=profile+".regions";Directory.CreateDirectory(RegionCache.OverrideFolder);File.WriteAllText(Path.Combine(RegionCache.OverrideFolder,region.id+".json"),JsonUtility.ToJson(region));data.region=region.id;data.regionTimeZone=region.timeZone;data.detailedScenery=true;
                }
                File.WriteAllText(profile,JsonUtility.ToJson(data,true));GameSession.AutoStartCircuit=false;
            }
            catch(Exception e){initialError=e.ToString();}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(requested)new GameObject("Isolated native appearance performance proof").AddComponent<AppearancePerformanceProof>();}
        void Start(){started=Time.realtimeSinceStartupAsDouble;Application.runInBackground=true;StartCoroutine(Guard(Execute()));}
        IEnumerator Guard(IEnumerator job)
        {
            while(!finished)
            {
                bool more=false;object next=null;Exception failure=null;
                try{more=job.MoveNext();if(more)next=job.Current;}catch(Exception e){failure=e;}
                if(failure!=null){Complete(false,failure.ToString());yield break;}if(!more)yield break;
                if(next is IEnumerator nested)yield return Guard(nested);else yield return next;
            }
        }
        void Update()
        {
            if(finished)return;
            if(!session)session=FindAnyObjectByType<GameSession>();
            if(session&&session.BootFailed){Complete(false,"Actual world boot failed");return;}
            if(captureEngineTimings&&Time.frameCount!=lastTimingCapture)
            {
                lastTimingCapture=Time.frameCount;
                try{FrameTimingManager.CaptureFrameTimings();}catch(Exception e){timingError=e.Message;captureEngineTimings=false;}
            }
            if(!finished&&Time.realtimeSinceStartupAsDouble-started>240)Complete(false,"Native appearance proof timed out");
        }
        void FixedUpdate()
        {
            if(!measuringSurfaceCoverage||fleet==null||!session||!session.World.GeographicWorld)return;
            var world=session.World.GeographicWorld;var region=world.Region;
            foreach(var car in fleet)
            {
                int cx=Mathf.FloorToInt(car.Body.position.x/320),cz=Mathf.FloorToInt(car.Body.position.z/320);
                for(int z=cz-1;z<=cz+1;z++)for(int x=cx-1;x<=cx+1;x++)
                {
                    if(x*320>=region.minX+region.width||(x+1)*320<=region.minX||z*320>=region.minZ+region.depth||(z+1)*320<=region.minZ)continue;
                    surfaceProbeSamples++;
                    if(world.HasSurface(new Vector3(x*320+160,0,z*320+160)))continue;
                    surfaceProbeFailures++;if(surfaceProbeErrors.Count<16)surfaceProbeErrors.Add(car.name+" frame "+Time.frameCount+" missing "+x+","+z);
                }
            }
        }
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);Debug.Log("APPEARANCE NATIVE PASS: "+message);}
        static double Fingerprint(GameObject source)
        {
            double result=0;int index=1;foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true))foreach(var p in filter.sharedMesh.vertices)result+=(p.x*.31+p.y*.17+p.z*.53)*(index++%7919+1);return result;
        }
        IEnumerator Execute()
        {
            Check(initialError==null,"isolated profile preparation succeeded"+(initialError==null?"":": "+initialError));
            Check(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"native rendering device is active");
            while(!(session=FindAnyObjectByType<GameSession>())||!session.BootReady||LocationLoading.Busy)
            {
                if(session&&session.BootFailed){Complete(false,"Actual world boot failed");yield break;}
                yield return null;
            }
            Check(PlayerProfile.StoragePath==prefix+"-profile.json"&&(RoadMode?!session.World.IsCircuit&&session.World.Route.Region.id=="download-appearance-road-proof":session.World.IsCircuit&&session.World.Route.Region.id=="spa"),"native proof uses its isolated "+(RoadMode?"prepared-road":"Spa")+" career");
            session.Updates.Enabled=session.Updates.Automatic=false;session.Input.enabled=false;session.Input.Throttle=session.Input.Brake=session.Input.Steer=session.Input.Handbrake=0;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=proofFrameRate;OnDemandRendering.renderFrameInterval=1;Time.captureFramerate=0;Screen.SetResolution(2560,1600,FullScreenMode.Windowed);
            for(int i=0;i<20;i++)yield return new WaitForEndOfFrame();Check(Screen.width==2560&&Screen.height==1600,"render target actually reached2560x1600");
            var source=Resources.Load<GameObject>("Vehicles/CarConcept");Check(source&&session.Car.Visual.ImportedAppearance,"production starter uses the licensed imported asset");sourceFingerprint=Fingerprint(source);sourceMaterials=source.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();sourceColours=sourceMaterials.Select(m=>m.color).ToArray();
            var watch=System.Diagnostics.Stopwatch.StartNew();session.Car.Visual.Dent(session.Car.transform.TransformPoint(new Vector3(0,.7f,2.15f)),18,session.Car.transform.TransformDirection(Vector3.back));watch.Stop();dentMilliseconds=(float)watch.Elapsed.TotalMilliseconds;
            Check(session.Car.Visual.DeformationExtent>.1f&&session.Car.Visual.DeformationExtent<=.701f,"native imported collision deforms real panels within bounds");session.Car.Repair();Check(session.Car.Visual.DeformationExtent==0,"native repair restores undamaged appearance before timing");
            if(RoadMode)yield return BeginRoad();else yield return BeginRace(false);Check(fleet.Count(c=>c.Visual.ImportedAppearance)==(RoadMode?1:4),"as-built detailed vehicle appearances retained in "+(RoadMode?"road drive":"four-car race"));
            string productionLabel=RoadMode?"prepared-road-woodland":separatedObservers?"separated-four-driver-streaming-stress":"production-as-built-four-detail";
            yield return Measure(productionLabel);yield return Captures(productionLabel);
            if(!productionOnly)
            {
                yield return BeginRace(true);Check(fleet.Count(c=>c.Visual.ImportedAppearance)==2,"diagnostic comparison changes only two opponent appearances to procedural equivalents");
                yield return Measure("controlled-two-detail-comparison");yield return Captures("controlled-two-detail-comparison");
                RestoreFour();Check(fleet.All(c=>c.Visual.ImportedAppearance),"all four detailed appearances are restored after the diagnostic comparison");
            }
            Check(Math.Abs(Fingerprint(source)-sourceFingerprint)<.000001&&sourceMaterials.Select((m,i)=>m.color==sourceColours[i]).All(x=>x),"native racing, damage and comparison rebuilds preserve shared source assets");
            if(nightRain)Check(session.Conditions.Hour<3&&session.Conditions.Rain>.6f&&session.Conditions.Wetness>.5f,"requested rainy night actually has rain and accumulated road wetness");
            Check(session.Conditions.WeatherRequests==0&&session.Updates.SourceRequests==0,"benchmark performs no weather or map provider requests");Check(runtimeErrors==0,"native benchmark emits no runtime or shader errors");
            Check((File.Exists(realPath)?DataPackageRules.HashFile(realPath):"missing")==realHash,"real career remains byte-for-byte unchanged");
            var captures=stages.SelectMany(s=>new[]{prefix+"-"+s.name+"-chase.png",prefix+"-"+s.name+"-cockpit.png"}).ToArray();double captureDeadline=Time.realtimeSinceStartupAsDouble+5;
            while(captures.Any(p=>!File.Exists(p)||new FileInfo(p).Length<1024)&&Time.realtimeSinceStartupAsDouble<captureDeadline)yield return new WaitForEndOfFrame();
            Check(captures.All(p=>File.Exists(p)&&new FileInfo(p).Length>=1024),"both chase and cockpit captures are written for each measured stage");Complete(true,null);
        }
        OwnedCar Setup(VehicleController car,int index)
        {
            var owned=session.Profile.garage.Active.Clone();owned.model=0;owned.paint=(session.Profile.garage.Active.paint+index+1)%8;owned.wheels=index%3;owned.bodyKit=index%3;owned.damage=car.Damage;owned.dents=new List<SavedDent>(car.Visual.RecordedDents);return owned;
        }
        IEnumerator BeginRoad()
        {
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            session.Racing.Cancel();session.Car.Repair();var route=RoadDemoRoute.Build(session.World.Route.Region,Vector3.zero);
            // RoadDemoRoute is open: keep the centred heading sample inside its
            // real approach instead of looking through the artificial wrap seam.
            session.Car.Recover(route.At(3,route.LaneOffset(3)),route.Rotation(3));session.World.GeographicWorld.Refresh(session.Car.Body.position,true);
            double preparing=Time.realtimeSinceStartupAsDouble;session.ResumeDrive();bool capturedPreparation=false;
            while(session.PreparingStart)
            {
                yield return new WaitForEndOfFrame();if(!capturedPreparation&&session.PreparingRoadFrontier&&Time.realtimeSinceStartupAsDouble-preparing>.15){ScreenCapture.CaptureScreenshot(prefix+"-preparing.png");capturedPreparation=true;}
            }
            roadPreparationSeconds=Time.realtimeSinceStartupAsDouble-preparing;Check(!session.Menu&&session.World.GeographicWorld.DriveFrontierReady,"real ResumeDrive completes near-border frontier preparation before physical driving");
            fleet=new List<VehicleController>{session.Car};session.Car.NetworkDriven=true;assist=session.Car.gameObject.AddComponent<RaceDriver>();assist.Car=session.Car;assist.Session=session;assist.Route=route;assist.Index=0;assist.Difficulty=0;assist.RoadLane=true;assist.Fleet=fleet;session.View.Mode=0;
            double warm=Time.realtimeSinceStartupAsDouble;int frames=0;while(frames++<120||Time.realtimeSinceStartupAsDouble-warm<2)yield return new WaitForEndOfFrame();
        }
        IEnumerator BeginRace(bool comparison)
        {
            if(assist){assist.enabled=false;Destroy(assist);assist=null;}session.Racing.Cancel();session.Car.Repair();yield return new WaitForEndOfFrame();
            session.SetMenu(false);session.Racing.Begin(2);fleet=new List<VehicleController>{session.Car};fleet.AddRange(FindObjectsByType<VehicleController>(FindObjectsSortMode.None).Where(c=>c!=session.Car&&c.name.StartsWith("Circuit opponent ",StringComparison.Ordinal)).OrderBy(c=>c.name));
            Check(session.Racing.Entrants==4&&fleet.Count==4,"real Spa race retains four physics-driven entrants");
            if(comparison)for(int i=2;i<4;i++){var owned=Setup(fleet[i],i);fleet[i].Visual.Rebuild(owned,false);fleet[i].Visual.RestoreDents(owned.dents);}
            session.Car.NetworkDriven=true;assist=session.Car.gameObject.AddComponent<RaceDriver>();assist.Car=session.Car;assist.Route=session.World.Route;assist.Session=session;assist.Index=0;assist.Difficulty=session.Profile.raceDifficulty;assist.Lane=-2.7f;assist.Fleet=fleet;
            if(separatedObservers)
            {
                for(int i=1;i<fleet.Count;i++){float distance=session.World.Route.Length*i/fleet.Count;fleet[i].Recover(session.World.Route.At(distance),session.World.Route.Rotation(distance));}
                session.World.GeographicWorld.Refresh(session.Car.Body.position,true);
                Check(fleet.Skip(1).All(c=>Vector3.Distance(c.Body.position,session.Car.Body.position)>500),"stress fixture separates physical race drivers across distant circuit sectors");
            }
            session.View.Mode=0;
            while(session.Racing.Countdown>0)yield return new WaitForEndOfFrame();double warm=Time.realtimeSinceStartupAsDouble;int frames=0;
            while(frames++<120||Time.realtimeSinceStartupAsDouble-warm<2)yield return new WaitForEndOfFrame();
        }
        IEnumerator Measure(string label)
        {
            var sampleTimes=new List<double>();var data=new List<float>();var unscaled=new List<float>();var updates=new List<int>();var rendered=new List<int>();var deltas=new List<int>();var engine=new List<EngineTiming>();var timestamps=new HashSet<ulong>();var timingBuffer=new FrameTiming[8];
            session.World.GeographicWorld.ClearStreamingTelemetry();WorkTelemetry.Clear();surfaceProbeSamples=0;surfaceProbeFailures=0;surfaceProbeErrors.Clear();measuringSurfaceCoverage=true;
            var woodland=new WoodlandSummary{sourceTrees=session.World.Route.Region.trees?.Length??0,totalAllocatedMemoryBefore=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),graphicsDriverMemoryBefore=UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver()};
            var facades=new FacadeSummary();
            var position=session.Car.Body.position;float clock=RoadMode?Time.time:session.Racing.Clock,peak=0;double began=Time.realtimeSinceStartupAsDouble,previous=began;
            int firstUpdate=Time.frameCount,firstRendered=Time.renderedFrameCount,lastUpdate=firstUpdate,lastRendered=firstRendered,repeated=0,skipped=0;bool timingEnabled=false,timingFeatureEnabled=false,timingFloorEstablished=false;timingError="";
            try{timingEnabled=FrameTimingManager.IsFeatureEnabled();}catch(Exception e){timingError=e.Message;}
            timingFeatureEnabled=timingEnabled;captureEngineTimings=timingEnabled;lastTimingCapture=-1;
            // Discard the timing manager's previous-stage cache. Results lag
            // rendering by approximately four frames (Unity documentation).
            if(timingEnabled)try{uint count=FrameTimingManager.GetLatestTimings((uint)timingBuffer.Length,timingBuffer);for(int i=0;i<count;i++)timestamps.Add(timingBuffer[i].frameStartTimestamp);}catch(Exception e){timingError=e.Message;}
            while(data.Count<Samples||Time.realtimeSinceStartupAsDouble-began<MinimumSeconds)
            {
                // An explicit next-update yield prevents multiple end-of-frame
                // coroutine resumptions from masquerading as rendered frames.
                yield return null;yield return new WaitForEndOfFrame();
                CheckConfiguration();int update=Time.frameCount,render=Time.renderedFrameCount;
                if(update==lastUpdate||render==lastRendered){repeated++;continue;}
                if(update<lastUpdate||render<lastRendered)throw new InvalidOperationException("Frame counters moved backwards");
                double now=Time.realtimeSinceStartupAsDouble;float elapsed=(float)((now-previous)*1000);CheckSample(elapsed);int delta=render-lastRendered;
                var trees=DetailedWoodlandChunk.Statistics;if(trees.frame==update){woodland.framesWithDetailedTrees++;woodland.peakResidentTrees=Math.Max(woodland.peakResidentTrees,trees.residentTrees);woodland.peakSubmittedTrees=Math.Max(woodland.peakSubmittedTrees,trees.submittedTrees);woodland.peakNearTrees=Math.Max(woodland.peakNearTrees,trees.nearTrees);woodland.peakMediumTrees=Math.Max(woodland.peakMediumTrees,trees.mediumTrees);woodland.peakFarTrees=Math.Max(woodland.peakFarTrees,trees.farTrees);woodland.peakDrawSubmissions=Math.Max(woodland.peakDrawSubmissions,trees.drawSubmissions);woodland.peakSubmittedTriangles=Math.Max(woodland.peakSubmittedTriangles,trees.submittedTriangles);}
                var detail=BuildingFacadeChunk.Statistics;if(detail.frame==update&&detail.residentFeatures>0){facades.framesWithDetails++;facades.peakResidentFeatures=Math.Max(facades.peakResidentFeatures,detail.residentFeatures);facades.peakSubmittedWindows=Math.Max(facades.peakSubmittedWindows,detail.submittedWindows);facades.peakSubmittedDoors=Math.Max(facades.peakSubmittedDoors,detail.submittedDoors);facades.peakDrawSubmissions=Math.Max(facades.peakDrawSubmissions,detail.drawSubmissions);facades.peakSubmittedTriangles=Math.Max(facades.peakSubmittedTriangles,detail.submittedTriangles);}
                sampleTimes.Add(now);data.Add(elapsed);unscaled.Add(Time.unscaledDeltaTime*1000);updates.Add(update);rendered.Add(render);deltas.Add(delta);skipped+=Mathf.Max(0,delta-1);previous=now;lastUpdate=update;lastRendered=render;peak=Mathf.Max(peak,session.Car.AbsoluteSpeed);
                // Skip initial latency so warmup data cannot enter this stage.
                if(timingEnabled&&data.Count>8)
                {
                    try
                    {
                        uint count=FrameTimingManager.GetLatestTimings((uint)timingBuffer.Length,timingBuffer);
                        if(!timingFloorEstablished&&count>0)
                        {
                            for(int i=0;i<count;i++)timestamps.Add(timingBuffer[i].frameStartTimestamp);timingFloorEstablished=true;
                        }
                        for(int i=(int)count-1;i>=0;i--)
                        {
                            var timing=timingBuffer[i];if(timing.frameStartTimestamp==0||!timestamps.Add(timing.frameStartTimestamp))continue;
                            engine.Add(new EngineTiming{frameStartTimestamp=timing.frameStartTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),readFrameCount=update,readRenderedFrameCount=render,cpuFrameMs=SafeTiming(timing.cpuFrameTime),cpuMainThreadMs=SafeTiming(timing.cpuMainThreadFrameTime),cpuRenderThreadMs=SafeTiming(timing.cpuRenderThreadFrameTime),presentWaitMs=SafeTiming(timing.cpuMainThreadPresentWaitTime),gpuFrameMs=SafeTiming(timing.gpuFrameTime)});
                        }
                    }
                    catch(Exception e){timingError=e.Message;timingEnabled=false;captureEngineTimings=false;}
                }
            }
            captureEngineTimings=false;measuringSurfaceCoverage=false;double seconds=Time.realtimeSinceStartupAsDouble-began;var sorted=data.OrderBy(x=>x).ToArray();var throughput=data.Select(x=>1000/x).OrderBy(x=>x).ToArray();
            var stage=new Stage{sampleRealtimeSeconds=sampleTimes.ToArray(),name=label,frames=data.Count,width=Screen.width,height=Screen.height,importedCars=fleet.Count(c=>c.Visual.ImportedAppearance),frameMilliseconds=data.ToArray(),unityUnscaledFrameMilliseconds=unscaled.ToArray(),frameCounts=updates.ToArray(),renderedFrameCounts=rendered.ToArray(),renderedFrameDeltas=deltas.ToArray(),firstFrameCount=firstUpdate,lastFrameCount=lastUpdate,firstRenderedFrameCount=firstRendered,lastRenderedFrameCount=lastRendered,discardedRepeatedFrameCounters=repeated,skippedRenderedFrames=skipped,renderFrameInterval=OnDemandRendering.renderFrameInterval,measurementSeconds=seconds,observedRenderedThroughput=(lastRendered-firstRendered)/seconds,observedUpdateThroughput=(lastUpdate-firstUpdate)/seconds,medianFrameMs=Median(sorted),p95FrameMs=Percentile(sorted,.95f),medianWallClockThroughput=Median(throughput),p05WallClockThroughput=Percentile(throughput,.05f),p95WallClockThroughput=Percentile(throughput,.95f),minimumWallClockThroughput=throughput[0],maximumWallClockThroughput=throughput[throughput.Length-1],travelMetres=Vector3.Distance(position,session.Car.Body.position),raceClockAdvanced=(RoadMode?Time.time:session.Racing.Clock)-clock,peakSpeed=peak,damage=session.Car.Damage,frameTimingFeatureEnabled=timingFeatureEnabled,engineFrameTimings=engine.ToArray(),cpuFrame=Metric(engine.Select(t=>t.cpuFrameMs)),cpuMainThread=Metric(engine.Select(t=>t.cpuMainThreadMs)),cpuRenderThread=Metric(engine.Select(t=>t.cpuRenderThreadMs)),presentWait=Metric(engine.Select(t=>t.presentWaitMs)),gpuFrame=Metric(engine.Select(t=>t.gpuFrameMs))};
            woodland.totalAllocatedMemoryAfter=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();woodland.graphicsDriverMemoryAfter=UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();stage.woodland=woodland;
            if(woodland.sourceTrees>0)Check(woodland.framesWithDetailedTrees>100&&woodland.peakResidentTrees>0&&woodland.peakSubmittedTriangles>0,"source canopy instances produce real frame submissions");
            foreach(var chunk in session.World.GeographicWorld.GetComponentsInChildren<BuildingFacadeChunk>(true))facades.activeChunkOverflow+=chunk.OverflowCount;
            stage.facades=facades;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-roadRushProofExpectedFacades")>=0)Check(facades.framesWithDetails>100&&facades.peakSubmittedWindows>0&&facades.peakSubmittedDoors>0&&facades.activeChunkOverflow==0,"metric facade instances submit real windows and doors without chunk overflow");
            stage.criticalSurfaceSamples=surfaceProbeSamples;stage.criticalSurfaceFailures=surfaceProbeFailures;stage.criticalSurfaceErrors=surfaceProbeErrors.ToArray();
            Check(surfaceProbeSamples>0&&surfaceProbeFailures==0,"all measured physics steps retain complete critical terrain chunks around all measured drivers");
            stage.streaming=session.World.GeographicWorld.CaptureStreamingTelemetry();stage.workEvents=WorkTelemetry.Capture();stage.workEventsDropped=WorkTelemetry.Dropped;stage.p99FrameMs=Percentile(sorted,.99f);
            stage.frameTimingStatus=!string.IsNullOrEmpty(timingError)?"Partial/unavailable: "+timingError:!timingEnabled?"FrameTimingManager disabled or unavailable":stage.gpuFrame.available&&stage.cpuFrame.available?"Positive uniquely timestamped CPU and GPU timings available; review distributions separately":"Partial/unavailable: zero or no positive uniquely timestamped CPU/GPU readings";
            foreach(var car in fleet)
            {
                stage.activeCarRenderers+=car.Visual.GetComponentsInChildren<Renderer>().Count(r=>r.enabled&&r.gameObject.activeInHierarchy);
                foreach(var filter in car.Visual.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||!renderer.enabled||!filter.gameObject.activeInHierarchy||!filter.sharedMesh)continue;stage.activeCarMeshRenderers++;var mesh=filter.sharedMesh;for(int submesh=0;submesh<mesh.subMeshCount;submesh++)stage.activeCarTriangles+=(long)mesh.GetIndexCount(submesh)/3;
                }
            }
            stages.Add(stage);
            Check(stage.frames>=Samples&&stage.measurementSeconds>=MinimumSeconds&&stage.renderedFrameCounts.Distinct().Count()==stage.frames&&stage.frameCounts.Distinct().Count()==stage.frames&&stage.renderFrameInterval==1,"measurement covers twenty seconds and at least300 unique rendered/update frames for "+label);
            Check((RoadMode||session.Racing.Running&&session.Racing.Entrants==4)&&stage.travelMetres>1&&stage.raceClockAdvanced>.1f&&stage.peakSpeed>1,"physical driving and simulation clock progress throughout "+label);
            Debug.Log($"APPEARANCE TIMING {label}: {stage.frames} unique samples / {seconds:F2}s, observed rendering throughput {stage.observedRenderedThroughput:F1}/s (not displayedFPS), median interval {stage.medianFrameMs:F2} ms / p95 {stage.p95FrameMs:F2} ms; CPU {stage.cpuFrame.status}, GPU {stage.gpuFrame.status}; {stage.importedCars} imported cars.");
        }
        static double SafeTiming(double value)=>double.IsNaN(value)||double.IsInfinity(value)||value<0?-1:value;
        static void CheckConfiguration(){if(OnDemandRendering.renderFrameInterval!=1)throw new InvalidOperationException("Rendering interval changed during measurement");}
        static TimingMetric Metric(IEnumerable<double> values)
        {
            var all=values.ToArray();var positive=all.Where(v=>!double.IsNaN(v)&&!double.IsInfinity(v)&&v>0&&v<=float.MaxValue).Select(v=>(float)v).OrderBy(v=>v).ToArray();
            var result=new TimingMetric{available=positive.Length>0,totalRecords=all.Length,zeroRecords=all.Count(v=>v==0),invalidRecords=all.Count(v=>double.IsNaN(v)||double.IsInfinity(v)||v<0||v>float.MaxValue),samples=positive.Length,status=positive.Length==0?"Unavailable: no positive unique readings; zero can mean unsupported (or no present wait)":"Available positive-only distribution; zeros/invalid omitted, see raw records"};
            if(positive.Length>0){result.medianMs=Median(positive);result.p95Ms=Percentile(positive,.95f);result.p99Ms=Percentile(positive,.99f);result.minimumMs=positive[0];result.maximumMs=positive[positive.Length-1];}return result;
        }
        static void CheckSample(float elapsed){if(float.IsNaN(elapsed)||float.IsInfinity(elapsed)||elapsed<=0)throw new InvalidOperationException("Invalid actual rendered-frame interval");}
        static float Median(float[] values)=>values.Length%2==0?(values[values.Length/2-1]+values[values.Length/2])*.5f:values[values.Length/2];
        static float Percentile(float[] values,float percentile)=>values[Mathf.Clamp(Mathf.CeilToInt(percentile*values.Length)-1,0,values.Length-1)];
        IEnumerator Captures(string label)
        {
            session.View.Mode=0;for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-"+label+"-chase.png");for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            session.View.Mode=2;double ready=Time.realtimeSinceStartupAsDouble+1;while(Time.realtimeSinceStartupAsDouble<ready)yield return new WaitForEndOfFrame();
            Check(session.View.Mode==2&&Vector3.Distance(session.View.transform.position,session.Car.transform.TransformPoint(session.Car.Visual.CockpitEye))<.05f,"actual cockpit camera uses the car's interior eye during "+label);ScreenCapture.CaptureScreenshot(prefix+"-"+label+"-cockpit.png");for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();session.View.Mode=0;
            if(RoadMode&&(session.World.Route.Region.trees?.Length??0)>0)
            {
                var tree=session.World.Route.Region.trees.FirstOrDefault(t=>t.height>=12&&t.height<=25&&t.crownRadius>=3.5f)??session.World.Route.Region.trees[0];
                var centre=new Vector3(tree.x,session.World.GeographicWorld.MeshSurfaceHeight(tree.x,tree.z),tree.z);
                session.View.enabled=false;var eye=centre+new Vector3(tree.crownRadius*4,tree.height*.3f,-tree.crownRadius*4);
                session.View.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(centre+Vector3.up*tree.height*.58f-eye,Vector3.up));
                for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-woodland-close.png");for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();session.View.enabled=true;
            }
        }
        void RestoreFour()
        {for(int i=1;i<fleet.Count;i++)if(!fleet[i].Visual.ImportedAppearance){var owned=Setup(fleet[i],i);fleet[i].Visual.Rebuild(owned,true);fleet[i].Visual.RestoreDents(owned.dents);}}
        // Pure formatting keeps partial-session diagnostics useful even before
        // Conditions/Profile exist. It is not a readiness or outcome issuer.
        internal static string FormatBootState(bool present,bool ready,bool failed,string status)
            => "Boot session="+present+"; BootReady="+ready+"; BootFailed="+failed+"; BootStatus="+(status??"unavailable");

        void Complete(bool pass,string error)
        {
            if(finished)return;
            finished=true;
            bool outcome=pass;
            string failure=error;
            Report report=null;
            try
            {
                bool present=session,ready=present&&session.BootReady,failed=present&&session.BootFailed;
                string status=present?session.BootStatus:"No actual GameSession observed";
                var conditions=present?session.Conditions:null;
                var profile=present?session.Profile:null;
                BootTelemetry.Bundle timing=null;
                string telemetryError="",lastOperation="";
                if(!outcome&&BootTelemetry.Enabled&&!string.IsNullOrEmpty(PlayerProfile.StorageOverride))
                {
                    try
                    {
                        timing=BootTelemetry.Capture();
                        var lastBoot=timing.boots?.LastOrDefault();
                        lastOperation=lastBoot?.events?.LastOrDefault()?.operation??"";
                    }
                    catch(Exception capture)
                    {
                        outcome=false;telemetryError=capture.ToString();
                        failure=(failure??"Proof failed")+"\nBoot telemetry capture failed: "+capture.Message;
                    }
                }
                if(!outcome)failure=(failure??"Proof failed")+"\n"+FormatBootState(present,ready,failed,status);
                bool realUnchanged=realPath!=null&&(File.Exists(realPath)?DataPackageRules.HashFile(realPath):"missing")==realHash;
            report=new Report{sessionPresent=present,bootReady=ready,bootFailed=failed,bootStatus=status,conditionsAvailable=conditions,profileAvailable=profile!=null,bootTelemetryEnabled=BootTelemetry.Enabled,bootTelemetryCaptureError=telemetryError,bootTiming=timing,lastRecordedBootOperation=lastOperation,roadSource=roadRegionFile,requestedNightRain=nightRain,actualHour=conditions?conditions.Hour:0,actualRain=conditions?conditions.Rain:0,actualWetness=conditions?conditions.Wetness:0,roadPreparationSeconds=roadPreparationSeconds,runtimeErrorCount=runtimeErrors,requestedMeasurementSeconds=MinimumSeconds,productionOnly=productionOnly,separatedObservers=separatedObservers,status=outcome?"PASS":"FAIL",error=failure??"",build=Application.version,unity=Application.unityVersion,platform=Application.platform.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName,graphicsAPI=SystemInfo.graphicsDeviceType.ToString(),timingDefinition="At least20seconds and300 distinct Time.renderedFrameCount/Time.frameCount samples per stage; next-update plus end-of-frame yields; renderFrameInterval1. Raw wall-clock intervals and inverse-interval throughput are not displayedFPS. FrameTimingManager CPU/GPU values are separate, deduplicated by frameStartTimestamp and can arrive four frames late. Screenshot writes/warmup excluded. Documentation: https://docs.unity.com/en-us/engine/6000.0/manual/analysis/graphics-performance-profiling/profile-rendering/frame-timing-manager/manager",limitations="Functional PASS is separate from performance acceptance. GPU values of zero/no data are unavailable, not estimates. Metal tile-based GPUs can report GPU time greater than CPU/frame time. Display refresh is metadata, not measured presentedFPS. Measurements apply only to this native run, scene, resolution and device; no Windows or controller-hardware performance claim. The two-detail stage is an explicit isolated diagnostic comparison, not production behaviour or a quality downgrade. Car triangles count active MeshFilter indices; generated TextMesh and particle geometry is excluded, while renderer counts include their active renderers. Each race resets the isolated damage and grid; scene progress and real contacts can differ between samples.",profile=PlayerProfile.StoragePath,width=Screen.width,height=Screen.height,quality=profile!=null?profile.quality:-1,unityQualityLevel=QualitySettings.GetQualityLevel(),vSync=QualitySettings.vSyncCount,targetFrameRate=Application.targetFrameRate,renderFrameInterval=OnDemandRendering.renderFrameInterval,displayRefreshHz=Screen.currentResolution.refreshRateRatio.value,checks=checks.Count,realCareerUnchanged=realUnchanged,dentMilliseconds=dentMilliseconds,passed=checks.ToArray(),stages=stages.ToArray()};
                File.WriteAllText(prefix+"-performance.json",JsonUtility.ToJson(report,true));
                File.WriteAllText(prefix+"-result.txt",report.status+" appearance functional proof\n"+"checks="+checks.Count+"\nrealCareerUnchanged="+realUnchanged+"\n"+string.Join("\n",stages.Select(s=>$"{s.name}: {s.frames} unique frames over {s.measurementSeconds:F2}s, {s.importedCars} imported cars, {s.activeCarRenderers} renderers, {s.activeCarTriangles} triangles; median {s.medianFrameMs:F2} ms / p95 {s.p95FrameMs:F2} ms; observed rendered throughput {s.observedRenderedThroughput:F1}/s (not displayedFPS); CPU {s.cpuFrame.status} median {s.cpuFrame.medianMs:F2}ms/p95 {s.cpuFrame.p95Ms:F2}ms; GPU {s.gpuFrame.status} median {s.gpuFrame.medianMs:F2}ms/p95 {s.gpuFrame.p95Ms:F2}ms; travel {s.travelMetres:F1} m"))+"\n"+(failure??"Performance values require a separate review; no FPS threshold was used.")+"\n");
                Debug.Log("APPEARANCE PROOF "+(outcome?"PASS":"FAIL")+(failure==null?"":": "+failure));
            }
            catch(Exception reporting)
            {
                // A report/capture/partial-session failure must never strand the
                // native process or turn into a successful proof. Keep a minimal
                // error receipt even if the richer native snapshot is unavailable.
                outcome=false;
                failure=(failure??"Proof reporting failed")+"\nReporting failure: "+reporting;
                try
                {
                    if(report!=null){report.status="FAIL";report.error=failure;File.WriteAllText(prefix+"-performance.json",JsonUtility.ToJson(report,true));}
                }
                catch(Exception retry){failure+="\nReport write unavailable: "+retry.Message;}
                try{File.WriteAllText(prefix+"-result.txt","FAIL appearance functional proof\n"+failure+"\n");}
                catch(Exception write){failure+="\nFailure receipt unavailable: "+write.Message;}
                try{Debug.LogError("Appearance proof failed: "+failure);}catch(Exception){}
            }
            finally
            {
                // Always attempted, including failures while reading a partial
                // session, capturing timing or serializing/writing the report.
                Application.Quit(outcome?0:1);
            }
        }
    }
}
