using System;using System.Collections.Generic;using System.Threading;
namespace RoadRush.TerrainDraft
{
 public sealed class ContinuousCompactTerrainField {
  public const double Radius=16,Power=1.75;
  readonly TerrainEdge[] edges; readonly TerrainDem dem; readonly double cellSize;
  readonly Dictionary<(int,int),int[]> cells; readonly int groupCount; readonly int maxCandidates;
  public readonly int CellCount,ReferenceCount; public int EdgeCount=>edges.Length;
  // Stable first-encounter ordering is important: do not share mutable scratch across workers.
  public sealed class Scratch { internal readonly double[] Distance,Y,SumRelative,WeightedRelative; internal readonly int[] Stamp,Order; internal int Generation; internal int Busy; internal readonly ContinuousCompactTerrainField Owner; public int CandidateVisits{get;internal set;} public double NearestSourceDistance{get;internal set;} internal Scratch(int n,ContinuousCompactTerrainField owner){Owner=owner;Distance=new double[n];Y=new double[n];SumRelative=new double[n];WeightedRelative=new double[n];Stamp=new int[n];Order=new int[n];} }
  public Scratch NewScratch()=>new Scratch(groupCount,this);
  static int Cell(double x,double size){double n=Math.Floor(x/size);if(n<int.MinValue||n>int.MaxValue)throw new ArgumentOutOfRangeException("Cell coordinate");return (int)n;}
  public ContinuousCompactTerrainField(IReadOnlyList<TerrainEdge> input,int groups,TerrainDem terrain,TerrainFieldLimits limits=null,CancellationToken cancellation=default){
   var l=limits??new TerrainFieldLimits();l.Validate();if(input==null||terrain==null||groups<1||groups>l.MaxGroups||input.Count>l.MaxEdges)throw new TerrainBudgetException("Source field budget");if(terrain.MaximumAbsoluteHeight>l.MaxAbsoluteHeight)throw new ArgumentException("DEM height limit");dem=terrain;groupCount=groups;cellSize=l.CellSize;maxCandidates=l.MaxCellCandidates;edges=new TerrainEdge[input.Count];var pending=new Dictionary<(int,int),List<int>>();int references=0;
   for(int i=0;i<input.Count;i++){
    if((i&127)==0)cancellation.ThrowIfCancellationRequested();var e=input[i];if(e.Role!=TerrainEdgeRole.PacketInfluence&&e.Role!=TerrainEdgeRole.ExposedGroundConstraint)throw new ArgumentException("Explicit edge role required");if(e.Group<0||e.Group>=groups)throw new ArgumentException("Invalid group");foreach(var p in new[]{e.A,e.B})if(!TerrainFieldLimits.Finite(p.X)||!TerrainFieldLimits.Finite(p.Y)||!TerrainFieldLimits.Finite(p.Z)||Math.Abs(p.X)>10000000||Math.Abs(p.Z)>10000000||Math.Abs(p.Y)>l.MaxAbsoluteHeight)throw new ArgumentException("Invalid source coordinate");edges[i]=e;
    if(e.A.X==e.B.X&&e.A.Z==e.B.Z)throw new ArgumentException("Zero-XZ edge rejected; cannot discard vertical source height");
    int x0=Cell(Math.Min(e.A.X,e.B.X)-Radius,cellSize),x1=Cell(Math.Max(e.A.X,e.B.X)+Radius,cellSize),z0=Cell(Math.Min(e.A.Z,e.B.Z)-Radius,cellSize),z1=Cell(Math.Max(e.A.Z,e.B.Z)+Radius,cellSize);
    long count=((long)x1-x0+1)*((long)z1-z0+1);if(count>l.MaxReferences-references)throw new TerrainBudgetException("Spatial reference preflight cap");
    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){
     if((references&1023)==0)cancellation.ThrowIfCancellationRequested();if(!pending.TryGetValue((x,z),out var list)){if(pending.Count>=l.MaxCells)throw new TerrainBudgetException("Spatial cell cap");pending.Add((x,z),list=new List<int>());}if(list.Count>=l.MaxCellCandidates)throw new TerrainBudgetException("Per-cell source density cap; no truncation");list.Add(i);references++;
    }
   }
   cancellation.ThrowIfCancellationRequested();cells=new Dictionary<(int,int),int[]>(pending.Count);foreach(var kv in pending)cells.Add(kv.Key,kv.Value.ToArray());CellCount=cells.Count;ReferenceCount=references;TerrainConstraintAdmission.Validate(edges,cells,l.MaxConstraintPairs,l.MaxConstraintVisits,.0002,cancellation);
  }
  public double Height(double x,double z,Scratch scratch,CancellationToken cancellation=default){
   if(scratch==null||!ReferenceEquals(scratch.Owner,this))throw new ArgumentException("Scratch belongs to another field");if(Interlocked.Exchange(ref scratch.Busy,1)!=0)throw new InvalidOperationException("Concurrent scratch reuse is forbidden; one scratch per worker");try{return HeightCore(x,z,scratch,cancellation);}finally{Volatile.Write(ref scratch.Busy,0);}
  }
  double HeightCore(double x,double z,Scratch scratch,CancellationToken cancellation){
   double natural=dem.Height(x,z);double hardZeroY=double.NaN,zeroY=double.NaN;bool zeroConflict=false;if(scratch==null||scratch.Distance.Length!=groupCount)throw new ArgumentException("Wrong scratch ownership");cancellation.ThrowIfCancellationRequested();scratch.CandidateVisits=0;scratch.NearestSourceDistance=Radius;
   if(!cells.TryGetValue((Cell(x,cellSize),Cell(z,cellSize)),out var candidates))return natural;if(candidates.Length>maxCandidates)throw new TerrainBudgetException("Query candidate cap");
   if(++scratch.Generation==int.MaxValue){Array.Clear(scratch.Stamp,0,scratch.Stamp.Length);scratch.Generation=1;}int stamp=scratch.Generation,n=0;
   foreach(int index in candidates){if((scratch.CandidateVisits++&127)==0)cancellation.ThrowIfCancellationRequested();var e=edges[index];double dx=e.B.X-e.A.X,dz=e.B.Z-e.A.Z,den=dx*dx+dz*dz,t=Math.Max(0,Math.Min(1,((x-e.A.X)*dx+(z-e.A.Z)*dz)/den));double px=e.A.X+dx*t-x,pz=e.A.Z+dz*t-z,d=Math.Sqrt(px*px+pz*pz);if(d>=Radius)continue;if(d==0){double y=e.A.Y+(e.B.Y-e.A.Y)*t;if(double.IsNaN(zeroY))zeroY=y;else if(Math.Abs(y-zeroY)>.0002)zeroConflict=true;}if(d==0&&e.Role==TerrainEdgeRole.ExposedGroundConstraint)hardZeroY=e.A.Y+(e.B.Y-e.A.Y)*t;int g=e.Group;
    if(scratch.Stamp[g]!=stamp){scratch.Stamp[g]=stamp;scratch.Order[n++]=g;scratch.SumRelative[g]=scratch.WeightedRelative[g]=0;scratch.Distance[g]=d;scratch.Y[g]=e.A.Y+(e.B.Y-e.A.Y)*t;}else if(d<scratch.Distance[g]){scratch.Distance[g]=d;scratch.Y[g]=e.A.Y+(e.B.Y-e.A.Y)*t;}
   }
   if(!double.IsNaN(hardZeroY)){scratch.NearestSourceDistance=0;return hardZeroY;}if(zeroConflict)throw new ArgumentException("Coincident differentheight packet influences require actualsourcepavement authority before terrainfield query");if(n==0)return natural;double best=double.PositiveInfinity,bestY=0;
   for(int i=0;i<n;i++){int g=scratch.Order[i];double d=scratch.Distance[g],y=scratch.Y[g];if(d<best||(d==best&&y<bestY)){best=d;bestY=y;}}
   scratch.NearestSourceDistance=best;if(best==0)return bestY;// Continuous projection within eachpacket: never selectY by Voronoi tie.
   // Relative weights are <=1, avoid singular intermediate source-height sums.
   foreach(int index in candidates){if((scratch.CandidateVisits++&127)==0)cancellation.ThrowIfCancellationRequested();var e=edges[index];double dx=e.B.X-e.A.X,dz=e.B.Z-e.A.Z,t=Math.Max(0,Math.Min(1,((x-e.A.X)*dx+(z-e.A.Z)*dz)/(dx*dx+dz*dz)));double px=e.A.X+dx*t-x,pz=e.A.Z+dz*t-z,d=Math.Sqrt(px*px+pz*pz);if(d>=Radius)continue;int g=e.Group;double dg=scratch.Distance[g],u=d/Radius,ug=dg/Radius,cutoff=Math.Pow(1-u,4)*(1+4*u),nearCutoff=Math.Pow(1-ug,4)*(1+4*ug);double relative=(cutoff/nearCutoff)*Math.Pow(dg/d,Power);scratch.SumRelative[g]+=relative;scratch.WeightedRelative[g]+=relative*(e.A.Y+(e.B.Y-e.A.Y)*t);}
   double sum=0,weighted=0;
   for(int i=0;i<n;i++){int g=scratch.Order[i];double d=scratch.Distance[g],u=d/Radius,c=Math.Pow(1-u,4)*(1+4*u),w=c/Math.Pow(d,Power);sum+=w;weighted+=w*(scratch.WeightedRelative[g]/scratch.SumRelative[g]);}
   if(!(sum>0)||!TerrainFieldLimits.Finite(sum)||!TerrainFieldLimits.Finite(weighted))throw new ArithmeticException("Nonfinite field weight; reject candidate");double f=best/Radius,blend=f*f*(3-2*f),height=weighted/sum*(1-blend)+natural*blend;if(!TerrainFieldLimits.Finite(height))throw new ArithmeticException("Nonfinite field output");return height;
  }
 }
}
