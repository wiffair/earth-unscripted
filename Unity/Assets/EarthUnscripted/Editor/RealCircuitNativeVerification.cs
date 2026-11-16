// Private real-source pack/render/collider acceptance. No career load or audio.
// Estimated boundaries remain estimates; this verifies transport and construction.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Packs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class RealCircuitNativeVerification
    {
        static string Arg(string name){var a=Environment.GetCommandLineArgs();return a[Array.IndexOf(a,name)+1];}
        static void Capture(Camera c,Vector3 position,Vector3 target,string path)
        {
            c.transform.position=position;c.transform.LookAt(target);
            var rt=new RenderTexture(1600,1000,24);var old=RenderTexture.active;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try{c.targetTexture=rt;c.Render();c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{c.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        public static void Run()
        {
            AudioListener.volume=0;AudioListener.pause=true;
            string output=Arg("-realCircuitOutput"),old=PlayerProfile.StorageOverride;
            GameObject root=null;Camera camera=null;
            try
            {
                if(Application.isPlaying)throw new Exception("Non-playing private Editor required");
                Directory.CreateDirectory(output);PlayerProfile.StorageOverride=Path.Combine(output,"isolated-unused-profile.json");
                int checks=0;void Check(bool pass,string why){if(!pass)throw new Exception(why);checks++;}
                void Phase(string value){File.WriteAllText(Path.Combine(output,"phase.txt"),value);Debug.Log("Real circuit verification: "+value);}
                Phase("install and captured-source read");
                string library=RegionalPackActivation.LibraryRoot();var store=new WorldPackStore(library);var installed=store.Install(Arg("-realCircuitPack"));
                var support=new PackEngineSupport("0.22.0",new Dictionary<string,int>{{"circuit-catalogue",1},{"circuit-layout",1},{"circuit-scenery",1},{"circuit-road-surfaces",1},{"provenance",1}});
                var reader=WorldPackCircuitReader.OpenVerified(store,installed,support);var layout=reader.Layouts.Single();
                var reference=WorldPackCircuitReference.For(installed.Manifest,layout);reference.Store(library);
                var sourceType=typeof(GameSession).Assembly.GetType("EarthUnscripted.PackCircuitSources");var hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
                var capture=sourceType.GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{reference.Token});
                string raw=(string)capture.GetType().GetMethod("Read",hidden).Invoke(capture,new object[]{CancellationToken.None});
                var region=WorldSnapshotCodec.DecodeBootSource(raw,out _);capture.GetType().GetMethod("BindOwned",hidden).Invoke(capture,new object[]{region,CancellationToken.None});
                Check(region.CircuitRoadSurfaces!=null&&region.roads.Length==3,"Three real-source roads retain admitted pavement");
                Check(region.CircuitRoadSurfaces.TriangleCount==17460,"Exact reviewed draft triangle count");
                Phase("finalize and construct muted world");
                var prepared=WorldDataFinalizer.PrepareOwned(region,null,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                root=new GameObject("Private real circuit draft renderer");var world=root.AddComponent<WorldBuilder>();world.BuildPrepared(prepared,true);
                camera=new GameObject("Private circuit camera").AddComponent<Camera>();camera.nearClipPlane=.1f;camera.farClipPlane=2500;camera.fieldOfView=60;
                var sites=JArray.Parse(File.ReadAllText(Arg("-realCircuitSites")));var results=new JArray();float worst=0;int samples=0;
                foreach(JObject site in sites)
                {
                    string id=(string)site["id"];var p=site["point"];var centre=new Vector3((float)p[0],(float)p[1],(float)p[2]);
                    Phase("stream and sample "+id);world.GeographicWorld.Refresh(centre,true);Physics.SyncTransforms();int local=0;
                    Check(root.GetComponentsInChildren<MeshCollider>().Any(c=>c.name=="Authored circuit pavement"),"Actual authored collider installed: "+id);
                    Check(!root.GetComponentsInChildren<MeshCollider>().Any(c=>c.name.StartsWith("Mapped road")||c.name.StartsWith("Finalized mapped asphalt")),"No legacy duplicate road mesh: "+id);
                    foreach(var road in region.roads)for(int i=0;i<road.points.Length;i+=10)
                    {
                        var point=road.points[i];if(new Vector2(point.x-centre.x,point.z-centre.z).magnitude>130)continue;
                        Check(region.CircuitRoadSurfaces.TryHeight(point.x,point.z,out float expected),"Admitted physical pavement query");
                        var hits=Physics.RaycastAll(new Vector3(point.x,expected+4,point.z),Vector3.down,8);
                        var asphalt=hits.Where(h=>h.collider.name=="Authored circuit pavement").ToArray();
                        Check(asphalt.Length>0,"Actual road collider at "+point+" near "+id);
                        float error=Math.Abs(asphalt.Max(h=>h.point.y)-expected);worst=Math.Max(worst,error);
                        Check(error<=.005f,"Actual pavement height exceeds5mm: "+error);
                        Check(!hits.Any(h=>h.collider.name=="Terrain outside authored pavement"&&h.point.y>expected-.04f),"Terrain protrudes through asphalt");local++;samples++;
                    }
                    Check(local>5,"Meaningful local road coverage sampled: "+id);
                    Capture(camera,centre+new Vector3(60,45,-60),centre,Path.Combine(output,id+"-overview.png"));
                    Capture(camera,centre+new Vector3(12,4,-18),centre+new Vector3(0,1,0),Path.Combine(output,id+"-low.png"));
                    results.Add(new JObject{{"id",id},{"samples",local}});
                }
                Check(AudioListener.volume==0&&AudioListener.pause,"Listener remains muted");Check(!File.Exists(PlayerProfile.StorageOverride),"No test or real career opened");
                File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"status","PASS"},{"checks",checks},{"samples",samples},{"maximumColliderHeightError_m",worst},{"sites",results},{"scope","Actual pack install/captured boot binding/finalization, streamed terrain and pavement rendering/PhysX in private Unity Editor. Not Player driving, track accuracy or Windows hardware acceptance."}}.ToString());
                Phase("complete");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());EditorApplication.Exit(1);}
            finally{if(root)UnityEngine.Object.DestroyImmediate(root);if(camera)UnityEngine.Object.DestroyImmediate(camera.gameObject);PlayerProfile.StorageOverride=old;}
        }
    }
}
