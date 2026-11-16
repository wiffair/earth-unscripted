using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        public static PathsD TrimBuildingRoads(PathsD footprint,PathsD corridors)
        {
            if(corridors.Count==0)return footprint;
            foreach(var path in corridors)if(Clipper.Area(path)<0)path.Reverse();
            return Clipper.Difference(footprint,corridors,FillRule.NonZero,3);
        }
        public static bool BuildingSharesRoadHeight(float roadLow,float roadHigh,float floor,float top)
            => roadHigh+4.8f>=floor && roadLow<=top+.25f;
        public bool FacadeOpeningClear(Vector3 site,float width,float height)
        {
            float radius=width*.5f+.35f,low=site.y-height*.5f,high=site.y+height*.5f;
            foreach(var pair in spatialByMap)
            {
                var cells=pair.Value.segments;
                for(int z=Mathf.FloorToInt((site.z-radius)/SegmentCellSize);z<=Mathf.FloorToInt((site.z+radius)/SegmentCellSize);z++)
                for(int x=Mathf.FloorToInt((site.x-radius)/SegmentCellSize);x<=Mathf.FloorToInt((site.x+radius)/SegmentCellSize);x++)
                {
                    if(!cells.TryGetValue(new Vector2Int(x,z),out var nearby))continue;
                    foreach(var segment in nearby)
                    {
                        var feature=segment.feature;
                        if(feature.category!="road"&&feature.category!="rail")continue;
                        float clearance=feature.category=="rail"?Mathf.Max(5.6f,feature.railClearanceHeight):4.8f;
                        if(high<Mathf.Min(segment.a.y,segment.b.y)-.25f||low>Mathf.Max(segment.a.y,segment.b.y)+clearance)continue;
                        var a=new Vector2(segment.a.x,segment.a.z);var b=new Vector2(segment.b.x,segment.b.z);var p=new Vector2(site.x,site.z);
                        var delta=b-a;float t=delta.sqrMagnitude>.0001f?Mathf.Clamp01(Vector2.Dot(p-a,delta)/delta.sqrMagnitude):0;
                        float half=(feature.category=="rail"?Mathf.Max(feature.width,feature.railClearanceWidth):feature.width)*.5f;
                        if(Vector2.Distance(p,a+delta*t)<half+radius)return false;
                    }
                }
            }
            return true;
        }
        PathsD BuildingRoadSpace(Building house,List<Vector3> points,float floor,float top,SpatialData spatial,BuildingWorkStats stats,bool audit)
        {
            var outline=new PathD(points.Select(p=>new PointD(p.x,p.z)));if(Clipper.Area(outline)<0)outline.Reverse();
            var subject=new PathsD{outline};
            foreach(var hole in house.holes??Array.Empty<CoastRing>())
            {if(hole.points==null||hole.points.Length<3)continue;var ring=new PathD(hole.points.Select(p=>new PointD(p.x,p.z)));if(Clipper.Area(ring)>0)ring.Reverse();subject.Add(ring);}
            var clips=new PathsD();var seen=new HashSet<(Feature,int)>();
            float minX=points.Min(p=>p.x),maxX=points.Max(p=>p.x),minZ=points.Min(p=>p.z),maxZ=points.Max(p=>p.z);
            var cells=spatial.segments;
            for(int z=Mathf.FloorToInt(minZ/SegmentCellSize);z<=Mathf.FloorToInt(maxZ/SegmentCellSize);z++)
            for(int x=Mathf.FloorToInt(minX/SegmentCellSize);x<=Mathf.FloorToInt(maxX/SegmentCellSize);x++)
            {
                if(!cells.TryGetValue(new Vector2Int(x,z),out var nearby))continue;
                foreach(var segment in nearby)
                {
                    var f=segment.feature;int i=segment.pointIndex;
                    if((f.category!="road" && f.category!="rail") || i<1 || !seen.Add((f,i)))continue;
                    // Preserve a building below a high bridge, or safely above a bore.
                    // Tunnel tags never exempt a solid facade at driving height.
                    // Only carriageways intersecting the rendered building's
                    // vertical envelope subtract from its footprint.
                    float headroom=f.category=="rail"?Mathf.Max(5.6f,f.railClearanceHeight):4.8f;
                    if(Mathf.Max(segment.a.y,segment.b.y)+headroom<floor || Mathf.Min(segment.a.y,segment.b.y)>top+.25f)continue;
                    float half=(f.category=="rail"?Mathf.Max(f.width,f.railClearanceWidth):f.width)*.5f+.30f;
                    var sa=Join(f.points,i-1);var sb=Join(f.points,i);
                    var direction=Vector3.ProjectOnPlane(segment.b-segment.a,Vector3.up).normalized*.30f;
                    var a=segment.a-direction;var b=segment.b+direction;
                    var quad=new[]{a-sa*half,b-sb*half,b+sb*half,a+sa*half};
                    if(quad.Max(p=>p.x)<minX||quad.Min(p=>p.x)>maxX||quad.Max(p=>p.z)<minZ||quad.Min(p=>p.z)>maxZ)continue;
                    clips.Add(new PathD(quad.Select(p=>new PointD(p.x,p.z))));
                }
            }
            var allowed=TrimBuildingRoads(subject,clips);
            if(Math.Abs(Clipper.Area(subject)-Clipper.Area(allowed))<.0001&&(house.holes==null||house.holes.Length==0))return null;
            stats.trimmed++;
            // Record the actual contours used for both visuals and collision;
            // the native isolated proof verifies them independently.
            if(audit && clips.Count>0)
            {
                var overlap=Clipper.Intersect(allowed,Clipper.Union(clips,new PathsD(),FillRule.NonZero,3),FillRule.NonZero,3);
                stats.overlap=Math.Max(stats.overlap,Math.Abs(Clipper.Area(overlap)));
            }
            return allowed;
        }
        void ClippedBuilding(Building house,List<Vector3> original,PathsD contours,float foundation,float roofBase,Batch walls,Batch roofs,BuildingWorkStats stats)
        {
            bool pitched=TryPitchedRoof(original,house,out var side,out float min,out float max);
            float rise=pitched?Mathf.Clamp(house.roofHeight,.5f,5):0;
            if(pitched&&house.heightIncludesRoof)roofBase-=rise;
            float mid=(min+max)*.5f,half=(max-min)*.5f;
            float Height(Vector3 p)=>roofBase+(pitched?rise*Mathf.Clamp01(1-Mathf.Abs(Vector3.Dot(p,side)-mid)/half):0);
            foreach(var contour in contours)
            {
                for(int i=0;i<contour.Count;i++)
                {
                    var p=contour[i];var q=contour[(i+1)%contour.Count];
                    var a=new Vector3((float)p.x,foundation,(float)p.y);var b=new Vector3((float)q.x,foundation,(float)q.y);
                    var ta=a;var tb=b;ta.y=Height(ta);tb.y=Height(tb);
                    // Clipper reverses holes: the same winding rule gives
                    // road-facing walls along a cut through a building.
                    walls.Quad(b,a,ta,tb);
                }
            }
            if(contours.Count==0)return;
            if(Clipper.Triangulate(contours,3,out var triangles)!=TriangulateResult.success)
            {stats.failedRoofs++;return;}
            foreach(var triangle in triangles)
            {
                var polygon=triangle.Select(p=>new Vector3((float)p.x,0,(float)p.y)).ToList();
                foreach(bool below in pitched?new[]{true,false}:new[]{true})
                {
                    var part=polygon;
                    if(pitched)
                    {
                        part=new List<Vector3>();var previous=polygon[polygon.Count-1];float pd=Vector3.Dot(previous,side)-mid;
                        foreach(var point in polygon)
                        {
                            float d=Vector3.Dot(point,side)-mid;
                            if((below?d<=0:d>=0)!=(below?pd<=0:pd>=0))part.Add(Vector3.Lerp(previous,point,pd/(pd-d)));
                            if(below?d<=0:d>=0)part.Add(point);previous=point;pd=d;
                        }
                    }
                    for(int i=1;i<part.Count-1;i++)
                    {
                        var a=part[0];var b=part[i];var c=part[i+1];a.y=Height(a);b.y=Height(b);c.y=Height(c);
                        if(Vector3.Cross(b-a,c-a).y<0)roofs.Triangle(a,c,b);else roofs.Triangle(a,b,c);
                    }
                }
            }
        }
    }
}
