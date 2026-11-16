using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        struct ClearStartSite { public GeoRoad Road; public float Station,Score; }
        public bool ClearStartVerified { get; private set; }
        bool ClearStartBox(VehicleController car, Vector3 ground, Quaternion rotation)
        {
            var m=car.Metrics;
            var centre=ground+rotation*new Vector3(0,m.RestRootHeight(car.Body.mass,car.SpringFactor)+m.BodyColliderY,m.ColliderCentreZ);
            var half=new Vector3(Mathf.Max(m.ColliderWidth,m.Track+m.TyreWidth)*.5f+.25f,Mathf.Max(.25f,m.BodyColliderHeight*.5f-.06f),m.ColliderLength*.5f+.5f);
            foreach(var hit in Physics.OverlapBox(centre,half,rotation,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(car.transform))return false;
            return true;
        }
        bool PlaceClearAutomaticStart(VehicleController car)
        {
            var region=World.Route.Region;var requested=car.Body.position;var sites=new List<ClearStartSite>();
            foreach(var road in region.roads)
            {
                if(road.length<45 || road.width<Mathf.Max(car.Metrics.ColliderWidth,car.Metrics.Track+car.Metrics.TyreWidth)+.6f || road.roundabout || road.kind=="track" || road.kind=="path" || road.kind=="footway")continue;
                float nearest=float.PositiveInfinity,station=0,along=0;
                for(int i=1;i<road.points.Length;i++)
                {
                    var a=road.points[i-1];var d=road.points[i]-a;float length=Vector3.Distance(a,road.points[i]);
                    float t=Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(requested-a,Vector3.up),Vector3.ProjectOnPlane(d,Vector3.up))/Mathf.Max(.001f,d.x*d.x+d.z*d.z));
                    float distance=Vector3.ProjectOnPlane(a+d*t-requested,Vector3.up).sqrMagnitude;
                    if(distance<nearest){nearest=distance;station=along+t*length;}along+=length;
                }
                if(nearest>200*200)continue;
                foreach(float offset in new[]{0f,12,-12,28,-28,56,-56})
                {
                    float at=Mathf.Clamp(station+offset,12,road.length-24);var centre=region.RoadPoint(road,at,out _);
                    float distance=Vector3.ProjectOnPlane(centre-requested,Vector3.up).magnitude;
                    sites.Add(new ClearStartSite{Road=road,Station=at,Score=distance+(road.kind=="service"?60:0)});
                }
            }
            foreach(var site in sites.OrderBy(s=>s.Score).Take(160))
            {
                var road=site.Road;var centre=region.RoadPoint(road,site.Station,out var sourceForward);
                var point=region.DrivingLanePoint(road,centre,sourceForward*(road.oneWay<0?-1:1),out var forward);
                if(!World.AutomaticWorld.Renderer.HasGeometryAt(point))continue;
                if(!car.TryInitializeSupportedRest(point,Quaternion.LookRotation(forward,Vector3.up),out _,out _))continue;
                if(!ClearStartBox(car,point,car.Body.rotation))continue;
                bool exit=true;
                foreach(float distance in new[]{8f,16f})
                {
                    float at=site.Station+distance*(road.oneWay<0?-1:1);
                    if(at<0||at>road.length){exit=false;break;}
                    var ahead=region.RoadPoint(road,at,out var tangent);var lane=region.DrivingLanePoint(road,ahead,forward,out var direction);
                    if(!World.AutomaticWorld.Renderer.HasGeometryAt(lane) || Vector3.Angle(forward,direction)>35 || !ClearStartBox(car,lane,Quaternion.LookRotation(direction,car.Body.rotation*Vector3.up))){exit=false;break;}
                }
                if(!exit)continue;
                ClearStartVerified=true;World.GeographicWorld.Refresh(point,true);return true;
            }
            return false;
        }
    }
}
