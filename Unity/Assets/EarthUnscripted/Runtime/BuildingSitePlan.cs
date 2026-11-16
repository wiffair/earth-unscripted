using System;
using UnityEngine;

namespace EarthUnscripted
{
    // A level artistic floor on sampled existing game ground. This neither edits
    // terrain nor invents a cadastral plot. Min/max below describe sampled ground.
    public static class BuildingSitePlan
    {
        public const int MaximumSamples=64;
        public const float FloorClearance=.12f,HighReliefLimit=2.5f;
        public sealed class Data
        {
            public float LevelFloorY,GroundMedianY,BoundaryMinY,BoundaryMaxY;
            public float MaxExposedFoundation,MaxUphillCut,FoundationBottomY;
            public bool HighRelief,ComplexFootprint,ConservativeFallback,CentreOutsideFootprint,RoofBurialRisk;
            public bool HasNearbyRoad,NearestRoadIsBridge;
            // Positive means the road is ABOVE the sampled ground / level floor.
            public float NearestRoadY,RoadGroundDelta,RoadToFloorDelta;
            public Vector3 Centre;
            // Last sample is the centre. Earlier entries sample the perimeter.
            public Vector3[] Samples;
            public int SampleCount=>Samples.Length;
            public int BoundarySampleCount=>Samples.Length-1;
            public float EntryThresholdY=>LevelFloorY;
            public bool RequiresAccessReview=>ConservativeFallback||NearestRoadIsBridge||(HasNearbyRoad&&Mathf.Abs(RoadToFloorDelta)>.35f);
        }
        public static Data Create(GeoRegion region,GeoBuilding building,Func<float,float,float> surface,Func<Vector3,GeoRegion.RoadProjection> finalRoadQuery=null)
        {
            if(region==null||building==null||surface==null)throw new ArgumentNullException("Building site input");
            var points=building.points;int n=points==null?0:points.Length-1;
            if(n<3||n>4096||!Finite(building.height)||building.height<=0)throw new ArgumentException("Invalid bounded building site footprint");
            if(!Finite(points[n].x)||!Finite(points[n].z)||(new Vector2(points[0].x,points[0].z)-new Vector2(points[n].x,points[n].z)).sqrMagnitude>1e-8f)throw new ArgumentException("Building site footprint is not closed");
            double twiceArea=0;float perimeter=0;var centre=Vector3.zero;
            for(int i=0;i<n;i++)
            {
                var a=points[i];var b=points[(i+1)%n];if(!Finite(a.x)||!Finite(a.z))throw new ArgumentException("Nonfinite building site footprint");
                perimeter+=new Vector2(b.x-a.x,b.z-a.z).magnitude;twiceArea+=(double)a.x*b.z-(double)b.x*a.z;centre+=new Vector3(a.x,0,a.z);
            }
            if(!Finite(perimeter)||perimeter<.01f||Math.Abs(twiceArea)<.0001)throw new ArgumentException("Degenerate building site footprint");
            // Canonical start and direction make equivalent CW/CCW or rotated
            // source rings sample exactly the same arc positions without mutation.
            int first=0;for(int i=1;i<n;i++)if(points[i].x<points[first].x||(points[i].x==points[first].x&&points[i].z<points[first].z))first=i;
            var canonical=new Vector3[n+1];int direction=twiceArea>0?1:-1;
            for(int i=0;i<n;i++)canonical[i]=points[(first+direction*i+n)%n];canonical[n]=canonical[0];points=canonical;
            centre=Vector3.zero;perimeter=0;for(int i=0;i<n;i++){centre+=new Vector3(points[i].x,0,points[i].z);perimeter+=EdgeLength(points,i,n);}centre/=n;
            bool complex=n>32;const float sign=1;
            for(int i=0;i<n&&!complex;i++)
            {var a=points[i];var b=points[(i+1)%n];var c=points[(i+2)%n];if(((b.x-a.x)*(c.z-b.z)-(b.z-a.z)*(c.x-b.x))*sign<-.0001f)complex=true;}
            // Even arc-length spacing avoids weighting a detailed outline more heavily
            // simply because its source contains many vertices. Boundary extrema are
            // sampled estimates, not guaranteed extrema of unsampled terrain.
            int count=Mathf.Clamp(Mathf.CeilToInt(perimeter/2.5f),Mathf.Min(n,MaximumSamples-1),MaximumSamples-1);
            var samples=new Vector3[count+1];var heights=new float[count+1];float boundaryMin=float.PositiveInfinity,boundaryMax=float.NegativeInfinity;
            int edge=0;float edgeStart=0;float edgeLength=EdgeLength(points,edge,n);
            for(int i=0;i<count;i++)
            {
                float distance=perimeter*i/count;
                while(edge<n-1&&(edgeLength<.000001f||distance>edgeStart+edgeLength))
                {edgeStart+=edgeLength;edge++;edgeLength=EdgeLength(points,edge,n);}
                var p=Vector3.Lerp(points[edge],points[(edge+1)%n],edgeLength>.000001f?(distance-edgeStart)/edgeLength:0);
                p.y=Sample(surface,p);samples[i]=p;heights[i]=p.y;boundaryMin=Mathf.Min(boundaryMin,p.y);boundaryMax=Mathf.Max(boundaryMax,p.y);
            }
            centre.y=Sample(surface,centre);samples[count]=centre;heights[count]=centre.y;Array.Sort(heights);
            float median=heights.Length%2==1?heights[heights.Length/2]:(heights[heights.Length/2-1]+heights[heights.Length/2])*.5f;
            float floor=median+FloorClearance;bool outside=!GeoRegion.Contains(points,new Vector2(centre.x,centre.z));
            GeoRoad road;Vector3 roadPoint;float separation;if(finalRoadQuery==null)road=region.Nearest(centre,out roadPoint,out separation);else{var projection=finalRoadQuery(centre);road=projection.Road;roadPoint=projection.Point;separation=projection.Separation;}bool nearby=road!=null&&separation<50;
            bool relief=boundaryMax-boundaryMin>HighReliefLimit;bool roofRisk=boundaryMax>floor+building.height-.3f;
            return new Data{LevelFloorY=floor,GroundMedianY=median,BoundaryMinY=boundaryMin,BoundaryMaxY=boundaryMax,
                MaxExposedFoundation=Mathf.Max(0,floor-boundaryMin),MaxUphillCut=Mathf.Max(0,boundaryMax-floor),FoundationBottomY=boundaryMin-.2f,
                HighRelief=relief,ComplexFootprint=complex,CentreOutsideFootprint=outside,RoofBurialRisk=roofRisk,ConservativeFallback=relief||complex||outside||roofRisk,
                HasNearbyRoad=nearby,NearestRoadIsBridge=nearby&&road.bridge,NearestRoadY=nearby?roadPoint.y:0,
                RoadGroundDelta=nearby?roadPoint.y-median:0,RoadToFloorDelta=nearby?roadPoint.y-floor:0,Centre=centre,Samples=samples};
        }
        static float EdgeLength(Vector3[] points,int edge,int n)=>new Vector2(points[(edge+1)%n].x-points[edge].x,points[(edge+1)%n].z-points[edge].z).magnitude;
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static float Sample(Func<float,float,float> surface,Vector3 p)
        {float value=surface(p.x,p.z);if(!Finite(value))throw new ArgumentException("Nonfinite building site surface");return value;}
    }
}
