using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RoadRush.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    [InitializeOnLoad] public static class GeoStreamingVerification
    {
        const string Flag="RRGeoStreamingVerification";static string root;static int phase,checks;static double deadline;static GeoWorld queued,baseline;static Transform observer,secondary;static readonly List<Transform> extras=new List<Transform>();static Mesh primitiveMesh,cancelledMesh;static int cancelFrame;static GameSession session;
        static GeoStreamingVerification(){if(!SessionState.GetBool(Flag,false))return;root=SessionState.GetString(Flag+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;}
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");root=Path.GetFullPath("Builds/Verification/streaming-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(new PlayerProfile{quality=0,volume=0,rumble=0,livingScenery=false}));SessionState.SetString(Flag+"Root",root);SessionState.SetBool(Flag,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string text){if(!value)throw new Exception("STREAMING FAIL: "+text);checks++;Debug.Log("STREAMING PASS: "+text);}
        static GeoRegion Fixture()
        {
            var region=new GeoRegion{id="streaming-fixture",name="Streaming fixture",minX=-2560,minZ=-2560,width=5120,depth=5120,nx=2,nz=2,heights=new[]{2f,2f,2f,2f},landNx=2,landNz=2,landCover=new int[4],waters=new[]{new GeoWater{name="Fixture river",width=12,points=new[]{new Vector3(-1800,-3,400),new Vector3(1800,-3,400)}}},areas=Array.Empty<GeoArea>(),buildings=new[]{new GeoBuilding{id="fixture-house",kind="house",height=7,roof="gabled",points=new[]{new Vector3(50,2,50),new Vector3(62,2,50),new Vector3(62,2,65),new Vector3(50,2,65),new Vector3(50,2,50)}}},roads=new[]{new GeoRoad{id="road",name="Fixture road",start="a",end="b",kind="residential",width=10,points=new[]{new Vector3(-2000,0,0),new Vector3(2000,0,0)}},new GeoRoad{id="bridge",name="Fixture bridge",start="c",end="d",kind="residential",width=10,bridge=true,points=new[]{new Vector3(400,9,250),new Vector3(400,9,550)}}},route=new[]{new Vector3(0,0,0),new Vector3(100,0,0),new Vector3(100,0,100),new Vector3(0,0,100),new Vector3(0,0,0)}};region.Prepare();return region;
        }
        static Dictionary<Vector2Int,GameObject> Chunks(GeoWorld world)=>(Dictionary<Vector2Int,GameObject>)typeof(GeoWorld).GetField("loaded",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
        static string Fingerprint(GameObject chunk)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
            {
                foreach(var t in chunk.GetComponentsInChildren<Transform>(true))
                {
                    writer.Write(t.name);foreach(float f in new[]{t.localPosition.x,t.localPosition.y,t.localPosition.z,t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w,t.localScale.x,t.localScale.y,t.localScale.z})writer.Write(f);
                    var filter=t.GetComponent<MeshFilter>();if(filter&&filter.sharedMesh)
                    {
                        var mesh=filter.sharedMesh;foreach(var p in mesh.vertices){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}foreach(var n in mesh.normals){writer.Write(n.x);writer.Write(n.y);writer.Write(n.z);}foreach(var uv in mesh.uv){writer.Write(uv.x);writer.Write(uv.y);}foreach(int index in mesh.triangles)writer.Write(index);
                    }
                    var meshCollider=t.GetComponent<MeshCollider>();if(meshCollider){writer.Write("meshCollider");writer.Write(meshCollider.enabled);writer.Write(meshCollider.convex);}
                    var box=t.GetComponent<BoxCollider>();if(box){writer.Write("boxCollider");writer.Write(box.enabled);foreach(float f in new[]{box.center.x,box.center.y,box.center.z,box.size.x,box.size.y,box.size.z})writer.Write(f);}
                }
                writer.Flush();return DataPackageRules.Hash(stream.ToArray());
            }
        }
        static GeoWorld Create(bool baselineMode)
        {GeoWorld.VerificationBaselineScheduling=baselineMode;try{var world=new GameObject(baselineMode?"Baseline streaming fixture":"Queued streaming fixture").AddComponent<GeoWorld>();world.Initialize(Fixture(),Geometry.Material("Streaming test asphalt",Color.gray));return world;}finally{GeoWorld.VerificationBaselineScheduling=false;}}
        static object InjectStage(Vector2Int key)
        {
            var type=typeof(GeoWorld);var job=type.GetMethod("StartStage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(queued,new object[]{key,"prefetch"});type.GetField("stage",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(queued,job);type.GetMethod("AdvanceStage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(queued,new[]{job});return job;
        }
        static bool Ready(GeoWorld world,IEnumerable<Vector3> positions)
        {foreach(var p in positions){int x=Mathf.FloorToInt(p.x/320),z=Mathf.FloorToInt(p.z/320);for(int zz=z-3;zz<=z+3;zz++)for(int xx=x-3;xx<=x+3;xx++){if(xx*320>=2560||(xx+1)*320<=-2560||zz*320>=2560||(zz+1)*320<=-2560)continue;if(!world.HasSurface(new Vector3(xx*320+160,0,zz*320+160)))return false;}}return true;}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Streaming verification timed out phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||!session.Updates)return;session.SetMenu(true);session.Input.enabled=false;session.Updates.Automatic=session.Updates.Enabled=false;session.World.gameObject.SetActive(false);
                    baseline=Create(true);baseline.gameObject.SetActive(false);queued=Create(false);observer=new GameObject("Streaming primary observer").transform;observer.position=new Vector3(20,0,20);queued.Observer=observer;
                    Check(baseline.BaselineScheduling&&!queued.BaselineScheduling,"baseline override is isolated and normal scheduling uses queued prefetch");Check(!GeoWorld.BaselineProofAllowed(new[]{"-roadRushStreamingBaseline"},PlayerProfile.StoragePath)&&GeoWorld.BaselineProofAllowed(new[]{"-roadRushStreamingBaseline","-roadRushAppearanceProof",root},root+"-profile.json"),"developer baseline flag requires its exact isolated appearance-proof profile");
                    Check(queued.LoadedChunks==49&&baseline.LoadedChunks==49,"force initialization immediately returns its complete original49chunks");
                    var a=Chunks(baseline);var b=Chunks(queued);Check(a.All(pair=>b.ContainsKey(pair.Key)&&Fingerprint(pair.Value)==Fingerprint(b[pair.Key])),"baseline and queued initial meshes, UVs, normals, indices and colliders are bit-identical");
                    var cache=queued.CaptureStreamingTelemetry();Check(cache.terrainCacheHits>0&&cache.terrainCacheGrids>0&&cache.terrainCachePeakGrids<=512,"terrain and road banks reuse an explicitly bounded exact-grid height memo");
                    bool same=true;var random=new System.Random(477);for(int i=0;i<2000;i++)
                    {
                        float x=(float)(random.NextDouble()*2400-1200),z=(float)(random.NextDouble()*2400-1200);float x0=Mathf.Floor(x/2.5f)*2.5f,z0=Mathf.Floor(z/2.5f)*2.5f,tx=(x-x0)/2.5f,tz=(z-z0)/2.5f;
                        float aa=queued.Region.TerrainHeight(x0,z0),bb=queued.Region.TerrainHeight(x0+2.5f,z0),cc=queued.Region.TerrainHeight(x0,z0+2.5f),dd=queued.Region.TerrainHeight(x0+2.5f,z0+2.5f);
                        float raw=tx+tz<=1?aa*(1-tx-tz)+bb*tx+cc*tz:dd*(tx+tz-1)+bb*(1-tz)+cc*(1-tx);same&=BitConverter.SingleToInt32Bits(raw)==BitConverter.SingleToInt32Bits(queued.MeshSurfaceHeight(x,z));
                    }
                    Check(same,"2000mixed-position height queries exactly match the original uncached triangle interpolation bit for bit");
                    var probe=GameObject.CreatePrimitive(PrimitiveType.Cylinder);probe.name="Shared primitive survivor";primitiveMesh=probe.GetComponent<MeshFilter>().sharedMesh;queued.ClearStreamingTelemetry();phase=1;return;
                }
                if(phase==1)
                {
                    if(queued.CaptureStreamingTelemetry().pendingBuilds>0)return;var stats=queued.CaptureStreamingTelemetry();Check(queued.LoadedChunks==81&&stats.prefetchBuilds==32,"stationary observer drains its32outerchunks to the bounded81chunk resident set");Check(stats.maxQueuedBuildsPerFrame==1,"actual update frames publish at most one outer chunk each");
                    var build=typeof(GeoWorld).GetMethod("Build",BindingFlags.Instance|BindingFlags.NonPublic);foreach(var pair in Chunks(queued))if(!Chunks(baseline).ContainsKey(pair.Key))build.Invoke(baseline,new object[]{pair.Key,"baseline-verification"});
                    Check(Chunks(queued).All(pair=>Fingerprint(pair.Value)==Fingerprint(Chunks(baseline)[pair.Key])),"all81chunks including32asynchronously prepared outer meshes are bit-identical to synchronous generation");Check(stats.stagedSlices>=32&&stats.maxSliceMilliseconds>0,"bounded preparation reports actual timed slices");observer.position=new Vector3(340,0,20);queued.Refresh(observer.position);Check(Ready(queued,new[]{observer.position})&&queued.CaptureStreamingTelemetry().requiredMisses==0,"cardinal crossing has immediate complete prefetched coverage without urgent builds");phase=2;return;
                }
                if(phase==2)
                {
                    var stats=queued.CaptureStreamingTelemetry();if(stats.pendingBuilds>0||stats.pendingRetires>0)return;Check(queued.LoadedChunks==81,"bounded retirement clears the previous outer row");observer.position=new Vector3(660,0,340);queued.Refresh(observer.position);Check(Ready(queued,new[]{observer.position})&&queued.CaptureStreamingTelemetry().requiredMisses==0,"diagonal crossing preserves every required chunk");phase=3;return;
                }
                if(phase==3)
                {
                    var stats=queued.CaptureStreamingTelemetry();if(stats.pendingBuilds>0||stats.pendingRetires>0)return;
                    observer.position=new Vector3(960,0,960);secondary=new GameObject("Streaming LAN observer").transform;secondary.position=new Vector3(-960,0,-960);queued.SecondaryObserver=secondary;
                    foreach(var p in new[]{new Vector3(960,0,-960),new Vector3(-960,0,960)}){var actor=new GameObject("Streaming race observer").transform;actor.position=p;extras.Add(actor);queued.RaceObservers.Add(actor);}
                    queued.Refresh(observer.position,true);Check(Ready(queued,new[]{observer.position,secondary.position}.Concat(extras.Select(t=>t.position))),"force registration synchronously covers four separated primary/LAN/race observers");Check(queued.LoadedChunks<=324,"four observer required coverage remains bounded");observer.position=new Vector3(-2240,0,2240);queued.Refresh(observer.position,true);Check(Ready(queued,new[]{observer.position,secondary.position}.Concat(extras.Select(t=>t.position))),"force teleport replaces stale queued plans without holes for any observer");phase=4;return;
                }
                if(phase==4)
                {
                    var stats=queued.CaptureStreamingTelemetry();if(stats.pendingBuilds>0||stats.pendingRetires>0)return;Check(queued.LoadedChunks<=324&&stats.maxQueuedBuildsPerFrame==1,"multi-observer prefetch drains without duplicate builds or budget violations");queued.SecondaryObserver=null;queued.RaceObservers.Clear();observer.position=new Vector3(20,0,20);queued.Refresh(observer.position,true);Physics.SyncTransforms();
                    Check(Ready(queued,new[]{observer.position}),"observer removal retains complete primary coverage");
                    bool road=Physics.RaycastAll(new Vector3(10,30,0),Vector3.down,60).Any(h=>h.collider.name=="Mapped road"&&h.collider.GetComponentInParent<GeoWorld>()==queued);Check(road,"published chunks contain actual driveable road colliders");Check(primitiveMesh&&UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Any(f=>f.sharedMesh==primitiveMesh),"streaming retirement never destroys shared Unity primitive meshes");
                    queued.ClearStreamingTelemetry();var record=typeof(GeoWorld).GetMethod("Record",BindingFlags.Instance|BindingFlags.NonPublic);for(int i=0;i<5000;i++)record.Invoke(queued,new object[]{Vector2Int.zero,"verification","bounded-ring",0f});stats=queued.CaptureStreamingTelemetry();Check(stats.events.Length==4096&&stats.dropped==904,"streaming telemetry remains bounded and reports dropped older events");
                    // An unfinished root must not expose surfaces or physics, and must be
                    // drained in place when the required window reaches it.
                    typeof(GeoWorld).GetMethod("Retire",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(queued,new object[]{new Vector2Int(4,0),"verification"});var staged=InjectStage(new Vector2Int(4,0));var hidden=(Transform)staged.GetType().GetField("root").GetValue(staged);
                    Check(!hidden.gameObject.activeInHierarchy&&!queued.HasSurface(new Vector3(1440,0,160)),"unfinished chunk root and its colliders remain unpublished");Check(queued.CaptureStreamingTelemetry().pendingBuilds>0,"pending count includes the active preparation job");
                    int urgent=queued.CaptureStreamingTelemetry().urgentStageFinishes;observer.position=new Vector3(340,0,20);queued.Refresh(observer.position,true);
                    Check(queued.CaptureStreamingTelemetry().urgentStageFinishes==urgent+1&&Chunks(queued)[new Vector2Int(4,0)]==hidden.gameObject&&Ready(queued,new[]{observer.position}),"required coverage finishes the same partially prepared root synchronously");
                    staged=InjectStage(new Vector2Int(-7,-7));hidden=(Transform)staged.GetType().GetField("root").GetValue(staged);var advance=typeof(GeoWorld).GetMethod("AdvanceStage",BindingFlags.Instance|BindingFlags.NonPublic);int steps=0;while(!hidden.GetComponentInChildren<MeshFilter>(true)&&steps++<10000)advance.Invoke(queued,new[]{staged});var ownedFilter=hidden.GetComponentInChildren<MeshFilter>(true);Check(ownedFilter&&ownedFilter.GetComponent<MeshCollider>()&&!hidden.gameObject.activeInHierarchy,"cancellation fixture has allocated its hidden terrain mesh and collider");cancelledMesh=ownedFilter.sharedMesh;cancelFrame=Time.frameCount;int cancelled=queued.CaptureStreamingTelemetry().cancelledStages;observer.position=new Vector3(2240,0,2240);queued.Refresh(observer.position,true);
                    Check(queued.CaptureStreamingTelemetry().cancelledStages==cancelled+1&&(!hidden||!hidden.gameObject.activeInHierarchy)&&Ready(queued,new[]{observer.position}),"teleport cancels an obsolete hidden job and immediately covers the new location");
                    phase=5;return;
                }
                if(phase==5)
                {
                    if(Time.frameCount<=cancelFrame)return;Check(!cancelledMesh&&primitiveMesh,"cancelled hidden-job owned mesh is destroyed on the next frame while shared primitives survive");
                    var replacement=Fixture();replacement.heights=new[]{17f,17f,17f,17f};queued.Initialize(replacement,queued.Asphalt);Check(queued.MeshSurfaceHeight(800,800)==17f,"region initialization invalidates old terrain memo even at previously cached coordinates");
                    File.WriteAllText("Builds/Verification/geo-streaming.txt","PASS "+checks+" actual streaming geometry/observer/force/queue/physics/ownership checks\n"+root+"\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Flag,false);GeoWorld.VerificationBaselineScheduling=false;EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
