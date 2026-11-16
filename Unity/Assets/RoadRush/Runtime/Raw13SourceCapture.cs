using System;using System.IO;using System.Linq;using System.Text;using System.Threading;using UnityEngine;using Newtonsoft.Json.Linq;using Newtonsoft.Json;
namespace RoadRush
{
    [Serializable]public sealed class Raw13OriginalSourcePins
    {public string sequence,timestamp,originalPbfSha256,transportContentSha256,terrainTileSha256,sourceRowsSha256;}
    [Serializable]public sealed class Raw13CaptureHeader
    {
        public string name,countryCode,timeZone,datasetVersion,datasetSource,attribution,railwayIdentity;
        public Raw13OriginalSourcePins sourcePins;public double latitude,longitude;public float size;public int gridSide,drivingSide,expectedElements,expectedHighways,expectedWaters,expectedAreas,expectedBaselineBuildings,expectedRegionalBuildings;public bool railwaysComplete;
    }
    // Copied data from a reader is not by itself evidence that a coherent read
    // occurred. This public payload grants inspection/conversion only, never trust.
    public sealed class Raw13CapturePayload
    {
        internal readonly byte[] HeaderBytes,ProviderBytes,GridBytes,RegionalBuildingsBytes,RegionalProvenanceBytes,SourceEvidenceBytes;
        public Raw13CapturePayload(Raw13CaptureHeader header,byte[] provider,byte[] absoluteGrid,GeoBuilding[] completeRegionalBuildings,byte[] regionalProvenance,byte[] sourceEvidence)
        {
            if(header==null||provider==null||absoluteGrid==null||completeRegionalBuildings==null||regionalProvenance==null||sourceEvidence==null)throw new ArgumentNullException("Complete copied raw capture required");
            if(provider.Length>64*1024*1024||absoluteGrid.Length>513*513*4||regionalProvenance.Length>8*1024*1024||completeRegionalBuildings.Length>2500||sourceEvidence.Length>65536)throw new InvalidDataException("Raw capture exceeds foundation budgets; no thinning");
            foreach(string value in new[]{header.name,header.countryCode,header.timeZone,header.datasetVersion,header.datasetSource,header.attribution,header.railwayIdentity})if(value!=null&&value.Length>2048)throw new InvalidDataException("Raw capture header text budget");
            if(header.sourcePins!=null)foreach(string value in new[]{header.sourcePins.sequence,header.sourcePins.timestamp,header.sourcePins.originalPbfSha256,header.sourcePins.transportContentSha256,header.sourcePins.terrainTileSha256,header.sourcePins.sourceRowsSha256})if(value!=null&&value.Length>2048)throw new InvalidDataException("Original source pin text budget");
            long total=0,roof=0;var ids=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);foreach(var building in completeRegionalBuildings){if(building==null||string.IsNullOrEmpty(building.id)||building.id.Length>256||!ids.Add(building.id)||building.points==null||building.points.Length<3||building.points.Length>4096)throw new InvalidDataException("Complete regional building identity/geometry budget");total+=building.points.Length;roof+=(long)building.points.Length*building.points.Length;if(total>400000||roof>20000000)throw new InvalidDataException("Complete regional building/roof budget; no thinning");}
            HeaderBytes=Encoding.UTF8.GetBytes(CanonicalGeography13.Write(header));ProviderBytes=(byte[])provider.Clone();GridBytes=(byte[])absoluteGrid.Clone();RegionalBuildingsBytes=Encoding.UTF8.GetBytes(CanonicalGeography13.Write(new Buildings{values=completeRegionalBuildings}));RegionalProvenanceBytes=(byte[])regionalProvenance.Clone();SourceEvidenceBytes=(byte[])sourceEvidence.Clone();
        }
        [Serializable]internal sealed class Buildings{public GeoBuilding[] values;}
        internal Raw13CaptureHeader Header()=>CanonicalGeography13.Read<Raw13CaptureHeader>(Encoding.UTF8.GetString(HeaderBytes));
        internal GeoBuilding[] BuildingsCopy()=>CanonicalGeography13.Read<Buildings>(Encoding.UTF8.GetString(RegionalBuildingsBytes)).values;
        public string InputDigest=>World13DocumentCodec.Hash(Encoding.UTF8.GetBytes("RoadRush/raw13-coherent-capture/canonical-exact-v1\n"+CanonicalGeography13.Mode+"\n"+string.Join("\n",new[]{HeaderBytes,ProviderBytes,GridBytes,RegionalBuildingsBytes,RegionalProvenanceBytes,SourceEvidenceBytes}.Select(World13DocumentCodec.Hash))));
    }
    // Only this reviewed fixed-original-source minter is implemented. A future
    // production transaction adapter must be reviewed here; payload bools/receipts
    // cannot construct this type and no cache decoder calls it.
    public sealed class Raw13SourceCapture
    {
        readonly Raw13CapturePayload payload;readonly string capturedDigest;public string InputDigest=>capturedDigest;
        public byte[] CopyHeaderBytesForInspection()=>(byte[])payload.HeaderBytes.Clone();
        public byte[] CopyBuildingsBytesForInspection()=>(byte[])payload.RegionalBuildingsBytes.Clone();
        internal Raw13CapturePayload CheckedPayload(){if(payload.InputDigest!=capturedDigest)throw new InvalidDataException("Verified capture changed after original-source validation");return payload;}
        public string Scope=>"Reviewed original frozen coherent country capture plus explicitly reviewed complete regional building-policy output; no current DB/provider access or prepared admission";
        Raw13SourceCapture(Raw13CapturePayload payload){this.payload=payload;capturedDigest=payload.InputDigest;}
        static byte[] Pinned(string folder,string name,int length,string sha,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();string path=Path.GetFullPath(Path.Combine(folder,name));if(!path.StartsWith("/private/tmp/",StringComparison.Ordinal)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Only unlinked reviewed outside fixture inputs");
            using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(f.Length!=length)throw new InvalidDataException("Reviewed original source size differs");byte[] bytes=new byte[length];int at=0;while(at<length){token.ThrowIfCancellationRequested();int n=f.Read(bytes,at,Math.Min(65536,length-at));if(n==0)throw new EndOfStreamException();at+=n;}if(f.ReadByte()!=-1||World13DocumentCodec.Hash(bytes)!=sha)throw new InvalidDataException("Reviewed original source pin differs");return bytes;}
        }
        public static Raw13SourceCapture CaptureReviewedPantside3km(string folder,CancellationToken token)
        {
            byte[] provider=Pinned(folder,"pantside-3000-source.json",975131,"0d3caaec98bb67b9db72040bd194babc84caffbb336afe9294cdca14732dde3d",token);
            byte[] grid=Pinned(folder,"pantside-3000-grid-f32.bin",16900,"dd25d4f968ca11fbc1e7647a610774781ab7ec544fcaadd1589d1178dce33cc5",token);
            byte[] full=Pinned(folder,"pantside-3000-full-regional-raw-review.json",2074891,"6d3d4cf35a55c0fc640a14f0bf9489678d0833394f17a436f31732348fe8a4e7",token);
            byte[] provenance=Pinned(folder,"pantside-3000-source-provenance-review.json",2878313,"60ac0af68907619ed0df643f690adb7085bb48d5ae218c4e506bd8e63842af43",token);
            byte[] evidence=Pinned(folder,"readonly-source-capture.json",5379,"cfc71bf6f051e6e7e725f195c010b4ef8b4fed0765de119bd34c4f1cf1efe113",token);
            var headerSource=CanonicalGeography13.Read<GeoRegion>(new UTF8Encoding(false,true).GetString(full));
            var header=new Raw13CaptureHeader{name=headerSource.name,countryCode=headerSource.countryCode,timeZone=headerSource.timeZone,datasetVersion=headerSource.datasetVersion,datasetSource=headerSource.datasetSource,attribution=headerSource.attribution,railwayIdentity=headerSource.railwaySourceIdentity,latitude=headerSource.latitude,longitude=headerSource.longitude,size=3000,gridSide=65,drivingSide=headerSource.drivingSide,railwaysComplete=headerSource.railwaySourceComplete,expectedElements=2054,expectedHighways=448,expectedWaters=32,expectedAreas=86,expectedBaselineBuildings=1454,expectedRegionalBuildings=1793};
            JObject original;using(var reader=new JsonTextReader(new StringReader(new UTF8Encoding(false,true).GetString(evidence))){DateParseHandling=DateParseHandling.None})original=JObject.Load(reader);var window=((JArray)original["windows"]).First(w=>(int)w["sizeMetres"]==3000);var meta=original["sourceMetadata"];header.sourcePins=new Raw13OriginalSourcePins{sequence=(string)meta["osmosis_replication_sequence_number"],timestamp=(string)meta["osmosis_replication_timestamp"],originalPbfSha256=(string)meta["railway_source_sha256"],transportContentSha256=(string)meta["transport_content_sha256"],terrainTileSha256=(string)original["terrainFilesVerified"][0]["sha256"],sourceRowsSha256=(string)window["rowsTagsGeometrySha256"]};
            token.ThrowIfCancellationRequested();return new Raw13SourceCapture(new Raw13CapturePayload(header,provider,grid,headerSource.buildings,provenance,evidence));
        }
    }
}
