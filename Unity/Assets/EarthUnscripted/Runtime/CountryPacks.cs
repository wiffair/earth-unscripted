using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed class CountryPack
    {
        public readonly string Folder;public readonly JObject Metadata;readonly Dictionary<string,HeightTile> heights=new Dictionary<string,HeightTile>();
        public CountryPack(string folder)
        {
            Folder=folder;Metadata=JObject.Parse(File.ReadAllText(Path.Combine(folder,"pack.json")));
            // SQLite commits are authoritative. Recover if the process ended
            // between a completed data transaction and its pack.json mirror.
            using(var db=new NativeMapDatabase(Path.Combine(folder,"map.sqlite")))
            {
                db.Execute("BEGIN");string sequence=MapReplication.Sequence(db);
                if(!string.IsNullOrEmpty(sequence))
                {
                    Metadata["version"]=sequence;
                    if(!(Metadata["replication"] is JObject))Metadata["replication"]=new JObject();
                    Metadata["replication"]["osmosis_replication_sequence_number"]=sequence;
                    using(var q=db.Prepare("SELECT key,value FROM metadata WHERE key IN ('osmosis_replication_timestamp','earth_unscripted_last_diff_sha256')"))while(q.Step())
                    {if(q.Text(0)=="osmosis_replication_timestamp")Metadata["replication"][q.Text(0)]=q.Text(1);else{Metadata["lastDiffSHA256"]=q.Text(1);if(Metadata["baseDatabaseSHA256"]==null)Metadata["baseDatabaseSHA256"]=Metadata["databaseSHA256"];Metadata.Remove("databaseSHA256");}}
                }
                var railway=ReadRailwayState(db);HasRailways=railway.Available;RailwaysComplete=railway.Complete;RailwayIdentity=railway.Identity;
            }
        }
        public bool HasRailways {get;private set;}
        public bool RailwaysComplete {get;private set;}
        public string RailwayIdentity {get;private set;}="railways-none";
        sealed class RailwayState{public bool Available,Complete;public string Identity="railways-none";}
        static RailwayState ReadRailwayState(NativeMapDatabase db)
        {
            var values=new Dictionary<string,string>();using(var q=db.Prepare("SELECT key,value FROM metadata WHERE key IN ('railway_schema','railway_complete','railway_source_sha256','transport_schema','transport_source_sha256','transport_content_sha256')"))while(q.Step())values[q.Text(0)]=q.Text(1);
            if(!values.TryGetValue("railway_schema",out string schema)){if(values.Keys.Any(k=>k.StartsWith("transport_",StringComparison.Ordinal)))throw new InvalidDataException("Transport metadata requires its railway baseline");return new RailwayState();}
            if(schema!="1")throw new InvalidDataException("Unsupported country railway schema");
            int tables=0;using(var q=db.Prepare("SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('railways','rail_bounds')")){q.Step();tables=(int)q.Integer(0);}
            if(!values.TryGetValue("railway_complete",out string ready)||(ready!="0"&&ready!="1"))throw new InvalidDataException("Invalid country railway completeness marker");
            bool complete=ready=="1";if(tables!=2)throw new InvalidDataException("Country railway capability is missing its tables");
            values.TryGetValue("railway_source_sha256",out string source);if(complete&&!System.Text.RegularExpressions.Regex.IsMatch(source??"","^[a-f0-9]{64}$"))throw new InvalidDataException("Complete railway source identity missing");
            // The bridge profile depends on road layers/restrictions as well as railway
            // alignment. Include same-sequence transport backfills in its provenance
            // so prepared areas cannot silently reuse an older clearance profile.
            string transport="";
            bool hasTransport=values.Keys.Any(k=>k.StartsWith("transport_",StringComparison.Ordinal));
            if(hasTransport)
            {
                if(!complete)throw new InvalidDataException("Transport metadata requires a complete railway source baseline");
                if(!values.TryGetValue("transport_schema",out string transportSchema)||transportSchema!="1")throw new InvalidDataException("Unsupported country transport metadata schema");
                if(!values.TryGetValue("transport_source_sha256",out string transportSource)||transportSource!=source)throw new InvalidDataException("Transport metadata source differs from railway baseline");
                if(!values.TryGetValue("transport_content_sha256",out string content)||!System.Text.RegularExpressions.Regex.IsMatch(content,"^[a-f0-9]{64}$"))throw new InvalidDataException("Transport metadata content identity missing");
                transport="|transport1|"+content;
            }
            return new RailwayState{Available=true,Complete=complete,Identity="railways1|"+(complete?"complete":"partial")+"|"+(source??"unrecorded")+transport};
        }
        public string ReplicationSource
        {
            get
            {
                string source=(string)Metadata["replication"]?["osmosis_replication_base_url"];
                if(!System.Text.RegularExpressions.Regex.IsMatch(source??"",@"^https://download\.geofabrik\.de/(?:[a-z0-9_-]+/)*[a-z0-9_-]+-updates/?$"))throw new InvalidDataException("Country has no supported HTTPS replication source");return source.TrimEnd('/');
            }
        }
        public string Id
        {
            get
            {
                try{return "geofabrik-"+ReplicationSource.Substring("https://download.geofabrik.de/".Length).Replace("/","-").Replace("-updates","");}
                catch(InvalidDataException){return System.Text.RegularExpressions.Regex.Replace(((string)Metadata["id"]??Name??"").ToLowerInvariant(),"[^a-z0-9_-]","-");}
            }
        }
        public string Name=>(string)Metadata["name"];public string Version=>(string)Metadata["version"];public string Country=>(string)Metadata["country"];public bool Ready=>(bool?)Metadata["mapReady"]==true&&(bool?)Metadata["terrainReady"]==true;
        public bool Covers(WorldPlace place)
        {
            if(!Ready||!string.Equals(Country,place.country_code,StringComparison.OrdinalIgnoreCase))return false;var b=(JArray)Metadata["bounds"];if(place.longitude<(double)b[0]||place.latitude<(double)b[1]||place.longitude>(double)b[2]||place.latitude>(double)b[3])return false;
            var rings=Metadata["coverage"] as JArray;if(rings==null)return true;bool inside=false;
            foreach(var ring in rings){var points=ring["points"] as JArray;bool contains=false;for(int i=0,j=points.Count-1;i<points.Count;j=i++){double ax=(double)points[i]["lon"],ay=(double)points[i]["lat"],bx=(double)points[j]["lon"],by=(double)points[j]["lat"];if((ay>place.latitude)!=(by>place.latitude)&&place.longitude<(bx-ax)*(place.latitude-ay)/(by-ay)+ax)contains=!contains;}if(contains){if((bool)ring["hole"])return false;inside=true;}}
            return inside;
        }
        public string Extract(WorldPlace place)
        {
            double size=Math.Max(1000,Math.Min(6000,place.size<=0?3000:place.size)),cos=Math.Cos(place.latitude*Math.PI/180);double dy=size*.5/111319.4908,dx=dy/cos;
            var elements=new JArray();var existing=new Dictionary<long,JObject>();int points=0,railPoints=0,railCount=0;
            using(var db=new NativeMapDatabase(Path.Combine(Folder,"map.sqlite")))
            {
                db.Execute("BEGIN");if(MapReplication.Sequence(db)!=Version||ReadRailwayState(db).Identity!=RailwayIdentity)throw new InvalidOperationException("Country cache advanced; prepare this area again");
                using(var rows=db.Prepare("SELECT f.id,f.tags,f.geometry FROM bounds b JOIN features f ON f.id=b.id WHERE b.minLon<=?1 AND b.maxLon>=?2 AND b.minLat<=?3 AND b.maxLat>=?4 ORDER BY f.kind,((b.minLon+b.maxLon)*.5-?5)*((b.minLon+b.maxLon)*.5-?5)+((b.minLat+b.maxLat)*.5-?6)*((b.minLat+b.maxLat)*.5-?6) LIMIT 10000"))
                {
                    rows.Number(1,place.longitude+dx).Number(2,place.longitude-dx).Number(3,place.latitude+dy).Number(4,place.latitude-dy).Number(5,place.longitude).Number(6,place.latitude);
                    while(rows.Step())
                    {
                        byte[] blob=rows.Bytes(2);int count=GeometryCount(blob);if(points+count>300000)continue;points+=count;
                        long id=rows.Integer(0);var element=Element(id,rows.Text(1),blob);elements.Add(element);existing[id]=element;
                    }
                }
                if(HasRailways)using(var rows=db.Prepare("SELECT r.id,r.tags,r.geometry FROM rail_bounds b JOIN railways r ON r.id=b.id WHERE b.minLon<=?1 AND b.maxLon>=?2 AND b.minLat<=?3 AND b.maxLat>=?4 ORDER BY r.id LIMIT 2049"))
                {
                    rows.Number(1,place.longitude+dx).Number(2,place.longitude-dx).Number(3,place.latitude+dy).Number(4,place.latitude-dy);
                    while(rows.Step())
                    {
                        if(++railCount>RailwayMetrics.MaximumWays)throw new InvalidDataException("Railway source density exceeds the area budget; prepare a smaller area");
                        var blob=rows.Bytes(2);railPoints+=GeometryCount(blob);if(railPoints>200000)throw new InvalidDataException("Railway source geometry exceeds the area budget; prepare a smaller area");
                        long id=rows.Integer(0);var rail=Element(id,rows.Text(1),blob);
                        if(existing.TryGetValue(id,out var element))
                        {
                            if(!JToken.DeepEquals(element["nodes"],rail["nodes"])||!JToken.DeepEquals(element["geometry"],rail["geometry"]))throw new InvalidDataException("Shared road/rail source geometry conflicts; current world retained");
                            var merged=(JObject)element["tags"];foreach(var tag in (JObject)rail["tags"])
                            {if(merged.TryGetValue(tag.Key,out var prior)&&!JToken.DeepEquals(prior,tag.Value))throw new InvalidDataException("Shared road/rail source attributes conflict; current world retained");merged[tag.Key]=tag.Value.DeepClone();}
                        }
                        else{elements.Add(rail);existing[id]=rail;}
                    }
                }
            }
            return new JObject{{"elements",elements},{"earthUnscriptedRailwaySource",new JObject{{"identity",RailwayIdentity},{"complete",RailwaysComplete}}}}.ToString(Newtonsoft.Json.Formatting.None);
        }
        static int GeometryCount(byte[] blob)
        {
            if(blob==null||blob.Length<4)throw new InvalidDataException("Invalid country geometry");
            using(var reader=new BinaryReader(new MemoryStream(blob))){int count=reader.ReadInt32();if(count<2||count>50000||blob.Length!=4+count*16)throw new InvalidDataException("Invalid country geometry");return count;}
        }
        static JObject Element(long id,string tags,byte[] blob)
        {
            int count=GeometryCount(blob);var nodes=new JArray();var geometry=new JArray();
            using(var reader=new BinaryReader(new MemoryStream(blob)))
            {
                reader.ReadInt32();for(int i=0;i<count;i++){nodes.Add(reader.ReadInt64());double latitude=reader.ReadInt32()/1e7,longitude=reader.ReadInt32()/1e7;geometry.Add(new JObject{{"lat",latitude},{"lon",longitude}});}
            }
            return new JObject{{"type","way"},{"id",id},{"tags",JObject.Parse(tags)},{"nodes",nodes},{"geometry",geometry}};
        }
        public float[] Grid(WorldPlace place,int n)
        {
            float size=Mathf.Clamp(place.size<=0?3000:place.size,1000,6000);double cos=Math.Cos(place.latitude*Math.PI/180);var data=new float[n*n];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)data[z*n+x]=Elevation(place.latitude+(-size*.5+z*size/(n-1))/111319.4908,place.longitude+(-size*.5+x*size/(n-1))/(111319.4908*cos));return data;
        }
        float Elevation(double lat,double lon)
        {
            int south=(int)Math.Floor(lat),west=(int)Math.Floor(lon);string key=south+"_"+west;
            if(!heights.TryGetValue(key,out var tile))
            {
                string path=Path.Combine(Folder,"terrain",key+".euh");if(!File.Exists(path))
                {bool expected=((JArray)Metadata["tiles"]).Any(t=>(int)t[0]==south&&(int)t[1]==west);if(expected)throw new InvalidOperationException("Country terrain is still preparing");return 0;}
                tile=new HeightTile(path);heights[key]=tile;
            }
            return tile.Sample(lat,lon);
        }
        sealed class HeightTile
        {
            readonly int nx,nz;readonly double west,north,dx,dy;readonly short[] data;
            public HeightTile(string path)
            {
                using(var reader=new BinaryReader(File.OpenRead(path))){if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4))!="EUH1")throw new InvalidDataException("Unknown terrain format");nx=reader.ReadInt32();nz=reader.ReadInt32();if(nx<2||nz<2||nx>5000||nz>5000||reader.BaseStream.Length!=44L+2L*nx*nz)throw new InvalidDataException("Oversized terrain");west=reader.ReadDouble();north=reader.ReadDouble();dx=reader.ReadDouble();dy=reader.ReadDouble();if(dx<=0||dy>=0)throw new InvalidDataException("Invalid terrain transform");data=new short[nx*nz];for(int i=0;i<data.Length;i++)data[i]=reader.ReadInt16();}
            }
            public float Sample(double lat,double lon)
            {
                double gx=Math.Max(0,Math.Min(nx-1.001,(lon-west)/dx-.5)),gz=Math.Max(0,Math.Min(nz-1.001,(lat-north)/dy-.5));int x=(int)gx,z=(int)gz;float tx=(float)(gx-x),tz=(float)(gz-z);float At(int i)=>data[i]< -32000?0:data[i];return Mathf.Lerp(Mathf.Lerp(At(z*nx+x),At(z*nx+x+1),tx),Mathf.Lerp(At((z+1)*nx+x),At((z+1)*nx+x+1),tx),tz);
            }
        }
    }
    public static class CountryPacks
    {
        public static string ExtraRoot;
        public static IEnumerable<CountryPack> Installed()
        {
            var roots=new List<string>();if(!string.IsNullOrEmpty(ExtraRoot))roots.Add(ExtraRoot);roots.Add(Path.Combine(EarthUnscripted.SinglePlayerBeta.ProductRoot,"WorldPacks"));roots.Add(Path.Combine(Path.GetDirectoryName(Application.dataPath),"WorldPacks"));
            if(Application.platform==RuntimePlatform.OSXPlayer)roots.Add(Path.GetFullPath(Path.Combine(Application.dataPath,"../..","WorldPacks")));
            string config=Path.Combine(RegionCache.Folder,"world-packs.txt");if(File.Exists(config))roots.AddRange(File.ReadAllLines(config).Where(p=>!string.IsNullOrWhiteSpace(p)));
            var found=new List<CountryPack>();foreach(var root in roots.Distinct())
            {
                if(!Directory.Exists(root))continue;foreach(var folder in Directory.GetDirectories(root))try{if(File.Exists(Path.Combine(folder,"pack.json"))&&File.Exists(Path.Combine(folder,"map.sqlite")))found.Add(new CountryPack(folder));}catch(Exception){}
            }
            return found.OrderByDescending(p=>p.Ready).ThenByDescending(p=>(bool?)p.Metadata["completeNodes"]==true).ThenByDescending(p=>long.TryParse(p.Version,out long version)?version:0);
        }
        public static CountryPack Find(WorldPlace place)=>Installed().FirstOrDefault(p=>p.Covers(place));
    }
}
