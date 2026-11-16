using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;
using RoadRush.Core;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class MapReplicationVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool value,string message){if(!value)throw new Exception("REPLICATION FAIL: "+message);checks++;Debug.Log("REPLICATION PASS: "+message);}
            try
            {
                string root=Path.GetFullPath("Builds/Verification/replication-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);string database=Path.Combine(root,"map.sqlite");File.WriteAllBytes(database,Array.Empty<byte>());
                using(var db=new NativeMapDatabase(database,true))
                {
                    foreach(var sql in new[]{"CREATE TABLE features(id INTEGER PRIMARY KEY,kind INTEGER,tags TEXT,geometry BLOB)","CREATE VIRTUAL TABLE bounds USING rtree(id,minLon,maxLon,minLat,maxLat)","CREATE TABLE nodes(id INTEGER PRIMARY KEY,lat INTEGER,lon INTEGER)","CREATE TABLE usage(node INTEGER,way INTEGER,PRIMARY KEY(node,way)) WITHOUT ROWID","CREATE INDEX usage_way ON usage(way)","CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT)","INSERT INTO metadata VALUES('osmosis_replication_sequence_number','10')","INSERT INTO nodes VALUES(1,520000000,-30000000),(2,520001000,-30000000),(3,520001000,-29999000)","INSERT INTO usage VALUES(1,100),(2,100)","INSERT INTO bounds VALUES(100,-3,-3,52,52.0001)"})db.Execute(sql);
                    using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
                    {writer.Write(2);writer.Write(1L);writer.Write(520000000);writer.Write(-30000000);writer.Write(2L);writer.Write(520001000);writer.Write(-30000000);using(var put=db.Prepare("INSERT INTO features VALUES(100,0,?1,?2)")){put.Text(1,"{\"highway\":\"residential\",\"name\":\"Fixture road\"}").Bytes(2,stream.ToArray());put.Step();}}
                }
                var metadata=new JObject{{"format",2},{"completeNodes",true},{"name","Fixture"},{"country","GB"},{"version","10"},{"mapReady",true},{"terrainReady",true},{"bounds",new JArray(-4,51,-2,53)},{"databaseSHA256",DataPackageRules.HashFile(database)},{"replication",new JObject{{"osmosis_replication_sequence_number","10"},{"osmosis_replication_base_url","https://download.geofabrik.de/europe/united-kingdom/wales-updates"},{"osmosis_replication_timestamp","2026-11-01T00:00:00Z"}}}};
                string packPath=Path.Combine(root,"pack.json");File.WriteAllText(packPath,metadata.ToString());
                DataFileManifest Diff(string name,string from,string to,string text)
                {
                    string path=Path.Combine(root,name+".osc.gz");using(var file=File.Create(path))using(var gzip=new GZipStream(file,CompressionMode.Compress))using(var writer=new StreamWriter(gzip,Encoding.UTF8))writer.Write(text);
                    return new DataFileManifest{id=name,kind="map-diff",pack=new CountryPack(root).Id,fromVersion=from,toVersion=to,source=new CountryPack(root).ReplicationSource+"/"+ReplicationState.File(long.Parse(to))+".osc.gz",licence="ODbL-1.0",bytes=new FileInfo(path).Length,sha256=DataPackageRules.HashFile(path)};
                }
                string PathFor(DataFileManifest m)=>Path.Combine(root,m.id+".osc.gz");
                var first=Diff("daily11","10","11","<osmChange version='0.6'><modify><node id='2' lat='52.0002' lon='-2.9998'/><way id='101'><nd ref='2'/><nd ref='3'/><tag k='highway' v='service'/></way></modify></osmChange>");
                var result=MapReplication.Apply(new CountryPack(root),PathFor(first),first,"2026-11-02T00:00:00Z");Check(result.nodes==1&&result.ways==2,"node-only changes rebuild existing referring ways and add explicit ways");
                using(var db=new NativeMapDatabase(database))
                {
                    Check(MapReplication.Sequence(db)=="11","country sequence advances only after an applied transaction");
                    using(var row=db.Prepare("SELECT geometry FROM features WHERE id=100")){Check(row.Step(),"unchanged way survives its node update");using(var reader=new BinaryReader(new MemoryStream(row.Bytes(0)))){reader.ReadInt32();reader.ReadBytes(16);reader.ReadInt64();Check(reader.ReadInt32()==520002000&&reader.ReadInt32()==-29998000,"existing way geometry uses the new node coordinate");}}
                    using(var row=db.Prepare("SELECT count(*) FROM usage WHERE way=101")){row.Step();Check(row.Integer(0)==2,"new way retains future node-change dependencies");}
                }
                var second=Diff("daily12","11","12","<osmChange version='0.6'><delete><way id='100'/><node id='1'/></delete></osmChange>");MapReplication.Apply(new CountryPack(root),PathFor(second),second,"2026-11-03T00:00:00Z");
                using(var db=new NativeMapDatabase(database))using(var row=db.Prepare("SELECT (SELECT count(*) FROM features WHERE id=100)+(SELECT count(*) FROM bounds WHERE id=100)+(SELECT count(*) FROM usage WHERE way=100)+(SELECT count(*) FROM nodes WHERE id=1)")){row.Step();Check(row.Integer(0)==0,"deletions remove feature bounds, dependencies and unused deleted nodes");}
                var broken=Diff("broken13","12","13","<osmChange version='0.6'><modify><node id='2' lat='53' lon='-2'/><way id='101'><nd ref='2'/><nd ref='999'/><tag k='highway' v='service'/></way></modify></osmChange>");bool rejected=false;
                try{MapReplication.Apply(new CountryPack(root),PathFor(broken),broken,"2026-11-04T00:00:00Z");}catch(InvalidDataException){rejected=true;}Check(rejected,"a missing reference rejects the complete update");
                using(var db=new NativeMapDatabase(database)){Check(MapReplication.Sequence(db)=="12","failed update preserves the previous dataset version");using(var row=db.Prepare("SELECT lat FROM nodes WHERE id=2")){row.Step();Check(row.Integer(0)==520002000,"failed update rolls back node mutations too");}}
                var xml=Diff("xml13","12","13","<!DOCTYPE osmChange [<!ENTITY a SYSTEM 'file:///etc/passwd'>]><osmChange version='0.6'><modify><node id='2' lat='52' lon='-3'/></modify></osmChange>");rejected=false;try{MapReplication.Apply(new CountryPack(root),PathFor(xml),xml,"");}catch(XmlException){rejected=true;}Check(rejected,"external entities and DTDs are prohibited");
                first.sha256=new string('0',64);rejected=false;try{MapReplication.Apply(new CountryPack(root),PathFor(first),first,"");}catch(InvalidDataException){rejected=true;}Check(rejected,"corrupted or stale payload cannot touch the country database");
                File.WriteAllText(packPath,metadata.ToString());var recovered=new CountryPack(root);
                Check(recovered.Version=="12"&&(string)recovered.Metadata["lastDiffSHA256"]==second.sha256,"a stale JSON mirror recovers the committed SQLite version and update hash");
                Check((string)recovered.Metadata["baseDatabaseSHA256"]==(string)metadata["databaseSHA256"],"metadata recovery retains original country-database provenance");
                rejected=false;try{MapReplication.Apply(recovered,PathFor(second),second,"");}catch(InvalidDataException){rejected=true;}Check(rejected,"an already applied daily update is rejected");
                var skipped=Diff("skipped14","12","14","<osmChange version='0.6'><modify><node id='3' lat='52.0003' lon='-2.9999'/></modify></osmChange>");rejected=false;try{MapReplication.Apply(recovered,PathFor(skipped),skipped,"");}catch(InvalidDataException){rejected=true;}Check(rejected,"country updates cannot skip a replication sequence");
                var third=Diff("daily13","12","13","<osmChange version='0.6'><modify><node id='3' lat='52.0003' lon='-2.9999'/></modify></osmChange>");third.pack="other-country";rejected=false;try{MapReplication.Apply(recovered,PathFor(third),third,"");}catch(InvalidDataException){rejected=true;}Check(rejected,"a different country identity cannot authorize an update");third.pack=recovered.Id;
                string canonical=third.source;third.source="https://download.geofabrik.de/europe/germany-updates/000/000/013.osc.gz";rejected=false;try{MapReplication.Apply(recovered,PathFor(third),third,"");}catch(InvalidDataException){rejected=true;}Check(rejected,"matching hashes cannot authorize a different country's source URL");
                third.source=canonical.Replace("013.osc.gz","014.osc.gz");rejected=false;try{MapReplication.Apply(recovered,PathFor(third),third,"");}catch(InvalidDataException){rejected=true;}Check(rejected,"source URL must name the exact replication sequence");third.source=canonical;
                MapReplication.Apply(recovered,PathFor(third),third,"2026-11-04T00:00:00Z");rejected=false;try{recovered.Extract(new WorldPlace{latitude=52.0002,longitude=-2.9999,country_code="GB",size=1000});}catch(InvalidOperationException){rejected=true;}Check(rejected,"a stale country reader cannot label newer geometry with its old version");
                rejected=false;using(var readonlyDb=new NativeMapDatabase(database))try{readonlyDb.Execute("DELETE FROM nodes WHERE id=2");}catch(InvalidOperationException){rejected=true;}Check(rejected,"query-only readers cannot mutate the country, including copied WAL databases");
                File.WriteAllText("Builds/Verification/map-replication.txt","PASS "+checks+" SQLite replication/rollback/security checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
