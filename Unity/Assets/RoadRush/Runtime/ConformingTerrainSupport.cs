using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading;using UnityEngine;
namespace RoadRush
{
    public sealed partial class ConformingTerrainSupport
    {
        public const int MaximumTriangles=2000000,MaximumIndexEntries=8000000,MaximumLocalCandidates=4096,MaximumChunkTriangles=200000;
        public sealed class Batch{public Vector3[] Vertices;public int[] Triangles;public Vector2[] UV;public bool Bed;}
        struct Triangle{public Vector3 A,B,C;public Vector2Int? OwnerCell;public float X0,Z0,X1,Z1;public RoadBankExteriorClip.Footprint Footprint;}
        readonly List<Triangle> triangles=new List<Triangle>();readonly Dictionary<(int,int),List<int>> cells=new Dictionary<(int,int),List<int>>();readonly Dictionary<Vector2Int,List<int>> chunks=new Dictionary<Vector2Int,List<int>>();
        public RenderedGroundGeometry Geometry{get;private set;}public int TriangleCount=>triangles.Count;public IEnumerable<Vector2Int> IndexedChunks=>chunks.Keys;public int ChunkTriangleCount(Vector2Int key)=>chunks.TryGetValue(key,out var list)?list.Count:0;public int IndexEntries{get;private set;}public int MaximumCandidatesSeen{get;private set;}
        static int Cell(float x)=>(int)Math.Floor(x/8);static int Chunk(float x)=>(int)Math.Floor(x/320);
        public static IEnumerable<int> Prepare(RenderedGroundGeometry geometry,Action<ConformingTerrainSupport> finish,CancellationToken token)
        {
            if(geometry==null)throw new ArgumentNullException(nameof(geometry));var result=new ConformingTerrainSupport{Geometry=geometry};int work=0;long refs=0,chunkRefs=0;
            foreach(var record in geometry.Records){var d=record.Mesh;for(int i=0;i<d.Triangles.Length;i+=3){token.ThrowIfCancellationRequested();var a=d.Vertices[d.Triangles[i]];var b=d.Vertices[d.Triangles[i+1]];var c=d.Vertices[d.Triangles[i+2]];
                // Do not flip legacy backfaces: preserve what downward physics
                // rays can actually hit. Changed strips already validate winding.
                if(Vector3.Cross(b-a,c-a).y<=1e-8f)continue;if(result.triangles.Count>=MaximumTriangles)throw new InvalidDataException("Ground support triangle budget");
                var t=new Triangle{A=a,B=b,C=c,OwnerCell=record.OwnerCell,X0=Mathf.Min(a.x,b.x,c.x),X1=Mathf.Max(a.x,b.x,c.x),Z0=Mathf.Min(a.z,b.z,c.z),Z1=Mathf.Max(a.z,b.z,c.z),Footprint=new RoadBankExteriorClip.Footprint{Id=record.Id,Polygon=new[]{new Vector2(a.x,a.z),new Vector2(b.x,b.z),new Vector2(c.x,c.z)}}};int id=result.triangles.Count;result.triangles.Add(t);
                long count=(long)(Cell(t.X1)-Cell(t.X0)+1)*(Cell(t.Z1)-Cell(t.Z0)+1);if(count>4096||(refs+=count)>MaximumIndexEntries)throw new InvalidDataException("Ground support spatial index budget");for(int z=Cell(t.Z0);z<=Cell(t.Z1);z++)for(int x=Cell(t.X0);x<=Cell(t.X1);x++){if(!result.cells.TryGetValue((x,z),out var list))result.cells[(x,z)]=list=new List<int>();list.Add(id);}
                count=(long)(Chunk(t.X1)-Chunk(t.X0)+1)*(Chunk(t.Z1)-Chunk(t.Z0)+1);if(count>64||(chunkRefs+=count)>4000000)throw new InvalidDataException("Ground support chunk index budget");for(int z=Chunk(t.Z0);z<=Chunk(t.Z1);z++)for(int x=Chunk(t.X0);x<=Chunk(t.X1);x++){var key=new Vector2Int(x,z);if(!result.chunks.TryGetValue(key,out var list))result.chunks[key]=list=new List<int>();if(list.Count>=MaximumChunkTriangles)throw new InvalidDataException("Ground support per-chunk budget");list.Add(id);}if(++work%64==0)yield return 0;
            }}result.IndexEntries=(int)refs;token.ThrowIfCancellationRequested();finish(result);
        }
        List<int> Query(float x0,float z0,float x1,float z1)
        {
            if((long)(Cell(x1)-Cell(x0)+1)*(Cell(z1)-Cell(z0)+1)>4096)throw new InvalidDataException("Terrain query span budget");var ids=new HashSet<int>();for(int z=Cell(z0);z<=Cell(z1);z++)for(int x=Cell(x0);x<=Cell(x1);x++)if(cells.TryGetValue((x,z),out var list))foreach(int id in list){var t=triangles[id];if(t.X1<x0||t.X0>x1||t.Z1<z0||t.Z0>z1)continue;ids.Add(id);if(ids.Count>MaximumLocalCandidates)throw new InvalidDataException("Terrain local pavement budget");}MaximumCandidatesSeen=Math.Max(MaximumCandidatesSeen,ids.Count);return ids.OrderBy(i=>i).ToList();
        }
        public static bool Plane(Vector3 p,Vector3 a,Vector3 b,Vector3 c,out float height)
        {
            double den=((double)b.z-c.z)*(a.x-c.x)+((double)c.x-b.x)*(a.z-c.z);if(Math.Abs(den)<1e-12){height=0;return false;}double u=(((double)b.z-c.z)*(p.x-c.x)+((double)c.x-b.x)*(p.z-c.z))/den,v=(((double)c.z-a.z)*(p.x-c.x)+((double)a.x-c.x)*(p.z-c.z))/den,w=1-u-v;height=(float)(u*a.y+v*b.y+w*c.y);return u>=-1e-7&&v>=-1e-7&&w>=-1e-7;
        }
        public bool TryAsphalt(float x,float z,out float top)
        {top=float.MinValue;foreach(int id in Query(x,z,x,z)){var t=triangles[id];if(Plane(new Vector3(x,0,z),t.A,t.B,t.C,out float y))top=Mathf.Max(top,y);}return top!=float.MinValue;}
        public bool TryBed(float x,float z,out float bed)
        {bed=float.MinValue;foreach(int id in Query(x,z,x,z)){var t=triangles[id];if(Plane(new Vector3(x,0,z),t.A-Vector3.up*.55f,t.B-Vector3.up*.55f,t.C-Vector3.up*.55f,out float y))bed=Mathf.Max(bed,y);}return bed!=float.MinValue;}
        public float MeshHeight(float x,float z,Func<float,float,float> originalGrid)
        {return TryBed(x,z,out float bed)?bed:originalGrid(x,z);}
    }
}
