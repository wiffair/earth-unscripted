using System;using System.Collections.Generic;using System.IO;using System.Linq;
namespace EarthUnscripted
{
    // Accounting only, NOT geometry/clearance admission. A typed geometry verifier
    // must bind this exact receipt before an augmented world can ever be attached.
    [Serializable]public sealed class WorldAuthoringChange
    {
        public string entity,id,kind,originalSHA,authoredSHA,policyPin,parentRoadId;
        public double parentSourceS;
    }
    public sealed class WorldMeshAuthoringAudit
    {
        public readonly string OriginalSourceDigest,AuthoredSourceDigest,AugmentationDigest;
        public readonly int AddedRoads,ChangedRails;
        public bool GeometryAdmitted=>false;
        WorldMeshAuthoringAudit(string original,string authored,string digest,int roads,int rails){OriginalSourceDigest=original;AuthoredSourceDigest=authored;AugmentationDigest=digest;AddedRoads=roads;ChangedRails=rails;}
        [Serializable]sealed class Receipt{public string recipe="ExplicitPostSourceAuthoring1",originalSourceDigest,authoredSourceDigest;public WorldAuthoringChange[] changes;}
        static void Need(bool okay,string why){if(!okay)throw new InvalidDataException("World authoring accounting: "+why);}
        static bool Hash(string value)=>value!=null&&value.Length==64&&value.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        static string Digest<T>(T value)=>World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(value));
        static GeoRoad[] Project(World13Document d)
        {
            var rows=d.roadAuthority.sourceRoads.ToDictionary(r=>r.roadId,r=>r.nodeIds,StringComparer.Ordinal);
            return d.geography.roads.Select(r=>new GeoRoad{id=r.id,start=r.start,end=r.end,name=r.name,kind=r.kind,width=r.width,maxVehicleHeight=r.maxVehicleHeight,limit=r.limit,oneWay=r.oneWay,layer=r.layer,bridge=r.bridge,tunnel=r.tunnel,points=(UnityEngine.Vector3[])r.points.Clone(),sourceNodeIds=(string[])rows[r.id].Clone()}).ToArray();
        }
        public static WorldMeshAuthoringAudit VerifyAccountingOnly(CanonicalRaw13Document source,GeoRoad[] authoredRoads,GeoRailway[] authoredRails,WorldAuthoringChange[] declared)
        {
            Need(source!=null&&authoredRoads!=null&&authoredRails!=null&&declared!=null&&declared.Length<=2048,"complete bounded accounting input");
            var original=WorldFieldJson.Read<World13Document>(new System.Text.UTF8Encoding(false,true).GetString(source.Write()));var sourceRoads=Project(original);string source2=RoadSourceIdentity.Compute(sourceRoads);Need(source2==original.roadAuthority.sourceDigest,"original source binding");
            var byId=authoredRoads.ToDictionary(r=>r.id,StringComparer.Ordinal);Need(sourceRoads.All(r=>byId.ContainsKey(r.id)),"source road removed");
            // Raw source XYZ/width/nodes/tags never change during derived authoring.
            Need(RoadSourceIdentity.Compute(sourceRoads.Select(r=>byId[r.id]).ToArray())==source2,"original source road fields mutated; use separate derived controls");
            var changes=new Dictionary<string,WorldAuthoringChange>(StringComparer.Ordinal);foreach(var c in declared){Need(c!=null&&c.id!=null&&c.id.Length<=256&&Hash(c.policyPin)&&Hash(c.authoredSHA)&&(c.entity=="road"||c.entity=="rail")&&changes.TryAdd(c.entity+"/"+c.id,c),"bounded unique explicit authoring declarations");}
            var originalIds=new HashSet<string>(sourceRoads.Select(r=>r.id),StringComparer.Ordinal);int added=0,rails=0;var used=new HashSet<string>(StringComparer.Ordinal);
            foreach(var r in authoredRoads)if(!originalIds.Contains(r.id))
            {
                Need(changes.TryGetValue("road/"+r.id,out var c)&&(c.kind=="service"||c.kind=="pit")&&c.originalSHA==""&&c.parentRoadId!=r.id&&originalIds.Contains(c.parentRoadId??"")&&!double.IsNaN(c.parentSourceS)&&!double.IsInfinity(c.parentSourceS),"unaccounted synthetic road/parent relationship");
                var metric=new RoadElevationMetric(byId[c.parentRoadId]);Need(c.parentSourceS>=0&&c.parentSourceS<=metric.SourceLength,"synthetic parent source station");
                Need(c.authoredSHA==RoadSourceIdentity.Compute(new[]{r}),"synthetic source/node/width identity differs");used.Add("road/"+r.id);added++;
            }
            var rawRails=(original.geography.railways??Array.Empty<GeoRailway>()).ToDictionary(r=>r.id,StringComparer.Ordinal);Need(authoredRails.Length==rawRails.Count&&authoredRails.Select(r=>r.id).Distinct(StringComparer.Ordinal).Count()==rawRails.Count,"rail source added/removed without a separate provenance adapter");
            foreach(var r in authoredRails)
            {
                Need(rawRails.TryGetValue(r.id,out var raw),"foreign authored rail");string before=Digest(raw),after=Digest(r);if(before==after)continue;
                Need(changes.TryGetValue("rail/"+r.id,out var c)&&c.kind=="rail-profile"&&c.originalSHA==before&&c.authoredSHA==after,"unaccounted post-source rail solve");
                // Only Y profiles may change; source topology/XZ/structure metadata stay pinned.
                var copy=WorldFieldJson.Clone(r);copy.points=(UnityEngine.Vector3[])copy.points.Clone();Need(copy.points.Length==raw.points.Length,"rail topology changed");for(int i=0;i<copy.points.Length;i++)copy.points[i].y=raw.points[i].y;Need(Digest(copy)==before,"rail source topology/XZ/metadata changed");used.Add("rail/"+r.id);rails++;
            }
            Need(used.Count==declared.Length,"declaration does not correspond to an actual authored change");string authored=RoadSourceIdentity.Compute(authoredRoads);var receipt=new Receipt{originalSourceDigest=source2,authoredSourceDigest=authored,changes=declared.OrderBy(c=>c.entity,StringComparer.Ordinal).ThenBy(c=>c.id,StringComparer.Ordinal).ToArray()};return new WorldMeshAuthoringAudit(source2,authored,Digest(receipt),added,rails);
        }
    }
}
