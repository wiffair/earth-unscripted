using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadRush
{
    // Optional, compiled scenery. Source rasters/archives are never needed at runtime.
    // Snapshot sharing carries the resulting geometry, not an unreviewed external path.
    public sealed class DetailBuildingPack
    {
        public readonly string Identity, Attribution,Id,RevisionSeries;public readonly long? Revision;
        readonly JObject data;
        readonly double[] bounds;
        public DetailBuildingPack(string folder)
        {
            string path=Path.Combine(folder,"buildings-pack.json");
            if(new FileInfo(path).Length>256000)throw new InvalidDataException("Scenery manifest too large");
            var meta=JObject.Parse(File.ReadAllText(path));
            if((string)meta["format"]!="RoadRushBuildings1"||(string)meta["licence"]!="OGL-3.0"||(bool?)meta["offlineAllowed"]!=true||(bool?)meta["peerRedistributionAllowed"]!=true)
                throw new InvalidDataException("Unsupported scenery format or reuse permissions");
            Id=(string)meta["id"];if(Id==null||!System.Text.RegularExpressions.Regex.IsMatch(Id,"^[a-zA-Z0-9_-]{1,100}$"))throw new InvalidDataException("Invalid scenery tile identity");
            bool hasRevision=meta["revision"]!=null,hasSeries=meta["revisionSeries"]!=null;
            if(hasRevision!=hasSeries)throw new InvalidDataException("Scenery revision and series must be declared together");
            if(hasRevision)
            {
                if(meta["revision"].Type!=JTokenType.Integer)throw new InvalidDataException("Scenery revision must be an integer");Revision=(long)meta["revision"];RevisionSeries=(string)meta["revisionSeries"];
                if(Revision<1||Revision>100000000||RevisionSeries==null||!System.Text.RegularExpressions.Regex.IsMatch(RevisionSeries,"^[a-zA-Z0-9_-]{1,100}$"))throw new InvalidDataException("Invalid scenery revision range/series");
            }
            if((string)meta["payload"]!="buildings.json")throw new InvalidDataException("Invalid scenery payload path");
            var bb=meta["bounds"] as JArray;if(bb==null||bb.Count!=4)throw new InvalidDataException("Scenery bounds missing");
            bounds=bb.Select(v=>(double)v).ToArray();if(bounds.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||bounds[0]>=bounds[2]||bounds[1]>=bounds[3]||bounds[0]<-180||bounds[2]>180||bounds[1]<-80||bounds[3]>80)throw new InvalidDataException("Invalid scenery coverage");
            path=Path.Combine(folder,"buildings.json");long length=new FileInfo(path).Length;
            if(length<1||length>4000000||length!=(long)meta["bytes"])throw new InvalidDataException("Scenery payload size mismatch");
            byte[] bytes=File.ReadAllBytes(path);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(hash!=(string)meta["sha256"])throw new InvalidDataException("Scenery payload checksum mismatch");
            // One content identifier binds cache identity to exact compiled geometry.
            using(var sha=SHA256.Create())Identity=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(folder,"buildings-pack.json")))).Replace("-","").ToLowerInvariant();Attribution=(string)meta["attribution"];
            if(string.IsNullOrWhiteSpace(Attribution)||Attribution.Length>1200)throw new InvalidDataException("Scenery attribution missing");
            using(var reader=new Newtonsoft.Json.JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes))){MaxDepth=12})data=JObject.Load(reader);
            if(!(data["buildings"] is JArray buildings)||buildings.Count>4096)throw new InvalidDataException("Scenery building budget exceeded");
        }
        public bool Intersects(WorldPlace place)
        {
            double half=Math.Max(1000,Math.Min(6000,place.size<=0?3000:place.size))*.5,dy=half/111319.4908,dx=dy/Math.Cos(place.latitude*Math.PI/180);
            return bounds[0]<place.longitude+dx&&bounds[2]>place.longitude-dx&&bounds[1]<place.latitude+dy&&bounds[3]>place.latitude-dy;
        }
        public bool ApplySafely(GeoRegion region)
        {
            try{Apply(region);return true;}
            catch(Exception e) when(e is InvalidDataException||e is Newtonsoft.Json.JsonException||e is FormatException||e is OverflowException||e is ArgumentException||e is InvalidCastException)
            {Debug.LogWarning("Optional scenery skipped; standard buildings retained: "+e.Message);return false;}
        }
        public int Apply(GeoRegion region)
        {
            var additions=new List<GeoBuilding>();double cos=Math.Cos(region.latitude*Math.PI/180);int vertices=0;
            foreach(var token in (JArray)data["buildings"])
            {
                var points=token["points"] as JArray;float height=(float)token["height"];string id=(string)token["id"];
                if(points==null||points.Count<4||points.Count>256||string.IsNullOrEmpty(id)||id.Length>100||float.IsNaN(height)||float.IsInfinity(height)||height<2||height>80)throw new InvalidDataException("Invalid compiled building");
                // Source classes describe outlines, not a guessed residential use.
                // The missing class is supported only for older reviewed packs.
                var kindToken=token["kind"];string kind=kindToken==null?"residential":kindToken.Type==JTokenType.String?(string)kindToken:null;
                if(kind==null||kind.Length>32||(kind!="residential"&&kind!="building"&&kind!="important_building"))throw new InvalidDataException("Invalid compiled building kind");
                vertices+=points.Count;if(vertices>100000)throw new InvalidDataException("Scenery vertex budget exceeded");
                var pp=new Vector3[points.Count];bool inside=true;
                for(int i=0;i<pp.Length;i++)
                {
                    if(!(points[i] is JArray coordinate)||coordinate.Count!=2)throw new InvalidDataException("Invalid geographic point");
                    double lon=(double)coordinate[0],lat=(double)coordinate[1];
                    if(double.IsNaN(lon)||double.IsInfinity(lon)||double.IsNaN(lat)||double.IsInfinity(lat)||lon<bounds[0]-.00001||lon>bounds[2]+.00001||lat<bounds[1]-.00001||lat>bounds[3]+.00001)throw new InvalidDataException("Building outside declared coverage");
                    float x=(float)((lon-region.longitude)*111319.4908*cos),z=(float)((lat-region.latitude)*111319.4908);
                    if(x<region.minX+5||x>region.minX+region.width-5||z<region.minZ+5||z>region.minZ+region.depth-5)inside=false;
                    pp[i]=new Vector3(x,region.RawHeight(x,z),z);
                }
                if(Vector2.Distance(new Vector2(pp[0].x,pp[0].z),new Vector2(pp[pp.Length-1].x,pp[pp.Length-1].z))>.05f)throw new InvalidDataException("Building footprint must close");
                if(inside)additions.Add(new GeoBuilding{id=id,kind=kind,roof=(string)token["roof"]=="gabled"?"gabled":"flat",height=height,measuredHeight=false,points=pp});
            }
            // Validate all additions before mutating the authoritative region.
            var merged=new List<GeoBuilding>(region.buildings);
            // Important source outlines win exact/corresponding ordinary overlaps,
            // independent of payload feature order; no duplicate walls or roofs.
            foreach(var b in additions.OrderBy(b=>b.kind=="important_building"?1:0).ThenBy(b=>b.id,StringComparer.Ordinal))
            {
                var centre=Centre(b.points);
                merged.RemoveAll(old=>old.id==b.id||GeoRegion.Contains(old.points,centre)||GeoRegion.Contains(b.points,Centre(old.points)));
                merged.Add(b);
            }
            var complete=merged.OrderBy(b=>b.id,StringComparer.Ordinal).ToArray();string attribution=additions.Count>0?region.attribution+" · "+Attribution:region.attribution;region.buildings=complete;region.attribution=attribution;
            return additions.Count;
        }
        static Vector2 Centre(Vector3[] points){var p=Vector2.zero;for(int i=0;i<points.Length-1;i++)p+=new Vector2(points[i].x,points[i].z);return p/Math.Max(1,points.Length-1);}
    }
    public static class DetailBuildingPacks
    {
        public static string OverrideRoot;
        public static DetailBuildingPack[] Find(WorldPlace place)
        {
            var roots=new List<string>();if(OverrideRoot!=null)roots.Add(OverrideRoot);
            else foreach(var pack in CountryPacks.Installed())if(pack.Country==place.country_code)roots.Add(Path.Combine(pack.Folder,"Detail"));
            var found=new List<DetailBuildingPack>();
            foreach(string root in roots.Distinct())if(Directory.Exists(root))
            {
                var folders=Directory.GetDirectories(root).Where(folder=>File.Exists(Path.Combine(folder,"buildings-pack.json"))).OrderBy(x=>x,StringComparer.Ordinal).Take(65).ToArray();
                if(folders.Length>64){Debug.LogWarning("Optional scenery discovery skipped: more than64tile manifests; refusing a partial revision catalogue");continue;}
                foreach(string folder in folders)try{var pack=new DetailBuildingPack(folder);if(!found.Any(p=>p.Identity==pack.Identity))found.Add(pack);}catch(Exception e){Debug.LogWarning("Optional scenery ignored: "+e.Message);}
            }
            return SelectRevisions(found).Where(p=>p.Intersects(place)).Take(8).ToArray();
        }
        public static DetailBuildingPack[] SelectRevisions(IEnumerable<DetailBuildingPack> candidates)
        {
            var selected=new List<DetailBuildingPack>();foreach(var group in candidates.GroupBy(p=>p.Id).OrderBy(g=>g.Key,StringComparer.Ordinal))
            {
                var versions=group.GroupBy(p=>p.Identity).Select(g=>g.First()).ToArray();if(versions.Length==1){selected.Add(versions[0]);continue;}string reason=null;
                if(versions.Any(p=>!p.Revision.HasValue))reason="coexisting tile revisions lack explicit ordering metadata";
                else if(versions.Select(p=>p.RevisionSeries).Distinct(StringComparer.Ordinal).Count()!=1)reason="coexisting tile revisions belong to different series";
                else if(versions.GroupBy(p=>p.Revision.Value).Any(g=>g.Count()>1))reason="conflicting content declares the same revision";
                if(reason!=null){Debug.LogWarning("Optional scenery tile "+group.Key+" skipped: "+reason+"; prepared worlds remain unchanged");continue;}selected.Add(versions.OrderByDescending(p=>p.Revision.Value).First());
            }
            return selected.OrderBy(p=>p.Identity,StringComparer.Ordinal).ToArray();
        }
        public static string CacheId(string original,DetailBuildingPack[] packs)
        {
            if(packs.Length==0)return original;
            using(var sha=SHA256.Create())return "download-detail-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("building-kind-revisions-v1|"+original+"|"+string.Join("|",packs.Select(p=>p.Identity).OrderBy(x=>x,StringComparer.Ordinal)))) ,0,16).Replace("-","").ToLowerInvariant();
        }
    }
}
