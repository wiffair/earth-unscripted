using EarthUnscripted.TerrainDraft;using System;using System.Collections.Generic;
namespace EarthUnscripted.TerrainDraft.Core800 {
 public sealed class CanonicalSeamCollar {
  public const string Recipe="ContinuousGroup1/CanonicalSeamCollar1m-Max3mm/ProtectedBand2p01";
  readonly double ox,oz;readonly IReadOnlyList<LocalTerrainSeam> seams;readonly Func<double,double,TerrainMeshSample> original;readonly Func<double,double,bool> terrainPresent;public double MaximumAppliedCorrection{get;private set;}
  public CanonicalSeamCollar(double originX,double originZ,IReadOnlyList<LocalTerrainSeam> canonical,Func<double,double,TerrainMeshSample> field,Func<double,double,bool> present){ox=originX;oz=originZ;seams=canonical;original=field;terrainPresent=present;if(canonical==null||canonical.Count!=4||field==null||present==null)throw new ArgumentException("Canonicalcollar inputs");}
  public TerrainMeshSample World(double x,double z){var source=original(x,z);if(source.Authority==TerrainHeightAuthority.SourcePavement)return source;double lx=x-ox,lz=z-oz,delta=0;foreach(var seam in seams){bool axisX=seam.Axis==0;double fixedWorld=seam.FixedWorld,distance=Math.Abs((axisX?x:z)-fixedWorld);if(distance>=1)continue;double px=axisX?fixedWorld:x,pz=axisX?z:fixedWorld;if(!terrainPresent(px,pz))continue;double along=axisX?pz:px;int low=0,high=seam.Count-1;if(along< (axisX?seam[0].Z:seam[0].X)||along>(axisX?seam[high].Z:seam[high].X))throw new ArgumentException("Collar projection outsidecanonicalseam");while(high-low>1){int mid=(low+high)/2;if((axisX?seam[mid].Z:seam[mid].X)<=along)low=mid;else high=mid;}var a=seam[low];var b=seam[high];double aa=axisX?a.Z:a.X,bb=axisX?b.Z:b.X,t=bb==aa?0:(along-aa)/(bb-aa),target=a.Y+t*(b.Y-a.Y),raw=original(px,pz).Y,residual=target-raw;
    // Eachpoint is within1m of atmosttwoadjacentedges.1.5mm peredge
    // gives3mm total, plusexplicitfinalfloat rounding. No clampingtofitgrade.
    if(Math.Abs(residual)>.0015){var m=original(px,pz);throw new TerrainSeamRefinementRequired(new TerrainPoint(a.X-ox,a.Y,a.Z-oz),new TerrainPoint(b.X-ox,b.Y,b.Z-oz),new TerrainPoint(px-ox,m.Y,pz-oz));}double w=1-distance;w=w*w*(3-2*w);delta+=w*residual;}
   if(Math.Abs(delta)>.0030000001)throw new ArgumentException("Canonicalcollar errorbudget violated");MaximumAppliedCorrection=Math.Max(MaximumAppliedCorrection,Math.Abs(delta));return new TerrainMeshSample(source.Y+delta,source.SourceDistance,source.Authority);
  }
 }
}
