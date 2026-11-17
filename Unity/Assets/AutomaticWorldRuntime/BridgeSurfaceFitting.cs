using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        Map bridgePathMatchMap;
        readonly Dictionary<Feature,Feature> bridgePathRoads=new Dictionary<Feature,Feature>();
        static bool NearestBridgePoint(Feature road,Vector3 point,out Vector3 nearest,out Vector3 tangent,out float distance)
        {
            nearest=point;tangent=Vector3.forward;distance=float.PositiveInfinity;
            for(int i=1;i<road.points.Length;i++)
            {
                var a=road.points[i-1];var delta=road.points[i]-a;float square=delta.x*delta.x+delta.z*delta.z;if(square<1e-8f)continue;
                float t=Mathf.Clamp01(((point.x-a.x)*delta.x+(point.z-a.z)*delta.z)/square);var at=a+delta*t;
                float gap=(at.x-point.x)*(at.x-point.x)+(at.z-point.z)*(at.z-point.z);
                if(gap>=distance)continue;distance=gap;nearest=at;tangent=Vector3.ProjectOnPlane(delta,Vector3.up).normalized;
            }
            distance=Mathf.Sqrt(distance);return !float.IsInfinity(distance);
        }
        Feature SharedBridgeRoad(Feature pathOwner)
        {
            if(pathOwner==null||pathOwner.category!="path"||!pathOwner.bridge||pathOwner.tunnel||pathOwner.points.Length<3)return null;
            var source=workingMap??map;if(source==null)return null;
            if(bridgePathMatchMap!=source){bridgePathMatchMap=source;bridgePathRoads.Clear();}
            if(bridgePathRoads.TryGetValue(pathOwner,out var existing))return existing;
            Feature best=null;float score=float.PositiveInfinity;
            foreach(var road in source.features)
            {
                if(road.category!="road"||!road.bridge||road.tunnel||road.layer!=pathOwner.layer||(road.level??"0")!=(pathOwner.level??"0")||road.points.Length<3)continue;
                float maximum=road.width*.5f+pathOwner.width*.5f+3;
                var start=pathOwner.points[0];var end=pathOwner.points[pathOwner.points.Length-1];
                if(Vector3.ProjectOnPlane(end-start,Vector3.up).sqrMagnitude<20*20)continue;
                if(!NearestBridgePoint(road,start,out var first,out var firstTangent,out float firstGap)||firstGap>maximum||Mathf.Abs(first.y-start.y)>2)continue;
                if(!NearestBridgePoint(road,end,out var last,out var lastTangent,out float lastGap)||lastGap>maximum||Mathf.Abs(last.y-end.y)>2)continue;
                var startDirection=Vector3.ProjectOnPlane(pathOwner.points[1]-start,Vector3.up).normalized;
                var endDirection=Vector3.ProjectOnPlane(end-pathOwner.points[pathOwner.points.Length-2],Vector3.up).normalized;
                if(Mathf.Abs(Vector3.Dot(startDirection,firstTangent))<.94f||Mathf.Abs(Vector3.Dot(endDirection,lastTangent))<.94f)continue;
                int middle=pathOwner.points.Length/2;var centre=pathOwner.points[middle];
                if(!NearestBridgePoint(road,centre,out _,out var middleTangent,out float middleGap)||middleGap>maximum)continue;
                var pathTangent=Vector3.ProjectOnPlane(pathOwner.points[middle+1]-pathOwner.points[middle-1],Vector3.up).normalized;
                if(Mathf.Abs(Vector3.Dot(pathTangent,middleTangent))<.94f)continue;
                float candidate=firstGap+lastGap+middleGap;if(candidate<score){best=road;score=candidate;}
            }
            // Nearby parallel paths which share both bridge ends use one deck
            // profile. Independent crossing/higher footbridges retain their own.
            bridgePathRoads[pathOwner]=best;return best;
        }
        Vector3 BridgeSurfacePoint(Feature owner,Vector3 point)
        {
            if(owner.category=="road")return RoadSurfacePoint(owner,point);
            var road=SharedBridgeRoad(owner);
            if(road!=null)
            {
                float nearest=float.PositiveInfinity,height=point.y;
                foreach(var segment in OwnSegments(road,point.x,point.z))
                {float gap=Project(segment,point.x,point.z,out float sample);if(gap<nearest){nearest=gap;height=sample;}}
                if(float.IsInfinity(nearest)&&NearestBridgePoint(road,point,out var centre,out _,out _))height=centre.y;
                point.y=height;point.y=RoadSurfacePoint(road,point).y+KerbLiftMetres;
            }
            return point;
        }
        void BridgeDeckBox(Batch batch,Feature owner,Vector3 a,Vector3 b,Vector3 sideA,Vector3 sideB,float half,float bottom,float top)
        {
            var al=BridgeSurfacePoint(owner,a-sideA*half);var ar=BridgeSurfacePoint(owner,a+sideA*half);
            var bl=BridgeSurfacePoint(owner,b-sideB*half);var br=BridgeSurfacePoint(owner,b+sideB*half);
            var up=Vector3.up*top;var down=Vector3.up*bottom;
            batch.Quad(al+up,bl+up,br+up,ar+up);batch.Quad(al+down,ar+down,br+down,bl+down);
            batch.Quad(al+down,bl+down,bl+up,al+up);batch.Quad(ar+down,ar+up,br+up,br+down);
            batch.Quad(al+down,al+up,ar+up,ar+down);batch.Quad(bl+down,br+down,br+up,bl+up);
        }
    }
}
