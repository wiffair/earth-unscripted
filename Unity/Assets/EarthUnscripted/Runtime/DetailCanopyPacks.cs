using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Reviewed relative heights only. Absolute LiDAR elevation never replaces terrain.
    public sealed class DetailCanopyPack
    {
        public readonly string Identity,Attribution,Id,RevisionSeries;public readonly long? Revision;readonly double[] bounds;readonly JArray entries,coverage;
        public DetailCanopyPack(string folder)
        {
            string manifest=Path.Combine(folder,"canopy-pack.json");if(new FileInfo(manifest).Length>256000)throw new InvalidDataException("Canopy manifest budget");
            var meta=JObject.Parse(File.ReadAllText(manifest));
            if((string)meta["format"]!="EarthUnscriptedCanopy1"||(string)meta["licence"]!="OGL-3.0"||(bool?)meta["activationAllowed"]!=true||(bool?)meta["offlineAllowed"]!=true||(bool?)meta["peerRedistributionAllowed"]!=true||(string)meta["outputCRS"]!="EPSG:4326"||(string)meta["heightReference"]!="relative-DSM-minus-DTM"||(string)meta["clearancePolicy"]!=DetailCanopyPacks.Recipe||(bool?)meta["terrainReplacementAllowed"]!=false)
                throw new InvalidDataException("Unreviewed canopy format/rights/relative-height policy");
            if(!(meta["sources"] is JArray sources)||sources.Count!=3||!new HashSet<string>(sources.Select(t=>(string)t["role"])).SetEquals(new[]{"DTM","DSM","buildingFootprints"})||sources.Any(t=>!System.Text.RegularExpressions.Regex.IsMatch((string)t["sha256"]??"","^[a-f0-9]{64}$")||(long?)t["bytes"]==null||(long?)t["bytes"]<1))throw new InvalidDataException("Canopy input provenance");
            Id=(string)meta["id"];if(!SafeId(Id))throw new InvalidDataException("Canopy identity");
            bool hasRevision=meta["revision"]!=null,hasSeries=meta["revisionSeries"]!=null;
            if(hasRevision!=hasSeries)throw new InvalidDataException("Canopy revision and series must be declared together");
            if(hasRevision)
            {
                if(meta["revision"].Type!=JTokenType.Integer)throw new InvalidDataException("Canopy revision must be an integer");Revision=(long)meta["revision"];RevisionSeries=(string)meta["revisionSeries"];
                if(Revision<1||Revision>100000000||!SafeId(RevisionSeries))throw new InvalidDataException("Canopy revision range/series");
            }
            var b=meta["bounds"] as JArray;if(b==null||b.Count!=4)throw new InvalidDataException("Canopy geographic coverage");bounds=b.Select(v=>(double)v).ToArray();
            if(bounds.Any(v=>!Finite(v))||bounds[0]>=bounds[2]||bounds[1]>=bounds[3]||bounds[0]<-180||bounds[2]>180||bounds[1]<-80||bounds[3]>80)throw new InvalidDataException("Canopy bounds");
            coverage=meta["coverage"] as JArray;if(coverage==null||coverage.Count<5||coverage.Count>129)throw new InvalidDataException("Canopy source perimeter");foreach(var point in coverage){if(!(point is JArray pp)||pp.Count!=2||!Finite((double)pp[0])||!Finite((double)pp[1])||(double)pp[0]<bounds[0]||(double)pp[0]>bounds[2]||(double)pp[1]<bounds[1]||(double)pp[1]>bounds[3])throw new InvalidDataException("Canopy perimeter coordinate");}
            if((double)coverage[0][0]!=(double)coverage[coverage.Count-1][0]||(double)coverage[0][1]!=(double)coverage[coverage.Count-1][1])throw new InvalidDataException("Canopy perimeter closure");
            if((string)meta["payload"]!="canopy.json")throw new InvalidDataException("Canopy path");string file=Path.Combine(folder,"canopy.json");long length=new FileInfo(file).Length;
            if(length<1||length>4*1024*1024||length!=(long?)meta["bytes"])throw new InvalidDataException("Canopy payload budget/length");byte[] bytes=File.ReadAllBytes(file);
            if(Hash(bytes)!=(string)meta["sha256"])throw new InvalidDataException("Canopy checksum");Identity=Hash(File.ReadAllBytes(manifest));Attribution=(string)meta["attribution"];
            if(string.IsNullOrWhiteSpace(Attribution)||Attribution.Length>1200)throw new InvalidDataException("Canopy attribution");
            using(var reader=new Newtonsoft.Json.JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes))){MaxDepth=8})entries=JObject.Load(reader)["estimatedCanopyCandidates"] as JArray;
            if(entries==null||entries.Count>8192||entries.Count!=(int?)meta["candidateCount"])throw new InvalidDataException("Canopy count budget");
            var ids=new HashSet<string>();foreach(var t in entries)
            {
                string id=(string)t["id"];double lon=(double)t["longitude"],lat=(double)t["latitude"],h=(double)t["height"],r=(double)t["crownRadius"];
                if(!SafeId(id)||!ids.Add(id)||!Finite(lon)||!Finite(lat)||lon<bounds[0]||lon>bounds[2]||lat<bounds[1]||lat>bounds[3]||!Finite(h)||h<4||h>45||!Finite(r)||r<1.6||r>5)throw new InvalidDataException("Canopy candidate fields");
            }
        }
        static bool SafeId(string id)=>id!=null&&System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-zA-Z0-9_-]{1,100}$");
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        static string Hash(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public bool Intersects(WorldPlace p)
        {double half=Math.Max(1000,Math.Min(6000,p.size<=0?3000:p.size))*.5,dy=half/111319.4908,dx=dy/Math.Cos(p.latitude*Math.PI/180);return bounds[0]<p.longitude+dx&&bounds[2]>p.longitude-dx&&bounds[1]<p.latitude+dy&&bounds[3]>p.latitude-dy;}
        public int Apply(GeoRegion region,FinalizedRoadSurfacePlan pavement=null)
        {
            if(!region.preparedWidths)throw new InvalidDataException("Canopy requires final driving widths");
            double cos=Math.Cos(region.latitude*Math.PI/180);float X(double lon)=>(float)((lon-region.longitude)*111319.4908*cos);float Z(double lat)=>(float)((lat-region.latitude)*111319.4908);
            var cover=new GeoCanopyCoverage{id=Id,sourceHash=Identity,candidateCount=entries.Count,minX=Math.Max(region.minX,X(bounds[0])),maxX=Math.Min(region.minX+region.width,X(bounds[2])),minZ=Math.Max(region.minZ,Z(bounds[1])),maxZ=Math.Min(region.minZ+region.depth,Z(bounds[3]))};
            cover.boundary=DetailCanopyPacks.Clip(coverage.Select(c=>new Vector3(X((double)c[0]),0,Z((double)c[1]))).ToArray(),cover);
            if(cover.boundary.Length<4)return 0;
            if(cover.minX>=cover.maxX||cover.minZ>=cover.maxZ)return 0;
            var existingCoverage=region.canopyCoverage??Array.Empty<GeoCanopyCoverage>();if(existingCoverage.Length>=8)throw new InvalidDataException("Canopy coverage budget");
            if(existingCoverage.Any(c=>c.sourceHash==Identity))return 0;if(existingCoverage.Any(c=>c.id==Id))throw new InvalidDataException("A different revision of this canopy tile is already pinned");
            var list=new List<GeoTree>(region.trees??Array.Empty<GeoTree>());var ids=new HashSet<string>(list.Select(t=>t.id));var clearance=new DetailCanopyPacks.Clearance(region,pavement);
            var density=new Dictionary<Vector2Int,int>();foreach(var tree in list){var k=new Vector2Int(Mathf.FloorToInt(tree.x/320),Mathf.FloorToInt(tree.z/320));density.TryGetValue(k,out int n);density[k]=n+1;}
            foreach(var t in entries.OrderByDescending(t=>(float)t["height"]).ThenBy(t=>DetailCanopyPacks.Seed((string)t["id"])).ThenBy(t=>(string)t["id"],StringComparer.Ordinal))
            {
                var tree=new GeoTree{id=(string)t["id"],x=X((double)t["longitude"]),z=Z((double)t["latitude"]),height=(float)t["height"],crownRadius=(float)t["crownRadius"]};tree.seed=DetailCanopyPacks.Seed(tree.id);
                if(!DetailCanopyPacks.Contains(cover,tree.x,tree.z,tree.crownRadius)){cover.clippedCount++;continue;}
                if(!ids.Add(tree.id)||existingCoverage.Any(c=>DetailCanopyPacks.Contains(c,tree.x,tree.z))){cover.overlapRejectedCount++;continue;}
                if(!clearance.Allows(tree)){cover.clearanceRejectedCount++;continue;}
                var cell=new Vector2Int(Mathf.FloorToInt(tree.x/320),Mathf.FloorToInt(tree.z/320));density.TryGetValue(cell,out int count);if(count>=256){cover.densityRejectedCount++;continue;}density[cell]=count+1;
                cover.acceptedCount++;
                list.Add(tree);
            }
            DetailCanopyPacks.ValidateCounts(list);int added=list.Count-(region.trees?.Length??0);
            var finalTrees=list.OrderBy(t=>t.id,StringComparer.Ordinal).ToArray();var finalCoverage=existingCoverage.Concat(new[]{cover}).OrderBy(c=>c.sourceHash,StringComparer.Ordinal).ToArray();string finalAttribution=region.attribution+" · "+Attribution;region.trees=finalTrees;region.canopyCoverage=finalCoverage;region.attribution=finalAttribution;
            return added;
        }
    }
    public static class DetailCanopyPacks
    {
        public const string Recipe="relative-canopy-clearance-v1";public static string OverrideRoot;
        public static int Seed(string id){unchecked{uint h=2166136261;foreach(char c in id)h=(h^c)*16777619;return (int)((h&0x7fffffff)%100000000);}}
        public static DetailCanopyPack[] Find(WorldPlace place)
        {
            var roots=OverrideRoot!=null?new[]{OverrideRoot}:CountryPacks.Installed().Where(p=>p.Country==place.country_code).Select(p=>Path.Combine(p.Folder,"Detail")).ToArray();var list=new List<DetailCanopyPack>();
            foreach(string root in roots.Distinct())if(Directory.Exists(root))
            {
                var folders=Directory.GetDirectories(root).Where(f=>File.Exists(Path.Combine(f,"canopy-pack.json"))).OrderBy(f=>f,StringComparer.Ordinal).Take(65).ToArray();
                if(folders.Length>64){Debug.LogWarning("Optional canopy discovery skipped: more than64tile manifests; refusing a partial revision catalogue");continue;}
                foreach(string folder in folders)try{var pack=new DetailCanopyPack(folder);if(!list.Any(p=>p.Identity==pack.Identity))list.Add(pack);}catch(Exception e){Debug.LogWarning("Optional canopy ignored: "+e.Message);}
            }
            return SelectRevisions(list).Where(p=>p.Intersects(place)).Take(8).ToArray();
        }
        public static DetailCanopyPack[] SelectRevisions(IEnumerable<DetailCanopyPack> candidates)
        {
            var selected=new List<DetailCanopyPack>();
            foreach(var group in candidates.GroupBy(p=>p.Id).OrderBy(g=>g.Key,StringComparer.Ordinal))
            {
                var versions=group.GroupBy(p=>p.Identity).Select(g=>g.First()).ToArray();
                if(versions.Length==1){selected.Add(versions[0]);continue;}
                string reason=null;
                if(versions.Any(p=>!p.Revision.HasValue))reason="coexisting tile revisions lack explicit ordering metadata";
                else if(versions.Select(p=>p.RevisionSeries).Distinct(StringComparer.Ordinal).Count()!=1)reason="coexisting tile revisions belong to different series";
                else if(versions.GroupBy(p=>p.Revision.Value).Any(g=>g.Count()>1))reason="conflicting content declares the same revision";
                if(reason!=null){Debug.LogWarning("Optional canopy tile "+group.Key+" skipped: "+reason+"; active worlds remain pinned");continue;}
                selected.Add(versions.OrderByDescending(p=>p.Revision.Value).First());
            }
            return selected.OrderBy(p=>p.Identity,StringComparer.Ordinal).ToArray();
        }
        public static string CacheId(string original,DetailCanopyPack[] packs)
        {if(packs.Length==0)return original;using(var sha=SHA256.Create())return "download-canopy-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(original+"|"+Recipe+"|"+string.Join("|",packs.Select(p=>p.Identity)))) ,0,16).Replace("-","").ToLowerInvariant();}
        public static void PrepareDrivingWidths(GeoRegion region)
        {if(region.preparedWidths)return;foreach(var road in region.roads)road.width=region.circuit?road.width*1.35f:Mathf.Max(road.kind=="service"?6.5f:8.5f,road.width*1.4f);region.preparedWidths=true;region.Prepare();}
        public static bool ApplySafely(DetailCanopyPack pack,GeoRegion region,FinalizedRoadSurfacePlan pavement=null)
        {try{pack.Apply(region,pavement);return true;}catch(Exception e){Debug.LogWarning("Optional canopy skipped: "+e.Message);return false;}}
        public static bool Contains(GeoCanopyCoverage c,float x,float z,float margin=0)
        {
            if(x-margin<c.minX||x+margin>c.maxX||z-margin<c.minZ||z+margin>c.maxZ||c.boundary==null||c.boundary.Length<4||!GeoRegion.Contains(c.boundary,new Vector2(x,z)))return false;
            if(margin>0)for(int i=1;i<c.boundary.Length;i++){var a=new Vector2(c.boundary[i-1].x,c.boundary[i-1].z);var b=new Vector2(c.boundary[i].x,c.boundary[i].z);var d=b-a;float t=Mathf.Clamp01(Vector2.Dot(new Vector2(x,z)-a,d)/Mathf.Max(.00001f,d.sqrMagnitude));if(Vector2.Distance(new Vector2(x,z),a+d*t)<margin)return false;}return true;
        }
        public static Vector3[] Clip(Vector3[] boundary,GeoCanopyCoverage bounds)
        {
            var polygon=new List<Vector3>(boundary);if(polygon.Count>0)polygon.RemoveAt(polygon.Count-1);
            for(int side=0;side<4;side++)
            {
                float Signed(Vector3 p)=>side==0?p.x-bounds.minX:side==1?bounds.maxX-p.x:side==2?p.z-bounds.minZ:bounds.maxZ-p.z;
                var output=new List<Vector3>();if(polygon.Count==0)break;var prev=polygon[polygon.Count-1];float pd=Signed(prev);
                foreach(var next in polygon){float nd=Signed(next);if((pd>=0)!=(nd>=0))output.Add(Vector3.Lerp(prev,next,pd/(pd-nd)));if(nd>=0)output.Add(next);prev=next;pd=nd;}polygon=output;
            }
            if(polygon.Count>=3)polygon.Add(polygon[0]);else polygon.Clear();return polygon.ToArray();
        }
        public static void ValidateCounts(IList<GeoTree> trees)
        {
            if(trees.Count>16384)throw new InvalidDataException("Canopy region budget");var cells=new Dictionary<Vector2Int,int>();foreach(var tree in trees){var key=new Vector2Int(Mathf.FloorToInt(tree.x/320),Mathf.FloorToInt(tree.z/320));cells.TryGetValue(key,out int n);if(n>=256)throw new InvalidDataException("Canopy per-chunk budget");cells[key]=n+1;}
        }
        public static void FinalizeForWorld(GeoRegion region,FinalizedRoadSurfacePlan pavement=null)
        {
            if(region==null||region.id.StartsWith("download-peer-",StringComparison.Ordinal)||region.trees==null||region.trees.Length==0)return;
            var safe=new Clearance(region,pavement);var retained=new List<GeoTree>();foreach(var tree in region.trees)
            {
                if(safe.Allows(tree)){retained.Add(tree);continue;}
                var owner=(region.canopyCoverage??Array.Empty<GeoCanopyCoverage>()).FirstOrDefault(c=>Contains(c,tree.x,tree.z));if(owner!=null){owner.acceptedCount--;owner.clearanceRejectedCount++;}
            }
            region.trees=retained.ToArray();
        }
        // Spatial broad phase indexes ALL road/water segments; nearest centreline is
        // insufficient where a narrow lane runs beside a wider carriageway.
        public sealed class Clearance
        {
            readonly GeoRegion region;readonly RoadPavementClearance finalRoads;readonly Dictionary<Vector2Int,List<Envelope>> cells=new Dictionary<Vector2Int,List<Envelope>>();
            sealed class Envelope{public Vector2 a,b;public float radius,localHalfWidth;public bool roadBroad;public Vector3[] polygon;}
            static Vector2 P(Vector3 p)=>new Vector2(p.x,p.z);static Vector2Int Cell(float x,float z)=>new Vector2Int(Mathf.FloorToInt(x/64),Mathf.FloorToInt(z/64));
            void Add(Envelope e,float minX,float minZ,float maxX,float maxZ)
            {var a=Cell(minX-7,minZ-7);var b=Cell(maxX+7,maxZ+7);for(int z=a.y;z<=b.y;z++)for(int x=a.x;x<=b.x;x++){var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var bucket))cells[key]=bucket=new List<Envelope>();bucket.Add(e);}}
            void Segment(Vector3 a,Vector3 b,float radius,bool roadBroad=false,float localHalfWidth=0){Add(new Envelope{a=P(a),b=P(b),radius=radius,roadBroad=roadBroad,localHalfWidth=localHalfWidth},Math.Min(a.x,b.x)-radius,Math.Min(a.z,b.z)-radius,Math.Max(a.x,b.x)+radius,Math.Max(a.z,b.z)+radius);}
            public Clearance(GeoRegion region,FinalizedRoadSurfacePlan pavement=null)
            {
                this.region=region;if(region.PreparedRoadWidths!=null)finalRoads=new RoadPavementClearance(region,pavement);
                var widthControls=region.PreparedRoadWidths?.CopySnapshot().ToDictionary(p=>p.roadId,StringComparer.Ordinal);
                foreach(var road in region.roads)
                {
                    float start=0;for(int i=1;i<road.points.Length;i++)
                    {
                        var a=road.points[i-1];var b=road.points[i];float length=Vector3.Distance(a,b),end=start+length;
                        if(region.PreparedRoadWidths==null||road.tunnel){Segment(a,b,road.width*.5f+2);start=end;continue;}
                        var dto=widthControls[road.id];var knots=new List<float>{start,end};knots.AddRange(dto.distances.Where(d=>d>start+.0001f&&d<end-.0001f));knots.Sort();
                        for(int k=1;k<knots.Count;k++){float from=knots[k-1],to=knots[k],maximum=region.PreparedRoadWidths.MaximumBetween(road,from,to);bool constant=Math.Abs(dto.Width(from)-dto.Width(to))<.000001&&Math.Abs(maximum-dto.Width(from))<.000001&&Math.Abs(dto.Width((2.0*from+to)/3)-dto.Width(from))<.000001&&Math.Abs(dto.Width((from+2.0*to)/3)-dto.Width(from))<.000001;int steps=constant?1:Math.Max(1,Mathf.CeilToInt((to-from)/2.5f));if(steps>4096)throw new InvalidDataException("Canopy variable-width interval budget");for(int j=0;j<steps;j++){float u=Mathf.Lerp(from,to,j/(float)steps),v=Mathf.Lerp(from,to,(j+1)/(float)steps),half=region.PreparedRoadWidths.MaximumBetween(road,u,v)*.5f;Segment(Vector3.Lerp(a,b,(u-start)/Mathf.Max(.000001f,length)),Vector3.Lerp(a,b,(v-start)/Mathf.Max(.000001f,length)),half*1.667f+2,true,half);}}
                        start=end;
                    }
                    foreach(int i in new[]{0,road.points.Length-1}){var p=road.points[i];float radius=region.PreparedRoadWidths==null?region.JunctionRadius(i==0?road.start:road.end,road.width*.5f)+2:region.JunctionRadius(i==0?road.start:road.end,region.EffectiveWidth(road,i==0?0:road.length)*.5f,road.layer)+2;Segment(p,p,radius,region.PreparedRoadWidths!=null&&!road.tunnel,radius-2);}
                    if(road.id=="fictional-service-approach")Segment(road.points[road.points.Length-1],road.points[road.points.Length-1],42);
                }
                foreach(var track in region.railways??Array.Empty<GeoRailway>())if(!track.tunnel&&RailwayMetrics.Visible(track.kind))for(int i=1;i<track.points.Length;i++)Segment(track.points[i-1],track.points[i],track.width*.5f+2);
                foreach(var water in region.waters)for(int i=1;i<water.points.Length;i++)Segment(water.points[i-1],water.points[i],water.width*.5f+2);
                foreach(var building in region.buildings){var e=new Envelope{polygon=building.points,radius=1.5f};Add(e,building.points.Min(p=>p.x)-1.5f,building.points.Min(p=>p.z)-1.5f,building.points.Max(p=>p.x)+1.5f,building.points.Max(p=>p.z)+1.5f);}
                if(region.HasServicePad)Segment(region.ServicePad,region.ServicePad,42);
                if(region.route?.Length>0)Segment(region.route[0],region.route[0],16);
            }
            static float Distance(Vector2 p,Vector2 a,Vector2 b){var v=b-a;return Vector2.Distance(p,a+v*Mathf.Clamp01(Vector2.Dot(p-a,v)/Mathf.Max(.00001f,v.sqrMagnitude)));}
            public bool Allows(GeoTree tree)
            {
                bool checkRoads=false;var p=new Vector2(tree.x,tree.z);if(cells.TryGetValue(Cell(tree.x,tree.z),out var bucket))foreach(var e in bucket)
                {
                    if(e.roadBroad){if(Distance(p,e.a,e.b)<e.radius+tree.crownRadius)checkRoads=true;continue;}
                    if(e.polygon==null){if(Distance(p,e.a,e.b)<e.radius+tree.crownRadius)return false;}
                    else {if(GeoRegion.Contains(e.polygon,p))return false;for(int i=1;i<e.polygon.Length;i++)if(Distance(p,P(e.polygon[i-1]),P(e.polygon[i]))<e.radius+tree.crownRadius)return false;}
                }
                if(checkRoads&&finalRoads!=null&&!finalRoads.Allows(tree.x,tree.z,tree.crownRadius+2))return false;
                for(int i=0;i<9;i++){float angle=i*Mathf.PI*.25f;float r=i==8?0:tree.crownRadius;if(region.IsWater(tree.x+Mathf.Sin(angle)*r,tree.z+Mathf.Cos(angle)*r))return false;}
                return true;
            }
        }
    }
}
