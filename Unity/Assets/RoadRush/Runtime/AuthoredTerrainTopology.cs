using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoadRush
{
    // Conform the PLAN mesh before evaluating its nonlinear height field. A
    // clipped face may introduce a point along a neighbouring face's edge; both
    // sides must use that point, including across terrain chunk boundaries.
    public sealed class AuthoredTerrainTopology
    {
        const double WeldMetres=.0001; // Soil only: at most .071mm horizontal move.
        const double EdgeToleranceMetres=.00015;
        const double CellMetres=2.5;
        const int MaximumVertices=300000,MaximumTriangles=400000;
        const long MaximumCandidateVisits=16000000;
        readonly List<Vector3> points=new List<Vector3>();
        readonly List<int> faces=new List<int>();
        readonly Dictionary<(long,long),int> unique=new Dictionary<(long,long),int>();
        readonly Dictionary<(int,int),List<int>> cells=new Dictionary<(int,int),List<int>>();
        readonly Dictionary<(int,int),int[]> edges=new Dictionary<(int,int),int[]>();
        long visits;int emitted;
        static int Cell(float value)=>(int)Math.Floor(value/CellMetres);
        int Add(Vector3 p)
        {
            if(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.z)||float.IsInfinity(p.z)||Math.Abs(p.x)>1000000||Math.Abs(p.z)>1000000)
                throw new InvalidDataException("Authored soil coordinate");
            var key=((long)Math.Round(p.x/WeldMetres,MidpointRounding.AwayFromZero),(long)Math.Round(p.z/WeldMetres,MidpointRounding.AwayFromZero));
            if(unique.TryGetValue(key,out int index))return index;
            if(points.Count>=MaximumVertices)throw new InvalidDataException("Authored soil vertex budget");
            index=points.Count;var value=new Vector3((float)(key.Item1*WeldMetres),0,(float)(key.Item2*WeldMetres));
            points.Add(value);unique.Add(key,index);
            var cell=(Cell(value.x),Cell(value.z));if(!cells.TryGetValue(cell,out var bucket))cells[cell]=bucket=new List<int>();bucket.Add(index);return index;
        }
        public void AddMesh(Vector3[] vertices,int[] triangles,bool includeFaces=true)
        {
            if(vertices==null||triangles==null||triangles.Length%3!=0)throw new InvalidDataException("Authored soil mesh");
            var map=new int[vertices.Length];for(int i=0;i<map.Length;i++)map[i]=Add(vertices[i]);
            if(!includeFaces)return;
            if(faces.Count/3+triangles.Length/3>MaximumTriangles)throw new InvalidDataException("Authored soil face budget");
            foreach(int i in triangles){if(i<0||i>=map.Length)throw new InvalidDataException("Authored soil index");faces.Add(map[i]);}
        }
        int[] Edge(int a,int b)
        {
            var key=a<b?(a,b):(b,a);if(edges.TryGetValue(key,out var found))return found;
            var start=points[key.Item1];var end=points[key.Item2];double dx=(double)end.x-start.x,dz=(double)end.z-start.z,length2=dx*dx+dz*dz;
            if(length2<1e-16)return edges[key]=Array.Empty<int>();
            double length=Math.Sqrt(length2);var values=new List<(double,int)>();
            int x0=Cell(Math.Min(start.x,end.x)-(float)EdgeToleranceMetres),x1=Cell(Math.Max(start.x,end.x)+(float)EdgeToleranceMetres);
            int z0=Cell(Math.Min(start.z,end.z)-(float)EdgeToleranceMetres),z1=Cell(Math.Max(start.z,end.z)+(float)EdgeToleranceMetres);
            if((long)(x1-x0+1)*(z1-z0+1)>256)throw new InvalidDataException("Authored soil edge must be locally tessellated");
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)if(cells.TryGetValue((x,z),out var bucket))foreach(int i in bucket)
            {
                if(++visits>MaximumCandidateVisits)throw new InvalidDataException("Authored soil conformity work budget");
                if(i==a||i==b)continue;var p=points[i];double px=(double)p.x-start.x,pz=(double)p.z-start.z;
                double t=(px*dx+pz*dz)/length2;
                if(t<=1e-6||t>=1-1e-6||Math.Abs(px*dz-pz*dx)/length>EdgeToleranceMetres)continue;
                values.Add((t,i));if(values.Count>128)throw new InvalidDataException("Authored soil edge subdivision budget");
            }
            values.Sort((u,v)=>u.Item1.CompareTo(v.Item1));var result=new int[values.Count];for(int i=0;i<result.Length;i++)result[i]=values[i].Item2;
            edges.Add(key,result);return result;
        }
        void Emit(Vector3 a,Vector3 b,Vector3 c,Action<Vector3,Vector3,Vector3> emit)
        {
            double area=((double)b.x-a.x)*((double)c.z-a.z)-((double)b.z-a.z)*((double)c.x-a.x);
            if(Math.Abs(area)<1e-10)return;
            if(++emitted>1000000)throw new InvalidDataException("Authored soil output budget");
            // Preserve the source face orientation; the caller owns material and collision.
            emit(a,b,c);
        }
        public IEnumerable<int> ConformSteps(Action<Vector3,Vector3,Vector3> emit)
        {
            for(int i=0;i<faces.Count;i+=3)
            {
                int a=faces[i],b=faces[i+1],c=faces[i+2];var ab=Edge(a,b);var bc=Edge(b,c);var ca=Edge(c,a);
                if(ab.Length+bc.Length+ca.Length==0)Emit(points[a],points[b],points[c],emit);
                else
                {
                    var ring=new List<int>{a};
                    void Append(int u,int v,int[] cuts){if(u<v)ring.AddRange(cuts);else for(int j=cuts.Length-1;j>=0;j--)ring.Add(cuts[j]);ring.Add(v);}
                    Append(a,b,ab);Append(b,c,bc);Append(c,a,ca);ring.RemoveAt(ring.Count-1);
                    // The original triangle is convex. Its centroid is interior;
                    // a fan through every boundary point retains all shared cuts.
                    var centre=(points[a]+points[b]+points[c])/3;
                    for(int j=0;j<ring.Count;j++)Emit(centre,points[ring[j]],points[ring[(j+1)%ring.Count]],emit);
                }
                if(i%(3*32)==0)yield return 0;
            }
        }
    }
}
