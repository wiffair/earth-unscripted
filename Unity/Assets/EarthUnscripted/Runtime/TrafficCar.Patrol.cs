using System;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class TrafficCar
    {
        GeoRoad patrolNext;int patrolNextDirection,patrolQueryRevision=-1;
        readonly Dictionary<(string,int),(GeoRoad road,int direction)> patrolChoices=new Dictionary<(string,int),(GeoRoad,int)>();
        readonly HashSet<(string,int)> patrolCoverageSeen=new HashSet<(string,int)>();
        internal void ResetPhysicalPatrolPlan(){networkRoad=null;patrolNext=null;networkStopped=false;patrolChoices.Clear();ResetControlStops();CancelTrafficCooperation();}
        GeoRoad NextPatrolRoad(GeoRegion region,GeoRoad from,int travel,out int nextTravel)
        {
            var key=(from.id,travel);if(patrolChoices.TryGetValue(key,out var chosen)){nextTravel=chosen.direction;return chosen.road;}
            nextTravel=travel;var next=region.Next(from,ref nextTravel,networkRandom);if(next==from)next=null;
            if(next!=null)
            {var end=region.RoadPoint(from,travel>0?from.length:0,out _);var begin=region.RoadPoint(next,nextTravel>0?0:next.length,out _);if(Vector3.Distance(end,begin)>1)next=null;}
            if(patrolChoices.Count>=64)return null;patrolChoices.Add(key,(next,nextTravel));return next;
        }
        bool PatrolLane(GeoRegion region,float distance,out Vector3 point)
        {
            point=default;float at=networkDistance+distance;var owner=networkRoad;int direction=Direction;patrolCoverageSeen.Clear();
            while(owner!=null)
            {
                if(!patrolCoverageSeen.Add((owner.id,direction))||patrolCoverageSeen.Count>32)return false;
                if(at<=owner.length){var centre=region.RoadPoint(owner,direction>0?at:owner.length-at,out var forward);point=region.DrivingLanePoint(owner,centre,forward*direction,out _);return true;}
                at-=owner.length;owner=NextPatrolRoad(region,owner,direction,out direction);
            }
            return false;
        }
        // Planning only: no body moves, synthetic physical progress, or kinematic courtesy.
        internal bool PlanPhysicalPatrol(VehicleController actual,float dt,out Vector3 target,out float desired)
        {
            target=actual.Body.position;desired=0;var region=Route?.Region;if(region==null)return false;
            int revision=Session&&Session.World.AutomaticWorld?Session.World.AutomaticWorld.QueryRevision:0;
            if(patrolQueryRevision!=revision){patrolQueryRevision=revision;if(networkRoad!=null){var old=networkRoad;networkRoad=Array.Find(region.roads,r=>r.id==old.id);}patrolNext=null;patrolChoices.Clear();}
            if(networkRoad==null)
            {
                networkRoad=region.ProjectRoadForVehicle(actual.Body.position,actual.RoadSupportHeight).Road;
                if(networkRoad==null)return false;float at=region.RoadProgress(networkRoad,actual.Body.position);region.RoadPoint(networkRoad,at,out var tangent);
                Direction=networkRoad.oneWay!=0?networkRoad.oneWay:Vector3.Dot(actual.transform.forward,tangent)>=0?1:-1;
            }
            float station=region.RoadProgress(networkRoad,actual.Body.position);networkDistance=Direction>0?station:networkRoad.length-station;Speed=actual.AbsoluteSpeed;
            patrolNext=NextPatrolRoad(region,networkRoad,Direction,out patrolNextDirection);float remaining=networkRoad.length-networkDistance;
            if(remaining<1.5f&&patrolNext!=null)
            {
                var end=region.RoadPoint(networkRoad,Direction>0?networkRoad.length:0,out var tangent);tangent*=Direction;
                if(Vector3.Dot(actual.Body.position-end,tangent)>-.5f)
                {patrolChoices.Remove((networkRoad.id,Direction));networkRoad=patrolNext;Direction=patrolNextDirection;ResetControlStops();station=region.RoadProgress(networkRoad,actual.Body.position);networkDistance=Direction>0?station:networkRoad.length-station;remaining=networkRoad.length-networkDistance;patrolNext=NextPatrolRoad(region,networkRoad,Direction,out patrolNextDirection);}
            }
            if(!PatrolLane(region,Mathf.Min(Mathf.Max(12,Speed*.8f+6),remaining+(patrolNext?.length??0)),out target))return false;
            float limit=Direction>0?networkRoad.NativeForwardLimitMps:networkRoad.NativeBackwardLimitMps;if(limit<=0)limit=networkRoad.limit/Core.DrivingMath.MphPerMetreSecond;
            desired=Mathf.Min(18,limit*.8f);if(remaining<20||networkDistance<20)desired=Mathf.Min(desired,7);
            if(patrolNext==null&&remaining<20)desired=Mathf.Min(desired,Mathf.Sqrt(Mathf.Max(0,remaining-2)*6));
            float speedCap=desired;
            void Observe(Vector3 position,Vector3 heading,Core.VehicleMetric metric)
            {var local=transform.InverseTransformPoint(position);float corridor=Vector3.Dot(transform.forward,heading)<-.65f?PassingClearance(heading,metric):2.6f;if(local.z>0&&local.z<100&&Mathf.Abs(local.x)<corridor&&Mathf.Abs(local.y)<2.5f)speedCap=Mathf.Min(speedCap,Mathf.Sqrt(Mathf.Max(0,local.z-8)*6));}
            Observe(Player.Body.position,Player.transform.forward,Core.VehicleDimensions.ForModel(Player.Visual?Player.Visual.Model:0));
            if(Fleet!=null)foreach(var peer in Fleet)if(peer&&peer!=this&&peer.gameObject.activeInHierarchy)Observe(peer.PhysicsPosition,peer.transform.forward,peer.Metrics);
            desired=JunctionSpeed(ControlSpeed(speedCap,dt),dt,false);
            var host=Session?Session.World.AutomaticWorld:null;
            float speed=Mathf.Max(Speed,desired),horizon=Mathf.Min(240,speed*speed/6+speed*1.2f+16),supported=0;
            for(float distance=0;distance<=horizon+8;distance+=8)
            {
                float probe=Mathf.Min(distance,horizon);
                if(!PatrolLane(region,probe,out var point)||(host?!host.Renderer.HasDrivingSurfaceAt(point):Session&&Session.World.GeographicWorld&&!Session.World.GeographicWorld.HasSurface(point)))
                {float available=Mathf.Max(0,supported-8);desired=Mathf.Min(desired,Mathf.Max(0,Mathf.Sqrt(9+6*available)-3));break;}
                supported=probe;if(probe>=horizon)break;
            }
            return true;
        }
    }
}
