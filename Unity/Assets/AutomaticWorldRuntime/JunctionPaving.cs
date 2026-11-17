using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        public sealed class PavingPatch { public PathD outline;public PathsD triangles;public bool island;public double minX,maxX,minZ,maxZ;public Path64 integerOutline;
            public bool Contains(float x,float z)
            {if(integerOutline==null)integerOutline=Clipper.ScalePath64(outline,1000);return Clipper.PointInPolygon(new Point64(new PointD(x,z),1000),integerOutline)!=PointInPolygonResult.IsOutside;}
        }
        static PathD Positive(IEnumerable<Vector3> points)
        {var p=new PathD(points.Select(v=>new PointD(v.x,v.z)));if(Clipper.Area(p)<0)p.Reverse();return p;}
        static PathD Hull(List<Vector3> input)
        {
            var p=input.Select(v=>new PointD(v.x,v.z)).OrderBy(v=>v.x).ThenBy(v=>v.y).ToList();var h=new PathD();
            double Cross(PointD a,PointD b,PointD c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
            foreach(var v in p){while(h.Count>=2&&Cross(h[h.Count-2],h[h.Count-1],v)<=0)h.RemoveAt(h.Count-1);h.Add(v);}int n=h.Count;
            for(int i=p.Count-2;i>=0;i--){var v=p[i];while(h.Count>n&&Cross(h[h.Count-2],h[h.Count-1],v)<=0)h.RemoveAt(h.Count-1);h.Add(v);}if(h.Count>1)h.RemoveAt(h.Count-1);return h;
        }
        static float LineDistance(Feature f,PointD p)
        {float best=float.MaxValue;for(int i=1;i<f.points.Length;i++){var a=f.points[i-1];var b=f.points[i];var d=new Vector2(b.x-a.x,b.z-a.z);float t=Mathf.Clamp01(Vector2.Dot(new Vector2((float)p.x-a.x,(float)p.y-a.z),d)/Mathf.Max(.00001f,d.sqrMagnitude));best=Mathf.Min(best,Vector2.Distance(new Vector2((float)p.x,(float)p.y),new Vector2(a.x,a.z)+d*t));}return best;}
        // Slice at every contour vertex height. Between adjacent slices the
        // sorted boundary edges form exact trapezoids, including holes and
        // contours touching at a point; no diagonal can cut across an island.
        static PathsD PavingTriangles(PathsD contours)
        {
            var levels=contours.SelectMany(p=>p).Select(p=>p.y).Distinct().OrderBy(v=>v).ToArray();var triangles=new PathsD();
            var edges=new List<(PointD a,PointD b)>();foreach(var p in contours)for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];if(Math.Abs(a.y-b.y)>1e-9)edges.Add((a,b));}
            double X((PointD a,PointD b) e,double y)=>e.a.x+(e.b.x-e.a.x)*(y-e.a.y)/(e.b.y-e.a.y);
            void Triangle(PointD a,PointD b,PointD c){var t=new PathD{a,b,c};double area=Clipper.Area(t);if(Math.Abs(area)<1e-10)return;if(area<0)t.Reverse();triangles.Add(t);}
            for(int i=1;i<levels.Length;i++)
            {
                double low=levels[i-1],high=levels[i],middle=(low+high)*.5;
                var crossing=edges.Where(e=>middle>Math.Min(e.a.y,e.b.y)&&middle<Math.Max(e.a.y,e.b.y)).OrderBy(e=>X(e,middle)).ToArray();
                if(crossing.Length%2!=0)throw new InvalidOperationException("Unclosed junction paving contour");
                for(int j=0;j<crossing.Length;j+=2){var l=crossing[j];var r=crossing[j+1];var a=new PointD(X(l,low),low);var b=new PointD(X(r,low),low);var c=new PointD(X(r,high),high);var d=new PointD(X(l,high),high);Triangle(a,b,c);Triangle(a,c,d);}
            }
            if(Math.Abs(Clipper.Area(triangles)-Clipper.Area(contours))>.005)throw new InvalidOperationException("Junction paving triangle area mismatch");return triangles;
        }
        // Work on mapped same-level connections, never an arbitrary X/Z crossing.
        // Existing closed pockets are protected before adding junction pavement.
        public static List<PavingPatch> PlanJunctionPaving(Map source)
        {
            var roads=source.features.Where(f=>f.category=="road"&&!f.bridge&&!f.tunnel&&f.layer==0&&(string.IsNullOrEmpty(f.level)||f.level=="0")).ToArray();
            bool bounded=source.coreMaxX>source.coreMinX&&source.coreMaxZ>source.coreMinZ;
            float west=source.coreMinX-128,east=source.coreMaxX+128,south=source.coreMinZ-128,north=source.coreMaxZ+128;
            bool Near(Vector3 a,Vector3 b)=>!bounded||Mathf.Max(a.x,b.x)>=west&&Mathf.Min(a.x,b.x)<=east&&Mathf.Max(a.z,b.z)>=south&&Mathf.Min(a.z,b.z)<=north;
            var roadBounds=roads.Select(f=>(f,minX:f.points.Min(p=>p.x)-f.width*.5f-1,maxX:f.points.Max(p=>p.x)+f.width*.5f+1,minZ:f.points.Min(p=>p.z)-f.width*.5f-1,maxZ:f.points.Max(p=>p.z)+f.width*.5f+1)).ToArray();
            var ribbons=new PathsD();var nodes=new Dictionary<string,List<(Feature f,int i)>>();
            foreach(var f in roads)
            {
                for(int i=1;i<f.points.Length;i++)
                {
                    if(!Near(f.points[i-1],f.points[i]))continue;
                    var a=f.points[i-1]+Join(f.points,i-1)*f.roadCentreOffset;var b=f.points[i]+Join(f.points,i)*f.roadCentreOffset;float half=f.width*.5f;
                    ribbons.Add(Positive(new[]{a-Join(f.points,i-1)*half,b-Join(f.points,i)*half,b+Join(f.points,i)*half,a+Join(f.points,i-1)*half}));
                }
                for(int i=0;i<(f.sourceNodeIds?.Length??0);i++)if(Near(f.points[i],f.points[i])&&f.sourceNodeIds[i].StartsWith("osm:",StringComparison.Ordinal))
                {if(!nodes.TryGetValue(f.sourceNodeIds[i],out var list))nodes[f.sourceNodeIds[i]]=list=new List<(Feature,int)>();list.Add((f,i));}
            }
            var union=Clipper.Union(ribbons,new PathsD(),FillRule.NonZero,3);var protectedSpace=new PathsD();var islands=new PathsD();
            foreach(var hole in union.Where(p=>Clipper.Area(p)<-.15))
            {
                var positive=new PathD(hole);positive.Reverse();double area=Clipper.Area(positive);protectedSpace.Add(positive);
                // Roundabout centres, large landscaped islands and enclosed blocks
                // stay intact. Small pockets between split one-way arms become
                // raised paved refuges with their exact original footprint.
                var boundary=positive[0];var adjacent=roadBounds.Where(b=>boundary.x>=b.minX&&boundary.x<=b.maxX&&boundary.y>=b.minZ&&boundary.y<=b.maxZ).Select(b=>b.f).Where(f=>LineDistance(f,boundary)<=f.width*.5f+1).ToArray();
                if(area<=150&&!adjacent.Any(f=>f.roundabout)&&adjacent.Where(f=>f.oneway).Select(f=>f.sourceId).Distinct().Count()>=2)islands.Add(positive);
            }
            var mouths=new PathsD();
            foreach(var entries in nodes.Values)
            {
                if(entries.Select(e=>e.f.sourceId).Distinct().Count()<2)continue;
                var points=new List<Vector3>();
                foreach(var e in entries)
                {
                    var f=e.f;int i=e.i;float reach=Mathf.Clamp(f.width*.5f,2,7);
                    var anchor=f.points[i]+Join(f.points,i)*f.roadCentreOffset;points.Add(anchor-Join(f.points,i)*f.width*.5f);points.Add(anchor+Join(f.points,i)*f.width*.5f);
                    foreach(int sign in new[]{-1,1})
                    {
                        int j=i+sign;if(j<0||j>=f.points.Length)continue;float remaining=reach;var a=f.points[i];var target=a;
                        for(;j>=0&&j<f.points.Length;j+=sign){var b=f.points[j];float length=Vector3.ProjectOnPlane(b-a,Vector3.up).magnitude;if(length>=remaining){target=Vector3.Lerp(a,b,remaining/Mathf.Max(.001f,length));break;}remaining-=length;target=b;a=b;}
                        var direction=Vector3.ProjectOnPlane(target-f.points[i],Vector3.up).normalized;var side=new Vector3(direction.z,0,-direction.x)*sign;target+=side*f.roadCentreOffset;
                        points.Add(target-side*f.width*.5f);points.Add(target+side*f.width*.5f);
                    }
                }
                if(points.Count>=3)mouths.Add(Hull(points));
                var arms=new List<(Vector3 direction,float half,float length)>();
                foreach(var e in entries)foreach(int sign in new[]{-1,1})
                {
                    int j=e.i+sign;if(j<0||j>=e.f.points.Length)continue;
                    var direction=Vector3.ProjectOnPlane(e.f.points[j]-e.f.points[e.i],Vector3.up).normalized;
                    if(direction.sqrMagnitude<.5f||arms.Any(a=>Vector3.Dot(a.direction,direction)>.995f))continue;
                    float length=0;var a=e.f.points[e.i];
                    for(;j>=0&&j<e.f.points.Length;j+=sign){var delta=Vector3.ProjectOnPlane(e.f.points[j]-a,Vector3.up);if(Vector3.Dot(delta.normalized,direction)<.9f)break;length+=delta.magnitude;a=e.f.points[j];if(length>=35)break;}
                    arms.Add((direction,e.f.width*.5f,length));
                }
                arms=arms.OrderBy(a=>Mathf.Atan2(a.direction.z,a.direction.x)).ToList();
                for(int n=0;n<arms.Count;n++)
                {
                    var first=arms[n];var second=arms[(n+1)%arms.Count];
                    float angle=Mathf.Repeat(Mathf.Atan2(second.direction.z,second.direction.x)-Mathf.Atan2(first.direction.z,first.direction.x),2*Mathf.PI);
                    if(angle<.26f||angle>Mathf.PI-.15f)continue;
                    var u=new Vector2(first.direction.x,first.direction.z);var v=new Vector2(second.direction.x,second.direction.z);var nu=new Vector2(-u.y,u.x);var nv=new Vector2(v.y,-v.x);
                    float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
                    var p1=nu*first.half;var p2=nv*second.half;float cross=Cross(u,v);if(Mathf.Abs(cross)<.001f)continue;
                    var corner=p1+u*(Cross(p2-p1,v)/cross);
                    float radius=Mathf.Clamp(Mathf.Min(first.half,second.half)*1.5f,3,8);
                    float available=Mathf.Min(first.length-Vector2.Dot(corner,u),second.length-Vector2.Dot(corner,v))-.5f;
                    radius=Mathf.Min(radius,Mathf.Max(0,available)*Mathf.Tan(angle*.5f));if(radius<.75f)continue;
                    var c1=p1+nu*radius;var c2=p2+nv*radius;var centre=c1+u*(Cross(c2-c1,v)/cross);var tangent=centre-nu*radius;
                    float start=Mathf.Atan2(tangent.y-centre.y,tangent.x-centre.x);int count=Mathf.CeilToInt((Mathf.PI-angle)*radius/.35f);
                    var origin=entries[0].f.points[entries[0].i];var fillet=new List<Vector3>{origin+new Vector3(corner.x,0,corner.y)};
                    for(int k=0;k<=count;k++){float a=start-(Mathf.PI-angle)*k/count;var point=centre+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;fillet.Add(origin+new Vector3(point.x,0,point.y));}
                    mouths.Add(Positive(fillet));
                }
            }
            var blocked=new PathsD(protectedSpace);
            foreach(var house in source.buildings??Array.Empty<Building>())if(house.points.Length>=3)blocked.AddRange(Clipper.InflatePaths(new PathsD{Positive(house.points)},.35,JoinType.Round,EndType.Polygon));
            var infill=Clipper.Difference(Clipper.Difference(Clipper.Union(mouths,new PathsD(),FillRule.NonZero,3),union,FillRule.NonZero,3),blocked,FillRule.NonZero,3);
            var result=new List<PavingPatch>();
            void Add(PathsD contours,bool island)
            {
                var clean=contours.Select(p=>Clipper.TrimCollinear(p,3)).Where(p=>p.Count>=3&&Math.Abs(Clipper.Area(p))>.0001).ToArray();
                // Assign each hole once. Repeating this search for every outer
                // contour becomes cubic in dense town-centre road networks.
                var outers=clean.Where(p=>Clipper.Area(p)>0).Select(p=>(path:p,area:Clipper.Area(p),minX:p.Min(v=>v.x),maxX:p.Max(v=>v.x),minZ:p.Min(v=>v.y),maxZ:p.Max(v=>v.y),group:new PathsD{p})).OrderBy(p=>p.area).ToArray();
                foreach(var hole in clean.Where(p=>Clipper.Area(p)<0))
                {
                    var point=hole[0];
                    foreach(var outer in outers)if(point.x>=outer.minX&&point.x<=outer.maxX&&point.y>=outer.minZ&&point.y<=outer.maxZ&&Clipper.PointInPolygon(point,outer.path,3)!=PointInPolygonResult.IsOutside){outer.group.Add(hole);break;}
                }
                foreach(var outer in outers)
                {
                    var triangles=PavingTriangles(outer.group);
                    foreach(var triangle in triangles)if(Math.Abs(Clipper.Area(triangle))>.0001){var canonical=new PathD(triangle.Select(v=>new PointD((float)v.x,(float)v.y)));if(Math.Abs(Clipper.Area(canonical))>.0001)result.Add(new PavingPatch{outline=canonical,triangles=new PathsD{canonical},island=island});}
                }
            }
            Add(infill,false);Add(Clipper.Difference(islands,new PathsD((source.buildings??Array.Empty<Building>()).Select(h=>Positive(h.points))),FillRule.NonZero,3),true);return result;
        }
        void CacheJunctionPaving(Map source,string sourceHash)
        {
            if(finishedStore==null)return;
            string key=FinishedGeometryStore.Digest("junction-paving-rounded-v2|"+EarthUnscripted.GeometryCacheVersion.Identity+"|"+sourceHash);
            var parts=finishedStore.Read(key).GetAwaiter().GetResult();
            if(parts!=null&&parts.Length==2&&parts.All(p=>p.vertices.Length%3==0))
            {
                source.preparedPaving=new List<PavingPatch>();
                for(int layer=0;layer<2;layer++)for(int i=0;i<parts[layer].vertices.Length;i+=3)
                {var outline=new PathD();for(int j=0;j<3;j++){var v=parts[layer].vertices[i+j];outline.Add(new PointD(v.x,v.z));}source.preparedPaving.Add(new PavingPatch{outline=outline,island=layer==1});}
                return;
            }
            source.preparedPaving=PlanJunctionPaving(source);parts=new FinishedGeometryStore.Part[2];
            for(int layer=0;layer<2;layer++)
            {
                var vertices=source.preparedPaving.Where(p=>p.island==(layer==1)).SelectMany(p=>p.outline).Select(v=>new Vector3((float)v.x,0,(float)v.y)).ToArray();
                parts[layer]=new FinishedGeometryStore.Part{vertices=vertices,normals=Enumerable.Repeat(Vector3.up,vertices.Length).ToArray(),indices=Enumerable.Range(0,vertices.Length).ToArray()};
            }
            finishedStore.Write(key,parts);
        }
        static void PrepareJunctionPaving(Map source)
        {
            source.paving=new Dictionary<Vector2Int,List<PavingPatch>>();
            foreach(var patch in source.preparedPaving??PlanJunctionPaving(source))
            {patch.integerOutline=Clipper.ScalePath64(patch.outline,1000);patch.minX=patch.outline.Min(p=>p.x)-.001;patch.maxX=patch.outline.Max(p=>p.x)+.001;patch.minZ=patch.outline.Min(p=>p.y)-.001;patch.maxZ=patch.outline.Max(p=>p.y)+.001;
             int x0=Mathf.FloorToInt((float)patch.minX/ChunkSize),x1=Mathf.FloorToInt((float)patch.maxX/ChunkSize),z0=Mathf.FloorToInt((float)patch.minZ/ChunkSize),z1=Mathf.FloorToInt((float)patch.maxZ/ChunkSize);
             for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){var key=new Vector2Int(x,z);if(!source.paving.TryGetValue(key,out var list))source.paving[key]=list=new List<PavingPatch>();list.Add(patch);}}
        }
        List<PavingPatch> PavingAt(float x,float z)
        {var source=AreaAt(x,z)??workingMap;return source?.paving!=null&&source.paving.TryGetValue(new Vector2Int(Mathf.FloorToInt(x/ChunkSize),Mathf.FloorToInt(z/ChunkSize)),out var patches)?patches:null;}
        float PavingHeight(float x,float z)
        {
            var nearby=Nearby(x,z);float best=float.MaxValue,height=RawHeight(x,z);
            if(nearby!=null)foreach(var s in nearby){var f=s.feature;if(f.category!="road"||f.bridge||f.tunnel||f.layer!=0)continue;float d=Project(s,x,z,out float y);if(d>=best)continue;best=d;height=RoadSurfacePoint(f,new Vector3(x,y,z)).y;}
            return height;
        }
        bool PavedGround(float x,float z,out float height)
        {height=0;var patches=PavingAt(x,z);if(patches==null)return false;foreach(var p in patches)if(x>=p.minX&&x<=p.maxX&&z>=p.minZ&&z<=p.maxZ&&p.Contains(x,z)){height=PavingHeight(x,z)-.20f;return true;}return false;}
        IEnumerable<bool> JunctionPavingRows(Batch asphaltBatch,Batch islandBatch,float x,float z)
        {
            var patches=PavingAt(x+.5f*ChunkSize,z+.5f*ChunkSize);if(patches==null)yield break;
            var clip=new PathsD{new PathD{new PointD(x,z),new PointD(x+ChunkSize,z),new PointD(x+ChunkSize,z+ChunkSize),new PointD(x,z+ChunkSize)}};
            foreach(var patch in patches)
            {
                var contours=Clipper.Intersect(new PathsD{patch.outline},clip,FillRule.NonZero,3);float lift=patch.island?.12f:0;
                foreach(var polygon in contours)
                {
                    var vertices=polygon.Select(p=>new Vector3((float)p.x,PavingHeight((float)p.x,(float)p.y)+lift,(float)p.y)).ToArray();var batch=patch.island?islandBatch:asphaltBatch;
                    for(int i=1;i<vertices.Length-1;i++)RoadTopTriangle(batch,vertices[0],vertices[i],vertices[i+1]);
                    // Solid walls underneath raised patches. Internal triangle
                    // edges are coplanar; they do not expose a line above the top.
                    for(int i=0;i<vertices.Length;i++){int j=(i+1)%vertices.Length;batch.Quad(vertices[i]+Vector3.down*(.24f+lift),vertices[j]+Vector3.down*(.24f+lift),vertices[j],vertices[i]);}
                }
                yield return true;
            }
        }
    }
}
