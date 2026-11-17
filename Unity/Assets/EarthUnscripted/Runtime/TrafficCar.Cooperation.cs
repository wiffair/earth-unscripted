using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class TrafficCar
    {
        struct RecentRoad {public GeoRoad road;public int direction;}
        readonly List<RecentRoad> recentRoads=new List<RecentRoad>(4);
        readonly Collider[] cooperationOverlaps=new Collider[32];
        TrafficCar blockedPeer,cooperationPeer;
        float blockedAge,junctionWaitAge,cooperationCooldown,cooperationAge,retreatOffset,retreatTarget;
        int cooperationPhase; // 1 reverse, 2 hold, 3 retrace forward
        bool cooperationDonor,cooperationCourtesy;
        GeoRoad cooperationPeerRoad,laneRoad;
        Vector3 cooperationJunction;
        float laneOffset,cooperationMoveSpeed,retreatStartLane;bool laneAdjusted;
        public int CooperativeYields {get;private set;}
        public int CooperativeCourtesyYields {get;private set;}
        public int CooperativeLateralAdjustments {get;private set;}
        public float CooperativeReverseMetres {get;private set;}
        public bool ReversingForTraffic=>cooperationDonor&&cooperationPhase==1;
        public Vector3 TrafficVelocity=>body&&!body.isKinematic?body.linearVelocity:transform.forward*(ReversingForTraffic?-Speed:Speed);

        void RememberTrafficRoad(GeoRoad road,int direction,GeoRoad next)
        {
            // Retreat never guesses an untravelled fork or crosses a junction.
            string node=direction>0?road.end:road.start;
            if(next==null||Route.Region.RoadsAtJunction(node).Count!=2){recentRoads.Clear();return;}
            if(recentRoads.Count==4)recentRoads.RemoveAt(0);
            recentRoads.Add(new RecentRoad{road=road,direction=direction});
        }
        bool ReversibleRoad(GeoRoad road)
        {
            if(road==null||road.oneWay!=0||road.bridge||road.tunnel||road.roundabout)return false;
            // Numeric source data attaches static speed signs to most roads.
            // They do not reserve a movement; all other/unknown controls do.
            if(road.NativeControls!=null)foreach(var control in road.NativeControls)
                if(control==null||control.kind!="speed")return false;
            return true;
        }
        float RoadTravelWidth(GeoRoad road)=>road.NativeTravelWidth>0?road.NativeTravelWidth:road.width;
        float LaneFor(GeoRoad road,bool outward=false)
        {
            float width=RoadTravelWidth(road),edge=Mathf.Max(0,width*.5f-Metrics.BodyWidth*.5f-.20f);
            return Mathf.Min(edge,Mathf.Min(width*.24f,2.4f)+(outward?.40f:0));
        }
        bool RetreatPose(float offset,bool outward,out Vector3 position,out Quaternion rotation,out GeoRoad road)
        {
            road=networkRoad;GeoRoad younger=null;int direction=Direction;float progress=networkDistance-offset;
            for(int i=recentRoads.Count-1;progress<0&&i>=0;i--)
            {younger=road;road=recentRoads[i].road;direction=recentRoads[i].direction;if(!ReversibleRoad(road)){position=default;rotation=default;return false;}progress+=road.length;}
            position=default;rotation=default;
            if(progress<0||!ReversibleRoad(road))return false;
            float station=direction>0?progress:road.length-progress;
            var point=Route.Region.RoadPoint(road,station,out var forward);forward*=direction;
            if(Vector3.Angle(transform.forward,forward)>35)return false;
            float lane=LaneFor(road,outward);
            if(younger!=null)lane=Mathf.Min(lane,Mathf.Lerp(LaneFor(younger,outward),lane,Mathf.Clamp01((road.length-progress-Metrics.BodyLength*.5f-.35f)/8)));
            float initialLane=retreatStartLane;
            if(cooperationPhase==0)
            {
                var origin=Route.Region.RoadPoint(networkRoad,Direction>0?networkDistance:networkRoad.length-networkDistance,out var originalForward);
                initialLane=Mathf.Abs(Vector3.Dot(body.position-origin,Vector3.Cross(Vector3.up,originalForward).normalized));
            }
            lane=Mathf.Min(lane,Mathf.Lerp(initialLane,lane,Mathf.Clamp01(offset/4)));
            point+=Vector3.Cross(Vector3.up,forward).normalized*lane*Route.Region.drivingSide;
            point.y=Route.Region.RoadSurfaceHeightForRoad(road,point.x,point.z);
            position=Rest(point);rotation=Quaternion.LookRotation(forward,Vector3.up);return true;
        }
        bool ManeuverClear(Vector3 position,Quaternion rotation)
        {
            var box=GetComponent<BoxCollider>();if(!box)return false;
            var ahead=rotation*Vector3.forward;var right=rotation*Vector3.right;
            bool Occupied(Vector3 otherPosition,Vector3 otherForward,Core.VehicleMetric other)
            {
                var delta=otherPosition-position;if(Mathf.Abs(delta.y)>2||Mathf.Abs(Vector3.Dot(delta,ahead))>=6.5f)return false;
                float clearance=Metrics.BodyWidth*.5f+Mathf.Abs(Vector3.Dot(Vector3.Cross(Vector3.up,otherForward).normalized,right))*other.BodyWidth*.5f+Mathf.Abs(Vector3.Dot(otherForward,right))*other.BodyLength*.5f+.2f;
                return Mathf.Abs(Vector3.Dot(delta,right))<clearance;
            }
            if(Player&&Occupied(Player.Body.position,Player.transform.forward,Core.VehicleDimensions.ForModel(Player.Visual?Player.Visual.Model:0)))return false;
            if(Fleet!=null)foreach(var other in Fleet)
                if(other&&other!=this&&other.gameObject.activeInHierarchy&&Occupied(other.PhysicsPosition,other.transform.forward,other.Metrics))return false;
            var extent=box.size*.5f+new Vector3(.14f,.025f,.20f);
            var centre=position+rotation*box.center;
            int count=Physics.OverlapBoxNonAlloc(centre,extent,cooperationOverlaps,rotation,~(1<<VehicleObstacleWheels.SuspensionSupportLayer),QueryTriggerInteraction.Ignore);
            if(count==cooperationOverlaps.Length)return false;
            for(int i=0;i<count;i++)
            {
                var hit=cooperationOverlaps[i];
                if(hit&&hit.attachedRigidbody!=body&&!hit.transform.IsChildOf(transform))return false;
            }
            // Real support at all four body corners. A numeric road alone does
            // not grant permission to reverse over an unpublished gap or water.
            for(int cornerIndex=0;cornerIndex<4;cornerIndex++)
            {
                float x=cornerIndex<2?-1:1,z=cornerIndex%2==0?-1:1;
                var corner=position+rotation*new Vector3(x*box.size.x*.48f,0,z*box.size.z*.48f);
                var origin=corner+Vector3.up*.9f;
                if(!Physics.Raycast(origin,Vector3.down,out var hit,1.4f,1<<VehicleObstacleWheels.SuspensionSupportLayer,QueryTriggerInteraction.Ignore)||Vector3.Dot(hit.normal,Vector3.up)<.75f||Mathf.Abs(hit.point.y-(position.y-Rest(Vector3.zero).y))>.35f)return false;
            }
            return true;
        }
        bool ClearRetreat(float target,bool outward)
        {
            if(target<0||target>35||!ReversibleRoad(networkRoad))return false;
            for(float offset=0;offset<=target+.01f;offset+=1)
                if(!RetreatPose(offset,outward,out var point,out var rotation,out _)||!ManeuverClear(point,rotation))return false;
            return RetreatPose(target,outward,out var end,out var facing,out _)&&ManeuverClear(end,facing);
        }
        bool FindPassingPlace(TrafficCar other,out float target)
        {
            target=0;if(!ReversibleRoad(networkRoad))return false;
            for(float offset=3;offset<=35;offset+=1)
            {
                if(!RetreatPose(offset,true,out var point,out _,out var road))break;
                float width=RoadTravelWidth(road);
                if(width<Metrics.BodyWidth+other.Metrics.BodyWidth+.8f)continue;
                // Keep the whole held vehicle clear of the constriction mouth.
                float ownProgress=Route.Region.RoadProgress(road,point);
                if(road!=networkRoad&&Mathf.Min(ownProgress,road.length-ownProgress)<Metrics.BodyLength)continue;
                var centre=Route.Region.RoadPoint(road,ownProgress,out var forward);
                float heldLane=Mathf.Abs(Vector3.Dot(point-centre,Vector3.Cross(Vector3.up,forward).normalized));
                // The approaching car must clear the held body before it has
                // itself reached the wider section; otherwise both stop again.
                if(heldLane+other.LaneFor(other.networkRoad)<PassingClearance(other.transform.forward,other.Metrics)+.10f)continue;
                if(ClearRetreat(offset,true)){target=offset;return true;}
            }
            return false;
        }
        int PairOrder(TrafficCar other)
        {
            if(Fleet!=null){int a=System.Array.IndexOf(Fleet,this),b=System.Array.IndexOf(Fleet,other);if(a>=0&&b>=0&&a!=b)return a.CompareTo(b);}
            return GetEntityId().GetHashCode().CompareTo(other.GetEntityId().GetHashCode());
        }
        void StartTrafficAgreement(TrafficCar other,float reverse,bool courtesy,Vector3 junction)
        {
            var origin=Route.Region.RoadPoint(networkRoad,Direction>0?networkDistance:networkRoad.length-networkDistance,out var forward);
            retreatStartLane=Mathf.Abs(Vector3.Dot(body.position-origin,Vector3.Cross(Vector3.up,forward).normalized));
            cooperationPeer=other;cooperationDonor=true;cooperationCourtesy=courtesy;cooperationPeerRoad=other.networkRoad;
            cooperationPhase=reverse>.01f?1:2;retreatOffset=0;retreatTarget=reverse;cooperationAge=cooperationMoveSpeed=0;cooperationJunction=junction;
            other.cooperationPeer=this;other.cooperationDonor=false;other.cooperationCourtesy=courtesy;other.cooperationPhase=0;other.cooperationAge=0;
            CooperativeYields++;if(courtesy)CooperativeCourtesyYields++;
        }
        void ReleaseTrafficPeer()
        {
            var other=cooperationPeer;cooperationPeer=null;
            if(other&&other.cooperationPeer==this){other.cooperationPeer=null;other.cooperationPhase=other.cooperationDonor&&other.retreatOffset>.001f?3:0;other.cooperationCooldown=3;}
            cooperationCooldown=3;blockedAge=0;
        }
        void CancelTrafficCooperation()
        {
            ReleaseTrafficPeer();cooperationPhase=0;retreatOffset=retreatTarget=0;cooperationDonor=false;blockedPeer=null;
            recentRoads.Clear();laneRoad=null;
        }
        bool TickTrafficAgreement(float dt)
        {
            cooperationCooldown=Mathf.Max(0,cooperationCooldown-dt);
            if(cooperationPhase==0&&!cooperationPeer)return false;
            cooperationAge+=dt;
            if(!cooperationDonor)
            {
                if(!cooperationPeer||!cooperationPeer.gameObject.activeInHierarchy){cooperationPeer=null;return false;}
                if(cooperationPeer.cooperationPhase==1){Speed=0;return true;}
                return false;
            }
            if(cooperationPhase!=3)
            {
                bool passed=!cooperationPeer||!cooperationPeer.gameObject.activeInHierarchy;
                if(!passed&&cooperationCourtesy)
                    passed=cooperationPeer.networkRoad!=cooperationPeerRoad&&Vector2.Distance(new Vector2(cooperationPeer.PhysicsPosition.x,cooperationPeer.PhysicsPosition.z),new Vector2(cooperationJunction.x,cooperationJunction.z))>12;
                else if(!passed)passed=Vector3.Dot(cooperationPeer.PhysicsPosition-body.position,transform.forward)<-8;
                if(passed||cooperationAge>20){ReleaseTrafficPeer();cooperationPhase=3;}
            }
            if(cooperationPhase==2){Speed=0;return true;}
            cooperationMoveSpeed=Mathf.MoveTowards(cooperationMoveSpeed,1.8f,1.5f*dt);
            if(cooperationPhase==3&&retreatOffset<.10f&&retreatOffset<=networkDistance)
            {
                // End at the actual supported cursor when a newly queued car
                // prevents the last centimetres of retracing; never snap poses.
                networkDistance-=retreatOffset;retreatOffset=0;cooperationPhase=0;cooperationDonor=false;laneRoad=null;Speed=0;return true;
            }
            float next=cooperationPhase==1?Mathf.Min(retreatTarget,retreatOffset+cooperationMoveSpeed*dt):Mathf.Max(0,retreatOffset-cooperationMoveSpeed*dt);
            if(!RetreatPose(next,true,out var position,out var rotation,out _)||!ManeuverClear(position,rotation)){Speed=0;return true;}
            float moved=Mathf.Abs(next-retreatOffset);Speed=moved/dt;
            if(cooperationPhase==1)CooperativeReverseMetres+=moved;
            spin+=(cooperationPhase==1?-1:1)*moved/Metrics.Radius*Mathf.Rad2Deg;
            retreatOffset=next;body.MovePosition(position);body.MoveRotation(Quaternion.RotateTowards(body.rotation,rotation,70*dt));
            if(Visual){Visual.Brake(true);foreach(var wheel in Visual.Wheels)wheel.localRotation=Quaternion.Euler(spin,0,0);}
            if(cooperationPhase==1&&retreatOffset>=retreatTarget-.001f){cooperationPhase=2;Speed=0;}
            if(cooperationPhase==3&&retreatOffset<=.001f){cooperationPhase=0;cooperationDonor=false;laneRoad=null;Speed=0;}
            return true;
        }
        void ObserveTrafficBlock(TrafficCar peer,float dt)
        {
            if(peer!=blockedPeer){blockedPeer=peer;blockedAge=0;}
            blockedAge=peer&&Speed<.8f?blockedAge+dt:0;
            if(!peer||blockedAge<1.5f||cooperationCooldown>0||cooperationPeer||peer.cooperationPeer||peer.Speed>.8f||PairOrder(peer)>0)return;
            cooperationCooldown=peer.cooperationCooldown=1;
            bool own=FindPassingPlace(peer,out float ownRetreat),theirs=peer.FindPassingPlace(this,out float theirRetreat);
            if(!own&&!theirs)return;
            // The older waiter keeps priority when both can retreat; a rear
            // queue or absent bay makes the physically usable side decisive.
            bool yield=own&&(!theirs||blockedAge<=peer.blockedAge+2);
            if(yield)StartTrafficAgreement(peer,ownRetreat,false,Vector3.zero);
            else peer.StartTrafficAgreement(this,theirRetreat,false,Vector3.zero);
        }
        float TrafficLaneOffset(Vector3 centre,Vector3 forward,float dt)
        {
            if(!Route.Region.AutomaticWorld)return Mathf.Min(RoadTravelWidth(networkRoad)*.24f,2.4f)*Mathf.Clamp01(Mathf.Min(networkDistance,networkRoad.length-networkDistance)/9);
            float normal=LaneFor(networkRoad),target=normal;
            if(laneRoad!=networkRoad){laneRoad=networkRoad;laneOffset=normal;laneAdjusted=false;}
            if(!networkRoad.bridge&&!networkRoad.tunnel&&Fleet!=null)foreach(var other in Fleet)
            {
                if(!other||other==this||!other.gameObject.activeInHierarchy||Vector3.Dot(forward,other.transform.forward)>-.65f)continue;
                var delta=other.PhysicsPosition-body.position;float along=Vector3.Dot(delta,forward);
                if(along>0&&along<35&&Mathf.Abs(delta.y)<2&&Mathf.Abs(Vector3.Dot(delta,Vector3.Cross(Vector3.up,forward)))<4){target=LaneFor(networkRoad,true);break;}
            }
            float next=Mathf.MoveTowards(laneOffset,target,.45f*dt);
            if(Mathf.Abs(next-laneOffset)>.0001f)
            {
                var point=centre+Vector3.Cross(Vector3.up,forward).normalized*next*Route.Region.drivingSide;
                point.y=Route.Region.RoadSurfaceHeightForRoad(networkRoad,point.x,point.z);
                if(ManeuverClear(Rest(point),Quaternion.LookRotation(forward)))
                {if(!laneAdjusted&&target>normal+.05f){CooperativeLateralAdjustments++;laneAdjusted=true;}laneOffset=next;}
            }
            return laneOffset;
        }
        bool RedControlAhead(TrafficCar car)
        {
            if(car.networkRoad?.NativeControls==null)return false;
            foreach(var control in car.networkRoad.NativeControls)
                if(control.kind=="traffic_signals"&&Vector3.Distance(control.Position,car.PhysicsPosition)<40&&Vector3.Dot(control.Forward,car.transform.forward)>.4f&&NewbridgePrototype.TrafficControlPhase.State(control,Time.time,Route.Region.countryCode)!=2)return true;
            return false;
        }
        bool YieldingTo(TrafficCar peer)=>cooperationDonor&&cooperationCourtesy&&cooperationPhase==2&&cooperationPeer==peer&&Speed<.2f;
        bool TryJunctionCourtesy(Vector3 junction,float remaining,float dt)
        {
            junctionWaitAge=Speed<.5f?junctionWaitAge+dt:0;
            if(cooperationPeer||cooperationCooldown>0||networkRoad.roundabout||!ReversibleRoad(networkRoad)||Speed>.5f||remaining>35)return false;
            TrafficCar candidate=null;
            if(Fleet!=null)foreach(var other in Fleet)
            {
                if(!other||other==this||!other.gameObject.activeInHierarchy||other.cooperationPeer||other.cooperationCooldown>0||other.networkRoad==null||other.networkRoad==networkRoad||other.Speed>.5f||other.junctionWaitAge<2||other.networkRoad.roundabout||TrafficJunctionRules.Priority(networkRoad)<=TrafficJunctionRules.Priority(other.networkRoad)||RedControlAhead(other))continue;
                var road=other.networkRoad;string node=other.Direction>0?road.end:road.start;
                string ownNode=Direction>0?networkRoad.end:networkRoad.start;
                if(node!=ownNode||road.length-other.networkDistance>12)continue;
                uint decision=unchecked((uint)(GetEntityId().GetHashCode()*397^other.GetEntityId().GetHashCode()));
                if((decision&3)!=0&&other.junctionWaitAge<8)continue;
                if(candidate==null||other.junctionWaitAge>candidate.junctionWaitAge)candidate=other;
            }
            if(!candidate)return false;
            // Never invite a minor arm across another moving stream or player.
            if(Vector3.Distance(Player.Body.position,junction)<40)return false;
            if(Fleet!=null)foreach(var other in Fleet)
                if(other&&other!=this&&other!=candidate&&other.gameObject.activeInHierarchy&&other.Speed>1&&Vector3.Distance(other.PhysicsPosition,junction)<35&&Vector3.Dot(other.transform.forward,transform.forward)<.5f)return false;
            float reverse=Mathf.Max(0,9-remaining);
            if(!ClearRetreat(reverse,true))return false;
            StartTrafficAgreement(candidate,reverse,true,junction);return true;
        }
    }
}
