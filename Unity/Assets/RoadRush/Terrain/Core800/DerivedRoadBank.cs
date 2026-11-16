using System;using System.Collections.Generic;using System.Linq;using RoadRush.TerrainDraft;
namespace RoadRush.TerrainDraft.Core800 {
// Explicit fictional cut-bank policy; not a surveyed bank, traversable terrain,
// source alteration, or a scene-wide terrain slope exception.
public sealed class DerivedRoadBank {
 public const string Recipe="DerivedRoadBank/PantsideInterRoad59659261-401498635/1";
 public const string UpperPacket="ground-body-59659261-span-8",LowerPacket="ground-body-401498635-span-0";
 readonly RoadTerrainSource source;readonly (TerrainPoint A,TerrainPoint B)[] upper,lower;
 public readonly string SourceEdgeDigest;
 public DerivedRoadBank(RoadTerrainSource source){this.source=source;var packets=source.CopyPackets();upper=Edges(packets.Single(p=>p.Id==UpperPacket));lower=Edges(packets.Single(p=>p.Id==LowerPacket));SourceEdgeDigest=VerifiedJoinedTerrain.Hash(string.Join("|",upper.Concat(lower).Select(e=>string.Join(",",new[]{e.A.X,e.A.Y,e.A.Z,e.B.X,e.B.Y,e.B.Z}.Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture))))));}
 static (TerrainPoint,TerrainPoint)[] Edges(RoadTerrainSourcePacket p){var counts=new Dictionary<(int,int),int>();for(int i=0;i<p.Indices.Length;i+=3)foreach(var e in new[]{(p.Indices[i],p.Indices[i+1]),(p.Indices[i+1],p.Indices[i+2]),(p.Indices[i+2],p.Indices[i])}){var k=(Math.Min(e.Item1,e.Item2),Math.Max(e.Item1,e.Item2));counts.TryGetValue(k,out int n);counts[k]=n+1;}TerrainPoint V(int i)=>new TerrainPoint(p.OriginX+p.XYZ[i*3],p.OriginY+p.XYZ[i*3+1],p.OriginZ+p.XYZ[i*3+2]);return counts.Where(k=>k.Value==1).Select(k=>(V(k.Key.Item1),V(k.Key.Item2))).ToArray();}
 static double Distance((TerrainPoint A,TerrainPoint B)[] edges,double x,double z){double best=double.PositiveInfinity;foreach(var e in edges){double dx=e.B.X-e.A.X,dz=e.B.Z-e.A.Z,t=Math.Max(0,Math.Min(1,((x-e.A.X)*dx+(z-e.A.Z)*dz)/(dx*dx+dz*dz))),xx=e.A.X+t*dx-x,zz=e.A.Z+t*dz-z;best=Math.Min(best,Math.Sqrt(xx*xx+zz*zz));}return best;}
 public bool Contains(double x,double z){if(x< -16||x> -6||z< -108||z> -96)return false;if(source.Pavement(x,z,out _,out _,out _))return false;return Distance(upper,x,z)<=4&&Distance(lower,x,z)<=4;}
 public bool Allows(TerrainPoint a,TerrainPoint b,TerrainPoint c,double ox=0,double oz=0){foreach(var p in new[]{a,b,c,new TerrainPoint((a.X+b.X)/2,0,(a.Z+b.Z)/2),new TerrainPoint((a.X+c.X)/2,0,(a.Z+c.Z)/2),new TerrainPoint((b.X+c.X)/2,0,(b.Z+c.Z)/2),new TerrainPoint((a.X+b.X+c.X)/3,0,(a.Z+b.Z+c.Z)/3)})if(!Contains(ox+p.X,oz+p.Z))return false;return true;}
}
}
