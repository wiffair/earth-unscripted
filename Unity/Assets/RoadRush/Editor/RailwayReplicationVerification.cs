using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RoadRush.Core;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class RailwayReplicationVerification
    {
        static readonly string FixtureSourceAnchor=DataPackageRules.Hash(Encoding.UTF8.GetBytes("RoadRush isolated synthetic railway fixture source v1"));
        static byte[] Geometry(params long[] nodes)
        {using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(nodes.Length);foreach(long node in nodes){writer.Write(node);writer.Write(520000000+(int)node*1000);writer.Write(-30000000+(int)node*1000);}return stream.ToArray();}}
        static string Fixture(string root,string name,bool complete=false)
        {
            string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);string database=Path.Combine(folder,"map.sqlite");File.WriteAllBytes(database,Array.Empty<byte>());
            using(var db=new NativeMapDatabase(database,true))
            {
                foreach(string sql in new[]{"CREATE TABLE features(id INTEGER PRIMARY KEY,kind INTEGER,tags TEXT,geometry BLOB)","CREATE VIRTUAL TABLE bounds USING rtree(id,minLon,maxLon,minLat,maxLat)","CREATE TABLE nodes(id INTEGER PRIMARY KEY,lat INTEGER,lon INTEGER)","CREATE TABLE usage(node INTEGER,way INTEGER,PRIMARY KEY(node,way)) WITHOUT ROWID","CREATE INDEX usage_way ON usage(way)","CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT)","INSERT INTO metadata VALUES('osmosis_replication_sequence_number','10')","INSERT INTO metadata VALUES('osmosis_replication_timestamp','2026-11-03T21:24:23Z')","INSERT INTO nodes VALUES(1,520001000,-29999000),(2,520002000,-29998000),(3,520003000,-29997000),(4,520004000,-29996000)","INSERT INTO usage VALUES(1,100),(2,100)","INSERT INTO bounds VALUES(100,-2.9999,-2.9998,52.0001,52.0002)"})db.Execute(sql);
                using(var put=db.Prepare("INSERT INTO features VALUES(100,0,?1,?2)")){put.Text(1,"{\"highway\":\"residential\",\"name\":\"Fixture road\"}").Bytes(2,Geometry(1,2));put.Step();}
                if(complete)
                {
                    db.Execute("CREATE TABLE railways(id INTEGER PRIMARY KEY,tags TEXT NOT NULL,geometry BLOB NOT NULL)");db.Execute("CREATE VIRTUAL TABLE rail_bounds USING rtree(id,minLon,maxLon,minLat,maxLat)");
                    db.Execute("INSERT INTO metadata VALUES('railway_schema','1'),('railway_complete','1'),('railway_source_sha256','"+FixtureSourceAnchor+"')");
                    using(var put=db.Prepare("INSERT INTO railways VALUES(100,?1,?2)")){put.Text(1,"{\"highway\":\"residential\",\"railway\":\"tram\",\"name\":\"Fixture road\"}").Bytes(2,Geometry(1,2));put.Step();}
                    db.Execute("INSERT INTO rail_bounds VALUES(100,-2.9999,-2.9998,52.0001,52.0002)");
                }
            }
            var metadata=new JObject{{"format",2},{"completeNodes",true},{"name","Railway fixture"},{"country","GB"},{"version","10"},{"mapReady",true},{"terrainReady",true},{"bounds",new JArray(-4,51,-2,53)},{"databaseSHA256",DataPackageRules.HashFile(database)},{"replication",new JObject{{"osmosis_replication_sequence_number","10"},{"osmosis_replication_base_url","https://download.geofabrik.de/europe/united-kingdom/wales-updates"},{"osmosis_replication_timestamp","2026-11-03T21:24:23Z"}}}};
            File.WriteAllText(Path.Combine(folder,"pack.json"),metadata.ToString());return folder;
        }
        static DataFileManifest Diff(string folder,string from,string to,string text)
        {
            string name="rail-"+to,path=Path.Combine(folder,name+".osc.gz");using(var file=File.Create(path))using(var gzip=new GZipStream(file,CompressionMode.Compress))using(var writer=new StreamWriter(gzip,Encoding.UTF8))writer.Write(text);
            var pack=new CountryPack(folder);return new DataFileManifest{id=name,kind="map-diff",pack=pack.Id,fromVersion=from,toVersion=to,source=pack.ReplicationSource+"/"+ReplicationState.File(long.Parse(to))+".osc.gz",licence="ODbL-1.0",bytes=new FileInfo(path).Length,sha256=DataPackageRules.HashFile(path)};
        }
        static void Apply(string folder,DataFileManifest diff)=>MapReplication.Apply(new CountryPack(folder),Path.Combine(folder,diff.id+".osc.gz"),diff,"2026-11-04T00:00:00Z");
        static long Number(string folder,string sql){using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite")))using(var q=db.Prepare(sql)){if(!q.Step())throw new Exception("Missing fixture scalar");return q.Integer(0);}}
        static string Text(string folder,string sql){using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite")))using(var q=db.Prepare(sql)){return q.Step()?q.Text(0):null;}}
        static byte[] Blob(string folder,string table,long id){using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite")))using(var q=db.Prepare("SELECT geometry FROM "+table+" WHERE id=?1")){q.Integer(1,id);if(!q.Step())return null;return q.Bytes(0);}}
        static JObject Tags(string folder,string table,long id)=>JObject.Parse(Text(folder,"SELECT tags FROM "+table+" WHERE id="+id));
        static void Guard(string folder)
        {
            using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite"),true))
            {
                db.Execute("INSERT OR REPLACE INTO metadata VALUES('railway_writer_active','0')");
                foreach(string action in new[]{"INSERT","UPDATE"})db.Execute("CREATE TRIGGER railway_replication_guard_"+action.ToLowerInvariant()+" BEFORE "+action+" ON metadata WHEN NEW.key LIKE 'osmosis_replication_%' AND COALESCE((SELECT value FROM metadata WHERE key='railway_complete'),'0')='1' AND COALESCE((SELECT value FROM metadata WHERE key='railway_writer_active'),'0')!='1' BEGIN SELECT RAISE(ABORT,'Country replication requires a railway-aware writer'); END");
            }
        }
        public static int Verify(string root,Action<string> log=null)
        {
            Directory.CreateDirectory(root);var names=new List<string>();void Check(bool value,string message){if(!value)throw new Exception("RAIL REPLICATION FAIL: "+message);names.Add(message);log?.Invoke("RAIL REPLICATION PASS: "+message);}
            string legacy=Fixture(root,"legacy");
            var first=Diff(legacy,"10","11","<osmChange version='0.6'><modify><way id='100'><nd ref='1'/><nd ref='2'/><tag k='highway' v='residential'/><tag k='railway' v='tram'/><tag k='name' v='Shared road'/></way><way id='200'><nd ref='2'/><nd ref='3'/><tag k='railway' v='rail'/><tag k='bridge' v='yes'/><tag k='layer' v='1'/><tag k='gauge' v='1435'/></way></modify></osmChange>");Apply(legacy,first);
            Check(Number(legacy,"SELECT kind FROM features WHERE id=100")==0&&Number(legacy,"SELECT count(*) FROM railways")==2,"legacy update preserves a dual road/tram and adds pure railway independently");
            Check(Blob(legacy,"features",100).SequenceEqual(Blob(legacy,"railways",100)),"shared road and railway store identical source geometry");
            Check(Number(legacy,"SELECT count(*) FROM features WHERE id=200")==0&&Number(legacy,"SELECT count(*) FROM rail_bounds")==2,"pure railway has separate bounds without masquerading as an ordinary feature");
            Check(Text(legacy,"SELECT value FROM metadata WHERE key='railway_complete'")=="0"&&!(bool)new CountryPack(legacy).Metadata["capabilities"]["railwayComplete"],"incremental legacy import declares partial coverage in DB and JSON mirror");
            Check(Text(legacy,"SELECT value FROM metadata WHERE key='railway_source_sha256'")==null,"incremental-only update never invents a complete-source SHA anchor");
            Check(Number(legacy,"SELECT count(*) FROM usage")==4,"dual-purpose source nodes are stored once in union usage");
            Apply(legacy,Diff(legacy,"11","12","<osmChange version='0.6'><modify><node id='3' lat='52.0005' lon='-2.9995'/></modify></osmChange>"));
            using(var reader=new BinaryReader(new MemoryStream(Blob(legacy,"railways",200)))){reader.ReadInt32();reader.ReadBytes(16);reader.ReadInt64();Check(reader.ReadInt32()==520005000&&reader.ReadInt32()==-29995000,"node-only update reconstructs a rail-only way from its separate stored row");}
            Check(Number(legacy,"SELECT count(*) FROM features WHERE id=200")==0&&Text(legacy,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="12","pure-rail node update advances one country sequence without creating an ordinary feature");
            using(var db=new NativeMapDatabase(Path.Combine(legacy,"map.sqlite"),true))using(var q=db.Prepare("UPDATE features SET tags=?1 WHERE id=100")){q.Text(1,"{\"highway\":\"residential\",\"name\":\"Current road name\"}");q.Step();}
            Apply(legacy,Diff(legacy,"12","13","<osmChange version='0.6'><modify><node id='2' lat='52.0006' lon='-2.9994'/></modify></osmChange>"));
            Check(Blob(legacy,"features",100).SequenceEqual(Blob(legacy,"railways",100)),"node-only shared update refreshes both geometry representations together");
            Check((string)Tags(legacy,"features",100)["railway"]=="tram"&&(string)Tags(legacy,"features",100)["name"]=="Current road name","missing legacy rail tags are recovered while current ordinary attributes remain authoritative");
            Check(Number(legacy,"SELECT count(*) FROM usage")==4,"node-only reconstruction preserves exact union dependencies");
            Apply(legacy,Diff(legacy,"13","14","<osmChange version='0.6'><modify><way id='200'><nd ref='2'/><nd ref='3'/><tag k='railway' v='razed'/><tag k='historic:bridge' v='yes'/></way></modify></osmChange>"));
            Check(RailwayTags.Lifecycle(Tags(legacy,"railways",200))=="razed"&&Blob(legacy,"railways",200)!=null,"razed status removes operating classification but preserves historical source geometry");
            Apply(legacy,Diff(legacy,"14","15","<osmChange version='0.6'><modify><way id='100'><nd ref='1'/><nd ref='2'/><tag k='highway' v='residential'/></way></modify></osmChange>"));
            Check(Number(legacy,"SELECT kind FROM features WHERE id=100")==0&&Number(legacy,"SELECT count(*) FROM railways WHERE id=100")==0&&Number(legacy,"SELECT count(*) FROM rail_bounds WHERE id=100")==0,"explicit loss of rail tags removes rail row/bounds while preserving its road");
            Check(Number(legacy,"SELECT count(*) FROM usage WHERE way=100")==2,"shared road retains dependencies after railway classification disappears");
            Apply(legacy,Diff(legacy,"15","16","<osmChange version='0.6'><delete><way id='200'/><node id='3'/></delete></osmChange>"));
            Check(Number(legacy,"SELECT (SELECT count(*) FROM railways WHERE id=200)+(SELECT count(*) FROM rail_bounds WHERE id=200)+(SELECT count(*) FROM usage WHERE way=200)+(SELECT count(*) FROM nodes WHERE id=3)")==0,"explicit source deletion removes rail geometry, bounds and dependencies atomically");
            Check(Number(legacy,"SELECT count(*) FROM features WHERE id=100")==1,"deleting pure rail leaves the neighbouring ordinary road intact");
            string complete=Fixture(root,"complete",true);Apply(complete,Diff(complete,"10","11","<osmChange version='0.6'><modify><way id='201'><nd ref='2'/><nd ref='4'/><tag k='abandoned:railway' v='rail'/></way></modify></osmChange>"));
            Check(Text(complete,"SELECT value FROM metadata WHERE key='railway_complete'")=="1"&&(bool)new CountryPack(complete).Metadata["capabilities"]["railwayComplete"],"previously backfilled complete capability remains complete across a diff");
            Check(Text(complete,"SELECT value FROM metadata WHERE key='railway_source_sha256'")==FixtureSourceAnchor,"replication preserves original full-source provenance anchor");
            Check(RailwayTags.Lifecycle(Tags(complete,"railways",201))=="abandoned","namespaced abandoned railway remains a distinct nonoperating source record");
            byte[] beforeRail=Blob(complete,"railways",201),beforeMain=Blob(complete,"features",100);long beforeLat=Number(complete,"SELECT lat FROM nodes WHERE id=2");
            var broken=Diff(complete,"11","12","<osmChange version='0.6'><modify><node id='2' lat='53' lon='-2'/><way id='201'><nd ref='2'/><nd ref='9999'/><tag k='railway' v='rail'/></way></modify></osmChange>");bool rejected=false;try{Apply(complete,broken);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"missing changed railway reference rejects the whole transaction");
            Check(Number(complete,"SELECT lat FROM nodes WHERE id=2")==beforeLat&&Blob(complete,"railways",201).SequenceEqual(beforeRail)&&Blob(complete,"features",100).SequenceEqual(beforeMain),"failed railway update rolls back node, main-feature and railway geometry together");
            Check(Text(complete,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="11"&&Text(complete,"SELECT value FROM metadata WHERE key='railway_complete'")=="1","rollback preserves country sequence and complete-source capability");
            var tamper=Diff(complete,"11","12","<osmChange version='0.6'><modify><node id='2' lat='52.0008' lon='-2.9992'/></modify></osmChange>");tamper.sha256=new string('0',64);rejected=false;try{Apply(complete,tamper);}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Number(complete,"SELECT lat FROM nodes WHERE id=2")==beforeLat,"tampered update hash rejects before any country mutation");
            string badTopology=Fixture(root,"bad-topology",true);using(var db=new NativeMapDatabase(Path.Combine(badTopology,"map.sqlite"),true))using(var q=db.Prepare("UPDATE railways SET geometry=?1 WHERE id=100")){q.Bytes(1,Geometry(1,3));q.Step();}
            rejected=false;try{Apply(badTopology,Diff(badTopology,"10","11","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/></modify></osmChange>"));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Number(badTopology,"SELECT lat FROM nodes WHERE id=1")==520001000&&Text(badTopology,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="10","inconsistent shared node topology rejects and rolls back instead of merging stale rail geometry");
            string legacyFailure=Fixture(root,"legacy-failure");rejected=false;try{Apply(legacyFailure,Diff(legacyFailure,"10","11","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/><way id='300'><nd ref='1'/><nd ref='9999'/><tag k='railway' v='rail'/></way></modify></osmChange>"));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Number(legacyFailure,"SELECT count(*) FROM sqlite_master WHERE name IN ('railways','rail_bounds')")==0,"failed legacy update rolls back rail schema creation too");
            Check(Text(legacyFailure,"SELECT value FROM metadata WHERE key='railway_complete'")==null&&Text(legacyFailure,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="10","failed legacy migration does not publish partial metadata or sequence");
            string priorities=Fixture(root,"priorities");var changes=new StringBuilder("<osmChange version='0.6'><modify>");string[] keys={"highway","waterway","building","landuse"},values={"residential","river","yes","railway"};for(int k=0;k<4;k++)changes.Append("<way id='").Append(500+k).Append("'><nd ref='1'/><nd ref='2'/><tag k='").Append(keys[k]).Append("' v='").Append(values[k]).Append("'/><tag k='railway' v='abandoned'/></way>");changes.Append("</modify></osmChange>");Apply(priorities,Diff(priorities,"10","11",changes.ToString()));
            Check(Enumerable.Range(0,4).All(k=>Number(priorities,"SELECT kind FROM features WHERE id="+(500+k))==k)&&Number(priorities,"SELECT count(*) FROM railways")==4,"all normal feature kinds retain their priority beside independent historical rail rows");
            Check(Number(priorities,"SELECT count(*) FROM usage WHERE way>=500 AND way<=503")==8,"four dual-purpose rows each retain two union dependencies, not duplicate usage");
            Check(Text(priorities,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="11"&&Text(priorities,"SELECT value FROM metadata WHERE key='railway_schema'")=="1","dual-table work and schema version commit with one country sequence");
            Apply(complete,Diff(complete,"11","12","<osmChange version='0.6'><delete><way id='100'/></delete></osmChange>"));
            Check(Number(complete,"SELECT (SELECT count(*) FROM features WHERE id=100)+(SELECT count(*) FROM bounds WHERE id=100)+(SELECT count(*) FROM railways WHERE id=100)+(SELECT count(*) FROM rail_bounds WHERE id=100)+(SELECT count(*) FROM usage WHERE way=100)")==0,"source deletion of a dual-purpose way retires both features, both bounds and union dependencies");
            Check(Number(complete,"SELECT count(*) FROM railways WHERE id=201")==1&&Text(complete,"SELECT value FROM metadata WHERE key='railway_complete'")=="1","dual deletion leaves other railway rows and complete capability intact");
            string missingTables=Fixture(root,"missing-complete-tables");using(var db=new NativeMapDatabase(Path.Combine(missingTables,"map.sqlite"),true))db.Execute("INSERT INTO metadata VALUES('railway_complete','1')");
            rejected=false;try{Apply(missingTables,Diff(missingTables,"10","11","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/></modify></osmChange>"));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Number(missingTables,"SELECT lat FROM nodes WHERE id=1")==520001000&&Number(missingTables,"SELECT count(*) FROM sqlite_master WHERE name='railways'")==0,"a false complete flag with missing base tables is rejected instead of manufacturing complete empty coverage");
            string retopology=Fixture(root,"retopology");Apply(retopology,Diff(retopology,"10","11","<osmChange version='0.6'><modify><way id='100'><nd ref='2'/><nd ref='4'/><tag k='highway' v='residential'/><tag k='railway' v='tram'/></way></modify></osmChange>"));
            Check(Number(retopology,"SELECT count(*) FROM usage WHERE way=100 AND node IN (2,4)")==2&&Number(retopology,"SELECT count(*) FROM usage WHERE way=100 AND node=1")==0,"changed shared topology rebuilds union usage and removes obsolete source-node dependencies");
            byte[] stable=Blob(retopology,"railways",100);Apply(retopology,Diff(retopology,"11","12","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/></modify></osmChange>"));
            Check(Blob(retopology,"railways",100).SequenceEqual(stable),"a former node no longer moves the reconnected road/rail way");
            Apply(retopology,Diff(retopology,"12","13","<osmChange version='0.6'><modify><node id='4' lat='52.0009' lon='-2.9991'/></modify></osmChange>"));
            Check(!Blob(retopology,"railways",100).SequenceEqual(stable)&&Blob(retopology,"railways",100).SequenceEqual(Blob(retopology,"features",100)),"new dependency moves both reconnected representations on the next node-only update");
            string future=Fixture(root,"future-schema");using(var db=new NativeMapDatabase(Path.Combine(future,"map.sqlite"),true))db.Execute("INSERT INTO metadata VALUES('railway_schema','2')");
            rejected=false;try{Apply(future,Diff(future,"10","11","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/></modify></osmChange>"));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Text(future,"SELECT value FROM metadata WHERE key='railway_schema'")=="2"&&Text(future,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="10","unknown future railway schema is rejected without downgrading metadata or country sequence");
            string conflict=Fixture(root,"conflicting-lifecycle",true);using(var db=new NativeMapDatabase(Path.Combine(conflict,"map.sqlite"),true))using(var q=db.Prepare("UPDATE features SET tags=?1 WHERE id=100")){q.Text(1,"{\"highway\":\"residential\",\"railway\":\"rail\"}");q.Step();}
            rejected=false;try{Apply(conflict,Diff(conflict,"10","11","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/></modify></osmChange>"));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Number(conflict,"SELECT lat FROM nodes WHERE id=1")==520001000&&Text(conflict,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="10","conflicting same-way railway lifecycles reject rather than silently selecting a stale representation");
            string guarded=Fixture(root,"guarded",true);Guard(guarded);
            Apply(guarded,Diff(guarded,"10","11","<osmChange version='0.6'><modify><node id='1' lat='52.0007' lon='-2.9993'/></modify></osmChange>"));
            Check(Text(guarded,"SELECT value FROM metadata WHERE key='railway_writer_active'")=="0"&&Text(guarded,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="11"&&Blob(guarded,"features",100).SequenceEqual(Blob(guarded,"railways",100)),"rail-aware update passes installed write guards, updates both geometries and clears active marker before commit");
            long guardedLat=Number(guarded,"SELECT lat FROM nodes WHERE id=1");rejected=false;
            using(var db=new NativeMapDatabase(Path.Combine(guarded,"map.sqlite"),true))
            {
                db.Execute("BEGIN IMMEDIATE");try{db.Execute("UPDATE nodes SET lat=530000000 WHERE id=1");db.Execute("INSERT OR REPLACE INTO metadata VALUES('osmosis_replication_sequence_number','12')");db.Execute("COMMIT");}catch(InvalidOperationException){rejected=true;db.Execute("ROLLBACK");}
            }
            Check(rejected&&Number(guarded,"SELECT lat FROM nodes WHERE id=1")==guardedLat&&Text(guarded,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="11","old-style sequence INSERT OR REPLACE is blocked and its earlier node mutations roll back");
            rejected=false;using(var db=new NativeMapDatabase(Path.Combine(guarded,"map.sqlite"),true))try{db.Execute("UPDATE metadata SET value='12' WHERE key='osmosis_replication_sequence_number'");}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&Text(guarded,"SELECT value FROM metadata WHERE key='railway_complete'")=="1"&&Text(guarded,"SELECT value FROM metadata WHERE key='railway_writer_active'")=="0","direct legacy sequence UPDATE is also blocked without changing completeness or active marker");
            rejected=false;try{Apply(guarded,Diff(guarded,"11","12","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/><way id='300'><nd ref='1'/><nd ref='9999'/><tag k='railway' v='rail'/></way></modify></osmChange>"));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Text(guarded,"SELECT value FROM metadata WHERE key='railway_writer_active'")=="0"&&Number(guarded,"SELECT lat FROM nodes WHERE id=1")==guardedLat&&Text(guarded,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="11","injected rail-aware failure rolls active marker, nodes and guarded sequence back together");
            Apply(guarded,Diff(guarded,"11","12","<osmChange version='0.6'><modify><way id='301'><nd ref='2'/><nd ref='4'/><tag k='railway' v='rail'/></way></modify></osmChange>"));
            Check(Number(guarded,"SELECT count(*) FROM railways WHERE id=301")==1&&Text(guarded,"SELECT value FROM metadata WHERE key='railway_writer_active'")=="0"&&Text(guarded,"SELECT value FROM metadata WHERE key='railway_complete'")=="1","next valid update still succeeds after blocked legacy and failed modern transactions");
            string invalidSourceAnchor=Fixture(root,"invalid-source-anchor",true);using(var db=new NativeMapDatabase(Path.Combine(invalidSourceAnchor,"map.sqlite"),true))db.Execute("UPDATE metadata SET value='not-a-sha256' WHERE key='railway_source_sha256'");
            rejected=false;try{Apply(invalidSourceAnchor,Diff(invalidSourceAnchor,"10","11","<osmChange version='0.6'><modify><node id='1' lat='53' lon='-2'/></modify></osmChange>"));}catch(InvalidDataException){rejected=true;}
            Check(rejected&&Text(invalidSourceAnchor,"SELECT value FROM metadata WHERE key='osmosis_replication_sequence_number'")=="10"&&Number(invalidSourceAnchor,"SELECT lat FROM nodes WHERE id=1")==520001000,"invalid complete-source SHA fixture is still refused without weakening reader validation or changing country state");
            File.WriteAllText(Path.Combine(root,"railway-replication-results.json"),new JObject{{"result","PASS"},{"checks",names.Count},{"cases",new JArray(names)},{"scope","Actual SQLite update/rollback fixtures; no live country database or rendered-world claims"}}.ToString());return names.Count;
        }
        public static void Run()
        {try{string root=Path.GetFullPath("Builds/Verification/RailwayReplication-"+DateTime.UtcNow.Ticks);int checks=Verify(root,Debug.Log);Debug.Log("RAIL REPLICATION COMPLETE "+checks);EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    }
}
