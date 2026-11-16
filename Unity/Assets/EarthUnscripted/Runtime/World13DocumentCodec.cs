using System;using System.Collections.Generic;using System.Collections.Concurrent;using System.IO;using System.Linq;using System.Reflection;using System.Security.Cryptography;using System.Text;using Newtonsoft.Json;using UnityEngine;
namespace EarthUnscripted
{
    public static class World13DocumentCodec
    {
        public const int MaximumBytes=32*1024*1024;
        static readonly ConcurrentDictionary<Type,Dictionary<string,FieldInfo>> Fields=new ConcurrentDictionary<Type,Dictionary<string,FieldInfo>>();
        sealed class Budget
        {
            public int tokens,objects,numbers,points;public readonly Dictionary<string,int> totals=new Dictionary<string,int>();
            public void Add(string key,int maximum){if(key==null)return;totals.TryGetValue(key,out int n);Require(n<maximum,"aggregate array budget "+key);totals[key]=n+1;}
        }
        static void Require(bool b,string s){if(!b)throw new InvalidDataException("World13 document: "+s);}
        static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        public static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static bool Next(JsonTextReader r,Budget b)
        {
            if(!r.Read())return false;Require(++b.tokens<=6000000,"token budget");
            if(r.TokenType==JsonToken.StartObject)Require(++b.objects<=450000,"object budget");
            if(r.TokenType==JsonToken.PropertyName)Require(((string)r.Value).Length<=96,"property length");
            if(r.TokenType==JsonToken.String)Require(((string)r.Value).Length<=2048,"text length");
            if(r.TokenType==JsonToken.Integer||r.TokenType==JsonToken.Float){double n=Convert.ToDouble(r.Value,System.Globalization.CultureInfo.InvariantCulture);Require(++b.numbers<=2400000&&Finite(n)&&Math.Abs(n)<=100000000,"numeric budget/range");}
            Require(r.TokenType!=JsonToken.Comment&&r.TokenType!=JsonToken.Undefined&&r.TokenType!=JsonToken.StartConstructor,"unsupported JSON syntax");return true;
        }
        static Dictionary<string,FieldInfo> Map(Type t)=>Fields.GetOrAdd(t,k=>k.GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>!f.IsStatic&&!f.IsNotSerialized).ToDictionary(f=>f.Name,StringComparer.Ordinal));
        static int Limit(Type owner,string name,Type element,out string totalKey,out int totalMaximum)
        {
            totalKey=null;totalMaximum=int.MaxValue;
            if(element==typeof(Vector3)){totalKey=owner==typeof(GeoRailway)?"rail-points":"geo-points";totalMaximum=owner==typeof(GeoRailway)?65536:400000;}
            if(owner==typeof(GeoRoad)&&name=="points"){totalKey="source-road-points";totalMaximum=300000;return 10000;}
            if(owner==typeof(SourceRoad13Metadata)){totalKey="road-source-nodes";totalMaximum=300000;return 10000;}
            if(owner==typeof(RoadElevationProfileDto)||owner==typeof(RoadWidthProfileDto)){totalKey=owner.Name+"."+name;totalMaximum=80000;return 512;}
            if(owner==typeof(RoadJunctionPatchDto)&&name=="boundary"){totalKey="junction-boundaries";totalMaximum=65536;return 65;}
            if(owner==typeof(RoadJunctionPatchDto)&&name=="arms"){totalKey="junction-arms";totalMaximum=65536;return 32;}
            if(owner==typeof(RoadJunctionArmDto)&&name=="spans"){totalKey="junction-spans";totalMaximum=262144;return 64;}
            if(owner==typeof(RoadAuthorityMetadata13))return name=="syntheticAnchors"?1024:10000;
            if(owner==typeof(GeoRailway)){if(name=="nodeIds"){totalKey="rail-node-ids";totalMaximum=65536;}return name=="points"||name=="nodeIds"?4096:10000;}
            if(owner==typeof(GeoRegion))
            {
                switch(name){case "heights":case "landCover":return 513*513;case "roads":return 10000;case "waters":return 1000;case "areas":return 2000;case "buildings":return 2500;case "trees":return 16384;case "canopyCoverage":return 8;case "railways":return 2048;case "route":return 20000;case "cornerNames":case "cornerDistances":return 500;case "roadWidthProfiles":return 10000;}
            }
            if(owner==typeof(GeoBuilding)||owner==typeof(GeoArea))return 4096;
            if(owner==typeof(GeoCanopyCoverage)&&name=="boundary")return 129;
            return 10000;
        }
        static void Shape(JsonTextReader r,Type type,Budget b,Type owner=null,string name=null)
        {
            if(r.TokenType==JsonToken.Null){Require(type==typeof(string)||(owner==typeof(GeoRegion)&&(name=="landCover"||name=="cornerNames"||name=="cornerDistances")),"null required field "+name);return;}
            if(type==typeof(string)){Require(r.TokenType==JsonToken.String,"string type "+name);return;}
            if(type==typeof(bool)){Require(r.TokenType==JsonToken.Boolean,"boolean type "+name);return;}
            if(type==typeof(int)){Require(r.TokenType==JsonToken.Integer&&Convert.ToInt64(r.Value)>=int.MinValue&&Convert.ToInt64(r.Value)<=int.MaxValue,"integer type "+name);return;}
            if(type==typeof(float)||type==typeof(double)){Require(r.TokenType==JsonToken.Integer||r.TokenType==JsonToken.Float,"numeric type "+name);if(type==typeof(float)){double n=Convert.ToDouble(r.Value,System.Globalization.CultureInfo.InvariantCulture);float f=(float)n;Require(!float.IsInfinity(f)&&!(n!=0&&f==0),"float range/underflow "+name);}return;}
            if(type.IsArray)
            {
                Require(r.TokenType==JsonToken.StartArray,"array type "+name);int count=0,max=Limit(owner,name,type.GetElementType(),out string key,out int global);
                while(Next(r,b)&&r.TokenType!=JsonToken.EndArray){Require(++count<=max,"per-array budget "+name);b.Add(key,global);if(type.GetElementType()==typeof(Vector3))Require(++b.points<=400000,"all geographic point budget");Shape(r,type.GetElementType(),b);}
                Require(r.TokenType==JsonToken.EndArray,"unterminated array");return;
            }
            Require(r.TokenType==JsonToken.StartObject,"object type "+name);var fields=Map(type);var seen=new HashSet<string>(StringComparer.Ordinal);
            while(Next(r,b)&&r.TokenType!=JsonToken.EndObject)
            {
                Require(r.TokenType==JsonToken.PropertyName,"property name");string property=(string)r.Value;
                Require(fields.TryGetValue(property,out var field)&&seen.Add(property),"unknown/duplicate field "+property);Require(Next(r,b),"missing value");Shape(r,field.FieldType,b,type,property);
            }
            Require(r.TokenType==JsonToken.EndObject&&seen.Count==fields.Count,"missing required fields "+type.Name);
        }
        public static void Preflight(string json)
        {
            Require(json!=null&&Encoding.UTF8.GetByteCount(json)<=MaximumBytes,"byte budget");var budget=new Budget();
            using(var r=new JsonTextReader(new StringReader(json)){MaxDepth=16,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})
            {Require(Next(r,budget),"empty");Shape(r,typeof(World13Document),budget);Require(!Next(r,budget),"trailing data");}
        }
        static World13Document Read(byte[] bytes,World13ReadContext context,out GeoRoad[] source)
        {
            Require(bytes!=null&&bytes.Length<=MaximumBytes&&context!=null,"bounded captured input/context");var owned=(byte[])bytes.Clone();context.RequireDocumentHash(Hash(owned));
            string json=new UTF8Encoding(false,true).GetString(owned);Preflight(json);var d=WorldFieldJson.Read<World13Document>(json);
            Require(d.format=="EarthUnscriptedWorld13Document"&&d.version==1&&d.compatibility==context.ExpectedCompatibility,"format/version/compatibility");
            Require(d.roadAuthority.layoutRecipe==context.ExpectedLayoutRecipe,"recipe differs from captured origin");
            World13GeographyValidation.Validate(d.geography);source=SourceProjection(d);return d;
        }
        // Fresh projection validates source IDs without attaching anything to the
        // inspected geography. The source rows are keyed, never zipped by order.
        static GeoRoad[] SourceProjection(World13Document d)
        {
            var a=d.roadAuthority;var roads=d.geography.roads;
            Require(a.schema==0||a.schema==1,"authority schema");Require(a.sourceRoads.Length==roads.Length,"source-node row coverage");
            var rows=new Dictionary<string,string[]>(StringComparer.Ordinal);
            foreach(var row in a.sourceRoads){Require(row!=null&&RoadSourceIdentity.Identifier(row.roadId)&&rows.TryAdd(row.roadId,row.nodeIds),"duplicate/invalid source row");}
            var source=new GeoRoad[roads.Length];
            for(int i=0;i<roads.Length;i++)
            {
                var road=roads[i];Require(road!=null&&RoadSourceIdentity.Identifier(road.id),"invalid source road id");Require(rows.TryGetValue(road.id,out var nodes),"missing source road row");
                source[i]=new GeoRoad{id=road.id,start=road.start,end=road.end,name=road.name,kind=road.kind,width=road.width,maxVehicleHeight=road.maxVehicleHeight,limit=road.limit,oneWay=road.oneWay,layer=road.layer,bridge=road.bridge,tunnel=road.tunnel,points=(Vector3[])road.points.Clone(),sourceNodeIds=(string[])nodes.Clone()};
            }
            Require(RoadSourceIdentity.Compute(source)==a.sourceDigest,"source digest/raw float/node binding");return source;
        }
        static void RequireRaw(World13Document d,World13ReadContext context)
        {
            var g=d.geography;var a=d.roadAuthority;Require(!context.ExpectedAuthoritative&&a.schema==0,"verified raw phase required");
            Require(!g.preparedWidths&&g.roadWidthSchema==0&&g.roadWidthProfiles.Length==0&&!g.railwayProfilesPrepared,"legacy prepared controls are not raw evidence");
            Require(a.elevationProfiles.Length==0&&a.junctionPatches.Length==0&&a.continuations.Length==0&&a.syntheticAnchors.Length==0,"raw source has derived authority");
            Require(!g.roads.Any(r=>r.id=="fictional-service-approach"||r.id=="fictional-pit-lane"),"generated connector requires original-source reprepare");
            context.RequireValidatedPayload(d.compatibility,a.layoutRecipe,false);
        }
        static void AttachRawOwned(World13Document d,GeoRoad[] source)
        {
            // Only called for an exclusive newly decoded/clone-owned raw document.
            // No mesh/Prepare/solver; complete validation has already succeeded.
            var g=d.geography;var a=d.roadAuthority;
            for(int i=0;i<g.roads.Length;i++)g.roads[i].sourceNodeIds=source[i].sourceNodeIds;
            g.roadLayoutRecipe=a.layoutRecipe;g.roadSourceDigest=a.sourceDigest;g.roadElevationSchema=0;
            g.roadElevationProfiles=a.elevationProfiles;g.roadJunctionPatches=a.junctionPatches;g.roadContinuations=a.continuations;g.roadSyntheticAnchors=a.syntheticAnchors;
        }
        public static VerifiedRawWorld13Document ReadVerifiedRaw(byte[] bytes,World13ReadContext context)
        {var d=Read(bytes,context,out var source);RequireRaw(d,context);AttachRawOwned(d,source);string canonical=WorldFieldJson.Write(d);return new VerifiedRawWorld13Document(d,context,Hash(new UTF8Encoding(false,true).GetBytes(canonical)));}
        public static byte[] WriteVerifiedRaw(VerifiedRawWorld13Document raw)
        {
            Require(raw!=null,"raw owner");lock(raw.Sync)
            {
            Require(raw.Document!=null,"raw ownership transferred");var d=raw.Document;RequireRaw(d,raw.Context);SourceProjection(d);
            Require(d.geography.roadSourceDigest==d.roadAuthority.sourceDigest&&d.geography.roadLayoutRecipe==d.roadAuthority.layoutRecipe&&d.geography.roadElevationSchema==0,"attached metadata diverged");
            Require(d.geography.roadElevationProfiles.Length==0&&d.geography.roadJunctionPatches.Length==0&&d.geography.roadContinuations.Length==0&&d.geography.roadSyntheticAnchors.Length==0,"attached derived metadata diverged");
            var rows=d.roadAuthority.sourceRoads.ToDictionary(n=>n.roadId,StringComparer.Ordinal);foreach(var r in d.geography.roads)Require(r.sourceNodeIds.SequenceEqual(rows[r.id].nodeIds),"attached source nodes diverged");
            string json=WorldFieldJson.Write(d);Preflight(json);byte[] output=new UTF8Encoding(false,true).GetBytes(json);Require(Hash(output)==raw.SourceCanonicalSha,"verified raw fields mutated before handoff");return output;
            }
        }
        public static UnadmittedWorld13Document ReadCandidate(byte[] bytes,World13ReadContext context)
        {var d=Read(bytes,context,out _);Require(context.ExpectedAuthoritative&&d.roadAuthority.schema==1,"prepared candidate inspection phase");return new UnadmittedWorld13Document(d);}
        public static void CheckFrozenAuthorityForDiagnostics(UnadmittedWorld13Document candidate)
        {
            Require(candidate!=null,"candidate");var d=candidate.Document;var a=d.roadAuthority;var roads=SourceProjection(d);var widths=d.geography.roadWidthSchema==1?d.geography.roadWidthProfiles:null;
            RoadElevationProfileDto.Validate(a.elevationProfiles,roads,widths);RoadSyntheticAnchorDto.Validate(a.syntheticAnchors,roads,a.elevationProfiles,a.sourceDigest);
            var continuations=new RoadContinuationAuthority(roads,a.elevationProfiles,widths,a.syntheticAnchors,a.continuations);
            RoadJunctionPatchDto.Validate(a.junctionPatches,roads,a.elevationProfiles,widths,a.syntheticAnchors,continuations);
            RoadTopologyAuthority.Validate(roads,a.junctionPatches,a.syntheticAnchors,continuations);
            RoadJunctionAuthorityProof.Validate(a.junctionPatches,roads,a.elevationProfiles,widths,continuations);
        }
        public static GeoRegion DecodePrepared(byte[] bytes,World13ReadContext context)
        {
            var candidate=ReadCandidate(bytes,context);CheckFrozenAuthorityForDiagnostics(candidate);
            throw new NotSupportedException("World13 prepared attachment is UNADMITTED: frozen validators do not prove unique interior corner/cap/body/terrain safety. Full reviewed admission required.");
        }
    }
}
