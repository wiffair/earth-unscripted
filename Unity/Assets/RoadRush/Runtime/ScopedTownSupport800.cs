using System;using System.Threading;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using RoadRush.Core800;
namespace RoadRush
{
    // Physical support/clearance from final triangles only. Raw indexes serve
    // semantic metadata/water queries; raw point.y never supplies a site height.
    public sealed partial class ScopedTownSupport800
    {
        readonly PreparedWorldMeshPlan800 plan;readonly GeoRegion region;
        readonly List<Face> faces=new List<Face>();readonly Dictionary<(int,int),List<int>> cells=new Dictionary<(int,int),List<int>>();
        readonly struct P{internal readonly double x,y,z;internal P(double x,double y,double z){this.x=x;this.y=y;this.z=z;}public static implicit operator P(Vector3 v)=>new P(v.x,v.y,v.z);internal Vector3 Vector()=>new Vector3((float)x,(float)y,(float)z);}
        struct Face{internal P A,B,C;internal GeoRoad Road;internal double AccessClearance;}
        int spatialReferences;const double Cell=8;public int PavementFaces=>faces.Count;
        public ScopedTownSupport800(PreparedWorldMeshPlan800 plan,GeoRegion region,CancellationToken token=default)
        {
            this.plan=plan??throw new ArgumentNullException(nameof(plan));this.region=region??throw new ArgumentNullException(nameof(region));var seen=new Dictionary<(string,int),int>();
            foreach(var road in region.roads.OrderBy(r=>r.id,StringComparer.Ordinal)){token.ThrowIfCancellationRequested();plan.VisitNamedTriangles(road.id,t=>{if(seen.TryGetValue((t.PacketId,t.Triangle),out int existing)){var shared=faces[existing];shared.AccessClearance=Math.Max(shared.AccessClearance,AccessMargin(road));faces[existing]=shared;return;}var f=new Face{A=new P(t.A.X,t.A.Y,t.A.Z),B=new P(t.B.X,t.B.Y,t.B.Z),C=new P(t.C.X,t.C.Y,t.C.Z),Road=road,AccessClearance=AccessMargin(road)};int id=faces.Count;seen.Add((t.PacketId,t.Triangle),id);faces.Add(f);if(faces.Count>1000000)throw new InvalidDataException("Scoped town pavement budget");Bounds(f,0,out int x0,out int z0,out int x1,out int z1);for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){if(!cells.TryGetValue((x,z),out var group))cells.Add((x,z),group=new List<int>());if(group.Count>=24000||++spatialReferences>12000000)throw new InvalidDataException("Scoped town pavement spatial budget");group.Add(id);}},token);}
        }
        static int K(double x)=>(int)Math.Floor(x/Cell);
        static void Bounds(Face f,double margin,out int x0,out int z0,out int x1,out int z1){x0=K(Math.Min(f.A.x,Math.Min(f.B.x,f.C.x))-margin);z0=K(Math.Min(f.A.z,Math.Min(f.B.z,f.C.z))-margin);x1=K(Math.Max(f.A.x,Math.Max(f.B.x,f.C.x))+margin);z1=K(Math.Max(f.A.z,Math.Max(f.B.z,f.C.z))+margin);}
        IEnumerable<Face> Candidates(double x0,double z0,double x1,double z1){var seen=new HashSet<int>();for(int z=K(z0);z<=K(z1);z++)for(int x=K(x0);x<=K(x1);x++)if(cells.TryGetValue((x,z),out var group))foreach(int id in group)if(seen.Add(id))yield return faces[id];}
        public bool HasSurface(Vector3 p)=>plan.TryGround(p.x,p.z,out _);
        public float RequireHeight(float x,float z){if(!plan.TryGround(x,z,out var h))throw new MissingScopedSupportException(x,z);return(float)h.Height;}
        public bool ContainsFootprintWithReach(Vector3[] p,double reach=4){return p!=null&&p.Length>=4&&p.All(v=>plan.Contains(v.x-reach,v.z-reach)&&plan.Contains(v.x+reach,v.z+reach));}
        public bool ClearPoint(Vector3 p,double margin){if(!plan.Contains(p.x,p.z))return false;foreach(var f in Candidates(p.x-margin,p.z-margin,p.x+margin,p.z+margin))if(Distance(p,f,out _)<=margin)return false;return true;}
        static double AccessMargin(GeoRoad road)=>road.kind=="residential"||road.kind=="service"?.61:3.01;
        public bool ClearAccess(Vector3 p)
        {if(!plan.TryPlacementGround(p.x,p.z,out _))return false;foreach(var f in Candidates(p.x-3.01,p.z-3.01,p.x+3.01,p.z+3.01))if(Distance(p,f,out _)<=f.AccessClearance)return false;return true;}
        public bool IntersectsAccessPavement(Vector3[] polygon)
        {double x0=polygon.Min(p=>p.x)-3.01,z0=polygon.Min(p=>p.z)-3.01,x1=polygon.Max(p=>p.x)+3.01,z1=polygon.Max(p=>p.z)+3.01;foreach(var f in Candidates(x0,z0,x1,z1))if(Overlaps(f,polygon,f.AccessClearance))return true;return false;}
        public GeoRegion.RoadProjection NearestPavement(Vector3 p)
        {
            double best=50;Face chosen=default;P q=default;bool found=false;
            foreach(var f in Candidates(p.x-50,p.z-50,p.x+50,p.z+50)){double d=Distance(p,f,out var at);if(d<best){best=d;chosen=f;q=at;found=true;}}
            // Separation here is final pavement distance, not a raw centreline estimate.
            return found?new GeoRegion.RoadProjection(chosen.Road,q.Vector(),(float)best,0,0):new GeoRegion.RoadProjection(null,Vector3.zero,float.PositiveInfinity,0,0);
        }
        public bool IntersectsPavement(Vector3[] polygon,double margin)
        {
            if(polygon==null||polygon.Length<3)throw new ArgumentException("Scoped clearance polygon required");double x0=polygon.Min(p=>p.x)-margin,z0=polygon.Min(p=>p.z)-margin,x1=polygon.Max(p=>p.x)+margin,z1=polygon.Max(p=>p.z)+margin;
            foreach(var f in Candidates(x0,z0,x1,z1))
            {
                if(Overlaps(f,polygon,margin))return true;
            }return false;
        }
        // Generic numeric clearance predicate, not an admission/capability factory.
        public static bool TriangleOverlapsPolygon(double[] xz,Vector3[] polygon,double margin)
        {
            if(xz==null||xz.Length!=6||xz.Any(x=>double.IsNaN(x)||double.IsInfinity(x))||polygon==null||polygon.Length<3||polygon.Length>4096||double.IsNaN(margin)||double.IsInfinity(margin)||margin<0||margin>16)throw new ArgumentException("Bounded triangle/polygon margin required");
            if(polygon.Any(p=>float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.z)||float.IsInfinity(p.z)))throw new ArgumentException("Finite polygon required");var f=new Face{A=new P(xz[0],0,xz[1]),B=new P(xz[2],0,xz[3]),C=new P(xz[4],0,xz[5])};if(Cross(f.A,f.B,f.C)==0)throw new ArgumentException("Nondegenerate triangle required");return Overlaps(f,polygon,margin);
        }
        static bool Overlaps(Face f,Vector3[] polygon,double margin)
        {
            var tri=new[]{f.A,f.B,f.C};foreach(var p in polygon)if(Distance(p,f,out _)<=margin)return true;foreach(var p in tri)if(InsidePolygon(p,polygon))return true;
            for(int i=0;i<polygon.Length;i++)for(int j=0;j<3;j++)if(SegmentsNear(polygon[i],polygon[(i+1)%polygon.Length],tri[j],tri[(j+1)%3],margin))return true;return false;
        }
        static bool InsidePolygon(P p,Vector3[] poly){bool inside=false;for(int i=0,j=poly.Length-1;i<poly.Length;j=i++){var a=poly[i];var b=poly[j];if((a.z>p.z)!=(b.z>p.z)&&p.x<((double)b.x-a.x)*(p.z-a.z)/((double)b.z-a.z)+a.x)inside=!inside;}return inside;}
        static double Cross(P a,P b,P p)=>((double)b.x-a.x)*(p.z-a.z)-((double)b.z-a.z)*(p.x-a.x);
        static bool SegmentsNear(P a,P b,P c,P d,double margin){double abC=Cross(a,b,c),abD=Cross(a,b,d),cdA=Cross(c,d,a),cdB=Cross(c,d,b);if(abC*abD<=0&&cdA*cdB<=0&&Math.Max(Math.Min(a.x,b.x),Math.Min(c.x,d.x))<=Math.Min(Math.Max(a.x,b.x),Math.Max(c.x,d.x))&&Math.Max(Math.Min(a.z,b.z),Math.Min(c.z,d.z))<=Math.Min(Math.Max(a.z,b.z),Math.Max(c.z,d.z)))return true;return SegmentDistance(a,c,d,out _)<=margin||SegmentDistance(b,c,d,out _)<=margin||SegmentDistance(c,a,b,out _)<=margin||SegmentDistance(d,a,b,out _)<=margin;}
        static double SegmentDistance(P p,P a,P b,out P q){double x=(double)b.x-a.x,z=(double)b.z-a.z,n=x*x+z*z,t=n==0?0:Math.Max(0,Math.Min(1,(((double)p.x-a.x)*x+((double)p.z-a.z)*z)/n));q=new P(a.x+x*t,a.y+(b.y-a.y)*t,a.z+z*t);return Math.Sqrt(Math.Pow((double)p.x-q.x,2)+Math.Pow((double)p.z-q.z,2));}
        static double Distance(P p,Face f,out P q){double den=Cross(f.A,f.B,f.C),u=Cross(f.A,p,f.C)/den,v=Cross(f.A,f.B,p)/den;if(u>=0&&v>=0&&u+v<=1){q=new P(p.x,f.A.y+u*(f.B.y-f.A.y)+v*(f.C.y-f.A.y),p.z);return 0;}double best=SegmentDistance(p,f.A,f.B,out q),d=SegmentDistance(p,f.B,f.C,out var at);if(d<best){best=d;q=at;}d=SegmentDistance(p,f.C,f.A,out at);if(d<best){best=d;q=at;}return best;}
    }
    public sealed class MissingScopedSupportException:Exception{public readonly float X,Z;public MissingScopedSupportException(float x,float z):base("No final800 support at "+x+","+z){X=x;Z=z;}}
}
