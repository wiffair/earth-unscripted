using System;using System.IO;using System.Linq;using System.Text;using System.Collections;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Reflection;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using RoadRush.Core800;using RoadRush.TerrainDraft;using RoadRush.TerrainDraft.Core800;
namespace RoadRush.Editor
{
 [InitializeOnLoad] public static class ScopedTown800NativeVerification
 {
  const string Flag="-roadRushTown800ProofOutput";static readonly bool Enabled;static readonly string OldProfile,OldRegions;
  static ScopedTown800NativeVerification(){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,Flag);if(i<0||i+1>=a.Length)return;string p=Path.GetFullPath(a[i+1]);if(!p.StartsWith("/private/tmp/rr019-town800-native-",StringComparison.Ordinal))throw new ArgumentException("Owned town proof output required");OldProfile=PlayerProfile.StorageOverride;OldRegions=RegionCache.OverrideFolder;PlayerProfile.StorageOverride=Path.Combine(p,"isolated","profile.json");RegionCache.OverrideFolder=Path.Combine(p,"isolated","Regions");Enabled=true;}
  static IEnumerator work;static string output,career,careerBefore,packetPath,rawPath,packetBefore,rawBefore;static double started;static int checks;static JObject report;static readonly List<string> errors=new List<string>();static readonly List<GameObject> objects=new List<GameObject>();static readonly List<Material> materials=new List<Material>();static PhysicalGround ground;static GeoWorld world;static GameObject sentinel;static Mesh sharedPrimitive;static Material road,terrain;static Camera camera;static ReviewedCanonicalRaw13Input input;static PreparedWorldMeshPlan800 plan;static byte[] rawOwned;static bool finished;static readonly CancellationTokenSource proofCancellation=new CancellationTokenSource();
  sealed class PhysicalGround
  {
   internal readonly PreparedWorldMeshPlan800 Plan;internal readonly GameObject Root;internal readonly Matrix4x4 Frame;internal readonly Mesh[] Meshes;internal int Generation{get;private set;}=1;internal bool Ready{get;private set;}internal GeoWorld AttachedTown;bool disposed;
   internal PhysicalGround(PreparedWorldMeshPlan800 p,GameObject root){Plan=p;Root=root;Frame=root.transform.localToWorldMatrix;Meshes=root.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();Need(root.activeInHierarchy&&root.transform.childCount==16&&Meshes.Length==p.PacketCount,"Actual16-root complete physical owner");foreach(var f in root.GetComponentsInChildren<MeshFilter>(true)){var mc=f.GetComponent<MeshCollider>();Need(mc&&mc.enabled&&mc.sharedMesh==f.sharedMesh&&f.GetComponent<GeneratedMeshOwner>()?.Mesh==f.sharedMesh,"Actual renderer/collider/owner binding before readiness");}Ready=true;}
   internal bool Current(int expected){if(!Ready||Generation!=expected||!Root||!Root.activeInHierarchy||Root.transform.childCount!=16)return false;var now=Root.transform.localToWorldMatrix;for(int i=0;i<16;i++)if(BitConverter.SingleToInt32Bits(now[i])!=BitConverter.SingleToInt32Bits(Frame[i]))return false;return true;}
   internal void Invalidate(bool notify=true){if(notify&&AttachedTown)AttachedTown.RevokeScopedTown800Support();Ready=false;Generation++;}
   internal void RearmForProof(){Need(!disposed&&Root&&Root.activeInHierarchy&&Meshes.All(m=>m),"Same unchanged physical buffers remain alive for new lease");Generation++;Ready=true;AttachedTown=null;}
   internal void Dispose(){if(disposed)return;disposed=true;Invalidate();Release(Root);foreach(var m in Meshes)if(m)UnityEngine.Object.DestroyImmediate(m);}
  }
  static void Need(bool okay,string why){checks++;if(!okay)throw new InvalidDataException(why);}
  static string Arg(string k){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,k);if(i<0||i+1>=a.Length)throw new ArgumentException(k);return a[i+1];}
  static string Hash(string p)=>File.Exists(p)?World13DocumentCodec.Hash(File.ReadAllBytes(p)):"missing";
  static void Release(GameObject g){if(g){Geometry.ReleaseMeshes(g);UnityEngine.Object.DestroyImmediate(g);}}
  static CanonicalRaw13Document FreshSource()=>CanonicalRaw13Codec.Read(input,input.CopyDocumentBytes());
  static void Log(string c,string s,LogType t){if((t==LogType.Error||t==LogType.Exception||t==LogType.Assert)&&errors.Count<20)errors.Add(c+"\n"+s);}
  public static void Run()
  {
   if(!Enabled)throw new InvalidOperationException("Explicit isolated town proof flag required; no side effects");
   try
   {
    Need(Enabled&&!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"Explicit isolated Editor invocation; no GameSession");output=Path.GetFullPath(Arg(Flag));Need(!Directory.Exists(output)&&!File.Exists(output),"Fresh proof output, retain failures");Directory.CreateDirectory(output);career=Path.Combine(Application.persistentDataPath,"road-rush-save.json");careerBefore=Hash(career);
    Need(UnityEngine.SceneManagement.SceneManager.sceneCount==1&&!UnityEngine.SceneManagement.SceneManager.GetSceneAt(0).isDirty&&UnityEngine.SceneManagement.SceneManager.GetSceneAt(0).rootCount==0,"Clean empty scene required");EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);report=new JObject{{"status","RUNNING"},{"scope","Explicit800m development source subset"},{"collisionPolicy","WALLS_ONLY plus existing stairs; NOT closed volumes"},{"normalWorldAdmission",false},{"registryIssued",false}};started=EditorApplication.timeSinceStartup;Application.logMessageReceived+=Log;work=Steps();EditorApplication.update+=Tick;
   }catch(Exception e){Finish(e);}
  }
  static void Tick(){if(finished)return;try{if(EditorApplication.timeSinceStartup-started>900)throw new TimeoutException("Bounded town proof900s");Need(!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"No hidden normal GameSession");if(!work.MoveNext())Finish(null);}catch(Exception e){Finish(e);}}
  static IEnumerator Steps()
  {
   string inputs=Path.GetFullPath(Arg("-roadRushTown800Inputs"));packetPath=Path.Combine(inputs,"ground-packets.json");rawPath=Path.Combine(inputs,"original-envelope.json");packetBefore=Hash(packetPath);rawBefore=Hash(rawPath);var producer=Task.Run(()=>CoreTerrainPreparation.BuildReviewedPantside800(packetPath,rawPath,proofCancellation.Token));while(!producer.IsCompleted)yield return null;var proof=producer.GetAwaiter().GetResult();Need(proof.FinalBufferDigest=="04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf","Reviewed complete final buffers");input=ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot("/private/tmp/rr019-integration-drafts/ActualPantside");rawOwned=input.CopyDocumentBytes();var planOwner=FreshSource();plan=PreparedWorldMeshPlan800.FromVerifiedCore(planOwner,proof);Need(planOwner.Write().SequenceEqual(rawOwned),"Plan did not transfer/mutate raw source");File.WriteAllBytes(Path.Combine(output,"owned-original-envelope.json"),rawOwned);
   var shader=Shader.Find("Universal Render Pipeline/Lit");Need(shader&&shader.isSupported,"Actual supported URP Lit");road=new Material(shader){name="Shared proof pavement"};terrain=new Material(shader){name="Shared proof ground"};materials.AddRange(new[]{road,terrain});road.SetColor("_BaseColor",new Color(.16f,.18f,.2f));terrain.SetColor("_BaseColor",new Color(.25f,.43f,.22f));foreach(var m in materials){m.SetFloat("_Metallic",0);m.SetFloat("_Smoothness",.1f);}sentinel=GameObject.CreatePrimitive(PrimitiveType.Cube);sentinel.name="Shared ownership sentinel";sentinel.SetActive(false);objects.Add(sentinel);sharedPrimitive=sentinel.GetComponent<MeshFilter>().sharedMesh;
   var stage=new GameObject("Actual physical800 owner16roots");stage.SetActive(false);objects.Add(stage);foreach(var key in plan.CopyRootKeys()){using(var upload=WorldMeshRootUpload800.BuildHidden(stage.transform,plan,key,r=>r==PreparedWorldMeshPlan800.Role.GroundRoad?road:terrain,default,()=>stage&&!stage.activeInHierarchy).GetEnumerator()){while(upload.MoveNext())yield return null;}}foreach(var mc in stage.GetComponentsInChildren<MeshCollider>(true))mc.gameObject.layer=30;stage.SetActive(true);Physics.SyncTransforms();VerifyExportPhysicalPackets(stage,plan);ground=new PhysicalGround(plan,stage);int groundGeneration=ground.Generation;bool Current()=>ground.Current(groundGeneration);
   report["sourceDigest"]=plan.SourceDigest;report["rawDocumentDigest"]=plan.RawDocumentDigest;report["finalBufferDigest"]=plan.FinalBufferDigest;report["supportGeometryDigest"]=plan.GeometryDigest;report["physicalRootEntityId"]=stage.GetEntityId().ToString();report["physicalRootGeneration"]=groundGeneration;report["physicalRootFrameBits"]=new JArray(Enumerable.Range(0,16).Select(i=>BitConverter.SingleToInt32Bits(ground.Frame[i]).ToString("x8")));report["physicalPacketMeshes"]=ground.Meshes.Length;
   // Cancel before town objects exist: worker may finish later, never attach.
   world=NewWorld("Town worker cancel");using(var cts=new CancellationTokenSource()){var start=world.BuildScopedTown800(FreshSource(),plan,cts.Token,Current);Need(start.MoveNext(),"Async preparation yields before mesh emission");cts.Cancel();bool cancelled=false;try{while(true){bool more;try{more=start.MoveNext();}catch(OperationCanceledException){cancelled=true;break;}if(!more)break;yield return null;}}finally{(start as IDisposable)?.Dispose();}Need(cancelled&&world.Region==null&&!world.HasSurface(Vector3.zero),"Cancelled preparation cannot expose or attach legacy terrain");bool missing=false;try{world.MeshSurfaceHeight(0,0);}catch(MissingScopedSupportException){missing=true;}Need(missing,"Cancelled support explicitly unavailable, no legacy fallback");}Release(world.gameObject);world=null;
   // Two partial allocations exercise real inactive-mesh teardown and stale generation.
   foreach(bool reinitialize in new[]{false,true})
   {
    world=NewWorld(reinitialize?"Town partial reinitialize":"Town partial destroy");var pending=world.BuildScopedTown800(FreshSource(),plan,default,Current);Mesh[] partial=null;try{int heartbeat=0;while(pending.MoveNext()){heartbeat++;partial=world.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Where(m=>m).ToArray();if(partial.Length>0)break;yield return null;}Need(partial!=null&&partial.Length>0,"Actual inactive wall mesh allocated before lifecycle mutation");Need(!world.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.gameObject.activeInHierarchy),"Partial town is hidden");if(reinitialize){var normal=AuthoredNormalFixture();world.Initialize(normal,road,true);VerifyNormalFixture(world,normal);int generation=world.SurfaceGeneration;bool stale=false;try{pending.MoveNext();}catch(OperationCanceledException){stale=true;}Need(stale&&ReferenceEquals(world.Region,normal)&&world.SurfaceGeneration==generation,"Stale town iterator cannot reset replacement normal world");}else{Release(world.gameObject);world=null;} (pending as IDisposable)?.Dispose();Need(partial.All(m=>!m)&&sharedPrimitive&&road&&terrain&&Current(),"Partial generated meshes released; physical ground/shared assets retained");report[reinitialize?"partialReinitializeReleased":"partialDestroyReleased"]=true;}finally{(pending as IDisposable)?.Dispose();if(world){Release(world.gameObject);world=null;}}
   }
   world=NewWorld("Actual scoped800 town");world.EntryAudit.Enabled=true;int townStartGeneration=world.SurfaceGeneration;var town=world.BuildScopedTown800(FreshSource(),plan,default,Current);try{while(town.MoveNext()){Need(Current(),"Ground owner stays bound throughout staged town");var townRoot=world.transform.childCount>0?world.transform.GetChild(0):null;if(townRoot)Need(!townRoot.gameObject.activeInHierarchy,"Town cannot publish partial buildings");yield return null;}}finally{(town as IDisposable)?.Dispose();}
   Need(Current()&&world.Region!=null&&world.SurfaceGeneration>townStartGeneration,"Town attached to exact current physical generation");Physics.SyncTransforms();var ledger=JObject.Parse(world.ExportScopedTownLedger800());File.WriteAllText(Path.Combine(output,"town-ledger.json"),ledger.ToString(Formatting.Indented));File.WriteAllText(Path.Combine(output,"entry-audit.json"),world.EntryAudit.ExportJson());Need((string)ledger["rawBefore"]==(string)ledger["rawAfter"]&&(string)ledger["rawDocumentDigest"]==plan.RawDocumentDigest,"Owned original geography unchanged");var rows=(JArray)ledger["buildings"];Need(rows.All(r=>(string)r["status"]=="Emitted"||(string)r["status"]=="Suppressed"),"Exactly terminal disposition for every rendering source entry");int originalCount=rows.Sum(r=>((JArray)r["originalIds"]).Count);Need(originalCount==world.Region.buildings.Length,"All original source rows accounted including coalesced/suppressed");Need(rows.Any(r=>(string)r["status"]=="Emitted")&&rows.Any(r=>(string)r["reason"]=="OutsideProvedSupportOrAccessReach4m"),"Actual town plus explicit boundary suppression");Need(!(bool)ledger["registryIssued"]&&!(bool)ledger["closedBuildingVolumes"],"No false registry or closed-volume claim");
   int wallRays=0,terrainRays=0,baseChecks=0;var walls=world.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.StartsWith("Building ",StringComparison.Ordinal)).ToArray();Need(walls.Length>0,"Actual existing wall MeshColliders");using(var writer=new JsonTextWriter(new StreamWriter(Path.Combine(output,"actual-wall-meshes.json"),false,new UTF8Encoding(false)))){writer.WriteStartArray();foreach(var collider in walls){collider.gameObject.layer=29;var mesh=collider.sharedMesh;Need(mesh&&collider.GetComponent<MeshFilter>()?.sharedMesh==mesh,"Wall collider and visible renderer share actual mesh");var v=mesh.vertices;var tris=mesh.triangles;var worldPoints=v.Select(p=>collider.transform.TransformPoint(p)).ToArray();writer.WriteStartObject();W(writer,"meshName",mesh.name);W(writer,"collisionPolicy","WALLS_ONLY");W(writer,"worldGeneration",world.SurfaceGeneration);W(writer,"physicsRootGeneration",groundGeneration);W(writer,"colliderEntityId",collider.GetEntityId().ToString());W(writer,"vertexCount",v.Length);writer.WritePropertyName("worldXYZ");writer.WriteStartArray();foreach(var p in worldPoints){writer.WriteValue(p.x);writer.WriteValue(p.y);writer.WriteValue(p.z);}writer.WriteEndArray();writer.WritePropertyName("triangles");writer.WriteStartArray();foreach(int i in tris)writer.WriteValue(i);writer.WriteEndArray();writer.WriteEndObject();
    for(int i=0;i<worldPoints.Length;i+=4){Need(i+3<worldPoints.Length,"Existing wall quad structure");foreach(int j in new[]{i,i+1}){var p=worldPoints[j];Need(plan.TryGround(p.x,p.z,out var q),"Every wall base has exact final support");Need(p.y<=q.Height-.1998,"Wall base extends below actual support, no floating foundation");baseChecks++;if(baseChecks%8==0){bool hit=Physics.Raycast(new Vector3(p.x,(float)q.Height+2,p.z),Vector3.down,out var h,4,1<<30,QueryTriggerInteraction.Ignore);Need(hit&&Math.Abs(h.point.y-q.Height)<=.0002,"Actual physical terrain supports wall sample");terrainRays++;}}
     var a=worldPoints[i];var b=worldPoints[i+1];var c=worldPoints[i+2];var d=worldPoints[i+3];var mid=(a+b+c+d)*.25f;var normal=Vector3.Cross(b-a,c-a).normalized;bool physical=collider.Raycast(new Ray(mid+normal*.4f,-normal),out var hitA,.8f)||collider.Raycast(new Ray(mid-normal*.4f,normal),out hitA,.8f);Need(physical&&Vector3.Distance(hitA.point,mid)<.001f,"Actual wall face physically raycasts (no closed-volume inference)");wallRays++;
    }
   }writer.WriteEndArray();}
   var sites=new JArray();foreach(var pair in world.BuildingSites){var site=pair.Value;foreach(var p in site.Samples){Need(plan.TryGround(p.x,p.z,out var q),"Every site sample uses proved final support");Need(Math.Abs(p.y-q.Height)<.0002,"Stored site sample height matches final query");}sites.Add(new JObject{{"partId",pair.Key},{"levelFloorY",site.LevelFloorY},{"foundationBottomY",site.FoundationBottomY},{"boundaryMinY",site.BoundaryMinY},{"boundaryMaxY",site.BoundaryMaxY},{"groundMedianY",site.GroundMedianY},{"samples",new JArray(site.Samples.Select(p=>new JArray(p.x,p.y,p.z)))}});}File.WriteAllText(Path.Combine(output,"actual-sites.json"),sites.ToString(Formatting.None));
   var features=new JArray();foreach(var chunk in world.GetComponentsInChildren<BuildingFacadeChunk>(true))foreach(var f in chunk.Features)features.Add(new JObject{{"id",f.Id},{"door",f.IsDoor},{"position",new JArray(f.Position.x,f.Position.y,f.Position.z)},{"rotation",new JArray(f.Rotation.x,f.Rotation.y,f.Rotation.z,f.Rotation.w)},{"width",f.Width},{"height",f.Height},{"sourceRoot",chunk.transform.parent?.name??""}});File.WriteAllText(Path.Combine(output,"actual-facade-features.json"),features.ToString(Formatting.None));Need(features.Any(f=>(bool)f["door"])&&features.Any(f=>!(bool)f["door"]),"Actual door and window plans present");
   var stairs=new JArray();foreach(var access in world.GetComponentsInChildren<BuildingAccessChunk>(true)){Need(access.OverflowCount==0,"No silent stair batch overflow");stairs.Add(new JObject{{"sourceRoot",access.name},{"flights",access.FlightCount},{"sealed",access.Sealed},{"meshColliders",access.GetComponentsInChildren<MeshCollider>(true).Count(c=>!c.name.StartsWith("Building ",StringComparison.Ordinal))}});}File.WriteAllText(Path.Combine(output,"actual-stairs.json"),stairs.ToString(Formatting.Indented));
   var sun=new GameObject("Town proof daylight");objects.Add(sun);sun.AddComponent<Light>().type=LightType.Directional;sun.GetComponent<Light>().intensity=1.1f;sun.transform.rotation=Quaternion.Euler(42,-35,0);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.58f,.6f);var cameraObject=new GameObject("Town proof camera");cameraObject.tag="MainCamera";objects.Add(cameraObject);camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.4f,.52f,.65f);camera.nearClipPlane=.05f;camera.farClipPlane=4000;camera.fieldOfView=50;
   var townRenderers=world.GetComponentsInChildren<MeshRenderer>(true);var bounds=townRenderers[0].bounds;foreach(var r in townRenderers)bounds.Encapsulate(r.bounds);var views=new JArray();Capture("overview",bounds.center+new Vector3(1,.9f,-1).normalized*(bounds.extents.magnitude/Mathf.Sin(25*Mathf.Deg2Rad)*1.2f),bounds.center,"Scoped800 town overview | WALLS ONLY collision",views);
   var door=world.GetComponentsInChildren<BuildingFacadeChunk>(true).SelectMany(c=>c.Features).First(f=>f.IsDoor);var forward=door.Rotation*Vector3.forward;Vector3 eye=door.Position+forward*12;Need(plan.TryGround(eye.x,eye.z,out var streetGround),"Street camera has final ground, no fallback");eye.y=(float)streetGround.Height+1.7f;Capture("street-level",eye,door.Position+Vector3.up*.8f,"Street-level | original source-derived houses | WALLS ONLY",views);
   var hill=world.BuildingSites.OrderByDescending(p=>p.Value.BoundaryMaxY-p.Value.BoundaryMinY).First();Vector3 hillEye=hill.Value.Centre+new Vector3(-16,0,-18);Need(plan.TryGround(hillEye.x,hillEye.z,out var hillGround),"Hillside camera has final ground");hillEye.y=(float)hillGround.Height+7;Capture("hillside",hillEye,new Vector3(hill.Value.Centre.x,hill.Value.LevelFloorY+2,hill.Value.Centre.z),"Hillside | level floors on final terrain | WALLS ONLY",views);File.WriteAllText(Path.Combine(output,"capture-views.json"),views.ToString(Formatting.Indented));
   Need(Hash(packetPath)==packetBefore&&Hash(rawPath)==rawBefore&&Hash(career)==careerBefore,"Source/provider files and real career unchanged");Need(errors.Count==0,"No engine/runtime errors");report["originalSourceBuildings"]=originalCount;report["emitted"]=rows.Count(r=>(string)r["status"]=="Emitted");report["suppressed"]=rows.Count(r=>(string)r["status"]=="Suppressed");report["wallColliders"]=walls.Length;report["wallFaceRaycasts"]=wallRays;report["wallBaseSupportChecks"]=baseChecks;report["physicalTerrainSupportSamples"]=terrainRays;report["worldGeneration"]=world.SurfaceGeneration;report["windows"]=features.Count(f=>!(bool)f["door"]);report["doors"]=features.Count(f=>(bool)f["door"]);report["lifecyclePolicy"]="queries revoked before cancellation/reinit/destruction; no source or registry authority";
   var completeMeshes=world.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();Need(completeMeshes.Length>0&&world.GetComponentsInChildren<MeshCollider>().Length>0,"Published town is genuinely visible/physical before revocation");ground.Invalidate(false);PollActualWorldForDiagnostic(world);Need(world.Region==null&&!world.HasSurface(Vector3.zero)&&completeMeshes.All(m=>!m)&&!world.GetComponentsInChildren<MeshCollider>().Any()&&!world.GetComponentsInChildren<MeshRenderer>().Any(),"Fallback MAIN UPDATE revokes published town after actual physical-owner generation flip");report["postPublicationPollRevocationPass"]=true;Release(world.gameObject);world=null;
   ground.RearmForProof();groundGeneration=ground.Generation;world=NewWorld("Published town explicit-owner revoke");var republish=world.BuildScopedTown800(FreshSource(),plan,default,Current);try{while(republish.MoveNext())yield return null;}finally{(republish as IDisposable)?.Dispose();}var hookMeshes=world.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();Need(hookMeshes.Length>0&&world.HasSurface(Vector3.zero),"Second real published town generation before destroy hook");ground.AttachedTown=world;ground.Dispose();Need(world.Region==null&&!world.HasSurface(Vector3.zero)&&hookMeshes.All(m=>!m)&&!world.GetComponentsInChildren<MeshCollider>().Any()&&!world.GetComponentsInChildren<MeshRenderer>().Any(),"Actual owner BEFORE-DESTROY hook revokes published town and all colliders");report["postPublicationOwnerDestroyHookPass"]=true;Release(world.gameObject);world=null;Need(ground.Meshes.All(m=>!m)&&sharedPrimitive&&road&&terrain,"Full physical owner cleanup");report["status"]="PASS_SCOPED_TOWN_EDITOR_NATIVE_ONLY";report["normalPlayAdmission"]=false;report["visualReviewPending"]=true;
  }
  static void VerifyExportPhysicalPackets(GameObject physical,PreparedWorldMeshPlan800 exactPlan)
  {
   var filters=physical.GetComponentsInChildren<MeshFilter>(true).OrderBy(f=>f.name,StringComparer.Ordinal).ToArray();Need(filters.Length==1063&&filters.Length==exactPlan.PacketCount,"Physical subtree exact1063packet count, no overlay");long vertexCount=0,triangleCount=0;int matrices=0;
   using(var w=new JsonTextWriter(new StreamWriter(Path.Combine(output,"actual-ground-packets.json"),false,new UTF8Encoding(false))))
   {
    w.WriteStartObject();W(w,"format","Scoped800ActualPhysicalPacketReadback1");W(w,"rawDocumentDigest",exactPlan.RawDocumentDigest);W(w,"sourceDigest",exactPlan.SourceDigest);W(w,"finalBufferDigest",exactPlan.FinalBufferDigest);W(w,"geometryDigest",exactPlan.GeometryDigest);W(w,"physicalRootEntityId",physical.GetEntityId().ToString());w.WritePropertyName("packets");w.WriteStartArray();
    foreach(var f in filters)
    {
     var data=exactPlan.CopyForUpload(f.name);var mesh=f.sharedMesh;var mc=f.GetComponent<MeshCollider>();Need(mesh&&mc&&mc.sharedMesh==mesh&&f.GetComponent<GeneratedMeshOwner>()?.Mesh==mesh,"Actual physical mesh binding before parity readiness");var vertices=mesh.vertices;var indices=mesh.triangles;Need(vertices.Length*3==data.XYZ.Length&&indices.SequenceEqual(data.Indices),"All actual packet index arrays equal final plan");for(int i=0;i<vertices.Length;i++)Need(BitConverter.SingleToInt32Bits(vertices[i].x)==BitConverter.SingleToInt32Bits(data.XYZ[i*3])&&BitConverter.SingleToInt32Bits(vertices[i].y)==BitConverter.SingleToInt32Bits(data.XYZ[i*3+1])&&BitConverter.SingleToInt32Bits(vertices[i].z)==BitConverter.SingleToInt32Bits(data.XYZ[i*3+2]),"All actual packet local vertexfloatbits equal final plan");
     var origin=f.transform.localPosition;Need((double)(float)data.OriginX==data.OriginX&&(double)(float)data.OriginY==data.OriginY&&(double)(float)data.OriginZ==data.OriginZ&&BitConverter.SingleToInt32Bits(origin.x)==BitConverter.SingleToInt32Bits((float)data.OriginX)&&BitConverter.SingleToInt32Bits(origin.y)==BitConverter.SingleToInt32Bits((float)data.OriginY)&&BitConverter.SingleToInt32Bits(origin.z)==BitConverter.SingleToInt32Bits((float)data.OriginZ),"Actual exact original packet origin");var matrix=f.transform.localToWorldMatrix;var expected=Matrix4x4.Translate(new Vector3((float)data.OriginX,(float)data.OriginY,(float)data.OriginZ));for(int i=0;i<16;i++)Need(matrix[i]==expected[i],"Actual packet matrix is exact source translation (numeric signedzero-equivalent basis)");matrices++;
     w.WriteStartObject();W(w,"id",data.Id);W(w,"role",exactPlan.PacketRole(data.Id).ToString());W(w,"colliderEntityId",mc.GetEntityId().ToString());w.WritePropertyName("doubleOrigin");w.WriteStartArray();w.WriteValue(data.OriginX);w.WriteValue(data.OriginY);w.WriteValue(data.OriginZ);w.WriteEndArray();w.WritePropertyName("actualLocalFloatXYZ");w.WriteStartArray();foreach(var v in vertices){w.WriteValue(v.x);w.WriteValue(v.y);w.WriteValue(v.z);}w.WriteEndArray();w.WritePropertyName("actualVertexFloatBits");w.WriteStartArray();foreach(var v in vertices){w.WriteValue(BitConverter.SingleToInt32Bits(v.x));w.WriteValue(BitConverter.SingleToInt32Bits(v.y));w.WriteValue(BitConverter.SingleToInt32Bits(v.z));}w.WriteEndArray();w.WritePropertyName("actualIndices");w.WriteStartArray();foreach(int i in indices)w.WriteValue(i);w.WriteEndArray();w.WritePropertyName("actualLocalToWorldFloatBits");w.WriteStartArray();for(int i=0;i<16;i++)w.WriteValue(BitConverter.SingleToInt32Bits(matrix[i]));w.WriteEndArray();w.WriteEndObject();vertexCount+=vertices.Length;triangleCount+=indices.Length/3;
    }
    w.WriteEndArray();w.WriteEndObject();
   }
   Need(vertexCount==317003&&triangleCount==533219&&matrices==1063,"Actual complete physical packet readback, no frozen substitute");report["allActualPhysicalBufferParityPass"]=true;report["actualPhysicalVerticesChecked"]=vertexCount;report["actualPhysicalTrianglesChecked"]=triangleCount;report["actualPhysicalMatricesChecked"]=matrices;report["actualGroundPacketReadbackSHA256"]=Hash(Path.Combine(output,"actual-ground-packets.json"));
  }
  static GeoWorld NewWorld(string name){var g=new GameObject(name);objects.Add(g);return g.AddComponent<GeoWorld>();}
  static GeoRegion AuthoredNormalFixture(){var r=new GeoRegion{id="town-reinit-authored",name="Explicit isolated normal lifecycle fixture",minX=-500,minZ=-500,width=1000,depth=1000,nx=2,nz=2,heights=new float[4],landNx=2,landNz=2,landCover=new int[4],roads=new[]{new GeoRoad{id="normal",start="a",end="b",kind="residential",width=4,points=new[]{new Vector3(-20,0,0),new Vector3(20,0,0)}}},waters=Array.Empty<GeoWater>(),buildings=new[]{new GeoBuilding{id="normal-building",kind="building",roof="flat",height=6,points=new[]{new Vector3(2,0,40),new Vector3(6,0,40),new Vector3(6,0,44),new Vector3(2,0,44),new Vector3(2,0,40)}}}};r.Prepare();return r;}
  static void VerifyNormalFixture(GeoWorld normalWorld,GeoRegion fixture)
  {
   string before=WorldFieldJson.Write(fixture);typeof(GeoWorld).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(normalWorld,new object[]{Vector2Int.zero,"TownNormalRegressionOneChunk"});
   Need(normalWorld.BuildingSites.TryGetValue("normal-building",out var site)&&Math.Abs(site.LevelFloorY-.12f)<1e-6&&site.Samples.All(p=>p.y==0),"Default normal flat-source site remains0.12m, no scoped800 height leakage");var collider=normalWorld.GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name.StartsWith("Building normal-building",StringComparison.Ordinal));var vertices=collider.sharedMesh.vertices;Need(vertices.Length==16&&collider.sharedMesh.triangles.Length==24,"Default unsectioned four-wall geometry remains16V/8triangles");for(int i=0;i<vertices.Length;i+=4)Need(Math.Abs(vertices[i].y+.88f)<1e-6&&Math.Abs(vertices[i+1].y+.88f)<1e-6&&Math.Abs(vertices[i+2].y-6.12f)<1e-6&&Math.Abs(vertices[i+3].y-6.12f)<1e-6,"Hand-computed normal flat-wall base/top geometry unchanged");Need(WorldFieldJson.Write(fixture)==before,"Normal/default rendering does not alter serialized source");report["targetedDefaultGeometryRegressionPass"]=true;
  }
  static void W(JsonWriter w,string name,object value){w.WritePropertyName(name);w.WriteValue(value);}
  /// <summary>
  /// Runs the real production poll from an Editor-only diagnostic. Unity's
  /// SendMessage lifecycle dispatcher asserts for non-running behaviours in Edit
  /// mode, so reflection invokes the same method without pretending Play mode ran.
  /// </summary>
  static void PollActualWorldForDiagnostic(GeoWorld target)
  {
   var update = typeof(GeoWorld).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
   Need(update != null, "Actual GeoWorld.Update poll exists");
   update.Invoke(target, null);
  }

  /// <summary>
  /// Saves the actual native scene render, without diagnostic overlays. Camera,
  /// source and geometry identities are recorded separately in capture-views.json.
  /// Product sign/UI readability is a separate acceptance requirement; a caption
  /// must not hide scene geometry or block the physical lifetime checks.
  /// </summary>
  static void Capture(string name, Vector3 position, Vector3 target, string title, JArray views)
  {
   const int CaptureWidthPixels = 1920;
   const int CaptureHeightPixels = 1200;
   const int DepthBufferBits = 24;

   camera.transform.position = position;
   camera.transform.LookAt(target);
   camera.aspect = (float)CaptureWidthPixels / CaptureHeightPixels;

   var renderTarget = new RenderTexture(CaptureWidthPixels, CaptureHeightPixels, DepthBufferBits);
   var previousActiveTarget = RenderTexture.active;
   var image = new Texture2D(CaptureWidthPixels, CaptureHeightPixels, TextureFormat.RGB24, false);
   try
   {
    camera.targetTexture = renderTarget;

    // These production facade components normally submit their instance batches
    // in LateUpdate. Editor diagnostics do not receive that player-loop callback.
    var initializeFacades = typeof(BuildingFacadeChunk).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
    var renderFacades = typeof(BuildingFacadeChunk).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
    Need(initializeFacades != null && renderFacades != null, "Actual production facade setup/render methods exist");
    var before = BuildingFacadeChunk.Statistics;
    foreach (var chunk in world.GetComponentsInChildren<BuildingFacadeChunk>(true))
    {
     // Non-ExecuteAlways behaviours need explicit production batch setup in this
     // Editor diagnostic. Setup reuses existing shared meshes and batch storage.
     initializeFacades.Invoke(chunk, null);
     renderFacades.Invoke(chunk, null);
    }
    var after = BuildingFacadeChunk.Statistics;
    int submittedDraws = after.drawSubmissions - before.drawSubmissions;
    int submittedWindows = after.submittedWindows - before.submittedWindows;

    // The distant overview deliberately retains the production 900-metre cull.
    Need(name == "overview" || (submittedWindows > 0 && submittedDraws > 0),
     "Street/hillside capture includes fresh actual production facade submissions");

    if (ScopedTown800ReadOnlyCensusRunner.IsArmed)
     ScopedTown800ReadOnlyCensusRunner.RecordBeforeCapture(name, ground.Root, world, camera, plan,
      submittedDraws, submittedWindows, before, after);

    camera.Render();
    RenderTexture.active = renderTarget;
    image.ReadPixels(new Rect(0, 0, CaptureWidthPixels, CaptureHeightPixels), 0, 0);
    image.Apply();
    string imagePath = Path.Combine(output, name + ".png");
    File.WriteAllBytes(imagePath, image.EncodeToPNG());

    views.Add(new JObject
    {
     { "name", name }, { "description", title },
     { "position", new JArray(position.x, position.y, position.z) },
     { "target", new JArray(target.x, target.y, target.z) },
     { "fovDegrees", camera.fieldOfView },
     { "widthPixels", CaptureWidthPixels }, { "heightPixels", CaptureHeightPixels },
     { "facadeDrawsThisCapture", submittedDraws },
     { "facadeWindowsThisCapture", submittedWindows },
     { "sourceDigest", plan.SourceDigest }, { "finalBufferDigest", plan.FinalBufferDigest },
     { "geometryDigest", plan.GeometryDigest }, { "pngSha256", Hash(imagePath) },
     { "diagnosticOverlay", false },
     { "scope", "Editor native scene render; human visual review required; no gameplay/performance acceptance" }
    });
   }
   finally
   {
    camera.targetTexture = null;
    RenderTexture.active = previousActiveTarget;
    renderTarget.Release();
    UnityEngine.Object.DestroyImmediate(renderTarget);
    UnityEngine.Object.DestroyImmediate(image);
   }
  }
  static void Finish(Exception error)
  {
   if(finished)return;finished=true;EditorApplication.update-=Tick;try{proofCancellation.Cancel();(work as IDisposable)?.Dispose();}catch(Exception e){error=error??e;}try{if(world)Release(world.gameObject);ground?.Dispose();foreach(var g in objects)Release(g);foreach(var m in materials)if(m)UnityEngine.Object.DestroyImmediate(m);}catch(Exception e){error=error??e;}Application.logMessageReceived-=Log;if(Enabled){PlayerProfile.StorageOverride=OldProfile;RegionCache.OverrideFolder=OldRegions;}report=report??new JObject();if(error!=null){report["status"]="FAIL";report["error"]=error.ToString();}report["checks"]=checks;report["runtimeErrors"]=new JArray(errors);report["realCareerUnchanged"]=career!=null&&Hash(career)==careerBefore;report["sourceFilesUnchanged"]=packetPath!=null&&Hash(packetPath)==packetBefore&&Hash(rawPath)==rawBefore;if(output!=null&&Directory.Exists(output))File.WriteAllText(Path.Combine(output,"receipt.json"),report.ToString(Formatting.Indented));int exit=error==null&&errors.Count==0&&report.Value<bool>("realCareerUnchanged")&&report.Value<bool>("sourceFilesUnchanged")?0:1;Debug.Log("SCOPED800 TOWN "+(exit==0?"PASS":"FAIL")+" "+output);EditorApplication.Exit(exit);
  }
 }
}
