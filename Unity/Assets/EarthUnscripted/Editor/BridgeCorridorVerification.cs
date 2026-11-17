using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using EarthUnscripted.Core;
using EarthUnscripted.NewbridgePrototype;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class BridgeCorridorVerification
    {
        const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static readonly Type World=typeof(NewbridgeWorld),Batch=typeof(NewbridgeWorld).GetNestedType("Batch",BindingFlags.NonPublic);
        static object Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Hidden|BindingFlags.Public).Invoke(target,args);
        static object NewBatch(string name)=>Activator.CreateInstance(Batch,new object[]{name,null,true});
        static void Check(bool yes,string message){if(!yes)throw new InvalidDataException(message);}
        public static void BatchRun(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            var owned=new List<GameObject>();var meshes=new List<Mesh>();var oldMode=Physics.simulationMode;float oldScale=Time.timeScale,oldFixed=Time.fixedDeltaTime;
            var report=new JObject();string output=Path.GetFullPath("Builds/Verification/Beta10BridgeCorridor");Directory.CreateDirectory(output);
            try
            {
                Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;Time.fixedDeltaTime=.01f;
                var source=JsonUtility.FromJson<NewbridgeWorld.Map>(File.ReadAllText("Assets/EarthUnscripted/Editor/Fixtures/KessockBridgeProfilesFixture.json"));
                var road=source.features.Single(f=>f.id=="33911529");var path=source.features.Single(f=>f.id=="38129651");
                var worldRoot=new GameObject("Exact Kessock bridge mesh regression");owned.Add(worldRoot);var world=worldRoot.AddComponent<NewbridgeWorld>();
                World.GetField("map",Hidden).SetValue(world,source);World.GetField("workingMap",Hidden).SetValue(world,source);Invoke(world,"IndexMap",source);
                Check(ReferenceEquals(Invoke(world,"SharedBridgeRoad",path),road),"Actual parallel bridge path shares the adjacent road deck");
                var elevated=new NewbridgeWorld.Feature{id="independent-high-path",sourceId="independent-high-path",category="path",bridge=true,layer=path.layer,level=path.level,width=path.width,points=path.points.Select(p=>p+Vector3.up*5).ToArray()};
                Check(Invoke(world,"SharedBridgeRoad",elevated)==null,"Independent higher parallel footbridge keeps its own level");
                var centre=road.points[road.points.Length/2];var across=Vector3.Cross(Vector3.up,(road.points[road.points.Length/2+1]-centre).normalized);
                var crossing=new NewbridgeWorld.Feature{id="crossing-footbridge",sourceId="crossing-footbridge",category="path",bridge=true,layer=path.layer,level=path.level,width=2,points=new[]{centre-across*30,centre,centre+across*30}};
                Check(Invoke(world,"SharedBridgeRoad",crossing)==null,"Independent crossing footbridge is not flattened onto the road");
                var route=new RoutePath(new GeoRegion{circuit=true,route=road.points});
                var original=new GameObject("Legacy raw-profile structure baseline");original.transform.position=Vector3.up*1000;owned.Add(original);
                var corrected=new GameObject("Corrected actual production meshes");corrected.transform.SetParent(worldRoot.transform,false);
                Emit(world,source,original.transform,true);Emit(world,source,corrected.transform,false);Physics.SyncTransforms();
                var carRoot=new GameObject("Actual stock coupe bridge drive");owned.Add(carRoot);carRoot.layer=8;carRoot.AddComponent<Rigidbody>();var chassis=carRoot.AddComponent<BoxCollider>();chassis.center=new Vector3(0,VehicleMetric.ColliderCentreY,0);chassis.size=new Vector3(1.95f,VehicleMetric.ColliderHeight,4.15f);
                var visual=carRoot.AddComponent<CarVisual>();visual.Build(Color.blue,true,0);var car=carRoot.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",Hidden).Invoke(car,null);car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=false;car.ApplyTuning(new OwnedCar{model=0});car.Body.interpolation=RigidbodyInterpolation.None;car.Body.constraints=RigidbodyConstraints.None;
                float lane=-road.width*.24f;int legacyObstructions=0,newObstructions=0;
                var baseline=original.GetComponentsInChildren<MeshCollider>();var fixedColliders=corrected.GetComponentsInChildren<MeshCollider>();
                for(float at=740;at<=925;at+=1)
                {
                    var position=route.At(at,lane)+Vector3.up*car.Metrics.RestRootHeight(car.Body.mass);var rotation=Quaternion.LookRotation(route.Forward(at),Vector3.up);
                    foreach(var c in baseline)if(Physics.ComputePenetration(chassis,position+Vector3.up*1000,rotation,c,c.transform.position,c.transform.rotation,out _,out float depth)&&depth>.005f)legacyObstructions++;
                    foreach(var c in fixedColliders)if(Physics.ComputePenetration(chassis,position,rotation,c,c.transform.position,c.transform.rotation,out _,out float depth)&&depth>.005f)newObstructions++;
                }
                report["legacyChassisObstructionSamples"]=legacyObstructions;report["correctedChassisObstructionSamples"]=newObstructions;
                Check(legacyObstructions>0,"Exact source fixture must reproduce physical structure overlap before the fix");Check(newObstructions==0,"No corrected deck, pavement or barrier may occupy the driving chassis corridor");
                float start=740;var initial=route.At(start,lane);var heading=Quaternion.LookRotation(route.Forward(start),Vector3.up);
                Check(car.TryInitializeSupportedRest(initial,heading,out var why,out _),"Actual bridge support: "+why);
                car.Body.linearVelocity=route.PlanarForward(start)*22.352f;
                float progress=start,maxYawError=0;int airborne=0;
                var step=typeof(VehicleController).GetMethod("FixedUpdate",Hidden);
                for(int tick=0;tick<1000&&progress<925;tick++)
                {
                    progress=BetaSmokeVerification.ClosestNear(route,car.Body.position,progress,25);
                    var aim=route.At(progress+24,lane);var local=car.transform.InverseTransformPoint(aim);
                    float angle=Mathf.Atan2(2*car.Metrics.Wheelbase*local.x,Mathf.Max(1,local.x*local.x+local.z*local.z))*Mathf.Rad2Deg;
                    car.NetworkSteer=Mathf.Clamp(angle/Mathf.Lerp(30,10,Mathf.Clamp01(car.AbsoluteSpeed/48)),-1,1);
                    car.NetworkThrottle=car.AbsoluteSpeed<22.352f?.4f:0;car.NetworkBrake=car.AbsoluteSpeed>22.9f?.2f:0;
                    step.Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();maxYawError=Mathf.Max(maxYawError,Vector3.Angle(Vector3.ProjectOnPlane(car.transform.forward,Vector3.up),route.PlanarForward(progress)));if(!car.Grounded)airborne++;
                }
                report["physicalDistanceMetres"]=progress-start;report["maximumYawErrorDegrees"]=maxYawError;report["airborneFrames"]=airborne;report["damage"]=car.Damage;report["finalSpeedMps"]=car.AbsoluteSpeed;
                Check(progress>=925&&maxYawError<10&&airborne==0&&car.Damage<.1f,"Actual unconstrained stock car must drive through the prior barrier/deck collision at50mph");
                report["passed"]=true;Debug.Log("BRIDGE CORRIDOR PASS "+report.ToString());
            }
            catch(Exception error){report["passed"]=false;report["failure"]=error.ToString();throw;}
            finally
            {
                File.WriteAllText(Path.Combine(output,"result.json"),report.ToString());
                foreach(var root in owned)if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}
                foreach(var mesh in meshes)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);
                Physics.simulationMode=oldMode;Time.timeScale=oldScale;Time.fixedDeltaTime=oldFixed;
            }
        }
        static void Emit(NewbridgeWorld world,NewbridgeWorld.Map source,Transform parent,bool legacy)
        {
            var roads=NewBatch("Road asphalt");var verges=NewBatch("Road shoulders");var paint=NewBatch("Unused markings");var paths=NewBatch("Footbridges / raised paths");var structures=NewBatch("Bridge decks / barriers");
            foreach(var f in source.features)
            {
                float station=0;
                for(int i=1;i<f.points.Length;i++)
                {
                    var a=f.points[i-1];var b=f.points[i];float length=Vector3.ProjectOnPlane(b-a,Vector3.up).magnitude;
                    if((a.x+b.x)*.5f< -640||(a.x+b.x)*.5f> -384||(a.z+b.z)*.5f<2304||(a.z+b.z)*.5f>2496){station+=length;continue;}
                    var sideA=(Vector3)World.GetMethod("Join",Hidden).Invoke(null,new object[]{f.points,i-1});var sideB=(Vector3)World.GetMethod("Join",Hidden).Invoke(null,new object[]{f.points,i});
                    if(f.category=="road")Invoke(world,"RoadSurface",roads,verges,paint,f,a,b,sideA,sideB,station);
                    else if(!legacy){a=(Vector3)Invoke(world,"BridgeSurfacePoint",f,a);b=(Vector3)Invoke(world,"BridgeSurfacePoint",f,b);Invoke(world,"FootpathStrip",paths,f,a,b,sideA,sideB,f.width*.5f);}
                    float top=f.category=="path"?-.12f:-.025f;
                    if(legacy)Invoke(structures,"Box",a,b,sideA,sideB,f.width*.5f+.12f,-.75f,top);else Invoke(world,"BridgeDeckBox",structures,f,a,b,sideA,sideB,f.width*.5f+.12f,-.75f,top);
                    foreach(int sign in new[]{-1,1})
                    {
                        var ba=a+sideA*(f.width*.5f+.16f)*sign;var bb=b+sideB*(f.width*.5f+.16f)*sign;
                        if(!legacy){ba=(Vector3)Invoke(world,"BridgeSurfacePoint",f,ba);bb=(Vector3)Invoke(world,"BridgeSurfacePoint",f,bb);}
                        bool clear=legacy?LegacyBarrierClear(source,ba,bb,f):(bool)Invoke(world,"BridgeBarrierClear",ba,bb,f);
                        if(clear)Invoke(structures,"Box",ba,bb,sideA,sideB,.1f,.05f,.75f);
                    }
                    station+=length;
                }
            }
            foreach(var batch in new[]{roads,verges,paths,structures}){Invoke(batch,"Publish",parent,new NewbridgeWorld.ChunkTiming());Invoke(batch,"FinishCollider");}
        }
        static bool LegacyBarrierClear(NewbridgeWorld.Map source,Vector3 a,Vector3 b,NewbridgeWorld.Feature owner)
        {
            for(int i=0;i<=2;i++)
            {
                var point=Vector3.Lerp(a,b,i*.5f);
                foreach(var road in source.features)
                {
                    if(road.category!="road"||road.sourceId==owner.sourceId)continue;
                    object[] args={road,point,Vector3.zero,Vector3.zero,0f};World.GetMethod("NearestBridgePoint",Hidden).Invoke(null,args);
                    var nearest=(Vector3)args[2];float gap=(float)args[4];if(Mathf.Abs(nearest.y-point.y)<.75f&&gap<road.width*.5f+.6f)return false;
                }
            }
            return true;
        }
    }
}
