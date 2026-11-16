using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using RoadRush.Core;
namespace RoadRush
{
    // Country-master maintenance only. A running world's GeoRegion is an immutable
    // snapshot; changing this database never rebuilds its visible meshes/colliders.
    public static class MapReplication
    {
        public sealed class Result {public int nodes,ways;public string version;public double west=180,south=90,east=-180,north=-90;}
        sealed class Node {public long id;public int lat,lon;public bool deleted;}
        sealed class Way {public long id;public bool deleted;public long[] nodes;public JObject tags;}
        static readonly HashSet<string> Roads=new HashSet<string>("motorway trunk primary secondary tertiary unclassified residential living_street service motorway_link trunk_link primary_link secondary_link tertiary_link".Split(' '));
        static long Id(string text){if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out long value)||value<=0)throw new InvalidDataException("Invalid OSM identifier");return value;}
        static int Coordinate(string text,double limit){if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||double.IsNaN(value)||double.IsInfinity(value)||Math.Abs(value)>limit)throw new InvalidDataException("Invalid OSM coordinate");return checked((int)Math.Round(value*1e7));}
        static int Kind(JObject tags)
        {
            string highway=(string)tags["highway"],access=(string)tags["access"],building=(string)tags["building"],natural=(string)tags["natural"];
            if(highway!=null&&Roads.Contains(highway)&&access!="no"&&access!="private"&&(string)tags["motor_vehicle"]!="no")return 0;
            if(new[]{"river","stream","canal"}.Contains((string)tags["waterway"]))return 1;
            if(building!=null&&building!="no")return 2;if(tags["landuse"]!=null||natural=="wood"||natural=="water")return 3;return -1;
        }
        public static string Sequence(NativeMapDatabase db)
        {using(var q=db.Prepare("SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'"))return q.Step()?q.Text(0):"";}
        static void Meta(NativeMapDatabase db,string key,string value)
        {using(var q=db.Prepare("INSERT OR REPLACE INTO metadata(key,value) VALUES(?1,?2)")){q.Text(1,key).Text(2,value);q.Step();}}
        static string Value(NativeMapDatabase db,string key)
        {using(var q=db.Prepare("SELECT value FROM metadata WHERE key=?1")){q.Text(1,key);return q.Step()?q.Text(0):"";}}
        static bool ReadStored(NativeMapDatabase db,string table,long id,out JObject tags,out long[] nodes)
        {
            tags=null;nodes=null;
            using(var old=db.Prepare("SELECT tags,geometry FROM "+table+" WHERE id=?1"))
            {
                old.Integer(1,id);if(!old.Step())return false;tags=JObject.Parse(old.Text(0));var blob=old.Bytes(1);
                using(var reader=new BinaryReader(new MemoryStream(blob)))
                {
                    if(blob.Length<4)throw new InvalidDataException("Corrupt base geometry");int count=reader.ReadInt32();
                    if(count<2||count>50000||blob.Length!=4+count*16)throw new InvalidDataException("Corrupt base geometry");nodes=new long[count];
                    for(int i=0;i<count;i++){nodes[i]=reader.ReadInt64();reader.ReadInt32();reader.ReadInt32();}
                }
                return true;
            }
        }
        static Way StoredWay(NativeMapDatabase db,long id)
        {
            bool main=ReadStored(db,"features",id,out var tags,out var nodes),rail=ReadStored(db,"railways",id,out var railTags,out var railNodes);
            if(!main&&!rail)return null;
            if(main&&rail)
            {
                if(!nodes.SequenceEqual(railNodes))throw new InvalidDataException("Shared road/rail node topology differs; base pack needs refresh");
                foreach(var property in railTags.Properties())
                {
                    var existing=tags[property.Name];bool railKey=property.Name=="railway"||property.Name.EndsWith(":railway",StringComparison.Ordinal)||property.Name.StartsWith("railway:",StringComparison.Ordinal);
                    if(existing==null)tags[property.Name]=property.Value.DeepClone();
                    else if(railKey&&!JToken.DeepEquals(existing,property.Value))throw new InvalidDataException("Shared road/rail lifecycle tags differ; base pack needs refresh");
                }
            }
            return new Way{id=id,tags=main?tags:railTags,nodes=main?nodes:railNodes};
        }
        static void IncludeOldBounds(NativeMapDatabase db,string table,long id,Result result)
        {
            using(var old=db.Prepare("SELECT minLon,minLat,maxLon,maxLat FROM "+table+" WHERE id=?1"))
            {old.Integer(1,id);if(old.Step()){result.west=Math.Min(result.west,old.Number(0));result.south=Math.Min(result.south,old.Number(1));result.east=Math.Max(result.east,old.Number(2));result.north=Math.Max(result.north,old.Number(3));}}
        }
        static void PutBounds(NativeMapDatabase db,string table,long id,double west,double south,double east,double north)
        {using(var put=db.Prepare("INSERT INTO "+table+" VALUES(?1,?2,?3,?4,?5)")){put.Integer(1,id).Number(2,west).Number(3,east).Number(4,south).Number(5,north);put.Step();}}
        public static Result Apply(CountryPack pack,string path,DataFileManifest manifest,string timestamp)
        {
            if(manifest==null||manifest.pack!=pack.Id||(bool?)pack.Metadata["completeNodes"]!=true||manifest.kind!="map-diff"||!long.TryParse(manifest.toVersion,out long sourceSequence)||manifest.source!=pack.ReplicationSource+"/"+ReplicationState.File(sourceSequence)+".osc.gz"||!DataPackageRules.FileMatches(path,manifest)||!DataPackageRules.NextSequence(pack.Version,manifest.fromVersion,manifest.toVersion))throw new InvalidDataException("Map update does not match this country version");
            if(manifest.format==2&&manifest.timestamp!=DateTimeOffset.Parse(timestamp,CultureInfo.InvariantCulture).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ",CultureInfo.InvariantCulture))throw new InvalidDataException("Replication state differs from the signed peer timestamp");
            var nodes=new List<Node>();var ways=new Dictionary<long,Way>();
            var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=128*1024*1024,MaxCharactersFromEntities=1024,IgnoreComments=true};string action="";
            using(var file=File.OpenRead(path))using(var gzip=new GZipStream(file,CompressionMode.Decompress))using(var xml=XmlReader.Create(gzip,settings))
            {
                xml.MoveToContent();if(xml.Name!="osmChange"||xml.IsEmptyElement)throw new InvalidDataException("Expected an OSM change document");
                while(xml.Read())
                {
                    if(xml.NodeType==XmlNodeType.EndElement&&(xml.Name=="create"||xml.Name=="modify"||xml.Name=="delete")){action="";continue;}
                    if(xml.NodeType!=XmlNodeType.Element)continue;
                    if(xml.Name=="create"||xml.Name=="modify"||xml.Name=="delete"){action=xml.Name;continue;}
                    if((xml.Name=="node"||xml.Name=="way")&&(action==""||xml.Depth!=2))throw new InvalidDataException("OSM change action missing");
                    if(xml.Name=="node")
                    {
                        if(nodes.Count>=300000)throw new InvalidDataException("Daily update has too many nodes");var n=new Node{id=Id(xml.GetAttribute("id")),deleted=action=="delete"||xml.GetAttribute("visible")=="false"};
                        if(!n.deleted){n.lat=Coordinate(xml.GetAttribute("lat"),90);n.lon=Coordinate(xml.GetAttribute("lon"),180);}nodes.Add(n);
                    }
                    else if(xml.Name=="way")
                    {
                        if(ways.Count>=150000)throw new InvalidDataException("Daily update has too many ways");
                        // ReadSubtree prevents skipping the following sibling way.
                        XElement element;using(var subtree=xml.ReadSubtree())element=XElement.Load(subtree);
                        var w=new Way{id=Id((string)element.Attribute("id")),deleted=action=="delete"||(string)element.Attribute("visible")=="false",tags=new JObject()};
                        if(!w.deleted)
                        {
                            w.nodes=element.Elements("nd").Select(e=>Id((string)e.Attribute("ref"))).Take(50001).ToArray();if(w.nodes.Length>50000)throw new InvalidDataException("Oversized changed way");
                            foreach(var tag in element.Elements("tag")){string key=(string)tag.Attribute("k"),value=(string)tag.Attribute("v");if(key==null||key.Length>256||value==null||value.Length>4096)throw new InvalidDataException("Invalid OSM tag");w.tags[key]=value;}
                        }
                        ways[w.id]=w;
                    }
                }
            }
            var result=new Result{nodes=nodes.Count,version=manifest.toVersion};bool railwayComplete=false;long railwayCount=0;
            using(var db=new NativeMapDatabase(Path.Combine(pack.Folder,"map.sqlite"),true))
            {
                db.Execute("PRAGMA journal_mode=WAL");db.Execute("PRAGMA synchronous=FULL");db.Execute("BEGIN IMMEDIATE");bool committed=false;
                try
                {
                    if(!DataPackageRules.NextSequence(Sequence(db),manifest.fromVersion,manifest.toVersion))throw new InvalidDataException("Country changed while update was staged");
                    Meta(db,"railway_writer_active","1");
                    // Schema and both representations live in the same sequence transaction.
                    string railSchema=Value(db,"railway_schema"),railComplete=Value(db,"railway_complete");
                    if(railSchema!=""&&railSchema!="1"||railComplete!=""&&railComplete!="0"&&railComplete!="1")throw new InvalidDataException("Unsupported railway capability metadata");
                    railwayComplete=railComplete=="1";
                    if(railwayComplete)using(var schema=db.Prepare("SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('railways','rail_bounds')")){schema.Step();if(schema.Integer(0)!=2)throw new InvalidDataException("Complete railway capability lacks its base tables; refresh the country pack");}
                    db.Execute("CREATE TABLE IF NOT EXISTS railways(id INTEGER PRIMARY KEY,tags TEXT NOT NULL,geometry BLOB NOT NULL)");
                    db.Execute("CREATE VIRTUAL TABLE IF NOT EXISTS rail_bounds USING rtree(id,minLon,maxLon,minLat,maxLat)");
                    var affected=new HashSet<long>(ways.Keys);long geometryReferences=0;
                    foreach(var node in nodes)
                    {
                        using(var refs=db.Prepare("SELECT way FROM usage WHERE node=?1")){refs.Integer(1,node.id);while(refs.Step()){affected.Add(refs.Integer(0));if(affected.Count>200000)throw new InvalidDataException("Update affects too many features");}}
                        using(var put=db.Prepare(node.deleted?"DELETE FROM nodes WHERE id=?1":"INSERT OR REPLACE INTO nodes(id,lat,lon) VALUES(?1,?2,?3)"))
                        {put.Integer(1,node.id);if(!node.deleted)put.Integer(2,node.lat).Integer(3,node.lon);put.Step();}
                    }
                    if(affected.Count>200000)throw new InvalidDataException("Update affects too many features");
                    foreach(long id in affected)
                    {
                        if(!ways.TryGetValue(id,out var way)){way=StoredWay(db,id);if(way==null)continue;}
                        IncludeOldBounds(db,"bounds",id,result);IncludeOldBounds(db,"rail_bounds",id,result);
                        foreach(string table in new[]{"bounds","features","rail_bounds","railways","usage"})using(var remove=db.Prepare("DELETE FROM "+table+" WHERE "+(table=="usage"?"way":"id")+"=?1")){remove.Integer(1,id);remove.Step();}
                        if(way.deleted)continue;
                        int kind=Kind(way.tags);bool railway=RailwayTags.IsTrack(way.tags);
                        if(railway&&(way.nodes==null||way.nodes.Length<2))throw new InvalidDataException("Invalid changed railway geometry");
                        if((kind<0&&!railway)||way.nodes==null||way.nodes.Length<2)continue;
                        geometryReferences+=way.nodes.Length;if(geometryReferences>12000000)throw new InvalidDataException("Update geometry work budget exceeded");
                        double west=180,south=90,east=-180,north=-90;byte[] geometry;
                        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
                        {
                            writer.Write(way.nodes.Length);
                            foreach(long node in way.nodes)
                            {
                                using(var point=db.Prepare("SELECT lat,lon FROM nodes WHERE id=?1"))
                                {point.Integer(1,node);if(!point.Step())throw new InvalidDataException("Missing node in changed way; base pack needs refresh");int lat=checked((int)point.Integer(0)),lon=checked((int)point.Integer(1));writer.Write(node);writer.Write(lat);writer.Write(lon);double y=lat/1e7,x=lon/1e7;west=Math.Min(west,x);east=Math.Max(east,x);south=Math.Min(south,y);north=Math.Max(north,y);}
                            }
                            geometry=stream.ToArray();
                        }
                        string serializedTags=way.tags.ToString(Newtonsoft.Json.Formatting.None);
                        if(kind>=0)
                        {
                            using(var put=db.Prepare("INSERT INTO features(id,kind,tags,geometry) VALUES(?1,?2,?3,?4)")){put.Integer(1,id).Integer(2,kind).Text(3,serializedTags).Bytes(4,geometry);put.Step();}
                            PutBounds(db,"bounds",id,west,south,east,north);
                        }
                        if(railway)
                        {
                            using(var put=db.Prepare("INSERT INTO railways(id,tags,geometry) VALUES(?1,?2,?3)")){put.Integer(1,id).Text(2,serializedTags).Bytes(3,geometry);put.Step();}
                            PutBounds(db,"rail_bounds",id,west,south,east,north);
                        }
                        foreach(long node in way.nodes.Distinct())using(var put=db.Prepare("INSERT OR IGNORE INTO usage(node,way) VALUES(?1,?2)")){put.Integer(1,node).Integer(2,id);put.Step();}
                        result.west=Math.Min(result.west,west);result.south=Math.Min(result.south,south);result.east=Math.Max(result.east,east);result.north=Math.Max(result.north,north);result.ways++;
                    }
                    Meta(db,"railway_schema","1");Meta(db,"railway_complete",railwayComplete?"1":"0");
                    Meta(db,"railway_capabilities",new JObject{{"railwaySchema",1},{"railwayWays",true},{"railwayComplete",railwayComplete},{"railwayTable","railways"},{"railwayBoundsTable","rail_bounds"}}.ToString(Newtonsoft.Json.Formatting.None));
                    using(var count=db.Prepare("SELECT count(*) FROM railways")){count.Step();railwayCount=count.Integer(0);}
                    Meta(db,"osmosis_replication_sequence_number",manifest.toVersion);Meta(db,"osmosis_replication_timestamp",timestamp);Meta(db,"road_rush_last_diff_sha256",manifest.sha256);Meta(db,"railway_writer_active","0");db.Execute("COMMIT");committed=true;
                }
                finally{if(!committed)db.Execute("ROLLBACK");}
            }
            // DB metadata is the authority if interruption occurs before this mirror.
            var metadata=(JObject)pack.Metadata.DeepClone();metadata["version"]=manifest.toVersion;metadata["replication"]["osmosis_replication_sequence_number"]=manifest.toVersion;metadata["replication"]["osmosis_replication_timestamp"]=timestamp;metadata["lastDiffSHA256"]=manifest.sha256;metadata["baseDatabaseSHA256"]=metadata["baseDatabaseSHA256"]??metadata["databaseSHA256"];metadata.Remove("databaseSHA256");
            var capabilities=metadata["capabilities"] as JObject??new JObject();metadata["capabilities"]=capabilities;
            capabilities["railwaySchema"]=1;capabilities["railwayWays"]=true;capabilities["railwayComplete"]=railwayComplete;capabilities["railwayTable"]="railways";capabilities["railwayBoundsTable"]="rail_bounds";metadata["railways"]=railwayCount;
            string target=Path.Combine(pack.Folder,"pack.json"),temporary=target+".partial";File.WriteAllText(temporary,metadata.ToString());File.Replace(temporary,target,target+".bak");return result;
        }
    }
}
