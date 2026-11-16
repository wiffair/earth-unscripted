using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Security.Cryptography;using System.Text;using System.Threading;using Newtonsoft.Json;using Newtonsoft.Json.Linq;
namespace RoadRush
{
    public sealed class CaptureFilePin
    {
        public string Path{get;}public string Sha256{get;}public long Bytes{get;}
        public CaptureFilePin(string path,long bytes,string sha256){Path=System.IO.Path.GetFullPath(path);Bytes=bytes;Sha256=sha256;if(bytes<=0||bytes>32*1024*1024||sha256==null||sha256.Length!=64||sha256.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new ArgumentException("Bounded external file pin required");}
    }
    public sealed class CoherentSqliteBudgets
    {
        public int Rows{get;}public int References{get;}public long EncodedBytes{get;}
        public CoherentSqliteBudgets(int rows=12048,int references=300000,long encodedBytes=64*1024*1024){if(rows<1||rows>12048||references<1||references>300000||encodedBytes<1||encodedBytes>64*1024*1024)throw new ArgumentOutOfRangeException("Foundation source budgets may only be reduced");Rows=rows;References=references;EncodedBytes=encodedBytes;}
    }
    public sealed class CoherentSqliteCaptureRequest
    {
        [Serializable]sealed class BuildingBox{public GeoBuilding[] values;}
        internal readonly byte[] HeaderBytes,BuildingBytes,RegionalProvenance;internal readonly Dictionary<string,string> ExpectedMetadata;internal readonly CaptureFilePin[] Files;
        public string DatabasePath{get;}public CoherentSqliteBudgets Budgets{get;}public double TimeoutSeconds{get;}
        public CoherentSqliteCaptureRequest(string database,Raw13CaptureHeader header,IEnumerable<KeyValuePair<string,string>> expectedMetadata,CaptureFilePin absoluteGrid,IEnumerable<CaptureFilePin> terrainInputs,GeoBuilding[] completeRegionalBuildings,byte[] regionalProvenance,CoherentSqliteBudgets budgets=null,double timeoutSeconds=10)
        {
            if(header==null||absoluteGrid==null||completeRegionalBuildings==null||completeRegionalBuildings.Length>2500||regionalProvenance==null||regionalProvenance.Length>8*1024*1024||timeoutSeconds<=0||timeoutSeconds>60)throw new InvalidDataException("Complete bounded unverified capture request required");
            if(header.gridSide<2||header.gridSide>513||absoluteGrid.Bytes!=(long)header.gridSide*header.gridSide*4||header.size<1000||header.size>6000||double.IsNaN(header.latitude)||Math.Abs(header.latitude)>85||double.IsNaN(header.longitude)||Math.Abs(header.longitude)>180)throw new InvalidDataException("Request/grid bounds");
            long points=0,roof=0;var buildingIds=new HashSet<string>(StringComparer.Ordinal);foreach(var b in completeRegionalBuildings){if(b==null||string.IsNullOrEmpty(b.id)||b.id.Length>256||!buildingIds.Add(b.id)||b.points==null||b.points.Length<3||b.points.Length>4096)throw new InvalidDataException("Complete regional geometry/identity budget");points+=b.points.Length;roof+=(long)b.points.Length*b.points.Length;if(points>400000||roof>20000000)throw new InvalidDataException("Complete regional point/roof budget");}
            HeaderBytes=CanonicalGeography13.WriteBytes(header);BuildingBytes=CanonicalGeography13.WriteBytes(new BuildingBox{values=completeRegionalBuildings});RegionalProvenance=(byte[])regionalProvenance.Clone();DatabasePath=Path.GetFullPath(database);Budgets=budgets??new CoherentSqliteBudgets();TimeoutSeconds=timeoutSeconds;ExpectedMetadata=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var pin in expectedMetadata??throw new ArgumentNullException(nameof(expectedMetadata))){if(!CoherentRaw13SqliteCapture.MetadataKeys.Contains(pin.Key)||pin.Value==null||pin.Value.Length>2048||ExpectedMetadata.ContainsKey(pin.Key))throw new InvalidDataException("External selected metadata pins invalid");ExpectedMetadata.Add(pin.Key,pin.Value);}
            foreach(string key in new[]{"osmosis_replication_sequence_number","osmosis_replication_timestamp","railway_source_sha256"})if(!ExpectedMetadata.ContainsKey(key))throw new InvalidDataException("Original sequence/timestamp/PBF pins required externally");
            string pbf=ExpectedMetadata["railway_source_sha256"];if(pbf.Length!=64||pbf.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new InvalidDataException("Selected original PBF SHA256 pin required");
            var inputs=new List<CaptureFilePin>{absoluteGrid};if(terrainInputs!=null)foreach(var pin in terrainInputs){if(pin==null||inputs.Count>=9||inputs.Any(p=>p.Path==pin.Path))throw new InvalidDataException("Terrain input pin count/duplicates");inputs.Add(pin);}Files=inputs.ToArray();
        }
        internal Raw13CaptureHeader Header()=>CanonicalGeography13.ReadBytes<Raw13CaptureHeader>(HeaderBytes);
        internal GeoBuilding[] Buildings()=>CanonicalGeography13.ReadBytes<BuildingBox>(BuildingBytes).values;
    }
    [Serializable]public sealed class CoherentSqliteReceipt
    {
        public string format="RoadRushCoherentRawReadInspection1",scope="Unverified copied original supported-way source snapshot; NOT provider authentication, prepared admission or commit authority",canonicalMode=CanonicalGeography13.Mode;
        public string selectedRowIdentityRecipe="sqlite-selected-rows-le-v1",selectedReferenceIdentityRecipe="sqlite-selected-ref-e7-le-v1",originalTagScope="Stored supported feature tags may be filtered; rail-table tags preserved as stored; not a complete PBF/tag/POI/relation source";
        public string metadataSha256,selectedRowsSha256,selectedReferencesSha256,gridSha256,terrainPinsSha256;[NonSerialized]public string payloadInputDigest;
        public int selectedRows,uniqueWays,rawReferences,uniqueWayReferences,highways,waters,baselineBuildings,areas,railways,zeroInWindowNodeRows,totalChanges,queryOnly;public long encodedBytes;
        public bool mainDatabaseReadOnly;public string walAuxiliarySemantics="READONLY main/query_only/noDML; ordinary WAL reader shared-memory locking/readmarks may change auxiliaries";
        public string[] metadata,terrainPins;
    }
    public sealed class UnverifiedCoherentRaw13Read
    {
        public Raw13CapturePayload PayloadForInspection{get;}public CoherentSqliteReceipt Receipt{get;}public bool SourceAuthority=>false;
        internal UnverifiedCoherentRaw13Read(Raw13CapturePayload payload,CoherentSqliteReceipt receipt){PayloadForInspection=payload;Receipt=receipt;}
    }
    public static class CoherentRaw13SqliteCapture
    {
        internal static readonly string[] MetadataKeys={"osmosis_replication_base_url","osmosis_replication_sequence_number","osmosis_replication_timestamp","railway_schema","railway_complete","railway_source_sha256","transport_schema","transport_source_sha256","transport_content_sha256"};
        public enum Phase{SnapshotMetadata,GridRead,RowsPreflighted,RowsCopied,FilesRevalidated,BeforeReturn}
        sealed class Row{public string Table;public long Id;public int Kind,Count,TagBytes,GeometryBytes;}
        static void Need(bool value,string why){if(!value)throw new InvalidDataException("Coherent raw source: "+why);}
        static void Check(CancellationToken token,Func<bool> current){token.ThrowIfCancellationRequested();if(current!=null&&!current())throw new OperationCanceledException("Source selection/generation changed");}
        static void Notify(Phase phase,CancellationToken token,Func<bool> current,Action<Phase> observe){Check(token,current);observe?.Invoke(phase);Check(token,current);}
        static Dictionary<string,string> Metadata(ReadOnlyCaptureSqlite db)
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);using(var q=db.Select("SELECT key,value FROM metadata WHERE key IN ('osmosis_replication_base_url','osmosis_replication_sequence_number','osmosis_replication_timestamp','railway_schema','railway_complete','railway_source_sha256','transport_schema','transport_source_sha256','transport_content_sha256') ORDER BY key"))while(q.Step()){string key=q.Text(0,128),value=q.Text(1,2048);Need(result.TryAdd(key,value),"duplicate metadata key");}return result;
        }
        static string[] MetadataLines(Dictionary<string,string> m)=>m.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value).ToArray();
        static byte[] FileBytes(CaptureFilePin pin,CancellationToken token,Action heartbeat)
        {
            for(var d=new DirectoryInfo(Path.GetDirectoryName(pin.Path));d!=null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked capture input ancestor");Need(File.Exists(pin.Path)&&(File.GetAttributes(pin.Path)&FileAttributes.ReparsePoint)==0,"missing/linked pinned terrain input");
            using(var f=new FileStream(pin.Path,FileMode.Open,FileAccess.Read,FileShare.Read)){Need(f.Length==pin.Bytes,"terrain input length changed");byte[] bytes=new byte[(int)pin.Bytes];int at=0;while(at<bytes.Length){token.ThrowIfCancellationRequested();heartbeat();int n=f.Read(bytes,at,Math.Min(65536,bytes.Length-at));Need(n>0,"truncated terrain input");at+=n;}Need(f.ReadByte()==-1&&World13DocumentCodec.Hash(bytes)==pin.Sha256,"terrain input content changed");return bytes;}
        }
        [Serializable]sealed class Lines{public string[] values;}
        static string HashLines(IEnumerable<string> values)=>World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(new Lines{values=values.ToArray()}));
        static byte[] LE(int value){byte[] b=BitConverter.GetBytes(value);if(!BitConverter.IsLittleEndian)Array.Reverse(b);return b;}
        static byte[] LE(long value){byte[] b=BitConverter.GetBytes(value);if(!BitConverter.IsLittleEndian)Array.Reverse(b);return b;}
        static void HashPart(IncrementalHash hash,byte[] bytes){hash.AppendData(LE(bytes.Length));hash.AppendData(bytes);}
        static string Finish(IncrementalHash hash)=>BitConverter.ToString(hash.GetHashAndReset()).Replace("-","").ToLowerInvariant();
        public static UnverifiedCoherentRaw13Read Capture(CoherentSqliteCaptureRequest request,CancellationToken token,Func<bool> stillCurrent=null,Action<Phase> observe=null)
        {
            if(request==null)throw new ArgumentNullException(nameof(request));Check(token,stillCurrent);var header=request.Header();
            double latHalf=header.size*.5/111319.4908,lonHalf=latHalf/Math.Cos(header.latitude*Math.PI/180),west=header.longitude-lonHalf,east=header.longitude+lonHalf,south=header.latitude-latHalf,north=header.latitude+latHalf;
            Need(west>=-180&&east<=180&&south>=-90&&north<=90,"antimeridian/polar requests need a separate reviewed split policy");
            var receipt=new CoherentSqliteReceipt();using(var db=new ReadOnlyCaptureSqlite(request.DatabasePath,token,request.TimeoutSeconds))
            {
                db.BeginSnapshot();try
                {
                    var metadata=Metadata(db);foreach(var pin in request.ExpectedMetadata)Need(metadata.TryGetValue(pin.Key,out string value)&&value==pin.Value,"metadata differs from external selected source pin: "+pin.Key);
                    Need(header.datasetVersion!=null&&header.datasetVersion.EndsWith("/"+metadata["osmosis_replication_sequence_number"],StringComparison.Ordinal),"header source version differs from captured sequence");if(metadata.TryGetValue("osmosis_replication_base_url",out string baseUrl))header.datasetSource=baseUrl;
                    receipt.metadata=MetadataLines(metadata);receipt.metadataSha256=HashLines(receipt.metadata);Notify(Phase.SnapshotMetadata,token,stillCurrent,observe);
                    byte[] grid=FileBytes(request.Files[0],token,db.Check);foreach(var pin in request.Files.Skip(1))FileBytes(pin,token,db.Check);receipt.gridSha256=request.Files[0].Sha256;receipt.terrainPins=request.Files.Select(p=>p.Bytes+":"+p.Sha256).ToArray();receipt.terrainPinsSha256=HashLines(receipt.terrainPins);Notify(Phase.GridRead,token,stillCurrent,observe);
                    var tables=new HashSet<string>();using(var q=db.Select("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('features','bounds','railways','rail_bounds')"))while(q.Step())tables.Add(q.Text(0,64));Need(tables.Contains("features")&&tables.Contains("bounds"),"supported feature tables missing");Need(tables.Contains("railways")==tables.Contains("rail_bounds"),"partial rail schema");
                    bool rails=tables.Contains("railways");if(rails)Need(metadata.TryGetValue("railway_schema",out string railSchema)&&railSchema=="1"&&metadata.TryGetValue("railway_complete",out string railComplete)&&(railComplete=="0"||railComplete=="1"),"rail schema/completeness metadata");if(metadata.TryGetValue("railway_complete",out string complete)&&complete=="1")Need(rails,"complete railway metadata without rail tables");
                    var rows=new List<Row>();foreach(bool rail in rails?new[]{false,true}:new[]{false})
                    {
                        string table=rail?"railways":"features",bounds=rail?"rail_bounds":"bounds",kind=rail?"4":"f.kind";
                        string sql="SELECT f.id,"+kind+",length(CAST(f.tags AS BLOB)),length(f.geometry),substr(f.geometry,1,4) FROM "+bounds+" b JOIN "+table+" f ON f.id=b.id WHERE b.minLon<=?1 AND b.maxLon>=?2 AND b.minLat<=?3 AND b.maxLat>=?4 ORDER BY f.id";
                        using(var q=db.Select(sql)){q.Number(1,east).Number(2,west).Number(3,north).Number(4,south);while(q.Step())
                        {
                            Check(token,stillCurrent);Need(rows.Count<request.Budgets.Rows,"complete row budget exceeded; no LIMIT/thinning");int tagBytes=checked((int)q.Integer(2)),geometryBytes=checked((int)q.Integer(3));byte[] count=q.Blob(4,4);Need(count.Length==4,"original BLOB header missing");int n=count[0]|count[1]<<8|count[2]<<16|count[3]<<24;Need(n>=2&&n<=50000&&geometryBytes==4+n*16&&tagBytes>=2&&tagBytes<=1000000,"original row geometry/tag budget");receipt.rawReferences+=n;receipt.encodedBytes+=(long)tagBytes+geometryBytes;Need(receipt.rawReferences<=request.Budgets.References&&receipt.encodedBytes<=request.Budgets.EncodedBytes,"complete reference/byte budget exceeded; no partial capture");long sourceId=q.Integer(0);int sourceKind=checked((int)q.Integer(1));Need(sourceId>0&&(rail?sourceKind==4:sourceKind>=0&&sourceKind<=3),"unsupported original source identity/class");rows.Add(new Row{Table=table,Id=sourceId,Kind=sourceKind,Count=n,TagBytes=tagBytes,GeometryBytes=geometryBytes});
                        }}
                    }
                    receipt.selectedRows=rows.Count;Notify(Phase.RowsPreflighted,token,stillCurrent,observe);var originals=new Dictionary<long,OriginalRaw13Way>();
                    using(var rowHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))using(var refHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        rowHash.AppendData(Encoding.UTF8.GetBytes("RoadRush/sqlite-selected-rows/1\n"));refHash.AppendData(Encoding.UTF8.GetBytes("RoadRush/sqlite-selected-references/1\n"));
                        foreach(var row in rows)
                        {
                            Check(token,stillCurrent);string tagsText;byte[] geometry;using(var q=db.Select("SELECT tags,geometry FROM "+row.Table+" WHERE id=?1")){q.Integer(1,row.Id);Need(q.Step(),"selected source row vanished");tagsText=q.Text(0,row.TagBytes);geometry=q.Blob(1,row.GeometryBytes);Need(!q.Step(),"duplicate original row");}
                            Need(geometry.Length==row.GeometryBytes&&Encoding.UTF8.GetByteCount(tagsText)==row.TagBytes,"selected source row size changed inside snapshot");
                            JObject tags;using(var reader=new JsonTextReader(new StringReader(tagsText)){MaxDepth=4,DateParseHandling=DateParseHandling.None})tags=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Need(tags.Count<=128&&tags.Properties().All(p=>p.Name.Length<=96&&p.Value.Type==JTokenType.String&&((string)p.Value).Length<=2048),"complete original tag bounds");
                            var nodes=new OriginalRaw13Node[row.Count];bool inWindow=false;using(var reader=new BinaryReader(new MemoryStream(geometry))){Need(reader.ReadInt32()==row.Count,"source header changed");for(int i=0;i<nodes.Length;i++){if((i&255)==0){Check(token,stillCurrent);db.Check();}long id=reader.ReadInt64();int lat=reader.ReadInt32(),lon=reader.ReadInt32();nodes[i]=new OriginalRaw13Node{id=id,latitudeE7=lat,longitudeE7=lon};inWindow|=lon/1e7>=west&&lon/1e7<=east&&lat/1e7>=south&&lat/1e7<=north;}}
                            if(!inWindow)receipt.zeroInWindowNodeRows++;HashPart(rowHash,Encoding.UTF8.GetBytes(row.Table));HashPart(rowHash,LE(row.Id));HashPart(rowHash,LE(row.Kind));HashPart(rowHash,Encoding.UTF8.GetBytes(tagsText));HashPart(rowHash,geometry);HashPart(refHash,Encoding.UTF8.GetBytes(row.Table));HashPart(refHash,LE(row.Id));HashPart(refHash,geometry);
                            var copiedTags=tags.Properties().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>new OriginalRaw13Tag{key=p.Name,value=(string)p.Value}).ToArray();
                            if(originals.TryGetValue(row.Id,out var prior))
                            {Need(prior.nodes.Length==nodes.Length&&prior.nodes.Select((p,i)=>p.id==nodes[i].id&&p.latitudeE7==nodes[i].latitudeE7&&p.longitudeE7==nodes[i].longitudeE7).All(v=>v),"dual feature/rail original geometry conflicts");var merged=prior.tags.ToDictionary(t=>t.key,t=>t.value,StringComparer.Ordinal);foreach(var tag in copiedTags){if(merged.TryGetValue(tag.key,out string old))Need(old==tag.value,"dual source tag conflict");else merged.Add(tag.key,tag.value);}prior.tags=merged.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new OriginalRaw13Tag{key=p.Key,value=p.Value}).ToArray();}
                            else originals.Add(row.Id,new OriginalRaw13Way{id=row.Id,nodes=nodes,tags=copiedTags});
                        }
                        receipt.selectedRowsSha256=Finish(rowHash);receipt.selectedReferencesSha256=Finish(refHash);
                    }
                    receipt.uniqueWays=originals.Count;receipt.uniqueWayReferences=originals.Values.Sum(w=>w.nodes.Length);Notify(Phase.RowsCopied,token,stillCurrent,observe);
                    Need(MetadataLines(Metadata(db)).SequenceEqual(receipt.metadata),"metadata changed within the same SQLite snapshot");foreach(var pin in request.Files)FileBytes(pin,token,db.Check);Notify(Phase.FilesRevalidated,token,stillCurrent,observe);
                    var ways=originals.Values.OrderBy(w=>w.id).ToArray();bool Has(OriginalRaw13Way w,string key)=>w.tags.Any(t=>t.key==key);
                    header.expectedElements=ways.Length;header.expectedHighways=ways.Count(w=>Has(w,"highway"));header.expectedWaters=ways.Count(w=>!Has(w,"highway")&&Has(w,"waterway"));header.expectedBaselineBuildings=ways.Count(w=>!Has(w,"highway")&&!Has(w,"waterway")&&Has(w,"building"));header.expectedAreas=ways.Count(w=>!Has(w,"highway")&&!Has(w,"waterway")&&!Has(w,"building")&&(Has(w,"landuse")||Has(w,"natural")));header.expectedRegionalBuildings=request.Buildings().Length;
                    receipt.highways=header.expectedHighways;receipt.waters=header.expectedWaters;receipt.baselineBuildings=header.expectedBaselineBuildings;receipt.areas=header.expectedAreas;receipt.railways=ways.Count(w=>w.tags.Any(t=>t.key=="railway"||t.key.EndsWith(":railway",StringComparison.Ordinal)));
                    metadata.TryGetValue("railway_source_sha256",out string pbf);metadata.TryGetValue("transport_content_sha256",out string transport);header.sourcePins=new Raw13OriginalSourcePins{sequence=metadata["osmosis_replication_sequence_number"],timestamp=metadata["osmosis_replication_timestamp"],originalPbfSha256=pbf,transportContentSha256=transport,terrainTileSha256=request.Files.Length>1?request.Files[1].Sha256:null,sourceRowsSha256=receipt.selectedRowsSha256};header.railwaysComplete=metadata.TryGetValue("railway_complete",out complete)&&complete=="1";header.railwayIdentity=rails?"railways1|"+(header.railwaysComplete?"complete":"partial")+"|"+pbf+(transport==null?"":"|transport1|"+transport):"railways-none";
                    receipt.mainDatabaseReadOnly=db.MainReadOnly;receipt.totalChanges=db.TotalChanges;using(var q=db.Select("PRAGMA query_only")){Need(q.Step(),"query_only diagnostic unavailable");receipt.queryOnly=checked((int)q.Integer(0));}Need(receipt.mainDatabaseReadOnly&&receipt.totalChanges==0&&receipt.queryOnly==1,"source connection violated read-only invariant");
                    byte[] evidence=CanonicalGeography13.WriteBytes(receipt);var payload=Raw13CoherentReadApi.CopyForInspection(header,ways,grid,request.Buildings(),request.RegionalProvenance,evidence);receipt.payloadInputDigest=payload.InputDigest;db.Check();Notify(Phase.BeforeReturn,token,stillCurrent,observe);db.Check();return new UnverifiedCoherentRaw13Read(payload,receipt);
                }
                finally{db.EndSnapshot();}
            }
        }
    }
}
