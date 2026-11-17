using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class AutomaticWorldHost
    {
        readonly List<Vector3> drivingCorridor=new List<Vector3>(256);
        float nextCoverageAt;
        public static float PreparationHorizon(float speed,float latency)=>Mathf.Clamp(speed*(Mathf.Clamp(latency,3,20)+6)+speed*speed/5+96,320,1800);
        public static float CoverageSpeed(float supportedMetres)=>Mathf.Sqrt(Mathf.Max(0,2*2.5f*(supportedMetres-24)));
        void UpdateDrivingCoverage()
        {
            if(Relocating){Player.StreamingSpeedLimit=0;Player.StreamingBrake=true;return;}
            if(Time.unscaledTime<nextCoverageAt)return;nextCoverageAt=Time.unscaledTime+.1f;
            float speed=Player.AbsoluteSpeed,horizon=PreparationHorizon(speed,Renderer.RecentPreparationSeconds);
            var velocity=Vector3.ProjectOnPlane(Player.Body.linearVelocity,Vector3.up);
            var heading=velocity.sqrMagnitude>1?velocity.normalized:Observer.forward;
            drivingCorridor.Clear();
            var road=Region.Nearest(Observer.position,out var centre,out float separation);
            bool onRoad=road!=null&&separation<Mathf.Max(road.width*.5f,4)+3;
            int direction=1;float station=0,offset=0;
            if(onRoad)
            {
                station=Region.RoadProgress(road,centre);Region.RoadPoint(road,station,out var forward);
                direction=Vector3.Dot(forward,heading)<0?-1:1;
                offset=Vector3.Dot(Observer.position-centre,Vector3.Cross(Vector3.up,forward.normalized));
            }
            float supported=0;bool missing=false,unresolvedEnd=false;
            for(float at=0;at<=horizon;at+=8)
            {
                Vector3 point;
                if(!onRoad)point=Observer.position+heading*at;
                else if(Renderer.TryPlannedDrivingPoint(Observer.position,at,out var planned))
                {
                    var selected=Region.Nearest(planned,out var snapped,out _);
                    point=selected!=null?Region.DrivingLanePoint(selected,snapped,heading,out _):planned;
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
                        if(best==null){point=Region.RoadPoint(road,Mathf.Clamp(next,0,road.length),out _)+incoming*(direction>0?next-road.length:-next);drivingCorridor.Add(point);missing=true;unresolvedEnd=true;goto Finished;}
                        float remainder=direction>0?next-road.length:-next;road=best;direction=travel;next=direction>0?remainder:road.length-remainder;
                    }
                    station=Mathf.Clamp(next,0,road.length);point=Region.RoadPoint(road,station,out var tangent);
                    point+=Vector3.Cross(Vector3.up,tangent.normalized)*Mathf.Clamp(offset,-road.width*.4f,road.width*.4f);
                }
                drivingCorridor.Add(point);
                if(!missing && (onRoad?!Renderer.HasDrivingSurfaceAt(point):!Renderer.HasGeometryAt(point)))missing=true;
                if(!missing)supported=at;
            }
            Finished:
            Renderer.SetDrivingCorridor(drivingCorridor,speed,onRoad,unresolvedEnd);
            float limit=missing?CoverageSpeed(supported):float.PositiveInfinity;
            Player.StreamingSpeedLimit=limit;
            Player.StreamingBrake=speed>limit+.25f || (missing&&supported<8);
        }
    }
}
