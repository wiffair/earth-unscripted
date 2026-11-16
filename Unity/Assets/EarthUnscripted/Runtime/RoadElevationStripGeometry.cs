using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace EarthUnscripted
{
    // Strip preparation for explicit height authority. This does not construct
    // caps, authorize a junction, or publish a complete drivable surface plan.
    public static class RoadElevationStripGeometry
    {
        public delegate RoadElevationCurve.Sample RoadLocalElevation(string roadId,double sourceS,double nominalLateral,Vector3 centre,Vector3 right,Vector3 dCentreDH,Vector3 dRightDH);
        public sealed class RequiredCap
        {
            public string RoadId,NodeId;
            public double SourceS,LegacyWidthArc;
            public Vector3 Centre;
            public float MinimumRadius;
            public bool Endpoint,Bridge;
            public int Layer;
        }
        public sealed class Data
        {
            public Vector3[] Vertices;
            public Vector2[] UV;
            public int[] Triangles;
            public double[] SourceS,LegacyWidthArc;
            public bool[] PreviousSide;
            public float[] Widths;
            public RequiredCap[] RequiredCaps;
            public string SourceRoadDigest;
            public int Columns;
        }
        static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        static void Require(bool ok,string why){if(!ok)throw new InvalidDataException("Elevation strip: "+why);}

        // Width function consumes LEGACY width arc. Elevation consumes EXACT
        // double SourceS plus this row's actual frame. No nearest-road/XZ lookup.
        public static Data Create(GeoRoad road,RoadMarkingPlan.RoadFrame legacyFrame,Func<double,double> widthAtLegacyArc,double maximumWidth,IReadOnlyList<double> requiredSourceStops,RoadLocalElevation elevation,bool bevelAll=false,CancellationToken cancellation=default)
        {
            Require(road!=null&&!road.tunnel&&legacyFrame!=null&&widthAtLegacyArc!=null&&elevation!=null,"explicit source/frame/width/height authority required");
            Require(Finite(maximumWidth)&&maximumWidth>=.1&&maximumWidth<=80,"maximum width");
            Require(requiredSourceStops!=null&&requiredSourceStops.Count<=2048,"required stop budget");
            cancellation.ThrowIfCancellationRequested();
            string digest=RoadSourceIdentity.Compute(new[]{road});
            var metric=new RoadElevationMetric(road);
            var frame=new RoadElevationHorizontalFrame(metric,legacyFrame);
            var seeds=new SortedSet<double>{0,metric.SourceLength};
            var corners=new SortedSet<double>();
            for(int i=1;i<metric.PointCount;i++)
            {
                double s=metric.SourceAtPoint(i);seeds.Add(s);
                if(i==metric.PointCount-1)continue;
                double legacy=frame.LegacySourceDistance(s);
                if(bevelAll||legacyFrame.SharpDistances.Any(v=>Math.Abs(v-legacy)<1e-7))corners.Add(s);
            }
            foreach(double s in requiredSourceStops){Require(Finite(s)&&s>=0&&s<=metric.SourceLength,"foreign/nonfinite source stop");seeds.Add(s);}
            Require(seeds.Count<=4096,"seed row budget");
            var distances=new List<double>{0};var previous=new List<bool>{false};
            double Width(double s,bool incoming)
            {
                double value=widthAtLegacyArc(frame.LegacySourceDistance(s,incoming));
                Require(Finite(value)&&value>=.1&&value<=maximumWidth+1e-6,"width outside explicit maximum");return value;
            }
            Vector3 Point(double s,double fraction,bool incoming)
            {
                cancellation.ThrowIfCancellationRequested();
                frame.At(s,out var centre,out var right,out var dc,out var dr,incoming,bevelAll);
                double lateral=Width(s,incoming)*fraction;
                var p=centre+right*(float)lateral;
                double y=elevation(road.id,s,lateral,centre,right,dc,dr).Elevation;
                Require(Finite(y)&&Math.Abs(y)<20000,"invalid road-local elevation");p.y=(float)y;return p;
            }
            void Append(double s,bool incoming)
            {Require(distances.Count<4096,"adaptive row budget");distances.Add(s);previous.Add(incoming);}
            void Refine(double a,double b,int depth)
            {
                cancellation.ThrowIfCancellationRequested();double middle=(a+b)*.5;
                Require(middle>a&&middle<b,"unrepresentable adaptive interval");
                bool split=metric.ToHorizontal(b)-metric.ToHorizontal(a)>2.5;
                foreach(double fraction in new[]{-.5,0,.5})
                {
                    var x=Point(a,fraction,false);var y=Point(b,fraction,true);var mid=Point(middle,fraction,false);
                    if((mid-(x+y)*.5f).magnitude>.02f)split=true;
                }
                if(split)
                {Require(depth<16&&b-a>.0001,"unresolved surface refinement; no coarse fallback");Refine(a,middle,depth+1);Refine(middle,b,depth+1);}
                else Append(b,corners.Contains(b));
            }
            var stops=seeds.ToArray();
            for(int i=1;i<stops.Length;i++)
            {if(i>1&&corners.Contains(stops[i-1]))Append(stops[i-1],false);Refine(stops[i-1],stops[i],0);}
            // Always include the crown centre. An odd column count would bridge
            // across it and reduce the intended crossfall on narrow roads.
            int columns=Math.Max(2,(int)Math.Ceiling(maximumWidth/2));if((columns&1)!=0)columns++;
            Require((long)distances.Count*(columns+1)<=200000,"vertex budget");
            var vertices=new Vector3[distances.Count*(columns+1)];var uv=new Vector2[vertices.Length];
            var widths=new float[distances.Count];var widthArc=new double[distances.Count];var triangles=new List<int>();
            for(int row=0;row<distances.Count;row++)
            {
                double s=distances[row];widths[row]=(float)Width(s,previous[row]);widthArc[row]=frame.LegacySourceDistance(s,previous[row]);
                for(int col=0;col<=columns;col++)
                {int index=row*(columns+1)+col;var p=Point(s,col/(double)columns-.5,previous[row]);vertices[index]=p;uv[index]=new Vector2(p.x/8,p.z/8);}
                if(row==0||s==distances[row-1])continue;
                for(int col=0;col<columns;col++)
                {int a=(row-1)*(columns+1)+col,b=a+1,c=a+columns+1,d=c+1;triangles.AddRange(new[]{a,c,b,b,c,d});}
            }
            for(int i=0;i<triangles.Count;i+=3)
            {
                cancellation.ThrowIfCancellationRequested();
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                double area=((double)b.z-a.z)*(c.x-a.x)-((double)b.x-a.x)*(c.z-a.z);
                Require(area>1e-10,"folded/degenerate asphalt; explicit frame/cap reauthoring required");
            }
            var capStations=new SortedSet<double>(corners){0,metric.SourceLength};
            var caps=capStations.Select(s=>
            {
                int point=0;while(point+1<metric.PointCount&&metric.SourceAtPoint(point)<s)point++;
                return new RequiredCap{RoadId=road.id,NodeId=road.sourceNodeIds[point],SourceS=s,LegacyWidthArc=frame.LegacySourceDistance(s),Centre=Point(s,0,false),MinimumRadius=(float)Width(s,false)*.5f,Endpoint=s==0||s==metric.SourceLength,Bridge=road.bridge,Layer=road.layer};
            }).ToArray();
            Require(digest==RoadSourceIdentity.Compute(new[]{road}),"source changed during strip preparation");
            cancellation.ThrowIfCancellationRequested();
            return new Data{Vertices=vertices,UV=uv,Triangles=triangles.ToArray(),SourceS=distances.ToArray(),LegacyWidthArc=widthArc,PreviousSide=previous.ToArray(),Widths=widths,RequiredCaps=caps,Columns=columns,SourceRoadDigest=digest};
        }
    }
}
