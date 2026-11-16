using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
namespace EarthUnscripted.RegionalDraft
{
    // Pure data-only preparation. Call on a worker with captured paths; no Unity,
    // country SQLite, network, installation, deletion or authoritative world mutation.
    public sealed class RegionalCollection
    {
        public readonly struct Rect
        {
            public readonly double West,South,East,North;
            public Rect(double west,double south,double east,double north)
            {Require(Finite(west)&&Finite(south)&&Finite(east)&&Finite(north)&&west>=-180&&east<=180&&south>=-80&&north<=80&&west<east&&south<north,"Invalid geographic rectangle");West=west;South=south;East=east;North=north;}
            public bool Intersects(Rect other)=>West<=other.East&&East>=other.West&&South<=other.North&&North>=other.South;
            public bool Contains(double lon,double lat)=>lon>=West&&lon<=East&&lat>=South&&lat<=North;
            internal static Rect Union(Rect a,Rect b)=>new Rect(Math.Min(a.West,b.West),Math.Min(a.South,b.South),Math.Max(a.East,b.East),Math.Max(a.North,b.North));
            internal bool Contains(Rect b)=>b.West>=West-1e-10&&b.East<=East+1e-10&&b.South>=South-1e-10&&b.North<=North+1e-10;
        }
        public readonly struct Point
        {public readonly double Longitude,Latitude;public Point(double lon,double lat){Longitude=lon;Latitude=lat;}}
        public sealed class Building
        {
            public readonly string Id,Kind,Roof,SourceKey,OwnerCell;public readonly float Height;public readonly Rect Bounds;public readonly IReadOnlyList<Point> Points;
            internal Building(string id,string kind,string roof,float height,string sourceKey,string owner,IReadOnlyList<Point> points,Rect bounds)
            {Id=id;Kind=kind;Roof=roof;Height=height;SourceKey=sourceKey;OwnerCell=owner;Points=points;Bounds=bounds;}
        }
        public sealed class Leaf
        {
            public readonly string Id,Series,ManifestPath,PayloadPath,ManifestHash,PayloadHash,Attribution,Cell;public readonly long Revision,ManifestBytes,PayloadBytes;public readonly int FeatureCount;public readonly Rect Bounds;
            internal Leaf(JObject obj,string cell)
            {
                Require(Text(obj,"kind",24)=="buildings"&&Text(obj,"format",64)=="EarthUnscriptedBuildings1","Only approved building leaves supported; canopy/POIs/previews cannot activate");
                Id=SafeId(Text(obj,"id",100));Series=SafeId(Text(obj,"revisionSeries",100));Revision=Integer(obj,"revision",1,100000000);Cell=cell;
                ManifestPath=Text(obj,"manifestPath",400);PayloadPath=Text(obj,"payloadPath",400);ManifestHash=SHA(Text(obj,"manifestSHA256",64));PayloadHash=SHA(Text(obj,"payloadSHA256",64));
                ManifestBytes=Integer(obj,"manifestBytes",1,256000);PayloadBytes=Integer(obj,"payloadBytes",1,4000000);FeatureCount=(int)Integer(obj,"featureCount",0,4096);Bounds=BoundsOf(obj["contentBoundsWGS84"]);Attribution=Text(obj,"attribution",1200);
                Require(Text(obj,"licence",64)=="OGL-3.0","Unsupported leaf licence");
            }
        }
        public sealed class Limits
        {
            public int MaximumBuildings=2500,MaximumBuildingPoints=120000;public long MaximumRoofCost=20000000,MaximumSelectedBytes=64L*1024*1024;
            public static Limits Proposed8192()=>new Limits{MaximumBuildings=8192};
        }
        public sealed class Selection
        {
            public readonly string CollectionIdentity,SelectionIdentity;public readonly IReadOnlyList<Leaf> Leaves;public readonly IReadOnlyList<Building> Buildings;public readonly IReadOnlyList<string> CoverageNotes,BudgetProblems;
            public readonly long SelectedBytes,RoofCost;public readonly int BuildingPoints;public readonly bool RuntimeAuthoringGate;
            public bool FitsDeclaredBuildingBudget=>BudgetProblems.Count==0;
            // This alone never authorizes world mutation: final merged world budget,
            // exact signed snapshot length and activation review are separate gates.
            public bool CollectionPermitsActivation=>!RuntimeAuthoringGate;
            internal Selection(string identity,List<Leaf> leaves,List<Building> buildings,List<string> notes,List<string> problems,long bytes,long roof,int points,bool blocked)
            {
                CollectionIdentity=identity;Leaves=new ReadOnlyCollection<Leaf>(leaves);Buildings=new ReadOnlyCollection<Building>(buildings);CoverageNotes=new ReadOnlyCollection<string>(notes);BudgetProblems=new ReadOnlyCollection<string>(problems);SelectedBytes=bytes;RoofCost=roof;BuildingPoints=points;RuntimeAuthoringGate=blocked;
                SelectionIdentity=DataPackageRules.Hash(Encoding.UTF8.GetBytes(identity+"|"+string.Join("|",leaves.Select(l=>l.ManifestHash+":"+l.PayloadHash))+"|"+string.Join("|",buildings.Select(b=>b.SourceKey))));
            }
        }
        sealed class Node {public Rect Bounds;public Node Left,Right;public Leaf Value;}
        readonly string root;readonly Leaf[] leaves;readonly JObject[] cells;readonly Node spatial;public readonly string Id,Series,IndexSHA256,SpecSHA256,Identity;public readonly long Revision;public readonly bool RuntimeActivationAllowed;public int LeafCount=>leaves.Length;
        public IReadOnlyList<Leaf> IndexedLeaves=>Array.AsReadOnly(leaves);
        // All common-metadata building IDs, including future representations omitted
        // from this reader view. Completeness must not be inferred from filtered leaves.
        public readonly IReadOnlyList<string> DeclaredBuildingLeafIds;
        RegionalCollection(string path,string expectedIndex,ISet<string> supportedLeaves=null)
        {
            root=Path.GetFullPath(path);NoLinks(root);byte[] bytes=Read("coverage-index.json",4194304,null,SHA(expectedIndex));var index=Object(bytes,32);
            Require(Text(index,"format",64)=="EarthUnscriptedRegionalCollection1","Unsupported regional index");Id=SafeId(Text(index,"collectionId",100));Series=SafeId(Text(index,"revisionSeries",100));Revision=Integer(index,"revision",1,100000000);IndexSHA256=DataPackageRules.Hash(bytes);Identity=Id+"|"+Series+"|"+Revision.ToString(CultureInfo.InvariantCulture)+"|"+IndexSHA256;
            RuntimeActivationAllowed=Boolean(index,"runtimeActivationAllowed");SpecSHA256=SHA(Text(index,"specSHA256",64));var spec=Object(Read("spec.json",256000,null,SpecSHA256),32);
            Require(Text(spec,"format",64)=="EarthUnscriptedRegionalCollectionSpec1"&&Text(spec,"collectionId",100)==Id&&Text(spec,"revisionSeries",100)==Series&&Integer(spec,"collectionRevision",1,100000000)==Revision,"Index/spec identity conflict");
            Require(!RuntimeActivationAllowed||Boolean(spec,"runtimeActivationAllowed"),"Index cannot promote authoring-only spec");
            var array=index["cells"] as JArray;Require(array!=null&&array.Count<=2048,"Cell allocation budget exceeded");cells=array.Cast<JObject>().ToArray();var found=new List<Leaf>();var cellIds=new HashSet<string>(StringComparer.Ordinal);var ids=new HashSet<string>(StringComparer.Ordinal);var paths=new HashSet<string>(StringComparer.Ordinal);int declaredLeaves=0;
            foreach(var cell in cells)
            {
                string cellId=SafeId(Text(cell,"cellId",100));Require(cellIds.Add(cellId),"Duplicate owner cell record");
                var entries=cell["leaves"] as JArray;Require(entries!=null,"Cell leaves missing");
                var layer=cell["layers"]?["buildings"] as JObject;Require(layer!=null,"Building coverage status missing");
                string state=Text(layer,"status",64);Require(new[]{"uninspected","source-cached","source-available-not-downloaded","source-missing","compiled-partial","compiled-supported-complete","blocked","budget-paused"}.Contains(state),"Unsupported building coverage state");
                long expectedFeatures=Integer(layer,"featureCount",0,1000000);Require(Integer(layer,"compiledLeafCount",0,8192)==entries.Count,"Coverage shard count differs from complete index");Integer(layer,"unsupportedCount",0,1000000);
                long cellFeatures=0;var cellBounds=BoundsOf(cell["contentBoundsWGS84"]);
                foreach(var token in entries)
                {
                    Require(token is JObject,"Invalid leaf record");var obj=(JObject)token;
                    // Unreviewed canopy is indexed separately as previews. Future other
                    // approved layers require explicit validators, not implicit loading.
                    if((string)obj["kind"]!="buildings")throw new InvalidDataException("Unimplemented approved regional layer; refusing partial activation");
                    string leafId=SafeId(Text(obj,"id",100));Require(ids.Add(leafId),"Duplicate/conflicting leaf identity in immutable index");Require(++declaredLeaves<=8192,"Leaf allocation budget exceeded");
                    cellFeatures+=Integer(obj,"featureCount",0,4096);Require(cellBounds.Contains(BoundsOf(obj["contentBoundsWGS84"])),"Cell content bounds omit leaf overhang");
                    // A pack-capability view may exclude explicitly unsupported elements.
                    // Validate common index metadata, but never parse their newer typed leaf/payload format.
                    // QueryAll's default constructor still admits every declared schema1 leaf as before.
                    if(supportedLeaves!=null&&!supportedLeaves.Contains(leafId))continue;
                    var leaf=new Leaf(obj,cellId);SafePath(leaf.ManifestPath);SafePath(leaf.PayloadPath);
                    Require(paths.Add(leaf.ManifestPath)&&paths.Add(leaf.PayloadPath),"Artifact path reused by multiple records");found.Add(leaf);
                }
                Require(cellFeatures==expectedFeatures,"Coverage feature count differs from complete indexed shards");Require(entries.Count==0||state=="compiled-partial"||state=="compiled-supported-complete","Uncompiled layer contains activation leaves");
            }
            if(supportedLeaves!=null)Require(supportedLeaves.All(id=>found.Any(leaf=>leaf.Id==id)),"Requested supported leaf is absent from this collection");
            DeclaredBuildingLeafIds=Array.AsReadOnly(ids.OrderBy(id=>id,StringComparer.Ordinal).ToArray());
            leaves=found.OrderBy(l=>l.Id,StringComparer.Ordinal).ToArray();spatial=Tree(leaves.OrderBy(l=>l.Bounds.West).ToArray(),0,leaves.Length);
        }
        public static RegionalCollection Load(string root,string expectedIndexSHA256)=>new RegionalCollection(root,expectedIndexSHA256);
        public static RegionalCollection LoadSupportedLeaves(string root,string expectedIndexSHA256,IEnumerable<string> supportedLeafIds)
        {
            if(supportedLeafIds==null)throw new ArgumentNullException(nameof(supportedLeafIds));
            return new RegionalCollection(root,expectedIndexSHA256,new HashSet<string>(supportedLeafIds,StringComparer.Ordinal));
        }
        public static RegionalCollection SelectForNewPreparation(IEnumerable<RegionalCollection> candidates)
        {
            var all=candidates.ToArray();Require(all.Length>0&&all.Length<=16,"Explicit collection candidate budget");Require(all.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()==1&&all.Select(c=>c.Series).Distinct(StringComparer.Ordinal).Count()==1,"Coexisting collections have different identities/series");
            Require(!all.GroupBy(c=>c.Revision).Any(g=>g.Select(c=>c.IndexSHA256).Distinct(StringComparer.Ordinal).Count()>1),"Conflicting index bytes declare the same collection revision");return all.OrderByDescending(c=>c.Revision).ThenBy(c=>c.IndexSHA256,StringComparer.Ordinal).First();
        }
        public Selection QueryAll(Rect window,Limits limits,CancellationToken cancel=default)
        {
            Require(limits!=null&&limits.MaximumBuildings>=1&&limits.MaximumBuildings<=32768&&limits.MaximumBuildingPoints>0&&limits.MaximumBuildingPoints<=400000&&limits.MaximumSelectedBytes>0&&limits.MaximumSelectedBytes<=128L*1024*1024&&limits.MaximumRoofCost>0&&limits.MaximumRoofCost<=20000000,"Invalid explicit preparation limits");
            double metresX=(window.East-window.West)*111319.4908*Math.Cos((window.North+window.South)*.5*Math.PI/180),metresY=(window.North-window.South)*111319.4908;
            Require(metresX<=6000.1&&metresY<=6000.1,"Canonical preparation window exceeds 6 km; collection radius is persistent data, not one giant prepared world");
            var selected=new List<Leaf>();Query(spatial,window,selected);selected.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
            long bytes=0;foreach(var leaf in selected){bytes=checked(bytes+leaf.ManifestBytes+leaf.PayloadBytes);Require(bytes<=limits.MaximumSelectedBytes,"Complete selected shard bytes exceed preparation bound; choose smaller window, no leaves omitted");}
            var result=new List<Building>();var unique=new Dictionary<string,string>(StringComparer.Ordinal);var featureIds=new HashSet<string>(StringComparer.Ordinal);var notes=new List<string>();long roof=0,decodedRoof=0;int points=0,decodedPoints=0,decodedFeatures=0;
            foreach(var leaf in selected)
            {
                cancel.ThrowIfCancellationRequested();var meta=Object(Read(leaf.ManifestPath,256000,leaf.ManifestBytes,leaf.ManifestHash),32);var payload=Object(Read(leaf.PayloadPath,4000000,leaf.PayloadBytes,leaf.PayloadHash),12);
                Require(Text(meta,"format",64)=="EarthUnscriptedBuildings1"&&Text(meta,"id",100)==leaf.Id&&Text(meta,"revisionSeries",100)==leaf.Series&&Integer(meta,"revision",1,100000000)==leaf.Revision,"Leaf manifest identity/revision conflicts with immutable index");
                Require(Text(meta,"collectionId",100)==Id&&Integer(meta,"collectionRevision",1,100000000)<=Revision,"Leaf points at different collection revision");
                Require(Text(meta,"payload",40)=="buildings.json"&&Path.GetFileName(leaf.PayloadPath)=="buildings.json"&&Path.GetDirectoryName(leaf.PayloadPath)==Path.GetDirectoryName(leaf.ManifestPath),"Unexpected leaf payload binding");
                Require(Text(meta,"sha256",64)==leaf.PayloadHash&&Integer(meta,"bytes",1,4000000)==leaf.PayloadBytes&&Text(meta,"licence",64)=="OGL-3.0"&&Boolean(meta,"offlineAllowed")&&Boolean(meta,"peerRedistributionAllowed"),"Leaf hash/size/reuse permissions conflict");
                Require(Text(meta,"attribution",1200)==leaf.Attribution,"Attribution conflict");var declared=BoundsOf(meta["bounds"]);Require(Equal(declared,leaf.Bounds),"Leaf content bounds disagree with manifest");
                var buildings=payload["buildings"] as JArray;var lineage=meta["sourceLineage"] as JObject;Require(buildings!=null&&buildings.Count==leaf.FeatureCount&&buildings.Count<=4096&&lineage!=null&&lineage.Count==buildings.Count,"Leaf source/feature counts inconsistent");
                int leafPoints=0;
                foreach(var token in buildings)
                {
                    cancel.ThrowIfCancellationRequested();Require(token is JObject,"Invalid compiled building record");var obj=(JObject)token;string id=Text(obj,"id",100),kind=Text(obj,"kind",32),roofType=Text(obj,"roof",24);Require(SafeId(id)==id&&(kind=="building"||kind=="important_building"||kind=="residential")&&(roofType=="flat"||roofType=="gabled"),"Invalid building identity/class/roof");
                    double height=Number(obj["height"]);Require(height>=2&&height<=80,"Invalid building height");var ring=obj["points"] as JArray;Require(ring!=null&&ring.Count>=4&&ring.Count<=256,"Invalid building ring budget");leafPoints+=ring.Count;Require(leafPoints<=100000,"Leaf vertex budget");decodedPoints+=ring.Count;decodedRoof+=(long)ring.Count*ring.Count;Require(++decodedFeatures<=32768&&decodedPoints<=400000&&decodedRoof<=40000000,"Decoded complete-shard work budget exceeded; smaller window required");
                    var pp=new List<Point>(ring.Count);double west=180,south=90,east=-180,north=-90;
                    foreach(var p in ring){Require(p is JArray pair&&pair.Count==2,"Invalid point pair");double lon=Number(p[0]),lat=Number(p[1]);Require(declared.Contains(lon,lat),"Feature outside whole-content bounds");pp.Add(new Point(lon,lat));west=Math.Min(west,lon);east=Math.Max(east,lon);south=Math.Min(south,lat);north=Math.Max(north,lat);}
                    Require(pp[0].Longitude==pp[pp.Count-1].Longitude&&pp[0].Latitude==pp[pp.Count-1].Latitude,"Open building ring");ValidateRing(pp);var bounds=new Rect(west,south,east,north);var source=lineage[id] as JObject;Require(source!=null,"Missing unique source ownership");
                    string owner=Text(source,"ownerCell",100);Require(owner==leaf.Cell,"Owner cell differs from leaf tile");string key=string.Join("|",new[]{Text(source,"provider",100),Text(source,"sourceVersion",100),Text(source,"sourceFeatureId",100),Text(source,"stablePartKey",100)}.Select(v=>Convert.ToBase64String(Encoding.UTF8.GetBytes(v))));
                    string content=DataPackageRules.Hash(Encoding.UTF8.GetBytes(obj.ToString(Formatting.None)));
                    if(unique.TryGetValue(key,out var prior)){Require(prior==content,"Same source key has conflicting feature geometry");throw new InvalidDataException("Whole source feature is duplicated across immutable leaves; ownership index must be repaired, not silently deduplicated");}
                    unique.Add(key,content);Require(featureIds.Add(id),"Building ID reused across leaves");
                    if(!bounds.Intersects(window)||!FootprintIntersects(pp,window))continue;
                    result.Add(new Building(id,kind,roofType,(float)height,key,owner,new ReadOnlyCollection<Point>(pp),bounds));points=checked(points+pp.Count);roof=checked(roof+(long)pp.Count*pp.Count);
                }
            }
            // Per-layer truth is preserved. A selected tile does not imply surveyed
            // completeness, and unreviewed canopy/garages are never promoted here.
            foreach(var cell in cells)
            {
                Rect? content=cell["contentBoundsWGS84"] is JArray?BoundsOf(cell["contentBoundsWGS84"]):(Rect?)null;
                if(!content.HasValue||!content.Value.Intersects(window))continue;
                var buildings=cell["layers"]?["buildings"];notes.Add(Text(cell,"cellId",100)+":buildings="+(string)buildings?["status"]+";unsupported="+(string)buildings?["unsupportedCount"]+";canopy="+(string)cell["layers"]?["canopy"]?["status"]);
            }
            result.Sort((a,b)=>StringComparer.Ordinal.Compare(a.SourceKey,b.SourceKey));var problems=new List<string>();if(result.Count>limits.MaximumBuildings)problems.Add("Complete detail window has "+result.Count+" buildings; budget "+limits.MaximumBuildings+". Explicitly choose smaller window or validated higher profile; no top-N omission.");if(points>limits.MaximumBuildingPoints)problems.Add("Building vertex budget exceeded");if(roof>limits.MaximumRoofCost)problems.Add("Roof triangulation cost exceeded");
            return new Selection(Identity,selected,result,notes,problems,bytes,roof,points,!RuntimeActivationAllowed);
        }
        static bool FootprintIntersects(IReadOnlyList<Point> points,Rect window)
        {
            foreach(var p in points)if(window.Contains(p.Longitude,p.Latitude))return true;
            for(int i=0;i<points.Count-1;i++)
            {
                var a=points[i];var b=points[i+1];double lo=0,hi=1;
                bool Edge(double p,double q){if(Math.Abs(p)<1e-20)return q>=0;double t=q/p;if(p<0){if(t>hi)return false;lo=Math.Max(lo,t);}else{if(t<lo)return false;hi=Math.Min(hi,t);}return true;}
                if(Edge(a.Longitude-b.Longitude,a.Longitude-window.West)&&Edge(b.Longitude-a.Longitude,window.East-a.Longitude)&&Edge(a.Latitude-b.Latitude,a.Latitude-window.South)&&Edge(b.Latitude-a.Latitude,window.North-a.Latitude)&&hi>=lo)return true;
            }
            bool inside=false;for(int i=0,j=points.Count-2;i<points.Count-1;j=i++)
            {var a=points[i];var b=points[j];if((a.Latitude>window.South)!=(b.Latitude>window.South)&&window.West<(b.Longitude-a.Longitude)*(window.South-a.Latitude)/(b.Latitude-a.Latitude)+a.Longitude)inside=!inside;}
            return inside;
        }
        static void ValidateRing(IReadOnlyList<Point> points)
        {
            double area=0;for(int i=0;i<points.Count-1;i++){var a=points[i];var b=points[i+1];Require(a.Longitude!=b.Longitude||a.Latitude!=b.Latitude,"Repeated consecutive ring coordinate");area+=(a.Longitude-points[0].Longitude)*(b.Latitude-points[0].Latitude)-(b.Longitude-points[0].Longitude)*(a.Latitude-points[0].Latitude);}
            Require(Math.Abs(area)>1e-14,"Degenerate ring area");
            double Cross(Point a,Point b,Point c)=>(b.Longitude-a.Longitude)*(c.Latitude-a.Latitude)-(b.Latitude-a.Latitude)*(c.Longitude-a.Longitude);
            for(int i=0;i<points.Count-1;i++)for(int j=i+2;j<points.Count-1;j++)
            {if(i==0&&j==points.Count-2)continue;var a=points[i];var b=points[i+1];var c=points[j];var d=points[j+1];double ca=Cross(a,b,c),da=Cross(a,b,d),ac=Cross(c,d,a),bc=Cross(c,d,b);Require(!(ca*da<0&&ac*bc<0),"Self-crossing building ring");}
        }
        static Node Tree(Leaf[] values,int start,int count)
        {if(count==0)return null;if(count==1)return new Node{Value=values[start],Bounds=values[start].Bounds};int half=count/2;var left=Tree(values,start,half);var right=Tree(values,start+half,count-half);return new Node{Left=left,Right=right,Bounds=Rect.Union(left.Bounds,right.Bounds)};}
        static void Query(Node node,Rect window,List<Leaf> result){if(node==null||!node.Bounds.Intersects(window))return;if(node.Value!=null){result.Add(node.Value);return;}Query(node.Left,window,result);Query(node.Right,window,result);}
        static JObject Object(byte[] bytes,int depth)
        {using var reader=new JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes))){MaxDepth=depth,DateParseHandling=DateParseHandling.None};var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Require(!reader.Read(),"Trailing JSON content");return result;}
        byte[] Read(string relative,int maximum,long? expected,string hash)
        {
            string path=SafePath(relative);using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);Require(stream.Length>=1&&stream.Length<=maximum&&(!expected.HasValue||stream.Length==expected.Value),"Artifact size mismatch/budget");var bytes=new byte[(int)stream.Length];int at=0;while(at<bytes.Length){int n=stream.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}Require(stream.ReadByte()==-1&&DataPackageRules.Hash(bytes)==hash,"Artifact exact-byte hash mismatch");return bytes;
        }
        string SafePath(string relative)
        {Require(relative!=null&&relative.Length<=400&&!Path.IsPathRooted(relative)&&!relative.Contains('\\')&&!relative.Contains(':')&&!relative.Split('/').Any(p=>p.Length==0||p=="."||p==".."),"Unsafe collection artifact path");string path=Path.GetFullPath(Path.Combine(root,relative));Require(path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Artifact escapes collection");NoLinks(path);return path;}
        static void NoLinks(string path){var current=Path.GetPathRoot(path);foreach(string part in path.Substring(current.Length).Split(Path.DirectorySeparatorChar)){if(part.Length==0)continue;current=Path.Combine(current,part);Require((File.GetAttributes(current)&FileAttributes.ReparsePoint)==0,"Symlink/reparse artifacts are not accepted");}}
        static Rect BoundsOf(JToken value){Require(value is JArray a&&a.Count==4,"Missing bounds");return new Rect(Number(value[0]),Number(value[1]),Number(value[2]),Number(value[3]));}
        static bool Equal(Rect a,Rect b)=>a.West==b.West&&a.South==b.South&&a.East==b.East&&a.North==b.North;
        static bool Finite(double d)=>!double.IsNaN(d)&&!double.IsInfinity(d);
        static double Number(JToken value){Require(value!=null&&(value.Type==JTokenType.Float||value.Type==JTokenType.Integer),"Expected numeric field");double number=(double)value;Require(Finite(number),"Nonfinite number");return number;}
        static long Integer(JObject obj,string key,long min,long max){Require(obj[key]?.Type==JTokenType.Integer,"Expected integer "+key);long value=(long)obj[key];Require(value>=min&&value<=max,"Integer out of range "+key);return value;}
        static bool Boolean(JObject obj,string key){Require(obj[key]?.Type==JTokenType.Boolean,"Expected boolean "+key);return (bool)obj[key];}
        static string Text(JObject obj,string key,int max){Require(obj[key]?.Type==JTokenType.String,"Expected string "+key);string text=(string)obj[key];Require(!string.IsNullOrWhiteSpace(text)&&text.Length<=max,"String out of range "+key);return text;}
        static string SafeId(string id){Require(Regex.IsMatch(id,"^[A-Za-z0-9_-]{1,100}$"),"Unsafe identity");return id;}
        static string SHA(string hash){Require(Regex.IsMatch(hash??"","^[a-f0-9]{64}$"),"Invalid pinned SHA256");return hash;}
        static void Require(bool valid,string message){if(!valid)throw new InvalidDataException(message);}
    }
}
