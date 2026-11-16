using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class RailwayCountryVerification
    {
        static readonly Vector2[] Loop={new Vector2(-120,-120),new Vector2(0,-120),new Vector2(120,-120),new Vector2(120,0),new Vector2(120,120),new Vector2(0,120),new Vector2(-120,120),new Vector2(-120,0)};
        static WorldPlace Place()=>new WorldPlace{name="Isolated railway country fixture",latitude=51.671156,longitude=-3.131748,country_code="GB",timezone="Europe/London",drivingSide=-1,size=1000};
        static byte[] Geometry(WorldPlace place,Vector2[] points,long[] ids)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
            {
                writer.Write(points.Length);double cos=Math.Cos(place.latitude*Math.PI/180);
                for(int i=0;i<points.Length;i++){writer.Write(ids[i]);writer.Write((int)Math.Round((place.latitude+points[i].y/111319.4908)*1e7));writer.Write((int)Math.Round((place.longitude+points[i].x/(111319.4908*cos))*1e7));}return stream.ToArray();
            }
        }
        static void Put(NativeMapDatabase db,string table,long id,JObject tags,byte[] geometry)
        {
            using(var put=db.Prepare(table=="features"?"INSERT INTO features VALUES(?1,0,?2,?3)":"INSERT INTO railways VALUES(?1,?2,?3)")){put.Integer(1,id).Text(2,tags.ToString(Newtonsoft.Json.Formatting.None)).Bytes(3,geometry);put.Step();}
            double west=180,south=90,east=-180,north=-90;using(var reader=new BinaryReader(new MemoryStream(geometry))){int count=reader.ReadInt32();for(int i=0;i<count;i++){reader.ReadInt64();double lat=reader.ReadInt32()/1e7,lon=reader.ReadInt32()/1e7;west=Math.Min(west,lon);east=Math.Max(east,lon);south=Math.Min(south,lat);north=Math.Max(north,lat);}}
            using(var put=db.Prepare("INSERT INTO "+(table=="features"?"bounds":"rail_bounds")+" VALUES(?1,?2,?3,?4,?5)")){put.Integer(1,id).Number(2,west).Number(3,east).Number(4,south).Number(5,north);put.Step();}
        }
        static string Fixture(string root,string name)
        {
            var place=Place();string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);string path=Path.Combine(folder,"map.sqlite");File.WriteAllBytes(path,Array.Empty<byte>());
            using(var db=new NativeMapDatabase(path,true))
            {
                foreach(string sql in new[]{"CREATE TABLE features(id INTEGER PRIMARY KEY,kind INTEGER,tags TEXT,geometry BLOB)","CREATE VIRTUAL TABLE bounds USING rtree(id,minLon,maxLon,minLat,maxLat)","CREATE TABLE nodes(id INTEGER PRIMARY KEY,lat INTEGER,lon INTEGER)","CREATE TABLE usage(node INTEGER,way INTEGER,PRIMARY KEY(node,way)) WITHOUT ROWID","CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT)","INSERT INTO metadata VALUES('osmosis_replication_sequence_number','10'),('osmosis_replication_timestamp','2026-11-03T21:24:23Z')"})db.Execute(sql);
                for(int i=0;i<8;i++)
                {
                    var tags=new JObject{{"highway","residential"},{"name","Fixture loop "+i}};if(i==2||i==6)tags["maxheight"]="13'3\"";
                    Put(db,"features",100+i,tags,Geometry(place,new[]{Loop[i],Loop[(i+1)%8]},new[]{(long)i+1,(i+1)%8+1L}));
                }
            }
            File.WriteAllText(Path.Combine(folder,"pack.json"),new JObject{{"format",2},{"name","Country reader fixture"},{"country","GB"},{"completeNodes",true},{"version","10"},{"mapReady",true},{"terrainReady",true},{"bounds",new JArray(-4,51,-2,53)},{"replication",new JObject{{"osmosis_replication_sequence_number","10"},{"osmosis_replication_timestamp","2026-11-03T21:24:23Z"},{"osmosis_replication_base_url","https://download.geofabrik.de/europe/united-kingdom/wales-updates"}}}}.ToString());return folder;
        }
        static void RailTables(string folder,string complete="1",string source=null)
        {
            using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite"),true))
            {
                db.Execute("CREATE TABLE railways(id INTEGER PRIMARY KEY,tags TEXT NOT NULL,geometry BLOB NOT NULL)");db.Execute("CREATE VIRTUAL TABLE rail_bounds USING rtree(id,minLon,maxLon,minLat,maxLat)");
                using(var q=db.Prepare("INSERT INTO metadata VALUES('railway_schema','1'),('railway_complete',?1),('railway_source_sha256',?2)")){q.Text(1,complete).Text(2,source??new string('a',64));q.Step();}
            }
        }
        static byte[] Blob(NativeMapDatabase db,string table,long id)
        {using(var q=db.Prepare("SELECT geometry FROM "+table+" WHERE id=?1")){q.Integer(1,id);if(!q.Step())throw new Exception("Missing fixture geometry");return q.Bytes(0);}}
        static void StandardRails(string folder)
        {
            var place=Place();using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite"),true))
            {
                Put(db,"railways",100,new JObject{{"railway","tram"},{"gauge","1435"},{"tracks","1"}},Blob(db,"features",100));
                Put(db,"railways",900,new JObject{{"railway","rail"},{"bridge","yes"},{"layer","1"},{"gauge","1435"},{"tracks","2"},{"width","8"},{"name","Fixture elevated line"},{"operator","Fixture Rail"},{"electrified","no"}},Geometry(place,new[]{new Vector2(-180,0),new Vector2(180,0)},new[]{9001L,9002L}));
                Put(db,"railways",901,new JObject{{"railway","rail"}},Geometry(place,new[]{new Vector2(3000,0),new Vector2(3010,0)},new[]{9011L,9012L}));
                Put(db,"railways",902,new JObject{{"railway","disused"}},Geometry(place,new[]{new Vector2(-650,230),new Vector2(650,230)},new[]{9021L,9022L}));
            }
        }
        static void Meta(string folder,string sql){using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite"),true))foreach(string statement in sql.Split(';'))if(!string.IsNullOrWhiteSpace(statement))db.Execute(statement);}
        static bool Refused(Action action)
        {try{action();return false;}catch(InvalidDataException){return true;}catch(InvalidOperationException){return true;}}
        static JObject Raw(string folder)=>JObject.Parse(new CountryPack(folder).Extract(Place()));
        public static int Verify(string root,Action<string> log=null)
        {
            Directory.CreateDirectory(root);var cases=new List<string>();void Check(bool value,string message){if(!value)throw new Exception("RAIL COUNTRY FAIL: "+message);cases.Add(message);log?.Invoke("RAIL COUNTRY PASS: "+message);}
            string priorCache=RegionCache.OverrideFolder,priorProfile=PlayerProfile.StorageOverride;
            RegionCache.OverrideFolder=Path.Combine(root,"Regions");PlayerProfile.StorageOverride=Path.Combine(root,"isolated-profile.json");
            try
            {
                var place=Place();string folder=Fixture(root,"identity-transition");var before=new CountryPack(folder);string oldId=RegionCache.PackId(place,before),oldRaw=before.Extract(place);
                Check(!before.HasRailways&&!before.RailwaysComplete&&before.RailwayIdentity=="railways-none","legacy country remains readable without any railway schema or tables");
                Check(((JArray)JObject.Parse(oldRaw)["elements"]).Count==8,"legacy ordinary source returns all eight connected driveable ways");
                Check((string)JObject.Parse(oldRaw)["earthUnscriptedRailwaySource"]["identity"]==before.RailwayIdentity,"legacy raw-source receipt explicitly identifies absence of railway capability");
                RailTables(folder);StandardRails(folder);var after=new CountryPack(folder);string id=RegionCache.PackId(place,after),source=after.Extract(place);var raw=JObject.Parse(source);var elements=(JArray)raw["elements"];
                Check(after.HasRailways&&after.RailwaysComplete&&after.Version==before.Version&&after.RailwayIdentity!=before.RailwayIdentity,"full railway schema changes source identity at the same country replication sequence");
                Check(id!=oldId&&after.Id==before.Id,"rail-aware area cache ID changes while stable country/provider ID remains the same");
                Check(Refused(()=>before.Extract(place)),"reader captured before same-sequence migration cannot label new rail geometry with its old identity");
                Check(elements.Count==10&&elements.Count(e=>(long)e["id"]==100)==1,"shared road/tram is merged once, pure and crossing-window tracks included");
                var shared=elements.Single(e=>(long)e["id"]==100);Check((string)shared["tags"]["highway"]=="residential"&&(string)shared["tags"]["railway"]=="tram"&&(string)shared["tags"]["name"]=="Fixture loop 0","missing railway tags merge without losing ordinary highway attributes");
                Check(elements.Any(e=>(long)e["id"]==900)&&!elements.Any(e=>(long)e["id"]==901),"spatial source bounds include local pure rail and exclude remote rail");
                var crossing=elements.Single(e=>(long)e["id"]==902);Check(((JArray)crossing["geometry"]).Count==2&&(double)crossing["geometry"][0]["lon"]<place.longitude,"whole source way crossing the window is retained even when its endpoints are outside");
                Check((string)raw["earthUnscriptedRailwaySource"]["identity"]==after.RailwayIdentity&&(bool)raw["earthUnscriptedRailwaySource"]["complete"],"raw OSM sidecar carries exact complete railway identity");
                string mismatch=Fixture(root,"geometry-conflict");RailTables(mismatch);using(var db=new NativeMapDatabase(Path.Combine(mismatch,"map.sqlite"),true))Put(db,"railways",100,new JObject{{"railway","tram"}},Geometry(place,new[]{new Vector2(-120,-120),new Vector2(10,-120)},new[]{1L,2L}));
                Check(Refused(()=>new CountryPack(mismatch).Extract(place)),"same-ID railway with different coordinates rejects instead of replacing road geometry");
                string tagConflict=Fixture(root,"tag-conflict");RailTables(tagConflict);using(var db=new NativeMapDatabase(Path.Combine(tagConflict,"map.sqlite"),true))Put(db,"railways",100,new JObject{{"railway","tram"},{"name","Conflicting source name"}},Blob(db,"features",100));
                Check(Refused(()=>new CountryPack(tagConflict).Extract(place)),"same-geometry contradictory source tags reject instead of overwriting ordinary attributes");
                string partial=Fixture(root,"partial");RailTables(partial,"0","");var part=new CountryPack(partial);Check(part.HasRailways&&!part.RailwaysComplete&&part.RailwayIdentity.Contains("partial"),"explicit partial railway schema is available but never labelled complete");
                string missingTables=Fixture(root,"missing-tables");Meta(missingTables,"INSERT INTO metadata VALUES('railway_schema','1'),('railway_complete','1'),('railway_source_sha256','"+new string('a',64)+"')");Check(Refused(()=>new CountryPack(missingTables)),"declared railway capability with missing tables is rejected");
                string future=Fixture(root,"future-schema");RailTables(future);Meta(future,"UPDATE metadata SET value='2' WHERE key='railway_schema'");Check(Refused(()=>new CountryPack(future)),"unknown future railway schema is rejected");
                string invalidFlag=Fixture(root,"invalid-complete");RailTables(invalidFlag,"2");Check(Refused(()=>new CountryPack(invalidFlag)),"unknown completeness value is not silently downgraded to partial");
                string missingFlag=Fixture(root,"missing-complete");RailTables(missingFlag);Meta(missingFlag,"DELETE FROM metadata WHERE key='railway_complete'");Check(Refused(()=>new CountryPack(missingFlag)),"schema without explicit completeness metadata is rejected");
                string invalidSource=Fixture(root,"invalid-source");RailTables(invalidSource,"1","unverified");Check(Refused(()=>new CountryPack(invalidSource)),"complete coverage requires a real SHA256-shaped source identity");
                string changedSource=Fixture(root,"changed-source");RailTables(changedSource);var captured=new CountryPack(changedSource);string capturedId=RegionCache.PackId(place,captured);Meta(changedSource,"UPDATE metadata SET value='"+new string('b',64)+"' WHERE key='railway_source_sha256'");var newSource=new CountryPack(changedSource);Check(Refused(()=>captured.Extract(place))&&RegionCache.PackId(place,newSource)!=capturedId,"source revision change at unchanged sequence invalidates captured reader and cache identity");
                string transportFolder=Fixture(root,"transport-transition");RailTables(transportFolder);var beforeTransport=new CountryPack(transportFolder);string beforeTransportId=RegionCache.PackId(place,beforeTransport);
                Meta(transportFolder,"INSERT INTO metadata VALUES('transport_schema','1'),('transport_source_sha256','"+new string('a',64)+"'),('transport_content_sha256','"+new string('c',64)+"')");
                var afterTransport=new CountryPack(transportFolder);
                Check(afterTransport.Version==beforeTransport.Version&&afterTransport.RailwayIdentity.Contains("|transport1|")&&RegionCache.PackId(place,afterTransport)!=beforeTransportId,"same-sequence road metadata backfill changes bridge profile provenance and prepared cache ID");
                Check(Refused(()=>beforeTransport.Extract(place)),"reader opened before road metadata backfill cannot extract with stale clearance provenance");
                Meta(transportFolder,"UPDATE metadata SET value='2' WHERE key='transport_schema'");Check(Refused(()=>new CountryPack(transportFolder)),"unknown transport metadata schema is rejected");
                Meta(transportFolder,"UPDATE metadata SET value='1' WHERE key='transport_schema'; UPDATE metadata SET value='bad' WHERE key='transport_content_sha256'");Check(Refused(()=>new CountryPack(transportFolder)),"transport supplement content identity must be a SHA256");
                Meta(transportFolder,"UPDATE metadata SET value='"+new string('c',64)+"' WHERE key='transport_content_sha256'; UPDATE metadata SET value='"+new string('b',64)+"' WHERE key='transport_source_sha256'");Check(Refused(()=>new CountryPack(transportFolder)),"transport supplement from another PBF is rejected");
                Meta(transportFolder,"DELETE FROM metadata WHERE key='transport_schema'");Check(Refused(()=>new CountryPack(transportFolder)),"partial transport capability markers fail closed");
                string tooMany=Fixture(root,"way-budget");RailTables(tooMany);using(var db=new NativeMapDatabase(Path.Combine(tooMany,"map.sqlite"),true)){db.Execute("BEGIN");var geometry=Geometry(place,new[]{new Vector2(0,0),new Vector2(1,0)},new[]{7001L,7002L});for(int i=0;i<=RailwayMetrics.MaximumWays;i++)Put(db,"railways",10000+i,new JObject{{"railway","rail"}},geometry);db.Execute("COMMIT");}Check(Refused(()=>new CountryPack(tooMany).Extract(place)),"rail source way limit rejects dense request instead of silently truncating it");
                string malformed=Fixture(root,"point-budget");RailTables(malformed);using(var db=new NativeMapDatabase(Path.Combine(malformed,"map.sqlite"),true))
                {
                    var points=Enumerable.Repeat(new Vector2(0,0),50001).ToArray();Put(db,"railways",20000,new JObject{{"railway","rail"}},Geometry(place,points,Enumerable.Range(1,points.Length).Select(v=>(long)v).ToArray()));
                }
                Check(Refused(()=>new CountryPack(malformed).Extract(place)),"per-source-way point count above fifty thousand is rejected before export");
                string aggregate=Fixture(root,"aggregate-point-budget");RailTables(aggregate);using(var db=new NativeMapDatabase(Path.Combine(aggregate,"map.sqlite"),true))
                {
                    var points=Enumerable.Repeat(new Vector2(0,0),50000).ToArray();var geometry=Geometry(place,points,Enumerable.Range(1,points.Length).Select(v=>(long)v).ToArray());
                    db.Execute("BEGIN");for(int i=0;i<5;i++)Put(db,"railways",21000+i,new JObject{{"railway","rail"}},geometry);db.Execute("COMMIT");
                }
                Check(Refused(()=>new CountryPack(aggregate).Extract(place)),"aggregate railway source point budget rejects five individually valid fifty-thousand-point ways");
                // Full conversion uses the same saved source, not invented rail-only JSON.
                var region=GlobalRegions.Convert(place,source,new[]{100f,100f,100f,100f},2,1000);region.id=id;region.datasetVersion=after.Name+"/"+after.Version;region.datasetSource=after.ReplicationSource;
                Check(region.railwaySourceIdentity==after.RailwayIdentity&&region.railwaySourceComplete,"country source identity and completeness enter the canonical world fields");
                Check(region.roads.Length==8&&region.railways.Length==3,"actual country source converts into eight roads plus tram, bridge and clipped disused rail");
                var bridge=region.railways.Single(r=>r.id.StartsWith("railway-900-",StringComparison.Ordinal));var tram=region.railways.Single(r=>r.id.StartsWith("railway-100-",StringComparison.Ordinal));
                Check(bridge.bridge&&!bridge.tunnel&&bridge.layer==1&&bridge.tracks==2&&Mathf.Abs(bridge.gauge-1.435f)<.00001f&&bridge.width>=8,"conversion retains bridge topology, two tracks and millimetre gauge at metric scale");
                Check(bridge.operatorName=="Fixture Rail"&&bridge.name=="Fixture elevated line"&&bridge.electrified=="no"&&bridge.estimatedProfile,"operator/name/electrification preserved while vertical profile is explicitly estimated");
                Check(tram.kind=="tram"&&!tram.bridge&&region.roads.Any(r=>r.id=="100-0"),"shared highway/tram source enters both networks at grade");
                Check(region.roads.Where(r=>r.id=="102-0"||r.id=="106-0").All(r=>Mathf.Abs(r.maxVehicleHeight-4.0386f)<.00001f),"13 foot 3 inch road restriction survives conversion as metres rather than an absolute elevation");
                Check(region.railways.All(r=>r.nodeIds.Length==r.points.Length&&r.start==r.nodeIds[0]&&r.end==r.nodeIds.Last()),"source clipping/resampling preserves aligned canonical railway node IDs");
                DetailCanopyPacks.PrepareDrivingWidths(region);string roadsBefore=JsonUtility.ToJson(new RoadList{roads=region.roads});var report=RailwayProfileSolver.Prepare(region);
                Check(region.railwayProfilesPrepared&&report.BridgeConstraints>0&&report.LiftedNodes>0,"country-converted bridge receives finalized estimated clearance profile");
                Check(JsonUtility.ToJson(new RoadList{roads=region.roads})==roadsBefore,"rail profile preparation preserves the authoritative driving-road geometry");
                string canonical=WorldSnapshotCodec.CanonicalJson(region),epoch=WorldSnapshotCodec.Epoch(region);var decoded=WorldSnapshotCodec.Decode(canonical,WorldSnapshotCodec.NetworkId(region),epoch);
                Check(WorldSnapshotCodec.CanonicalJson(decoded)==canonical&&WorldSnapshotCodec.Epoch(decoded)==epoch,"country-derived prepared world wire round trip retains exact canonical content and epoch");
                Check(decoded.railways.Single(r=>r.id==bridge.id).points.SequenceEqual(bridge.points)&&decoded.railwayProfilesPrepared,"guest retains exact host railway profile instead of solving it again");
                Check(decoded.railwaySourceIdentity==after.RailwayIdentity&&decoded.railwaySourceComplete,"canonical wire round trip preserves exact railway source identity and completeness");
                RegionCache.Save(region,source);var oldRegion=GlobalRegions.Convert(place,oldRaw,new[]{100f,100f,100f,100f},2,1000);oldRegion.id=oldId;RegionCache.Save(oldRegion,oldRaw);
                Check(RegionCache.Exists(oldId)&&RegionCache.Exists(id)&&File.ReadAllText(RegionCache.SourcePath(oldId))==oldRaw,"road-only and rail-aware caches coexist without overwriting old source receipts");
                var saved=JObject.Parse(File.ReadAllText(RegionCache.SourcePath(id)));Check((string)saved["earthUnscriptedRailwaySource"]["identity"]==after.RailwayIdentity,"saved raw source record binds rail-aware cache to its exact railway source identity");
                File.WriteAllText(Path.Combine(root,"railway-country-results.json"),new JObject{{"result","PASS"},{"checks",cases.Count},{"cases",new JArray(cases)},{"sourceIdentity",after.RailwayIdentity},{"cacheId",id},{"wireEpoch",epoch},{"scope","Isolated SQLite/country conversion/profile/canonical wire fixtures; no installed country or real-world clearance claims"}}.ToString());return cases.Count;
            }
            finally{RegionCache.OverrideFolder=priorCache;PlayerProfile.StorageOverride=priorProfile;}
        }
        [Serializable] sealed class RoadList{public GeoRoad[] roads;}
        public static void Run()
        {try{string root=Path.GetFullPath("Builds/Verification/RailwayCountry-"+DateTime.UtcNow.Ticks);int checks=Verify(root,Debug.Log);Debug.Log("RAIL COUNTRY COMPLETE "+checks);EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    }
}
