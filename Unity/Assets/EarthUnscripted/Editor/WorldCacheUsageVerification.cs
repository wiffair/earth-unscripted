using System;
using System.IO;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class WorldCacheUsageVerification
    {
        [Serializable] sealed class RouteFixture {public Vector3[] points;}
        public static void Run()
        {
            PeerOsmVerification.Run();
            string root=Path.Combine(Path.GetTempPath(),"EarthUnscriptedCacheUsage-"+Guid.NewGuid().ToString("N"));int checks=0;
            void Need(bool ok,string why){checks++;if(!ok)throw new Exception("CACHE USAGE "+why);}
            try
            {
                Need(BenchmarkDrives.FocusQualified(Newtonsoft.Json.Linq.JObject.Parse("{\"focusedSeconds\":99,\"unfocusedSeconds\":1}")),"brief benchmark handoff focus loss stays bounded");
                Need(!BenchmarkDrives.FocusQualified(Newtonsoft.Json.Linq.JObject.Parse("{\"focusedSeconds\":40,\"unfocusedSeconds\":60}")),"background-heavy captures are not comparable display benchmarks");
                Need(!BenchmarkDrives.FocusQualified(new Newtonsoft.Json.Linq.JObject()),"legacy captures without focus timing are not silently qualified");
                Need(BenchmarkDrives.ValidScenario("dense-city")&&BenchmarkDrives.ValidScenario("open-road")&&BenchmarkDrives.ValidScenario("tunnel-bridge")&&BenchmarkDrives.ValidScenario("m4-m32")&&!BenchmarkDrives.ValidScenario("../escape"),"only fixed benchmark scenarios");
                Need(BenchmarkEndurance.RouteLength>32000&&BenchmarkEndurance.RouteLength<34000&&BenchmarkEndurance.TargetMph(0)==100&&BenchmarkEndurance.TargetMph(BenchmarkEndurance.RouteLength-50)==70,"long route and explicit motorway speed stages");
                var seed=Resources.Load<TextAsset>("Benchmarks/M4M32Start");var source=WorldSnapshotCodec.DecodeBootSource(seed.text,out _);Need(AutomaticWorldHost.Eligible(source)&&source.route.Length>=2,"bounded M4 boot seed is valid automatic-world input");
                Need(!BenchmarkDrives.IsIsolatedCache(root)&&BenchmarkDrives.IsIsolatedCache(Path.Combine(Path.GetTempPath(),"EarthUnscriptedBetaWorld-"+Guid.NewGuid().ToString("N"))),"benchmark cache cannot target owner folders");
                var worldRoot=new GameObject("Actual corridor source-preemption fixture");
                try
                {
                    var renderer=worldRoot.AddComponent<NewbridgePrototype.NewbridgeWorld>();renderer.enabled=false;
                    var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=renderer.GetType();
                    type.GetField("pendingArea",hidden).SetValue(renderer,new Vector2Int(5,-2));
                    var candidateType=type.GetNestedType("Candidate",System.Reflection.BindingFlags.NonPublic);var candidate=Activator.CreateInstance(candidateType,true);
                    candidateType.GetField("tileX").SetValue(candidate,5);candidateType.GetField("tileZ").SetValue(candidate,-1);
                    bool Yield()=> (bool)type.GetMethod("ShouldYieldDataRequest",hidden).Invoke(renderer,new[]{candidate});
                    type.GetField("immediateDrivingCorridor",hidden).SetValue(renderer,new[]{new Vector3(7900,0,-1600)});
                    Need(!Yield(),"nearer retry does not cancel useful work farther along actual driving corridor");
                    type.GetField("immediateDrivingCorridor",hidden).SetValue(renderer,new[]{new Vector3(6100,0,-100)});
                    Need(Yield(),"off-corridor source work can still yield to actual driving source");
                    // Reproduce the diagonal-core forecast disagreement seen
                    // near Bristol: physical halo skips a short earlier core,
                    // while route preparation correctly selects it again.
                    type.GetField("observer",hidden).SetValue(renderer,new Vector3(1700,0,-100));
                    type.GetField("pendingArea",hidden).SetValue(renderer,new Vector2Int(1,-1));
                    candidateType.GetField("tileX").SetValue(candidate,2);candidateType.GetField("tileZ").SetValue(candidate,-1);
                    renderer.SetPlannedRoute(new[]{new Vector3(1700,0,-100),new Vector3(4000,0,-100)},true);
                    Need(!Yield(),"earlier pending route core survives a farther halo forecast candidate");
                    type.GetField("observer",hidden).SetValue(renderer,new Vector3(1700,0,300));
                    Need(Yield(),"leaving the route removes its pending-core protection");
                    type.GetField("observer",hidden).SetValue(renderer,new Vector3(1700,0,-100));
                    candidateType.GetField("tileZ").SetValue(candidate,0);
                    Need(Yield(),"a genuine turn away from the planned route can supersede pending work");
                    type.GetField("pendingArea",hidden).SetValue(renderer,new Vector2Int(2,-1));
                    candidateType.GetField("tileX").SetValue(candidate,1);candidateType.GetField("tileZ").SetValue(candidate,-1);
                    Need(Yield(),"a genuinely earlier route core can preempt farther work");
                    renderer.SetPlannedRoute(null);Need(Yield(),"no cancelled or absent route retains source priority");
                    var routePoints=JsonUtility.FromJson<RouteFixture>(Resources.Load<TextAsset>("Benchmarks/M4M32Route").text).points;
                    var actualRoute=new NewbridgePrototype.RoutePreparation(routePoints);renderer.SetPlannedRoute(routePoints,true);
                    type.GetField("pendingArea",hidden).SetValue(renderer,new Vector2Int(11,-7));
                    candidateType.GetField("tileX").SetValue(candidate,11);candidateType.GetField("tileZ").SetValue(candidate,-8);
                    type.GetField("immediateDrivingCorridor",hidden).SetValue(renderer,Array.Empty<Vector3>());
                    bool OtherCore(int x,int z)=>x!=11||(z!=-7&&z!=-8);
                    for(int phase=0;phase<64;phase++)
                    {
                        var at=actualRoute.At(27700+phase);type.GetField("observer",hidden).SetValue(renderer,at);
                        Need(actualRoute.NextMissing(at,3072,1536,OtherCore,out var missing,out _)&&Mathf.FloorToInt(missing.x/1536)==11&&Mathf.FloorToInt(missing.z/1536)==-7,"actual 45m M32 corner core is selected at every former 64m sampling phase");
                        Need(!Yield(),"actual short M32 route core is never cancelled for the following core as the car advances");
                    }
                    renderer.SetPlannedRoute(new[]{new Vector3(1400,0,5),new Vector3(4000,0,5)},true);
                    type.GetField("observer",hidden).SetValue(renderer,new Vector3(1400,0,5));
                    type.GetField("pendingArea",hidden).SetValue(renderer,new Vector2Int(1,-1));
                    candidateType.GetField("tileX").SetValue(candidate,2);candidateType.GetField("tileZ").SetValue(candidate,0);
                    Need(!Yield(),"lane corridor retains an earlier source core just across the centreline boundary");
                    candidateType.GetField("tileZ").SetValue(candidate,1);Need(Yield(),"buffered corridor does not retain work against a genuine off-route turn");
                    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
                    {
                        var at=new Vector3(sx*10,0,sz*10);var diagonal=new NewbridgePrototype.RoutePreparation(new[]{at,new Vector3(sx*4000,0,sz*7000)});
                        int startX=Mathf.FloorToInt(at.x/1536),startZ=Mathf.FloorToInt(at.z/1536);
                        bool Current(int x,int z)=>x==startX&&z==startZ;
                        Need(diagonal.NextMissing(at,3000,1536,Current,out var missing,out _)&&Mathf.FloorToInt(missing.x/1536)==startX&&Mathf.FloorToInt(missing.z/1536)==(sz>0?1:-2),"exact core traversal preserves ownership in every coordinate quadrant");
                    }
                    var repeated=new NewbridgePrototype.RoutePreparation(new[]{Vector3.zero,Vector3.zero,new Vector3(4000,0,0)});
                    bool Origin(int x,int z)=>x==0&&z==0;
                    Need(repeated.NextMissing(Vector3.zero,3000,1536,Origin,out var afterRepeated,out _)&&Mathf.FloorToInt(afterRepeated.x/1536)==1,"repeated route vertices do not prevent forward source traversal");
                    Need(!repeated.NextMissing(Vector3.zero,1000,1536,Origin,out _,out _),"exact traversal never requests a core beyond its planning range");
                    renderer.SetPlannedRoute(null);
                    type.GetField("pendingArea",hidden).SetValue(renderer,new Vector2Int(5,-2));
                    candidateType.GetField("tileX").SetValue(candidate,5);candidateType.GetField("tileZ").SetValue(candidate,-2);Need(!Yield(),"same source tile never cancels itself");
                    type.GetField("immediateDrivingCorridor",hidden).SetValue(renderer,Array.Empty<Vector3>());
                    type.GetField("observer",hidden).SetValue(renderer,new Vector3(7800,0,-1000));candidateType.GetField("distanceMetres").SetValue(candidate,3000f);
                    bool Resident()=> (bool)type.GetMethod("NeedsResidentSource",hidden).Invoke(renderer,new[]{candidate});
                    Need(Resident(),"a nearby cached core is promoted even when its road approaches take a longer route");
                    type.GetField("observer",hidden).SetValue(renderer,Vector3.zero);
                    Need(!Resident(),"a genuinely distant source remains prepared-only");
                    var requestType=type.GetNestedType("AreaRequest",System.Reflection.BindingFlags.NonPublic);
                    var request=Activator.CreateInstance(requestType,true);requestType.GetField("start").SetValue(request,new Vector3(17,23,29));
                    foreach(var tile in new[]{new Vector2Int(3,-1),new Vector2Int(-1,-2),new Vector2Int(0,0),new Vector2Int(6510,-6510)})
                    {
                        type.GetMethod("CenterSourceRequest",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{request,tile.x,tile.y});
                        var roundtrip=JsonUtility.FromJson(JsonUtility.ToJson(request),requestType);
                        float x=(float)requestType.GetField("x").GetValue(roundtrip),z=(float)requestType.GetField("z").GetValue(roundtrip);
                        Need(Mathf.FloorToInt(x/1536)==tile.x&&Mathf.FloorToInt(z/1536)==tile.y,"serialized area request retains its authoritative forecast tile across positive and negative edges");
                    }
                    Need((Vector3)requestType.GetField("start").GetValue(request)==new Vector3(17,23,29),"tile selection never relocates the actual driving start");
                    type.GetField("fetching",hidden).SetValue(renderer,true);
                    type.GetMethod("DataFailure",hidden).Invoke(renderer,new object[]{"transient source fixture"});
                    float RetrySeconds()=>(float)type.GetMethod("InitialSourceRetrySeconds",hidden).Invoke(renderer,null);
                    Need(RetrySeconds()>14&&RetrySeconds()<=15,"startup honours the actual source failure retry deadline instead of spending retries during cooldown");
                    var deadlines=(System.Collections.Generic.Dictionary<Vector2Int,double>)type.GetField("areaRetryAfter",hidden).GetValue(renderer);deadlines[new Vector2Int(5,-2)]=-1;
                    Need(RetrySeconds()==2,"an expired source backoff retains only the normal short retry delay");
                }
                finally{UnityEngine.Object.DestroyImmediate(worldRoot);}
                var overlapRoot=new GameObject("Actual bounded source-overlap fixture");
                try
                {
                    var renderer=overlapRoot.AddComponent<NewbridgePrototype.NewbridgeWorld>();renderer.enabled=false;
                    var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=renderer.GetType();
                    void Set(string name,object value)=>type.GetField(name,hidden).SetValue(renderer,value);
                    var areas=(System.Collections.Generic.Dictionary<Vector2Int,NewbridgePrototype.NewbridgeWorld.Map>)type.GetField("areas",hidden).GetValue(renderer);
                    areas[Vector2Int.zero]=new NewbridgePrototype.NewbridgeWorld.Map();
                    Set("observer",new Vector3(1500,0,100));Set("pendingArea",new Vector2Int(1,0));
                    renderer.SetDrivingCorridor(new System.Collections.Generic.List<Vector3>{new Vector3(1500,0,100),new Vector3(1700,0,100),new Vector3(3200,0,100)},30);
                    renderer.SetPlannedRoute(new[]{new Vector3(100,0,100),new Vector3(5000,0,100)},true);
                    var next=type.GetMethod("AheadDataCandidate",hidden).Invoke(renderer,null);
                    var candidateType=type.GetNestedType("Candidate",System.Reflection.BindingFlags.NonPublic);
                    Need((int)candidateType.GetField("tileX").GetValue(next)==2,"ahead selection excludes the current unpublished core without publishing it early");
                    Need(areas.Count==1&&!renderer.HasPreparedAreaAt(new Vector3(1700,0,100)),"selection never claims the current pending numeric map is resident");
                    bool Can()=> (bool)type.GetMethod("CanPrepareAhead",hidden).Invoke(renderer,new[]{next});
                    Need(!Can(),"boot does not expand its preparation window for optional overlap");
                    Set("ready",true);Need(!Can(),"an inactive pump cannot start optional numeric work");
                    Set("pumpRunning",true);Need(Can(),"a ready driving session can overlap one nearby next core");
                    Set("fly",true);Need(!Can(),"flight mode cannot create a driving prefetch ticket");Set("fly",false);
                    candidateType.GetField("tileX").SetValue(next,5);Need(!Can(),"optional overlap rejects cores beyond the existing planned horizon");
                    Set("observer",new Vector3(2000,0,100));candidateType.GetField("tileX").SetValue(next,3);
                    Need(Can(),"one already forecast route-cache core can overlap admission beyond residency");
                    Set("observer",new Vector3(2000,0,500));Need(!Can(),"leaving the active route does not create distant speculative overlap");Set("observer",new Vector3(2000,0,100));
                    renderer.SetPlannedRoute(null,true);Need(!Can(),"without an active route overlap retains its original nearby-only bound");
                    renderer.SetPlannedRoute(new[]{new Vector3(100,0,100),new Vector3(5000,0,100)},true);
                    candidateType.GetField("tileZ").SetValue(next,2);Need(!Can(),"an unrelated far side core is not promoted by the presence of a route");candidateType.GetField("tileZ").SetValue(next,0);
                    Set("observer",new Vector3(1500,0,100));candidateType.GetField("tileX").SetValue(next,2);
                    var ticketType=type.GetNestedType("AheadPreparation",System.Reflection.BindingFlags.NonPublic);var ticket=Activator.CreateInstance(ticketType,true);
                    ticketType.GetField("tile").SetValue(ticket,new Vector2Int(1,0));ticketType.GetField("id").SetValue(ticket,"fixture-existing-job");Set("aheadPreparation",ticket);
                    Need(!Can(),"an occupied ticket prevents an unbounded speculative job queue");
                    var retained=type.GetMethod("ContinueAheadPreparation",hidden).Invoke(renderer,new[]{next});
                    Need((int)candidateType.GetField("tileX").GetValue(retained)==1,"the earlier useful route job remains selected while Unity finishes admission");
                    renderer.SetDrivingCorridor(new System.Collections.Generic.List<Vector3>(),0);Set("observer",new Vector3(1500,0,500));
                    var changed=type.GetMethod("ContinueAheadPreparation",hidden).Invoke(renderer,new[]{next});Need(ReferenceEquals(changed,next),"leaving the route permits a real new foreground request to supersede the ahead ticket");
                    var requestType=type.GetNestedType("AreaRequest",System.Reflection.BindingFlags.NonPublic);var request=Activator.CreateInstance(requestType,true);
                    requestType.GetField("x").SetValue(request,2304f);requestType.GetField("z").SetValue(request,100f);
                    var acquire=(System.Collections.IEnumerator)type.GetMethod("AcquireArea",hidden).Invoke(renderer,new[]{request,(object)false});
                    try
                    {
                        Need(acquire.MoveNext()&&acquire.Current is WaitForSecondsRealtime,"adopting the matching real acquisition ticket reaches its existing-job poll without submitting duplicate HTTP work");
                        Need(renderer.AheadPreparationReused==1&&type.GetField("aheadPreparation",hidden).GetValue(renderer)==null,"one acquisition consumes its ahead ticket exactly once");
                        Need(areas.Count==1,"ticket reuse cannot advertise source or geometry readiness before verification");
                    }
                    finally{(acquire as IDisposable)?.Dispose();}
                }
                finally{UnityEngine.Object.DestroyImmediate(overlapRoot);}
                var budgetRoot=new GameObject("Actual geometry frame-budget fixture");
                try
                {
                    var renderer=budgetRoot.AddComponent<NewbridgePrototype.NewbridgeWorld>();renderer.enabled=false;
                    var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=renderer.GetType();
                    var ready=type.GetField("ready",hidden);var frame=type.GetField("initialGeometryFrame",hidden);
                    var begin=type.GetMethod("BeginInitialGeometrySlice",hidden);var expired=type.GetMethod("GeometrySliceExpired",hidden);
                    bool Expired(double work)=>(bool)expired.Invoke(renderer,new object[]{work});
                    ready.SetValue(renderer,true);frame.SetValue(renderer,-1);begin.Invoke(renderer,null);
                    Need(!Expired(0),"cheap driving attachments can share the same frame");
                    System.Threading.Thread.Sleep(4);Need(Expired(0),"cumulative driving work exhausts the 2ms allowance");
                    frame.SetValue(renderer,-1);Need(!Expired(0),"a new driving frame receives a fresh allowance");
                    Need(Expired(3),"an individual over-budget driving operation yields");
                    ready.SetValue(renderer,false);frame.SetValue(renderer,-1);begin.Invoke(renderer,null);
                    Need(!Expired(3),"loading retains its larger 12ms allowance");
                    System.Threading.Thread.Sleep(15);Need(Expired(0),"loading cumulative budget remains bounded");
                }
                finally{UnityEngine.Object.DestroyImmediate(budgetRoot);}
                var signRoot=new GameObject("Actual underpass sign floor fixture");
                try
                {
                    var control=new NewbridgePrototype.NewbridgeWorld.Control{id="underpass-proof",kind="speed",value="40",y=18,dz=1,direction=1};
                    NewbridgePrototype.TrafficControlVisual.Create(signRoot.transform,control,7,"GB",-1,p=>18,p=>28,false,new Vector3(-5,18,0));
                    Need(Mathf.Abs(signRoot.transform.GetChild(0).position.y-18)<.001f,"actual underpass sign creation retains the source road floor");
                    var colliders=signRoot.GetComponentsInChildren<BoxCollider>();Need(colliders.Length>=2,"actual sign has its breakable post and panel collision");
                    foreach(var collider in colliders)Need(collider.bounds.max.y<22,"underpass sign colliders remain below the upper road");
                }
                finally{UnityEngine.Object.DestroyImmediate(signRoot);}
                Need(NewbridgePrototype.TrafficControlVisual.RoadsideBaseHeight(18,28)==18,"an underpass sign cannot rise onto the road above");
                Need(NewbridgePrototype.TrafficControlVisual.RoadsideBaseHeight(28,3)==28,"a bridge sign remains on its own deck");
                Need(NewbridgePrototype.TrafficControlVisual.RoadsideBaseHeight(18,18.4f)==18.4f,"ordinary verge height is retained");
                Need(NewbridgePrototype.TrafficControlVisual.RoadsideBaseHeight(18,float.NaN)==18,"invalid terrain cannot move a valid road sign");
                GeoRoad ProjectionRoad(string id,float x,float height,string kind,int direction)=>new GeoRoad{id=id,start=id+"a",end=id+"b",kind=kind,oneWay=direction,width=7,points=new[]{new Vector3(x,height,-100),new Vector3(x,height,100)}};
                var upper=ProjectionRoad("upper",0,40,"motorway",1);var lower=ProjectionRoad("lower",0,-10,"residential",1);var opposite=ProjectionRoad("opposite",3,40,"motorway",-1);
                var projectionRegion=new GeoRegion{roads=new[]{lower,upper,opposite},waters=Array.Empty<GeoWater>()};projectionRegion.RebuildRenderQueryIndexesOnly(true);
                var projection=projectionRegion.ProjectRoutePlanXZ(Vector3.zero,Vector3.forward);
                Need(projection.Road==upper&&projection.Point.y==40,"flat route plans use source road height and motorway heading, not zero terrain");
                Need(projectionRegion.ProjectRoutePlanXZ(Vector3.zero,Vector3.back).Road==opposite,"plan projection respects opposite one-way carriageways");
                Need(projectionRegion.ProjectRoutePlanXZ(new Vector3(500,0,0),Vector3.forward).Road==null,"missing local cell does not scan unrelated roads");
                WorkTelemetry.Clear();WorkTelemetry.SetRuntimeEnabled(true);
                using(WorkTelemetry.Measure("verification.runtime-toggle")){}
                Need(WorkTelemetry.Capture().Length==1,"runtime capture can enable without isolated profile");
                WorkTelemetry.SetRuntimeEnabled(false);Need(WorkTelemetry.Capture().Length==0,"disabling clears buffered work");
                using(WorkTelemetry.Measure("verification.disabled")){}
                Need(WorkTelemetry.Capture().Length==0,"disabled normal play records no scope");
                var profile=JsonUtility.FromJson<PlayerProfile>("{\"detailedPerformanceCapture\":true}");
                Need(profile.detailedPerformanceCapture&&JsonUtility.ToJson(profile).Contains("\"detailedPerformanceCapture\":true"),"capture preference round trips");
                foreach(string name in new[]{"Prepared","FinishedGeometry","Raw","OpenStreetMap","FarTerrain"})Directory.CreateDirectory(Path.Combine(root,name));
                File.WriteAllBytes(Path.Combine(root,"Prepared","a.json"),new byte[12]);File.WriteAllBytes(Path.Combine(root,"FinishedGeometry","a.eugeo"),new byte[20]);File.WriteAllBytes(Path.Combine(root,"Raw","a.tif"),new byte[30]);File.WriteAllBytes(Path.Combine(root,"OpenStreetMap","regional.osm.pbf"),new byte[40]);
                var snapshot=WorldCacheUsage.Measure(root);Need(snapshot.prepared==12&&snapshot.finished==20&&snapshot.sources==30&&snapshot.osm==40,"distinct map and terrain file inventories");Need(snapshot.sourceLimit==5&&snapshot.osmLimit==5&&snapshot.preparedLimit==5&&snapshot.finishedLimit==5,"all defaults 5GB");
                File.WriteAllText(Path.Combine(root,"storage-settings.json"),"{\"preparedGigabytes\":2,\"finishedGigabytes\":0,\"sourceGigabytes\":8}");snapshot=WorldCacheUsage.Measure(root);Need(snapshot.preparedLimit==2&&snapshot.finishedLimit==0&&snapshot.sourceLimit==8,"independent configured quotas");Need(snapshot.Summary.Contains("OSM")&&snapshot.Summary.Contains("terrain")&&snapshot.Summary.Contains("/8"),"stats expose sources and quota");
                File.WriteAllText(Path.Combine(root,"storage-settings.json"),"{\"preparedGigabytes\":3}");snapshot=WorldCacheUsage.Measure(root);Need(snapshot.sourceLimit==5,"old settings inherit source default");
                File.WriteAllText(Path.Combine(root,"storage-settings.json"),"{\"osmGigabytes\":9,\"sourceGigabytes\":2}");snapshot=WorldCacheUsage.Measure(root);Need(snapshot.osmLimit==9&&snapshot.sourceLimit==2,"OSM quota is independent of terrain and old settings");
                File.WriteAllText(Path.Combine(root,"storage-settings.json"),"invalid json");Need(WorldCacheUsage.Measure(root).sourceLimit==5,"bad settings safe default");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
            Debug.Log("WORLD_CACHE_USAGE_PASS "+checks);
        }
    }
}
