// Frozen0.18 independent regression oracle; Editor-only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace RoadRush.Editor.VerificationSupport
{
    // Cosmetic preparation only. Never edits canonical widths/points, terrain or
    // colliders. The optional width envelope is a proposal, not admitted geometry.
    public sealed class Legacy18RoadMarkingPlan
    {
        public sealed class Stroke
        {
            public string RoadId,ChainId;public Vector3 A,B,RightA,RightB;public double ArcStart,ArcEnd;
            public Vector3 LeftA=>A-RightA*.065f;public Vector3 LeftB=>B-RightB*.065f;
            public Vector3 EdgeA=>A+RightA*.065f;public Vector3 EdgeB=>B+RightB*.065f;
        }
        public sealed class WidthEnvelope
        {
            public string RoadId;public float CanonicalWidth,StartTarget,EndTarget;public double Length,StartRun,EndRun;
            public bool RequiresCollisionAdmission=>StartTarget>CanonicalWidth+.001f||EndTarget>CanonicalWidth+.001f;
            static double Smooth(double t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
            public float At(double sourceDistance)
            {
                double a=StartRun>0?(StartTarget-CanonicalWidth)*Smooth(1-sourceDistance/StartRun):0;
                double b=EndRun>0?(EndTarget-CanonicalWidth)*Smooth(1-(Length-sourceDistance)/EndRun):0;
                return CanonicalWidth+(float)Math.Max(0,Math.Max(a,b));
            }
        }
        sealed class Edge
        {
            public GeoRoad Source;public string Id;public Arm First,Last;public double Length;public WidthEnvelope Width;
            public bool Eligible=>!Source.tunnel&&Source.width>=5.4f;
        }
        sealed class Arm
        {
            public Edge Edge;public bool AtStart;public string Key;public Vector3 Point,Outward;public Arm Continue;public double Blank;
            public Arm Opposite=>AtStart?Edge.Last:Edge.First;
        }
        public sealed class ChainPart {public GeoRoad Road;public bool Forward;public double Start,Length;}
        public sealed class ChainDescription {public string Id;public bool Closed;public double Length;public ChainPart[] Parts;}
        public readonly List<ChainDescription> Continuations=new List<ChainDescription>();
        public sealed class RoadFrame
        {
            public bool Forward;public double Length;public Vector3[] Points,Right,IncomingRight,OutgoingRight;public double[] Arc,SharpDistances;
            public void At(double sourceDistance,out Vector3 centre,out Vector3 right,bool previousSide=false,bool bevelAll=false)
            {
                double distance=Forward?sourceDistance:Length-sourceDistance;distance=Math.Max(0,Math.Min(Length,distance));int lo=0,hi=Arc.Length-1;
                while(hi-lo>1){int m=(lo+hi)/2;if(Arc[m]<=distance)lo=m;else hi=m;}
                bool incoming=previousSide==Forward;
                if(incoming&&lo>0&&Math.Abs(distance-Arc[lo])<.000001){hi=lo;lo--;}
                else if(!incoming&&hi<Arc.Length-1&&Math.Abs(distance-Arc[hi])<.000001){lo=hi;hi++;}
                while(hi<Arc.Length-1&&Arc[hi]-Arc[lo]<.000001)hi++;while(lo>0&&Arc[hi]-Arc[lo]<.000001)lo--;
                double span=Arc[hi]-Arc[lo];float t=span>.000001?(float)Math.Max(0,Math.Min(1,(distance-Arc[lo])/span)):0;centre=Vector3.LerpUnclamped(Points[lo],Points[hi],t);right=bevelAll?Legacy18RoadMarkingPlan.Right(Unit(Points[hi]-Points[lo])):Vector3.LerpUnclamped(OutgoingRight[lo],IncomingRight[hi],t);if(!Forward)right=-right;
            }
        }
        public readonly Dictionary<string,RoadFrame> Frames=new Dictionary<string,RoadFrame>(StringComparer.Ordinal);

        sealed class Span{public Edge Edge;public bool Forward;public double Start;public Vector3[] Points,Right,IncomingRight,OutgoingRight;public bool[] Sharp;public double[] Arc;}
        public readonly List<Stroke> Strokes=new List<Stroke>();public readonly List<WidthEnvelope> Widths=new List<WidthEnvelope>();
        public int Junctions,Chains,Cycles,ShortRoadsBlanked,InteriorCorners;public double PaintedLength;
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static double Length(Vector3 a,Vector3 b){double x=b.x-a.x,y=b.y-a.y,z=b.z-a.z;return Math.Sqrt(x*x+y*y+z*z);}
        static Vector3 Unit(Vector3 value){double m=Math.Sqrt((double)value.x*value.x+(double)value.z*value.z);return m<.000001?Vector3.zero:new Vector3((float)(value.x/m),0,(float)(value.z/m));}
        static double Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.z*b.z;
        static Vector3 Right(Vector3 direction)=>new Vector3(direction.z,0,-direction.x);
        static Vector3 Miter(Vector3 incoming,Vector3 outgoing)
        {
            var normal=Unit(Right(incoming)+Right(outgoing));if(normal.sqrMagnitude<.001f)return Right(outgoing);
            return normal/(float)Math.Max(.6,Dot(normal,Right(outgoing)));
        }
        static string NodeKey(string id,int layer)=>layer.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+id;
        static string ArmOrder(Arm a)=>a.Key+"|"+a.Edge.Id+"|"+a.Opposite.Key+"|"+a.Outward.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+a.Outward.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        public static Legacy18RoadMarkingPlan Build(IEnumerable<GeoRoad> source,Func<GeoRoad,double,float> effectiveWidth=null)
        {
            var result=new Legacy18RoadMarkingPlan();var edges=new List<Edge>();var groups=new Dictionary<string,List<Arm>>(StringComparer.Ordinal);var ids=new HashSet<string>(StringComparer.Ordinal);int pointBudget=0;double networkLength=0;
            var inputs=source?.Take(10001).ToArray()??throw new InvalidDataException("Missing road marking source");if(inputs.Length>10000)throw new InvalidDataException("Road marking edge budget");
            foreach(var r in inputs.OrderBy(r=>r?.id??"",StringComparer.Ordinal))
            {
                if(edges.Count>=10000)throw new InvalidDataException("Road marking edge budget");
                if(r==null||string.IsNullOrEmpty(r.id)||!ids.Add(r.id)||string.IsNullOrEmpty(r.start)||string.IsNullOrEmpty(r.end)||!Finite(r.width)||r.width<2||r.width>80||r.points==null||r.points.Length<2)throw new InvalidDataException("Invalid road marking input");
                pointBudget+=r.points.Length;if(pointBudget>300000)throw new InvalidDataException("Road marking point budget");
                foreach(var p in r.points)if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new InvalidDataException("Nonfinite road marking point");
                if(r.tunnel)continue;var e=new Edge{Id=r.id,Source=r};for(int i=1;i<r.points.Length;i++)e.Length+=Length(r.points[i-1],r.points[i]);
                if(e.Length<.01)continue;networkLength+=e.Length;if(e.Length>100000||networkLength>2000000)throw new InvalidDataException("Road marking arc-distance work budget");
                e.First=new Arm{Edge=e,AtStart=true,Key=NodeKey(r.start,r.layer)+"|"+(r.bridge?"bridge":"ground"),Point=r.points[0],Outward=Unit(r.points[1]-r.points[0])};
                e.Last=new Arm{Edge=e,AtStart=false,Key=NodeKey(r.end,r.layer)+"|"+(r.bridge?"bridge":"ground"),Point=r.points.Last(),Outward=Unit(r.points[r.points.Length-2]-r.points.Last())};
                e.Width=new WidthEnvelope{RoadId=e.Id,CanonicalWidth=r.width,StartTarget=r.width,EndTarget=r.width,Length=e.Length};result.Widths.Add(e.Width);edges.Add(e);
                foreach(var a in new[]{e.First,e.Last}){if(!groups.TryGetValue(a.Key,out var group))groups[a.Key]=group=new List<Arm>();group.Add(a);}
            }
            foreach(var group in groups.Values)
            {
                if(group.Count>64)throw new InvalidDataException("Road marking node degree budget");
                double radius=group.Max(a=>effectiveWidth==null?a.Edge.Source.width:effectiveWidth(a.Edge.Source,a.AtStart?0:a.Edge.Length))*.5+.6;
                bool paired=group.Count==2&&Length(group[0].Point,group[1].Point)<.02&&Dot(group[0].Outward,group[1].Outward)<-.5;
                if(paired)
                {
                    group[0].Continue=group[1];group[1].Continue=group[0];
                    foreach(var arm in group)
                    {
                        float target=Math.Max(arm.Edge.Source.width,arm.Continue.Edge.Source.width);double run=Math.Min(25,Math.Min(arm.Edge.Length*.45,Math.Max(8,3*(target-arm.Edge.Source.width))));
                        if(arm.AtStart){arm.Edge.Width.StartTarget=target;arm.Edge.Width.StartRun=run;}else{arm.Edge.Width.EndTarget=target;arm.Edge.Width.EndRun=run;}
                    }
                }
                else if(group.Count>=2){if(group.Count>=3)result.Junctions++;foreach(var arm in group)arm.Blank=radius;}
            }
            var visited=new HashSet<Edge>();var ordered=edges.SelectMany(e=>new[]{e.First,e.Last}).OrderBy(ArmOrder,StringComparer.Ordinal).ToArray();
            void Chain(Arm first)
            {
                var spans=new List<Span>();Arm entry=first;double distance=0;bool cycle=false;
                while(entry!=null&&!visited.Contains(entry.Edge))
                {
                    var edge=entry.Edge;visited.Add(edge);bool forward=entry.AtStart;var points=(Vector3[])edge.Source.points.Clone();if(!forward)Array.Reverse(points);
                    var s=new Span{Edge=edge,Forward=forward,Start=distance,Points=points,Arc=new double[points.Length],Right=new Vector3[points.Length],IncomingRight=new Vector3[points.Length],OutgoingRight=new Vector3[points.Length],Sharp=new bool[points.Length]};
                    for(int i=1;i<points.Length;i++)s.Arc[i]=s.Arc[i-1]+Length(points[i-1],points[i]);
                    for(int i=0;i<points.Length;i++)
                    {
                        var incoming=i>0?Unit(points[i]-points[i-1]):entry.Continue!=null?-entry.Continue.Outward:Unit(points[1]-points[0]);
                        var exit=entry.Opposite;var outgoing=i+1<points.Length?Unit(points[i+1]-points[i]):exit.Continue!=null?exit.Continue.Outward:Unit(points.Last()-points[points.Length-2]);
                        if(incoming.sqrMagnitude<.001f)incoming=outgoing;if(outgoing.sqrMagnitude<.001f)outgoing=incoming;
                        bool sharp=i>0&&i+1<points.Length&&Dot(incoming,outgoing)<.5;s.Sharp[i]=sharp;
                        s.Right[i]=Miter(incoming,outgoing);s.IncomingRight[i]=sharp?Right(incoming):s.Right[i];s.OutgoingRight[i]=sharp?Right(outgoing):s.Right[i];if(sharp)result.InteriorCorners++;
                    }
                    result.Frames.Add(edge.Id,new RoadFrame{Forward=forward,Length=edge.Length,Points=s.Points,Right=s.Right,IncomingRight=s.IncomingRight,OutgoingRight=s.OutgoingRight,Arc=s.Arc,SharpDistances=Enumerable.Range(0,s.Sharp.Length).Where(i=>s.Sharp[i]).Select(i=>forward?s.Arc[i]:edge.Length-s.Arc[i]).OrderBy(x=>x).ToArray()});
                    spans.Add(s);distance+=edge.Length;entry=entry.Opposite.Continue;
                    if(entry!=null&&visited.Contains(entry.Edge)){cycle=ReferenceEquals(entry.Edge,first.Edge);break;}
                }
                result.Chains++;if(cycle)result.Cycles++;string chainId=first.Key+"/"+first.Edge.Id;
                result.Continuations.Add(new ChainDescription{Id=chainId,Closed=cycle,Length=distance,Parts=spans.Select(s=>new ChainPart{Road=s.Edge.Source,Forward=s.Forward,Start=s.Start,Length=s.Edge.Length}).ToArray()});
                // Closed degree-two loops fit whole periods to the loop, avoiding
                // a phase jump at an arbitrary source boundary. Open chains use12m.
                double period=cycle?distance/Math.Max(1,Math.Round(distance/12)):12,paint=period*5/12;
                foreach(var span in spans)
                {
                    if(!span.Edge.Eligible)continue;
                    double begin=span.Start+(span.Forward?span.Edge.First.Blank:span.Edge.Last.Blank),end=span.Start+span.Edge.Length-(span.Forward?span.Edge.Last.Blank:span.Edge.First.Blank);
                    if(end<=begin){result.ShortRoadsBlanked++;continue;}
                    var blanks=new List<Tuple<double,double>>();for(int i=1;i<span.Points.Length-1;i++)if(span.Sharp[i])
                    {
                        double local=span.Forward?span.Arc[i]:span.Edge.Length-span.Arc[i];double radius=(effectiveWidth==null?span.Edge.Source.width:effectiveWidth(span.Edge.Source,local))*.5+.6;double a=span.Start+span.Arc[i]-radius,b=span.Start+span.Arc[i]+radius;
                        if(blanks.Count>0&&a<=blanks.Last().Item2){var last=blanks.Last();blanks[blanks.Count-1]=Tuple.Create(last.Item1,Math.Max(last.Item2,b));}else blanks.Add(Tuple.Create(a,b));
                    }
                    IEnumerable<Tuple<double,double>> Visible(double a,double b)
                    {
                        int left=0,right=blanks.Count;while(left<right){int mid=(left+right)/2;if(blanks[mid].Item2<=a)left=mid+1;else right=mid;}
                        double cursor=a;for(int i=left;i<blanks.Count&&blanks[i].Item1<b;i++){if(blanks[i].Item1>cursor)yield return Tuple.Create(cursor,Math.Min(b,blanks[i].Item1));cursor=Math.Max(cursor,blanks[i].Item2);if(cursor>=b)yield break;}if(cursor<b)yield return Tuple.Create(cursor,b);
                    }
                    for(int segment=0;segment<span.Points.Length-1;segment++)
                    {
                        double sa=span.Start+span.Arc[segment],sb=span.Start+span.Arc[segment+1],lo=Math.Max(begin,sa),hi=Math.Min(end,sb);if(hi<=lo||sb-sa<.000001)continue;
                        long k=(long)Math.Floor(lo/period);
                        while(k*period<hi)
                        {
                            double a=Math.Max(lo,k*period),b=Math.Min(hi,k*period+paint);
                            if(b-a>.000001)
                            {
                                foreach(var fragment in Visible(a,b))
                                {
                                double visibleA=fragment.Item1,visibleB=fragment.Item2;int pieces=Math.Max(1,(int)Math.Ceiling((visibleB-visibleA)/2.5));
                                for(int piece=0;piece<pieces;piece++)
                                {
                                    if(result.Strokes.Count>=250000)throw new InvalidDataException("Road marking output budget");
                                    double aa=visibleA+(visibleB-visibleA)*piece/pieces,bb=visibleA+(visibleB-visibleA)*(piece+1)/pieces;float ta=(float)((aa-sa)/(sb-sa)),tb=(float)((bb-sa)/(sb-sa));
                                    result.Strokes.Add(new Stroke{RoadId=span.Edge.Id,ChainId=chainId,A=Vector3.LerpUnclamped(span.Points[segment],span.Points[segment+1],ta),B=Vector3.LerpUnclamped(span.Points[segment],span.Points[segment+1],tb),RightA=Vector3.LerpUnclamped(span.OutgoingRight[segment],span.IncomingRight[segment+1],ta),RightB=Vector3.LerpUnclamped(span.OutgoingRight[segment],span.IncomingRight[segment+1],tb),ArcStart=aa,ArcEnd=bb});result.PaintedLength+=bb-aa;
                                }
                                }
                            }
                            k++;
                        }
                    }
                }
            }
            foreach(var arm in ordered)if(arm.Continue==null&&!visited.Contains(arm.Edge))Chain(arm);
            foreach(var arm in ordered)if(!visited.Contains(arm.Edge))Chain(arm);
            return result;
        }
    }
}
