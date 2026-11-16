using System;using System.Collections.Generic;using System.IO;using System.Text;using Newtonsoft.Json.Linq;
namespace RoadRush
{
    // Current country storage adapter's exact original E7 node representation.
    // These typed records are COPIED data, not a grant of coherent-source trust.
    [Serializable]public sealed class OriginalRaw13Node{public long id;public int latitudeE7,longitudeE7;}
    [Serializable]public sealed class OriginalRaw13Tag{public string key,value;}
    [Serializable]public sealed class OriginalRaw13Way{public long id;public OriginalRaw13Node[] nodes;public OriginalRaw13Tag[] tags;}
    public static class Raw13CoherentReadApi
    {
        // Called by a future read-transaction worker AFTER it has captured the
        // complete selected rows/grid/regional policy under its own source pins.
        // No bool/JSON receipt can upgrade the returned payload to SourceCapture.
        // Source tags use dynamic keys; field-DTO canonical serialization remains
        // strict. This bounded, shape-built token tree uses the SAME exact IEEE
        // numeric spellings and ordinal keys, never JValue floating ToString/R.
        static byte[] WriteSource(JToken root)
        {
            var utf8=new UTF8Encoding(false,true);using(var text=new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            {
                using(var writer=new Newtonsoft.Json.JsonTextWriter(text){Culture=System.Globalization.CultureInfo.InvariantCulture,Formatting=Newtonsoft.Json.Formatting.None})
                {
                    void Visit(JToken token)
                    {
                        if(token is JObject obj){writer.WriteStartObject();foreach(var property in System.Linq.Enumerable.OrderBy(obj.Properties(),p=>p.Name,StringComparer.Ordinal)){utf8.GetByteCount(property.Name);writer.WritePropertyName(property.Name);Visit(property.Value);}writer.WriteEndObject();}
                        else if(token is JArray array){writer.WriteStartArray();foreach(var item in array)Visit(item);writer.WriteEndArray();}
                        else if(token.Type==JTokenType.Integer)writer.WriteRawValue(Convert.ToString(((JValue)token).Value,System.Globalization.CultureInfo.InvariantCulture));
                        else if(token.Type==JTokenType.Float)writer.WriteRawValue(CanonicalGeography13.DoubleToken(token.Value<double>()));
                        else if(token.Type==JTokenType.String){string value=token.Value<string>();utf8.GetByteCount(value);writer.WriteValue(value);}
                        else throw new InvalidDataException("Unsupported original source token");
                    }
                    Visit(root);
                }
                byte[] bytes=utf8.GetBytes(text.ToString());if(bytes.Length>64*1024*1024)throw new InvalidDataException("Complete canonical source encoding budget");return bytes;
            }
        }
        public static Raw13CapturePayload CopyForInspection(Raw13CaptureHeader header,OriginalRaw13Way[] originalWays,byte[] absoluteGrid,GeoBuilding[] completeRegionalBuildings,byte[] regionalProvenance,byte[] originalReadEvidence)
        {
            if(originalWays==null||originalWays.Length>12048)throw new InvalidDataException("Complete original way count budget");
            int references=0;long encodedBudget=32;var ids=new HashSet<long>();var coordinates=new Dictionary<long,(int Lat,int Lon)>();var rows=new JArray();
            foreach(var way in originalWays)
            {
                if(way==null||way.id<=0||!ids.Add(way.id)||way.nodes==null||way.nodes.Length<2||way.nodes.Length>50000||way.tags==null||way.tags.Length>128)throw new InvalidDataException("Complete typed original way budget/identity");references+=way.nodes.Length;encodedBudget+=128L+way.nodes.Length*256L;if(encodedBudget>64L*1024*1024)throw new InvalidDataException("Complete source encoding budget before DOM assembly");if(references>300000)throw new InvalidDataException("Complete original reference budget; no row thinning");
                var nodes=new JArray();var geometry=new JArray();var tags=new JObject();foreach(var node in way.nodes){if(node==null||node.id<=0||node.latitudeE7< -900000000||node.latitudeE7>900000000||node.longitudeE7< -1800000000||node.longitudeE7>1800000000)throw new InvalidDataException("Original E7 node bounds");var point=(node.latitudeE7,node.longitudeE7);if(coordinates.TryGetValue(node.id,out var existing)&&existing!=point)throw new InvalidDataException("Conflicting original E7 node coordinates");coordinates[node.id]=point;nodes.Add(node.id);geometry.Add(new JObject{{"lat",node.latitudeE7/1e7},{"lon",node.longitudeE7/1e7}});}
                foreach(var tag in way.tags){if(tag==null||string.IsNullOrEmpty(tag.key)||tag.key.Length>96||tag.value==null||tag.value.Length>2048||tags.Property(tag.key)!=null)throw new InvalidDataException("Original source tag budget/duplicate");try{var utf8=new UTF8Encoding(false,true);utf8.GetByteCount(tag.key);utf8.GetByteCount(tag.value);encodedBudget+=utf8.GetByteCount(Newtonsoft.Json.JsonConvert.ToString(tag.key))+utf8.GetByteCount(Newtonsoft.Json.JsonConvert.ToString(tag.value))+8;}catch(EncoderFallbackException e){throw new InvalidDataException("Malformed original tag Unicode",e);}if(encodedBudget>64L*1024*1024)throw new InvalidDataException("Complete tag/source encoding budget before DOM assembly");tags.Add(tag.key,tag.value);}
                rows.Add(new JObject{{"type","way"},{"id",way.id},{"nodes",nodes},{"geometry",geometry},{"tags",tags}});
            }
            // Header pins remain externally captured claims until the actual
            // coherent reader/minter is reviewed. This API only copies/inspects.
            byte[] provider=WriteSource(new JObject{{"elements",rows}});
            return new Raw13CapturePayload(header,provider,absoluteGrid,completeRegionalBuildings,regionalProvenance,originalReadEvidence);
        }
    }
}
