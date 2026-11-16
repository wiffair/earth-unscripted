using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Text;using System.Text.RegularExpressions;using System.Threading;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using UnityEngine;
namespace EarthUnscripted
{
    public sealed class Raw13ConversionInspection
    {
        readonly byte[] document;public readonly Raw13ConversionReport Report;public bool PreparedAdmitted=>false;public bool CommitCapability=>false;
        internal Raw13ConversionInspection(byte[] bytes,Raw13ConversionReport report){document=(byte[])bytes.Clone();Report=report;}
        public byte[] CopyDocumentBytes()=>(byte[])document.Clone();
    }
    [Serializable]public sealed class Raw13RoadOrigin
    {public string roadId,sourceWayId,widthProvenance;public int[] sourceEdgeIndices;}
    [Serializable]public sealed class Raw13ConversionReport
    {
        public string recipe="original-ways-grid-binary32-canonical13-v2",canonicalMode=CanonicalGeography13.Mode,layoutRecipe="preserved-ways-v1",scope="RAW source conversion inspection only; no prepared geometry/boot/peer or cache commit authority",inputDigest,conversionIdentity,sourceDigest,documentSha,providerSha,gridSha,regionalBuildingsSha,regionalProvenanceSha,originalCaptureEvidenceSha;
        public string rawRailwayYRecipe="DEM-node-estimate+0.45m-railhead-build-up";public bool reviewedFrozenCapture,sourceYClamp=false,widthWidening=false,legacyPrepare=false,railwayRenderDensification=false;public int inputElements,inputHighways,baselineBuildings,regionalBuildings,waters,areas,railways,sourceRoadEdges,outputRoadEdges,outputRoads,maximumRoadPoints,outsideRoadEdges,zeroLengthRoadEdges;
        public Raw13OriginalSourcePins originalSourcePins;public Raw13RoadOrigin[] roadOrigins;public string[] previewRouteSourceNodeIds;
    }
    public static class OriginalWayRaw13Converter
    {
        public const string Recipe="original-ways-grid-binary32-canonical13-v2";
        public enum Phase{OriginalSourceParsed,FeaturesCopied,PreservedLayout,BeforeSerialization,Validated}
        static void Progress(Phase phase,Action<Phase> observe,CancellationToken token){token.ThrowIfCancellationRequested();observe?.Invoke(phase);token.ThrowIfCancellationRequested();}
        sealed class Way{public string Id;public JObject Tags;public string[] Nodes;public Vector3[] Points;public Dictionary<string,int> Repeated=new Dictionary<string,int>(StringComparer.Ordinal);public List<Edge> Edges=new List<Edge>();}
        sealed class Edge{public int Index;public string A,B;public Vector3 P,Q;}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]static int Binary32Bits(double value)=>BitConverter.SingleToInt32Bits((float)value);
        static float F(double value)=>BitConverter.Int32BitsToSingle(Binary32Bits(value));
        static void Need(bool value,string why){if(!value)throw new InvalidDataException("Original raw13 conversion: "+why);}
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static bool Same(Vector3 a,Vector3 b)=>BitConverter.SingleToInt32Bits(a.x)==BitConverter.SingleToInt32Bits(b.x)&&BitConverter.SingleToInt32Bits(a.y)==BitConverter.SingleToInt32Bits(b.y)&&BitConverter.SingleToInt32Bits(a.z)==BitConverter.SingleToInt32Bits(b.z);
        static float Clamp(float v,float lo,float hi)=>Math.Max(lo,Math.Min(hi,v));
        static float Lerp(float a,float b,float t)=>F((double)a+F((double)F((double)b-a)*t));
        static string Text(JObject tags,string key,int max=2048){JToken t=tags[key];if(t==null)return null;Need(t.Type==JTokenType.String,"nonstring source tag");string s=(string)t;Need(s.Length<=max,"source tag text budget");return s;}
        static string Id(JToken token){Need(token!=null&&token.Type==JTokenType.Integer&&long.TryParse(token.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out long value)&&value>0,"original positive integer source node/way required");return token.ToString();}
        static float Number(string value,float fallback){if(value==null)return fallback;var m=Regex.Match(value,@"[-+]?\d+(\.\d+)?");return m.Success&&float.TryParse(m.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float x)&&Finite(x)?x:fallback;}
        static float Sample(GeoRegion r,float x,float z)
        {
            float gx=Clamp(F((double)F((double)F((double)x-r.minX)/r.width)*(r.nx-1)),0,r.nx-1),gz=Clamp(F((double)F((double)F((double)z-r.minZ)/r.depth)*(r.nz-1)),0,r.nz-1);
            int ix=Math.Min((int)gx,r.nx-2),iz=Math.Min((int)gz,r.nz-2);float fx=F((double)gx-ix),fz=F((double)gz-iz);
            return Lerp(Lerp(r.heights[iz*r.nx+ix],r.heights[iz*r.nx+ix+1],fx),Lerp(r.heights[(iz+1)*r.nx+ix],r.heights[(iz+1)*r.nx+ix+1],fx),fz);
        }
        static Vector3 At(GeoRegion r,float x,float z)=>new Vector3(x,Sample(r,x,z),z);
        static bool Clip(Vector3 a,Vector3 b,float half,out float lo,out float hi)
        {
            lo=0;hi=1;float dx=F((double)b.x-a.x),dz=F((double)b.z-a.z);var ps=new[]{-dx,dx,-dz,dz};var qs=new[]{F((double)a.x+half),F((double)half-a.x),F((double)a.z+half),F((double)half-a.z)};
            for(int i=0;i<4;i++){float p=ps[i],q=qs[i];if(p==0){if(q<0)return false;continue;}float t=F((double)q/p);if(p<0){if(t>hi)return false;lo=Math.Max(lo,t);}else{if(t<lo)return false;hi=Math.Min(hi,t);}}return lo<=hi;
        }
        static Vector3 Clipped(GeoRegion r,Vector3 a,Vector3 b,float t)=>t==0?a:t==1?b:At(r,F((double)a.x+F((double)F((double)b.x-a.x)*t)),F((double)a.z+F((double)F((double)b.z-a.z)*t)));
        static Vector3[] ClipPolygon(GeoRegion r,Vector3[] polygon,float half)
        {
            var values=polygon.Take(polygon.Length-1).ToList();
            for(int side=0;side<4;side++)
            {
                var input=values;values=new List<Vector3>();if(input.Count==0)break;
                float D(Vector3 p)=>side==0?F((double)p.x+half):side==1?F((double)half-p.x):side==2?F((double)p.z+half):F((double)half-p.z);
                Vector3 before=input[input.Count-1];float bd=D(before);foreach(var after in input){float ad=D(after);if((bd>=0)!=(ad>=0)){float t=F((double)bd/F((double)bd-ad));values.Add(Clipped(r,before,after,t));}if(ad>=0)values.Add(after);before=after;bd=ad;}
            }
            if(values.Count<3)return Array.Empty<Vector3>();values.Add(values[0]);return values.ToArray();
        }
        static bool Contains(Vector3[] poly,float x,float z)
        {bool inside=false;for(int i=0,j=poly.Length-1;i<poly.Length;j=i++){var a=poly[i];var b=poly[j];if((a.z>z)!=(b.z>z)){double cross=((double)b.x-a.x)*((double)z-a.z)-((double)x-a.x)*((double)b.z-a.z);if((b.z>a.z&&cross>0)||(b.z<a.z&&cross<0))inside=!inside;}}return inside;}
        static void Register(Dictionary<string,Vector3> nodes,string id,Vector3 p)
        {if(nodes.TryGetValue(id,out var old))Need(Same(old,p),"conflicting original shared-node coordinate bits");else nodes.Add(id,p);}
        static GeoRoad Road(Way w,Edge edge)
        {
            var t=w.Tags;string kind=Text(t,"highway"),lanesText=Text(t,"lanes"),widthText=Text(t,"width");float lanes=Number(lanesText,2),width=Number(widthText,Clamp(F((double)lanes*3.1f),4,20));Need(width>=.1f&&width<=80,"source width outside raw budget");
            string max=Text(t,"maxspeed");float speed=Number(max,30);if(max!=null&&Regex.IsMatch(max,@"\d")&&!max.Contains("mph"))speed=F(speed/1.609344);int limit=Math.Max(1,Math.Min(200,(int)Math.Round(speed,MidpointRounding.ToEven)));
            string one=Text(t,"oneway");int layer=0;if(Text(t,"layer") is string layerText){Need(int.TryParse(layerText,NumberStyles.Integer,CultureInfo.InvariantCulture,out layer)&&layer>=-20&&layer<=20,"source layer outside raw budget");}
            bool bridge=t["bridge"]!=null&&Text(t,"bridge")!="no",tunnel=t["tunnel"]!=null&&Text(t,"tunnel")!="no";Need(!bridge||!tunnel,"ambiguous road structure");
            return new GeoRoad{id=w.Id+"-span-"+edge.Index,start=edge.A,end=edge.B,name=Text(t,"name")??kind,kind=kind,width=width,limit=limit,oneWay=one=="-1"?-1:one=="yes"||Text(t,"junction")=="roundabout"?1:0,bridge=bridge,tunnel=tunnel,layer=layer,maxVehicleHeight=RailwayTags.Metres(Text(t,"maxheight"),0)};
        }
        public static Raw13ConversionInspection ConvertReviewed(Raw13SourceCapture capture,string compatibility,CancellationToken token,Action<Phase> progress=null)
        {if(capture==null)throw new ArgumentNullException(nameof(capture));return Convert(capture.CheckedPayload(),compatibility,true,token,progress);}
        public static Raw13ConversionInspection ConvertUnverifiedForInspection(Raw13CapturePayload payload,string compatibility,CancellationToken token,Action<Phase> progress=null)
            =>Convert(payload,compatibility,false,token,progress);
        static Raw13ConversionInspection Convert(Raw13CapturePayload payload,string compatibility,bool reviewed,CancellationToken token,Action<Phase> progress)
        {
            token.ThrowIfCancellationRequested();Need(payload!=null&&compatibility!=null&&compatibility.EndsWith("/geometry13",StringComparison.Ordinal),"explicit13 inputs required");var h=payload.Header();
            Need(!double.IsNaN(h.latitude)&&!double.IsInfinity(h.latitude)&&Math.Abs(h.latitude)<=85&&!double.IsNaN(h.longitude)&&!double.IsInfinity(h.longitude)&&Math.Abs(h.longitude)<=180&&h.gridSide>=2&&h.gridSide<=513&&h.size>=1000&&h.size<=6000&&payload.GridBytes.Length==h.gridSide*h.gridSide*4,"capture/grid bounds");
            Need(h.expectedRegionalBuildings<=2500&&h.expectedBaselineBuildings<=2500,"complete building count exceeds2500foundation; no thinning");var buildings=payload.BuildingsCopy();Need(buildings.Length==h.expectedRegionalBuildings&&buildings.Length<=2500,"complete regional building count differs");
            float[] grid=new float[h.gridSide*h.gridSide];for(int i=0;i<grid.Length;i++){grid[i]=BitConverter.ToSingle(payload.GridBytes,i*4);Need(Finite(grid[i])&&Math.Abs(grid[i])<20000,"nonfinite/out-of-range original grid");}float datum=F(Math.Floor(grid.Min())-10);string conversionIdentity=World13DocumentCodec.Hash(Encoding.UTF8.GetBytes("EarthUnscripted/raw13-conversion/1\n"+Recipe+"\n"+payload.InputDigest));
            var r=new GeoRegion{id="raw13-input-"+conversionIdentity.Substring(0,24),name=h.name,countryCode=h.countryCode,timeZone=h.timeZone,datasetVersion=h.datasetVersion,datasetSource=h.datasetSource,attribution=h.attribution,latitude=h.latitude,longitude=h.longitude,datum=datum,minX=F(-h.size*.5),minZ=F(-h.size*.5),width=h.size,depth=h.size,nx=h.gridSide,nz=h.gridSide,heights=grid.Select(g=>F((double)g-datum)).ToArray(),landNx=65,landNz=65,landCover=new int[4225],drivingSide=h.drivingSide,buildings=buildings,railwaySourceIdentity=h.railwayIdentity,railwaySourceComplete=h.railwaysComplete};
            var report=new Raw13ConversionReport{originalSourcePins=h.sourcePins,reviewedFrozenCapture=reviewed,inputDigest=payload.InputDigest,conversionIdentity=conversionIdentity,providerSha=World13DocumentCodec.Hash(payload.ProviderBytes),gridSha=World13DocumentCodec.Hash(payload.GridBytes),regionalBuildingsSha=World13DocumentCodec.Hash(payload.RegionalBuildingsBytes),regionalProvenanceSha=World13DocumentCodec.Hash(payload.RegionalProvenanceBytes),originalCaptureEvidenceSha=World13DocumentCodec.Hash(payload.SourceEvidenceBytes),regionalBuildings=buildings.Length};
            JObject source;try{using(var reader=new JsonTextReader(new StringReader(new UTF8Encoding(false,true).GetString(payload.ProviderBytes))){MaxDepth=16,DateParseHandling=DateParseHandling.None})source=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});}catch(Exception e)when(e is JsonException||e is DecoderFallbackException){throw new InvalidDataException("Original raw13 conversion: malformed source JSON/UTF8",e);}
            var elements=source["elements"] as JArray;Need(elements!=null&&elements.Count==h.expectedElements&&elements.Count<=12048,"complete source element count differs/budget");report.inputElements=elements.Count;Progress(Phase.OriginalSourceParsed,progress,token);
            var originalCoordinates=new Dictionary<string,(long Lat,long Lon)>(StringComparer.Ordinal);var ways=new List<Way>();var nodes=new Dictionary<string,Vector3>(StringComparer.Ordinal);var incidence=new Dictionary<string,int>(StringComparer.Ordinal);var sourceIds=new HashSet<string>(StringComparer.Ordinal);var waters=new List<GeoWater>();var areas=new List<GeoArea>();var rails=new List<GeoRailway>();int rawRefs=0;
            double cos=Math.Cos(h.latitude*Math.PI/180);float half=F(h.size*.5-5);
            foreach(JToken element in elements)
            {
                token.ThrowIfCancellationRequested();Need((string)element["type"]=="way","unsupported original element type");string id=Id(element["id"]);Need(sourceIds.Add(id),"duplicate original way identity");var tags=element["tags"] as JObject;var geometry=element["geometry"] as JArray;var refs=element["nodes"] as JArray;Need(tags!=null&&geometry!=null&&refs!=null&&geometry.Count==refs.Count&&geometry.Count>=2&&geometry.Count<=50000,"complete original way/node alignment");rawRefs+=refs.Count;Need(rawRefs<=300000,"original source reference budget");var points=new Vector3[refs.Count];var ids=new string[refs.Count];
                for(int i=0;i<points.Length;i++){token.ThrowIfCancellationRequested();ids[i]=Id(refs[i]);Need(geometry[i] is JObject&&geometry[i]["lat"]!=null&&geometry[i]["lon"]!=null&&(geometry[i]["lat"].Type==JTokenType.Float||geometry[i]["lat"].Type==JTokenType.Integer)&&(geometry[i]["lon"].Type==JTokenType.Float||geometry[i]["lon"].Type==JTokenType.Integer),"numeric original node coordinates required");double lat=geometry[i]["lat"].Value<double>(),lon=geometry[i]["lon"].Value<double>();Need(!double.IsNaN(lat)&&!double.IsNaN(lon)&&!double.IsInfinity(lat)&&!double.IsInfinity(lon)&&Math.Abs(lat)<=90&&Math.Abs(lon)<=180,"original coordinate bounds");var coordinate=(BitConverter.DoubleToInt64Bits(lat),BitConverter.DoubleToInt64Bits(lon));if(originalCoordinates.TryGetValue(ids[i],out var previousCoordinate))Need(previousCoordinate==coordinate,"conflicting original node coordinates across source ways");else originalCoordinates.Add(ids[i],coordinate);points[i]=At(r,F((lon-h.longitude)*111319.4908*cos),F((lat-h.latitude)*111319.4908));}
                if(Text(tags,"highway")!=null)
                {
                    report.inputHighways++;var w=new Way{Id=id,Tags=tags,Nodes=ids,Points=points};foreach(string node in ids){w.Repeated.TryGetValue(node,out int count);w.Repeated[node]=count+1;}for(int i=1;i<ids.Length;i++)foreach(string node in new[]{ids[i-1],ids[i]}){incidence.TryGetValue(node,out int count);incidence[node]=count+1;}
                    for(int i=0;i<points.Length-1;i++){report.sourceRoadEdges++;if(!Clip(points[i],points[i+1],half,out float lo,out float hi)){report.outsideRoadEdges++;continue;}var a=Clipped(r,points[i],points[i+1],lo);var b=Clipped(r,points[i],points[i+1],hi);if(a.x==b.x&&a.z==b.z){report.zeroLengthRoadEdges++;continue;}string first=lo==0?ids[i]:"clip-"+id+"-"+i+"-a",last=hi==1?ids[i+1]:"clip-"+id+"-"+i+"-b";Register(nodes,first,a);Register(nodes,last,b);w.Edges.Add(new Edge{Index=i,A=first,B=last,P=a,Q=b});}
                    ways.Add(w);
                }
                if(RailwayTags.IsTrack(tags))AppendRails(rails,r,tags,id,ids,points,half,token);
                if(Text(tags,"highway")==null&&Text(tags,"waterway")!=null){waters.Add(new GeoWater{name=Text(tags,"name")??"Waterway",width=Clamp(Number(Text(tags,"width"),Text(tags,"waterway")=="river"?14:3),2,100),points=points});Need(waters.Count<=1000,"water count budget");}
                else if(Text(tags,"highway")==null&&Text(tags,"building")!=null){Need(points.Length>=4&&points.Length<=4096&&Same(points[0],points[points.Length-1]),"incomplete original building polygon");report.baselineBuildings++;}
                else if(Text(tags,"highway")==null&&(Text(tags,"landuse")!=null||Text(tags,"natural")!=null)){Need(points.Length>=4&&points.Length<=4096&&Same(points[0],points[points.Length-1]),"incomplete original land-cover polygon");var clipped=ClipPolygon(r,points,half);if(clipped.Length>=4)areas.Add(new GeoArea{kind=Text(tags,"landuse")??Text(tags,"natural"),points=clipped});Need(areas.Count<=2000,"land-cover count budget");}
            }
            Need(report.inputHighways==h.expectedHighways&&report.baselineBuildings==h.expectedBaselineBuildings&&waters.Count==h.expectedWaters&&areas.Count==h.expectedAreas,"complete feature counts differ; no partial result");
            Progress(Phase.FeaturesCopied,progress,token);var roads=new List<GeoRoad>();var origins=new List<Raw13RoadOrigin>();
            foreach(var w in ways.OrderBy(w=>long.Parse(w.Id,CultureInfo.InvariantCulture)))
            {
                GeoRoad current=null;var positions=new List<Vector3>();var sourceNodes=new List<string>();var indices=new List<int>();int previous=-2;
                void Flush(){if(current==null)return;current.points=positions.ToArray();current.sourceNodeIds=sourceNodes.ToArray();roads.Add(current);Need(roads.Count<=10000,"road count budget");origins.Add(new Raw13RoadOrigin{roadId=current.id,sourceWayId=w.Id,sourceEdgeIndices=indices.ToArray(),widthProvenance=w.Tags["width"]!=null?"source-width-tag":w.Tags["lanes"]!=null?"source-lanes-estimate":"explicit-default-lanes-estimate"});current=null;positions.Clear();sourceNodes.Clear();indices.Clear();}
                foreach(var edge in w.Edges){token.ThrowIfCancellationRequested();if(current!=null&&(edge.Index!=previous+1||current.end!=edge.A||!w.Repeated.TryGetValue(edge.A,out int repeated)||repeated!=1||!incidence.TryGetValue(edge.A,out int degree)||degree!=2||positions.Count>=512))Flush();if(current==null){current=Road(w,edge);positions.Add(edge.P);sourceNodes.Add(edge.A);}else Need(Same(positions[positions.Count-1],edge.P),"shared endpoint altered");positions.Add(edge.Q);sourceNodes.Add(edge.B);indices.Add(edge.Index);current.end=edge.B;previous=edge.Index;}Flush();
            }
            Need(roads.Count>=8,"insufficient complete road network");Progress(Phase.PreservedLayout,progress,token);r.roads=roads.ToArray();r.railways=rails.ToArray();r.waters=waters.ToArray();r.areas=areas.ToArray();r.route=PreviewRoute(roads,nodes,out string[] routeIds,token);report.previewRouteSourceNodeIds=routeIds;
            foreach(var area in areas)for(int z=0;z<65;z++)for(int x=0;x<65;x++){if(x==0)token.ThrowIfCancellationRequested();if(Contains(area.points,F(-h.size*.5+x*h.size/64),F(-h.size*.5+z*h.size/64)))r.landCover[z*65+x]=area.kind=="forest"||area.kind=="wood"?2:area.kind=="residential"||area.kind=="industrial"||area.kind=="commercial"?3:1;}
            report.waters=waters.Count;report.areas=areas.Count;report.railways=rails.Count;report.outputRoads=roads.Count;report.outputRoadEdges=roads.Sum(x=>x.points.Length-1);report.maximumRoadPoints=roads.Max(x=>x.points.Length);Need(report.outputRoadEdges==ways.Sum(w=>w.Edges.Count),"original selected source edges not conserved");report.roadOrigins=origins.ToArray();
            report.sourceDigest=RoadSourceIdentity.Compute(r.roads);r.roadLayoutRecipe="preserved-ways-v1";r.roadSourceDigest=report.sourceDigest;
            var doc=new World13Document{compatibility=compatibility,geography=r,roadAuthority=new RoadAuthorityMetadata13{schema=0,layoutRecipe="preserved-ways-v1",sourceDigest=report.sourceDigest,sourceRoads=roads.Select(x=>new SourceRoad13Metadata{roadId=x.id,nodeIds=(string[])x.sourceNodeIds.Clone()}).ToArray()}};
            Progress(Phase.BeforeSerialization,progress,token);byte[] bytes=Encoding.UTF8.GetBytes(CanonicalGeography13.Write(doc));Need(bytes.Length<=World13DocumentCodec.MaximumBytes,"whole raw document budget");report.documentSha=World13DocumentCodec.Hash(bytes);
            // Strict typed inspection validates output budgets, but never mints a
            // cache/boot capability or upgrades this to prepared authority.
            World13DocumentCodec.Preflight(new UTF8Encoding(false,true).GetString(bytes));World13GeographyValidation.Validate(r);Progress(Phase.Validated,progress,token);return new Raw13ConversionInspection(bytes,report);
        }
        static void AppendRails(List<GeoRailway> rails,GeoRegion r,JObject tags,string id,string[] ids,Vector3[] points,float half,CancellationToken token)
        {
            string kind=RailwayTags.Lifecycle(tags);Need(RailwayMetrics.Supported(kind),"unsupported raw railway lifecycle");var run=new List<Vector3>();var refs=new List<string>();int part=0;
            void Flush(){if(run.Count>=2){Need(rails.Count<2048&&run.Count<=4096,"raw railway way budget");float gauge=Number(Text(tags,"gauge"),1435);if(gauge>=100)gauge=F(gauge/1000.0);if(gauge<.3f||gauge>3)gauge=RailwayMetrics.DefaultGauge;int tracks=(int)Number(Text(tags,"tracks"),1);Need(tracks>=1&&tracks<=16,"raw railway tracks budget");float width=Math.Max(Number(Text(tags,"width"),RailwayMetrics.DefaultWidth),F(gauge+1.2+(tracks-1)*(double)RailwayMetrics.TrackSpacing));Need(width<=100,"raw railway width budget");rails.Add(new GeoRailway{id="railway-"+id+"-"+part++,start=refs[0],end=refs[refs.Count-1],name=Text(tags,"name",240),operatorName=Text(tags,"operator",240),electrified=Text(tags,"electrified",64),kind=kind,width=width,gauge=gauge,tracks=tracks,layer=RailwayTags.Layer(tags),bridge=tags["bridge"]!=null&&Text(tags,"bridge")!="no",tunnel=tags["tunnel"]!=null&&Text(tags,"tunnel")!="no",points=run.ToArray(),nodeIds=refs.ToArray(),estimatedProfile=true});}run.Clear();refs.Clear();}
            for(int i=0;i<points.Length-1;i++){token.ThrowIfCancellationRequested();if(!Clip(points[i],points[i+1],half,out float lo,out float hi)){Flush();continue;}var a=Clipped(r,points[i],points[i+1],lo);var b=Clipped(r,points[i],points[i+1],hi);if(a.x==b.x&&a.z==b.z)continue;string first=lo==0?ids[i]:"railclip-"+id+"-"+i+"-a",last=hi==1?ids[i+1]:"railclip-"+id+"-"+i+"-b";if(refs.Count>0&&refs[refs.Count-1]!=first)Flush();if(run.Count==0){run.Add(new Vector3(a.x,F((double)a.y+RailwayMetrics.TrackBuildUp),a.z));refs.Add(first);}run.Add(new Vector3(b.x,F((double)b.y+RailwayMetrics.TrackBuildUp),b.z));refs.Add(last);if(hi<1)Flush();}Flush();Need(rails.Sum(x=>x.points.Length)<=65536,"raw railway point budget");
        }
        static Vector3[] PreviewRoute(List<GeoRoad> roads,Dictionary<string,Vector3> nodes,out string[] ids,CancellationToken token)
        {
            var graph=new Dictionary<string,SortedSet<string>>(StringComparer.Ordinal);foreach(var road in roads)for(int i=1;i<road.sourceNodeIds.Length;i++){string a=road.sourceNodeIds[i-1],b=road.sourceNodeIds[i];if(!graph.ContainsKey(a))graph[a]=new SortedSet<string>(StringComparer.Ordinal);if(!graph.ContainsKey(b))graph[b]=new SortedSet<string>(StringComparer.Ordinal);graph[a].Add(b);graph[b].Add(a);}
            string root=graph.Keys.OrderBy(k=>(double)nodes[k].x*nodes[k].x+(double)nodes[k].z*nodes[k].z).ThenBy(k=>k,StringComparer.Ordinal).First();var route=new List<string>();var visited=new HashSet<string>(StringComparer.Ordinal);
            void Walk(string a,int depth){token.ThrowIfCancellationRequested();route.Add(a);if(depth>=128)return;foreach(string b in graph[a]){if(visited.Count>=256)break;string key=string.CompareOrdinal(a,b)<0?a+"|"+b:b+"|"+a;if(!visited.Add(key))continue;Walk(b,depth+1);route.Add(a);}}
            Walk(root,0);Need(route.Count>=2,"original source-node route unavailable");ids=route.ToArray();return ids.Select(k=>nodes[k]).ToArray();
        }
    }
}
