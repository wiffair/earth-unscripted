using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class AutomaticWorldHost
    {
        readonly List<Vector3> drivingCorridor=new List<Vector3>(256);
        readonly List<bool> drivingCorridorRoadRequired=new List<bool>(256);
        float nextCoverageAt;
        public static float PreparationHorizon(float speed,float latency)=>Mathf.Clamp(speed*(Mathf.Clamp(latency,3,20)+6)+speed*speed/5+96,320,1800);
        public static float CoverageSpeed(float supportedMetres)=>Mathf.Sqrt(Mathf.Max(0,2*2.5f*(supportedMetres-24)));
        void UpdateDrivingCoverage()
        {
            if(Relocating){Player.StreamingSpeedLimit=0;Player.StreamingBrake=true;return;}
            if(Time.unscaledTime<nextCoverageAt)return;nextCoverageAt=Time.unscaledTime+.1f;
            float speed=Player.AbsoluteSpeed,horizon=PreparationHorizon(speed,Renderer.RecentPreparationSeconds);
            var velocity=Vector3.ProjectOnPlane(Player.Body.linearVelocity,Vector3.up);
            var position=Player.Body.position;
            float throttle=Player.NetworkDriven?Player.NetworkThrottle:Player.Input?Player.Input.Throttle:0;
            float brake=Player.NetworkDriven?Player.NetworkBrake:Player.Input?Player.Input.Brake:0;
            var heading=CoverageTravelHeading(velocity,Player.transform.forward,throttle,brake,Player.Reverse);
            drivingCorridor.Clear();drivingCorridorRoadRequired.Clear();
            var projection=Region.ProjectRoadForVehicle(position,Player.RoadSupportHeight);
            var road=projection.Road;var centre=projection.Source.Point;float separation=projection.Source.Separation;
            // Preserve planar road/verge classification even if no deck has a
            // plausible height: missing asphalt must still be tested by the strict guard.
            bool onRoad=road!=null&&separation<Mathf.Max(projection.Source.Width*.5f,4)+3;
            GeoRoad departedRoad=null;int departedDirection=1;
            if(onRoad&&BeyondKnownRoadEnd(position,projection,out departedDirection)){departedRoad=road;onRoad=false;}
            int direction=1;float station=0,offset=0;
            if(onRoad)
            {
                station=Region.RoadProgress(road,centre);Region.RoadPoint(road,station,out var forward);
                direction=Vector3.Dot(forward,heading)<0?-1:1;
                offset=Vector3.Dot(position-centre,Vector3.Cross(Vector3.up,forward.normalized));
            }
            float supported=0;bool missing=false,unresolvedEnd=false,groundContinuation=!onRoad;
            Vector3 groundOrigin=position,groundHeading=heading;float groundStartAt=0;GeoRoad endedRoad=departedRoad;int endedDirection=departedDirection;
            for(float at=0;at<=horizon;at+=8)
            {
                Vector3 point;bool requiresRoad=onRoad,groundPrepared=true;
                if(groundContinuation)
                {
                    var forecast=groundOrigin+groundHeading*(at-groundStartAt);
                    groundPrepared=Renderer.TryPreparedGroundPoint(forecast,out point);
                    requiresRoad=GroundContinuationRequiresRoad(point,endedRoad,endedDirection);
                }
                else if(Renderer.TryPlannedDrivingPoint(position,at,out var planned))
                {
                    var plannedProjection=Region.ProjectRoadForVehicle(planned,planned.y);
                    var selected=plannedProjection.Road;
                    if(selected==null)point=planned;
                    else
                    {
                        var plannedForward=heading;
                        if(Renderer.TryPlannedDrivingPoint(position,at+2,out var after)&&(after-planned).sqrMagnitude>.0001f)
                            plannedForward=(after-planned).normalized;
                        else if(at>0&&Renderer.TryPlannedDrivingPoint(position,Mathf.Max(0,at-2),out var before)&&(planned-before).sqrMagnitude>.0001f)
                            plannedForward=(planned-before).normalized;
                        point=Region.DrivingLanePoint(selected,plannedProjection.Source.Point,plannedForward,out var travel);
                        // Continue from the planned tail when it ends within the
                        // forecast horizon, retaining the named road's legal lane.
                        road=selected;station=plannedProjection.Source.SourceDistance;
                        Region.RoadPoint(road,station,out var sourceForward);
                        direction=Vector3.Dot(sourceForward,travel)<0?-1:1;
                        var lateral=Vector3.Cross(Vector3.up,sourceForward.normalized);
                        offset=Vector3.Dot(point-plannedProjection.Source.Point,lateral)/Mathf.Max(.001f,lateral.sqrMagnitude);
                    }
                }
                else
                {
                    float next=station+(at==0?0:8)*direction;int crossed=0;
                    while((next<0||next>road.length)&&crossed++<16)
                    {
                        string node=direction>0?road.end:road.start;
                        Region.RoadPoint(road,direction>0?road.length:0,out var incoming);incoming*=direction;
                        GeoRoad best=null;int travel=1;float alignment=-2;
                        foreach(var candidate in Region.RoadsAtJunction(node))
                        {
                            if(candidate==road||candidate.id==road.id)continue;
                            int d=candidate.start==node?1:-1;if((d>0&&candidate.oneWay==-1)||(d<0&&candidate.oneWay==1))continue;
                            Region.RoadPoint(candidate,d>0?0:candidate.length,out var outgoing);
                            float score=Vector3.Dot(incoming,outgoing*d);if(score>alignment){best=candidate;travel=d;alignment=score;}
                        }
                        if(best==null)
                        {
                            float endOvershoot=direction>0?next-road.length:-next;
                            var endpoint=Region.RoadPoint(road,direction>0?road.length:0,out var sourceTangent);
                            endpoint+=Vector3.Cross(Vector3.up,sourceTangent.normalized)*Mathf.Clamp(offset,-road.width*.4f,road.width*.4f);
                            if(!KnownPreparedRoadEnd(road,direction))
                            {point=endpoint+incoming*endOvershoot;drivingCorridor.Add(point);drivingCorridorRoadRequired.Add(true);missing=true;unresolvedEnd=true;goto Finished;}
                            // A genuine interior native road end is not missing
                            // asphalt. Continue only over proven prepared support.
                            groundContinuation=true;endedRoad=road;endedDirection=direction;groundOrigin=endpoint;groundHeading=incoming.normalized;groundStartAt=at-endOvershoot;
                            groundPrepared=Renderer.TryPreparedGroundPoint(endpoint+groundHeading*endOvershoot,out point);
                            requiresRoad=GroundContinuationRequiresRoad(point,endedRoad,endedDirection);
                            goto PointReady;
                        }
                        float remainder=direction>0?next-road.length:-next;road=best;direction=travel;next=direction>0?remainder:road.length-remainder;
                    }
                    station=Mathf.Clamp(next,0,road.length);point=Region.RoadPoint(road,station,out var tangent);
                    point+=Vector3.Cross(Vector3.up,tangent.normalized)*Mathf.Clamp(offset,-road.width*.4f,road.width*.4f);
                }
                PointReady:
                drivingCorridor.Add(point);drivingCorridorRoadRequired.Add(requiresRoad);
                if(!missing && (!groundPrepared||(requiresRoad?!Renderer.HasDrivingSurfaceAt(point):!Renderer.HasPreparedGroundAt(point))))missing=true;
                if(!missing)supported=at;
            }
            Finished:
            Renderer.SetDrivingCorridor(drivingCorridor,speed,onRoad,unresolvedEnd,drivingCorridorRoadRequired);
            float limit=missing?CoverageSpeed(supported):float.PositiveInfinity;
            Player.StreamingSpeedLimit=limit;
            Player.StreamingBrake=speed>limit+.25f || (missing&&supported<8);
        }
    }
}
