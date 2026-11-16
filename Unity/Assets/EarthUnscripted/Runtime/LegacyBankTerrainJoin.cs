using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    // Pure geometry for optional, expanded schema0/layer0 soil shoulders. This
    // changes neither the terrain evaluator nor an asphalt/access surface. All
    // emitted vertices are shared by the bank renderer AND its collider.
    internal sealed class LegacyBankTerrainJoin
    {
        readonly RoadBankExteriorClip.SpatialIndex pavement;
        readonly Func<float,float,float> roadHeight, terrainHeight;
        readonly float gridMetres, shoulderMetres;
        readonly Dictionary<(int,int),float> soilVertices = new Dictionary<(int,int),float>();
        // Memoize queries to the EXISTING index, not another spatial index. Keys
        // are global terrain cells, independent of chunk and road emission order.
        readonly Dictionary<(int,int),RoadBankExteriorClip.Footprint[]> neighbors =
            new Dictionary<(int,int),RoadBankExteriorClip.Footprint[]>();

        internal LegacyBankTerrainJoin(RoadBankExteriorClip.SpatialIndex pavement,
            Func<float,float,float> roadHeight, Func<float,float,float> terrainHeight,
            float gridMetres, float shoulderMetres)
        {
            this.pavement=pavement??throw new ArgumentNullException(nameof(pavement));
            this.roadHeight=roadHeight??throw new ArgumentNullException(nameof(roadHeight));
            this.terrainHeight=terrainHeight??throw new ArgumentNullException(nameof(terrainHeight));
            // Two bank cells per actual terrain cell: the phase/diagonal is still
            // identical, while the narrow soil fade gets a practical extra knot.
            this.gridMetres=gridMetres*.5f;this.shoulderMetres=shoulderMetres;
            if(gridMetres<=0||shoulderMetres<=0)throw new ArgumentOutOfRangeException(nameof(gridMetres));
        }

        readonly struct Point
        {
            internal readonly double X,Z;
            internal Point(double x,double z){X=x;Z=z;}
        }
        static double Cross(Point a,Point b,Point p)=>
            (b.X-a.X)*(p.Z-a.Z)-(b.Z-a.Z)*(p.X-a.X);

        static List<Point> Clip(List<Point> input,Point a,Point b)
        {
            var output=new List<Point>();if(input.Count==0)return output;
            var previous=input[input.Count-1];double before=-Cross(a,b,previous);
            foreach(var next in input)
            {
                double after=-Cross(a,b,next);
                if((before>=0)!=(after>=0))
                {
                    double t=before/(before-after);
                    output.Add(new Point(previous.X+t*(next.X-previous.X),previous.Z+t*(next.Z-previous.Z)));
                }
                if(after>=0)output.Add(next);
                previous=next;before=after;
            }
            return output;
        }

        RoadBankExteriorClip.Footprint[] Neighbors(int x,int z)
        {
            if(neighbors.TryGetValue((x,z),out var found))return found;
            float west=x*gridMetres-shoulderMetres,south=z*gridMetres-shoulderMetres;
            float east=(x+1)*gridMetres+shoulderMetres,north=(z+1)*gridMetres+shoulderMetres;
            found=pavement.At(new[]{new Vector3(west,0,south),new Vector3(east,0,south),new Vector3(west,0,north)},0,false);
            neighbors.Add((x,z),found);return found;
        }

        static float FootprintDistance(float x,float z,Vector2[] ring)
        {
            bool positive=false,negative=false;double closest=double.PositiveInfinity;
            for(int i=0;i<ring.Length;i++)
            {
                var a=ring[i];var b=ring[(i+1)%ring.Length];
                double dx=(double)b.x-a.x,dz=(double)b.y-a.y;
                double px=(double)x-a.x,pz=(double)z-a.y;
                double cross=dx*pz-dz*px;positive|=cross>0;negative|=cross<0;
                double length=dx*dx+dz*dz;
                double t=length==0?0:Math.Max(0,Math.Min(1,(px*dx+pz*dz)/length));
                double ex=px-t*dx,ez=pz-t*dz;closest=Math.Min(closest,ex*ex+ez*ez);
            }
            return positive&&negative?(float)Math.Sqrt(closest):0;
        }

        internal float SoilHeight(float x,float z)
        {
            float ground=terrainHeight(x,z)+.012f;
            int ix=Mathf.FloorToInt(x/gridMetres),iz=Mathf.FloorToInt(z/gridMetres);
            float distance=float.PositiveInfinity;
            // Only geometry of the same-layer pavement UNION is used. Footprint.Id
            // is deliberately ignored: the existing index may deduplicate one
            // shape shared by multiple source owners, retaining only its first ID.
            foreach(var footprint in Neighbors(ix,iz))
                distance=Mathf.Min(distance,FootprintDistance(x,z,footprint.Polygon));
            if(distance>=shoulderMetres)return ground;
            float t=Mathf.Clamp01(distance/shoulderMetres);
            float weight=1-t*t*(3-2*t);
            // ONE actual legacy shared field, not one independent bank plane per
            // road. Eligible shoulders are at-grade, away from bridge/layer sites.
            float preferred=roadHeight(x,z)-.035f;
            return ground+Mathf.Max(0,preferred-ground)*weight;
        }

        float GridHeight(int x,int z)
        {
            if(soilVertices.TryGetValue((x,z),out float height))return height;
            height=SoilHeight(x*gridMetres,z*gridMetres);soilVertices.Add((x,z),height);return height;
        }

        float PlaneHeight(int x,int z,float px,float pz,bool lower)
        {
            float tx=(px-x*gridMetres)/gridMetres,tz=(pz-z*gridMetres)/gridMetres;
            float a=GridHeight(x,z),b=GridHeight(x+1,z),c=GridHeight(x,z+1),d=GridHeight(x+1,z+1);
            return lower?a*(1-tx-tz)+b*tx+c*tz:d*(tx+tz-1)+b*(1-tz)+c*(1-tx);
        }

        internal float ConformingHeight(float x,float z)
        {
            int ix=Mathf.FloorToInt(x/gridMetres),iz=Mathf.FloorToInt(z/gridMetres);
            return PlaneHeight(ix,iz,x,z,(x-ix*gridMetres)+(z-iz*gridMetres)<=gridMetres);
        }

        internal RoadBankExteriorClip.Data Conform(Vector3 a,Vector3 b,Vector3 c)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            int west=Mathf.FloorToInt(Mathf.Min(a.x,b.x,c.x)/gridMetres);
            int east=Mathf.FloorToInt(Mathf.Max(a.x,b.x,c.x)/gridMetres);
            int south=Mathf.FloorToInt(Mathf.Min(a.z,b.z,c.z)/gridMetres);
            int north=Mathf.FloorToInt(Mathf.Max(a.z,b.z,c.z)/gridMetres);
            for(int z=south;z<=north;z++)for(int x=west;x<=east;x++)
            {
                double x0=x*gridMetres,x1=(x+1)*gridMetres,z0=z*gridMetres,z1=(z+1)*gridMetres;
                // Same terrain-aligned half-cell diagonal: tx+tz=1. Both
                // clockwise XZ rings have upward 3D winding when emitted.
                int half=0;
                foreach(var groundTriangle in new[]{
                    new[]{new Point(x0,z0),new Point(x0,z1),new Point(x1,z0)},
                    new[]{new Point(x1,z0),new Point(x0,z1),new Point(x1,z1)}})
                {
                    bool lower=half++==0;
                    var polygon=new List<Point>{new Point(a.x,a.z),new Point(b.x,b.z),new Point(c.x,c.z)};
                    for(int edge=0;edge<3&&polygon.Count>0;edge++)
                        polygon=Clip(polygon,groundTriangle[edge],groundTriangle[(edge+1)%3]);
                    if(polygon.Count<3)continue;
                    int start=vertices.Count;
                    foreach(var point in polygon)
                    {
                        float px=(float)point.X,pz=(float)point.Z;
                        // ALL banks use this same global cell plane. Sampling the
                        // nonlinear fade separately per road triangle would leave
                        // different overlapping height planes despite shared XYZ.
                        vertices.Add(new Vector3(px,PlaneHeight(x,z,px,pz,lower),pz));uv.Add(new Vector2(px/8,pz/8));
                    }
                    for(int i=1;i<polygon.Count-1;i++)
                    {
                        if(Cross(polygon[0],polygon[i],polygon[i+1])==0)continue;
                        triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);
                    }
                }
            }
            // Every half-cell is inside ONE actual terrain triangle. Its shared
            // grid vertices are >= terrain+12mm, so the entire common soil plane
            // stays above terrain. Overlapping banks now have identical planes;
            // no mutual subtraction or blanket metre-scale raising is involved.
            return new RoadBankExteriorClip.Data{Vertices=vertices.ToArray(),Triangles=triangles.ToArray(),UV=uv.ToArray()};
        }
    }
}
