using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using UnityEngine;
namespace EarthUnscripted
{
    // Candidate: resulting exact vertices/triangles must drive BOTH bank renderer
    // and collider. Asphalt union (strips AND junction aprons) supplies tyre support.
    // No .55m terrain-bed alteration. Reuse one SpatialIndex; don't rebuild per face.
    public static class RoadBankExteriorClip
    {
        public const int MaximumPavementPrimitives=256000;public const long MaximumIndexMemberships=8000000;
        public sealed class Footprint{public string Id;public int Layer;public bool Bridge,Tunnel;public Vector2[] Polygon;}
        public sealed class Data{public Vector3[] Vertices;public int[] Triangles;public Vector2[] UV;}
        sealed class Entry{public Footprint Value;public string Key;public float X0,Z0,X1,Z1;}
        public sealed class SpatialIndex
        {
            const float Cell=8;readonly Dictionary<(int,int),List<Entry>> cells=new Dictionary<(int,int),List<Entry>>();
            static int Grid(float x)=>(int)Math.Floor(x/Cell);
            static string ShapeKey(Vector2[] ring)
            {
                var values=ring.Select(p=>p.x.ToString("R",CultureInfo.InvariantCulture)+","+p.y.ToString("R",CultureInfo.InvariantCulture)).ToArray();string best=null;
                foreach(var sequence in new[]{values,values.Reverse().ToArray()})for(int i=0;i<sequence.Length;i++){string key=string.Join(";",sequence.Skip(i).Concat(sequence.Take(i)));if(best==null||StringComparer.Ordinal.Compare(key,best)<0)best=key;}return best;
            }
            public SpatialIndex(IEnumerable<Footprint> source)
            {
                var unique=new Dictionary<string,Entry>(StringComparer.Ordinal);int count=0;long memberships=0;
                foreach(var f in source)
                {
                    if(++count>MaximumPavementPrimitives)throw new InvalidDataException("Pavement primitive budget");if(f.Tunnel)continue;
                    var ring=f.Polygon;if(ring==null||ring.Length<3||ring.Length>64||ring.Any(p=>float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.y)))throw new InvalidDataException("Invalid pavement primitive");
                    double area=Area(ring);if(Math.Abs(area)<1e-8)continue;double sign=area>0?1:-1;
                    for(int i=0;i<ring.Length;i++)if(Side(ring[i],ring[(i+1)%ring.Length],new Vector3(ring[(i+2)%ring.Length].x,0,ring[(i+2)%ring.Length].y))*sign<-.00001)throw new InvalidDataException("Nonconvex paving primitive; triangulate before indexing");
                    string shape=f.Layer+"|"+f.Bridge+"|"+ShapeKey(ring);if(unique.ContainsKey(shape))continue;
                    var e=new Entry{Value=new Footprint{Id=f.Id,Layer=f.Layer,Bridge=f.Bridge,Polygon=(Vector2[])ring.Clone()},Key=shape,X0=ring.Min(p=>p.x),Z0=ring.Min(p=>p.y),X1=ring.Max(p=>p.x),Z1=ring.Max(p=>p.y)};unique.Add(shape,e);
                    long visits=(long)(Grid(e.X1)-Grid(e.X0)+1)*(Grid(e.Z1)-Grid(e.Z0)+1);if(visits>4096)throw new InvalidDataException("Pavement primitive must be locally tessellated");if(visits<0||(memberships+=visits)>MaximumIndexMemberships)throw new InvalidDataException("Pavement index membership budget");
                    for(int z=Grid(e.Z0);z<=Grid(e.Z1);z++)for(int x=Grid(e.X0);x<=Grid(e.X1);x++){if(!cells.TryGetValue((x,z),out var list))cells[(x,z)]=list=new List<Entry>();list.Add(e);}
                }
            }
            internal Footprint[] At(Vector3[] triangle,int layer,bool bridge)
            {
                float x0=triangle.Min(p=>p.x),z0=triangle.Min(p=>p.z),x1=triangle.Max(p=>p.x),z1=triangle.Max(p=>p.z);var found=new HashSet<Entry>();long visits=(long)(Grid(x1)-Grid(x0)+1)*(Grid(z1)-Grid(z0)+1);if(visits>4096)throw new InvalidDataException("Bank face must be locally tessellated");
                for(int z=Grid(z0);z<=Grid(z1);z++)for(int x=Grid(x0);x<=Grid(x1);x++)if(cells.TryGetValue((x,z),out var list))foreach(var e in list)
                {
                    if(e.Value.Layer!=layer||e.Value.Bridge!=bridge||e.X1<x0||e.X0>x1||e.Z1<z0||e.Z0>z1)continue;found.Add(e);
                    if(found.Count>4096)throw new InvalidDataException("Pathological coincident pavement density; retain active world");
                }
                return found.OrderBy(e=>e.Key,StringComparer.Ordinal).Select(e=>e.Value).ToArray();
            }
        }
        static double Side(Vector2 a,Vector2 b,Vector3 p)
        {double dx=(double)b.x-a.x,dz=(double)b.y-a.y;return(dx*((double)p.z-a.y)-dz*((double)p.x-a.x))/Math.Max(.000001,Math.Sqrt(dx*dx+dz*dz));}
        static double Area(IReadOnlyList<Vector2> p){double a=0;for(int i=0;i<p.Count;i++){var q=p[(i+1)%p.Count];a+=(double)p[i].x*q.y-(double)q.x*p[i].y;}return a*.5;}
        readonly struct Point{public readonly double X,Y,Z;public Point(double x,double y,double z){X=x;Y=y;Z=z;}public Point(Vector3 p){X=p.x;Y=p.y;Z=p.z;}public Vector3 Float()=>new Vector3((float)X,(float)Y,(float)Z);}
        static double Side(Vector2 a,Vector2 b,Point p){double dx=(double)b.x-a.x,dz=(double)b.y-a.y;return(dx*(p.Z-a.y)-dz*(p.X-a.x))/Math.Max(.000001,Math.Sqrt(dx*dx+dz*dz));}
        static List<Point> Half(List<Point> source,Vector2 a,Vector2 b,double orientation,bool inside)
        {
            var output=new List<Point>();if(source.Count==0)return output;var previous=source.Last();double pd=Side(a,b,previous)*orientation;
            foreach(var next in source)
            {
                double nd=Side(a,b,next)*orientation;bool oldIn=inside?pd>=0:pd<=0,newIn=inside?nd>=0:nd<=0;
                if(oldIn!=newIn){double t=pd/(pd-nd);output.Add(new Point(previous.X+(next.X-previous.X)*t,previous.Y+(next.Y-previous.Y)*t,previous.Z+(next.Z-previous.Z)*t));}if(newIn)output.Add(next);previous=next;pd=nd;
            }
            for(int i=output.Count-1;i>=0&&output.Count>1;i--){var p=output[i];var q=output[(i+1)%output.Count];double x=p.X-q.X,y=p.Y-q.Y,z=p.Z-q.Z;if(x*x+y*y+z*z<1e-20)output.RemoveAt(i);}return output;
        }
        public static IEnumerable<int> ClipSteps(Vector3[] triangle,int layer,bool bridge,SpatialIndex index,Action<Data> finish)
        {
            if(triangle==null||triangle.Length!=3||triangle.Any(p=>float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.z)))throw new InvalidDataException("Invalid bank source triangle");
            var candidates=index.At(triangle,layer,bridge);var parts=new List<List<Point>>{triangle.Select(p=>new Point(p)).ToList()};int work=0;
            foreach(var footprint in candidates)
            {
                var ring=footprint.Polygon;double orientation=Area(ring)>0?1:-1;var outside=new List<List<Point>>();
                foreach(var subject in parts)
                {
                    var remaining=subject;
                    for(int i=0;i<ring.Length&&remaining.Count>=3;i++)
                    {
                        var piece=Half(remaining,ring[i],ring[(i+1)%ring.Length],orientation,false);if(piece.Count>=3)outside.Add(piece);remaining=Half(remaining,ring[i],ring[(i+1)%ring.Length],orientation,true);
                    }
                    if(++work>131072)throw new InvalidDataException("Road bank clipping total work budget");if(outside.Count>1024||outside.Sum(p=>p.Count)>32768)throw new InvalidDataException("Road bank exterior clipping complexity budget");if(work%32==0)yield return 0;
                }
                parts=outside;if(parts.Count==0)break;yield return 0;
            }
            var vertices=new List<Vector3>();var triangles=new List<int>();foreach(var part in parts)
            {
                int start=vertices.Count;vertices.AddRange(part.Select(p=>p.Float()));for(int i=1;i<part.Count-1;i++){double ax=part[i].X-part[0].X,az=part[i].Z-part[0].Z,bx=part[i+1].X-part[0].X,bz=part[i+1].Z-part[0].Z;if(Math.Abs(ax*bz-az*bx)<1e-8)continue;triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);}
            }
            finish(new Data{Vertices=vertices.ToArray(),Triangles=triangles.ToArray(),UV=vertices.Select(p=>new Vector2(p.x/8,p.z/8)).ToArray()});
        }
        public static Data Clip(Vector3[] triangle,int layer,bool bridge,IReadOnlyList<Footprint> nearby)
        {Data data=null;foreach(var step in ClipSteps(triangle,layer,bridge,new SpatialIndex(nearby),value=>data=value)){}return data;}
    }
}
