using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
    // Invoke the actual traffic driver and PhysX kinematic moves, including
    // production chassis/cabin shapes. No collision layers are ignored here.
    public static class TrafficPassingVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly MethodInfo Step=typeof(TrafficCar).GetMethod("FixedUpdate",Hidden);
        static readonly FieldInfo NetworkRoad=typeof(TrafficCar).GetField("networkRoad",Hidden),NetworkDistance=typeof(TrafficCar).GetField("networkDistance",Hidden);
        static int checks;
        static void Check(bool okay,string why){if(!okay)throw new InvalidDataException("TRAFFIC PASSING FAIL "+why);checks++;}
        static TrafficCar Traffic(RoutePath route,VehicleController player,float z,int direction,List<GameObject> owned)
        {
            var root=new GameObject("Actual queued traffic "+direction+" "+z);owned.Add(root);root.layer=8;root.AddComponent<Rigidbody>();
            var chassis=root.AddComponent<BoxCollider>();chassis.center=new Vector3(0,Core.VehicleMetric.ColliderCentreY,0);chassis.size=new Vector3(1.95f,Core.VehicleMetric.ColliderHeight,4.15f);
            var visual=root.AddComponent<CarVisual>();visual.Build(Color.blue);VehicleGeometry.ApplyCollider(chassis,0);
            var car=root.AddComponent<TrafficCar>();typeof(TrafficCar).GetMethod("Awake",Hidden).Invoke(car,null);
            car.Route=route;car.Player=player;car.Visual=visual;car.Direction=direction;car.Speed=10;
            var road=route.Region.roads[0];NetworkRoad.SetValue(car,road);NetworkDistance.SetValue(car,direction>0?z+500:500-z);
            float lane=road.NativeTravelWidth*.24f;var body=root.GetComponent<Rigidbody>();body.interpolation=RigidbodyInterpolation.None;
            body.position=VehicleGeometry.RoadRestPosition(new Vector3(direction*lane*route.Region.drivingSide,1000,z),0);body.rotation=Quaternion.LookRotation(Vector3.forward*direction);
            return car;
        }
        static void Case(int side,float width,bool stoppedPlayer,bool headOn,bool red)
        {
            var owned=new List<GameObject>();
            try
            {
                var world=new GameObject("Automatic two-way passing fixture");owned.Add(world);var renderer=world.AddComponent<NewbridgeWorld>();
                var road=new GeoRoad{id="meeting-road",start="a",end="b",kind="residential",width=width,NativeTravelWidth=width,limit=50,points=new[]{new Vector3(0,1000,-500),new Vector3(0,1000,500)}};
                var region=new GeoRegion{countryCode="GB",drivingSide=side,roads=new[]{road},route=(Vector3[])road.points.Clone(),waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),minX=-1000,minZ=-1000,width=2000,depth=2000,nx=2,nz=2,heights=new[]{1000f,1000f,1000f,1000f}};
                region.RebuildRenderQueryIndexesOnly(true);typeof(GeoRegion).GetField("NativeWorld",Hidden).SetValue(region,renderer);
                if(red)
                {
                    var signal=new NewbridgeWorld.Control{kind="traffic_signals",id="meeting-red",x=0,y=1000,z=0,dz=1};
                    // The two signal axes are never green together. The
                    // driver's accepted orientation cone still points forward.
                    if(TrafficControlPhase.State(signal,Time.time,region.countryCode)==2)signal.dx=1.1f;
                    Check(TrafficControlPhase.State(signal,Time.time,region.countryCode)!=2,"fixture signal is red/amber during scripted physics");road.NativeControls=new[]{signal};
                }
                var route=new RoutePath(region);
                var pr=new GameObject("Stopped real player");owned.Add(pr);pr.layer=8;pr.AddComponent<Rigidbody>();var pc=pr.AddComponent<BoxCollider>();pc.center=new Vector3(0,Core.VehicleMetric.ColliderCentreY,0);pc.size=new Vector3(Core.VehicleDimensions.ForModel(0).ColliderWidth,Core.VehicleMetric.ColliderHeight,Core.VehicleDimensions.ForModel(0).ColliderLength);
                var player=pr.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",Hidden).Invoke(player,null);player.Body.isKinematic=true;
                float lane=width*.24f;player.Body.position=VehicleGeometry.RoadRestPosition(stoppedPlayer?new Vector3((headOn?1:-1)*lane*side,1000,0):new Vector3(1000,1000,1000),0);player.Body.rotation=Quaternion.LookRotation(Vector3.back);
                var cars=new List<TrafficCar>{Traffic(route,player,-50,1,owned)};
                if(!stoppedPlayer){cars.Add(Traffic(route,player,-67,1,owned));cars.Add(Traffic(route,player,50,-1,owned));cars.Add(Traffic(route,player,67,-1,owned));}
                foreach(var car in cars)car.Fleet=cars.ToArray();Physics.SyncTransforms();
                float minimumPlayer=float.MaxValue;bool collisionCoasting=false,penetrated=false;var bodies=cars.Select(c=>c.GetComponent<Rigidbody>()).ToArray();
                for(int tick=0;tick<1200;tick++)
                {
                    foreach(var car in cars)Step.Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();
                    for(int i=0;i<cars.Count;i++)
                    {
                        collisionCoasting|=!bodies[i].isKinematic;minimumPlayer=Mathf.Min(minimumPlayer,Vector3.Distance(bodies[i].position,player.Body.position));
                        if(stoppedPlayer)
                        {
                            var own=cars[i].GetComponent<BoxCollider>();
                            penetrated|=Physics.ComputePenetration(own,own.transform.position,own.transform.rotation,pc,pc.transform.position,pc.transform.rotation,out _,out float playerDepth)&&playerDepth>.005f;
                        }
                        for(int j=i+1;j<cars.Count;j++)
                        {
                            var a=cars[i].GetComponent<BoxCollider>();var b=cars[j].GetComponent<BoxCollider>();
                            penetrated|=Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float depth)&&depth>.005f;
                        }
                    }
                }
                Check(!penetrated&&!collisionCoasting,"actual chassis remain separated and powered, side="+side+" width="+width+" player="+stoppedPlayer);
                if(headOn||width<4)
                    Check(cars[0].PhysicsPosition.z< -6.5f&&cars[0].Speed<.2f&&minimumPlayer>6.5f,"genuine opposing collision/narrow meeting is not ignored");
                else if(red)
                    Check(cars[0].PhysicsPosition.z<=-3.45f&&cars[0].PhysicsPosition.z> -4&&cars[0].Speed<.2f&&cars[1].PhysicsPosition.z<cars[0].PhysicsPosition.z-6.5f&&cars[2].PhysicsPosition.z< -50,"red-light queue holds while separate oncoming lane passes");
                else if(stoppedPlayer)
                    Check(cars[0].PhysicsPosition.z>50,"traffic passes a physically separate stopped player without unpowered collision handover");
                else
                    Check(cars[0].PhysicsPosition.z>50&&cars[1].PhysicsPosition.z>30&&cars[2].PhysicsPosition.z< -50&&cars[3].PhysicsPosition.z< -30,"opposing leads and both queued followers actually pass, side="+side);
                Debug.Log("TRAFFIC PASSING CASE side="+side+" width="+width+" player="+stoppedPlayer+" headOn="+headOn+" red="+red+" positions="+string.Join(",",cars.Select(c=>c.PhysicsPosition.z.ToString("F2"))));
            }
            finally{foreach(var root in owned)if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}}
        }
        public static void Run()
        {
            checks=0;var previous=Physics.simulationMode;float scale=Time.timeScale,fixedStep=Time.fixedDeltaTime;
            try
            {
                Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;Time.fixedDeltaTime=.01f;
                Check(5*.48f<2.6f,"old fixed following corridor blocks separate 5m-road lanes");
                foreach(int side in new[]{-1,1}){Case(side,5,false,false,false);Case(side,5,true,false,false);Case(side,5,false,false,true);}
                Case(-1,5,true,true,false);Case(-1,3.4f,true,false,false);
                Debug.Log("TRAFFIC PASSING PASS "+checks+" actual multi-body lane, queue, player and control checks");
            }
            finally{Physics.simulationMode=previous;Time.timeScale=scale;Time.fixedDeltaTime=fixedStep;}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
