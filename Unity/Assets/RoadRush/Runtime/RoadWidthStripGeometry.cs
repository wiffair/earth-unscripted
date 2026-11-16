using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace RoadRush
{
    // Candidate surface data only. Every Corner describes a required paving patch
    // for renderer/collider/footprint consumers; it is not optional decoration.
    // Inward profiles use endpoint round collars, serialized as bevelBoundary.
    public static class RoadWidthStripGeometry
    {
        public sealed class Corner{public double Distance;public Vector3 Centre;public float Radius;public bool Bridge;public int Layer;}
        public sealed class Data{public Vector3[] Vertices;public int[] Triangles;public Vector2[] UV;public double[] Distances;public float[] Widths;public int Columns;public Corner[] Corners;public bool BevelFallback;}
        public static Data Create(RoadWidthTaperPlan.RoadProfile profile,RoadMarkingPlan.RoadFrame frame,Func<float,float,float> surface)
        {var stops=profile.Chain.Stations.Select(st=>{double local=st.Distance-profile.Start;return profile.Forward?local:profile.Length-local;});return Core(profile.Road,profile.Length,frame,profile.WidthAt,stops,Math.Max(profile.Chain.Stations.Max(st=>st.Width),profile.Road.width),profile.Chain.Inward,surface,profile.Chain.Inward);}
        // Production overload: admitted float controls are evaluated identically on host/guest.
        public static Data Create(GeoRoad road,RoadWidthProfileDto finalized,RoadMarkingPlan.RoadFrame frame,float maximum,Func<float,float,float> surface)
        {return Core(road,frame.Length,frame,finalized.Width,finalized.distances.Select(x=>(double)x),maximum,finalized.bevelBoundary,surface,finalized.bevelBoundary);}
        static Data Core(GeoRoad road,double length,RoadMarkingPlan.RoadFrame frame,Func<double,double> widthAt,IEnumerable<double> stops,double maximum,bool inward,Func<float,float,float> surface,bool bevelAll)
        {
            var positions=new SortedSet<double>{0,length};
            foreach(double distance in frame.Arc)positions.Add(frame.Forward?distance:frame.Length-distance);
            double stationEpsilon=Math.Max(.0001,length*.0000002);foreach(double local in stops)if(local>stationEpsilon&&local<length-stationEpsilon&&!positions.Any(x=>Math.Abs(x-local)<stationEpsilon))positions.Add(local);
            var corners=(bevelAll?frame.Arc.Skip(1).Take(frame.Arc.Length-2).Select(x=>frame.Forward?x:frame.Length-x):frame.SharpDistances).Concat(inward?new[]{0.0,length}:Array.Empty<double>()).Distinct().OrderBy(x=>x).ToArray();
            bool IsCorner(double distance)=>corners.Any(x=>Math.Abs(x-distance)<.000001);
            Vector3 Edge(double distance,int side,bool incoming)
            {frame.At(distance,out var p,out var right,incoming,bevelAll);p+=right*(float)(side*widthAt(distance)*.5);p.y=surface(p.x,p.z);if(float.IsNaN(p.y)||float.IsInfinity(p.y))throw new InvalidDataException("Invalid road surface sample");return p;}
            var seed=positions.ToArray();var distances=new List<double>{0};var previous=new List<bool>{false};
            void Append(double b,bool incoming){if(distances.Count>=4096)throw new InvalidDataException("Road width strip sample budget");distances.Add(b);previous.Add(incoming);}
            void Refine(double a,double b,int depth)
            {
                if(b-a<.0000001)return;double m=(a+b)*.5;bool split=b-a>2.5;
                foreach(int side in new[]{-1,1}){var x=Edge(a,side,false);var y=Edge(b,side,true);var mid=Edge(m,side,false);if((mid-(x+y)*.5f).magnitude>.02f)split=true;}
                if(split&&depth<16&&b-a>.01){Refine(a,m,depth+1);Refine(m,b,depth+1);}else Append(b,IsCorner(b));
            }
            for(int i=1;i<seed.Length;i++){if(i>1&&IsCorner(seed[i-1]))Append(seed[i-1],false);Refine(seed[i-1],seed[i],0);}
            int columns=Math.Max(2,(int)Math.Ceiling(maximum/2));
            var v=new Vector3[distances.Count*(columns+1)];var uv=new Vector2[v.Length];var widths=new float[distances.Count];var triangles=new List<int>();
            for(int row=0;row<distances.Count;row++)
            {
                frame.At(distances[row],out var centre,out var right,previous[row],bevelAll);float width=widths[row]=(float)widthAt(distances[row]);
                for(int col=0;col<=columns;col++){var p=centre+right*(width*(col/(float)columns-.5f));p.y=surface(p.x,p.z);if(float.IsNaN(p.y)||float.IsInfinity(p.y))throw new InvalidDataException("Nonfinite interior asphalt surface");int index=row*(columns+1)+col;v[index]=p;uv[index]=new Vector2(p.x/8,p.z/8);}
                if(row==0||distances[row]-distances[row-1]<.000001)continue;
                for(int col=0;col<columns;col++){int a=(row-1)*(columns+1)+col,b=a+1,c=a+columns+1,d=c+1;triangles.AddRange(new[]{a,c,b,b,c,d});}
            }
            bool folded=false;for(int i=0;i<triangles.Count;i+=3)if(Vector3.Cross(v[triangles[i+1]]-v[triangles[i]],v[triangles[i+2]]-v[triangles[i]]).y<=.00000001f){folded=true;break;}
            if(folded&&!bevelAll)return Core(road,length,frame,widthAt,stops,maximum,inward,surface,true);
            if(folded)throw new InvalidDataException("Degenerate bank/road geometry after bounded bevel fallback");
            var patches=corners.Select(s=>{frame.At(s,out var p,out _,false,bevelAll);p.y=surface(p.x,p.z);return new Corner{Distance=s,Centre=p,Radius=(float)widthAt(s)*.5f,Bridge=road.bridge,Layer=road.layer};}).ToArray();
            return new Data{Vertices=v,Triangles=triangles.ToArray(),UV=uv,Distances=distances.ToArray(),Widths=widths,Columns=columns,Corners=patches,BevelFallback=bevelAll};
        }
    }
}
