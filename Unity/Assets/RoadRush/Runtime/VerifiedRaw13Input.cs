using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Text;using System.Threading;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using UnityEngine;
namespace RoadRush
{
    // Fixture-only capability. Disk receipts/JSON flags cannot construct this type.
    public sealed class VerifiedRaw13Input
    {
        [Serializable]public sealed class EvidenceFile{public string role,sha256;public long bytes;}
        [Serializable]sealed class Evidence
        {
            public string format="RoadRushRaw13SourceEvidence1",origin="verified-frozen-offline-country-source",recipe="preserved-ways-v1",sourceIdentity="source-roads/2",datasetVersion,datasetSource,sourceDigest,scope="Original pinned source/node/projection/edge evidence; no provider signature, current-countryDB or prepared-world admission";
            public int roads=799,edges=2787,buildings=1793,maximumSourcePoints=512;public EvidenceFile[] files;
        }
        sealed class Pin
        {public readonly string Role,Name,Sha,Group;public readonly long Bytes;public string Path;public Pin(string role,string name,string sha,long bytes,string group){Role=role;Name=name;Sha=sha;Bytes=bytes;Group=group;}}
        sealed class Way{public string[] Nodes;public JArray Geometry;public readonly Dictionary<string,int> Repeated=new Dictionary<string,int>(StringComparer.Ordinal);}
        struct XZ{public float X,Z;public XZ(float x,float z){X=x;Z=z;}}
        const string ExpectedRoadDigest="6c46339f3b03141caff71417448980759d96caf1e3547d92f4a4d093cd4d6f42";
        readonly byte[] document,evidence;readonly Pin[] pins;
        public string DocumentSha256{get;}public string EvidenceSha256{get;}public string Compatibility{get;}public string SourceDigest=>ExpectedRoadDigest;public string CacheKey{get;}
        VerifiedRaw13Input(byte[] document,byte[] evidence,string compatibility,Pin[] pins)
        {this.document=document;this.evidence=evidence;this.pins=pins;Compatibility=compatibility;DocumentSha256=World13DocumentCodec.Hash(document);EvidenceSha256=World13DocumentCodec.Hash(evidence);CacheKey=World13DocumentCodec.Hash(Encoding.UTF8.GetBytes("RoadRush/raw13-input-cache/1\n"+compatibility+"\n"+DocumentSha256+"\n"+EvidenceSha256));}
        public byte[] CopyDocument()=> (byte[])document.Clone();public byte[] CopyEvidence()=> (byte[])evidence.Clone();
        public World13ReadContext Context()=>World13ReadContext.FromVerifiedRawReceipt(World13ReadContext.RawOrigin.VerifiedFreshProvider,Compatibility,DocumentSha256,EvidenceSha256);
        public void RevalidateSourcePins(CancellationToken token)
        {foreach(var p in pins){token.ThrowIfCancellationRequested();ReadPinned(p);}}
        static void Require(bool b,string why){if(!b)throw new InvalidDataException("Verified raw13 source: "+why);}
        static byte[] ReadPinned(Pin p)
        {
            Require(File.Exists(p.Path)&&!new FileInfo(p.Path).Attributes.HasFlag(FileAttributes.ReparsePoint),"missing/linked original "+p.Role);
            using(var f=new FileStream(p.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {Require(f.Length==p.Bytes&&p.Bytes>0&&p.Bytes<8*1024*1024,"original size "+p.Role);var bytes=new byte[(int)p.Bytes];int at=0;while(at<bytes.Length){int n=f.Read(bytes,at,bytes.Length-at);Require(n>0,"truncated original "+p.Role);at+=n;}Require(f.ReadByte()==-1&&World13DocumentCodec.Hash(bytes)==p.Sha,"original pin changed "+p.Role);return bytes;}
        }
        static JObject Json(byte[] bytes)
        {using(var r=new JsonTextReader(new StringReader(new UTF8Encoding(false,true).GetString(bytes))){MaxDepth=16,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})return JObject.Load(r,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});}
        static bool Bits(float a,float b)=>BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b);
        static bool Point(Vector3 a,Vector3 b)=>Bits(a.x,b.x)&&Bits(a.y,b.y)&&Bits(a.z,b.z);
        static string N(int value)=>value.ToString(CultureInfo.InvariantCulture);
        static string Integer(JToken value){Require(value!=null&&value.Type==JTokenType.Integer,"original node/way integer ID");return value.Value<long>().ToString(CultureInfo.InvariantCulture);}
        static bool Attributes(GeoRoad a,GeoRoad b)=>a.name==b.name&&a.kind==b.kind&&Bits(a.width,b.width)&&Bits(a.maxVehicleHeight,b.maxVehicleHeight)&&a.limit==b.limit&&a.oneWay==b.oneWay&&a.layer==b.layer&&a.bridge==b.bridge&&a.tunnel==b.tunnel;
        static GeoRoad CopyRoad(GeoRoad r)=>new GeoRoad{id=r.id,start=r.start,end=r.end,name=r.name,kind=r.kind,width=r.width,maxVehicleHeight=r.maxVehicleHeight,limit=r.limit,oneWay=r.oneWay,layer=r.layer,bridge=r.bridge,tunnel=r.tunnel};
        // Match the pinned converter's separate binary32 operations. Integer return
        // across a non-inlined boundary prevents Mono retaining a wider product.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Binary32Bits(double value)=>BitConverter.SingleToInt32Bits((float)value);
        static float F32(double value)=>BitConverter.Int32BitsToSingle(Binary32Bits(value));
        static XZ Project(JToken point,GeoRegion g)=>new XZ(F32((point["lon"].Value<double>()-g.longitude)*111319.4908*Math.Cos(g.latitude*Math.PI/180)),F32((point["lat"].Value<double>()-g.latitude)*111319.4908));
        static void Clip(XZ a,XZ b,float half,out XZ start,out XZ end,out float lo,out float hi)
        {
            lo=0;hi=1;float dx=F32((double)b.X-a.X),dz=F32((double)b.Z-a.Z);
            var ps=new[]{-dx,dx,-dz,dz};var qs=new[]{F32((double)a.X+half),F32((double)half-a.X),F32((double)a.Z+half),F32((double)half-a.Z)};
            for(int i=0;i<4;i++){float p=ps[i],q=qs[i];if(p==0){Require(q>=0,"nonintersecting original edge");continue;}float t=F32((double)q/p);if(p<0){Require(t<=hi,"original clip intersection");lo=Math.Max(lo,t);}else{Require(t>=lo,"original clip intersection");hi=Math.Min(hi,t);}}
            start=new XZ(F32((double)a.X+F32((double)dx*lo)),F32((double)a.Z+F32((double)dz*lo)));
            end=new XZ(F32((double)a.X+F32((double)dx*hi)),F32((double)a.Z+F32((double)dz*hi)));
        }
        public static VerifiedRaw13Input VerifyFrozenPantside3km(string actualFolder,string layoutFolder,string compatibility,CancellationToken token)
        {
            // These paths select ONLY reviewed frozen bytes, not an arbitrary claimed source.
            actualFolder=Path.GetFullPath(actualFolder);layoutFolder=Path.GetFullPath(layoutFolder);
            Require(actualFolder.StartsWith("/private/tmp/",StringComparison.Ordinal)&&layoutFolder.StartsWith("/private/tmp/",StringComparison.Ordinal),"outside-only frozen fixture roots");
            var specs=new[]{
            new Pin("provider","pantside-3000-source.json","0d3caaec98bb67b9db72040bd194babc84caffbb336afe9294cdca14732dde3d",975131,"actual"),
            new Pin("raw","pantside-3000-raw-converted.json","f7f8194447063779372b5aa1e8dcae4968483918de1543850bfa52e075458adf",1890802,"actual"),
            new Pin("full","pantside-3000-full-regional-raw-review.json","6d3d4cf35a55c0fc640a14f0bf9489678d0833394f17a436f31732348fe8a4e7",2074891,"actual"),
            new Pin("provenance","pantside-3000-source-provenance-review.json","60ac0af68907619ed0df643f690adb7085bb48d5ae218c4e506bd8e63842af43",2878313,"actual"),
            new Pin("capture","readonly-source-capture.json","cfc71bf6f051e6e7e725f195c010b4ef8b4fed0765de119bd34c4f1cf1efe113",5379,"actual"),
            new Pin("grid","pantside-3000-grid-f32.bin","dd25d4f968ca11fbc1e7647a610774781ab7ec544fcaadd1589d1178dce33cc5",16900,"actual"),
            new Pin("preserved","pantside-3000-preserved-ways-raw.json","db9abc489898fb2413a81117a127dd1505a03e997f24fd5e57c83c95eefde498",1452527,"layout"),
            new Pin("layoutReceipt","pantside-3000-layout-receipt.json","9c871bb7274da2c91c3053e6bffb6117f84b248eb7068e6a6e54730e68617a31",222259,"layout"),
            new Pin("layoutCode","preserve_ways.py","11c4483056fe90df242b8cb40f97ca01e04b92609e7445900a4066c9ecd4e083",9538,"layout")
            };
            var buffers=new Dictionary<string,byte[]>(StringComparer.Ordinal);
            foreach(var p in specs){token.ThrowIfCancellationRequested();p.Path=Path.Combine(p.Group=="layout"?layoutFolder:actualFolder,p.Name);buffers.Add(p.Role,ReadPinned(p));}
            string fullText=new UTF8Encoding(false,true).GetString(buffers["full"]),rawText=new UTF8Encoding(false,true).GetString(buffers["raw"]);
            var full=WorldFieldJson.Read<GeoRegion>(fullText);var raw=WorldFieldJson.Read<GeoRegion>(rawText);
            var a=Json(buffers["full"]);var b=Json(buffers["raw"]);a.Remove("buildings");b.Remove("buildings");Require(JToken.DeepEquals(a,b),"regional source changed nonbuilding data");
            Require(full.buildings.Length==1793&&raw.buildings.Length==1454&&full.roads.Length==2787&&full.datasetVersion=="United Kingdom/1074"&&full.datasetSource=="https://download.geofabrik.de/europe/united-kingdom-updates","exact frozen3km scope");
            Require(full.width==3000&&full.depth==3000&&full.nx==65&&full.nz==65,"fixture geometry bounds");
            var grid=buffers["grid"];Require(grid.Length==full.heights.Length*4,"DEM sample count");for(int i=0;i<full.heights.Length;i++)Require(Bits(full.heights[i],BitConverter.ToSingle(grid,i*4)-full.datum),"datum-relative terrain sample bits");
            var ways=new Dictionary<string,Way>(StringComparer.Ordinal);var incidence=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var item in (JArray)Json(buffers["provider"])["elements"])
            {
                if((string)item["type"]!="way"||item["tags"]?["highway"]==null)continue;string id=Integer(item["id"]);var w=new Way{Nodes=((JArray)item["nodes"]).Select(Integer).ToArray(),Geometry=(JArray)item["geometry"]};Require(w.Nodes.Length==w.Geometry.Count&&ways.TryAdd(id,w),"complete unique original way");
                foreach(string node in w.Nodes){w.Repeated.TryGetValue(node,out int n);w.Repeated[node]=n+1;}
                for(int i=1;i<w.Nodes.Length;i++)foreach(string node in new[]{w.Nodes[i-1],w.Nodes[i]}){incidence.TryGetValue(node,out int n);incidence[node]=n+1;}
            }
            var expectedXZ=new Dictionary<string,XZ>(StringComparer.Ordinal);var edges=new Dictionary<string,GeoRoad>(StringComparer.Ordinal);
            foreach(var road in full.roads)
            {
                token.ThrowIfCancellationRequested();Require(road.points.Length==2&&edges.TryAdd(road.id,road),"original node-edge source");int split=road.id.LastIndexOf('-');string wayId=road.id.Substring(0,split);int index=int.Parse(road.id.Substring(split+1),CultureInfo.InvariantCulture);Require(ways.TryGetValue(wayId,out var w)&&index>=0&&index+1<w.Nodes.Length,"edge original way reference");
                Clip(Project(w.Geometry[index],full),Project(w.Geometry[index+1],full),full.width*.5f-5,out var x,out var z,out float lo,out float hi);
                string start=lo==0?w.Nodes[index]:"clip-"+wayId+"-"+N(index)+"-a",end=hi==1?w.Nodes[index+1]:"clip-"+wayId+"-"+N(index)+"-b";Require(road.start==start&&road.end==end,"provider source-node/clip binding");expectedXZ[start]=x;expectedXZ[end]=z;
            }
            foreach(var road in full.roads)for(int i=0;i<2;i++){var p=expectedXZ[i==0?road.start:road.end];var actual=road.points[i];Require(Bits(p.X,actual.x)&&Bits(p.Z,actual.z),"provider projection/shared-node overwrite bits road="+road.id+" point="+i+" expectedXZ="+p.X.ToString("R",CultureInfo.InvariantCulture)+","+p.Z.ToString("R",CultureInfo.InvariantCulture)+" actualXZ="+actual.x.ToString("R",CultureInfo.InvariantCulture)+","+actual.z.ToString("R",CultureInfo.InvariantCulture)+" expectedBits="+BitConverter.SingleToInt32Bits(p.X).ToString("X8")+","+BitConverter.SingleToInt32Bits(p.Z).ToString("X8")+" actualBits="+BitConverter.SingleToInt32Bits(actual.x).ToString("X8")+","+BitConverter.SingleToInt32Bits(actual.z).ToString("X8"));}
            var output=new List<GeoRoad>();var rows=new List<SourceRoad13Metadata>();var consumed=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in (JArray)Json(buffers["layoutReceipt"])["roads"])
            {
                token.ThrowIfCancellationRequested();string wayId=(string)row["sourceWayId"],id=(string)row["roadId"];int[] indices=((JArray)row["sourceEdgeIndices"]).Values<int>().ToArray();Require(indices.Length>=1&&indices.Length<512,"preserved source point budget");
                var first=edges[wayId+"-"+N(indices[0])];var result=CopyRoad(first);result.id=id;Require(id==wayId+"-span-"+N(indices[0]),"preserved source ID");var points=new List<Vector3>{first.points[0]};var nodes=new List<string>{first.start};GeoRoad previous=null;
                foreach(int index in indices)
                {
                    string edgeId=wayId+"-"+N(index);Require(edges.TryGetValue(edgeId,out var edge)&&consumed.Add(edgeId),"unique conserved source edge");Require(Attributes(first,edge),"same-way attribute boundary");
                    if(previous!=null){Require(index==indices[points.Count-2]+1&&previous.end==edge.start&&Point(previous.points[1],edge.points[0]),"exact contiguous source edge");Require(ways[wayId].Repeated.TryGetValue(edge.start,out int repeat)&&repeat==1&&incidence[edge.start]==2,"true junction/repeated node cannot be merged");}
                    points.Add(edge.points[1]);nodes.Add(edge.end);previous=edge;
                }
                result.end=previous.end;result.points=points.ToArray();result.sourceNodeIds=nodes.ToArray();output.Add(result);rows.Add(new SourceRoad13Metadata{roadId=id,nodeIds=(string[])result.sourceNodeIds.Clone()});
            }
            Require(consumed.Count==2787&&output.Count==799,"all original edges conserved");full.roads=output.ToArray();Require(RoadSourceIdentity.Compute(full.roads)==ExpectedRoadDigest,"independent Python source2 bit vector");
            var document=new World13Document{compatibility=compatibility,geography=full,roadAuthority=new RoadAuthorityMetadata13{schema=0,layoutRecipe="preserved-ways-v1",sourceDigest=ExpectedRoadDigest,sourceRoads=rows.ToArray()}};
            byte[] body=Encoding.UTF8.GetBytes(WorldFieldJson.Write(document));var record=new Evidence{datasetVersion=full.datasetVersion,datasetSource=full.datasetSource,sourceDigest=ExpectedRoadDigest,files=specs.OrderBy(p=>p.Role,StringComparer.Ordinal).Select(p=>new EvidenceFile{role=p.Role,sha256=p.Sha,bytes=p.Bytes}).ToArray()};byte[] receipt=Encoding.UTF8.GetBytes(WorldFieldJson.Write(record));
            var resultInput=new VerifiedRaw13Input(body,receipt,compatibility,specs);var verified=World13DocumentCodec.ReadVerifiedRaw(body,resultInput.Context());Require(World13DocumentCodec.Hash(World13DocumentCodec.WriteVerifiedRaw(verified))==resultInput.DocumentSha256,"exact complete envelope roundtrip");resultInput.RevalidateSourcePins(token);return resultInput;
        }
    }
}
