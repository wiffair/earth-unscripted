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
    // Actual collidable traffic, native kinematic moves, road support and static
    // blockers. Waiting timers advance solely through the real driver's dt.
    public static class TrafficCooperationVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly MethodInfo Step=typeof(TrafficCar).GetMethod("FixedUpdate",Hidden);
        static int checks;
        static void Check(bool okay,string why){if(!okay)throw new InvalidDataException("TRAFFIC COOPERATION FAIL "+why);checks++;}
        sealed class World:IDisposable
        {
            public readonly List<GameObject> owned=new List<GameObject>();
            public readonly List<TrafficCar> cars=new List<TrafficCar>();
            public readonly GeoRegion region;public readonly RoutePath route;public readonly VehicleController player;
            public bool overlap,coasting;public float largestReverseStep;
            public World(int side,params GeoRoad[] roads)
            {
                region=new GeoRegion{countryCode="GB",drivingSide=side,roads=roads,route=(Vector3[])roads[0].points.Clone(),waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),minX=-2000,minZ=-2000,width=4000,depth=4000,nx=2,nz=2,heights=new[]{1000f,1000f,1000f,1000f}};
                region.RebuildRenderQueryIndexesOnly(true);
                var root=Own("Supported traffic cooperation fixture");typeof(GeoRegion).GetField("NativeWorld",Hidden).SetValue(region,root.AddComponent<NewbridgeWorld>());
                foreach(var road in roads)
                {
                    var floor=Own("Road asphalt");VehicleObstacleWheels.MarkSuspensionSurface(floor,"Road asphalt");var box=floor.AddComponent<BoxCollider>();
                    var a=road.points[0];var b=road.points[road.points.Length-1];floor.transform.position=(a+b)*.5f-Vector3.up*.1f;floor.transform.rotation=Quaternion.LookRotation(b-a);box.size=new Vector3(road.width,.2f,Vector3.Distance(a,b));
                }
                route=new RoutePath(region);var pr=Own("Distant actual player");pr.layer=8;pr.AddComponent<Rigidbody>();player=pr.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",Hidden).Invoke(player,null);player.Body.isKinematic=true;player.Body.position=new Vector3(1500,1000,1500);
            }
            public GameObject Own(string name){var root=new GameObject(name);owned.Add(root);return root;}
            public TrafficCar Car(GeoRoad road,float station,int direction,float speed=0)
            {
                var root=Own("Actual negotiating traffic");root.layer=8;root.AddComponent<Rigidbody>();var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,Core.VehicleMetric.ColliderCentreY,0);box.size=new Vector3(1.95f,Core.VehicleMetric.ColliderHeight,4.15f);
                var visual=root.AddComponent<CarVisual>();visual.Build(Color.blue);VehicleGeometry.ApplyCollider(box,0);
                var car=root.AddComponent<TrafficCar>();typeof(TrafficCar).GetMethod("Awake",Hidden).Invoke(car,null);car.Route=route;car.Player=player;car.Visual=visual;car.Direction=direction;car.Speed=speed;
                typeof(TrafficCar).GetField("networkRoad",Hidden).SetValue(car,road);typeof(TrafficCar).GetField("networkDistance",Hidden).SetValue(car,direction>0?station:road.length-station);
                var position=region.RoadPoint(road,station,out var forward);forward*=direction;
                float lane=Mathf.Min(road.width*.24f,2.4f,Mathf.Max(0,road.width*.5f-Core.VehicleDimensions.ForModel(0).BodyWidth*.5f-.2f));
                position+=Vector3.Cross(Vector3.up,forward).normalized*lane*region.drivingSide;
                var body=root.GetComponent<Rigidbody>();body.interpolation=RigidbodyInterpolation.None;body.position=VehicleGeometry.RoadRestPosition(position,0);body.rotation=Quaternion.LookRotation(forward);
                cars.Add(car);foreach(var c in cars)c.Fleet=cars.ToArray();Physics.SyncTransforms();return car;
            }
            public void Drive(float seconds,Action<int> observe=null,TrafficCar parked=null)
            {
                var previous=new Vector3[cars.Count];
                for(int tick=0;tick<Mathf.RoundToInt(seconds/.01f);tick++)
                {
                    for(int i=0;i<cars.Count;i++)previous[i]=cars[i].PhysicsPosition;
                    foreach(var car in cars)if(car!=parked&&car.gameObject.activeInHierarchy)Step.Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();
                    for(int i=0;i<cars.Count;i++)
                    {
                        var car=cars[i];if(!car.gameObject.activeInHierarchy)continue;coasting|=!car.GetComponent<Rigidbody>().isKinematic;
                        if(car.ReversingForTraffic)largestReverseStep=Mathf.Max(largestReverseStep,Vector3.Distance(previous[i],car.PhysicsPosition));
                        foreach(var a in car.GetComponentsInChildren<BoxCollider>())
                        {
                            for(int j=i+1;j<cars.Count;j++)foreach(var b in cars[j].gameObject.activeInHierarchy?cars[j].GetComponentsInChildren<BoxCollider>():Array.Empty<BoxCollider>())
                                overlap|=Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float depth)&&depth>.01f;
                            foreach(var obstacle in owned)
                            {
                                if(!obstacle||!obstacle.name.StartsWith("Blocking"))continue;var b=obstacle.GetComponent<BoxCollider>();
                                overlap|=Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out float obstacleDepth)&&obstacleDepth>.01f;
                            }
                        }
                    }
                    observe?.Invoke(tick);
                }
            }
            public void Dispose(){foreach(var root in owned)if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}}
        }
        static GeoRoad Road(string id,string start,string end,Vector3 a,Vector3 b,float width,string kind="residential")
            =>new GeoRoad{id=id,start=start,end=end,kind=kind,width=width,NativeTravelWidth=width,limit=30,points=new[]{a,b}};
        static void AddSourceSpeed(GeoRoad road)
        {road.NativeControls=new[]{new NewbridgeWorld.Control{id=road.id+"-speed",kind="speed",value="30",x=road.points[0].x,y=1000,z=road.points[0].z,dz=1}};}
        static Vector3 P(float x,float z)=>new Vector3(x,1000,z);
        static string State(TrafficCar car)
        {
            var names=new[]{"networkDistance","blockedAge","cooperationPhase","retreatOffset","retreatTarget","cooperationAge"};
            return car.PhysicsPosition+" speed="+car.Speed+" road="+car.RoadForDiagnostics?.id+" reverse="+car.CooperativeReverseMetres+" yields="+car.CooperativeYields+" "+string.Join(" ",names.Select(n=>n+"="+typeof(TrafficCar).GetField(n,Hidden).GetValue(car)));
        }
        static void Narrow(int side,bool rearQueue,bool staticBlock)
        {
            var west=Road("west-bay","west","mouth-a",P(0,-1000),P(0,0),6);
            var narrow=Road("single-track","mouth-a","mouth-b",P(0,0),P(0,70),3.4f);
            var east=Road("east-bay","mouth-b","east",P(0,70),P(0,1000),6);
            AddSourceSpeed(west);AddSourceSpeed(narrow);AddSourceSpeed(east);
            using(var world=new World(side,west,narrow,east))
            {
                var a=world.Car(west,1000+(staticBlock?-1:-8),1,4);var b=world.Car(narrow,18,-1,4);
                if(rearQueue)world.Car(west,981,1,4);
                if(staticBlock){var block=world.Own("Blocking rear wall");block.transform.position=P(0,-7.2f)+Vector3.up;block.AddComponent<BoxCollider>().size=new Vector3(6,2,1);Physics.SyncTransforms();}
                world.Drive(rearQueue||staticBlock?12:35,tick=>{if(tick%300==0)Debug.Log("NARROW PROGRESS "+tick+" a="+State(a)+" b="+State(b));});
                Debug.Log("NARROW FINAL a="+State(a)+" b="+State(b));
                Check(!world.overlap&&!world.coasting,"narrow encounter remains collidable and separated, side="+side+" queue="+rearQueue+" obstacle="+staticBlock);
                Check(world.largestReverseStep<.08f,"reverse uses continuous walking-speed poses across passing-place boundary");
                if(rearQueue||staticBlock)Check(a.CooperativeReverseMetres+b.CooperativeReverseMetres==0&&a.Speed<.2f&&b.Speed<.2f,"occupied rear corridor prevents unsafe reversing");
                else Check(a.CooperativeReverseMetres>3&&a.CooperativeYields==1&&b.PhysicsPosition.z< -20&&a.PhysicsPosition.z>20,"one car retreats into a real bay, lets peer pass, then resumes forward");
                Debug.Log("TRAFFIC NARROW CASE side="+side+" queue="+rearQueue+" obstacle="+staticBlock+" z="+a.PhysicsPosition.z+","+b.PhysicsPosition.z+" reversed="+a.CooperativeReverseMetres);
            }
        }
        static void Junction(int side,bool red)
        {
            var south=Road("main-south","south","j",P(0,-200),P(0,0),6,"primary");south.NativeTurnLanes="through";
            var north=Road("main-north","j","north",P(0,0),P(0,1000),6,"primary");
            AddSourceSpeed(south);AddSourceSpeed(north);
            var minor=Road("minor","minor-end","j",P(200,0),P(0,0),5);minor.NativeTurnLanes="right";
            minor.NativeControls=new[]{new NewbridgeWorld.Control{id="minor-give-way",kind="give_way",y=1000,dx=-1}};
            if(red)
            {
                var signal=new NewbridgeWorld.Control{id="red-main",kind="traffic_signals",y=1000,dz=1};
                if(TrafficControlPhase.State(signal,Time.time,"GB")==2)signal.dx=1.1f;south.NativeControls=south.NativeControls.Concat(new[]{signal}).ToArray();
            }
            using(var world=new World(side,south,north,minor))
            {
                var donor=world.Car(south,194,1);var beneficiary=world.Car(minor,196.5f,1);
                // A parked, physically present main-road car holds the mapped
                // give-way approach. Advance only the waiting driver's real dt,
                // then let both drivers negotiate; no timer is forced/reflected.
                world.Drive(8.2f,null,donor);
                world.Drive(red?14:30,tick=>{if(tick%500==0)Debug.Log("COURTESY PROGRESS "+tick+" a="+State(donor)+" b="+State(beneficiary));});
                Debug.Log("COURTESY FINAL a="+State(donor)+" b="+State(beneficiary));
                Check(!world.overlap&&!world.coasting,"junction courtesy retains real collision clearance, side="+side+" red="+red);
                if(red)Check(donor.CooperativeCourtesyYields==0&&donor.PhysicsPosition.z<0&&beneficiary.PhysicsPosition.x>0,"red main approach cannot offer or bypass a signal");
                else Check(donor.CooperativeCourtesyYields==1&&donor.CooperativeReverseMetres>2&&beneficiary.PhysicsPosition.z>15&&donor.PhysicsPosition.z>5,"aged minor-road waiter receives a peer-specific courtesy gap and both continue");
                Debug.Log("TRAFFIC COURTESY CASE side="+side+" red="+red+" donor="+donor.PhysicsPosition+" beneficiary="+beneficiary.PhysicsPosition+" count="+donor.CooperativeCourtesyYields);
            }
        }
        static void InterruptedPeer()
        {
            var west=Road("interrupted-west","west","a",P(0,-1000),P(0,0),6);
            var narrow=Road("interrupted-narrow","a","b",P(0,0),P(0,70),3.4f);
            var east=Road("interrupted-east","b","east",P(0,70),P(0,1000),6);
            AddSourceSpeed(west);AddSourceSpeed(narrow);AddSourceSpeed(east);
            using(var world=new World(-1,west,narrow,east))
            {
                var a=world.Car(west,992,1,4);var b=world.Car(narrow,18,-1,4);
                var phase=typeof(TrafficCar).GetField("cooperationPhase",Hidden);bool removed=false,wasActive=false;float maximumStep=0;var previous=a.PhysicsPosition;
                world.Drive(25,_=>
                {
                    bool active=(int)phase.GetValue(a)>0;
                    if(active||wasActive)maximumStep=Mathf.Max(maximumStep,Vector3.Distance(previous,a.PhysicsPosition));
                    if(!removed&&a.ReversingForTraffic&&a.CooperativeReverseMetres>2){b.gameObject.SetActive(false);removed=true;}
                    previous=a.PhysicsPosition;wasActive=active;
                });
                Check(removed&&maximumStep<.08f,"disappearing peer cancels negotiation through continuous supported retracing");
                Check(!world.overlap&&!world.coasting&&(int)phase.GetValue(a)==0&&a.PhysicsPosition.z>30,"interrupted donor resumes the real cursor without snapping or a stranded agreement");
            }
        }
        static void Lateral(int side)
        {
            var road=Road("meeting-lanes","a","b",P(0,-500),P(0,500),4.8f);
            using(var world=new World(side,road))
            {
                var a=world.Car(road,470,1,8);var b=world.Car(road,530,-1,8);float edge=0;
                world.Drive(12,_=>{edge=Mathf.Max(edge,Mathf.Abs(a.PhysicsPosition.x)+Core.VehicleDimensions.ForModel(0).BodyWidth*.5f,Mathf.Abs(b.PhysicsPosition.x)+Core.VehicleDimensions.ForModel(0).BodyWidth*.5f);});
                Check(a.CooperativeLateralAdjustments+b.CooperativeLateralAdjustments>0&&edge<=road.width*.5f-.15f,"opposing drivers adjust inside supported own-lane edge, side="+side);
                Check(!world.overlap&&!world.coasting&&a.PhysicsPosition.z>30&&b.PhysicsPosition.z< -30,"lateral courtesy passes actual collidable cars without stopping");
            }
        }
        public static void Run()
        {
            checks=0;var mode=Physics.simulationMode;float scale=Time.timeScale,step=Time.fixedDeltaTime;
            try
            {
                Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;Time.fixedDeltaTime=.01f;
                foreach(int side in new[]{-1,1}){Narrow(side,false,false);Junction(side,false);Lateral(side);}
                Narrow(-1,true,false);Narrow(-1,false,true);Junction(-1,true);InterruptedPeer();
                Debug.Log("TRAFFIC COOPERATION PASS "+checks+" actual supported narrow-road, reverse, queue, obstacle, courtesy and lane checks");
            }
            finally{Physics.simulationMode=mode;Time.timeScale=scale;Time.fixedDeltaTime=step;}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
