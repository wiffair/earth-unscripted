using System;using System.Collections.Generic;using System.Numerics;using System.Threading;
namespace RoadRush.TerrainDraft {
 internal static class TerrainConstraintAdmission {
  // Exact orientation for finite actualbinary32 XZ. 2^149 scaling handles
  // subnormal coordinates without a floating epsilon or long overflow.
  static BigInteger Exact(float x){int bits=BitConverter.SingleToInt32Bits(x),exp=(bits>>23)&255,m=bits&0x7fffff;BigInteger n=exp==0?m:(m|0x800000);if(exp!=0)n<<=exp-1;return bits<0?-n:n;}
  static BigInteger Cross(TerrainPoint a,TerrainPoint b,TerrainPoint c){var ax=Exact((float)a.X);var az=Exact((float)a.Z);return(Exact((float)b.X)-ax)*(Exact((float)c.Z)-az)-(Exact((float)b.Z)-az)*(Exact((float)c.X)-ax);}
  static double Y(TerrainEdge e,double coordinate,bool useX){double a=useX?e.A.X:e.A.Z,b=useX?e.B.X:e.B.Z;return e.A.Y+(e.B.Y-e.A.Y)*((coordinate-a)/(b-a));}
  static void Pair(TerrainEdge a,TerrainEdge b,double tolerance){
   if(Math.Max(a.A.X,a.B.X)<Math.Min(b.A.X,b.B.X)||Math.Max(b.A.X,b.B.X)<Math.Min(a.A.X,a.B.X)||Math.Max(a.A.Z,a.B.Z)<Math.Min(b.A.Z,b.B.Z)||Math.Max(b.A.Z,b.B.Z)<Math.Min(a.A.Z,a.B.Z))return;
   var aa=Cross(a.A,a.B,b.A);var ab=Cross(a.A,a.B,b.B);var ba=Cross(b.A,b.B,a.A);var bb=Cross(b.A,b.B,a.B);if(aa.Sign*ab.Sign>0||ba.Sign*bb.Sign>0)return;
   if(aa.IsZero&&ab.IsZero){bool useX=Math.Abs(a.B.X-a.A.X)>=Math.Abs(a.B.Z-a.A.Z);double a0=useX?a.A.X:a.A.Z,a1=useX?a.B.X:a.B.Z,b0=useX?b.A.X:b.A.Z,b1=useX?b.B.X:b.B.Z,lo=Math.Max(Math.Min(a0,a1),Math.Min(b0,b1)),hi=Math.Min(Math.Max(a0,a1),Math.Max(b0,b1));if(hi<lo)return;if(Math.Abs(Y(a,lo,useX)-Y(b,lo,useX))>tolerance||Math.Abs(Y(a,hi,useX)-Y(b,hi,useX))>tolerance)throw new ArgumentException("Conflicting coincident exposed-ground constraints; no lower-height tie fallback");return;}
   // Intersection parameter t=ba/(ba-bb), u=aa/(aa-ab); exact orientation
   // determines incidence, floating conversion only interpolates bounded Y.
   double t=(double)ba/(double)(ba-bb),u=(double)aa/(double)(aa-ab);double ay=a.A.Y+t*(a.B.Y-a.A.Y),by=b.A.Y+u*(b.B.Y-b.A.Y);if(Math.Abs(ay-by)>tolerance)throw new ArgumentException("Conflicting crossing exposed-ground constraints; bridge/tunnel layer classification required before ground admission");
  }
  public static void Validate(TerrainEdge[] edges,Dictionary<(int,int),int[]> cells,int pairCap,int visitCap,double tolerance,CancellationToken cancellation){
   int hard=0;for(int i=0;i<edges.Length;i++)if(edges[i].Role==TerrainEdgeRole.ExposedGroundConstraint){hard++;var e=edges[i];foreach(var p in new[]{e.A,e.B})if((double)(float)p.X!=p.X||(double)(float)p.Z!=p.Z)throw new ArgumentException("Hard constraints require canonical actualbinary32 XZ");}if(hard<2)return;
   long visits=0;var seen=new HashSet<ulong>();foreach(var list in cells.Values){cancellation.ThrowIfCancellationRequested();for(int i=0;i<list.Length;i++){int a=list[i];if(edges[a].Role!=TerrainEdgeRole.ExposedGroundConstraint)continue;for(int j=i+1;j<list.Length;j++){if(++visits>visitCap)throw new TerrainBudgetException("Ground-constraint comparison work cap");if((visits&1023)==0)cancellation.ThrowIfCancellationRequested();int b=list[j];if(edges[b].Role!=TerrainEdgeRole.ExposedGroundConstraint)continue;ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);if(seen.Contains(key))continue;if(seen.Count>=pairCap)throw new TerrainBudgetException("Ground-constraint pair admission cap; no partial input");seen.Add(key);if((seen.Count&127)==0)cancellation.ThrowIfCancellationRequested();Pair(edges[a],edges[b],tolerance);}}}
  }
 }
}
