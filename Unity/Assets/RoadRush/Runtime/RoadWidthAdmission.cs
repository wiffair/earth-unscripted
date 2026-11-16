using System;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace RoadRush
{
    // Draft: atomic per continuation chain. Only new paving, after subtraction of
    // the authoritative old pavement union, is checked. No canonical mutation.
    public static class RoadWidthAdmission
    {
        public sealed class Obstacle{public string Id,Kind;public Vector2[] Ring;public float Margin;public int Layer;public bool Bridge,Tunnel;}
        public sealed class Decision{public string ChainId,Status;public string[] RoadIds,Conflicts,RejectedOutwardConflicts;public float InwardInsetMetres;public double NewTriangleAreaUpperBound;public RoadWidthProfileDto[] Profiles;}
        public sealed class Result{public Decision[] Decisions;public RoadWidthProfileDto[] Profiles;public int BlockedChains,AdmittedChains;}
        public sealed class ObstacleIndex
        {
            const float Cell=32;readonly Dictionary<(int,int),List<Obstacle>> buckets=new Dictionary<(int,int),List<Obstacle>>();
            static int Grid(float v)=>(int)Math.Floor(v/Cell);
            public ObstacleIndex(IEnumerable<Obstacle> obstacles)
            {
                int count=0,pointCount=0;long memberships=0;foreach(var o in obstacles){if(++count>100000||o?.Ring==null||o.Ring.Length<2||o.Ring.Length>4096||!Finite(o.Margin)||o.Margin<0||o.Margin>50)throw new InvalidDataException("Admission obstacle budget");if((pointCount+=o.Ring.Length)>500000)throw new InvalidDataException("Admission total obstacle point budget");if(o.Ring.Any(p=>!Finite(p.x)||!Finite(p.y)))throw new InvalidDataException("Nonfinite admission obstacle");
                    float x0=o.Ring.Min(p=>p.x)-o.Margin,z0=o.Ring.Min(p=>p.y)-o.Margin,x1=o.Ring.Max(p=>p.x)+o.Margin,z1=o.Ring.Max(p=>p.y)+o.Margin;long visits=(long)(Grid(x1)-Grid(x0)+1)*(Grid(z1)-Grid(z0)+1);if(visits>65536||(memberships+=visits)>2000000)throw new InvalidDataException("Admission obstacle extent budget");for(int z=Grid(z0);z<=Grid(z1);z++)for(int x=Grid(x0);x<=Grid(x1);x++){if(!buckets.TryGetValue((x,z),out var list))buckets[(x,z)]=list=new List<Obstacle>();list.Add(o);}}
            }
            internal IEnumerable<Obstacle> Query(Vector3[] triangle)
            {float x0=triangle.Min(p=>p.x),z0=triangle.Min(p=>p.z),x1=triangle.Max(p=>p.x),z1=triangle.Max(p=>p.z);var found=new HashSet<Obstacle>();if((long)(Grid(x1)-Grid(x0)+1)*(Grid(z1)-Grid(z0)+1)>4096)throw new InvalidDataException("Admission triangle extent budget");for(int z=Grid(z0);z<=Grid(z1);z++)for(int x=Grid(x0);x<=Grid(x1);x++)if(buckets.TryGetValue((x,z),out var list))foreach(var o in list){if(o.Ring.Max(p=>p.x)+o.Margin<x0||o.Ring.Min(p=>p.x)-o.Margin>x1||o.Ring.Max(p=>p.y)+o.Margin<z0||o.Ring.Min(p=>p.y)-o.Margin>z1)continue;if(found.Add(o)&&found.Count>4096)throw new InvalidDataException("Admission local obstacle density budget");}return found.OrderBy(o=>o.Kind,StringComparer.Ordinal).ThenBy(o=>o.Id,StringComparer.Ordinal);}
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        static double Cross(Vector2 a,Vector2 b,Vector2 c)=>((double)b.x-a.x)*(c.y-a.y)-((double)b.y-a.y)*(c.x-a.x);
        static double Distance(Vector2 p,Vector2 a,Vector2 b){double x=(double)b.x-a.x,z=(double)b.y-a.y,t=((p.x-a.x)*x+(p.y-a.y)*z)/Math.Max(1e-16,x*x+z*z);t=Math.Max(0,Math.Min(1,t));x=p.x-a.x-t*x;z=p.y-a.y-t*z;return Math.Sqrt(x*x+z*z);}
        static bool Inside(Vector2 p,Vector2[] ring){bool inside=false;for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){var a=ring[i];var b=ring[j];if(Distance(p,a,b)<1e-6)return true;if((a.y>p.y)!=(b.y>p.y)&&p.x<(double)(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;}return inside;}
        static bool Segments(Vector2 a,Vector2 b,Vector2 c,Vector2 d)=>Cross(a,b,c)*Cross(a,b,d)<=0&&Cross(c,d,a)*Cross(c,d,b)<=0&&Math.Max(a.x,b.x)>=Math.Min(c.x,d.x)&&Math.Max(c.x,d.x)>=Math.Min(a.x,b.x)&&Math.Max(a.y,b.y)>=Math.Min(c.y,d.y)&&Math.Max(c.y,d.y)>=Math.Min(a.y,b.y);
        static bool Touches(Vector3[] triangle,Obstacle obstacle)
        {
            var t=triangle.Select(p=>new Vector2(p.x,p.z)).ToArray();var r=obstacle.Ring;if(r.Length>=3&&(t.Any(p=>Inside(p,r))||r.Any(p=>Inside(p,t))))return true;int edges=r.Length==2?1:r.Length;
            for(int i=0;i<3;i++)for(int j=0;j<edges;j++){var a=t[i];var b=t[(i+1)%3];var c=r[j];var d=r[(j+1)%r.Length];if(Segments(a,b,c,d)||Math.Min(Math.Min(Distance(a,c,d),Distance(b,c,d)),Math.Min(Distance(c,a,b),Distance(d,a,b)))<=obstacle.Margin+1e-5)return true;}return false;
        }
        // Caller freezes all managed region/profile/index data and supplies a pure,
        // thread-safe height sampler. Never pass UnityEngine.Object or mutable memo.
        public static Task<Result> EvaluateWorker(RoadWidthTaperPlan proposal,RoadMarkingPlan topology,RoadBankExteriorClip.SpatialIndex oldPavement,ObstacleIndex obstacles,Func<float,float,float> surface,CancellationToken token)
        {return Task.Run(()=>{Result value=null;foreach(var step in EvaluateSteps(proposal,topology,oldPavement,obstacles,surface,r=>value=r)){token.ThrowIfCancellationRequested();}token.ThrowIfCancellationRequested();return value;},token);}
        public static Result Evaluate(RoadWidthTaperPlan proposal,RoadMarkingPlan topology,RoadBankExteriorClip.SpatialIndex oldPavement,ObstacleIndex obstacles,Func<float,float,float> surface)
        {Result value=null;foreach(var step in EvaluateSteps(proposal,topology,oldPavement,obstacles,surface,r=>value=r)){}return value;}
        public static IEnumerable<int> EvaluateSteps(RoadWidthTaperPlan proposal,RoadMarkingPlan topology,RoadBankExteriorClip.SpatialIndex oldPavement,ObstacleIndex obstacles,Func<float,float,float> surface,Action<Result> finish)
        {
            var decisions=new List<Decision>();var all=new List<RoadWidthProfileDto>();foreach(var chain in proposal.Chains.OrderBy(c=>c.Id,StringComparer.Ordinal))
            {
                var parts=chain.Parts.Select(p=>proposal.Roads[p.Road.id]).ToArray();var conflicts=new SortedSet<string>(StringComparer.Ordinal);double area=0;bool expanded=parts.Any(p=>p.RequiresExpandedFootprint)||chain.Inward&&parts.Any(p=>Math.Abs(p.WidthAt(0)-p.Road.width)>.001||Math.Abs(p.WidthAt(p.Length)-p.Road.width)>.001||p.Chain.Stations.Any(st=>st.Distance>=p.Start&&st.Distance<=p.Start+p.Length&&Math.Abs(st.Width-p.Road.width)>.001));
                // Deck/barrier/foundation remodelling is not implemented by this draft.
                foreach(var part in parts){float floor=Math.Max(2,Math.Min(part.Road.width,(part.Road.kind=="service"?6.5f:8.5f)-(chain.Inward?chain.InwardInsetMetres:0)));if(part.ToDto().widths.Min()<floor-.0001f)conflicts.Add("comfortable-minimum:"+part.Road.id);}
                if(expanded&&chain.HasBridge)conflicts.Add("structural-admission:bridge-collar");
                if(expanded&&!chain.HasBridge)foreach(var part in parts)
                {
                    var chainDtos=parts.Select(p=>p.ToDto()).ToArray();var chainField=new EffectiveRoadWidthField(chainDtos,parts.Select(p=>p.Road).ToArray());float maximum=parts.Max(p=>chainField.Maximum(p.Road));var mesh=RoadWidthStripGeometry.Create(part.Road,chainDtos.Single(p=>p.roadId==part.Road.id),topology.Frames[part.Road.id],maximum,surface);
                    IEnumerable<int> Check(Vector3[] triangle)
                    {
                        RoadBankExteriorClip.Data fresh=null;foreach(var step in RoadBankExteriorClip.ClipSteps(triangle,part.Road.layer,part.Road.bridge,oldPavement,d=>fresh=d))yield return step;
                        for(int i=0;i<fresh.Triangles.Length;i+=3){var tri=new[]{fresh.Vertices[fresh.Triangles[i]],fresh.Vertices[fresh.Triangles[i+1]],fresh.Vertices[fresh.Triangles[i+2]]};double patch=Math.Abs(Cross(new Vector2(tri[0].x,tri[0].z),new Vector2(tri[1].x,tri[1].z),new Vector2(tri[2].x,tri[2].z)))*.5;if(patch<1e-6)continue;area+=patch;int obstacleWork=0;foreach(var obstacle in obstacles.Query(tri)){if(Touches(tri,obstacle))conflicts.Add(obstacle.Kind+":"+obstacle.Id);if(++obstacleWork%32==0)yield return 0;}yield return 0;}
                    }
                    for(int i=0;i<mesh.Triangles.Length;i+=3)foreach(var step in Check(new[]{mesh.Vertices[mesh.Triangles[i]],mesh.Vertices[mesh.Triangles[i+1]],mesh.Vertices[mesh.Triangles[i+2]]}))yield return step;
                    foreach(var corner in mesh.Corners)for(int i=0;i<32;i++){double a=i*Math.PI/16,b=(i+1)*Math.PI/16;var p=corner.Centre+new Vector3((float)Math.Cos(a)*corner.Radius,0,(float)Math.Sin(a)*corner.Radius);var q=corner.Centre+new Vector3((float)Math.Cos(b)*corner.Radius,0,(float)Math.Sin(b)*corner.Radius);p.y=surface(p.x,p.z);q.y=surface(q.x,q.z);foreach(var step in Check(new[]{corner.Centre,p,q}))yield return step;}
                }
                bool blocked=conflicts.Count>0;var profiles=parts.Select(p=>blocked?new RoadWidthProfileDto{roadId=p.Road.id,distances=new[]{0f,(float)p.Length},widths=new[]{p.Road.width,p.Road.width},derivatives=new[]{0f,0f}}:p.ToDto()).OrderBy(p=>p.roadId,StringComparer.Ordinal).ToArray();all.AddRange(profiles);decisions.Add(new Decision{ChainId=chain.Id,Status=blocked?"blocked-original-width-step":expanded?(chain.Inward?"admitted-inward-geometric-clearance":"admitted-geometric-clearance"):"unchanged",RoadIds=parts.Select(p=>p.Road.id).ToArray(),Conflicts=conflicts.ToArray(),NewTriangleAreaUpperBound=area,Profiles=profiles});yield return 0;
            }
            finish(new Result{Decisions=decisions.ToArray(),Profiles=all.ToArray(),BlockedChains=decisions.Count(d=>d.Status.StartsWith("blocked")),AdmittedChains=decisions.Count(d=>d.Status.StartsWith("admitted"))});
        }
        public static Obstacle[] RegionObstacles(GeoRegion region,float buildingMargin=.3f,float railMargin=1,float waterMargin=.3f)
        {
            var list=new List<Obstacle>();foreach(var b in region.buildings??Array.Empty<GeoBuilding>())list.Add(new Obstacle{Id=b.id,Kind="building",Margin=buildingMargin,Ring=b.points.Select(p=>new Vector2(p.x,p.z)).ToArray()});
            foreach(var rail in region.railways??Array.Empty<GeoRailway>())if(RailwayMetrics.Visible(rail.kind)&&!rail.tunnel)for(int i=1;i<rail.points.Length;i++)list.Add(new Obstacle{Id=rail.id+"/"+i,Kind="rail-conservative-vertical-review",Margin=rail.width*.5f+railMargin,Ring=new[]{new Vector2(rail.points[i-1].x,rail.points[i-1].z),new Vector2(rail.points[i].x,rail.points[i].z)},Layer=rail.layer,Bridge=rail.bridge});
            foreach(var water in region.waters??Array.Empty<GeoWater>())for(int i=1;i<water.points.Length;i++)list.Add(new Obstacle{Id=water.name+"/"+i,Kind="water",Margin=water.width*.5f+waterMargin,Ring=new[]{new Vector2(water.points[i-1].x,water.points[i-1].z),new Vector2(water.points[i].x,water.points[i].z)}});return list.ToArray();
        }
    }
}
