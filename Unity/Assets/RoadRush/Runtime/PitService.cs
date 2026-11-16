using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    public sealed class PitService : MonoBehaviour
    {
        public GameSession Session;public Vector3 Bay {get;private set;}public float Remaining {get;private set;}
        public bool Servicing=>Remaining>0;public bool AtBay=>Session&&Session.World.IsCircuit&&Vector3.Distance(Session.Car.Body.position,Bay)<5;
        string requestedCarId,serviceCarId;RoadRush.Core.RaceSetup requestedSetup;RacePitCrewCandidate raceCrew;
        public bool RequestSetup(RoadRush.Core.RaceSetup setup)
        {if(setup==null||!Session||Session.Network.Active||Session.DrivenCar.model!=3||!AtBay||Session.Car.AbsoluteSpeed>=.7f||Servicing){Session?.Notify("Stop the single-seat car in the pit bay to request setup fitting");return false;}requestedCarId=Session.DrivenCar.id;requestedSetup=setup.Clone();requestedSetup.Validate();visited=false;Session.Notify("Pit setup queued · close menu to begin free service");return true;}
        readonly List<Transform> crew=new List<Transform>();readonly List<Vector3> homes=new List<Vector3>();Vector3[] lane;PitRouteProgress lapProgress;Quaternion facing;float elapsed;bool visited;
        public static void Prepare(GeoRegion region,RoutePath route)
        {
            if(region.CircuitScenery!=null)return;
            if(!region.circuit||System.Array.Exists(region.roads,r=>r.id=="fictional-pit-lane"))return;var points=new List<Vector3>();float side=1;
            for(int i=0;i<=24;i++){float d=-100+i*12;float blend=Mathf.Min(Mathf.SmoothStep(0,1,(d+100)/55),1-Mathf.SmoothStep(0,1,(d-130)/58));points.Add(route.At(d,side*(region.roads[0].width*.5f+10)*blend));}
            var roads=new List<GeoRoad>(region.roads){new GeoRoad{id="fictional-pit-lane",start="pit-entry",end="pit-exit",name="Pit lane",kind="service",width=7,limit=30,oneWay=1,points=points.ToArray()}};region.roads=roads.ToArray();region.Prepare();
        }
        public void Initialize(GameSession session)
        {
            Session=session;if(!session.World.IsCircuit)return;var route=session.World.Route;lane=System.Array.Find(route.Region.roads,r=>r.id==(route.Region.CircuitScenery?.PitRoadId??"fictional-pit-lane")).points;
            lapProgress=new PitRouteProgress(route,lane);
            Bay=route.Region.CircuitScenery?.Bay??route.At(42,route.Region.roads[0].width*.5f+10);facing=route.Region.CircuitScenery!=null?Quaternion.LookRotation(route.Region.CircuitScenery.BayForward,Vector3.up):route.Rotation(42);
            var root=new GameObject("Free pit service").transform;root.SetParent(transform,false);root.SetPositionAndRotation(Bay,facing);
            var concrete=Geometry.Surface("Pit concrete",new Color(.4f,.41f,.39f),.12f,2);var dark=Geometry.Material("Pit charcoal",new Color(.08f,.09f,.1f));var paint=Geometry.Material("Pit yellow",new Color(.85f,.67f,.08f));
            if(route.Region.CircuitRoadSurfaces==null)Geometry.Box("Pit service pad",root,new Vector3(0,-.05f,0),new Vector3(6,.1f,12),concrete,true);
            if(route.Region.CircuitRoadSurfaces!=null)
            {
                // Paint follows the actual authored height field, rather than
                // a horizontal box that disappears into a sloping pit lane.
                foreach(int side in new[]{-1,1})ServiceMarking("Stop box edge",root,new Vector3(side*1.6f,0,-2.5f),new Vector3(side*1.6f,0,2.5f),.12f,paint);
                ServiceMarking("Stop line",root,new Vector3(-1.65f,0,2.5f),new Vector3(1.65f,0,2.5f),.15f,paint);
            }
            else
            {
                foreach(int side in new[]{-1,1})Geometry.Box("Stop box edge",root,new Vector3(side*1.6f,.022f,0),new Vector3(.12f,.02f,5),paint);
                Geometry.Box("Stop line",root,new Vector3(0,.022f,2.5f),new Vector3(3.3f,.02f,.15f),paint);
            }
            var serviceLabel=Geometry.FittedLabel("PIT · FREE REPAIR",root,new Vector3(0,.05f,-4.2f),Quaternion.Euler(90,0,0),5,1);
            if(route.Region.CircuitRoadSurfaces!=null){var p=serviceLabel.transform.position;p.y=ServiceGroundHeight(p)+.05f;serviceLabel.transform.position=p;}
            if(route.Region.CircuitScenery==null){Geometry.Box("Pit canopy",root,new Vector3(5,4,0),new Vector3(4,.2f,12),dark);foreach(int side in new[]{-1,1})Geometry.Box("Canopy post",root,new Vector3(6,2,side*5),new Vector3(.12f,4,.12f),dark);
            Geometry.Box("Pit sign",root,new Vector3(5,3.4f,-5.5f),new Vector3(4,1,.15f),dark);Geometry.BoardText("FREE PIT SERVICE",root,new Vector3(5,3.4f,-5.5f),3.7f,.8f,.15f);
            }
            var trolley=Geometry.Box("Tool trolley",root,new Vector3(4.7f,.6f,3),new Vector3(1,1.2f,.6f),paint,true);
            if(route.Region.CircuitRoadSurfaces!=null){var p=trolley.transform.position;p.y=ServiceGroundHeight(p)+.6f;trolley.transform.position=p;}
            for(int i=0;i<4;i++)
            {
                var person=new GameObject("Pit mechanic "+i).transform;person.SetParent(root,false);person.localPosition=new Vector3(4.5f,0,-3+i*1.7f);if(route.Region.CircuitRoadSurfaces!=null){var p=person.position;p.y=ServiceGroundHeight(p);person.position=p;}homes.Add(person.position);crew.Add(person);
                var suit=Geometry.Material("Crew overalls",new Color(.12f,.23f,.4f));var skin=Geometry.Material("Crew skin",new Color(.63f,.42f,.29f));
                Geometry.Primitive(PrimitiveType.Capsule,"Overalls",person,new Vector3(0,1,0),new Vector3(.42f,.45f,.28f),suit);Geometry.Primitive(PrimitiveType.Sphere,"Helmet",person,new Vector3(0,1.62f,0),Vector3.one*.3f,paint);
                foreach(int side in new[]{-1,1}){Geometry.Box("Trouser leg",person,new Vector3(side*.12f,.38f,0),new Vector3(.16f,.7f,.19f),suit);Geometry.Box("Boot",person,new Vector3(side*.12f,.08f,.08f),new Vector3(.2f,.16f,.33f),dark);Geometry.Primitive(PrimitiveType.Capsule,"Arm",person,new Vector3(side*.28f,1.05f,.12f),new Vector3(.14f,.29f,.14f),suit);Geometry.Primitive(PrimitiveType.Sphere,"Glove",person,new Vector3(side*.28f,.79f,.12f),Vector3.one*.14f,skin);}
            }
        }
        const float ServicePaintLiftMetres=.012f,ServicePaintSegmentMetres=.5f;
        float ServiceGroundHeight(Vector3 point)
        {
            var region=Session.World.Route.Region;
            return region.CircuitRoadSurfaces!=null&&region.CircuitRoadSurfaces.TryHeight(point.x,point.z,out float top)
                ?top:Session.World.Route.TerrainHeight(point.x,point.z);
        }
        void ServiceMarking(string name,Transform root,Vector3 localA,Vector3 localB,float width,Material material)
        {
            var a=root.TransformPoint(localA);var b=root.TransformPoint(localB);var direction=b-a;direction.y=0;
            var side=Vector3.Cross(Vector3.up,direction.normalized)*(width*.5f);
            int count=Mathf.Max(1,Mathf.CeilToInt(direction.magnitude/ServicePaintSegmentMetres));
            var vertices=new Vector3[(count+1)*2];var uv=new Vector2[vertices.Length];var triangles=new int[count*6];
            for(int i=0;i<=count;i++)for(int edge=0;edge<2;edge++)
            {
                var p=Vector3.Lerp(a,b,(float)i/count)+side*(edge==0?-1:1);p.y=ServiceGroundHeight(p)+ServicePaintLiftMetres;
                int at=i*2+edge;vertices[at]=root.InverseTransformPoint(p);uv[at]=new Vector2(edge,(float)i/count);
            }
            for(int i=0;i<count;i++){int at=i*6,v=i*2;triangles[at]=v;triangles[at+1]=v+2;triangles[at+2]=v+1;triangles[at+3]=v+1;triangles[at+4]=v+2;triangles[at+5]=v+3;}
            Geometry.Mesh(name,root,vertices,triangles,uv,material,false);
        }
        public bool TryLapProgress(Vector3 position,float mainProgress,ref bool engaged,out float progress)
        {progress=mainProgress;return lapProgress!=null&&lapProgress.TryProgress(position,mainProgress,ref engaged,out progress);}
        public bool OnLane(Vector3 position)
        {
            if(lane==null)return false;
            for(int i=0;i<lane.Length-1;i++){var a=lane[i];var b=lane[i+1];var v=b-a;v.y=0;var p=position-a;p.y=0;float t=Mathf.Clamp01(Vector3.Dot(p,v)/Mathf.Max(.001f,v.sqrMagnitude));if((p-v*t).sqrMagnitude<16)return true;}return false;
        }
        void Update()
        {
            if(!Session||!Session.World.IsCircuit||Session.Menu)return;
            if(requestedSetup!=null&&requestedCarId!=Session.DrivenCar.id){requestedSetup=null;requestedCarId=null;Session.Notify("Queued pit fit cancelled · active car changed");}
            if(AtBay&&Session.Car.AbsoluteSpeed<.7f&&(Session.Car.Damage>.1f||requestedSetup!=null)&&!Servicing&&!visited)
            {visited=true;serviceCarId=Session.DrivenCar.id;Remaining=6;elapsed=0;Session.Car.StartLocked=true;Session.Racing?.Lap?.Invalidate("Pit service used");Session.Notify("Pit crew working · free repair · 6 seconds");}
            if(!AtBay&&!Servicing)visited=false;
            if(Servicing&&(!AtBay||Session.Car.AbsoluteSpeed>1||serviceCarId!=Session.DrivenCar.id)){Remaining=0;requestedSetup=null;Session.Car.StartLocked=false;Session.Notify("Pit service aborted · no setup fitted");}
            if(Servicing)
            {
                elapsed+=Time.deltaTime;Remaining=Mathf.Max(0,6-elapsed);
                if(Remaining==0){
                    if(requestedSetup!=null&&Session.IsRenting)
                    {Session.FitHiredRaceSetup(requestedSetup);requestedSetup=null;Session.Notify("Hire setup and tyres fitted · owned garage unchanged");}
                    else if(requestedSetup!=null){Session.SyncActiveCondition();var before=Session.Profile.garage;var fitted=RoadRush.Core.RaceSetupRules.Fit(before,Session.Economy.Credits,requestedSetup);if(fitted==null){requestedSetup=null;Session.Car.StartLocked=false;Session.Notify("Pit fit cancelled · unsupported car");return;}int previousCredits=Session.Profile.credits;Session.Profile.credits=Session.Economy.Credits;fitted.Garage.Active.damage=0;fitted.Garage.Active.dents.Clear();Session.Profile.garage=fitted.Garage;
                        if(Session.Profile.Save()){Session.Car.ReplaceRaceSetup(fitted.Garage.Active.raceSetup);Session.Car.Repair();Session.Notify("Pit setup and tyres saved · GO · no charge");}else{Session.Profile.garage=before;Session.Profile.credits=previousCredits;Session.Notify("Pit save failed · setup was not applied");}requestedSetup=null;
                    }else Session.FreeRepair("Pit stop complete · GO · no charge");Session.Car.StartLocked=false;}
            }
            Session.Car.Visual.SetRaceWheelsVisible(!(Servicing&&elapsed>=2.6f&&elapsed<3.6f));
            if(Session.DrivenCar.model==3)
            {
                if(!raceCrew){var g=new GameObject("Race pit crew");g.transform.SetParent(transform,false);raceCrew=g.AddComponent<RacePitCrewCandidate>();raceCrew.Build(Session.Car.Metrics.Track,Session.Car.Metrics.Wheelbase,Session.Car.Metrics.Radius,Session.Car.Metrics.TyreWidth);}
                raceCrew.transform.SetPositionAndRotation(new Vector3(Session.Car.transform.position.x,Bay.y,Session.Car.transform.position.z),Session.Car.transform.rotation);
                raceCrew.Pose(!Servicing?RoadRush.Core.RacePitPhase.Idle:elapsed<1.5f?RoadRush.Core.RacePitPhase.BringTyres:elapsed<3?RoadRush.Core.RacePitPhase.RemoveWheels:elapsed<4.5f?RoadRush.Core.RacePitPhase.FitWheels:RoadRush.Core.RacePitPhase.ReturnOldTyres,elapsed);
                raceCrew.gameObject.SetActive(AtBay||Servicing);
            }else if(raceCrew)raceCrew.gameObject.SetActive(false);
            for(int i=0;i<crew.Count;i++)
            {crew[i].gameObject.SetActive(Session.DrivenCar.model!=3);
                var target=Servicing?Session.Car.transform.TransformPoint(new Vector3(i<2?-1.55f:1.55f,0,i%2==0?-1.36f:1.36f)):homes[i];target.y=Session.World.Route.Region.CircuitRoadSurfaces!=null?ServiceGroundHeight(target):Bay.y;
                crew[i].position=Vector3.MoveTowards(crew[i].position,target,Time.deltaTime*2.7f);if(Session.World.Route.Region.CircuitRoadSurfaces!=null){var p=crew[i].position;p.y=ServiceGroundHeight(p);crew[i].position=p;}if(Servicing){crew[i].rotation=Quaternion.LookRotation((Session.Car.Body.position-crew[i].position).normalized,Vector3.up);crew[i].localScale=new Vector3(1,.82f+Mathf.Sin(elapsed*13+i)*.025f,1);}else{crew[i].rotation=facing;crew[i].localScale=Vector3.one;}
            }
        }
        void OnDestroy(){if(Session&&Session.Car){Session.Car.StartLocked=false;Session.Car.Visual.SetRaceWheelsVisible(true);}}
    }
}
