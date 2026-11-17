using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed class LivingScenery : MonoBehaviour
    {
        sealed class Actor {public Transform Root;public Transform[] Limbs;public int Kind,Seed;public Vector3 Home,Target;public float Phase,Wait,Retry;}
        public GameSession Session;readonly List<Actor> actors=new List<Actor>();float nextCheck;
        public int Count=>actors.Count;
        public void Initialize(GameSession session)
        {
            Session=session;for(int i=0;i<24;i++){var actor=Build(i,i<12?0:i<20?1:2);actors.Add(actor);Place(actor);}
        }
        Actor Build(int seed,int kind)
        {
            var root=new GameObject(kind==0?"Walking resident":kind==1?"Grazing sheep":"Woodland deer").transform;root.SetParent(transform,false);var a=new Actor{Root=root,Kind=kind,Seed=seed,Phase=seed};var limbs=new List<Transform>();
            var dark=Geometry.Material("Wildlife dark",new Color(.055f,.045f,.035f));var wool=Geometry.Material("Sheep wool",new Color(.78f,.76f,.66f),0,.08f);var fur=Geometry.Material("Deer fur",new Color(.32f,.18f,.085f),0,.1f);
            if(kind==0)
            {
                var clothes=Geometry.Material("Resident jacket "+(seed%4),new[]{new Color(.16f,.23f,.29f),new Color(.28f,.13f,.09f),new Color(.25f,.3f,.17f),new Color(.3f,.26f,.3f)}[seed%4]);var trousers=Geometry.Material("Resident trousers",new Color(.08f,.1f,.13f));var skin=Geometry.Material("Resident skin "+(seed%3),new[]{new Color(.62f,.41f,.28f),new Color(.36f,.23f,.16f),new Color(.78f,.57f,.43f)}[seed%3]);
                Geometry.Primitive(PrimitiveType.Capsule,"Jacket",root,new Vector3(0,1.13f,0),new Vector3(.41f,.33f,.25f),clothes);Geometry.Primitive(PrimitiveType.Capsule,"Neck",root,new Vector3(0,1.50f,0),new Vector3(.13f,.085f,.13f),skin);Geometry.Primitive(PrimitiveType.Sphere,"Head",root,new Vector3(0,1.65f,.01f),new Vector3(.24f,.3f,.24f),skin);Geometry.Primitive(PrimitiveType.Sphere,"Hair",root,new Vector3(0,1.73f,-.025f),new Vector3(.25f,.17f,.24f),dark);Geometry.Primitive(PrimitiveType.Sphere,"Nose",root,new Vector3(0,1.65f,.135f),new Vector3(.05f,.06f,.05f),skin);
                foreach(int side in new[]{-1,1})
                {
                    var leg=new GameObject("Walking leg").transform;leg.SetParent(root,false);leg.localPosition=new Vector3(side*.11f,.83f,0);Geometry.Primitive(PrimitiveType.Capsule,"Trouser leg",leg,new Vector3(0,-.34f,0),new Vector3(.15f,.34f,.17f),trousers);Geometry.Box("Shoe",leg,new Vector3(0,-.75f,.06f),new Vector3(.17f,.12f,.29f),dark);limbs.Add(leg);
                    var arm=new GameObject("Walking arm").transform;arm.SetParent(root,false);arm.localPosition=new Vector3(side*.25f,1.4f,0);Geometry.Primitive(PrimitiveType.Capsule,"Sleeve",arm,new Vector3(0,-.25f,0),new Vector3(.13f,.25f,.13f),clothes);Geometry.Primitive(PrimitiveType.Sphere,"Hand",arm,new Vector3(0,-.52f,0),Vector3.one*.11f,skin);limbs.Add(arm);
                }
            }
            else
            {
                bool deer=kind==2;float h=deer?.92f:.68f;var coat=deer?fur:wool;
                Geometry.Primitive(PrimitiveType.Capsule,"Body",root,new Vector3(0,h,0),new Vector3(deer?.43f:.54f,deer?.62f:.55f,deer?.48f:.6f),coat).transform.localRotation=Quaternion.Euler(90,0,0);
                if(!deer)for(int i=0;i<10;i++){float angle=i*2.4f;Geometry.Primitive(PrimitiveType.Sphere,"Wool fleece",root,new Vector3(Mathf.Sin(angle)*.2f,h+Mathf.Cos(angle)*.16f,(i/9f-.5f)*.8f),new Vector3(.32f,.32f,.32f),coat);}
                Geometry.Primitive(PrimitiveType.Capsule,"Neck",root,new Vector3(0,h+.22f,.43f),new Vector3(.2f,.34f,.23f),deer?fur:dark).transform.localRotation=Quaternion.Euler(22,0,0);Geometry.Primitive(PrimitiveType.Sphere,"Head",root,new Vector3(0,h+.48f,.57f),new Vector3(.2f,.24f,.4f),deer?fur:dark);
                foreach(int side in new[]{-1,1}){Geometry.Primitive(PrimitiveType.Sphere,"Ear",root,new Vector3(side*.15f,h+.6f,.5f),new Vector3(.16f,.07f,.09f),coat);Geometry.Primitive(PrimitiveType.Sphere,"Eye",root,new Vector3(side*.105f,h+.5f,.63f),Vector3.one*.025f,dark);}
                for(int i=0;i<4;i++){var leg=new GameObject("Walking animal leg").transform;leg.SetParent(root,false);leg.localPosition=new Vector3(i<2?-.17f:.17f,h-.12f,i%2==0?-.37f:.36f);float length=h-.1f;Geometry.Primitive(PrimitiveType.Capsule,"Leg",leg,new Vector3(0,-length*.5f,0),new Vector3(.08f,length*.5f,.09f),deer?fur:dark);limbs.Add(leg);}
                Geometry.Primitive(PrimitiveType.Capsule,"Tail",root,new Vector3(0,h+.1f,-.58f),new Vector3(.08f,.16f,.08f),coat).transform.localRotation=Quaternion.Euler(45,0,0);
            }
            a.Limbs=limbs.ToArray();return a;
        }
        float Ground(Vector3 point)
        {
            if(Physics.Raycast(point+Vector3.up*40,Vector3.down,out var hit,100,~(1<<8),QueryTriggerInteraction.Ignore))return hit.point.y;
            return Session.World.GeographicWorld?Session.World.GeographicWorld.MeshSurfaceHeight(point.x,point.z):Session.World.Route.TerrainHeight(point.x,point.z);
        }
        bool Clear(Vector3 point)
        {
            var region=Session.World.Route.Region;if(region==null)return true;if(region.IsWater(point.x,point.z))return false;
            foreach(var b in region.buildings)if(GeoRegion.Contains(b.points,new Vector2(point.x,point.z)))return false;return true;
        }
        void Place(Actor a)
        {
            var route=Session.World.Route;float progress=route.Closest(Session.Car.Body.position,out _,out _);var random=new System.Random(a.Seed*631+Mathf.RoundToInt(progress/200));Vector3 point=Vector3.zero;bool found=false;
            for(int attempt=0;attempt<18;attempt++)
            {
                float d=progress-140+(float)random.NextDouble()*340;float side=random.Next(2)==0?-1:1;
                float offset=route.Width(d)*.5f+(a.Kind==0?(Session.World.IsCircuit?18:2.4f):25+(float)random.NextDouble()*70);
                point=route.At(d,side*offset);
                if(Session.World.AutomaticWorld)
                {
                    // Route.At wraps around the original guided route. Sample the
                    // actual nearby road network instead as the player explores.
                    float angle=(float)random.NextDouble()*Mathf.PI*2;
                    var query=Session.Car.Body.position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(75+(float)random.NextDouble()*140);
                    var nearby=route.Region.Nearest(query,out var centre,out float separation);
                    if(nearby==null||nearby.tunnel||nearby.bridge||separation>60)continue;
                    route.Region.RoadPoint(nearby,route.Region.RoadProgress(nearby,centre),out var forward);
                    offset=nearby.width*.5f+(a.Kind==0?2.4f:15+(float)random.NextDouble()*45);
                    point=centre+Vector3.Cross(Vector3.up,forward).normalized*side*offset;
                    if(!Session.World.AutomaticWorld.Renderer.HasGeometryAt(point))continue;
                }
                if(Vector3.Distance(point,Session.Car.Body.position)<45||Session.PopulationVisible(point,a.Kind==0?2:1.6f))continue;
                var region=route.Region;if(region!=null){if(region.IsOnRoad(point,1.5f))continue;}
                if(region!=null&&a.Kind==1&&region.Land(point.x,point.z)==3)continue;if(region!=null&&a.Kind==2&&region.Land(point.x,point.z)!=2)continue;if(Clear(point)){found=true;break;}
            }
            if(!found){a.Retry=Time.time+8;a.Root.gameObject.SetActive(false);return;}point.y=Ground(point);a.Root.position=point;a.Home=point;a.Target=point;a.Wait=(float)random.NextDouble()*3;a.Root.gameObject.SetActive(Session.Profile.livingScenery);
        }
        bool travelDirectionBlocked(Vector3 point,Vector3 direction)=>direction.sqrMagnitude>.01f&&Physics.SphereCast(point+Vector3.up*.85f,.23f,direction.normalized,out _,1.3f,~0,QueryTriggerInteraction.Ignore);
        void Update()
        {
            using var timing=WorkTelemetry.Measure("frame.scenery",null,8);
            if(!Session||Session.Menu)return;bool enabled=Session.Profile.livingScenery;float dt=Time.deltaTime;
            foreach(var a in actors)
            {
                if(!enabled){if(a.Root.gameObject.activeSelf)a.Root.gameObject.SetActive(false);continue;}
                if(!a.Root.gameObject.activeSelf&&Time.time<a.Retry)continue;
                if(!a.Root.gameObject.activeSelf||((a.Root.position-Session.Car.Body.position).sqrMagnitude>330*330&&!Session.PopulationVisible(a.Root.position))){Place(a);continue;}
                if(Session.World.GeographicWorld&&!Session.World.GeographicWorld.HasSurface(a.Root.position))continue;
                var away=a.Root.position-Session.Car.Body.position;away.y=0;bool flee=away.magnitude<18&&Session.Car.AbsoluteSpeed>2;
                if(flee)a.Target=a.Root.position+away.normalized*5;
                else if((a.Target-a.Root.position).sqrMagnitude<.1f)
                {
                    a.Wait-=dt;if(a.Wait>0)continue;float angle=Time.time*.31f+a.Seed*2.4f;a.Target=a.Home+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(a.Kind==0?8:12);a.Wait=a.Kind==0?.3f:2;
                }
                var target=a.Target;target.y=a.Root.position.y;var delta=target-a.Root.position;var candidate=a.Root.position+delta.normalized*Mathf.Min(delta.magnitude,dt*(flee?3.7f:a.Kind==0?1.15f:.6f));
                var region=Session.World.Route.Region;if(region!=null){if(region.IsWater(candidate.x,candidate.z)||region.IsOnRoad(candidate,1.1f)){a.Target=a.Home;a.Wait=1;continue;}}
                if(travelDirectionBlocked(a.Root.position,delta))
                {a.Target=a.Root.position+Quaternion.AngleAxis(90,Vector3.up)*delta.normalized*4;a.Wait=.5f;continue;}
                if(delta.sqrMagnitude>.01f)a.Root.rotation=Quaternion.RotateTowards(a.Root.rotation,Quaternion.LookRotation(delta),dt*140);float travel=Vector3.Distance(candidate,a.Root.position);candidate.y=Ground(candidate);a.Root.position=candidate;a.Phase+=travel*(a.Kind==0?5:7);
                for(int i=0;i<a.Limbs.Length;i++)a.Limbs[i].localRotation=Quaternion.Euler(Mathf.Sin(a.Phase+(i==0||i==3?0:Mathf.PI))*24,0,0);
            }
        }
    }
}
