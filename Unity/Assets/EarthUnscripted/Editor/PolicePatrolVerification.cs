using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    // Actual production policy/planning/LOS calls, not a replacement simulation.
    // Does not claim physical factory provenance or a sustained native patrol drive.
    public static class PolicePatrolVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static int checks;
        static void Check(bool pass,string why){if(!pass)throw new Exception("POLICE PATROL "+why);checks++;}
        static void Set(object owner,string property,object value)=>owner.GetType().GetProperty(property,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(owner,value);
        static void Awake(Component owner)=>owner.GetType().GetMethod("Awake",Hidden).Invoke(owner,null);
        public static int Exercise()
        {
            checks=0;var schedule=new PoliceEncounterSchedule(0);Check(schedule.RemainingMetres==800,"Real policy requires at least800m between placements");
            schedule.Observe(900,false);Check(!schedule.Due,"Ineligible/stationary travel cannot advance encounter");schedule.Observe(double.NaN,true);schedule.Observe(double.PositiveInfinity,true);Check(schedule.RemainingMetres==800,"Invalid displacement cannot schedule a patrol");schedule.Observe(799,true);Check(!schedule.Due,"Minimum travel cannot be bypassed");schedule.Observe(1,true);Check(schedule.Due,"Accepted real travel makes a placement due without granting placement authority");
            schedule.Reset(.5);Check(Math.Abs(schedule.RemainingMetres-(800+2500*Math.Log(2)))<.001,"Configured extra gap follows the exponential distribution");
            var random=new System.Random(24817);double min=double.MaxValue,max=0,sum=0;for(int i=0;i<10000;i++){schedule.Reset(random.NextDouble());min=Math.Min(min,schedule.RemainingMetres);max=Math.Max(max,schedule.RemainingMetres);sum+=schedule.RemainingMetres;}
            Check(min>=800&&max>12000&&sum/10000>3000&&sum/10000<3600,"Seeded source policy has long irregular gaps, not fixed spacing");
            var domain=typeof(GameSession).Assembly.GetType("EarthUnscripted.VehiclePopulation.ActualVehicleRosterDomain",true);Check((int)domain.GetField("RoadPoliceSlots",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue()==1,"Actual source declares exactly one required base patrol");
            var scoped=Activator.CreateInstance(domain,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false,true},null);
            var required=domain.GetField("requiredBootSlots",Hidden).GetValue(scoped);Check((int)required.GetType().GetProperty("Count").GetValue(required)==28,"Reviewed scoped800 source still requires its genuine28 actors including3patrols");
            void Move(VehicleController car,Vector3 position){car.transform.position=position;car.Body.position=position;}
            var roots=new List<GameObject>();
            GameObject Root(string name){var g=new GameObject(name);g.SetActive(false);roots.Add(g);return g;}
            VehicleController Car(string name,Vector3 position){var g=Root(name);g.transform.position=position;g.SetActive(true);g.layer=8;g.AddComponent<Rigidbody>();var c=g.AddComponent<VehicleController>();Awake(c);c.Body.isKinematic=false;c.Body.position=position;return c;}
            try
            {
                var session=Root("Inactive police policy session").AddComponent<GameSession>();var network=session.gameObject.AddComponent<LanSession>();Set(session,"Network",network);
                var player=Car("Player observation target",new Vector3(0,1001,130));Set(session,"Car",player);Set(session,"OnRoad",true);Set(session,"CurrentRoad",new GeoRoad{limit=30});player.Body.linearVelocity=Vector3.forward*20;
                var patrol=Car("Physical police planner",new Vector3(0,1001,100));var driver=patrol.gameObject.AddComponent<PoliceDriver>();driver.Session=session;Set(driver,"Car",patrol);driver.CooldownUntil=Time.time-1;
                Set(driver,"State",PoliceDriver.Duty.Parked);Physics.SyncTransforms();Check(driver.CanWitnessOffence(70),"Actual LOS witness accepts a parked patrol; gap="+Vector3.Distance(player.Body.position,patrol.Body.position)+" parked="+driver.IdlePatrol+" network="+network.Active);Set(driver,"State",PoliceDriver.Duty.Cruising);Check(driver.CanObserveSpeeding(),"Actual speeding witness accepts cruising as well as parked");
                var wall=Root("Actual LOS blocker");wall.transform.position=new Vector3(0,1002,115);var box=wall.AddComponent<BoxCollider>();box.size=new Vector3(10,10,1);wall.SetActive(true);Physics.SyncTransforms();Check(!driver.CanWitnessOffence(70),"Real collider occlusion blocks crime observation");wall.SetActive(false);Physics.SyncTransforms();
                Set(driver,"State",PoliceDriver.Duty.Pursuit);Check(!driver.CanWitnessOffence(70),"Pursuit is not a new idle observation");Set(driver,"State",PoliceDriver.Duty.Cruising);driver.CooldownUntil=Time.time+60;Check(!driver.CanWitnessOffence(70),"Post-pursuit cooldown applies to a cruising observer");driver.CooldownUntil=Time.time-1;
                var road=new GeoRoad{id="patrol-source",start="a",end="b",kind="primary",width=8,oneWay=1,limit=70,points=new[]{new Vector3(0,1000,0),new Vector3(0,1000,300)}};
                var region=new GeoRegion{drivingSide=-1,minX=-100,minZ=0,width=200,depth=400,nx=2,nz=2,heights=new[]{1000f,1000f,1000f,1000f},roads=new[]{road},route=road.points,waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>()};region.Prepare();
                var world=Root("Inactive planning world").AddComponent<WorldBuilder>();Set(world,"Route",new RoutePath(region));Set(session,"World",world);
                var marker=patrol.gameObject.AddComponent<TrafficCar>();Awake(marker);marker.Session=session;marker.Player=player;marker.Route=world.Route;marker.Police=true;marker.ManagedPolice=driver;driver.Marker=marker;patrol.Body.isKinematic=false;
                var plan=typeof(TrafficCar).GetMethod("PlanPhysicalPatrol",Hidden);var reset=typeof(TrafficCar).GetMethod("ResetPhysicalPatrolPlan",Hidden);
                float Plan(){object[] args={patrol,.02f,null,0f};Check((bool)plan.Invoke(marker,args),"Actual source-road patrol planning succeeds");return(float)args[3];}
                var cameraRoot=Root("Controlled visibility camera");cameraRoot.transform.position=new Vector3(0,1001,0);cameraRoot.transform.rotation=Quaternion.identity;cameraRoot.AddComponent<Camera>().farClipPlane=1000;Set(session,"View",cameraRoot.AddComponent<DriveCamera>());
                var visible=typeof(GameSession).GetMethod("PopulationVisible",Hidden);var clear=typeof(GameSession).GetMethod("PatrolClear",Hidden);var visiblePoint=new Vector3(0,1000,450);
                Check((bool)visible.Invoke(session,new object[]{visiblePoint,4f}),"Controlled candidate is actually in the expanded camera view");Check(!(bool)clear.Invoke(session,new object[]{driver,visiblePoint,Vector3.forward}),"Visible candidate cannot grant an idle recycle");
                Set(driver,"State",PoliceDriver.Duty.Pursuit);var held=patrol.Body.position;Check(!(bool)typeof(PoliceDriver).GetMethod("TryPlaceIdle",Hidden).Invoke(driver,new object[]{visiblePoint,Vector3.forward,false,90f})&&patrol.Body.position==held,"Pursuit recycle refuses before any physical pose mutation");Set(driver,"State",PoliceDriver.Duty.Cruising);
                Move(player,new Vector3(0,1001,240));var before=patrol.Body.position;float open=Plan();Check(marker.Direction==1,"Actual one-way source fixes legal forward travel");Check(open>0&&!patrol.Body.isKinematic&&patrol.Body.position==before,"Planning yields a command without moving or making the real body kinematic");
                road.oneWay=-1;reset.Invoke(marker,null);Plan();Check(marker.Direction==-1,"Reverse one-way source authority survives physical pose heading");road.oneWay=1;reset.Invoke(marker,null);
                road.NativeControls=new[]{new Native.Control{id="mapped-stop",kind="stop",x=0,y=1000,z=120,dx=0,dz=1}};Move(patrol,new Vector3(0,1001,116.5f));reset.Invoke(marker,null);Check(Plan()<.01f,"Shared mapped stop control holds the physical planner behind the line");
                var signalA=new Native.Control{id="signal-a",kind="traffic_signals",x=0,y=1000,z=120,dx=0,dz=1};var signalB=new Native.Control{id="signal-b",kind="traffic_signals",x=0,y=1000,z=120,dx=1,dz=.5f};
                var red=EarthUnscripted.NewbridgePrototype.TrafficControlPhase.State(signalA,Time.time,"GB")!=2?signalA:signalB;road.NativeControls=new[]{red};reset.Invoke(marker,null);Check(Plan()<.01f,"Current simulated non-green signal holds the actual cruise command");
                var green=EarthUnscripted.NewbridgePrototype.TrafficControlPhase.State(signalA,Time.time,"GB")==2?signalA:EarthUnscripted.NewbridgePrototype.TrafficControlPhase.State(signalB,Time.time,"GB")==2?signalB:null;
                if(green!=null){road.NativeControls=new[]{green};reset.Invoke(marker,null);Check(Plan()>0,"Current simulated green permits ordinary cruising");}
                road.NativeControls=null;Move(patrol,new Vector3(0,1001,100));Move(player,new Vector3(0,1001,105));reset.Invoke(marker,null);Check(Plan()<.01f,"Cruising planner stops for the actual player in front");
                Move(player,new Vector3(0,1001,240));reset.Invoke(marker,null);Check(Plan()>0,"Readiness fixture has an otherwise open legal source command");
                var host=world.gameObject.AddComponent<AutomaticWorldHost>();var renderer=Root("Actual empty physical road renderer").AddComponent<Native>();Set(host,"Renderer",renderer);Set(world,"AutomaticWorld",host);reset.Invoke(marker,null);Check(Plan()<.01f,"Unpublished physical asphalt stops cruise despite a numeric road");
                Set(session,"Menu",false);var scheduleField=typeof(GameSession).GetField("patrolEncounter",Hidden);scheduleField.SetValue(session,new PoliceEncounterSchedule(.5));typeof(GameSession).GetMethod("TickPatrolPopulation",Hidden).Invoke(session,new object[]{.02f});Check(scheduleField.GetValue(session)==null,"Changed world/player ownership discards a stale actual encounter scheduler");
                Set(world,"AutomaticWorld",null);
                patrol.gameObject.SetActive(true);patrol.Body.isKinematic=false;
                var own=new GameObject("Own layer0 cabin obstacle fixture");own.transform.SetParent(patrol.transform,false);own.transform.localPosition=new Vector3(0,.85f,4);var ownBox=own.AddComponent<BoxCollider>();ownBox.size=new Vector3(2,2,.3f);Physics.SyncTransforms();
                var obstacle=typeof(PoliceDriver).GetMethod("PatrolObstacleSpeed",Hidden);var aim=patrol.Body.position+Vector3.forward*10;
                Check((float)obstacle.Invoke(driver,new object[]{10f,aim})==10,"Actual sphere cast ignores own layer0 child collider on the physical body");
                wall.transform.position=patrol.Body.position+new Vector3(0,.85f,5);box.size=new Vector3(2,2,.3f);wall.SetActive(true);Physics.SyncTransforms();Check((float)obstacle.Invoke(driver,new object[]{10f,aim})<10,"Same sphere cast still brakes for a real external obstacle");wall.SetActive(false);
                var passing=typeof(TrafficCar).GetMethod("FindPassingPlace",Hidden);object[] passingArgs={marker,0f};Check(!(bool)passing.Invoke(marker,passingArgs),"Physical police cannot be recruited as a kinematic retreat donor");
            }
            finally{for(int i=roots.Count-1;i>=0;i--)if(roots[i])UnityEngine.Object.DestroyImmediate(roots[i]);}
            return checks;
        }
        public static void Run(){Debug.Log("POLICE PATROL SOURCE/LOS/PLANNING PASS "+Exercise()+"; physical cruising/recycling remains native acceptance");}
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
