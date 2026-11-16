using System;
using System.Collections.Generic;
using System.Threading;
namespace RoadRush.TerrainDraft
{
 public readonly struct TerrainPoint { public readonly double X,Y,Z; public TerrainPoint(double x,double y,double z){X=x;Y=y;Z=z;} }
 public enum TerrainEdgeRole { Undefined=0, PacketInfluence=1, ExposedGroundConstraint=2 }
 public readonly struct TerrainEdge { public readonly int Group; public readonly TerrainPoint A,B; public readonly TerrainEdgeRole Role; public TerrainEdge(int g,TerrainPoint a,TerrainPoint b,TerrainEdgeRole role){Group=g;A=a;B=b;Role=role;} }
 public sealed class TerrainBudgetException:Exception { public TerrainBudgetException(string s):base(s){} }
 public sealed class TerrainFieldLimits {
  public int MaxEdges=262144,MaxGroups=16384,MaxCells=262144,MaxReferences=4000000,MaxCellCandidates=8192;
  public double CellSize=4,MaxAbsoluteHeight=100000; public int MaxConstraintPairs=1000000,MaxConstraintVisits=8000000;
  public void Validate(){if(!Finite(MaxAbsoluteHeight)||MaxAbsoluteHeight<=0||MaxConstraintPairs<1||MaxConstraintVisits<1||MaxEdges<1||MaxGroups<1||MaxCells<1||MaxReferences<1||MaxCellCandidates<1||!Finite(CellSize)||CellSize<1||CellSize>64)throw new ArgumentException("Invalid finite field budgets");}
  internal static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
 }
 public sealed class TerrainDem {
  public readonly double MinX,MinZ,Width,Depth; public readonly int Nx,Nz; public readonly double MaximumAbsoluteHeight; readonly double[] heights;
  public TerrainDem(double x,double z,double w,double d,int nx,int nz,double[] h){
   if(!TerrainFieldLimits.Finite(x)||!TerrainFieldLimits.Finite(z)||!TerrainFieldLimits.Finite(w)||!TerrainFieldLimits.Finite(d)||w<=0||d<=0||Math.Abs(x)>10000000||Math.Abs(z)>10000000||Math.Abs(x+w)>10000000||Math.Abs(z+d)>10000000||nx<2||nz<2||(long)nx*nz>4000000||h==null||h.Length!=(long)nx*nz)throw new ArgumentException("Invalid finite DEM");
   MinX=x;MinZ=z;Width=w;Depth=d;Nx=nx;Nz=nz;heights=(double[])h.Clone();foreach(var y in heights){if(!TerrainFieldLimits.Finite(y)||Math.Abs(y)>100000)throw new ArgumentException("Nonfinite/unbounded DEM");MaximumAbsoluteHeight=Math.Max(MaximumAbsoluteHeight,Math.Abs(y));}
  }
  public double Height(double x,double z){if(!TerrainFieldLimits.Finite(x)||!TerrainFieldLimits.Finite(z)||x<MinX||z<MinZ||x>MinX+Width||z>MinZ+Depth)throw new ArgumentOutOfRangeException("Outside supplied DEM; no silent clamp/fallback");double gx=(x-MinX)/Width*(Nx-1),gz=(z-MinZ)/Depth*(Nz-1);int ix=Math.Min((int)gx,Nx-2),iz=Math.Min((int)gz,Nz-2);double a=gx-ix,b=gz-iz;return heights[iz*Nx+ix]*(1-a)*(1-b)+heights[iz*Nx+ix+1]*a*(1-b)+heights[(iz+1)*Nx+ix]*(1-a)*b+heights[(iz+1)*Nx+ix+1]*a*b;}
 }
 // Historical numeric-parity prototype, NOT a production-admitted field.
 // Nearest-edge Y selection within one group can jump at its Voronoi boundary:
 // a controlled U-shaped group showed a 4.21875m jump across 2 micrometres.
 // Complete spatial support and Python parity do not prove continuity. Keep
 // this frozen behaviour as a regression oracle while the continuous recipe
 // is verified; do not connect it to normal world rendering/colliders.
 public sealed class CompactTerrainField {
  public const double Radius=16,Power=1.75;
  readonly TerrainEdge[] edges; readonly TerrainDem dem; readonly double cellSize;
  readonly Dictionary<(int,int),int[]> cells; readonly int groupCount; readonly int maxCandidates;
  public readonly int CellCount,ReferenceCount; public int EdgeCount=>edges.Length;
  // Stable first-encounter ordering is important: do not share mutable scratch across workers.
  public sealed class Scratch { internal readonly double[] Distance,Y; internal readonly int[] Stamp,Order; internal int Generation; internal int Busy; internal readonly CompactTerrainField Owner; public int CandidateVisits{get;internal set;} public double NearestSourceDistance{get;internal set;} internal Scratch(int n,CompactTerrainField owner){Owner=owner;Distance=new double[n];Y=new double[n];Stamp=new int[n];Order=new int[n];} }
  public Scratch NewScratch()=>new Scratch(groupCount,this);
  static int Cell(double x,double size){double n=Math.Floor(x/size);if(n<int.MinValue||n>int.MaxValue)throw new ArgumentOutOfRangeException("Cell coordinate");return (int)n;}
  public CompactTerrainField(IReadOnlyList<TerrainEdge> input,int groups,TerrainDem terrain,TerrainFieldLimits limits=null,CancellationToken cancellation=default){
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
   double natural=dem.Height(x,z);double hardZeroY=double.NaN;if(scratch==null||scratch.Distance.Length!=groupCount)throw new ArgumentException("Wrong scratch ownership");cancellation.ThrowIfCancellationRequested();scratch.CandidateVisits=0;scratch.NearestSourceDistance=Radius;
   if(!cells.TryGetValue((Cell(x,cellSize),Cell(z,cellSize)),out var candidates))return natural;if(candidates.Length>maxCandidates)throw new TerrainBudgetException("Query candidate cap");
   if(++scratch.Generation==int.MaxValue){Array.Clear(scratch.Stamp,0,scratch.Stamp.Length);scratch.Generation=1;}int stamp=scratch.Generation,n=0;
   foreach(int index in candidates){if((scratch.CandidateVisits++&127)==0)cancellation.ThrowIfCancellationRequested();var e=edges[index];double dx=e.B.X-e.A.X,dz=e.B.Z-e.A.Z,den=dx*dx+dz*dz,t=Math.Max(0,Math.Min(1,((x-e.A.X)*dx+(z-e.A.Z)*dz)/den));double px=e.A.X+dx*t-x,pz=e.A.Z+dz*t-z,d=Math.Sqrt(px*px+pz*pz);if(d>=Radius)continue;if(d==0&&e.Role==TerrainEdgeRole.ExposedGroundConstraint)hardZeroY=e.A.Y+(e.B.Y-e.A.Y)*t;int g=e.Group;
    if(scratch.Stamp[g]!=stamp){scratch.Stamp[g]=stamp;scratch.Order[n++]=g;scratch.Distance[g]=d;scratch.Y[g]=e.A.Y+(e.B.Y-e.A.Y)*t;}else if(d<scratch.Distance[g]){scratch.Distance[g]=d;scratch.Y[g]=e.A.Y+(e.B.Y-e.A.Y)*t;}
   }
   if(!double.IsNaN(hardZeroY)){scratch.NearestSourceDistance=0;return hardZeroY;}if(n==0)return natural;double best=double.PositiveInfinity,bestY=0;
   for(int i=0;i<n;i++){int g=scratch.Order[i];double d=scratch.Distance[g],y=scratch.Y[g];if(d<best||(d==best&&y<bestY)){best=d;bestY=y;}}
   scratch.NearestSourceDistance=best;if(best==0)return bestY;double sum=0,weighted=0;
   for(int i=0;i<n;i++){int g=scratch.Order[i];double d=scratch.Distance[g],u=d/Radius,c=Math.Pow(1-u,4)*(1+4*u),w=c/Math.Pow(d,Power);sum+=w;weighted+=w*scratch.Y[g];}
   if(!(sum>0)||!TerrainFieldLimits.Finite(sum)||!TerrainFieldLimits.Finite(weighted))throw new ArithmeticException("Nonfinite field weight; reject candidate");double f=best/Radius,blend=f*f*(3-2*f),height=weighted/sum*(1-blend)+natural*blend;if(!TerrainFieldLimits.Finite(height))throw new ArithmeticException("Nonfinite field output");return height;
  }
 }
}
