using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using EarthUnscripted.Core;
using UnityEngine;
using NativeWorld = EarthUnscripted.NewbridgePrototype.NewbridgeWorld;

namespace EarthUnscripted
{
    // Native worlds share their actual numeric geometry, rather than asking a
    // peer's legacy terrain/bridge solvers to reinterpret a gameplay facade.
    // This separate envelope leaves all legacy canonical JSON bytes unchanged.
    public static class AutomaticSharedSnapshot
    {
        public const string Format="EarthUnscriptedAutomaticShared1";
        [Serializable] public sealed class Envelope
        {
            public string format=Format,mapId,name,attribution,countryCode,timeZone,liveRegionId;
            public double latitude,longitude;
            public float datum;
            public int drivingSide;
            public Vector3 start,forward;
            public NativeWorld.Map[] maps;
            public NativeWorld.DriveRoute guide;
            public NativeWorld.FarPatch far;
        }
        sealed class Budget {public int objects,numbers;public long cells;}
        static readonly ConcurrentDictionary<Type,Dictionary<string,Type>> shapes=new ConcurrentDictionary<Type,Dictionary<string,Type>>();
        static void Require(bool condition,string reason)
        {if(!condition)throw new InvalidDataException("Invalid shared automatic world: "+reason);}
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        public static bool IsEnvelope(string text)=>text!=null && text.TrimStart().StartsWith("{\"format\":\""+Format+"\"",StringComparison.Ordinal);

        public static Envelope DecodeEnvelope(string text)
        {
            Require(IsEnvelope(text)&&Encoding.UTF8.GetByteCount(text)<=DataPackageRules.MaximumFileBytes,"format/byte budget");
            using(var reader=new JsonTextReader(new StringReader(text)){MaxDepth=16,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})
            {
                Require(reader.Read(),"empty payload");Shape(reader,typeof(Envelope),new Budget());Require(!reader.Read(),"trailing JSON");
            }
            var envelope=JsonUtility.FromJson<Envelope>(text);Validate(envelope);return envelope;
        }

        static void Shape(JsonTextReader reader,Type type,Budget budget)
        {
            if(reader.TokenType==JsonToken.Null){Require(!type.IsValueType,"null value type");return;}
            if(type==typeof(string))
            {Require(reader.TokenType==JsonToken.String&&((string)reader.Value).Length<=2048,"text type/budget");return;}
            if(type==typeof(bool)){Require(reader.TokenType==JsonToken.Boolean,"boolean type");return;}
            if(type==typeof(int)||type==typeof(float)||type==typeof(double))
            {
                Require(reader.TokenType==JsonToken.Integer || type!=typeof(int)&&reader.TokenType==JsonToken.Float,"numeric type");
                double number=Convert.ToDouble(reader.Value,System.Globalization.CultureInfo.InvariantCulture);
                Require(++budget.numbers<=2400000&&Finite(number)&&Math.Abs(number)<=int.MaxValue,"numeric budget/range");return;
            }
            if(type.IsArray)
            {
                Require(reader.TokenType==JsonToken.StartArray,"array type");int count=0;
                while(reader.Read()&&reader.TokenType!=JsonToken.EndArray)
                {Require(++count<=400000,"array budget");Shape(reader,type.GetElementType(),budget);}
                Require(reader.TokenType==JsonToken.EndArray,"array closure");return;
            }
            Require(reader.TokenType==JsonToken.StartObject&&++budget.objects<=450000,"object type/budget");
            var fields=shapes.GetOrAdd(type,t=>
            {
                var result=new Dictionary<string,Type>();
                foreach(var field in t.GetFields(BindingFlags.Public|BindingFlags.Instance))if(!field.IsNotSerialized)result.Add(field.Name,field.FieldType);
                return result;
            });
            var seen=new HashSet<string>();
            while(reader.Read()&&reader.TokenType!=JsonToken.EndObject)
            {
                Require(reader.TokenType==JsonToken.PropertyName,"object property");string key=(string)reader.Value;
                Require(seen.Add(key)&&fields.ContainsKey(key),"unknown/duplicate field");Require(reader.Read(),"property value");Shape(reader,fields[key],budget);
            }
            Require(reader.TokenType==JsonToken.EndObject,"object closure");
            if(type==typeof(Vector3))Require(seen.Count==3,"complete vector");
        }

        public static void Validate(Envelope data)
        {
            Require(data!=null&&data.format==Format&&DataPackageRules.SafeId(data.mapId),"identity");
            Require(Finite(data.latitude)&&Math.Abs(data.latitude)<=85&&Finite(data.longitude)&&Math.Abs(data.longitude)<=180,"origin");
            Require(Finite(data.datum)&&Math.Abs(data.datum)<20000&&(data.drivingSide==-1||data.drivingSide==1),"datum/driving side");
            Require(data.maps!=null&&data.maps.Length>0&&data.maps.Length<=4,"bounded shared area count");
            var tiles=new HashSet<Vector2Int>();bool startOwned=false;var budget=new Budget();
            foreach(var map in data.maps)
            {
                Require(map!=null&&map.format=="LiveWorldNumeric1"&&map.frame!=null,"numeric map schema");
                Require(tiles.Add(new Vector2Int(map.tileX,map.tileZ)),"duplicate core");
                Require(Finite(map.coreMinX)&&Finite(map.coreMinZ)&&Math.Abs(map.coreMinX)<20000000&&Math.Abs(map.coreMinZ)<20000000&&
                    Mathf.Abs(map.coreMinX-map.tileX*1536f)<.01f&&Mathf.Abs(map.coreMinZ-map.tileZ*1536f)<.01f&&
                    Mathf.Abs(map.coreMaxX-map.coreMinX-1536)<.01f&&Mathf.Abs(map.coreMaxZ-map.coreMinZ-1536)<.01f,"core bounds");
                Require(map.minX==map.coreMinX&&map.minZ==map.coreMinZ&&map.maxX==map.coreMaxX&&map.maxZ==map.coreMaxZ&&
                    map.nx>=2&&map.nx<=513&&map.nz>=2&&map.nz<=513&&map.heights!=null&&map.heights.Length==map.nx*map.nz&&
                    Finite(map.gridStep)&&map.gridStep>0&&Mathf.Abs((map.nx-1)*map.gridStep-1536)<.01f&&Mathf.Abs((map.nz-1)*map.gridStep-1536)<.01f,"terrain grid");
                foreach(float height in map.heights)Require(Finite(height)&&Math.Abs(height)<20000,"terrain height");
                Require(Finite(map.frame.height_origin)&&Math.Abs(map.frame.height_origin)<20000,"map height origin");
                Require(map.features!=null&&map.features.Length<=20000&&(map.buildings?.Length??0)<=12000&&(map.trees?.Length??0)<=160000,"map feature budgets");
                foreach(var feature in map.features)
                {
                    Require(feature!=null&&(feature.category=="road"||feature.category=="rail"||feature.category=="water"||feature.category=="path")&&
                        !string.IsNullOrEmpty(feature.id)&&!string.IsNullOrEmpty(feature.sourceId)&&!string.IsNullOrEmpty(feature.kind)&&
                        Finite(feature.width)&&feature.width>.05f&&feature.width<=200&&feature.layer>=-20&&feature.layer<=20&&
                        Finite(feature.travelWidth)&&feature.travelWidth>=0&&feature.travelWidth<=feature.width&&
                        Finite(feature.roadCentreOffset)&&Math.Abs(feature.roadCentreOffset)<=feature.width&&
                        Finite(feature.shoulderLeft)&&feature.shoulderLeft>=0&&feature.shoulderLeft<=40&&
                        Finite(feature.shoulderRight)&&feature.shoulderRight>=0&&feature.shoulderRight<=40&&feature.laneCount>=0&&feature.laneCount<=24&&
                        Finite(feature.speedLimitMps)&&feature.speedLimitMps>=0&&feature.speedLimitMps<=100&&
                        Finite(feature.tunnelClearance)&&feature.tunnelClearance>=0&&feature.tunnelClearance<=30,"corridor metadata");
                    Points(feature.points,map,2,budget,true);
                    Require(feature.sourceNodeIds==null||feature.sourceNodeIds.Length==feature.points.Length,"source node count");
                    foreach(var node in feature.sourceNodeIds??Array.Empty<string>())Require(!string.IsNullOrEmpty(node),"source node identity");
                }
                foreach(var house in map.buildings??Array.Empty<NativeWorld.Building>())
                {
                    Require(house!=null&&Finite(house.height)&&house.height>=0&&house.height<=300&&Finite(house.roofHeight)&&house.roofHeight>=0&&house.roofHeight<=60,"building metadata");
                    Points(house.points,map,3,budget,false);
                }
                foreach(var tree in map.trees??Array.Empty<NativeWorld.TreeSeed>())
                    Require(tree!=null&&Finite(tree.x)&&Finite(tree.z)&&Finite(tree.y)&&Math.Abs(tree.y)<20000&&
                        tree.x>=map.coreMinX-768&&tree.x<=map.coreMaxX+768&&tree.z>=map.coreMinZ-768&&tree.z<=map.coreMaxZ+768&&
                        Finite(tree.height)&&tree.height>0&&tree.height<=80&&Finite(tree.crownRadius)&&tree.crownRadius>0&&tree.crownRadius<=20&&tree.seed>=0,"tree estimate");
                startOwned|=data.start.x>=map.coreMinX&&data.start.x<map.coreMaxX&&data.start.z>=map.coreMinZ&&data.start.z<map.coreMaxZ;
            }
            Require(startOwned&&Finite(data.start.y)&&Math.Abs(data.start.y)<20000&&Finite(data.forward.x)&&Finite(data.forward.z)&&
                Vector3.ProjectOnPlane(data.forward,Vector3.up).sqrMagnitude>.5f,"supported start");
            Require(data.guide!=null&&data.guide.points!=null&&data.guide.points.Length>=2&&data.guide.points.Length<=10000,"guide");
            foreach(var point in data.guide.points)Require(Finite(point.x)&&Finite(point.y)&&Finite(point.z)&&Math.Abs(point.x)<20000000&&Math.Abs(point.z)<20000000&&Math.Abs(point.y)<20000,"guide point");
            if(data.far!=null)
            {
                var far=data.far;
                Require(Finite(far.minX)&&Finite(far.minZ)&&Math.Abs(far.minX)<20000000&&Math.Abs(far.minZ)<20000000&&Finite(far.step)&&far.step>=16&&far.step<=512&&far.nx>=2&&far.nx<=513&&far.nz>=2&&far.nz<=513&&
                    (far.nx-1)*far.step<=65536&&(far.nz-1)*far.step<=65536&&far.heights!=null&&far.heights.Length==far.nx*far.nz&&
                    Finite(far.verticalOffset)&&Math.Abs(far.verticalOffset)<20000,"distant terrain grid");
                foreach(float height in far.heights)Require(Finite(height)&&Math.Abs(height)<20000,"distant terrain height");
            }
        }
        static void Points(Vector3[] points,NativeWorld.Map map,int minimum,Budget budget,bool index)
        {
            Require(points!=null&&points.Length>=minimum&&points.Length<=10000,"point budget");
            foreach(var point in points)Require(Finite(point.x)&&Finite(point.y)&&Finite(point.z)&&Math.Abs(point.y)<20000&&
                point.x>=map.coreMinX-4096&&point.x<=map.coreMaxX+4096&&point.z>=map.coreMinZ-4096&&point.z<=map.coreMaxZ+4096,"point coordinate");
            if(index)for(int i=1;i<points.Length;i++)
            {
                budget.cells+=(long)(Math.Abs(points[i].x-points[i-1].x)/64+4)*(long)(Math.Abs(points[i].z-points[i-1].z)/64+4);
                Require(budget.cells<=2000000,"spatial index budget");
            }
        }
        public static string CanonicalJson(Envelope data)
        {Validate(data);string text=JsonUtility.ToJson(data);DecodeEnvelope(text);return text;}
        public static GeoRegion Region(Envelope data)
        {
            string text=CanonicalJson(data);
            string epoch=WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,data.mapId,text);
            return new GeoRegion{id="download-peer-"+epoch,networkMapId=data.mapId,name=data.name,attribution=data.attribution,
                countryCode=data.countryCode,timeZone=data.timeZone,latitude=data.latitude,longitude=data.longitude,datum=data.datum,
                drivingSide=data.drivingSide,preparedWidths=true,SharedAutomatic=data,route=data.guide.points,
                roads=Array.Empty<GeoRoad>(),waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>()};
        }
    }
}
