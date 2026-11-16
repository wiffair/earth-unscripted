using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace RoadRush
{
    // Cosmetic only. Prepared dash endpoints/phase are authoritative inputs to this renderer.
    public static class RoadMarkingChunkGeometry
    {
        public const int MaximumVertices=8192,MaximumStrokes=250000,MaximumIndexEntries=1000000,MaximumStrokesPerSlice=8;
        public const float PaintLift=.025f,MaximumSampleLength=2.5f;
        public sealed class MeshData {public Vector3[] Vertices;public int[] Triangles;public Vector2[] UV;}
        internal sealed class Reference {public RoadMarkingPlan.Stroke Stroke;public GeoRoad Road;}
        public sealed class Prepared
        {
            internal readonly Dictionary<Vector2Int,List<Reference>> cells;public readonly float ChunkSize;public readonly int StrokeCount,IndexEntries;
            public long StrokesConsidered,RejectedStrokes,Uploads;public string LastError{get;private set;}
            public int CandidateCount(Vector2Int key)=>cells.TryGetValue(key,out var values)?values.Count:0;
            public Prepared(RoadMarkingPlan plan,IEnumerable<GeoRoad> roads,float chunkSize)
            {
                if(plan==null||roads==null||!Finite(chunkSize)||chunkSize<.5f||chunkSize>4096)throw new InvalidDataException("Invalid road-marking renderer preparation");if(plan.Strokes.Count>MaximumStrokes)throw new InvalidDataException("Road-marking renderer stroke budget");
                var byId=new Dictionary<string,GeoRoad>(StringComparer.Ordinal);foreach(var road in roads){if(road==null||string.IsNullOrEmpty(road.id)||byId.Count>=10000||byId.ContainsKey(road.id))throw new InvalidDataException("Invalid road-marking source identity");byId.Add(road.id,road);}
                var built=new Dictionary<Vector2Int,List<Reference>>();int count=0;foreach(var stroke in plan.Strokes)
                {
                    if(stroke==null||stroke.RoadId==null||!byId.TryGetValue(stroke.RoadId,out var road)||road.tunnel||string.IsNullOrEmpty(stroke.ChainId)||!Finite(stroke.A)||!Finite(stroke.B)||!Finite(stroke.RightA)||!Finite(stroke.RightB)||!Finite(stroke.ArcStart)||!Finite(stroke.ArcEnd)||stroke.ArcStart<0||stroke.ArcEnd<=stroke.ArcStart||stroke.ArcEnd>2000000||stroke.ArcEnd-stroke.ArcStart>32||stroke.RightA.sqrMagnitude<.25f||stroke.RightB.sqrMagnitude<.25f||stroke.RightA.sqrMagnitude>4||stroke.RightB.sqrMagnitude>4||Vector3.Distance(stroke.A,stroke.B)>32)throw new InvalidDataException("Invalid road-marking stroke/frame");
                    float west=Mathf.Min(stroke.LeftA.x,stroke.EdgeA.x,stroke.LeftB.x,stroke.EdgeB.x),east=Mathf.Max(stroke.LeftA.x,stroke.EdgeA.x,stroke.LeftB.x,stroke.EdgeB.x),south=Mathf.Min(stroke.LeftA.z,stroke.EdgeA.z,stroke.LeftB.z,stroke.EdgeB.z),north=Mathf.Max(stroke.LeftA.z,stroke.EdgeA.z,stroke.LeftB.z,stroke.EdgeB.z);
                    if(Mathf.Max(Mathf.Abs(west),Mathf.Abs(east),Mathf.Abs(south),Mathf.Abs(north))>100000)throw new InvalidDataException("Road-marking coordinate budget");int ax=Mathf.FloorToInt(west/chunkSize),bx=Mathf.FloorToInt(east/chunkSize),az=Mathf.FloorToInt(south/chunkSize),bz=Mathf.FloorToInt(north/chunkSize);long added=(long)(bx-ax+1)*(bz-az+1);if(added>MaximumIndexEntries-count)throw new InvalidDataException("Road-marking index budget");count+=(int)added;
                    var frozen=new RoadMarkingPlan.Stroke{RoadId=stroke.RoadId,ChainId=stroke.ChainId,A=stroke.A,B=stroke.B,RightA=stroke.RightA,RightB=stroke.RightB,ArcStart=stroke.ArcStart,ArcEnd=stroke.ArcEnd};var item=new Reference{Road=road,Stroke=frozen};for(int z=az;z<=bz;z++)for(int x=ax;x<=bx;x++){var key=new Vector2Int(x,z);if(!built.TryGetValue(key,out var list))built[key]=list=new List<Reference>();list.Add(item);}
                }
                cells=built;ChunkSize=chunkSize;StrokeCount=plan.Strokes.Count;IndexEntries=count;
            }
            internal void Failure(Exception error){RejectedStrokes++;LastError=(error.Message??"Road-marking sampling failed");if(LastError.Length>240)LastError=LastError.Substring(0,240);}
        }
        public static bool TryPrepare(RoadMarkingPlan plan,IEnumerable<GeoRoad> roads,float chunkSize,out Prepared prepared,out string error)
        {
            try{prepared=new Prepared(plan,roads,chunkSize);error=null;return true;}
            catch(Exception e)when(e is InvalidDataException||e is ArgumentException||e is OverflowException){prepared=null;error=e.Message;return false;}
        }
        readonly struct Vertex
        {
            public readonly Vector3 Position;public readonly Vector2 UV;public Vertex(Vector3 p){Position=p;UV=new Vector2(p.x/8,p.z/8);}Vertex(Vector3 p,Vector2 uv){Position=p;UV=uv;}
            public static Vertex Mix(Vertex a,Vertex b,float t,int plane,float boundary){var p=Vector3.LerpUnclamped(a.Position,b.Position,t);if(plane<2)p.x=boundary;else p.z=boundary;return new Vertex(p,Vector2.LerpUnclamped(a.UV,b.UV,t));}
        }
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);static bool Finite(Vector3 p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z);
        static List<Vertex> Clip(Vertex a,Vertex b,Vertex c,Vector2Int key,float size)
        {
            if(Vector3.Cross(b.Position-a.Position,c.Position-a.Position).y<0){var swap=b;b=c;c=swap;}var polygon=new List<Vertex>(8){a,b,c};float west=key.x*size,east=west+size,south=key.y*size,north=south+size;
            for(int plane=0;plane<4&&polygon.Count>0;plane++)
            {
                float boundary=plane==0?west:plane==1?east:plane==2?south:north;float Signed(Vector3 p)=>plane==0?p.x-west:plane==1?east-p.x:plane==2?p.z-south:north-p.z;
                var output=new List<Vertex>(8);var previous=polygon[polygon.Count-1];float pd=Signed(previous.Position);foreach(var next in polygon){float nd=Signed(next.Position);if((pd>=0)!=(nd>=0))output.Add(Vertex.Mix(previous,next,pd/(pd-nd),plane,boundary));if(nd>=0)output.Add(next);previous=next;pd=nd;}polygon=output;
            }
            for(int i=polygon.Count-1;i>=0&&polygon.Count>1;i--)if((polygon[i].Position-polygon[(i+1)%polygon.Count].Position).sqrMagnitude<.0000000001f)polygon.RemoveAt(i);
            if(polygon.Count<3)return null;float area=0;for(int i=1;i<polygon.Count-1;i++)area+=Vector3.Cross(polygon[i].Position-polygon[0].Position,polygon[i+1].Position-polygon[0].Position).y;if(area<=.00000001f)return null;if(polygon.Count>8)throw new InvalidDataException("Road-marking clip budget");return polygon;
        }
        static List<Vertex[]> StrokeTriangles(Reference item,Func<float,float,float> roadSurface,Func<GeoRoad,float,float,float> contextualRoadSurface)
        {
            var s=item.Stroke;int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(s.A,s.B)/MaximumSampleLength));if(steps>13)throw new InvalidDataException("Road-marking sample budget");var result=new List<Vertex[]>(steps*2);
            Vertex Point(Vector3 point){point.y=(contextualRoadSurface!=null?contextualRoadSurface(item.Road,point.x,point.z):item.Road.bridge?point.y:roadSurface(point.x,point.z))+PaintLift;if(!Finite(point))throw new InvalidDataException("Nonfinite road-marking surface");return new Vertex(point);}
            for(int j=0;j<steps;j++)
            {
                float t=j/(float)steps,u=(j+1)/(float)steps;var a=Vector3.LerpUnclamped(s.A,s.B,t);var b=Vector3.LerpUnclamped(s.A,s.B,u);var ra=Vector3.LerpUnclamped(s.RightA,s.RightB,t)*.065f;var rb=Vector3.LerpUnclamped(s.RightA,s.RightB,u)*.065f;var leftA=Point(a-ra);var rightA=Point(a+ra);var leftB=Point(b-rb);var rightB=Point(b+rb);result.Add(new[]{leftA,leftB,rightA});result.Add(new[]{rightA,leftB,rightB});
            }
            return result;
        }
        // Null entries are cooperative checkpoints. Mesh entries are complete owned upload batches.
        // Ground samples happen before clipping; clipping interpolates the original triangle plane.
        public static IEnumerable<MeshData> PrepareBatches(Prepared prepared,Vector2Int key,Func<float,float,float> roadSurface,Func<GeoRoad,float,float,float> contextualRoadSurface=null)
        {
            if(prepared==null||roadSurface==null)yield break;if(!prepared.cells.TryGetValue(key,out var local))yield break;
            var vertices=new List<Vector3>(MaximumVertices);var triangles=new List<int>(MaximumVertices*2);var uv=new List<Vector2>(MaximumVertices);int considered=0;
            MeshData Flush(){var data=new MeshData{Vertices=vertices.ToArray(),Triangles=triangles.ToArray(),UV=uv.ToArray()};vertices.Clear();triangles.Clear();uv.Clear();return data;}
            foreach(var item in local)
            {
                prepared.StrokesConsidered++;List<Vertex[]> source=null;try{source=StrokeTriangles(item,roadSurface,contextualRoadSurface);}catch(Exception e)when(e is InvalidDataException||e is ArgumentException||e is InvalidOperationException||e is ArithmeticException){prepared.Failure(e);}
                if(source!=null)foreach(var tri in source)
                {
                    var polygon=Clip(tri[0],tri[1],tri[2],key,prepared.ChunkSize);if(polygon==null)continue;if(vertices.Count+polygon.Count>MaximumVertices)yield return Flush();int offset=vertices.Count;foreach(var point in polygon){vertices.Add(point.Position);uv.Add(point.UV);}for(int i=1;i<polygon.Count-1;i++){triangles.Add(offset);triangles.Add(offset+i);triangles.Add(offset+i+1);}
                }
                if(++considered%MaximumStrokesPerSlice==0)yield return null;
            }
            if(vertices.Count>0)yield return Flush();
        }
        public static IEnumerable<int> Build(Transform root,Prepared prepared,Vector2Int key,Material sharedWhite,Func<float,float,float> roadSurface,Func<GeoRoad,float,float,float> contextualRoadSurface=null)
        {
            if(!root||prepared==null||!sharedWhite)yield break;int serial=0;foreach(var batch in PrepareBatches(prepared,key,roadSurface,contextualRoadSurface))
            {
                if(batch!=null){Geometry.Mesh("Mapped chain road markings "+serial++,root,batch.Vertices,batch.Triangles,batch.UV,sharedWhite,false);prepared.Uploads++;}yield return 0;
            }
        }
    }
}
