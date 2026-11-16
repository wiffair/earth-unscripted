// Opt-in native Editor verification of the real pack reader/binding/finalizer
// and chunk renderer. Does not replace normal Player acceptance or a real-track
// evidence review. Always run in the isolated integration project with guards.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using RoadRush.Packs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class AuthoredCircuitPackVerification
    {
        static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
        static string Arg(string key)
        {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new Exception("Missing "+key);return args[i+1];}
        static PackEngineSupport Support(string version,bool authored)=>new PackEngineSupport(version,
            new Dictionary<string,int>{{"circuit-catalogue",1},{"circuit-layout",1},{"provenance",1},{"circuit-scenery",1}}
                .Concat(authored?new[]{new KeyValuePair<string,int>("circuit-road-surfaces",1)}:Array.Empty<KeyValuePair<string,int>>()).ToDictionary(p=>p.Key,p=>p.Value));
        static GeoRegion ReadOwned(WorldPackStore store,InstalledWorldPack installed,string library,out string token)
        {
            var reader=WorldPackCircuitReader.OpenVerified(store,installed,Support("0.22.0",true));
            var layout=reader.Layouts.Single();var reference=WorldPackCircuitReference.For(installed.Manifest,layout);reference.Store(library);token=reference.Token;
            // Exercise the exact normal-boot captured source and BindOwned path.
            // Reflection crosses only the existing runtime/editor assembly boundary.
            var sourceType=typeof(GameSession).Assembly.GetType("RoadRush.PackCircuitSources");
            var capture=sourceType.GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{token});
            string raw=(string)capture.GetType().GetMethod("Read",Hidden).Invoke(capture,new object[]{CancellationToken.None});
            var region=WorldSnapshotCodec.DecodeBootSource(raw,out _);
            capture.GetType().GetMethod("BindOwned",Hidden).Invoke(capture,new object[]{region,CancellationToken.None});
            return region;
        }
        static void Capture(Camera camera,Vector3 position,Vector3 target,string path)
        {
            camera.transform.position=position;camera.transform.LookAt(target);
            var rt=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        public static void Run()
        {
            AudioListener.volume=0;AudioListener.pause=true;
            string output=Arg("-authoredOutput"),old=PlayerProfile.StorageOverride;
            try
            {
                if(Application.isPlaying)throw new Exception("Non-playing private Editor required");
                Directory.CreateDirectory(output);PlayerProfile.StorageOverride=Path.Combine(output,"isolated-unused-profile.json");
                string library=RegionalPackActivation.LibraryRoot();var store=new WorldPackStore(library);
                var installed=store.Install(Arg("-authoredPack"));int checks=0;var diagnostics=new JArray();
                void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
                Check(!WorldPackCompatibility.Resolve(installed.Manifest,Support("0.21.3",false)).CanActivate,"Old engine must refuse new required geometry");
                Check(!WorldPackCompatibility.Resolve(installed.Manifest,Support("0.22.0",false)).CanActivate,"Missing authored reader cannot silently fall back");
                var gapFixture=store.Install(Arg("-gapFixturePack"));bool gapRefused=false;
                try{ReadOwned(store,gapFixture,library,out _);}
                catch(TargetInvocationException error)
                {var cause=error.GetBaseException();gapRefused=cause is InvalidDataException&&cause.Message.Contains("unsupported pavement gap");}
                Check(gapRefused,"Original sparse-sample fixture must fail continuous pavement admission");
                var region=ReadOwned(store,installed,library,out string token);
                Check(region.id==token&&region.CircuitRoadSurfaces!=null,"Normal immutable source binding");
                var prepared=WorldDataFinalizer.PrepareOwned(region,null,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                Check(region.roads.Length==2&&!region.roads.Any(r=>r.id.StartsWith("fictional-")),"No invented pit or garage roads");
                // A service marker must not invent a flat earth pad around an
                // independently graded authored pit. Toggle it on the same immutable
                // geometry and check both pavement and surrounding soil positions.
                bool hadPad=region.HasServicePad;
                foreach(var sample in new[]{new Vector2(16,50),new Vector2(20,50),new Vector2(26,50),new Vector2(26,20),new Vector2(26,80)})
                {
                    region.HasServicePad=false;float noPad=region.TerrainHeight(sample.x,sample.y);
                    region.HasServicePad=true;float withPad=region.TerrainHeight(sample.x,sample.y);
                    Check(Math.Abs(noPad-withPad)<.000001f,"Service marker must not flatten authored terrain at "+sample);
                }
                region.HasServicePad=hadPad;
                string physical=PhysicalCircuitIdentity.Compute(prepared);region.name="Cosmetic rename";
                Check(PhysicalCircuitIdentity.Compute(prepared)==physical,"Cosmetic rename does not reset physical course identity");
                // Independent topology regression: a neighbouring/halo face
                // supplies an intermediate point on a nonlinear-height edge.
                var topologyProof=new AuthoredTerrainTopology();
                topologyProof.AddMesh(new[]{new Vector3(0,0,0),new Vector3(0,0,2),new Vector3(2,0,0)},new[]{0,1,2});
                topologyProof.AddMesh(new[]{new Vector3(1,0,1)},Array.Empty<int>(),false);
                int splitFaces=0;bool retainedCut=false,longEdge=false;double areaSum=0;
                foreach(var tick in topologyProof.ConformSteps((a,b,c)=>
                {
                    splitFaces++;
                    var ring=new[]{a,b,c};
                    foreach(var v in ring)if(Vector3.Distance(v,new Vector3(1,0,1))<.00001f)retainedCut=true;
                    for(int j=0;j<3;j++)if(Vector3.Distance(ring[j],new Vector3(0,0,2))<.00001f&&Vector3.Distance(ring[(j+1)%3],new Vector3(2,0,0))<.00001f)longEdge=true;
                    areaSum+=Math.Abs(((double)b.x-a.x)*((double)c.z-a.z)-((double)b.z-a.z)*((double)c.x-a.x))*.5;
                })){}
                Check(splitFaces==4&&retainedCut&&!longEdge,"Halo cut must subdivide the whole shared edge");
                Check(Math.Abs(areaSum-2)<.000001,"Conformity must preserve footprint area");
                // Existing r3 is still read by its unchanged representation.
                var legacy=store.Install(Arg("-legacyPack"));var legacyRegion=ReadOwned(store,legacy,library,out _);
                Check(legacyRegion.CircuitRoadSurfaces==null&&legacyRegion.CircuitScenery!=null,"Legacy r3 remains on existing path");
                var legacyChecks=new JArray();
                foreach(string flag in new[]{"-legacyR1Pack","-legacyR2Pack"})
                {
                    var arguments=Environment.GetCommandLineArgs();int index=Array.IndexOf(arguments,flag);
                    if(index<0)continue;
                    var revision=store.Install(arguments[index+1]);var older=ReadOwned(store,revision,library,out string olderToken);
                    Check(older.id==olderToken&&older.CircuitRoadSurfaces==null&&older.CircuitScenery!=null,"Legacy owned source remains on original representation: "+flag);
                    var olderPrepared=WorldDataFinalizer.PrepareOwned(older,null,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                    Check(olderPrepared!=null&&older.roads.Length>0&&older.HasServicePad,"Legacy finalization retains declared pit service: "+flag);
                    legacyChecks.Add(new JObject{{"argument",flag},{"manifestSha256",revision.Manifest.ManifestSha256},{"roads",older.roads.Length},{"physicalIdentity",PhysicalCircuitIdentity.Compute(olderPrepared)}});
                }
                File.WriteAllText(Path.Combine(output,"legacy-revisions.json"),legacyChecks.ToString());
                using(var cancelled=new CancellationTokenSource())
                {cancelled.Cancel();bool refused=false;try{store.Install(Arg("-authoredPack"),cancelled.Token);}catch(OperationCanceledException){refused=true;}Check(refused,"Cancelled import cannot publish another revision");}
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var root=new GameObject("Native authored pack renderer");var world=root.AddComponent<WorldBuilder>();world.BuildPrepared(prepared);
                Physics.SyncTransforms();
                Check(root.GetComponentsInChildren<MeshCollider>().Any(c=>c.name=="Authored circuit pavement"),"Actual chunk-owned authored collider");
                Check(!root.GetComponentsInChildren<MeshCollider>().Any(c=>c.name.StartsWith("Mapped road")||c.name.StartsWith("Finalized mapped asphalt")),"No second generated driving surface");
                var terrainDump=new JArray();
                foreach(var collider in root.GetComponentsInChildren<MeshCollider>().Where(c=>c.name=="Terrain outside authored pavement"))
                {
                    var mesh=collider.sharedMesh;
                    terrainDump.Add(new JObject{{"vertices",new JArray(mesh.vertices.Select(v=>{var p=collider.transform.TransformPoint(v);return new JArray(p.x,p.y,p.z);} ))},{"triangles",new JArray(mesh.triangles)}});
                }
                File.WriteAllText(Path.Combine(output,"terrain-mesh.json"),terrainDump.ToString());
                float maxError=0;int samples=0;
                foreach(var road in region.roads)
                for(int i=1;i<road.points.Length;i++)
                {
                    float length=Vector3.Distance(road.points[i-1],road.points[i]);int steps=Math.Max(1,(int)Math.Ceiling(length/1.25));
                    for(int j=0;j<=steps;j++)
                    {
                        var point=Vector3.Lerp(road.points[i-1],road.points[i],(float)j/steps);
                        Check(region.CircuitRoadSurfaces.TryHeight(point.x,point.z,out float expected),"Navigation stays on admitted pavement");
                        var hits=Physics.RaycastAll(new Vector3(point.x,expected+4,point.z),Vector3.down,8);
                        var actual=hits.Where(h=>h.collider.name=="Authored circuit pavement").ToArray();
                        Check(actual.Length>0,"Authored collider coverage at "+point);
                        float error=Math.Abs(actual.Max(h=>h.point.y)-expected);maxError=Math.Max(maxError,error);Check(error<=.005f,"Collider differs from reviewed surface");
                        Check(!hits.Any(h=>h.collider.name=="Terrain outside authored pavement"&&h.point.y>expected-.04f),"Terrain protrudes into pavement");samples++;
                    }
                }
                float maximumBoundaryGap=0;
                foreach(var point in new[]{new Vector3(5.002f,0,40),new Vector3(12.998f,0,50),new Vector3(19.002f,0,50)})
                {
                    float edgeX=point.x<10?5:point.x<16?13:19;
                    Check(region.CircuitRoadSurfaces.TryHeight(edgeX,point.z,out float roadHeight),"Known fixture edge");
                    var soil=Physics.RaycastAll(new Vector3(point.x,roadHeight+4,point.z),Vector3.down,8)
                        .Where(h=>h.collider.name=="Terrain outside authored pavement").ToArray();
                    Check(soil.Length>0,"Soil meets road edge");
                    float gap=roadHeight-soil.Max(h=>h.point.y);maximumBoundaryGap=Math.Max(maximumBoundaryGap,Math.Abs(gap));
                    Check(gap>=-.005f&&gap<=.005f,"Boundary road/soil contact exceeds5mm: "+gap);
                }
                var camera=new GameObject("Authored fixture camera").AddComponent<Camera>();camera.nearClipPlane=.1f;camera.farClipPlane=1500;camera.fieldOfView=60;
                Capture(camera,new Vector3(-35,105,-35),new Vector3(25,0,50),Path.Combine(output,"loop-overview.png"));
                Capture(camera,new Vector3(29,8,-2),new Vector3(9,.5f,20),Path.Combine(output,"pit-entry.png"));
                Capture(camera,new Vector3(28,5,64),new Vector3(7,1.8f,89),Path.Combine(output,"pit-exit.png"));
                File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"status","PASS"},{"checks",checks},{"navigationSamples",samples},{"maximumColliderHeightError_m",maxError},{"maximumBoundaryContactGap_m",maximumBoundaryGap},{"listenerVolume",AudioListener.volume},{"listenerPaused",AudioListener.pause},{"profileFileCreated",File.Exists(PlayerProfile.StorageOverride)},{"scope","Actual existing pack install/captured boot binding/finalization and chunk render/collider in private Unity Editor; not normal Player lifecycle, full vehicle driving, real-circuit accuracy or Windows hardware"}}.ToString());
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera.gameObject);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=old;}
        }
    }
}
