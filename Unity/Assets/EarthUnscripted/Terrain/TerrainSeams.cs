using System;using System.Collections.Generic;using System.Threading;
namespace EarthUnscripted.TerrainDraft
{
 public readonly struct TerrainSeamKey:IEquatable<TerrainSeamKey> {
  // Axis0: fixedX, varyingZ. Axis1: fixedZ, varyingX. Globalinteger32m lattice.
  public readonly int Axis,FixedCell,AlongCell;public TerrainSeamKey(int axis,int fixedCell,int alongCell){if(axis!=0&&axis!=1)throw new ArgumentException("Seam axis");Axis=axis;FixedCell=fixedCell;AlongCell=alongCell;}
  public bool Equals(TerrainSeamKey k)=>Axis==k.Axis&&FixedCell==k.FixedCell&&AlongCell==k.AlongCell;public override bool Equals(object o)=>o is TerrainSeamKey k&&Equals(k);public override int GetHashCode()=>unchecked((Axis*397^FixedCell)*397^AlongCell);
 }
 public readonly struct SeamHeight { public readonly float Y;public readonly double DistanceToSource; public SeamHeight(float y,double distance){if(!TerrainFieldLimits.Finite(y)||!TerrainFieldLimits.Finite(distance)||distance<0)throw new ArgumentException("Seam sample");Y=y;DistanceToSource=distance;} }
 public sealed class TerrainSeam {
  public readonly TerrainSeamKey Key;readonly TerrainPoint[] points;public int Count=>points.Length;public TerrainPoint this[int i]=>points[i];public readonly int QueryCount;
  internal TerrainSeam(TerrainSeamKey key,TerrainPoint[] p,int queries){Key=key;points=p;QueryCount=queries;}
 }
 public sealed class TerrainSeamPlanner {
  public const int CellMetres=32;readonly int maxKnots,maxQueries;readonly double error;
  public TerrainSeamPlanner(int knotCap=4096,int queryCap=16384,double errorMetres=.003){if(knotCap<2||queryCap<3||errorMetres<=0)throw new ArgumentException("Seam budgets");maxKnots=knotCap;maxQueries=queryCap;error=errorMetres;}
  // Query MUST include actual-source pavement plane precedence when on its footprint.
  // Mandatory knots MUST already include all canonical pavement/DEM/seam intersections.
  // This method never guesses missing source constraints or certifies2D triangles.
  public TerrainSeam Build(TerrainSeamKey key,IReadOnlyList<float> mandatoryAlong,Func<float,float,SeamHeight> query,CancellationToken cancellation=default){
   if(query==null||mandatoryAlong==null||mandatoryAlong.Count>maxKnots)throw new TerrainBudgetException("Mandatory seam knot cap");float fixedCoordinate=checked(key.FixedCell*CellMetres),lo=checked(key.AlongCell*CellMetres),hi=checked((key.AlongCell+1)*CellMetres);var knots=new SortedSet<float>{lo,hi};foreach(float v in mandatoryAlong){if(!TerrainFieldLimits.Finite(v)||v<lo||v>hi)throw new ArgumentException("Mandatory seam bound");knots.Add(v);}var cache=new Dictionary<float,SeamHeight>();
   SeamHeight At(float t){cancellation.ThrowIfCancellationRequested();if(cache.TryGetValue(t,out var value))return value;if(cache.Count>=maxQueries)throw new TerrainBudgetException("Seam field query cap");value=key.Axis==0?query(fixedCoordinate,t):query(t,fixedCoordinate);cache.Add(t,value);return value;}
   var pending=new Stack<(float,float,int)>();float? previous=null;foreach(float t in knots){At(t);if(previous.HasValue)pending.Push((previous.Value,t,0));previous=t;}
   while(pending.Count>0){cancellation.ThrowIfCancellationRequested();var edge=pending.Pop();float a=edge.Item1,b=edge.Item2,mid=(float)(((double)a+b)*.5);var av=At(a);var bv=At(b);var mv=At(mid);double length=(double)b-a,lower=Math.Max(0,mv.DistanceToSource-length*.5);double maxEdge=lower<=2?.7:Math.Min(16,.7+(lower-2)*.9);double err=Math.Abs(mv.Y-((double)av.Y+bv.Y)*.5);
    if(length<=maxEdge&&err<=error)continue;if(edge.Item3>=24||mid==a||mid==b||length<=.002)throw new TerrainBudgetException("Unresolved protected seam resolution");if(knots.Count>=maxKnots)throw new TerrainBudgetException("Refined seam knot cap");knots.Add(mid);pending.Push((mid,b,edge.Item3+1));pending.Push((a,mid,edge.Item3+1));
   }
   var result=new TerrainPoint[knots.Count];int i=0;foreach(float t in knots){var h=At(t);result[i++]=key.Axis==0?new TerrainPoint(fixedCoordinate,h.Y,t):new TerrainPoint(t,h.Y,fixedCoordinate);}return new TerrainSeam(key,result,cache.Count);
  }
 }
 // Preflight/residency accounting, not a claim that all these meshes already exist.
 public sealed class TerrainResidentBudget {
  public readonly long MaxVertices,MaxTriangles; public readonly int MaxChunks;long vertices,triangles;readonly Dictionary<(int,int),(int,int)> chunks=new Dictionary<(int,int),(int,int)>();
  public TerrainResidentBudget(long maxVertices,long maxTriangles,int maxChunks=81){if(maxVertices<1||maxTriangles<1||maxChunks<1||maxChunks>4096)throw new ArgumentException("Resident budgets");MaxVertices=maxVertices;MaxTriangles=maxTriangles;MaxChunks=maxChunks;}
  public bool TryAdmitReplacement(int x,int z,int v,int t){if(v<0||t<0)throw new ArgumentException("Counts");if(!chunks.ContainsKey((x,z))&&chunks.Count>=MaxChunks)return false;chunks.TryGetValue((x,z),out var old);long nv=vertices-old.Item1+v,nt=triangles-old.Item2+t;if(nv>MaxVertices||nt>MaxTriangles)return false;chunks[(x,z)]=(v,t);vertices=nv;triangles=nt;return true;}
  public long NumericAndRendererBufferLowerBoundBytes=>52*vertices+24*triangles;
  public void Release(int x,int z){if(chunks.TryGetValue((x,z),out var old)){vertices-=old.Item1;triangles-=old.Item2;chunks.Remove((x,z));}}
 }
}
