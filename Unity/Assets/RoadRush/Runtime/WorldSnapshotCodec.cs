using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using UnityEngine;
using RoadRush.Core;
namespace RoadRush
{
    public static class WorldSnapshotCodec
    {
        // The raw schema is unchanged, but peers must share the derived height
        // recipe; older builds would otherwise accept the same map epoch while
        // rendering/driving on different bridge and terrain profiles.
        public static string Compatibility=>"RoadRush/"+Application.version+"/geometry15-groundedpaths-water1";
        public static string NetworkId(GeoRegion region)=>string.IsNullOrEmpty(region.networkMapId)?region.id:region.networkMapId;
        public static string CanonicalJson(GeoRegion region)
        {
            if(!region.preparedWidths)throw new InvalidDataException("World snapshot must contain prepared driving widths");
            var copy=region.SnapshotCopy();copy.id=copy.networkMapId=NetworkId(region);return JsonUtility.ToJson(copy);
        }
        public static string Epoch(GeoRegion region)=>WorldEpoch.Compute(Compatibility,NetworkId(region),CanonicalJson(region));
        static readonly HashSet<string> RootFields=new HashSet<string>(("id name attribution countryCode timeZone datasetVersion datasetSource networkMapId preparedWidths drivingSide circuit referenceLength cornerNames cornerDistances latitude longitude datum minX minZ width depth nx nz landNx landNz heights landCover roads waters areas buildings trees canopyCoverage route railways railwayProfilesPrepared railwaySourceIdentity railwaySourceComplete roadWidthSchema roadWidthProfiles").Split(' '));
        static void Require(bool value,string message){if(!value)throw new InvalidDataException("Invalid peer world: "+message);}
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        static void CheckJson(string json)
        {
            Require(json!=null&&Encoding.UTF8.GetByteCount(json)<=DataPackageRules.MaximumFileBytes,"payload size");
            using(var reader=new JsonTextReader(new StringReader(json)){MaxDepth=16,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})
            {
                var roots=new HashSet<string>();int tokens=0,objects=0,numbers=0;bool began=false;
                while(reader.Read())
                {
                    Require(++tokens<=6000000,"token budget");
                    if(!began){Require(reader.TokenType==JsonToken.StartObject,"object root");began=true;}
                    if(reader.TokenType==JsonToken.StartObject)Require(++objects<=450000,"object budget");
                    if(reader.TokenType==JsonToken.PropertyName)
                    {
                        string key=(string)reader.Value;Require(key.Length<=96,"property length");
                        if(reader.Depth==1)Require(RootFields.Contains(key)&&roots.Add(key),"unknown or duplicate root field");
                    }
                    if(reader.TokenType==JsonToken.String)Require(((string)reader.Value).Length<=2048,"text length");
                    if(reader.TokenType==JsonToken.Integer||reader.TokenType==JsonToken.Float)
                    {double value=Convert.ToDouble(reader.Value,System.Globalization.CultureInfo.InvariantCulture);Require(++numbers<=2400000&&Finite(value)&&Math.Abs(value)<=100000000,"numeric budget/range");}
                    Require(reader.TokenType!=JsonToken.Comment&&reader.TokenType!=JsonToken.Undefined&&reader.TokenType!=JsonToken.StartConstructor,"unsupported JSON syntax");
                }
                Require(began,"empty payload");
            }
        }
        // A second streaming pass validates the serialized DTO shape without allocating a DOM.
        // Unity's permissive JSON coercion/unknown-field skipping must not define the peer protocol.
        static void CheckShape(string json)
        {
            using(var reader=new JsonTextReader(new StringReader(json)){MaxDepth=16,DateParseHandling=DateParseHandling.None})
            {Require(reader.Read(),"empty shape");Shape(reader,typeof(GeoRegion),new ShapeBudget());Require(!reader.Read(),"trailing JSON");}
        }
        static readonly ConcurrentDictionary<Type,Dictionary<string,Type>> Shapes=new ConcurrentDictionary<Type,Dictionary<string,Type>>();
        sealed class ShapeBudget {public int railPoints;public readonly int[] widthControls=new int[3];}
        static void Shape(JsonTextReader reader,Type type,ShapeBudget budget,int maximum=400000,bool railPoints=false,int widthControl=0)
        {
            if(reader.TokenType==JsonToken.Null){Require(!type.IsValueType,"null value type");return;}
            if(type==typeof(string)){Require(reader.TokenType==JsonToken.String,"string type");return;}
            if(type==typeof(bool)){Require(reader.TokenType==JsonToken.Boolean,"boolean type");return;}
            if(type==typeof(int)){Require(reader.TokenType==JsonToken.Integer&&Convert.ToInt64(reader.Value)>=int.MinValue&&Convert.ToInt64(reader.Value)<=int.MaxValue,"integer type/range");return;}
            if(type==typeof(float)||type==typeof(double)){Require(reader.TokenType==JsonToken.Integer||reader.TokenType==JsonToken.Float,"numeric type");return;}
            if(type.IsArray)
            {
                Require(reader.TokenType==JsonToken.StartArray,"array type");int count=0;
                while(reader.Read()&&reader.TokenType!=JsonToken.EndArray){Require(++count<=maximum,"array budget");if(railPoints)Require(++budget.railPoints<=RailwayMetrics.MaximumTotalPoints,"railway total point budget");if(widthControl>0)Require(++budget.widthControls[widthControl-1]<=80000,"width total control budget");Shape(reader,type.GetElementType(),budget);}
                Require(reader.TokenType==JsonToken.EndArray,"array closure");return;
            }
            Require(reader.TokenType==JsonToken.StartObject,"object type");
            var fields=Shapes.GetOrAdd(type,t=>
            {
                var result=new Dictionary<string,Type>();foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance))if(!f.IsNotSerialized)result.Add(f.Name,f.FieldType);return result;
            });
            var seen=new HashSet<string>();
            while(reader.Read()&&reader.TokenType!=JsonToken.EndObject)
            {
                Require(reader.TokenType==JsonToken.PropertyName,"object property");string key=(string)reader.Value;
                Require(seen.Add(key)&&fields.TryGetValue(key,out _),"unknown or duplicate typed field");Require(reader.Read(),"property value");
                int fieldMaximum=type==typeof(RoadWidthProfileDto)&&(key=="distances"||key=="widths"||key=="derivatives")?512:type==typeof(GeoRegion)&&key=="roadWidthProfiles"?10000:type==typeof(GeoRailway)&&(key=="points"||key=="nodeIds")?RailwayMetrics.MaximumPointsPerWay:type==typeof(GeoRegion)&&key=="railways"?RailwayMetrics.MaximumWays:400000;
                Shape(reader,fields[key],budget,fieldMaximum,type==typeof(GeoRailway)&&key=="points",type==typeof(RoadWidthProfileDto)?key=="distances"?1:key=="widths"?2:key=="derivatives"?3:0:0);
            }
            Require(reader.TokenType==JsonToken.EndObject,"object closure");
            if(type==typeof(Vector3))Require(seen.Count==3,"complete vector");
            if(type==typeof(RoadWidthProfileDto))foreach(string required in new[]{"roadId","bevelBoundary","distances","widths","derivatives"})Require(seen.Contains(required),"missing width profile field "+required);
            if(type==typeof(GeoRailway))foreach(string required in new[]{"id","start","end","kind","width","gauge","tracks","layer","bridge","tunnel","estimatedProfile","points","nodeIds"})Require(seen.Contains(required),"missing railway field "+required);
        }
        static bool RailIdentifier(string value)
        {if(string.IsNullOrEmpty(value)||value.Length>128)return false;foreach(char c in value)if(char.IsControl(c)||char.IsWhiteSpace(c))return false;return true;}
        public static GeoRegion Decode(string json,string expectedMap,string expectedEpoch,string compatibility=null)
        {
            CheckJson(json);CheckShape(json);var region=JsonUtility.FromJson<GeoRegion>(json);Validate(region);
            Require(NetworkId(region)==expectedMap&&WorldEpoch.Matches(WorldEpoch.Compute(compatibility??Compatibility,NetworkId(region),CanonicalJson(region)),expectedEpoch),"canonical map identity/content epoch");
            region.id="download-peer-"+expectedEpoch.ToLowerInvariant();region.networkMapId=expectedMap;region.Prepare();
            foreach(var point in region.route){var projection=region.ProjectRoad(point);Require(projection.Road!=null&&projection.Separation<projection.Width+20,"route outside driving network");}
            return region;
        }
        public static GeoRegion Import(string path,DataFileManifest manifest,string trustedFingerprint)
        {
            Require(manifest!=null&&manifest.kind=="region-snapshot"&&DataPackageRules.VerifySignature(manifest,trustedFingerprint)&&DataPackageRules.FileMatches(path,manifest),"signed snapshot header/content");
            var region=Decode(File.ReadAllText(path,new UTF8Encoding(false,true)),manifest.fromVersion,manifest.toVersion);
            RegionCache.Save(region,"Host-prepared geographic snapshot, not raw OSM XML/JSON.\nCanonical network map: "+region.networkMapId+"\nWorld epoch: "+manifest.toVersion+"\n"+region.attribution+"\nSource: "+region.datasetSource);return region;
        }
        // Bounded boot/source decode validates coordinates and index costs BEFORE
        // any Prepare. This is format validation, not publisher/bundle authority.
        public static GeoRegion DecodeBootSource(string json,out bool authoritative)
        {
            CheckJson(json);CheckShape(json);var region=JsonUtility.FromJson<GeoRegion>(json);
            authoritative=region!=null&&(region.roadWidthSchema!=0||(region.id??"").StartsWith("download-peer-",StringComparison.Ordinal));
            if(!authoritative)RawWaterwayBoundary.ClipOwned(region);
            Validate(region,!authoritative);
            if(!authoritative&&!region.preparedWidths)
            {
                foreach(var road in region.roads)road.width=region.circuit?road.width*1.35f:Mathf.Max(road.kind=="service"?6.5f:8.5f,road.width*1.4f);
                region.preparedWidths=true;Validate(region,true);
            }
            return region;
        }
        static void Validate(GeoRegion r,bool rawSource=false)
        {
            Require(r!=null&&DataPackageRules.SafeId(r.id)&&DataPackageRules.SafeId(NetworkId(r))&&(r.preparedWidths||rawSource),"identity/prepared-width format");
            Require(Finite(r.latitude)&&Finite(r.longitude)&&Math.Abs(r.latitude)<=85&&Math.Abs(r.longitude)<=180,"geographic origin");
            Require(r.drivingSide==-1||r.drivingSide==1,"driving side");Require(r.countryCode==null||r.countryCode.Length==0||System.Text.RegularExpressions.Regex.IsMatch(r.countryCode,"^[A-Z]{2}$"),"country code");
            Require(Finite(r.width)&&Finite(r.depth)&&r.width>=100&&r.width<=10000&&r.depth>=100&&r.depth<=10000&&Finite(r.minX)&&Finite(r.minZ)&&Math.Abs(r.minX)<50000&&Math.Abs(r.minZ)<50000,"world bounds");
            Require(Finite(r.datum)&&Math.Abs(r.datum)<20000&&r.nx>=2&&r.nx<=513&&r.nz>=2&&r.nz<=513&&r.heights!=null&&r.heights.Length==r.nx*r.nz,"terrain grid");
            foreach(float h in r.heights)Require(Finite(h)&&Math.Abs(h)<20000,"terrain elevation");
            Require(r.landNx>=0&&r.landNx<=513&&r.landNz>=0&&r.landNz<=513&&(r.landCover?.Length??0)==r.landNx*r.landNz,"land-cover grid");
            if(r.landCover!=null)foreach(int value in r.landCover)Require(value>=0&&value<=4,"land-cover class");
            Require(r.roads!=null&&r.roads.Length>0&&r.roads.Length<=10000&&r.waters!=null&&r.waters.Length<=1000&&r.areas!=null&&r.areas.Length<=2000&&r.buildings!=null&&r.buildings.Length<=2500,"feature counts");
            int points=0;long indexCost=0;
            void Points(Vector3[] values,int minimum,int maximum)
            {
                Require(values!=null&&values.Length>=minimum&&values.Length<=maximum,"feature point count");points+=values.Length;Require(points<=400000,"geometry budget");
                foreach(var p in values)Require(Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&p.x>=r.minX-RawWaterwayBoundary.GeometryMarginMetres&&p.x<=r.minX+r.width+RawWaterwayBoundary.GeometryMarginMetres&&p.z>=r.minZ-RawWaterwayBoundary.GeometryMarginMetres&&p.z<=r.minZ+r.depth+RawWaterwayBoundary.GeometryMarginMetres&&Math.Abs(p.y)<20000,"geometry coordinate");
            }
            void Index(Vector3[] values)
            {
                for(int i=1;i<values.Length;i++){indexCost+=(long)(Math.Abs(values[i].x-values[i-1].x)/80+4)*(long)(Math.Abs(values[i].z-values[i-1].z)/80+4);Require(indexCost<=2000000,"spatial-index budget");}
            }
            foreach(var road in r.roads){Require(road!=null&&!string.IsNullOrEmpty(road.start)&&!string.IsNullOrEmpty(road.end)&&Finite(road.width)&&road.width>=(rawSource&&!r.preparedWidths?.1f:2f)&&road.width<=80&&road.limit>=1&&road.limit<=200&&road.oneWay>=-1&&road.oneWay<=1&&road.layer>=-20&&road.layer<=20&&Finite(road.maxVehicleHeight)&&(road.maxVehicleHeight==0||road.maxVehicleHeight>=.5f&&road.maxVehicleHeight<=20),"road metadata");Points(road.points,2,10000);Index(road.points);}
            Require(r.roadWidthSchema==0||r.roadWidthSchema==1,"road width schema");
            if(r.roadWidthSchema==0)Require((r.roadWidthProfiles?.Length??0)==0,"legacy world cannot carry unprepared width controls");
            else RoadWidthProfileDto.Validate(r.roadWidthProfiles,r.roads);
            Require(r.railways!=null&&r.railways.Length<=RailwayMetrics.MaximumWays&&(rawSource||r.railways.Length==0||r.railwayProfilesPrepared),"railway counts/prepared profiles");
            Require((r.railwaySourceIdentity?.Length??0)<=256&&(!r.railwaySourceComplete||!string.IsNullOrWhiteSpace(r.railwaySourceIdentity)),"railway source provenance bounds");
            int railPoints=0;var railIds=new HashSet<string>();
            foreach(var rail in r.railways)
            {
                Require(rail!=null&&RailIdentifier(rail.id)&&railIds.Add(rail.id)&&RailIdentifier(rail.start)&&RailIdentifier(rail.end)&&RailwayMetrics.Supported(rail.kind),"railway identity/lifecycle");
                Require(Finite(rail.width)&&rail.width>=.5f&&rail.width<=100&&Finite(rail.gauge)&&rail.gauge>=.3f&&rail.gauge<=3&&rail.tracks>=1&&rail.tracks<=16&&rail.width>=rail.gauge+(rail.tracks-1)*RailwayMetrics.TrackSpacing&&rail.layer>=-20&&rail.layer<=20&&!(rail.bridge&&rail.tunnel),"railway dimensions/layer");
                Require((rail.name?.Length??0)<=256&&(rail.operatorName?.Length??0)<=256&&(rail.electrified?.Length??0)<=64,"railway text bounds");
                Points(rail.points,2,RailwayMetrics.MaximumPointsPerWay);railPoints+=rail.points.Length;Require(railPoints<=RailwayMetrics.MaximumTotalPoints,"railway total point budget");
                Require(rail.nodeIds!=null&&rail.nodeIds.Length==rail.points.Length&&rail.nodeIds[0]==rail.start&&rail.nodeIds[rail.nodeIds.Length-1]==rail.end,"railway node alignment/endpoints");
                for(int i=0;i<rail.nodeIds.Length;i++){Require(RailIdentifier(rail.nodeIds[i]),"railway node identity");if(i>0)Require(rail.nodeIds[i]!=rail.nodeIds[i-1]&&(rail.points[i]-rail.points[i-1]).sqrMagnitude>.000001f,"railway degenerate segment");}
            }
            foreach(var water in r.waters){Require(water!=null&&Finite(water.width)&&water.width>=0&&water.width<=500,"water metadata");Points(water.points,2,10000);Index(water.points);}
            foreach(var area in r.areas){Require(area!=null,"area metadata");Points(area.points,3,4096);}
            long roofCost=0;foreach(var building in r.buildings){Require(building!=null&&Finite(building.height)&&building.height>=0&&building.height<=500,"building metadata");Points(building.points,3,4096);roofCost+=(long)building.points.Length*building.points.Length;Require(roofCost<=20000000,"roof triangulation budget");}
            Require(r.trees!=null&&r.trees.Length<=16384&&r.canopyCoverage!=null&&r.canopyCoverage.Length<=8,"canopy counts");
            var coverIds=new HashSet<string>();var hashes=new HashSet<string>();int accepted=0;
            foreach(var c in r.canopyCoverage)
            {
                Require(c!=null&&System.Text.RegularExpressions.Regex.IsMatch(c.id??"","^[a-zA-Z0-9_-]{1,100}$")&&coverIds.Add(c.id)&&System.Text.RegularExpressions.Regex.IsMatch(c.sourceHash??"","^[a-f0-9]{64}$")&&hashes.Add(c.sourceHash),"canopy coverage identity");
                Require(Finite(c.minX)&&Finite(c.maxX)&&Finite(c.minZ)&&Finite(c.maxZ)&&c.minX<c.maxX&&c.minZ<c.maxZ&&c.minX>=r.minX&&c.maxX<=r.minX+r.width&&c.minZ>=r.minZ&&c.maxZ<=r.minZ+r.depth,"canopy coverage bounds");
                Require(c.candidateCount>=0&&c.candidateCount<=8192&&c.acceptedCount>=0&&c.clearanceRejectedCount>=0&&c.clippedCount>=0&&c.overlapRejectedCount>=0&&c.densityRejectedCount>=0&&(long)c.acceptedCount+c.clearanceRejectedCount+c.clippedCount+c.overlapRejectedCount+c.densityRejectedCount==c.candidateCount,"canopy audit counts");accepted+=c.acceptedCount;Require(c.boundary!=null&&c.boundary.Length>=4&&c.boundary.Length<=129,"canopy perimeter budget");foreach(var p in c.boundary)Require(Finite(p.x)&&Finite(p.y)&&p.y==0&&Finite(p.z)&&p.x>=c.minX-.001f&&p.x<=c.maxX+.001f&&p.z>=c.minZ-.001f&&p.z<=c.maxZ+.001f,"canopy perimeter bounds");Require(c.boundary[0]==c.boundary[c.boundary.Length-1],"canopy perimeter closure");
            }
            Require(accepted==r.trees.Length,"canopy accepted audit total");var treeIds=new HashSet<string>();
            foreach(var tree in r.trees)
            {
                Require(tree!=null&&System.Text.RegularExpressions.Regex.IsMatch(tree.id??"","^[a-zA-Z0-9_-]{1,100}$")&&treeIds.Add(tree.id)&&Finite(tree.x)&&Finite(tree.z)&&Finite(tree.height)&&tree.height>=4&&tree.height<=45&&Finite(tree.crownRadius)&&tree.crownRadius>=1.6f&&tree.crownRadius<=5&&tree.seed==DetailCanopyPacks.Seed(tree.id),"canopy tree metadata");
                Require(Array.Exists(r.canopyCoverage,c=>DetailCanopyPacks.Contains(c,tree.x,tree.z,tree.crownRadius)),"canopy tree coverage");
            }
            DetailCanopyPacks.ValidateCounts(r.trees);
            Points(r.route,2,20000);float length=0;for(int i=1;i<r.route.Length;i++)length+=Vector3.Distance(r.route[i-1],r.route[i]);Require(length>=20,"usable route");
            Require((r.cornerNames?.Length??0)<=500&&(r.cornerDistances?.Length??0)<=500&&Finite(r.referenceLength)&&r.referenceLength>=0,"circuit metadata");
            if(rawSource)return;
            r.Prepare();if(r.trees.Length>0)
            {
                var pavement=RoadWidthWorldPreparation.PrepareSurfacesOwned(r,System.Threading.CancellationToken.None);
                var canopyClearance=new DetailCanopyPacks.Clearance(r,pavement);foreach(var tree in r.trees)Require(canopyClearance.Allows(tree),"canopy road/building/water/service/spawn clearance");
            }

        }
    }
}
