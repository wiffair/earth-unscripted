using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class CanopyRenderingVerification
    {
        const string Flag="EUCanopyRenderingVerification";static string root,canonical;static int checks,phase,retireFrame;static double deadline;static GeoWorld world;static GeoRegion region;static Mesh[] resources;static Material[] materials;static Transform observer;static object staged;static Mesh stagedOwned;
        static CanopyRenderingVerification(){if(!SessionState.GetBool(Flag,false))return;root=SessionState.GetString(Flag+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;}
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");root=Path.GetFullPath("Builds/Verification/CanopyRendering");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(new PlayerProfile{quality=0,volume=0,rumble=0,livingScenery=false}));SessionState.SetBool(Flag,true);SessionState.SetString(Flag+"Root",root);EditorApplication.EnterPlaymode();
        }
        static void Check(bool ok,string text){if(!ok)throw new Exception("CANOPY RENDER FAIL: "+text);checks++;Debug.Log("CANOPY RENDER PASS: "+text);}
        static object Invoke(string name,params object[] args)=>typeof(GeoWorld).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(world,args);
        static void ResourcesIntact(string stage)
        {
            Check(resources.All(m=>m&&AssetDatabase.Contains(m))&&materials.All(m=>m&&AssetDatabase.Contains(m)),stage+" retains every shared mesh and material asset");
            for(int v=0;v<3;v++)for(int lod=0;lod<3;lod++){int index=(v*3+lod)*2;Check(Resources.Load<Mesh>($"Vegetation/Broadleaf-{v}-{lod}-bark")==resources[index]&&Resources.Load<Mesh>($"Vegetation/Broadleaf-{v}-{lod}-leaves")==resources[index+1],stage+" preserves Resource mesh identity "+v+"/"+lod);}
            Check(Resources.FindObjectsOfTypeAll<Mesh>().Where(m=>m.name.StartsWith("Broadleaf-",StringComparison.Ordinal)).All(AssetDatabase.Contains),stage+" creates no cloned broadleaf meshes");
            Check(Resources.FindObjectsOfTypeAll<Material>().Where(m=>m.name.StartsWith("BroadleafBark",StringComparison.Ordinal)||m.name.StartsWith("BroadleafLeaves",StringComparison.Ordinal)).All(AssetDatabase.Contains),stage+" creates no per-tree material copies");
        }
        static void CheckPublished()
        {
            var expected=region.trees.Where(t=>world.HasSurface(new Vector3(t.x,0,t.z))).ToDictionary(t=>t.id);var chunks=world.GetComponentsInChildren<DetailedWoodlandChunk>(true).Where(c=>c.gameObject.activeInHierarchy).ToArray();var actual=chunks.SelectMany(c=>c.Instances).ToArray();
            Check(actual.Length==expected.Count&&actual.Select(t=>t.Id).Distinct().Count()==actual.Length&&actual.All(t=>expected.ContainsKey(t.Id)),"every canonical tree on published surfaces appears exactly once");
            bool ground=true,transforms=true,colliders=true,bounds=true,noCopies=true;
            foreach(var chunk in chunks)
            {
                typeof(DetailedWoodlandChunk).GetMethod("RebuildWorldMatrices",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chunk,null);
                var caps=chunk.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.name.StartsWith("Mapped tree trunk ",StringComparison.Ordinal)).ToArray();colliders&=caps.Length==chunk.TreeCount;
                foreach(var instance in chunk.Instances)
                {
                    var tree=expected[instance.Id];ground&=Math.Abs(instance.Position.y-world.MeshSurfaceHeight(tree.x,tree.z))<.0001f&&instance.Position.x==tree.x&&instance.Position.z==tree.z;
                    var matrix=Matrix4x4.TRS(instance.Position,Quaternion.Euler(0,tree.seed%360,0),new Vector3(tree.crownRadius,tree.height,tree.crownRadius));for(int i=0;i<16;i++)transforms&=Math.Abs(matrix[i]-instance.LocalMatrix[i])<.00001f;
                    transforms&=instance.Height==tree.height&&instance.Radius==tree.crownRadius&&instance.Variant==(tree.seed%3+3)%3;
                    var cap=caps.Single(c=>c.name=="Mapped tree trunk "+instance.Id);float radius=Mathf.Clamp(tree.crownRadius*.075f,.12f,.5f),height=Mathf.Max(radius*2,tree.height*.5f);
                    colliders&=cap.direction==1&&Math.Abs(cap.radius-radius)<.0001f&&Math.Abs(cap.height-height)<.0001f&&Vector3.Distance(cap.center,Vector3.up*height*.5f)<.0001f&&Vector3.Distance(cap.transform.localPosition,instance.Position)<.0001f;
                    noCopies&=cap.GetComponentsInChildren<MeshFilter>(true).Length==0&&cap.GetComponentsInChildren<Renderer>(true).Length==0;
                    for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)for(int y=0;y<=1;y++){var p=chunk.transform.TransformPoint(instance.Position+new Vector3(x*tree.crownRadius,y*tree.height,z*tree.crownRadius));bounds&=chunk.WorldBounds.SqrDistance(p)<.00001f;}
                }
            }
            Check(ground,"every tree base uses the actual triangulated game ground surface");Check(transforms,"canonical height radius variant and yaw bind to exact instance matrices");Check(colliders,"one physical trunk capsule per published tree has the declared ground and envelope");Check(noCopies,"mapped tree scene nodes contain physical capsules without per-tree renderers or cloned meshes");Check(bounds,"shared instance world bounds include every declared full tree envelope");Check(WorldSnapshotCodec.CanonicalJson(region)==canonical,"renderer leaves canonical host trees coverage source audits and epoch unchanged");
        }
        static void ShaderEnvelope()
        {
            bool valid=true,channels=true;long tested=0;float maximum=0;
            foreach(var mesh in resources)
            {
                bool leaf=mesh.name.EndsWith("leaves",StringComparison.Ordinal);var vertices=mesh.vertices;var colors=mesh.colors;var pivots=new List<Vector3>();mesh.GetUVs(1,pivots);channels&=colors.Length==vertices.Length&&pivots.Count==vertices.Length;
                foreach(float h in new[]{4f,12f,45f})foreach(float radius in new[]{.8f,1.6f,5f})for(int i=0;i<vertices.Length;i++)
                {
                    var p=vertices[i];if(leaf)p.y=Mathf.Clamp01(pivots[i].y+(p.y-pivots[i].y)*radius/h);
                    float bend=.028f*Mathf.Abs(colors[i].r)*p.y*p.y;float radial=Mathf.Sqrt(Mathf.Pow(Mathf.Abs(p.x)+bend,2)+Mathf.Pow(Mathf.Abs(p.z)+bend,2));maximum=Math.Max(maximum,radial);valid&=p.y>=0&&p.y<=1&&radial<=1.00001f;tested++;
                }
            }
            Check(channels,"all shared meshes supply shader vertex-colour and UV1 pivot channels");Check(valid,"shader UV1 aspect correction vertical clamp and independent maximum X/Z wind remain within declared radius and0..height at radius.8..5m/height4..45m");
            File.WriteAllText(Path.Combine(root,"shader-envelope.txt"),$"verticesAndDimensionCases={tested}\nmaxNormalizedRadiusIncludingIndependentWind={maximum:R}\nCPU evaluation of actual shader expressions and uploaded resource data; native rendering validated separately.\n");
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Canopy rendering timed out phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    var session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||!session.Updates)return;session.SetMenu(true);session.Input.enabled=false;session.Updates.Automatic=session.Updates.Enabled=false;session.World.gameObject.SetActive(false);
                    string json=File.ReadAllText("Builds/Verification/CanopyPack/peer-world.json");var source=JsonUtility.FromJson<GeoRegion>(json);region=WorldSnapshotCodec.Decode(json,WorldSnapshotCodec.NetworkId(source),WorldSnapshotCodec.Epoch(source));canonical=WorldSnapshotCodec.CanonicalJson(region);Check(region.trees.Length>0,"fixture is the actual accepted canonical Pantside peer world");
                    DetailedWoodlandChunk.EnsureAssets();var list=new List<Mesh>();for(int v=0;v<3;v++)for(int lod=0;lod<3;lod++)foreach(string part in new[]{"bark","leaves"})list.Add(Resources.Load<Mesh>($"Vegetation/Broadleaf-{v}-{lod}-{part}"));resources=list.ToArray();materials=new[]{Resources.Load<Material>("Vegetation/BroadleafBark"),Resources.Load<Material>("Vegetation/BroadleafLeaves")};Check(resources.Length==18&&resources.All(m=>m),"all18 actual shared resource meshes load");ShaderEnvelope();
                    world=new GameObject("Canopy rendering fixture").AddComponent<GeoWorld>();world.Initialize(region,Geometry.Material("Canopy verification asphalt",Color.gray));observer=new GameObject("Canopy observer").transform;var c=region.canopyCoverage[0];observer.position=new Vector3((c.minX+c.maxX)*.5f,0,(c.minZ+c.maxZ)*.5f);world.Observer=observer;world.Refresh(observer.position,true);CheckPublished();ResourcesIntact("initial force");phase=1;return;
                }
                if(phase==1)
                {
                    var stats=world.CaptureStreamingTelemetry();if(stats.pendingBuilds>0||stats.pendingRetires>0)return;CheckPublished();ResourcesIntact("outer preparation");
                    // Force terrain allocation on an unpublished outside-resident key,
                    // then change origin so normal reconciliation cancels that job.
                    var key=new Vector2Int(-4,-4);Invoke("Retire",key,"verification");staged=Invoke("StartStage",key,"prefetch");typeof(GeoWorld).GetField("stage",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(world,staged);var hidden=(Transform)staged.GetType().GetField("root").GetValue(staged);int steps=0;while(!hidden.GetComponentInChildren<MeshFilter>(true)&&steps++<10000)Invoke("AdvanceStage",staged);stagedOwned=hidden.GetComponentInChildren<MeshFilter>(true).sharedMesh;Check(!hidden.gameObject.activeInHierarchy,"partial terrain and collider remain hidden before cancellation");
                    observer.position=new Vector3(region.minX+region.width-50,0,region.minZ+region.depth-50);world.Refresh(observer.position,true);retireFrame=Time.frameCount;phase=2;return;
                }
                if(phase==2)
                {
                    if(Time.frameCount<=retireFrame)return;Check(!stagedOwned,"cancelled staged geometry releases its owned mesh");CheckPublished();ResourcesIntact("forced retirement and cancellation");
                    UnityEngine.Object.Destroy(world.gameObject);retireFrame=Time.frameCount;phase=3;return;
                }
                if(phase==3)
                {
                    if(Time.frameCount<=retireFrame)return;ResourcesIntact("complete world disposal");File.WriteAllText(Path.Combine(root,"result.txt"),$"PASS {checks} actual canopy initialization placement collider shader envelope shared-asset ownership and cleanup checks\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
