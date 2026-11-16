using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        /// <summary>
        /// Local source-node connection and its authoritative road surface.
        /// The worker supplies ownership; runtime never joins roads by X/Z alone.
        /// All distances and elevations use world metres. Resolved segments are
        /// owned by one immutable map and prepared on its indexing worker task.
        /// </summary>
        [Serializable] public sealed class SurfaceJoin
        {
            public string ownerId;
            public float x,z,radius;
            [NonSerialized] public Feature owner;
            [NonSerialized] internal List<Segment> segments;
        }

        const float JunctionBlendMetres=8f;
        const float RoadbedDepthMetres=.24f;
        const float PaintBoundaryResolutionMetres=.08f;

        static void ResolveSurfaceJoins(Map source)
        {
            PrepareJunctionPaving(source);
            var byId=new Dictionary<string,Feature>();
            foreach(var feature in source.features)
            {
                byId[feature.id]=feature;
                feature.sharpBends=new List<Vector3>();
                for(int index=1;index<feature.points.Length-1;index++)
                {
                    var before=Vector3.ProjectOnPlane(feature.points[index]-feature.points[index-1],Vector3.up).normalized;
                    var after=Vector3.ProjectOnPlane(feature.points[index+1]-feature.points[index],Vector3.up).normalized;
                    if(Vector3.Dot(before,after)<.97f)feature.sharpBends.Add(feature.points[index]);
                }
            }
            foreach(var feature in source.features)
            {
                if(feature.junctions==null)continue;
                foreach(var join in feature.junctions)
                {
                    if(!byId.TryGetValue(join.ownerId,out var owner))continue;
                    join.owner=owner;
                    join.segments=new List<Segment>();
                    for(int index=1;index<owner.points.Length;index++)
                    {
                        var a=owner.points[index-1]+Join(owner.points,index-1)*owner.roadCentreOffset;
                        var b=owner.points[index]+Join(owner.points,index)*owner.roadCentreOffset;
                        var segment=new Segment{feature=owner,a=a,b=b,surfaceStart=index==1,surfaceEnd=index==owner.points.Length-1};
                        if(Project(segment,join.x,join.z,out _)<=join.radius+owner.width+JunctionBlendMetres)
                            join.segments.Add(segment);
                    }
                }
                Array.Sort(feature.junctions,(first,second)=>(second.owner?.surfacePriority??int.MaxValue).CompareTo(first.owner?.surfacePriority??int.MaxValue));
            }
        }

        // A finite ribbon, rather than a round cap beyond its source endpoint,
        // owns a junction. Projection onto a remote part of a looping road is
        // excluded by both the local source anchor and the prepared segment set.
        static bool OwnerSurface(SurfaceJoin join,Vector3 point,out float distance,out float height)
        {
            distance=float.MaxValue;height=point.y;
            if(join.owner==null || join.segments==null)return false;
            float dx=point.x-join.x,dz=point.z-join.z;
            if(dx*dx+dz*dz>join.radius*join.radius)return false;
            foreach(var segment in join.segments)
            {
                var direction=Vector3.ProjectOnPlane(segment.b-segment.a,Vector3.up);
                float fraction=Vector3.Dot(Vector3.ProjectOnPlane(point-segment.a,Vector3.up),direction)/Mathf.Max(.0001f,direction.sqrMagnitude);
                bool closed=Vector3.ProjectOnPlane(join.owner.points[0]-join.owner.points[join.owner.points.Length-1],Vector3.up).sqrMagnitude<.0001f;
                // An internal bend has a corner wedge shared by adjacent
                // segments. Only actual open source endpoints have end caps;
                // rejecting every segment's clamped projection leaves gaps.
                if(!closed && (fraction<-.001f&&segment.surfaceStart || fraction>1.001f&&segment.surfaceEnd))continue;
                float candidate=Project(segment,point.x,point.z,out float elevation);
                if(candidate>=distance)continue;
                distance=candidate;height=elevation;
            }
            return distance<float.MaxValue;
        }

        Vector3 RoadSurfacePoint(Feature feature,Vector3 point,int depth=0)
        {
            // Cross sections can extend past a short resampled corner segment.
            // A single height field over the finite carriageway prevents those
            // overlapping inner strips from creating different-height ledges.
            var local=OwnSegments(feature,point.x,point.z);
            float ownDistance=float.MaxValue,ownHeight=point.y,sourceHeight=point.y;
            Segment nearest=default;
            if(local.Count>0)
            {
                foreach(var segment in local)
                {
                    if(segment.feature.sourceId!=feature.sourceId)continue;
                    float distance=Project(segment,point.x,point.z,out float height);
                    if(distance>=ownDistance || Mathf.Abs(height-sourceHeight)>2.5f)continue;
                    ownDistance=distance;ownHeight=height;nearest=segment;
                }
                if(ownDistance<float.MaxValue)
                {
                    // At an inner corner two adjacent arms are equally close.
                    // Blend those arms continuously instead of switching a
                    // half-metre height when the nearest segment changes.
                    float otherDistance=float.MaxValue,otherHeight=ownHeight;
                    var direction=Vector3.ProjectOnPlane(nearest.b-nearest.a,Vector3.up).normalized;
                    foreach(var segment in local)
                    {
                        if(segment.feature.sourceId!=feature.sourceId)continue;
                        var otherDirection=Vector3.ProjectOnPlane(segment.b-segment.a,Vector3.up).normalized;
                        if(Mathf.Abs(Vector3.Dot(direction,otherDirection))>=.97f)continue;
                        float distance=Project(segment,point.x,point.z,out float height);
                        if(distance>=otherDistance || distance>ownDistance+feature.width || Mathf.Abs(height-sourceHeight)>2.5f)continue;
                        otherDistance=distance;otherHeight=height;
                    }
                    if(otherDistance<float.MaxValue)
                    {
                        float ownWeight=1/Mathf.Pow(Mathf.Max(.001f,ownDistance),4);
                        float otherWeight=1/Mathf.Pow(Mathf.Max(.001f,otherDistance),4);
                        ownHeight=(ownHeight*ownWeight+otherHeight*otherWeight)/(ownWeight+otherWeight);
                    }
                    point.y=ownHeight;
                }
            }
            if(feature.junctions==null || depth>6)return point;
            // Apply weaker surfaces first. A distant dominant road with only
            // a tiny blend must not discard a nearer owner's complete fit.
            // Within overlapping pavement the strongest authority runs last.
            foreach(var join in feature.junctions)
            {
                if(!OwnerSurface(join,point,out float distance,out float height))continue;
                if(distance>join.owner.width*.5f+JunctionBlendMetres)continue;
                var owned=RoadSurfacePoint(join.owner,new Vector3(point.x,height,point.z),depth+1);
                float blend=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((distance-join.owner.width*.5f)/JunctionBlendMetres));
                // Local ownership ends at a finite radius. Fade to zero before
                // that boundary so a distant approach cannot acquire a step.
                float radial=Vector2.Distance(new Vector2(point.x,point.z),new Vector2(join.x,join.z));
                blend*=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,join.radius-JunctionBlendMetres),join.radius,radial));
                point.y=Mathf.Lerp(point.y,owned.y,blend);
            }
            return point;
        }

        static bool PaintVisible(Feature feature,Vector3 point)
        {
            if(feature.junctions==null)return true;
            foreach(var join in feature.junctions)
                if(OwnerSurface(join,point,out float distance,out _) && distance<=join.owner.width*.5f+.16f)
                    return false;
            return true;
        }

        static bool NeedsSurfaceDetail(Feature feature,Vector3 point)
        {
            if(feature.junctions!=null)
                foreach(var join in feature.junctions)
                    if(Vector2.Distance(new Vector2(point.x,point.z),new Vector2(join.x,join.z))<join.radius+JunctionBlendMetres+feature.width)return true;
            if(feature.sharpBends!=null)
                foreach(var bend in feature.sharpBends)
                    if(Vector3.ProjectOnPlane(point-bend,Vector3.up).sqrMagnitude<4*feature.width*feature.width)return true;
            return false;
        }

        static void RoadTopTriangle(Batch batch,Vector3 a,Vector3 b,Vector3 c)
        {
            float area=Vector3.Cross(b-a,c-a).y;
            if(Mathf.Abs(area)<.000001f)return;
            if(area>0)batch.Triangle(a,b,c);else batch.Triangle(a,c,b);
        }

        IEnumerable<bool> RoadRibbonRows(Batch batch,Feature feature,Vector3 a,Vector3 b,Vector3 rightA,Vector3 rightB,float halfWidth)
        {
            bool detailed=NeedsSurfaceDetail(feature,(a+b)*.5f);
            int along=detailed?Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/.5f)):1;
            int across=detailed?Mathf.Max(1,Mathf.CeilToInt(halfWidth*4)):1;
            // Each grid vertex is shared by up to four cells. Evaluate the
            // source-owned surface once, retaining the exact same triangles.
            // A suspended row owns both arrays. QA can sample another ribbon
            // while this coroutine is paused without overwriting its state.
            var previous=ArrayPool<Vector3>.Shared.Rent(across+1);
            var next=ArrayPool<Vector3>.Shared.Rent(across+1);
            try
            {
            for(int column=0;column<=across;column++)
                previous[column]=RoadSurfacePoint(feature,a+rightA*Mathf.Lerp(-halfWidth,halfWidth,(float)column/across));
            for(int row=0;row<along;row++)
            {
                float end=(float)(row+1)/along;
                var endPoint=Vector3.Lerp(a,b,end);var endRight=Vector3.Lerp(rightA,rightB,end);
                for(int column=0;column<=across;column++)
                    next[column]=RoadSurfacePoint(feature,endPoint+endRight*Mathf.Lerp(-halfWidth,halfWidth,(float)column/across));
                for(int column=0;column<across;column++)
                {
                    var leftA=previous[column];var leftB=next[column];
                    var edgeA=previous[column+1];var edgeB=next[column+1];
                    RoadTopTriangle(batch,leftA,leftB,edgeB);
                    RoadTopTriangle(batch,leftA,edgeB,edgeA);
                    var down=Vector3.down*RoadbedDepthMetres;
                    if(column==0)batch.Quad(leftA+down,leftB+down,leftB,leftA);
                    if(column==across-1)batch.Quad(edgeA,edgeB,edgeB+down,edgeA+down);
                }
                var swap=previous;previous=next;next=swap;
                yield return true;
            }
            }
            finally{ArrayPool<Vector3>.Shared.Return(previous);ArrayPool<Vector3>.Shared.Return(next);}
        }

        void RoadPaint(Batch batch,Feature feature,Vector3 a,Vector3 b,Vector3 rightA,Vector3 rightB,float halfWidth,int depth=0)
        {
            var leftA=a-rightA*halfWidth;var leftB=b-rightB*halfWidth;
            var edgeA=a+rightA*halfWidth;var edgeB=b+rightB*halfWidth;
            bool start=PaintVisible(feature,leftA)&&PaintVisible(feature,edgeA);
            bool end=PaintVisible(feature,leftB)&&PaintVisible(feature,edgeB);
            bool middle=PaintVisible(feature,(a+b)*.5f);
            if(!start&&!end&&!middle)return;
            bool refineSurface=NeedsSurfaceDetail(feature,(a+b)*.5f)&&Vector3.Distance(a,b)>.5f;
            if((!start||!end||!middle||refineSurface) && depth<8 && Vector3.Distance(a,b)>PaintBoundaryResolutionMetres)
            {
                var centre=(a+b)*.5f;var right=(rightA+rightB)*.5f;
                RoadPaint(batch,feature,a,centre,rightA,right,halfWidth,depth+1);
                RoadPaint(batch,feature,centre,b,right,rightB,halfWidth,depth+1);
                return;
            }
            if(!start||!end||!middle)return;
            var lift=Vector3.up*.012f;
            var topA=RoadSurfacePoint(feature,leftA)+lift;var topB=RoadSurfacePoint(feature,leftB)+lift;
            var topC=RoadSurfacePoint(feature,edgeB)+lift;var topD=RoadSurfacePoint(feature,edgeA)+lift;
            RoadTopTriangle(batch,topA,topB,topC);RoadTopTriangle(batch,topA,topC,topD);
        }
    }
}
