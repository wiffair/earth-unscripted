using System;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class AutomaticWorldHost
    {
        // Match the automatic pedals' stopped direction-change threshold. Moving
        // coverage always follows real momentum; stopped coverage follows input.
        public static Vector3 CoverageTravelHeading(Vector3 velocity,Vector3 forward,float throttle,float brake,bool reverseGear)
        {
            velocity=Vector3.ProjectOnPlane(velocity,Vector3.up);
            if(velocity.sqrMagnitude>.35f*.35f)return velocity.normalized;
            forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
            bool reverse=brake>.05f&&throttle<=.05f||brake<=.05f&&throttle<=.05f&&reverseGear;
            return forward*(reverse?-1:1);
        }
        bool KnownPreparedRoadEnd(GeoRoad road,int direction)
        {
            if(road==null||road.bridge||road.tunnel||road.layer>0)return false;
            string node=direction>0?road.end:road.start;
            if(string.IsNullOrEmpty(node)||!node.StartsWith("osm:",StringComparison.Ordinal))return false;
            // An interior source section can end at a mapped junction. It is
            // not a terminal when another legally outgoing source road exists.
            foreach(var candidate in Region.RoadsAtJunction(node))
            {
                if(candidate==road||candidate.id==road.id)continue;
                int travel=candidate.start==node?1:-1;
                if((travel>0&&candidate.oneWay!=-1)||(travel<0&&candidate.oneWay!=1))return false;
            }
            var end=Region.RoadPoint(road,direction>0?road.length:0,out _);
            // A clipped frontier must still wait for source. Require actual
            // resident ownership around the endpoint, not the region's AABB.
            for(int z=-1;z<=1;z+=2)for(int x=-1;x<=1;x+=2)
            {
                float px=end.x+x*16,pz=end.z+z*16;bool owned=false;
                foreach(var area in Renderer.ResidentAreas)if(px>=area.coreMinX&&px<area.coreMaxX&&pz>=area.coreMinZ&&pz<area.coreMaxZ){owned=true;break;}
                if(!owned)return false;
            }
            return true;
        }
        bool BeyondKnownRoadEnd(Vector3 position,GeoRegion.VehicleRoadProjection projection,out int direction)
        {
            direction=1;var road=projection.Road;if(road==null)return false;
            Region.RoadPoint(road,projection.Source.SourceDistance,out var tangent);
            float beyond=Vector3.Dot(Vector3.ProjectOnPlane(position-projection.Source.Point,Vector3.up),Vector3.ProjectOnPlane(tangent,Vector3.up));
            if(projection.Source.SourceDistance<=.001f&&beyond<-.01f)direction=-1;
            else if(projection.Source.SourceDistance<road.length-.001f||beyond<=.01f)return false;
            return KnownPreparedRoadEnd(road,direction);
        }
        bool GroundContinuationRequiresRoad(Vector3 point,GeoRoad endedRoad,int direction)
        {
            var projection=Region.ProjectRoadForVehicle(point,point.y);var owner=projection.Road;
            if(owner==null||projection.Source.Separation>=Mathf.Max(projection.Source.Width*.5f,4)+3)return false;
            // This path has already proved actual ground support. An explicitly
            // separate deck/tunnel cannot demand asphalt at the ground's height.
            if(!projection.HasPlausibleHeight&&(owner.bridge||owner.tunnel))return false;
            if(owner==endedRoad)
            {
                var end=Region.RoadPoint(owner,direction>0?owner.length:0,out var tangent);
                if(Vector3.Dot(Vector3.ProjectOnPlane(point-end,Vector3.up),Vector3.ProjectOnPlane(tangent*direction,Vector3.up))>.01f)return false;
            }
            return true; // Rejoining a mapped road never substitutes terrain for its asphalt.
        }
    }
}
