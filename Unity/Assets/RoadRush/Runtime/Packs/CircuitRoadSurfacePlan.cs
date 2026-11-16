using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadRush.Packs
{
    /// <summary>Immutable, data-only authored circuit pavement. Schema1 is a
    /// single ground layer: positive-area projected overlaps and stacked road
    /// surfaces are refused. Scenery bridges have a separate representation.
    /// Parsing/indexing creates no Unity objects and is safe on the boot worker.</summary>
    public sealed class CircuitRoadSurfacePlan
    {
        public const int MaximumBytes=32*1024*1024,MaximumAllocatedMeshVertices=3000000;
        const int MaximumTokens=3000000,MaximumMeshes=512,MaximumSourceVertices=1000000;
        const int MaximumIndexReferences=2000000,MaximumPairChecks=4000000,MaximumSeamVisits=10000000;
        const double Cell=64,SeamTolerance=.005,GeometryEpsilon=1e-9;
        // Exterior soil blends across nearby boundaries; these are construction
        // parameters, never a road-height correction or longitudinal grade cap.
        const double BoundaryBlendRadius=6,BoundaryContactInset=.004;
        const double BoundaryWeightFloorSquared=1e-12; // One micrometre squared.
        public sealed class Part
        {
            public string Id{get;private set;}
            public IReadOnlyList<string> RoadIds{get;private set;}
            public IReadOnlyList<Vector3> Vertices{get;private set;}
            public IReadOnlyList<int> Triangles{get;private set;}
            internal Part(string id,string[] roads,Vector3[] vertices,int[] triangles)
            {Id=id;RoadIds=Array.AsReadOnly(roads);Vertices=Array.AsReadOnly(vertices);Triangles=Array.AsReadOnly(triangles);}
        }
        readonly struct Triangle
        {
            public readonly Vector3 A,B,C;
            public readonly double MinX,MaxX,MinZ,MaxZ,Denominator;
            public Triangle(Vector3 a,Vector3 b,Vector3 c)
            {A=a;B=b;C=c;MinX=Math.Min(a.x,Math.Min(b.x,c.x));MaxX=Math.Max(a.x,Math.Max(b.x,c.x));MinZ=Math.Min(a.z,Math.Min(b.z,c.z));MaxZ=Math.Max(a.z,Math.Max(b.z,c.z));Denominator=Orient(a,b,c);}
            // Clip a navigation segment against this triangle in parameter
            // space. Unioning intervals proves continuous support; sparse point
            // samples could miss a short gap between two correctly named meshes.
            public bool Interval(Vector3 a,Vector3 b,out double low,out double high)
            {
                low=0;high=1;var edgeA=new[]{A,B,C};var edgeB=new[]{B,C,A};
                for(int i=0;i<3;i++)
                {
                    double start=Orient(edgeA[i],edgeB[i],a),end=Orient(edgeA[i],edgeB[i],b),delta=end-start;
                    if(Math.Abs(delta)<1e-15){if(start>GeometryEpsilon)return false;continue;}
                    double crossing=(GeometryEpsilon-start)/delta;
                    if(delta>0)high=Math.Min(high,crossing);else low=Math.Max(low,crossing);
                    if(low>high)return false;
                }
                return true;
            }
            public bool Sample(double x,double z,out float height)
            {
                height=0;if(x<MinX-GeometryEpsilon||x>MaxX+GeometryEpsilon||z<MinZ-GeometryEpsilon||z>MaxZ+GeometryEpsilon)return false;
                double u=((B.x-x)*(C.z-z)-(B.z-z)*(C.x-x))/Denominator;
                double v=((C.x-x)*(A.z-z)-(C.z-z)*(A.x-x))/Denominator,w=1-u-v;
                if(u< -1e-8||v< -1e-8||w< -1e-8)return false;
                height=(float)(u*A.y+v*B.y+w*C.y);return true;
            }
        }
        readonly struct EdgeKey:IEquatable<EdgeKey>
        {
            readonly int ax,ay,az,bx,by,bz;
            static int Bits(float value)=>BitConverter.SingleToInt32Bits(value==0?0:value);
            public EdgeKey(Vector3 a,Vector3 b)
            {
                if(a.x>b.x||a.x==b.x&&(a.y>b.y||a.y==b.y&&a.z>b.z)){var swap=a;a=b;b=swap;}
                ax=Bits(a.x);ay=Bits(a.y);az=Bits(a.z);bx=Bits(b.x);by=Bits(b.y);bz=Bits(b.z);
            }
            public bool Equals(EdgeKey v)=>ax==v.ax&&ay==v.ay&&az==v.az&&bx==v.bx&&by==v.by&&bz==v.bz;
            public override bool Equals(object value)=>value is EdgeKey key&&Equals(key);
            public override int GetHashCode(){unchecked{return (((((ax*397^ay)*397^az)*397^bx)*397^by)*397)^bz;}}
        }
        sealed class EdgeUse{public Vector3 A,B;public int Count;public double PlanLength;}
        // Composite pair keys put nearby triangle indices in opposite halves.
        // Int64's default XOR hash collapses many such pairs into the same hash
        // bucket. Avalanche both halves before folding to32bits; equality and
        // the complete comparison/work budgets stay exactly unchanged.
        sealed class TrianglePairComparer:IEqualityComparer<long>
        {
            public bool Equals(long a,long b)=>a==b;
            public int GetHashCode(long value)
            {
                unchecked
                {
                    ulong mixed=(ulong)value;
                    mixed=(mixed^(mixed>>30))*0xbf58476d1ce4e5b9UL;
                    mixed=(mixed^(mixed>>27))*0x94d049bb133111ebUL;
                    mixed^=mixed>>31;
                    return (int)(mixed^(mixed>>32));
                }
            }
        }
        readonly Triangle[] triangles;
        readonly EdgeUse[] boundaries;
        readonly Dictionary<long,int[]> triangleCells,boundaryCells;
        readonly HashSet<string> covered;
        public IReadOnlyList<Part> Parts{get;private set;}
        public string LayoutSha256{get;private set;}
        public string PayloadSha256{get;private set;}
        public int AllocatedMeshVertices{get;private set;}
        public int TriangleCount=>triangles.Length;
        public int BoundaryEdgeCount=>boundaries.Length;
        internal RoadBankExteriorClip.SpatialIndex TerrainPavement {get;private set;}
        public bool Covers(string roadId)=>covered.Contains(roadId);
        static void Check(bool condition,string reason){if(!condition)throw new InvalidDataException("Authored circuit surfaces: "+reason);}
        static double Orient(Vector3 a,Vector3 b,Vector3 c)=>((double)b.x-a.x)*((double)c.z-a.z)-((double)b.z-a.z)*((double)c.x-a.x);
        static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        static int Grid(double value)=>(int)Math.Floor(value/Cell);
        static float Number(JToken token)
        {Check(token!=null&&(token.Type==JTokenType.Float||token.Type==JTokenType.Integer),"numeric value required");float value=(float)token;Check(!float.IsNaN(value)&&!float.IsInfinity(value),"finite value required");return value;}
        static string Id(JToken token)
        {Check(token?.Type==JTokenType.String,"string identity required");string value=(string)token;Check(value.Length>0&&value.Length<=96&&char.IsLetter(value[0])&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='.'),"invalid identity");return value;}
        static Vector3 Vector(JToken token,GeoRegion region)
        {
            Check(token is JArray a&&a.Count==3,"XYZ vector required");var point=new Vector3(Number(token[0]),Number(token[1]),Number(token[2]));
            Check(point.x>=region.minX-80&&point.x<=region.minX+region.width+80&&point.z>=region.minZ-80&&point.z<=region.minZ+region.depth+80&&point.y>=-100&&point.y<=2000,"vertex outside bounded layout frame");return point;
        }
        public static CircuitRoadSurfacePlan Parse(string text,GeoRegion region,string layoutHash,CancellationToken token=default)
        {
            Check(text!=null&&Encoding.UTF8.GetByteCount(text)<=MaximumBytes,"byte budget");
            using(var scan=new JsonTextReader(new StringReader(text)){MaxDepth=12,DateParseHandling=DateParseHandling.None})
            {int count=0;while(scan.Read()){Check(++count<=MaximumTokens,"JSON token budget");if((count&1023)==0)token.ThrowIfCancellationRequested();}}
            JObject data;
            using(var reader=new JsonTextReader(new StringReader(text)){MaxDepth=12,DateParseHandling=DateParseHandling.None})
            {data=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Check(!reader.Read(),"trailing data");}
            Check((string)data["format"]=="RoadRushCircuitRoadSurfaces1"&&(string)data["layoutSha256"]==layoutHash,"format/layout SHA binding");
            Check((string)data["units"]=="metres"&&(string)data["axes"]=="east-up-north"&&region!=null&&region.circuit,"coordinate/layout kind");
            Check(data["latitude"]?.Type==JTokenType.Float||data["latitude"]?.Type==JTokenType.Integer,"numeric origin latitude");
            Check(data["longitude"]?.Type==JTokenType.Float||data["longitude"]?.Type==JTokenType.Integer,"numeric origin longitude");
            Check(Math.Abs((double)data["latitude"]-region.latitude)<1e-9&&Math.Abs((double)data["longitude"]-region.longitude)<1e-9&&Number(data["datum"])==region.datum,"coordinate origin/datum");
            Check(data["verticalReference"]?.Type==JTokenType.String&&!string.IsNullOrWhiteSpace((string)data["verticalReference"]),"named vertical reference");
            Check(region.roads!=null&&region.roads.Length>0&&region.roads.Length<=10000&&!region.roads.Any(r=>r==null||r.tunnel||r.bridge||r.layer!=0),"schema1 requires bounded single-layer ground roads");
            var roadIds=new HashSet<string>(region.roads.Select(r=>r.id),StringComparer.Ordinal);Check(roadIds.Count==region.roads.Length&&!roadIds.Contains(null),"unique layout road identities");
            var rows=data["meshes"] as JArray;Check(rows!=null&&rows.Count>0&&rows.Count<=MaximumMeshes,"mesh count budget");
            var parts=new List<Part>();var meshIds=new HashSet<string>();var covered=new HashSet<string>();var all=new List<Triangle>();int vertices=0,allocated=0;
            foreach(var row in rows)
            {
                token.ThrowIfCancellationRequested();Check(row is JObject,"mesh object required");string id=Id(row["id"]);Check(meshIds.Add(id),"duplicate mesh identity");
                var references=row["roadIds"] as JArray;Check(references!=null&&references.Count>0&&references.Count<=10000,"mesh road references");
                string[] roads=references.Select(Id).ToArray();Check(roads.Distinct().Count()==roads.Length&&roads.All(roadIds.Contains),"unknown/duplicate mesh road reference");foreach(string road in roads)covered.Add(road);
                var vv=row["vertices"] as JArray;var ii=row["triangles"] as JArray;
                Check(vv!=null&&vv.Count>=3&&vv.Count<=60000&&ii!=null&&ii.Count>=3&&ii.Count<=180000&&ii.Count%3==0,"mesh array budget");
                vertices+=vv.Count;allocated+=ii.Count*2;Check(vertices<=MaximumSourceVertices&&allocated<=MaximumAllocatedMeshVertices,"aggregate allocation budget");
                var points=vv.Select(v=>Vector(v,region)).ToArray();var indices=new int[ii.Count];
                for(int i=0;i<ii.Count;i++){Check(ii[i].Type==JTokenType.Integer,"integer triangle index");indices[i]=(int)ii[i];Check(indices[i]>=0&&indices[i]<points.Length,"triangle index range");}
                for(int i=0;i<indices.Length;i+=3){if((i&1023)==0)token.ThrowIfCancellationRequested();var triangle=new Triangle(points[indices[i]],points[indices[i+1]],points[indices[i+2]]);Check(triangle.Denominator< -1e-9,"upward nondegenerate ground triangle required");all.Add(triangle);}
                parts.Add(new Part(id,roads,points,indices));
            }
            Check(covered.SetEquals(roadIds),"all navigation roads need admitted authored surface coverage");
            return new CircuitRoadSurfacePlan(parts,all.ToArray(),covered,layoutHash,text,allocated,region.roads,token);
        }
        CircuitRoadSurfacePlan(List<Part> parts,Triangle[] faces,HashSet<string> roadIds,string layoutHash,string text,int allocated,GeoRoad[] roads,CancellationToken token)
        {
            Parts=new ReadOnlyCollection<Part>(parts);triangles=faces;covered=roadIds;LayoutSha256=layoutHash;AllocatedMeshVertices=allocated;
            using(var sha=SHA256.Create())PayloadSha256=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
            var cells=new Dictionary<long,List<int>>();var edgeUses=new Dictionary<EdgeKey,EdgeUse>();int references=0;
            void Edge(Vector3 a,Vector3 b){var key=new EdgeKey(a,b);if(edgeUses.TryGetValue(key,out var use)){Check(++use.Count<=2,"nonmanifold surface edge");}else edgeUses.Add(key,new EdgeUse{A=a,B=b,Count=1});}
            for(int index=0;index<faces.Length;index++)
            {
                token.ThrowIfCancellationRequested();var face=faces[index];int x0=Grid(face.MinX),x1=Grid(face.MaxX),z0=Grid(face.MinZ),z1=Grid(face.MaxZ);
                Check((long)(x1-x0+1)*(z1-z0+1)<=256,"triangle spatial extent budget; split long primitives");
                for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++){Check(++references<=MaximumIndexReferences,"spatial reference budget");long key=Key(x,z);if(!cells.TryGetValue(key,out var list))cells.Add(key,list=new List<int>());list.Add(index);}
                Edge(face.A,face.B);Edge(face.B,face.C);Edge(face.C,face.A);
            }
            triangleCells=cells.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
            // Positive projected overlap is never accepted as a hidden bridge or
            // duplicate asphalt layer. Adjacent shared edges are valid.
            var pairs=new HashSet<long>(new TrianglePairComparer());int comparisons=0;
            foreach(var cell in triangleCells.Values)for(int i=0;i<cell.Length;i++)for(int j=i+1;j<cell.Length;j++)
            {
                long key=((long)Math.Min(cell[i],cell[j])<<32)|(uint)Math.Max(cell[i],cell[j]);if(!pairs.Add(key))continue;
                Check(++comparisons<=MaximumPairChecks,"overlap-comparison budget");if((comparisons&1023)==0)token.ThrowIfCancellationRequested();
                Check(!Overlap(faces[cell[i]],faces[cell[j]]),"positive-area road overlap at triangles "+cell[i]+"/"+cell[j]+"; stacked roads need a supported layer representation");
            }
            int visits=0;var seamQueries=new HashSet<Vector3>();
            foreach(var face in faces)foreach(var point in new[]{face.A,face.B,face.C})
            {
                token.ThrowIfCancellationRequested();
                // TryHeight checks every candidate layer at this XZ, including
                // different Y values. Shared triangle vertices therefore need
                // one exact horizontal query, not one per incident face. Keep
                // the original work bound and5mm disagreement rule unchanged.
                if(!seamQueries.Add(new Vector3(point.x,0,point.z)))continue;
                var list=triangleCells[Key(Grid(point.x),Grid(point.z))];visits+=list.Length;Check(visits<=MaximumSeamVisits,"seam-validation budget");
                TryHeight(point.x,point.z,out _);
            }
            // Declared ownership alone cannot establish pavement coverage: a
            // connector may name a road whose physical surface is absent. Sample
            // every navigation segment, including both ends, at at most 2m.
            // Query paths are hints only; triangles remain the physical authority.
            int routeCells=0;
            foreach(var road in roads)
            {
                Check(road.points!=null&&road.points.Length>=2&&road.points.Length<=10000,"bounded navigation path required");
                for(int leg=1;leg<road.points.Length;leg++)
                {
                    token.ThrowIfCancellationRequested();var a=road.points[leg-1];var b=road.points[leg];
                    Check(Finite(a.x)&&Finite(a.y)&&Finite(a.z)&&Finite(b.x)&&Finite(b.y)&&Finite(b.z),"finite navigation path required");
                    double dx=(double)b.x-a.x,dz=(double)b.z-a.z,length=Math.Sqrt(dx*dx+dz*dz);
                    Check(length<=100000,"navigation segment extent budget");
                    var candidates=new HashSet<int>();
                    foreach(long cell in SegmentCells(a,b))
                    {
                        Check(++routeCells<=1000000,"navigation cell budget");
                        if(triangleCells.TryGetValue(cell,out var rows))
                        {visits+=rows.Length;Check(visits<=MaximumSeamVisits,"navigation validation budget");foreach(int index in rows)candidates.Add(index);}
                    }
                    var intervals=new List<(double Low,double High)>();
                    foreach(int index in candidates)
                    {if(triangles[index].Interval(a,b,out double low,out double high))intervals.Add((low,high));}
                    intervals.Sort((left,right)=>left.Low.CompareTo(right.Low));double reached=0;
                    foreach(var interval in intervals)
                    {
                        Check((interval.Low-reached)*Math.Max(length,1)<=SeamTolerance,"navigation road "+road.id+" leg "+leg+" has an unsupported pavement gap of "+((interval.Low-reached)*length).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"m");
                        reached=Math.Max(reached,interval.High);
                    }
                    Check(intervals.Count>0&&(1-reached)*Math.Max(length,1)<=SeamTolerance,"navigation road is missing authored pavement coverage");
                }
            }
            boundaries=edgeUses.Values.Where(e=>e.Count==1).ToArray();var edgeCells=new Dictionary<long,List<int>>();references=0;
            for(int index=0;index<boundaries.Length;index++)
            {
                token.ThrowIfCancellationRequested();var e=boundaries[index];
                double dx=(double)e.B.x-e.A.x,dz=(double)e.B.z-e.A.z;e.PlanLength=Math.Sqrt(dx*dx+dz*dz);
                for(int x=Grid(Math.Min(e.A.x,e.B.x)-BoundaryBlendRadius);x<=Grid(Math.Max(e.A.x,e.B.x)+BoundaryBlendRadius);x++)for(int z=Grid(Math.Min(e.A.z,e.B.z)-BoundaryBlendRadius);z<=Grid(Math.Max(e.A.z,e.B.z)+BoundaryBlendRadius);z++)
                {Check(++references<=MaximumIndexReferences,"boundary spatial-reference budget");long key=Key(x,z);if(!edgeCells.TryGetValue(key,out var list))edgeCells.Add(key,list=new List<int>());list.Add(index);}
            }
            boundaryCells=edgeCells.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
            // Reuse the bounded convex subtraction index for terrain. Exact
            // authored triangles, including connectors, cut the terrain footprint;
            // sparse terrain vertices alone cannot prevent grass protruding.
            IEnumerable<RoadBankExteriorClip.Footprint> Footprints()
            {
                for(int i=0;i<faces.Length;i++)
                {
                    token.ThrowIfCancellationRequested();var f=faces[i];
                    yield return new RoadBankExteriorClip.Footprint{Id="authored-"+i,Layer=0,
                        Polygon=new[]{new Vector2(f.A.x,f.A.z),new Vector2(f.B.x,f.B.z),new Vector2(f.C.x,f.C.z)}};
                }
            }
            TerrainPavement=new RoadBankExteriorClip.SpatialIndex(Footprints());
        }
        // Visit the cells intersected by a segment (including its endpoints),
        // rather than its potentially enormous rectangular bounding box.
        static IEnumerable<long> SegmentCells(Vector3 a,Vector3 b)
        {
            int x=Grid(a.x),z=Grid(a.z),endX=Grid(b.x),endZ=Grid(b.z);
            double dx=(double)b.x-a.x,dz=(double)b.z-a.z;
            int sx=Math.Sign(dx),sz=Math.Sign(dz);
            double tx=sx==0?double.PositiveInfinity:((x+(sx>0?1:0))*Cell-a.x)/dx;
            double tz=sz==0?double.PositiveInfinity:((z+(sz>0?1:0))*Cell-a.z)/dz;
            double stepX=sx==0?double.PositiveInfinity:Cell/Math.Abs(dx),stepZ=sz==0?double.PositiveInfinity:Cell/Math.Abs(dz);
            for(int count=0;;count++)
            {
                Check(count<=8192,"navigation spatial extent budget");yield return Key(x,z);
                if(x==endX&&z==endZ)yield break;
                if(tx<tz){x+=sx;tx+=stepX;}
                else if(tz<tx){z+=sz;tz+=stepZ;}
                else{x+=sx;z+=sz;tx+=stepX;tz+=stepZ;}
            }
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        public bool TryHeight(float x,float z,out float height)
        {
            height=0;if(!Finite(x)||!Finite(z))return false;if(!triangleCells.TryGetValue(Key(Grid(x),Grid(z)),out var candidates))return false;
            float min=float.PositiveInfinity,max=float.NegativeInfinity;
            foreach(int index in candidates)if(triangles[index].Sample(x,z,out float value)){min=Math.Min(min,value);max=Math.Max(max,value);}
            if(float.IsInfinity(min))return false;Check((double)max-min<=SeamTolerance,"touching surfaces differ by more than5mm");height=max;return true;
        }
        /// <summary>Ground-bed sample with a6m boundary blend. Integration must
        /// still ensure the terrain mesh cannot poke through authored pavement.</summary>
        public bool TryTerrainHeight(float x,float z,float natural,out float height)
        {
            if(TryHeight(x,z,out height)){height-=.08f;return true;}height=natural;
            if(!boundaryCells.TryGetValue(Key(Grid(x),Grid(z)),out var candidates))return false;
            double radiusSquared=BoundaryBlendRadius*BoundaryBlendRadius;
            double best=radiusSquared,weightedHeight=0,weights=0;
            foreach(int index in candidates)
            {
                var e=boundaries[index];double dx=(double)e.B.x-e.A.x,dz=(double)e.B.z-e.A.z,length=dx*dx+dz*dz;if(length<=0)continue;
                double t=Math.Max(0,Math.Min(1,((x-e.A.x)*dx+(z-e.A.z)*dz)/length));
                double px=e.A.x+t*dx,pz=e.A.z+t*dz,d=(x-px)*(x-px)+(z-pz)*(z-pz);
                if(d>=radiusSquared)continue;best=Math.Min(best,d);
                // A nearest-edge switch creates a height step between parallel
                // main/pit roads at different levels. Blend all supported edges
                // continuously instead. Length weighting avoids giving dense
                // profile sampling extra influence merely for adding vertices.
                // Inverse fourth-power distance preserves close road contact;
                // compact support makes each contribution vanish at6m.
                double cutoff=1-d/radiusSquared,denominator=d+BoundaryWeightFloorSquared;
                double weight=e.PlanLength*cutoff*cutoff/(denominator*denominator);
                weightedHeight+=weight*(e.A.y+t*(e.B.y-e.A.y));weights+=weight;
            }
            if(best>=radiusSquared||weights<=0)return false;
            double fraction=Math.Sqrt(best)/BoundaryBlendRadius;fraction=fraction*fraction*(3-2*fraction);
            height=(float)((weightedHeight/weights-BoundaryContactInset)*(1-fraction)+natural*fraction);return true;
        }
        static bool Inside(Vector3 point,Triangle t,bool strict)=>Inside(point.x,point.z,t,strict);
        static bool Inside(double x,double z,Triangle t,bool strict)
        {
            double Side(Vector3 from,Vector3 to)=>((double)to.x-from.x)*(z-from.z)-((double)to.z-from.z)*(x-from.x);
            double a=Side(t.A,t.B),b=Side(t.B,t.C),c=Side(t.C,t.A);
            return strict?(a< -GeometryEpsilon&&b< -GeometryEpsilon&&c< -GeometryEpsilon):(a<=GeometryEpsilon&&b<=GeometryEpsilon&&c<=GeometryEpsilon);
        }
        static bool Cross(Vector3 a,Vector3 b,Vector3 c,Vector3 d)=>Orient(a,b,c)*Orient(a,b,d)< -1e-18&&Orient(c,d,a)*Orient(c,d,b)< -1e-18;
        static bool Overlap(Triangle a,Triangle b)
        {
            if(a.MaxX<=b.MinX+GeometryEpsilon||b.MaxX<=a.MinX+GeometryEpsilon||a.MaxZ<=b.MinZ+GeometryEpsilon||b.MaxZ<=a.MinZ+GeometryEpsilon)return false;
            var aa=new[]{a.A,a.B,a.C};var bb=new[]{b.A,b.B,b.C};
            if(aa.Any(p=>Inside(p,b,true))||bb.Any(p=>Inside(p,a,true))||Inside(((double)a.A.x+a.B.x+a.C.x)/3,((double)a.A.z+a.B.z+a.C.z)/3,b,true)||Inside(((double)b.A.x+b.B.x+b.C.x)/3,((double)b.A.z+b.B.z+b.C.z)/3,a,true))return true;
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)if(Cross(aa[i],aa[(i+1)%3],bb[j],bb[(j+1)%3]))return true;
            return false;
        }
    }
}
