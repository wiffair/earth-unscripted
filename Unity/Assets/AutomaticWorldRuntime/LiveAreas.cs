using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RoadRush.NewbridgePrototype
{
    /// <summary>
    /// Live numeric-area lifecycle. A loopback companion fetches public sources
    /// and prepares immutable cache entries on a worker. Unity downloads and
    /// verifies bytes, parses plain data off the main thread, then installs its
    /// indexes between chunk jobs. Each 1,536 m core owns its chunks; the 768 m
    /// source halo supplies seam anchors without replacing geometry under a car.
    /// No whole future route or environment is bundled with this prototype.
    /// </summary>
    public sealed partial class NewbridgeWorld
    {
        const float AreaSizeMetres = 1536;
        const float DataLookAheadMetres = 1800;
        // Boot needs an immediate supported driving window. Farther branches
        // retain the full forecast and are acquired by DataPump after readiness;
        // an unavailable optional provider must not block a safe local start.
        const float InitialDataLookAheadMetres=200;
        const int MaximumInitialAreas=12;
        const int MaximumResidentAreas = 16;
        const float CacheReuseDriveMetres = 3600;
        bool shortDrive;
        string worldTitle="Live road world",sourceAttribution="© OpenStreetMap contributors · Public elevation data";
        readonly HashSet<string> tunnelCaptures=new HashSet<string>();
        Vector3 destination;
        float destinationRadius, nextAreaX, nextAreaZ;
        float cruiseIntegral,lastSpeedTarget;
        float brakingSeconds,cruise70Seconds,cruise70BrakingSeconds;
        float maximumOverspeed, overspeedSeconds, distanceAt70Limit, distanceAbove68Mph;
        int forecastRequests, maximumForecastNodes, maximumBranchCandidates;
        FrontierReply lastForecast;
        Corridor[] branchWindows=Array.Empty<Corridor>();
        readonly Dictionary<Vector2Int,Map> areas = new Dictionary<Vector2Int,Map>();
        readonly Dictionary<Map,SpatialData> spatialByMap = new Dictionary<Map,SpatialData>();
        readonly List<AreaLoad> areaLoads = new List<AreaLoad>();
        Map workingMap;
        string endpoint, dataStatus = "Connecting to the local data worker";
        bool fetching, pumpRunning, recentDataFailure;
        Vector2Int pendingArea;
        readonly Dictionary<Vector2Int,double> areaRetryAfter=new Dictionary<Vector2Int,double>();
        float driveSeconds, currentGuideProgress;
        int failedRequests, evictedAreas;

        sealed class SpatialData
        {
            public readonly Dictionary<Vector2Int,List<Segment>> segments = new Dictionary<Vector2Int,List<Segment>>();
            public readonly Dictionary<Vector2Int,Dictionary<string,List<int>>> sourceSegments = new Dictionary<Vector2Int,Dictionary<string,List<int>>>();
            public readonly Dictionary<Vector2Int,HashSet<Feature>> features = new Dictionary<Vector2Int,HashSet<Feature>>();
            public readonly Dictionary<Vector2Int,List<Building>> buildings = new Dictionary<Vector2Int,List<Building>>();
        }
        [Serializable] sealed class AreaRequest
        {
            public float x,z,headingX,headingZ,playerSeconds,playerProgress;
            public string id;
            public Vector3 start;
            public bool coverageOnly;
            public TileID[] loaded;
        }
        [Serializable] sealed class TileID { public int x,z; }
        [Serializable] sealed class Candidate { public int tileX,tileZ; public float x,z,distanceMetres,priority; }
        [Serializable] sealed class Corridor { public Vector3 point; public float distanceMetres; }
        [Serializable] sealed class FrontierReply { public Candidate[] candidates; public Corridor[] corridors; public int reachableRoadNodes; }
        [Serializable] sealed class Destination { public float x,z,radiusMetres; public string name; }
        [Serializable] sealed class JobReply
        {
            public string id,state,error,url,sha256,signUrl,signSha256;public int signBytes;
            public int tileX,tileZ,bytes,seamSamples;
            public bool cached;
            public double generationSeconds,maximumSeamGap,idleSeconds;
            public DriveRoute route;
            public float nextX,nextZ;
        }
        [Serializable] sealed class Bootstrap
        {
            public Vector3 start;
            public string name,attribution;
            public float headingX,headingZ;
        }
        [Serializable] sealed class AreaLoad
        {
            public int tileX,tileZ,bytes,seamSamples;
            public bool cached,requestedDuringDrive,readyDuringDrive;
            public string sha256;
            public float requestPlayerSeconds,requestProgressMetres,readyProgressMetres;
            public double requestUTC,readyUTC,totalSeconds,parseMilliseconds,generationSeconds,maximumSeamGap,indexMilliseconds;
        }
        [Serializable] sealed class LiveReport
        {
            public string scope = "Actual live source acquisition, preparation and persistent-cache reuse; separate Mac prototype with local Python companion";
            public Report runtime;
            public List<AreaLoad> areas;
            public int failedRequests,evictedResidentAreas,residentAreas;
            public float startX,startZ,endX,endZ;
            public bool completed;
            public int forecastRequests,maximumForecastNodes,maximumBranchCandidates;
            public float brakingSeconds,cruise70Seconds,cruise70BrakingSeconds;
            public float maximumOverspeedMps,overspeedSeconds,distanceAt70LimitMetres,distanceAbove68MphMetres,distanceToDestinationMetres;
        }

        Map AreaAt(float x,float z)
        {
            areas.TryGetValue(new Vector2Int(Mathf.FloorToInt(x/AreaSizeMetres),Mathf.FloorToInt(z/AreaSizeMetres)),out var source);
            return source;
        }

        IEnumerator Start()
        {
            if(!IsPrototypePlayer)yield break;
            var arguments = Environment.GetCommandLineArgs();
            int endpointArgument = Array.IndexOf(arguments,"-liveEndpoint");
            endpoint = endpointArgument >= 0 && endpointArgument+1 < arguments.Length
                ? arguments[endpointArgument+1].TrimEnd('/') : "http://127.0.0.1:28978";
            if (!endpoint.StartsWith("http://127.0.0.1:",StringComparison.Ordinal))
                throw new InvalidDataException("Loopback worker endpoint required");
            shortDrive=Array.IndexOf(arguments,"-liveShortDrive")>=0;
            int destinationArgument=Array.IndexOf(arguments,"-liveDestination");
            string destinationPath=destinationArgument>=0&&destinationArgument+1<arguments.Length?arguments[destinationArgument+1]:Path.Combine(Application.streamingAssetsPath,"destination.json");
            var goal=JsonUtility.FromJson<Destination>(File.ReadAllText(destinationPath));
            destination=new Vector3(goal.x,0,goal.z);destinationRadius=goal.radiusMetres;
            int resumeArgument=Array.IndexOf(arguments,"-liveBootstrap");
            string bootstrapPath=resumeArgument>=0 && resumeArgument+1<arguments.Length?arguments[resumeArgument+1]:Path.Combine(Application.streamingAssetsPath,"bootstrap.json");
            var bootstrap = JsonUtility.FromJson<Bootstrap>(File.ReadAllText(bootstrapPath));
            yield return AcquireInitialMap(bootstrap);
            MakeMaterials();
            MakeLighting();
            view = new GameObject("Live world inspection / driving camera").AddComponent<Camera>();
            view.transform.SetParent(transform,false);
            view.nearClipPlane=.15f;view.farClipPlane=2500;view.fieldOfView=63;
            view.backgroundColor=new Color(.66f,.78f,.87f);view.clearFlags=CameraClearFlags.SolidColor;
            yield return PrepareForwardCoverage(bootstrap);
            MakeCar();
            ready=true;
            StartCoroutine(DistantTerrainLoop());
            report.initialReadySeconds=life.Elapsed.TotalSeconds;
            if (capturing)
            {
                File.WriteAllText(Path.Combine(output,"initial-ready.json"),JsonUtility.ToJson(report,true));
                yield return CaptureLiveDrive();
            }
            else { pumpRunning=true;StartCoroutine(DataPump()); }
        }

        IEnumerator AcquireInitialMap(Bootstrap bootstrap)
        {
            if(!string.IsNullOrEmpty(bootstrap.name))worldTitle=bootstrap.name;
            if(!string.IsNullOrEmpty(bootstrap.attribution))sourceAttribution=bootstrap.attribution;
            var request=new AreaRequest{x=bootstrap.start.x,z=bootstrap.start.z,start=bootstrap.start,headingX=bootstrap.headingX,headingZ=bootstrap.headingZ};
            for(int attempt=0;attempt<3 && map==null;attempt++)
            {
                yield return AcquireArea(request,true);
                if(map==null)yield return new WaitForSecondsRealtime(2);
            }
            if(map?.routes==null || map.routes.Length==0 || map.routes[0]?.points==null || map.routes[0].points.Length<2)
                throw new InvalidDataException("Initial area could not be acquired: "+dataStatus);
        }

        IEnumerator PrepareForwardCoverage(Bootstrap bootstrap)
        {
            // A restart may be within metres of a core edge. Acquire the first
            // 40 m of supported route before allowing a physical spawn there.
            foreach(var point in map.routes[0].points.Take(25).ToArray())
                if(AreaAt(point.x,point.z)==null)
                    yield return AcquireArea(new AreaRequest{x=point.x,z=point.z,start=point,headingX=bootstrap.headingX,headingZ=bootstrap.headingZ,coverageOnly=true},false);
            observer=map.routes[0].points[Mathf.Min(5,map.routes[0].points.Length-1)];
            yield return RefreshRoadFrontier();
            // Spend initial loading time establishing the nearby reachable
            // road window. Starting 200 m from a core boundary otherwise gives
            // a cold provider too little time before the first driving stop.
            // Forecasting includes all branches; no complete future route is
            // prepared. A failed optional core stays on the normal retry path.
            var initialFailures=new HashSet<Vector2Int>();
            while(areas.Count<MaximumInitialAreas)
            {
                // Load the currently known reachable frontier as one batch.
                // Rebuilding the full graph after every single core repeats
                // work; refresh once the batch is resident, then discover any
                // additional branches. The same distance/core limits apply.
                var candidates=lastForecast?.candidates?.Where(item=>item.distanceMetres<=InitialDataLookAheadMetres &&
                    !areas.ContainsKey(new Vector2Int(item.tileX,item.tileZ)) &&
                    !initialFailures.Contains(new Vector2Int(item.tileX,item.tileZ))).Take(MaximumInitialAreas-areas.Count).ToArray();
                if(candidates==null || candidates.Length==0)break;
                foreach(var candidate in candidates)
                {
                    var key=new Vector2Int(candidate.tileX,candidate.tileZ);
                    if(areas.ContainsKey(key))continue;
                    yield return AcquireArea(new AreaRequest{x=candidate.x,z=candidate.z,start=observer,headingX=bootstrap.headingX,headingZ=bootstrap.headingZ,coverageOnly=true},false);
                    if(!areas.ContainsKey(key))initialFailures.Add(key);
                }
                yield return RefreshRoadFrontier();
            }
            yield return EnsureAround(observer);
        }

        void ReconcileGuideHeights(Map source)
        {
            using var work=RoadRush.WorkTelemetry.Measure("world.guide-heights",null,8);
            var route=map.routes[0];
            if(route.sourceWays==null)return;
            var spatial=spatialByMap[source];
            for(int i=0;i<route.points.Length;i++)
            {
                var point=route.points[i];
                if(AreaAt(point.x,point.z)!=source)continue;
                var cell=new Vector2Int(Mathf.FloorToInt(point.x/SegmentCellSize),Mathf.FloorToInt(point.z/SegmentCellSize));
                if(!spatial.segments.TryGetValue(cell,out var segments))continue;
                float nearest=.03f;float height=point.y;
                foreach(var segment in segments)
                {
                    if(segment.feature.category!="road"||segment.feature.sourceId!=route.sourceWays[i])continue;
                    // Guide points follow the running-road centre. Spatial
                    // envelopes use the full pavement centre after an
                    // asymmetric hard shoulder is added; use canonical
                    // segments here so the shoulder cannot prevent a match.
                    var running=segment;running.a=segment.centreA;running.b=segment.centreB;
                    float distance=Project(running,point.x,point.z,out float candidate);
                    if(distance<nearest){nearest=distance;height=candidate;}
                }
                point.y=height;route.points[i]=point;
            }
            UpdateGuideIndex();
        }

        void UpdateGuideIndex()
        {
            var route=map.routes[0];
            route.stations=new float[route.points.Length];
            for(int i=1;i<route.points.Length;i++)
                route.stations[i]=route.stations[i-1]+Vector3.Distance(route.points[i-1],route.points[i]);
            report.routeLengthMetres=route.stations.Last();
        }

        IEnumerator AcquireArea(AreaRequest request,bool initial)
        {
            fetching=true;
            pendingArea=new Vector2Int(Mathf.FloorToInt(request.x/AreaSizeMetres),Mathf.FloorToInt(request.z/AreaSizeMetres));
            var watch=Stopwatch.StartNew();
            var load=new AreaLoad { requestUTC=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0,
                requestPlayerSeconds=driveSeconds,requestProgressMetres=currentGuideProgress,requestedDuringDrive=measuringDrive };
            dataStatus="Fetching source data / preparing next area…";
            string payload=JsonUtility.ToJson(request);
            JobReply reply;
            using(var submit=new UnityWebRequest(endpoint+"/request","POST"))
            {
                submit.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                submit.downloadHandler=new DownloadHandlerBuffer();submit.SetRequestHeader("Content-Type","application/json");submit.timeout=15;
                yield return submit.SendWebRequest();
                if(submit.result!=UnityWebRequest.Result.Success) { DataFailure(submit.error);yield break; }
                reply=JsonUtility.FromJson<JobReply>(submit.downloadHandler.text);
            }
            string identity=reply.id;
            while(reply.state=="pending")
            {
                yield return new WaitForSecondsRealtime(.5f);
                // A newly selected route can supersede a slow side-area job.
                // Cooperative cancellation retains complete raw inputs and
                // leaves the current collider/stopping-margin authority intact.
                var planned=!initial&&request.coverageOnly&&AreaAt(observer.x,observer.z)!=null?PlannedDataCandidate():null;
                if(planned!=null&&(planned.tileX!=pendingArea.x||planned.tileZ!=pendingArea.y))
                {
                    var cancel=ObserverRequest();cancel.id=identity;
                    using(var supersede=Post("/cancel",cancel))
                    {
                        yield return supersede.SendWebRequest();
                        if(supersede.result==UnityWebRequest.Result.Success)
                        {fetching=false;dataStatus="Preparing planned route first…";yield break;}
                    }
                }
                using(var poll=UnityWebRequest.Get(endpoint+"/job/"+identity))
                {
                    poll.timeout=15;yield return poll.SendWebRequest();
                    if(poll.result!=UnityWebRequest.Result.Success) { DataFailure(poll.error);yield break; }
                    reply=JsonUtility.FromJson<JobReply>(poll.downloadHandler.text);
                }
                // Official providers may throttle a cold area for several
                // minutes. Progress resets the worker watchdog; HTTP polling
                // still has its own 15 s network timeout. The car holds its
                // supported stopping margin while source preparation proceeds.
                if(reply.state=="pending" && reply.idleSeconds>180) { DataFailure("Data worker stopped making progress");yield break; }
            }
            if(reply.state!="ready") { DataFailure(reply.error??"Area unavailable");yield break; }
            byte[] bytes;
            using(var download=UnityWebRequest.Get(endpoint+reply.url))
            {
                download.timeout=30;yield return download.SendWebRequest();
                if(download.result!=UnityWebRequest.Result.Success) { DataFailure(download.error);yield break; }
                bytes=download.downloadHandler.data;
            }
            var parseWatch=Stopwatch.StartNew();
            var parsing=Task.Run(() =>
            {
                using(var hash=SHA256.Create())
                {
                    string digest=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                    if(digest!=reply.sha256 || bytes.Length!=reply.bytes) throw new InvalidDataException("Prepared area hash/size mismatch");
                }
                return JsonUtility.FromJson<Map>(Encoding.UTF8.GetString(bytes));
            });
            while(!parsing.IsCompleted) yield return null;
            if(parsing.IsFaulted) { DataFailure(parsing.Exception.ToString());yield break; }
            var source=parsing.Result;
            if(source.format!="LiveWorldNumeric1" || source.heights.Length!=source.nx*source.nz)
            { DataFailure("Numeric area schema mismatch");yield break; }
            // Optional source-bound sign context is independent of geometry cache identity.
            if(!string.IsNullOrEmpty(reply.signUrl))
            {
                using(var signs=UnityWebRequest.Get(endpoint+reply.signUrl))
                {
                    signs.timeout=15;yield return signs.SendWebRequest();
                    if(signs.result==UnityWebRequest.Result.Success)
                    {
                        var signBytes=signs.downloadHandler.data;
                        var applying=Task.Run(()=>SignContextData.Apply(source,signBytes,reply.signSha256,reply.signBytes,reply.sha256));
                        while(!applying.IsCompleted)yield return null;
                        if(applying.IsFaulted)UnityEngine.Debug.LogWarning("UK sign context rejected; numeric roads retained: "+applying.Exception.GetBaseException().Message);
                    }
                    else UnityEngine.Debug.LogWarning("UK sign context unavailable; numeric roads retained");
                }
            }
            load.parseMilliseconds=parseWatch.Elapsed.TotalMilliseconds;
            while(building) yield return null;
            var key=new Vector2Int(source.tileX,source.tileZ);
            if(!areas.ContainsKey(key))
            {
                var indexWatch=Stopwatch.StartNew();
                var indexing=Task.Run(()=>{CacheJunctionPaving(source,reply.sha256);return BuildSpatialIndex(source);});
                while(!indexing.IsCompleted)yield return null;
                if(indexing.IsFaulted){DataFailure(indexing.Exception.ToString());yield break;}
                while(building)yield return null;
                load.indexMilliseconds=indexWatch.Elapsed.TotalMilliseconds;
                spatialByMap.Add(source,indexing.Result);areas.Add(key,source);
                finishedSourceHashes[source] = reply.sha256;
            }
            if(initial)
            {
                // The guide holder owns no terrain/features: retiring the initial
                // core can therefore release its numeric arrays too.
                map=new Map {frame=source.frame,routes=new[]{reply.route}};UpdateGuideIndex();
                nextAreaX=reply.nextX;nextAreaZ=reply.nextZ;
            }
            else if(!request.coverageOnly && reply.route?.points!=null) InstallGuide(reply);
            ReconcileGuideHeights(source);
            load.tileX=source.tileX;load.tileZ=source.tileZ;load.bytes=bytes.Length;load.cached=reply.cached;load.sha256=reply.sha256;
            load.generationSeconds=reply.generationSeconds;load.seamSamples=reply.seamSamples;load.maximumSeamGap=reply.maximumSeamGap;
            load.readyUTC=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0;load.totalSeconds=watch.Elapsed.TotalSeconds;
            load.readyProgressMetres=currentGuideProgress;load.readyDuringDrive=measuringDrive;areaLoads.Add(load);
            dataStatus=(reply.cached?"Loaded prepared area from disk cache":"Downloaded and prepared new area")+" · "+areas.Count+" resident";
            fetching=false;
            recentDataFailure=false;
            lastReconcilePosition=new Vector3(float.MaxValue,0,0);
            geometryRevision++;
            if(capturing) File.WriteAllText(Path.Combine(output,"area-loads.json"),JsonUtility.ToJson(new LiveReport{areas=areaLoads},true));
            RetireDataAreas();
            AreasChanged?.Invoke();
        }

        void DataFailure(string message)
        {
            recentDataFailure=true;
            failedRequests++;
            if(fetching)areaRetryAfter[pendingArea]=life.Elapsed.TotalSeconds+15;
            dataStatus="Waiting for area data: "+message;
            fetching=false;
        }

        UnityWebRequest Post(string path,AreaRequest request)
        {
            var exchange=new UnityWebRequest(endpoint+path,"POST");
            exchange.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)));
            exchange.downloadHandler=new DownloadHandlerBuffer();exchange.SetRequestHeader("Content-Type","application/json");exchange.timeout=20;
            return exchange;
        }

        AreaRequest ObserverRequest()
        {
            var heading=embedded?embeddedHeading:car!=null?car.transform.forward:map.routes[0].points[Mathf.Min(6,map.routes[0].points.Length-1)]-map.routes[0].points[Mathf.Min(5,map.routes[0].points.Length-2)];
            return new AreaRequest {x=observer.x,z=observer.z,start=observer,headingX=heading.x,headingZ=heading.z,
                playerSeconds=driveSeconds,playerProgress=currentGuideProgress,
                loaded=areas.Keys.Select(key=>new TileID{x=key.x,z=key.y}).ToArray()};
        }

        IEnumerator RefreshRoadFrontier()
        {
            var forecastWatch=Stopwatch.StartNew();
            using(var exchange=Post("/frontier",ObserverRequest()))
            {
                yield return exchange.SendWebRequest();
                if(exchange.result!=UnityWebRequest.Result.Success) {DataFailure(exchange.error);yield break;}
                lastForecast=JsonUtility.FromJson<FrontierReply>(exchange.downloadHandler.text);
            }
            branchWindows=lastForecast.corridors??Array.Empty<Corridor>();
            recentDataFailure=false;
            geometryRevision++;
            report.forecastMilliseconds+=forecastWatch.Elapsed.TotalMilliseconds;
            report.forecastRequests++;
            forecastRequests++;
            maximumForecastNodes=Mathf.Max(maximumForecastNodes,lastForecast.reachableRoadNodes);
            maximumBranchCandidates=Mathf.Max(maximumBranchCandidates,lastForecast.candidates?.Length??0);
            // Reconcile branch geometry even when the car is stopped at a light
            // or data boundary. No destination or guide enters this forecast.
            lastReconcilePosition=new Vector3(float.MaxValue,0,0);
        }

        void InstallGuide(JobReply reply)
        {
            var route=map.routes[0];
            if(reply.route?.points==null || reply.route.points.Length<2 || Vector3.Distance(route.points.Last(),reply.route.points[0])>.05f)
                throw new InvalidDataException("Live route continuation does not join");
            route.points=route.points.Concat(reply.route.points.Skip(1)).ToArray();
            route.speedLimits=route.speedLimits.Concat(reply.route.speedLimits.Skip(1)).ToArray();
            route.speedLimitSources=route.speedLimitSources.Concat(reply.route.speedLimitSources.Skip(1)).ToArray();
            route.sourceWays=route.sourceWays.Concat(reply.route.sourceWays.Skip(1)).ToArray();
            UpdateGuideIndex();nextAreaX=reply.nextX;nextAreaZ=reply.nextZ;
        }

        IEnumerator ExtendDriverGuide()
        {
            var points=map.routes[0].points;var tail=points.Last();int previous=points.Length-2;
            while(previous>0 && Vector3.Distance(points[previous],tail)<30)previous--;
            var heading=Vector3.ProjectOnPlane(tail-points[previous],Vector3.up).normalized;
            bool hintReady=AreaAt(nextAreaX,nextAreaZ)!=null;
            using(var exchange=Post("/guide",new AreaRequest{x=hintReady?nextAreaX:tail.x,z=hintReady?nextAreaZ:tail.z,start=tail,headingX=heading.x,headingZ=heading.z,playerSeconds=driveSeconds,playerProgress=currentGuideProgress,loaded=areas.Keys.Select(key=>new TileID{x=key.x,z=key.y}).ToArray()}))
            {
                yield return exchange.SendWebRequest();
                if(exchange.result!=UnityWebRequest.Result.Success) {DataFailure(exchange.error);yield break;}
                var reply=JsonUtility.FromJson<JobReply>(exchange.downloadHandler.text);
                if(reply.state!="ready") {DataFailure(reply.error);yield break;}
                InstallGuide(reply);
            }
        }

        IEnumerator DataPump()
        {
            while(pumpRunning)
            {
                if(ready && !fetching && !fly)
                {
                    // Recovery/towing can relocate a normal-game observer
                    // beyond resident data. Acquire its own core before asking
                    // the resident road graph to forecast the next branches.
                    if(AreaAt(observer.x,observer.z)==null)
                    {
                        var current=ObserverRequest();current.coverageOnly=true;
                        yield return AcquireArea(current,false);
                        if(AreaAt(observer.x,observer.z)==null)
                        {yield return new WaitForSecondsRealtime(3);continue;}
                    }
                    var candidate=PlannedDataCandidate();
                    if(candidate==null)
                    {
                        yield return RefreshRoadFrontier();
                        candidate=lastForecast?.candidates?.FirstOrDefault(item=>!areaRetryAfter.TryGetValue(new Vector2Int(item.tileX,item.tileZ),out double retry)||life.Elapsed.TotalSeconds>=retry);
                    }
                    // A clipped or disconnected road graph can report no
                    // frontier at an artificial edge. Human driving must
                    // still acquire nearby cores, including while stopped.
                    if(candidate==null){candidate=NearbyDataCandidate();if(candidate!=null)report.emptyFrontierFallbackRequests++;}
                    if(candidate!=null)
                    {
                        var request=ObserverRequest();request.x=candidate.x;request.z=candidate.z;request.coverageOnly=true;
                        yield return AcquireArea(request,false);
                    }
                    // Only the acceptance driver's navigation uses the goal.
                    // Human driving still forecasts every reachable road exit.
                    var tail=map.routes[0].points.Last();
                    if(capturing && map.routes[0].stations.Last()-currentGuideProgress<DataLookAheadMetres && (AreaAt(nextAreaX,nextAreaZ)!=null||AreaAt(tail.x,tail.z)!=null))
                        yield return ExtendDriverGuide();
                }
                yield return new WaitForSecondsRealtime(recentDataFailure?3:1);
            }
        }

        Candidate PlannedDataCandidate()
        {
            if(plannedRoute==null||!plannedRoute.Active)return null;
            bool Skip(int x,int z){var key=new Vector2Int(x,z);return areas.ContainsKey(key)||(areaRetryAfter.TryGetValue(key,out double retry)&&life.Elapsed.TotalSeconds<retry);}
            if(plannedRoute.NextMissing(observer,3000,AreaSizeMetres,Skip,out var p,out float ahead))
                return new Candidate{tileX=Mathf.FloorToInt(p.x/AreaSizeMetres),tileZ=Mathf.FloorToInt(p.z/AreaSizeMetres),x=p.x,z=p.z,distanceMetres=ahead,priority=-2000+ahead};
            return null;
        }

        Candidate NearbyDataCandidate()
        {
            var heading=embedded?embeddedHeading:car?car.transform.forward:Vector3.forward;
            heading=Vector3.ProjectOnPlane(heading,Vector3.up).normalized;
            int cx=Mathf.FloorToInt(observer.x/AreaSizeMetres),cz=Mathf.FloorToInt(observer.z/AreaSizeMetres);
            int radius=Mathf.CeilToInt(DataLookAheadMetres/AreaSizeMetres);Candidate best=null;
            for(int dx=-radius;dx<=radius;dx++)for(int dz=-radius;dz<=radius;dz++)
            {
                var key=new Vector2Int(cx+dx,cz+dz);
                if(areas.ContainsKey(key)||(areaRetryAfter.TryGetValue(key,out double retry)&&life.Elapsed.TotalSeconds<retry))continue;
                float x=Mathf.Clamp(observer.x,key.x*AreaSizeMetres+.5f,(key.x+1)*AreaSizeMetres-.5f);
                float z=Mathf.Clamp(observer.z,key.y*AreaSizeMetres+.5f,(key.y+1)*AreaSizeMetres-.5f);
                var offset=new Vector3(x-observer.x,0,z-observer.z);float distance=offset.magnitude;
                if(distance>DataLookAheadMetres)continue;
                float priority=distance+180*(1-Vector3.Dot(offset.normalized,heading));
                if(best==null||priority<best.priority)best=new Candidate{tileX=key.x,tileZ=key.y,x=x,z=z,distanceMetres=distance,priority=priority};
            }
            return best;
        }

        void RetireDataAreas()
        {
            using var work=RoadRush.WorkTelemetry.Measure("world.retire-areas",null,8);
            if(areas.Count<=MaximumResidentAreas) return;
            foreach(var pair in areas.OrderByDescending(pair=>Vector2.Distance(new Vector2(observer.x,observer.z),new Vector2((pair.Key.x+.5f)*AreaSizeMetres,(pair.Key.y+.5f)*AreaSizeMetres))).ToArray())
            {
                if(areas.Count<=MaximumResidentAreas) break;
                float west=pair.Key.x*AreaSizeMetres,south=pair.Key.y*AreaSizeMetres;
                float dx=Mathf.Max(west-observer.x,0,observer.x-(west+AreaSizeMetres));
                float dz=Mathf.Max(south-observer.z,0,observer.z-(south+AreaSizeMetres));
                if(dx*dx+dz*dz<2000*2000)continue;
                if(plannedRoute!=null&&plannedRoute.Active)
                {
                    bool Planned(int x,int z)=>x!=pair.Key.x||z!=pair.Key.y;
                    if(plannedRoute.NextMissing(observer,3000,AreaSizeMetres,Planned,out _,out _))continue;
                }

                bool used=chunks.Keys.Any(key=>AreaAt((key.x+.5f)*ChunkSize,(key.y+.5f)*ChunkSize)==pair.Value);
                if(used) continue;
                PurgeWarmArea(pair.Key);
                areas.Remove(pair.Key);spatialByMap.Remove(pair.Value);finishedSourceHashes.Remove(pair.Value);evictedAreas++;
            }
        }

        IEnumerator CaptureLiveDrive()
        {
            car.StartLocked=true;car.Body.isKinematic=true;
            yield return Capture("00-live-start",RouteAt(30)+new Vector3(70,80,-80),RouteAt(80));
            var starting=car.transform.position;
            yield return DriveLive();
            var ending=car.transform.position;
            while(fetching) yield return null;
            yield return Capture("08-live-finish-overview",ending+new Vector3(80,90,-75),ending);
            report.allCaptureSeconds=life.Elapsed.TotalSeconds;
            var drive=report.drives.Last();
            bool pass=drive.completed && drive.maxDamage<.1f && drive.airborneSeconds<.1f && drive.missingRoadSamples==0;
            var result=new LiveReport {runtime=report,areas=areaLoads,failedRequests=failedRequests,evictedResidentAreas=evictedAreas,
                residentAreas=areas.Count,forecastRequests=forecastRequests,maximumForecastNodes=maximumForecastNodes,maximumBranchCandidates=maximumBranchCandidates,
                brakingSeconds=brakingSeconds,cruise70Seconds=cruise70Seconds,cruise70BrakingSeconds=cruise70BrakingSeconds,
                maximumOverspeedMps=maximumOverspeed,overspeedSeconds=overspeedSeconds,distanceAt70LimitMetres=distanceAt70Limit,distanceAbove68MphMetres=distanceAbove68Mph,
                distanceToDestinationMetres=Vector2.Distance(new Vector2(ending.x,ending.z),new Vector2(destination.x,destination.z)),startX=starting.x,startZ=starting.z,endX=ending.x,endZ=ending.z,completed=pass};
            File.WriteAllText(Path.Combine(output,"runtime.json"),JsonUtility.ToJson(result,true));
            Application.Quit(pass?0:2);
        }

        bool AtDestination()=>Vector2.Distance(new Vector2(car.transform.position.x,car.transform.position.z),new Vector2(destination.x,destination.z))<=destinationRadius;

        IEnumerator DriveLive()
        {
            SpawnRoute(0);car.StartLocked=true;
            yield return new WaitForSeconds(1);
            car.StartLocked=false;car.NetworkDriven=true;measuringDrive=true;
            car.NetworkThrottle=car.NetworkBrake=car.NetworkHandbrake=0;
            var result=new DriveResult {name=worldTitle};
            var last=car.transform.position;var timeout=Stopwatch.StartNew();
            float lastTrace=-1,lastSupport=0,lastProgressTime=0,bestProgress=0;
            int captureKilometre=0;
            string trace=Path.Combine(output,"drive.csv");
            File.WriteAllText(trace,"seconds,progress,x,y,z,speed,targetSpeed,damage,grounded,activeChunks,pendingChunks,lateralError,areas,availableGuide,limitMps,limitSource,throttle,brake\n");
            pumpRunning=true;StartCoroutine(DataPump());
            while(!(shortDrive?result.distance>=CacheReuseDriveMetres:AtDestination()) && driveSeconds<3000 && timeout.Elapsed.TotalSeconds<3400)
            {
                var position=car.transform.position;
                currentGuideProgress=ClosestStation(position,out int nearest,true);guideSegment=nearest;
                if(currentGuideProgress>bestProgress+1) {bestProgress=currentGuideProgress;lastProgressTime=driveSeconds;}
                float available=map.routes[0].stations.Last()-currentGuideProgress;
                float speed=car.AbsoluteSpeed;
                var local=car.transform.InverseTransformPoint(RouteAt(currentGuideProgress+Mathf.Clamp(12+speed*.75f,12,40)));
                float desiredDegrees=Mathf.Atan2(2*car.Metrics.Wheelbase*local.x,Mathf.Max(1,local.x*local.x+local.z*local.z))*Mathf.Rad2Deg;
                car.NetworkSteer=Mathf.Clamp(desiredDegrees/Mathf.Lerp(30,10,Mathf.Clamp01(speed/48)),-1,1);
                // Keep a 25 m stopping margin before the available guide ends.
                // This governor uses real brake input and permits a data wait.
                float targetSpeed=Mathf.Min(CurveSpeed(currentGuideProgress),Mathf.Sqrt(Mathf.Max(0,2*2*(available-25))));
                bool dataStop=!RoadReadyAt(currentGuideProgress+Mathf.Max(16,speed*2));
                bool holdForData=dataStop||available<=26;
                // Source-provider backoff can legitimately exceed the driving
                // stall timeout. Count only time when supported road is ready
                // and movement is permitted, so new data resumes the car.
                if(holdForData||fetching)lastProgressTime=driveSeconds;
                if(holdForData)targetSpeed=0;
                float legalLimit=AllowedSpeedAt(currentGuideProgress,out string limitSource);
                float error=targetSpeed-speed;
                if(targetSpeed<lastSpeedTarget-1.5f || dataStop)cruiseIntegral=0;
                // Do not wind up while acceleration already demands full
                // throttle; otherwise the first cruise interval overshoots.
                if(error*.7f+cruiseIntegral<.98f || error<0)
                    cruiseIntegral=Mathf.Clamp(cruiseIntegral+error*.12f*Time.deltaTime,0,.6f);
                lastSpeedTarget=targetSpeed;
                // PI removes steady cruise error. Coast through small geometry
                // changes; brake for genuine slowing and any legal-limit excess.
                bool legalBrake=speed>legalLimit+.08f;
                bool planBrake=speed>targetSpeed+.8f;
                car.NetworkThrottle=!holdForData&&!legalBrake&&!planBrake?Mathf.Clamp(error*.7f+cruiseIntegral,0,1):0;
                car.NetworkBrake=holdForData?Mathf.Clamp(speed*.25f+.6f,.6f,.95f):legalBrake?Mathf.Clamp((speed-legalLimit+.1f)*.7f,0,.85f):planBrake?Mathf.Clamp((speed-targetSpeed)*.25f,0,.85f):0;
                if(car.NetworkBrake>.03f){brakingSeconds+=Time.deltaTime;cruiseIntegral*=Mathf.Exp(-2*Time.deltaTime);}
                if(legalLimit>=31.29f && targetSpeed>30){cruise70Seconds+=Time.deltaTime;if(car.NetworkBrake>.03f)cruise70BrakingSeconds+=Time.deltaTime;}
                float step=Vector3.Distance(position,last);result.distance+=step;last=position;
                maximumOverspeed=Mathf.Max(maximumOverspeed,speed-legalLimit);
                if(speed>legalLimit+.44704f)overspeedSeconds+=Time.deltaTime;
                if(legalLimit>=31.29f) {distanceAt70Limit+=step;if(speed>=30.399f)distanceAbove68Mph+=step;}
                result.maxDamage=Mathf.Max(result.maxDamage,car.Damage);result.maximumSpeed=Mathf.Max(result.maximumSpeed,speed);
                if(!car.Grounded)
                {
                    result.airborneSeconds+=Time.deltaTime;
                    if(report.airborneSamples.Count<200 && (report.airborneSamples.Count==0 || driveSeconds-report.airborneSamples.Last().seconds>=.1f))
                        report.airborneSamples.Add(new AirborneSample{position=position,seconds=driveSeconds,station=currentGuideProgress,speed=speed,targetSpeed=targetSpeed});
                }
                var road=RouteAt(currentGuideProgress);
                float lateral=Vector2.Distance(new Vector2(position.x,position.z),new Vector2(road.x,road.z));
                result.maxLateralError=Mathf.Max(result.maxLateralError,lateral);
                observer=position;view.transform.position=position-car.transform.forward*8+Vector3.up*3.8f;
                view.transform.LookAt(position+Vector3.up+car.transform.forward*10);
                if(NeedsReconcile(position))StartCoroutine(EnsureAround(position));
                if(driveSeconds-lastSupport>.5f)
                {
                    lastSupport=driveSeconds;
                    if(!RoadReadyAt(currentGuideProgress+12))result.missingRoadSamples++;
                }
                if(driveSeconds-lastTrace>=1)
                {
                    lastTrace=driveSeconds;RecordFrontier(driveSeconds,currentGuideProgress);
                    File.AppendAllText(trace,string.Join(",",new object[]{driveSeconds,currentGuideProgress,position.x,position.y,position.z,speed,targetSpeed,car.Damage,car.Grounded,chunks.Count,report.pendingChunks,lateral,areas.Count,available,legalLimit,limitSource,car.NetworkThrottle,car.NetworkBrake})+"\n");
                    File.WriteAllText(Path.Combine(output,"live.json"),JsonUtility.ToJson(report.frontier.Last(),true));
                    WriteLiveDiagnostics();
                    var insideTunnel=Nearby(position.x,position.z)?.Select(segment=>segment.feature).FirstOrDefault(feature=>feature.tunnel&&feature.category=="road"&&feature.points.Any(point=>Vector2.Distance(new Vector2(point.x,point.z),new Vector2(position.x,position.z))<5));
                    if(insideTunnel!=null && Vector3.Distance(position,insideTunnel.points[0])>60 && Vector3.Distance(position,insideTunnel.points.Last())>60 && tunnelCaptures.Add(insideTunnel.sourceId))
                        ScreenCapture.CaptureScreenshot(Path.Combine(output,"tunnel-"+insideTunnel.sourceId+"-interior.png"));
                }
                if(result.distance>=(captureKilometre+1)*1000)
                {
                    captureKilometre++;yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(output,"route-"+captureKilometre+"km.png"));
                }
                if(car.Damage>.1f) {result.error="Physical collision: "+car.LastImpactObject;break;}
                if(lateral>10) {result.error="Car left the live route corridor";break;}
                if(driveSeconds-lastProgressTime>120 && available>50 && !fetching && RoadReadyAt(currentGuideProgress+40)) {result.error="No progress after data wait";break;}
                driveSeconds+=Time.deltaTime;yield return null;
            }
            pumpRunning=false;
            result.completed=shortDrive?result.distance>=CacheReuseDriveMetres:AtDestination();
            result.elapsedSeconds=driveSeconds;result.lastImpactObject=car.LastImpactObject??"";
            if(!result.completed && string.IsNullOrEmpty(result.error))result.error="Live drive timeout";
            report.drives.Add(result);measuringDrive=false;
            car.NetworkThrottle=0;car.NetworkBrake=1;car.StartLocked=true;car.Body.isKinematic=true;
            while(building)yield return null;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,result.completed?"07-live-drive-finish.png":"failed-drive.png"));
        }
    }
}
