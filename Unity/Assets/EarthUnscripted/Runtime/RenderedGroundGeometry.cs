using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading;using UnityEngine;
namespace EarthUnscripted
{
    // CPU descriptors shared by asphalt upload and exact terrain support. Bridges
    // and tunnels never enter this ground-only collection.
    public sealed class RenderedGroundGeometry
    {
        public const int MaximumVertices=4000000,MaximumIndices=24000000;
        public sealed class Data{public Vector3[] Vertices;public int[] Triangles;public Vector2[] UV;}
        public sealed class Record{public string Id,Kind;public int Layer;public Vector2Int? OwnerCell;public Data Mesh;}
        public GeoRegion Owner{get;private set;}public EffectiveRoadWidthField Field{get;private set;}
        readonly List<Record> records=new List<Record>();public IReadOnlyList<Record> Records=>records;
        static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/320),Mathf.FloorToInt(p.z/320));
        public static Data Cap(Vector3 p,float radius,Func<float,float,float> surface)
        {
            const int sides=32;int rings=Mathf.Max(1,Mathf.CeilToInt(radius/1.5f));if(rings>27)throw new InvalidDataException("Ground cap radius budget");var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();Vector3 Point(float x,float z)=>new Vector3(x,surface(x,z)+.003f,z);v.Add(Point(p.x,p.z));uv.Add(new Vector2(p.x/8,p.z/8));
            for(int ring=1;ring<=rings;ring++)for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides,r=radius*ring/rings;var point=Point(p.x+Mathf.Cos(a)*r,p.z+Mathf.Sin(a)*r);v.Add(point);uv.Add(new Vector2(point.x/8,point.z/8));int current=1+(ring-1)*sides+i,next=1+(ring-1)*sides+(i+1)%sides;if(ring==1)t.AddRange(new[]{0,next,current});else{int inner=current-sides,innerNext=next-sides;t.AddRange(new[]{inner,innerNext,current,innerNext,next,current});}}
            return new Data{Vertices=v.ToArray(),Triangles=t.ToArray(),UV=uv.ToArray()};
        }
        public static IEnumerable<int> LegacyStrip(GeoRoad road,Func<float,float,float> surface,Action<Data> finish,CancellationToken token)
        {
            long wanted=0;for(int i=1;i<road.points.Length;i++)wanted+=(long)Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(road.points[i-1],road.points[i])/2.5f))*Mathf.Max(2,Mathf.CeilToInt(road.width/2))*4;if(wanted>MaximumVertices)throw new InvalidDataException("Legacy strip preallocation budget");
            var v=new List<Vector3>((int)wanted);var t=new List<int>((int)wanted/4*6);var uv=new List<Vector2>((int)wanted);var source=road.points;
            for(int i=0;i<source.Length-1;i++)
            {
                var a=source[i];var b=source[i+1];var dir=(b-a).normalized;var before=i>0?(source[i]-source[i-1]).normalized:dir;var after=i+2<source.Length?(source[i+2]-source[i+1]).normalized:dir;var r0=Vector3.Cross(Vector3.up,(before+dir).normalized).normalized;var r1=Vector3.Cross(Vector3.up,(dir+after).normalized).normalized;r0/=Mathf.Max(.6f,Vector3.Dot(r0,Vector3.Cross(Vector3.up,dir).normalized));r1/=Mathf.Max(.6f,Vector3.Dot(r1,Vector3.Cross(Vector3.up,dir).normalized));if(Vector3.Angle(before,dir)>60)r0=Vector3.Cross(Vector3.up,dir).normalized;if(Vector3.Angle(dir,after)>60)r1=Vector3.Cross(Vector3.up,dir).normalized;int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/2.5f));
                for(int j=0;j<steps;j++){token.ThrowIfCancellationRequested();var p=Vector3.Lerp(a,b,j/(float)steps);var q=Vector3.Lerp(a,b,(j+1)/(float)steps);var rp=Vector3.Lerp(r0,r1,j/(float)steps);var rq=Vector3.Lerp(r0,r1,(j+1)/(float)steps);int across=Mathf.Max(2,Mathf.CeilToInt(road.width/2));Vector3 S(Vector3 x){x.y=surface(x.x,x.z);return x;}for(int col=0;col<across;col++){float left=road.width*(col/(float)across-.5f),right=road.width*((col+1)/(float)across-.5f);int n=v.Count;var points=new[]{S(p+rp*left),S(p+rp*right),S(q+rq*left),S(q+rq*right)};v.AddRange(points);t.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});foreach(var x in points)uv.Add(new Vector2(x.x/8,x.z/8));}yield return 0;}
            }
            finish(new Data{Vertices=v.ToArray(),Triangles=t.ToArray(),UV=uv.ToArray()});
        }
        public static IEnumerable<int> Prepare(GeoRegion region,FinalizedRoadSurfacePlan finalized,Action<RenderedGroundGeometry> finish,CancellationToken token)
        {
            if(region==null||finalized==null||finalized.Owner!=region||finalized.WidthField!=region.PreparedRoadWidths)throw new InvalidDataException("Ground descriptor ownership");var result=new RenderedGroundGeometry{Owner=region,Field=region.PreparedRoadWidths};long vertices=0,indices=0;
            void Add(Record r){if((vertices+=r.Mesh.Vertices.Length)>MaximumVertices||(indices+=r.Mesh.Triangles.Length)>MaximumIndices)throw new InvalidDataException("Ground descriptor global array budget");result.records.Add(r);}
            foreach(var road in region.roads)if(!road.bridge&&!road.tunnel){token.ThrowIfCancellationRequested();Data data=null;if(finalized.Parts.TryGetValue(road.id,out var part))data=new Data{Vertices=part.Surface.Vertices,Triangles=part.Surface.Triangles,UV=part.Surface.UV};else foreach(var step in LegacyStrip(road,region.RoadSurfaceHeight,x=>data=x,token))yield return step;Add(new Record{Id=road.id,Kind=part==null?"legacy":"changed",Layer=road.layer,Mesh=data});yield return 0;}
            // Match actual per-chunk cap ownership, including rare same-node points
            // on opposite sides of a chunk boundary. No global centroid inference.
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var road in region.roads)if(!road.bridge&&!road.tunnel)
            {
                foreach(int end in new[]{0,1}){var p=end==0?road.points[0]:road.points.Last();string node=end==0?road.start:road.end;var owner=Cell(p);string key=road.layer+"|"+node+"|"+owner.x+"|"+owner.y;if(seen.Add(key)){float radius=region.JunctionRadius(node,region.EffectiveWidth(road,end==0?0:road.length)*.5f,road.layer);Add(new Record{Id="cap/"+key,Kind="cap",Layer=road.layer,OwnerCell=owner,Mesh=Cap(p,radius,region.RoadSurfaceHeight)});}token.ThrowIfCancellationRequested();yield return 0;}
                if(finalized.Parts.TryGetValue(road.id,out var part)){foreach(var c in part.Surface.Corners)if(c.Distance>.001&&c.Distance<road.length-.001){Add(new Record{Id="corner/"+road.id+"/"+c.Distance.ToString("R",System.Globalization.CultureInfo.InvariantCulture),Kind="cap",Layer=road.layer,OwnerCell=Cell(c.Centre),Mesh=Cap(c.Centre,c.Radius,region.RoadSurfaceHeight)});token.ThrowIfCancellationRequested();yield return 0;}}
                else for(int i=1;i<road.points.Length-1;i++)if(Vector3.Angle(road.points[i]-road.points[i-1],road.points[i+1]-road.points[i])>60){Add(new Record{Id="corner/"+road.id+"/"+i,Kind="cap",Layer=road.layer,OwnerCell=Cell(road.points[i]),Mesh=Cap(road.points[i],region.EffectiveWidthNear(road,road.points[i])*.5f,region.RoadSurfaceHeight)});token.ThrowIfCancellationRequested();yield return 0;}
            }
            token.ThrowIfCancellationRequested();finish(result);
        }
    }
}
