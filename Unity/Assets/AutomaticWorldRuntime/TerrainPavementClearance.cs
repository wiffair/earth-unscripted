using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        public readonly struct PavementTriangleRef
        {public readonly int vertex;public readonly bool ground;public PavementTriangleRef(int start,bool ownsGround){vertex=start;ground=ownsGround;}}
        public sealed class TerrainPavementResult
        {
            public readonly List<Vector3> addedVertices=new List<Vector3>();public readonly List<int> indices=new List<int>();
            public int changedTriangles,roadTriangles;public double milliseconds;
        }
        readonly struct TerrainCutPlane
        {
            readonly Vector3 normal,anchor;readonly float offset;
            public TerrainCutPlane(Vector3 direction,Vector3 origin,float distance=0){normal=direction;anchor=origin;offset=distance;}
            public float Distance(Vector3 point)=>Vector3.Dot(normal,point-anchor)+offset;
        }
        readonly struct TerrainRoadTriangle
        {
            public readonly Vector3 a,b,c;public readonly float xSlope,zSlope,minX,maxX,minZ,maxZ;public readonly bool ground;
            public TerrainRoadTriangle(Vector3 first,Vector3 second,Vector3 third,bool ownsGround)
            {
                a=first;b=second;c=third;ground=ownsGround;var n=Vector3.Cross(b-a,c-a);xSlope=-n.x/n.y;zSlope=-n.z/n.y;
                minX=Mathf.Min(a.x,Mathf.Min(b.x,c.x));maxX=Mathf.Max(a.x,Mathf.Max(b.x,c.x));minZ=Mathf.Min(a.z,Mathf.Min(b.z,c.z));maxZ=Mathf.Max(a.z,Mathf.Max(b.z,c.z));
            }
            public float Height(Vector3 point)=>a.y+xSlope*(point.x-a.x)+zSlope*(point.z-a.z);
            public TerrainCutPlane HeightCut(float offset)=>new TerrainCutPlane(new Vector3(-xSlope,1,-zSlope),a,offset);
            public TerrainCutPlane Edge(int index)
            {
                var first=index==0?a:index==1?b:c;var last=index==0?b:index==1?c:a;
                float cross=(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x),sign=cross>=0?1:-1;
                return new TerrainCutPlane(new Vector3(-(last.z-first.z)*sign,0,(last.x-first.x)*sign),first);
            }
            public bool Overlaps(Vector3 first,Vector3 second,Vector3 third)
                =>Mathf.Min(first.x,Mathf.Min(second.x,third.x))<=maxX&&Mathf.Max(first.x,Mathf.Max(second.x,third.x))>=minX&&
                  Mathf.Min(first.z,Mathf.Min(second.z,third.z))<=maxZ&&Mathf.Max(first.z,Mathf.Max(second.z,third.z))>=minZ;
            public bool Overlaps(List<Vector3> polygon)
            {
                float left=float.PositiveInfinity,right=float.NegativeInfinity,south=float.PositiveInfinity,north=float.NegativeInfinity;
                foreach(var point in polygon){left=Mathf.Min(left,point.x);right=Mathf.Max(right,point.x);south=Mathf.Min(south,point.z);north=Mathf.Max(north,point.z);}
                return left<=maxX&&right>=minX&&south<=maxZ&&north>=minZ;
            }
        }
        static bool TerrainPolygonHasArea(List<Vector3> polygon)
        {
            if(polygon==null||polygon.Count<3)return false;var origin=polygon[0];double twice=0;
            for(int i=1;i+1<polygon.Count;i++)twice+=(double)(polygon[i].x-origin.x)*(polygon[i+1].z-origin.z)-(double)(polygon[i].z-origin.z)*(polygon[i+1].x-origin.x);
            return Math.Abs(twice)>.0000001;
        }
        static void SplitTerrainPolygon(List<Vector3> input,TerrainCutPlane plane,out List<Vector3> inside,out List<Vector3> outside)
        {
            inside=new List<Vector3>(input.Count+1);outside=new List<Vector3>(input.Count+1);var previous=input[input.Count-1];float before=plane.Distance(previous);
            foreach(var point in input)
            {
                float after=plane.Distance(point);bool p=before>=0,q=after>=0;
                if(p!=q){var crossing=Vector3.Lerp(previous,point,Mathf.Clamp01(before/(before-after)));inside.Add(crossing);outside.Add(crossing);}
                if(q)inside.Add(point);else outside.Add(point);previous=point;before=after;
            }
        }
        static List<Vector3> InsideTerrainRoad(List<Vector3> polygon,TerrainRoadTriangle road,List<List<Vector3>> outside)
        {
            var inner=polygon;
            for(int edge=0;edge<3;edge++)
            {
                SplitTerrainPolygon(inner,road.Edge(edge),out var kept,out var removed);if(TerrainPolygonHasArea(removed))outside.Add(removed);
                if(!TerrainPolygonHasArea(kept))return null;inner=kept;
            }
            return inner;
        }
        static List<List<Vector3>> SupportGroundRoadPolygon(List<Vector3> polygon,TerrainRoadTriangle road)
        {
            if(!road.ground||!road.Overlaps(polygon))return null;
            float deficit=float.NegativeInfinity;foreach(var point in polygon)deficit=Mathf.Max(deficit,road.Height(point)-point.y);
            if(deficit<=.45f)return null;
            var output=new List<List<Vector3>>();var inner=InsideTerrainRoad(polygon,road,output);if(inner==null)return null;
            float overlapDeficit=float.NegativeInfinity;foreach(var point in inner)overlapDeficit=Mathf.Max(overlapDeficit,road.Height(point)-point.y);
            if(overlapDeficit<=.45f)return null;
            // Portal excavation operates on coarse terrain cells. Restore only
            // actual emitted ground-road footprints where that cut (or a nearby
            // lower apron) removed their embankment. Bridge spans and tunnel
            // floors never acquire this authority. Split on the target plane so
            // existing safe soil and the unpaved island/verge remain unchanged.
            SplitTerrainPolygon(inner,road.HeightCut(.20f),out var alreadySupported,out var low);
            if(TerrainPolygonHasArea(alreadySupported))output.Add(alreadySupported);
            if(!TerrainPolygonHasArea(low))return null;
            for(int i=0;i<low.Count;i++){var point=low[i];point.y=Mathf.Max(point.y,road.Height(point)-.20f);low[i]=point;}
            output.Add(low);return output;
        }
        static List<List<Vector3>> CutTerrainPolygon(List<Vector3> polygon,TerrainRoadTriangle road,List<TerrainRoadTriangle> all,List<int> neighbours,out bool changed)
        {
            const float recess=.12f,groundSupport=.65f;changed=false;
            if(!road.Overlaps(polygon))return null;
            float excess=float.NegativeInfinity;foreach(var point in polygon)excess=Mathf.Max(excess,point.y-road.Height(point));
            if(excess<=-.02f)return null;
            var output=new List<List<Vector3>>();var inner=InsideTerrainRoad(polygon,road,output);
            if(inner==null)return null;
            float overlapExcess=float.NegativeInfinity;foreach(var point in inner)overlapExcess=Mathf.Max(overlapExcess,point.y-road.Height(point));
            if(overlapExcess<=-.02f)return null;
            var eligible=new List<List<Vector3>>{inner};
            // Preserve soil supporting an actual higher GROUND-road footprint.
            // Upper bridge spans do not own soil underneath them. This uses
            // emitted affine planes instead of a height cutoff that would leave
            // genuinely high erroneous grass standing through a bridge landing.
            foreach(int index in neighbours)
            {
                var upper=all[index];if(!upper.ground||!upper.Overlaps(polygon))continue;
                var aboveRoad=new TerrainCutPlane(new Vector3(upper.xSlope-road.xSlope,0,upper.zSlope-road.zSlope),road.a,upper.Height(road.a)-road.a.y-groundSupport);
                float possible=float.NegativeInfinity;foreach(var point in polygon)possible=Mathf.Max(possible,aboveRoad.Distance(point));if(possible<=0)continue;
                var next=new List<List<Vector3>>();
                foreach(var piece in eligible)
                {
                    if(!upper.Overlaps(piece)){next.Add(piece);continue;}
                    float separation=float.NegativeInfinity;foreach(var point in piece)separation=Mathf.Max(separation,aboveRoad.Distance(point));
                    if(separation<=0){next.Add(piece);continue;}
                    var notProtected=new List<List<Vector3>>();var protectedPart=InsideTerrainRoad(piece,upper,notProtected);
                    if(protectedPart==null){next.Add(piece);continue;}
                    SplitTerrainPolygon(protectedPart,aboveRoad,out var separated,out var nearLevel);if(TerrainPolygonHasArea(nearLevel))notProtected.Add(nearLevel);
                    if(TerrainPolygonHasArea(separated))
                    {
                        SplitTerrainPolygon(separated,upper.HeightCut(groundSupport),out var supported,out var belowSupport);
                        if(TerrainPolygonHasArea(supported))output.Add(supported);if(TerrainPolygonHasArea(belowSupport))notProtected.Add(belowSupport);
                    }
                    next.AddRange(notProtected);
                }
                eligible=next;if(eligible.Count==0)break;
            }
            foreach(var piece in eligible)
            {
                float pieceExcess=float.NegativeInfinity;foreach(var point in piece)pieceExcess=Mathf.Max(pieceExcess,point.y-road.Height(point));
                if(pieceExcess<=-.02f){output.Add(piece);continue;}
                SplitTerrainPolygon(piece,road.HeightCut(recess),out var protrusion,out var alreadyClear);
                if(TerrainPolygonHasArea(alreadyClear))output.Add(alreadyClear);
                if(!TerrainPolygonHasArea(protrusion))continue;
                for(int i=0;i<protrusion.Count;i++){var point=protrusion[i];point.y=Mathf.Min(point.y,road.Height(point)-recess);protrusion[i]=point;}
                output.Add(protrusion);changed=true;
            }
            return changed?output:null;
        }
        public static TerrainPavementResult ConstrainTerrainPavement(IReadOnlyList<Vector3> terrainVertices,IReadOnlyList<int> terrainIndices,IReadOnlyList<Vector3> roadVertices,IReadOnlyList<PavementTriangleRef> roadTop)
        {
            var watch=Stopwatch.StartNew();var result=new TerrainPavementResult();var roads=new List<TerrainRoadTriangle>();var grid=new Dictionary<Vector2Int,List<int>>();const float cellSize=4;
            foreach(var reference in roadTop)
            {
                var a=roadVertices[reference.vertex];var b=roadVertices[reference.vertex+1];var c=roadVertices[reference.vertex+2];if(Mathf.Abs(Vector3.Cross(b-a,c-a).y)<.000001f)continue;
                var triangle=new TerrainRoadTriangle(a,b,c,reference.ground);int index=roads.Count;roads.Add(triangle);
                for(int z=Mathf.FloorToInt(triangle.minZ/cellSize);z<=Mathf.FloorToInt(triangle.maxZ/cellSize);z++)for(int x=Mathf.FloorToInt(triangle.minX/cellSize);x<=Mathf.FloorToInt(triangle.maxX/cellSize);x++)
                {var key=new Vector2Int(x,z);if(!grid.TryGetValue(key,out var list))grid[key]=list=new List<int>();list.Add(index);}
            }
            result.roadTriangles=roads.Count;var seen=new HashSet<int>();var neighbours=new List<int>();
            for(int i=0;i+2<terrainIndices.Count;i+=3)
            {
                int ia=terrainIndices[i],ib=terrainIndices[i+1],ic=terrainIndices[i+2];var a=terrainVertices[ia];var b=terrainVertices[ib];var c=terrainVertices[ic];seen.Clear();neighbours.Clear();
                for(int z=Mathf.FloorToInt(Mathf.Min(a.z,Mathf.Min(b.z,c.z))/cellSize);z<=Mathf.FloorToInt(Mathf.Max(a.z,Mathf.Max(b.z,c.z))/cellSize);z++)for(int x=Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))/cellSize);x<=Mathf.FloorToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))/cellSize);x++)
                    if(grid.TryGetValue(new Vector2Int(x,z),out var list))foreach(int index in list)if(seen.Add(index))neighbours.Add(index);
                List<List<Vector3>> pieces=null;bool changed=false;
                // Establish ground support first, before the existing exact
                // clearance pass protects upper ground roads from lower spans.
                // Most triangles need neither pass to allocate any polygons.
                foreach(int index in neighbours)
                {
                    var road=roads[index];if(!road.ground||!road.Overlaps(a,b,c))continue;
                    if(pieces==null&&Mathf.Max(road.Height(a)-a.y,Mathf.Max(road.Height(b)-b.y,road.Height(c)-c.y))<=.45f)continue;
                    if(pieces==null)pieces=new List<List<Vector3>>{new List<Vector3>{a,b,c}};
                    var supported=new List<List<Vector3>>();
                    foreach(var piece in pieces){var fill=SupportGroundRoadPolygon(piece,road);if(fill!=null){supported.AddRange(fill);changed=true;}else supported.Add(piece);}pieces=supported;
                }
                foreach(int index in neighbours)
                {
                    var road=roads[index];if(!road.Overlaps(a,b,c))continue;
                    // Most terrain is already below its roads. Avoid allocating
                    // polygon lists or changing topology for that common case.
                    if(pieces==null&&Mathf.Max(a.y-road.Height(a),Mathf.Max(b.y-road.Height(b),c.y-road.Height(c)))<=-.02f)continue;
                    if(pieces==null)pieces=new List<List<Vector3>>{new List<Vector3>{a,b,c}};
                    var next=new List<List<Vector3>>();foreach(var piece in pieces){var cut=CutTerrainPolygon(piece,road,roads,neighbours,out bool altered);if(altered){next.AddRange(cut);changed=true;}else next.Add(piece);}pieces=next;
                }
                if(!changed){result.indices.Add(ia);result.indices.Add(ib);result.indices.Add(ic);continue;}
                result.changedTriangles++;
                foreach(var piece in pieces)for(int j=1;j+1<piece.Count;j++)
                {
                    if(Mathf.Abs(Vector3.Cross(piece[j]-piece[0],piece[j+1]-piece[0]).y)<.0000001f)continue;
                    int first=terrainVertices.Count+result.addedVertices.Count;result.addedVertices.Add(piece[0]);result.addedVertices.Add(piece[j]);result.addedVertices.Add(piece[j+1]);result.indices.Add(first);result.indices.Add(first+1);result.indices.Add(first+2);
                }
            }
            watch.Stop();result.milliseconds=watch.Elapsed.TotalMilliseconds;return result;
        }
        public int ClearedTerrainPavementTriangles{get;private set;}public double MaximumTerrainPavementWorkerMilliseconds{get;private set;}
        IEnumerator ClearTerrainPavement(Batch terrain,Batch roads)
        {
            if(roads.TerrainRoads.Count==0)yield break;
            // One immutable numeric job: no mesh, physics, scene or material API
            // runs on the worker. Its result reaches both visual and collider
            // publication before this chunk becomes visible or reusable.
            var preparing=EarthUnscripted.MacWorkPriority.RunUrgent(()=>ConstrainTerrainPavement(terrain.Vertices,terrain.Indices,roads.Vertices,roads.TerrainRoads),"terrain.pavement-clearance");
            while(!preparing.IsCompleted)yield return null;var result=preparing.GetAwaiter().GetResult();
            ClearedTerrainPavementTriangles+=result.changedTriangles;MaximumTerrainPavementWorkerMilliseconds=Math.Max(MaximumTerrainPavementWorkerMilliseconds,result.milliseconds);
            if(result.changedTriangles>0)terrain.ApplyTerrainPavement(result);
        }
    }
}
