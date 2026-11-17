using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EarthUnscripted.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    // Actual unconstrained rigidbody, convex wheel colliders and suspension. No
    // rotation locks or per-tick velocity/pose resets conceal a verge roll.
    public static class VehicleVergeVerification
    {
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly MethodInfo Step=typeof(VehicleController).GetMethod("FixedUpdate",Private);
        static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
        public static void Run()
        {
            var previous=Physics.simulationMode;float oldScale=Time.timeScale,oldFixed=Time.fixedDeltaTime;
            var cases=new JArray();var crossingFailures=new List<string>();var owned=new List<GameObject>();string failure=null;
            string output=Path.GetFullPath("Builds/Verification/Beta10Verge");Directory.CreateDirectory(output);
            try
            {
                Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;Time.fixedDeltaTime=.01f;
                VehiclePackLibrary.Initialize(Path.Combine(output,"isolated-library"),Path.Combine(Application.streamingAssetsPath,"VehiclePacks"));
                var hekla=ImportedVehicleCatalogue.Find("earthunscripted.hekla-formula-offroad");Require(hekla!=null,"Included Hekla pack must load");
                foreach(float rise in new[]{.085f,.20f})foreach(float angle in new[]{15f,45f,90f})foreach(float speed in new[]{5f,13.4112f,22f})
                {
                    var ground=new GameObject("Actual grass-road verge fixture");owned.Add(ground);Require(VehicleObstacleWheels.MarkSuspensionSurface(ground,"Terrain"),"Production support layer assignment");
                    var mesh=new Mesh{vertices=new[]{new Vector3(-1000,1000,-1000),new Vector3(-1000,1000,1000),new Vector3(0,1000,1000),new Vector3(0,1000,-1000),new Vector3(0,1000+rise,-1000),new Vector3(0,1000+rise,1000),new Vector3(1000,1000+rise,1000),new Vector3(1000,1000+rise,-1000)},triangles=new[]{0,1,2,0,2,3,3,2,5,3,5,4,4,5,6,4,6,7}};
                    mesh.RecalculateNormals();ground.AddComponent<MeshCollider>().sharedMesh=mesh;
                    var root=new GameObject("Actual Hekla verge drive");owned.Add(root);root.layer=8;root.AddComponent<Rigidbody>();var chassis=root.AddComponent<BoxCollider>();chassis.center=new Vector3(0,Core.VehicleMetric.ColliderCentreY,0);chassis.size=new Vector3(1.95f,Core.VehicleMetric.ColliderHeight,4.15f);
                    var visual=root.AddComponent<CarVisual>();visual.Build(Color.red,true,hekla.Model);
                    var car=root.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",Private).Invoke(car,null);car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=false;car.ApplyTuning(new OwnedCar{model=hekla.Model,packVehicleId=hekla.Settings.Id});
                    Require(car.GetComponent<BoxCollider>().excludeLayers.value==0,"Chassis must retain terrain collision");
                    Require(car.GetComponentsInChildren<MeshCollider>().All(c=>(c.excludeLayers.value&(1<<VehicleObstacleWheels.SuspensionSupportLayer))!=0),"Obstacle tyres must delegate support surfaces to suspension");
                    var barrierCheck=new GameObject("Bridge safety barriers");Require(!VehicleObstacleWheels.MarkSuspensionSurface(barrierCheck,barrierCheck.name)&&barrierCheck.layer==0,"Barriers retain physical tyre collision");UnityEngine.Object.DestroyImmediate(barrierCheck);
                    car.Body.interpolation=RigidbodyInterpolation.None;car.Body.constraints=RigidbodyConstraints.None;
                    Physics.SyncTransforms();var heading=Quaternion.Euler(0,angle,0);
                    Require(car.TryInitializeSupportedRest(new Vector3(-4,1000,-40),heading,out var why,out _),"Fixture support: "+why);
                    for(int tick=0;tick<150;tick++){Step.Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();}
                    car.Body.linearVelocity=heading*Vector3.forward*speed;car.NetworkThrottle=.20f;
                    float maxTilt=0,maxAngular=0,minY=float.MaxValue;int unsupported=0;
                    for(int tick=0;tick<700;tick++)
                    {
                        Step.Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();
                        maxTilt=Mathf.Max(maxTilt,Vector3.Angle(car.transform.up,Vector3.up));maxAngular=Mathf.Max(maxAngular,car.Body.angularVelocity.magnitude);minY=Mathf.Min(minY,car.Body.position.y);if(!car.Grounded)unsupported++;
                    }
                    cases.Add(new JObject{["riseMetres"]=rise,["headingDegrees"]=angle,["initialMps"]=speed,["peakTiltDegrees"]=maxTilt,["peakAngularVelocity"]=maxAngular,["unsupportedFrames"]=unsupported,["finalX"]=car.Body.position.x,["minimumY"]=minY});
                    if(!(maxTilt<55&&minY>999.8f&&car.Body.position.x>1))crossingFailures.Add(cases.Last.ToString());
                    Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(ground);UnityEngine.Object.DestroyImmediate(mesh);owned.Clear();
                }
                FollowStoppedPlayer(cases,owned);
                Require(crossingFailures.Count==0,"Hekla verge crossing failures: "+string.Join(";",crossingFailures));
            }
            catch(Exception e){failure=e.ToString();Debug.LogException(e);}
            finally
            {
                foreach(var root in owned)if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}
                Physics.simulationMode=previous;Time.timeScale=oldScale;Time.fixedDeltaTime=oldFixed;
            }
            File.WriteAllText(Path.Combine(output,"result.json"),new JObject{["passed"]=failure==null,["failure"]=failure,["cases"]=cases}.ToString());Debug.Log("VERGE / FOLLOWING VERIFICATION "+output);if(failure!=null)throw new InvalidDataException(failure);
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        static void FollowStoppedPlayer(JArray cases,List<GameObject> owned)
        {
            var route=new RoutePath();float at=700;
            var playerRoot=new GameObject("Stopped player fixture");owned.Add(playerRoot);playerRoot.layer=8;playerRoot.AddComponent<Rigidbody>();var player=playerRoot.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",Private).Invoke(player,null);player.Body.position=VehicleGeometry.RoadRestPosition(route.At(at),0);player.Body.rotation=route.Rotation(at);player.Body.isKinematic=true;
            var root=new GameObject("Following traffic fixture");owned.Add(root);root.layer=8;root.AddComponent<Rigidbody>();var chassis=root.AddComponent<BoxCollider>();chassis.center=new Vector3(0,Core.VehicleMetric.ColliderCentreY,0);chassis.size=new Vector3(1.95f,Core.VehicleMetric.ColliderHeight,4.15f);var visual=root.AddComponent<CarVisual>();visual.Build(Color.blue);VehicleGeometry.ApplyCollider(chassis,0);var traffic=root.AddComponent<TrafficCar>();typeof(TrafficCar).GetMethod("Awake",Private).Invoke(traffic,null);traffic.Route=route;traffic.Player=player;traffic.Visual=visual;traffic.Distance=at-70;traffic.Speed=18;traffic.Direction=1;traffic.Lane=0;traffic.Fleet=new[]{traffic};root.GetComponent<Rigidbody>().position=VehicleGeometry.RoadRestPosition(route.At(at-70),0);root.GetComponent<Rigidbody>().rotation=route.Rotation(at-70);
            float minimum=float.MaxValue;bool coasted=false;var step=typeof(TrafficCar).GetMethod("FixedUpdate",Private);
            for(int tick=0;tick<2000;tick++){step.Invoke(traffic,null);Physics.Simulate(.01f);Physics.SyncTransforms();minimum=Mathf.Min(minimum,Vector3.Distance(traffic.PhysicsPosition,player.Body.position));coasted|=!root.GetComponent<Rigidbody>().isKinematic;}
            cases.Add(new JObject{["stoppedPlayerMinimumCentreGap"]=minimum,["followingSpeed"]=traffic.Speed,["uncommandedCollisionCoasting"]=coasted});Require(minimum>6.5f&&traffic.Speed<.2f&&!coasted,"Following traffic must brake and remain stopped behind player");
        }
    }
}
