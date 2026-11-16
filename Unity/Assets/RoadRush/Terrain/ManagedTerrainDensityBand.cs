using System;using System.Collections.Generic;using System.Linq;using Clipper2Lib;
namespace RoadRush.TerrainDraft {
 public static class ManagedTerrainDensityBand {
  // ONLY the deriveddensity partition is quantized here. Originalpavement/raw
  // source/terrain target are untouched. Fine+far stillpartition same target.
  // 2.01m offset -1mm arctolerance -<3µm localrounding retains >2m protection.
  public static Paths64 Build(Paths64 source,long originX,long originZ,int cap){long s=ManagedTerrainDomainLocal.Scale;var raw=Clipper.InflatePaths(source,2.01*s,JoinType.Round,EndType.Polygon,2,.001*s);if(raw.Sum(p=>p.Count)>cap)throw new TerrainBudgetException("Densityband offsetcap");var canonical=new Paths64();foreach(var ring in raw){var path=new Path64();foreach(var p in ring){long x=originX+(long)Math.Round((double)(float)((double)(p.X-originX)/s)*s),z=originZ+(long)Math.Round((double)(float)((double)(p.Y-originZ)/s)*s);if(path.Count==0||path[path.Count-1].X!=x||path[path.Count-1].Y!=z)path.Add(new Point64(x,z));}if(path.Count>1&&path[0].Equals(path[path.Count-1]))path.RemoveAt(path.Count-1);if(path.Count>=3)canonical.Add(path);}var result=Clipper.Union(canonical,FillRule.NonZero);if(result.Sum(p=>p.Count)>cap)throw new TerrainBudgetException("Canonicaldensityband cap");return result;}
 }
}
