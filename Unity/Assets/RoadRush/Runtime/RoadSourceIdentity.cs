using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace RoadRush
{
    // Geometry13 source commitment v2: every serialized source-road field,
    // including immutable raw width. No derived heights/runtime indexes enter it.
    // BinaryWriter writes specified little-endian IEEE754 float bits and UTF8
    // strings prefixed with fixed int32 byte lengths, never locale formatting.
    public static class RoadSourceIdentity
    {
        public static string Compute(IReadOnlyList<GeoRoad> roads)
        {
            if(roads==null||roads.Count<1||roads.Count>10000)throw new InvalidDataException("Road source count");
            var seen=new HashSet<string>(StringComparer.Ordinal);int totalPoints=0;
            foreach(var r in roads)
            {
                if(r==null||!Identifier(r.id)||!seen.Add(r.id)||!Identifier(r.start)||!Identifier(r.end)||r.points==null||r.points.Length<2||r.points.Length>10000||r.sourceNodeIds==null||r.sourceNodeIds.Length!=r.points.Length)throw new InvalidDataException("Road source identity/topology shape");
                if((totalPoints+=r.points.Length)>300000)throw new InvalidDataException("Road source point budget");
                if(r.sourceNodeIds[0]!=r.start||r.sourceNodeIds[r.sourceNodeIds.Length-1]!=r.end)throw new InvalidDataException("Road source topology endpoints");
                foreach(var id in r.sourceNodeIds)if(!Identifier(id))throw new InvalidDataException("Road source node identity");
                if(!Finite(r.width)||!Finite(r.maxVehicleHeight)||(r.name?.Length??0)>2048||(r.kind?.Length??0)>2048)throw new InvalidDataException("Road source scalar/text budget");
                foreach(var p in r.points)if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new InvalidDataException("Road source coordinate");
            }
            using(var buffer=new MemoryStream())using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true))
            {
                Text(writer,"RoadRush/source-roads/2");writer.Write(roads.Count);
                foreach(var r in roads.OrderBy(r=>r.id,StringComparer.Ordinal))
                {
                    Text(writer,r.id);Text(writer,r.start);Text(writer,r.end);OptionalText(writer,r.name);OptionalText(writer,r.kind);writer.Write(r.width);writer.Write(r.maxVehicleHeight);writer.Write(r.limit);writer.Write(r.oneWay);writer.Write(r.layer);writer.Write(r.bridge);writer.Write(r.tunnel);writer.Write(r.points.Length);
                    for(int i=0;i<r.points.Length;i++){Text(writer,r.sourceNodeIds[i]);writer.Write(r.points[i].x);writer.Write(r.points[i].y);writer.Write(r.points[i].z);}
                }
                writer.Flush();buffer.Position=0;using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(buffer)).Replace("-","").ToLowerInvariant();
            }
        }
        public static string AnchorPin(RoadSyntheticAnchorDto anchor,string roadSourceDigest)
        {
            if(anchor==null||!Identifier(anchor.id)||!Identifier(anchor.parentRoadId)||!Identifier(anchor.connectorRoadId)||!System.Text.RegularExpressions.Regex.IsMatch(roadSourceDigest??"","^[a-f0-9]{64}$"))throw new InvalidDataException("Synthetic anchor source pin input");
            using(var buffer=new MemoryStream())using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true))
            {
                Text(writer,"RoadRush/synthetic-road-anchor/1");Text(writer,roadSourceDigest);Text(writer,anchor.id);Text(writer,anchor.kind??"");Text(writer,anchor.parentRoadId);writer.Write(anchor.parentSourceDistance);Text(writer,anchor.connectorRoadId);writer.Write(anchor.connectorSourceDistance);writer.Flush();buffer.Position=0;
                using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(buffer)).Replace("-","").ToLowerInvariant();
            }
        }
        static void OptionalText(BinaryWriter writer,string value){writer.Write(value!=null);if(value!=null)Text(writer,value);}
        static void Text(BinaryWriter writer,string value){var bytes=Encoding.UTF8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes);}
        public static bool Identifier(string value)
        {if(string.IsNullOrEmpty(value)||value.Length>128)return false;foreach(char c in value)if(char.IsWhiteSpace(c)||char.IsControl(c))return false;return true;}
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
    }
}
