// Opt-in editor-only verification. No game boot, profile load, music, controller
// or networking entry point is called. Use only in a guarded private project.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted.Editor
{
    public static class BuilderSurfaceNativeVerification
    {
        const int FixtureLayer=30;
        static string Arg(string name)
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);
            if(index<0||index+1>=args.Length)throw new Exception("Missing argument "+name);
            return args[index+1];
        }
        static Vector3 Point(JToken token)=>new Vector3((float)token[0],(float)token[1],(float)token[2]);
        static void AddSurface(string name,JToken source,Color colour)
        {
            var vertices=source["vertices"].Select(Point).ToArray();
            if(vertices.Length==0)return;
            var mesh=new Mesh{name="Builder fixture "+name,indexFormat=IndexFormat.UInt32};
            mesh.vertices=vertices;mesh.triangles=source["triangles"].SelectMany(f=>f.Select(i=>(int)i)).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject("Builder fixture "+name);go.layer=FixtureLayer;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var shader=Shader.Find("Universal Render Pipeline/Lit");if(!shader)throw new Exception("Missing existing URP shader");
            var material=new Material(shader);material.SetColor("_BaseColor",colour);material.SetFloat("_Smoothness",.15f);
            go.AddComponent<MeshRenderer>().sharedMaterial=material;
            go.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        static void Capture(Camera camera,Vector3 position,Vector3 target,string path)
        {
            camera.transform.position=position;camera.transform.LookAt(target);
            var rt=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;
            var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            // Warm the first off-screen SRP material pass before recording it.
            // Native1's first view had transient uniform colour; later views
            // were correct. This does not alter scene geometry or collision.
            try {camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally {camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);}
        }
        static JArray VehicleCases(Camera camera,string output)
        {
            // Existing controller forces and actual PhysX are stepped manually
            // in an empty editor scene, following the established parking QA
            // pattern. This is not the normal Player fixed-update lifecycle.
            const float dt=.01f;
            var mode=Physics.simulationMode;float oldStep=Time.fixedDeltaTime,oldScale=Time.timeScale;
            var awake=typeof(VehicleController).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance);
            var tick=typeof(VehicleController).GetMethod("FixedUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
            var result=new JArray();
            try
            {
                if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new Exception("Vehicle fixture requires empty non-playing editor scene");
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=dt;Time.timeScale=1;
                var cases=new[]{
                    ("main-uphill",new Vector3(0,0,3),Vector3.forward,14f),
                    ("main-downhill",new Vector3(0,0,17),Vector3.back,14f),
                    ("branch-to-main",new Vector3(26,0,10),Vector3.left,26f),
                    ("main-to-branch",new Vector3(0,0,10),Vector3.right,26f)
                };
                foreach(var item in cases)
                {
                    GameObject root=null;
                    try
                    {
                        var start=item.Item2;var heading=Quaternion.LookRotation(item.Item3,Vector3.up);
                        if(!Physics.Raycast(start+Vector3.up*25,Vector3.down,out var support,50,1<<FixtureLayer))throw new Exception("Missing initial fixture support");
                        root=new GameObject("Builder owned vehicle "+item.Item1);root.layer=8;root.transform.SetPositionAndRotation(support.point+Vector3.up*.7f,heading);
                        // Same vertical player collider role as GameSession's
                        // factory; VehicleGeometry then fits model-specific XZ.
                        var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);
                        var visual=root.AddComponent<CarVisual>();visual.Build(Color.white,false);
                        var car=root.AddComponent<VehicleController>();awake.Invoke(car,null);car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=true;
                        car.ApplyTuning(new Core.OwnedCar());car.Body.interpolation=RigidbodyInterpolation.None;
                        if(!car.TryInitializeSupportedRest(support.point,heading,out var reason,out _))throw new Exception("Vehicle supported rest refused: "+reason);
                        var initial=car.Body.position;float progress=0,air=0,maxAir=0,maxDamage=0,maxImpact=0,maxVertical=0,maxRoll=0;
                        int steps=0,minContacts=4;var labels=new HashSet<string>();
                        using(var observations=new StreamWriter(Path.Combine(output,item.Item1+"-vehicle.csv")))
                        {
                            observations.WriteLine("step,simulation_seconds,x,y,z,speed,vertical_velocity,contacts,damage,suspension_impact");
                            while(progress<item.Item4&&steps<2000)
                            {
                                float speed=car.Speed;
                                car.NetworkThrottle=Mathf.Clamp01((3-speed)*.5f+.12f);
                                car.NetworkBrake=speed>3.3f?Mathf.Clamp01((speed-3.3f)*.5f):0;
                                car.NetworkSteer=0;car.NetworkHandbrake=0;
                                tick.Invoke(car,null);Physics.Simulate(dt);Physics.SyncTransforms();steps++;
                                progress=Vector3.Dot(car.Body.position-initial,item.Item3);
                                int contacts=car.WheelGrounded.Count(g=>g);minContacts=Math.Min(minContacts,contacts);
                                air=contacts==0?air+dt:0;maxAir=Mathf.Max(maxAir,air);maxDamage=Mathf.Max(maxDamage,car.Damage);maxImpact=Mathf.Max(maxImpact,car.SuspensionImpact);maxVertical=Mathf.Max(maxVertical,Mathf.Abs(car.Body.linearVelocity.y));maxRoll=Mathf.Max(maxRoll,Mathf.Abs(Vector3.SignedAngle(Vector3.up,car.transform.up,car.transform.forward)));
                                foreach(var point in car.ContactPoints)if(Physics.Raycast(point+Vector3.up*.1f,Vector3.down,out var wheelHit,.3f,1<<FixtureLayer))labels.Add(wheelHit.collider.name);
                                var p=car.Body.position;
                                observations.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7},{8:R},{9:R}",steps,steps*dt,p.x,p.y,p.z,car.AbsoluteSpeed,car.Body.linearVelocity.y,contacts,car.Damage,car.SuspensionImpact));
                            }
                        }
                        bool crossedBoth=item.Item1.StartsWith("main-")&&item.Item1!="main-to-branch" || labels.Contains("Builder fixture main")&&labels.Contains("Builder fixture branch")&&labels.Contains("Builder fixture connector");
                        bool pass=progress>=item.Item4&&maxDamage<.01f&&maxAir<.1f&&maxRoll<25&&crossedBoth;
                        var entry=new JObject{{"case",item.Item1},{"pass",pass},{"distance_m",progress},{"simulation_seconds",steps*dt},{"maximum_damage",maxDamage},{"maximum_no_contact_seconds",maxAir},{"minimum_wheel_contacts",minContacts},{"maximum_suspension_impact",maxImpact},{"maximum_vertical_speed_mps",maxVertical},{"maximum_roll_degrees",maxRoll},{"contact_surfaces",new JArray(labels.OrderBy(v=>v))},{"no_pose_resets_after_initial_rest",true}};
                        result.Add(entry);File.WriteAllText(Path.Combine(output,"vehicle-result.json"),result.ToString());
                        Capture(camera,car.Body.position+new Vector3(9,7,-10),car.Body.position+Vector3.up*.7f,Path.Combine(output,item.Item1+"-vehicle.png"));
                        if(!pass)throw new Exception("Vehicle surface case failed: "+entry.ToString());
                    }
                    finally {if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}Physics.SyncTransforms();}
                }
                return result;
            }
            finally {Physics.simulationMode=mode;Time.fixedDeltaTime=oldStep;Time.timeScale=oldScale;}
        }
        public static void Run()
        {
            // Mute before constructing any test scene/object. Do not persist it.
            AudioListener.volume=0;AudioListener.pause=true;
            string previous=PlayerProfile.StorageOverride;
            try
            {
                string output=Path.GetFullPath(Arg("-builderSurfaceOutput"));Directory.CreateDirectory(output);
                PlayerProfile.StorageOverride=Path.Combine(output,"isolated-unused-profile.json");
                var assembly=JObject.Parse(File.ReadAllText(Arg("-builderSurfaceAssembly")));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                AddSurface("main",assembly["surfaces"]["main"],new Color(.11f,.28f,.43f));
                AddSurface("branch",assembly["surfaces"]["branch"],new Color(.18f,.37f,.24f));
                AddSurface("connector",assembly["connectors"]["side-road-mouth"],new Color(.58f,.32f,.12f));
                Physics.SyncTransforms();int checks=0;double maximumMainError=0,maximumSeamSpread=0;
                void Check(bool pass,string message){if(!pass)throw new Exception("BUILDER SURFACE FAIL "+message);checks++;}
                Check(AudioListener.volume==0&&AudioListener.pause,"Muted before fixture scene");
                for(int i=1;i<40;i++)
                {
                    float z=i*.5f;bool hit=Physics.Raycast(new Vector3(0,25,z),Vector3.down,out var result,50,1<<FixtureLayer);
                    Check(hit,"Main surface collider coverage");double error=Math.Abs(result.point.y-z*.1f);maximumMainError=Math.Max(maximumMainError,error);
                    Check(error<=.005,"Main uphill gradient retained");
                }
                foreach(float x in new[]{4.01f,6f,8f,10f,11.99f})foreach(float z in new[]{8.01f,9f,10f,11f,11.99f})
                    Check(Physics.Raycast(new Vector3(x,25,z),Vector3.down,50,1<<FixtureLayer),"Connector interior collider coverage");
                foreach(float x in new[]{4f,12f})for(int i=0;i<=16;i++)
                {
                    float z=8+i*.25f;var hits=Physics.RaycastAll(new Vector3(x,25,z),Vector3.down,50,1<<FixtureLayer);
                    Check(hits.Length>0,"Shared seam collider coverage");
                    double spread=hits.Max(h=>h.point.y)-hits.Min(h=>h.point.y);maximumSeamSpread=Math.Max(maximumSeamSpread,spread);
                    Check(spread<=.005,"Shared seam collider heights within5mm");
                }
                var light=new GameObject("Builder fixture sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(45,-35,0);
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.6f);RenderSettings.fog=false;
                var camera=new GameObject("Builder fixture camera").AddComponent<Camera>();camera.backgroundColor=new Color(.08f,.12f,.16f);camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.05f;camera.farClipPlane=150;camera.fieldOfView=52;
                Capture(camera,new Vector3(36,26,-15),new Vector3(10,1,10),Path.Combine(output,"junction-overview.png"));
                Capture(camera,new Vector3(18,8,2),new Vector3(5,1,10),Path.Combine(output,"junction-seam.png"));
                Capture(camera,new Vector3(0,3.0f,1),new Vector3(0,1.7f,18),Path.Combine(output,"main-road-low-view.png"));
                var vehicles=Environment.GetCommandLineArgs().Contains("-builderSurfaceVehicle")?VehicleCases(camera,output):new JArray();
                File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"status","PASS"},{"checks",checks},{"vehicleCases",vehicles},{"maximumMainGradientHeightError_m",maximumMainError},{"maximumSeamColliderHeightSpread_m",maximumSeamSpread},{"listenerVolume",AudioListener.volume},{"listenerPaused",AudioListener.pause},{"profileFileCreated",File.Exists(PlayerProfile.StorageOverride)},{"scope","Actual Unity Editor render/MeshCollider and optional manually-stepped existing controller/PhysX fixtures; not normal Player, real circuit accuracy or Windows hardware acceptance"}}.ToString());
                Debug.Log("BUILDER SURFACE PASS "+checks+" actual Unity collider/render checks");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally {PlayerProfile.StorageOverride=previous;}
        }
    }
}
