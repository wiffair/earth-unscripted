using UnityEngine;

namespace EarthUnscripted
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed partial class TrafficCar : MonoBehaviour
    {
        [System.NonSerialized] public RoutePath Route;
        public TrafficCar[] Fleet;
        public VehicleController Player;
        public GameSession Session;
        public CarVisual Visual;
        public float Distance,Speed=12;
        public int Direction=1;
        public bool Police;
        public PoliceDriver ManagedPolice;
        public float Lane;
        Core.VehicleMetric Metrics=>Core.VehicleDimensions.ForModel(Visual?Visual.Model:0);
        Vector3 Rest(Vector3 point)=>VehicleGeometry.RoadRestPosition(point,Visual?Visual.Model:0);
        Rigidbody body;
        public Vector3 PhysicsPosition=>body.position;
        float spin,slowUntil,networkDistance,nextPopulationCheck;
        GeoRoad networkRoad;bool networkStopped;float nextNetworkRetry;
        public GeoRoad RoadForDiagnostics => networkRoad;
        public bool StoppedForDiagnostics => networkStopped;
        readonly System.Random networkRandom=new System.Random(241);
        void Awake(){body=GetComponent<Rigidbody>();body.isKinematic=true;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            BuildTrafficContactProfile();
            VehiclePopulation.VehiclePopulationHooks.ObserveAwake(this);
        }
        void FixedUpdate()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            if(Route==null||Player==null)return;
            float dt=Time.fixedDeltaTime;
            if(!Police&&!ManagedPolice&&Route.Region!=null&&Route.Region.AutomaticWorld&&Session&&Time.time>=nextPopulationCheck)
            {
                nextPopulationCheck=Time.time+1+(GetEntityId().GetHashCode()&7)*.07f;
                float populationGap=Vector3.Distance(body.position,Player.Body.position);
                bool stranded=!Session.World.AutomaticWorld.Renderer.HasGeometryAt(body.position);
                if((populationGap>400||stranded&&populationGap>160)&&!Session.PopulationVisible(body.position,4))Session.TryRecycleTraffic(this);
            }
            if(TickCollisionDynamics(dt))return;
            if(Police){if(!ManagedPolice)UpdatePursuit(dt);return;}
            if(Route.Region!=null){UpdateNetworkTraffic(dt);return;}
            float target=Route.Limit(Distance)/2.23694f*.88f;
            float gap=150;
            if(Fleet!=null)foreach(var other in Fleet)
            {
                if(!other||other==this||other.Direction!=Direction||!other.gameObject.activeSelf)continue;
                float along=Core.DrivingMath.Wrap((other.Distance-Distance)*Direction,Route.Length);
                if(along<gap&&Mathf.Abs(other.Lane-Lane)<2.8f)gap=along;
            }
            float playerAt=Session?Session.Progress:Route.Closest(Player.Body.position,out _,out _);
            float playerLane=Session?Session.Lateral:0;
            float playerAlong=Core.DrivingMath.Wrap((playerAt-Distance)*Direction,Route.Length);
            if(playerAlong<150&&Mathf.Abs(playerLane-Lane)<2.8f)gap=Mathf.Min(gap,playerAlong);
            var delta=Player.transform.position-transform.position;
            float ahead=Vector3.Dot(delta,transform.forward);float side=Vector3.Dot(delta,transform.right);
            if(ahead>0&&Mathf.Abs(side)<2.5f)gap=Mathf.Min(gap,ahead);
            target=Mathf.Min(target,Mathf.Max(0,(gap-7)/1.4f));
            if(Time.time<slowUntil)target=0;
            Speed=Mathf.MoveTowards(Speed,target,(target<Speed?7:2.8f)*dt);
            Distance=Core.DrivingMath.Wrap(Distance+Direction*Mathf.Min(Speed*dt,Mathf.Max(0,gap-7)),Route.Length);
            var position=Route.At(Distance,Lane);
            body.MovePosition(Rest(position));body.MoveRotation(Quaternion.LookRotation(Route.Forward(Distance)*Direction,Vector3.up));
            spin+=Direction*Speed*dt/Metrics.Radius*Mathf.Rad2Deg;
            if(Visual){Visual.Brake(Speed>target+.5f||gap<20);foreach(var w in Visual.Wheels)w.localRotation=Quaternion.Euler(spin,0,0);}
        }
        void UpdateNetworkTraffic(float dt)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            var region=Route.Region;
            if(networkRoad==null){networkRoad=region.AutomaticWorld?region.ProjectRoadForVehicle(body.position,body.position.y-Rest(Vector3.zero).y).Road:region.Nearest(body.position,out _,out _);networkDistance=0;if(networkRoad==null)return;float at=region.RoadProgress(networkRoad,body.position);if(networkRoad.oneWay!=0)Direction=networkRoad.oneWay;else if(region.AutomaticWorld){region.RoadPoint(networkRoad,at,out var sourceForward);Direction=Vector3.Dot(body.rotation*Vector3.forward,sourceForward)>=0?1:-1;}networkDistance=Direction>0?at:networkRoad.length-at;}
            if(region.AutomaticWorld && networkStopped && Time.time>=nextNetworkRetry)
            {
                nextNetworkRetry=Time.time+1;int nextDirection=Direction;
                var next=region.Next(networkRoad,ref nextDirection,networkRandom);
                if(next!=null && next.id!=networkRoad.id)
                {networkRoad=next;Direction=nextDirection;networkDistance=0;networkStopped=false;ResetControlStops();}
            }
            float limitMps=Direction>0?networkRoad.NativeForwardLimitMps:networkRoad.NativeBackwardLimitMps;
            if(limitMps<=0)limitMps=networkRoad.limit/2.23694f;
            float desired=networkStopped?0:Mathf.Min(18,limitMps*.75f);
            if(networkRoad.length-networkDistance<20||networkDistance<20)desired=Mathf.Min(desired,7);
            var delta=Player.Body.position-body.position;
            float followingAdvance=float.PositiveInfinity;
            if(region.AutomaticWorld)
            {
                void Follow(Vector3 other)
                {
                    var local=transform.InverseTransformPoint(other);
                    if(local.z>0 && local.z<100 && Mathf.Abs(local.x)<2.6f && Mathf.Abs(local.y)<2)
                    {
                        desired=Mathf.Min(desired,Mathf.Max(0,(local.z-8)/1.8f));
                        // Kinematic route advance must never overrun the observed
                        // bumper gap even during a long physics catch-up frame.
                        followingAdvance=Mathf.Min(followingAdvance,Mathf.Max(0,local.z-7));
                    }
                }
                Follow(Player.Body.position);
                if(Fleet!=null)foreach(var other in Fleet)if(other && other!=this&&other.gameObject.activeInHierarchy)Follow(other.PhysicsPosition);
            }
            else if(Vector3.Dot(delta,transform.forward)>0&&Vector3.Dot(delta,transform.forward)<10&&Mathf.Abs(Vector3.Dot(delta,transform.right))<2.8f)desired=0;
            if(region.AutomaticWorld){desired=ControlSpeed(desired,dt);desired=JunctionSpeed(desired);}
            Speed=Mathf.MoveTowards(Speed,Time.time<slowUntil?0:desired,(desired<Speed?6:3)*dt);
            float advance=Mathf.Min(Mathf.Min(Speed*dt,controlAdvance),followingAdvance);
            if(junctionYielding)advance=Mathf.Min(advance,Mathf.Max(0,networkRoad.length-networkDistance-4));
            networkDistance+=advance;
            if(networkDistance>networkRoad.length){networkDistance-=networkRoad.length;var next=region.Next(networkRoad,ref Direction,networkRandom);if(next!=null){networkRoad=next;ResetControlStops();}else{networkStopped=true;networkDistance=networkRoad.length;}}
            float d=Direction>0?networkDistance:networkRoad.length-networkDistance;var position=region.RoadPoint(networkRoad,d,out var forward);forward*=Direction;
            // Interpolate lane offset to zero at the junction, so turns meet cleanly.
            float lane=Mathf.Min((networkRoad.NativeTravelWidth>0?networkRoad.NativeTravelWidth:networkRoad.width)*.24f,2.4f)*(region.AutomaticWorld?1:Mathf.Clamp01(Mathf.Min(networkDistance,networkRoad.length-networkDistance)/9));
            position+=Vector3.Cross(Vector3.up,forward).normalized*lane*region.drivingSide;
            if(!networkRoad.tunnel)position.y=region.RoadSurfaceHeightForRoad(networkRoad,position.x,position.z);
            body.MovePosition(Rest(position));body.MoveRotation(Quaternion.RotateTowards(body.rotation,Quaternion.LookRotation(forward,Vector3.up),100*dt));
            spin+=Speed*dt/Metrics.Radius*Mathf.Rad2Deg;if(Visual)foreach(var w in Visual.Wheels)w.localRotation=Quaternion.Euler(spin,0,0);
        }
        internal void RecycleAt(Vector3 point,Vector3 forward)
        {
            using var appearanceWrite=VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
            VehiclePopulation.VehiclePopulationHooks.PoseWillReset(this);
            if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            body.isKinematic=true;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            body.position=Rest(point);body.rotation=Quaternion.LookRotation(forward,Vector3.up);
            networkRoad=null;networkStopped=false;collisionDynamics=false;struck=false;Speed=4;slowUntil=0;ResetControlStops();
        }
        bool junctionYielding;
        float JunctionSpeed(float desired)
        {
            junctionYielding=false;float remaining=networkRoad.length-networkDistance;
            if(remaining>60)return desired;
            var region=Route.Region;string node=Direction>0?networkRoad.end:networkRoad.start;
            var connected=region.RoadsAtJunction(node);
            if(connected.Count<3&&!System.Linq.Enumerable.Any(connected,r=>r.roundabout&&!networkRoad.roundabout))return desired;
            var junction=Direction>0?networkRoad.points[networkRoad.points.Length-1]:networkRoad.points[0];
            float ownETA=Mathf.Max(0,remaining-4)/Mathf.Max(Speed,3);
            bool Conflict(Vector3 position,Vector3 velocity,GeoRoad road,bool tieBreak)
            {
                if(road==null||road.id==networkRoad.id)return false;
                bool joins=road.start==node||road.end==node;
                if(!joins&&road.roundabout&&!string.IsNullOrEmpty(road.NativeSourceWay))foreach(var arm in connected)if(arm.roundabout&&arm.NativeSourceWay==road.NativeSourceWay){joins=true;break;}
                if(!joins)return false;
                var delta=junction-position;float distance=new Vector2(delta.x,delta.z).magnitude;
                if(distance>80)return false;
                bool approaching=Vector3.Dot(velocity,delta)>0||distance<8;
                float speed=new Vector2(velocity.x,velocity.z).magnitude;
                float eta=distance/Mathf.Max(speed,1);
                bool right=Vector3.Dot(position-body.position,transform.right)>1;
                if(speed<.5f&&distance>8)right=tieBreak;
                return TrafficJunctionRules.MustYield(networkRoad,road,ownETA,eta,distance,Mathf.Abs(position.y-body.position.y),approaching,right);
            }
            // GameSession already resolves the player's physical road once per frame.
            // Repeating native road-surface projection per car/per physics tick
            // causes quadratic expensive surface evaluations near busy junctions.
            if(Session&&Vector2.SqrMagnitude(new Vector2(Player.Body.position.x-junction.x,Player.Body.position.z-junction.z))<80*80)
                junctionYielding=Conflict(Player.Body.position,Player.Body.linearVelocity,Session.CurrentRoad,true);
            if(!junctionYielding&&Fleet!=null)foreach(var other in Fleet)
            {
                if(!other||other==this||!other.gameObject.activeInHierarchy)continue;
                Vector3 velocity=other.ManagedPolice?other.ManagedPolice.Car.Body.linearVelocity:other.transform.forward*other.Speed;
                if(Conflict(other.PhysicsPosition,velocity,other.networkRoad,System.Array.IndexOf(Fleet,other)<System.Array.IndexOf(Fleet,this))){junctionYielding=true;break;}
            }
            if(junctionYielding)return Mathf.Min(desired,Mathf.Sqrt(Mathf.Max(0,remaining-4)*8));
            return desired;
        }
        public void BeginPursuit()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            if(ManagedPolice){ManagedPolice.BeginPursuit();return;}
            float target=Route.Closest(Player.Body.position,out float lateral,out _);
            float travel=Vector3.Dot(Player.Body.linearVelocity,Route.Forward(target));
            if(Mathf.Abs(travel)<1)travel=Vector3.Dot(Player.Body.rotation*Vector3.forward,Route.Forward(target));
            Direction=travel>=0?1:-1;Distance=Core.DrivingMath.Wrap(target-Direction*50,Route.Length);
            Lane=Mathf.Clamp(lateral,-Route.Width(target)*.5f+.8f,Route.Width(target)*.5f-.8f);
            Speed=Mathf.Min(Player.AbsoluteSpeed+10,30);slowUntil=0;
            VehiclePopulation.VehiclePopulationHooks.PoseWillReset(this);
            body.position=Rest(Route.At(Distance,Lane));body.rotation=Quaternion.LookRotation(Route.Forward(Distance)*Direction,Vector3.up);
        }
        void UpdatePursuit(float dt)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            float target=Route.Closest(Player.Body.position,out float lateral,out _);
            float delta=Core.DrivingMath.SignedRouteDelta(Distance,target,Route.Length);
            float along=delta*Direction;
            if(along < -2)
            {
                Speed=Mathf.MoveTowards(Speed,0,14*dt);
                if(Speed<.3f){Direction=-Direction;along=-along;}
            }
            float laneLimit=Mathf.Max(1,Route.Width(Distance)*.5f-.8f);
            Lane=Mathf.MoveTowards(Lane,Mathf.Clamp(lateral,-laneLimit,laneLimit),3.8f*dt);
            var facing=Quaternion.LookRotation(Route.Forward(Distance)*Direction,Vector3.up);
            bool turning=Quaternion.Angle(body.rotation,facing)>35;
            float playerSpeed=Mathf.Max(0,Vector3.Dot(Player.Body.linearVelocity,Route.Forward(target))*Direction);
            float desired=along<0||turning?0:Mathf.Min(42,playerSpeed+Mathf.Sqrt(Mathf.Max(0,(along-5.5f)*18)));
            if(Time.time<slowUntil)desired=0;
            Speed=Mathf.MoveTowards(Speed,desired,(desired<Speed?14:7)*dt);
            // Stand off behind the target: a stopped player cannot be overtaken by the patrol.
            float advance=turning?0:Mathf.Min(Speed*dt,Mathf.Max(0,along-5.5f));
            Distance=Core.DrivingMath.Wrap(Distance+Direction*advance,Route.Length);
            body.MovePosition(Rest(Route.At(Distance,Lane)));body.MoveRotation(Quaternion.RotateTowards(body.rotation,facing,160*dt));
            spin+=Direction*advance/Metrics.Radius*Mathf.Rad2Deg;
            if(Visual){Visual.Brake(desired<Speed||along<12);foreach(var w in Visual.Wheels)w.localRotation=Quaternion.Euler(spin,0,0);}
        }
        public void Hit(Vector3 point,float impact){if(ManagedPolice)return;struck=true;CollisionDamage=Mathf.Clamp(CollisionDamage+impact,0,100);slowUntil=Time.time+4;Visual.Dent(point,impact);if(!collisionDynamics)Speed*=.7f;}
    }
}
