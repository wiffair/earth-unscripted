using System;using System.Collections;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Numerics;using System.Reflection;using System.Text;using Newtonsoft.Json;using Newtonsoft.Json.Serialization;
namespace EarthUnscripted
{
    // NEW opt-in family only. Legacy WorldFieldJson/geometry12/fixed raw catalogs
    // retain their original bytes. Wire typed/resource preflight precedes Read.
    public static class CanonicalGeography13
    {
        public const string Mode="ieee-exact-decimal-v1";public const int MaximumBytes=32*1024*1024,MaximumNumberCharacters=1100;
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);static readonly Fields Resolver=new Fields();
        static JsonSerializer Serializer()=>JsonSerializer.Create(new JsonSerializerSettings{ContractResolver=Resolver,TypeNameHandling=TypeNameHandling.None,MetadataPropertyHandling=MetadataPropertyHandling.Ignore,MissingMemberHandling=MissingMemberHandling.Error,NullValueHandling=NullValueHandling.Include,DefaultValueHandling=DefaultValueHandling.Include,ObjectCreationHandling=ObjectCreationHandling.Replace,ConstructorHandling=ConstructorHandling.AllowNonPublicDefaultConstructor,Culture=CultureInfo.InvariantCulture,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double,MaxDepth=16,Converters=new List<JsonConverter>{new Numbers(),new Strings()}});
        static void Size(string text){try{if(text==null||Utf8.GetByteCount(text)>MaximumBytes)throw new InvalidDataException("Canonical13 byte budget");}catch(EncoderFallbackException e){throw new InvalidDataException("Canonical13 malformed UTF16/UTF8",e);}}
        public static string Write(object value,bool pretty=false)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));using(var text=new StringWriter(CultureInfo.InvariantCulture)){using(var writer=new JsonTextWriter(text){Formatting=pretty?Formatting.Indented:Formatting.None,Culture=CultureInfo.InvariantCulture})Serializer().Serialize(writer,value);string result=text.ToString();Size(result);return result;}
        }
        public static byte[] WriteBytes(object value)=>Utf8.GetBytes(Write(value));
        public static T Read<T>(string text){Size(text);var value=WorldFieldJson.Read<T>(text);Write(value);return value;}
        public static T ReadBytes<T>(byte[] bytes){if(bytes==null||bytes.Length>MaximumBytes)throw new InvalidDataException("Canonical13 byte budget");try{return Read<T>(Utf8.GetString(bytes));}catch(DecoderFallbackException e){throw new InvalidDataException("Canonical13 malformed UTF8",e);}}
        public static T ReadCanonical<T>(byte[] bytes){var value=ReadBytes<T>(bytes);if(!WriteBytes(value).SequenceEqual(bytes))throw new InvalidDataException("Canonical13 noncanonical bytes");return value;}
        public static T Clone<T>(T value)=>Read<T>(Write(value));
        // Exact finite IEEE rational => terminating decimal. All arithmetic and
        // digit formatting here is integer-only; no G9/G17/R floating formatter.
        static string Decimal(bool negative,ulong significand,int exponent)
        {
            if(significand==0)return negative?"-0.0":"0.0";
            while(exponent<0&&(significand&1)==0){significand>>=1;exponent++;}
            BigInteger coefficient=new BigInteger(significand);string result;
            if(exponent>=0)result=(coefficient<<exponent).ToString(CultureInfo.InvariantCulture)+".0";
            else
            {
                int scale=-exponent;string digits=(coefficient*BigInteger.Pow(5,scale)).ToString(CultureInfo.InvariantCulture);int point=digits.Length-scale;
                result=point<=0?"0."+new string('0',-point)+digits:digits.Insert(point,".");
            }
            if(negative)result="-"+result;if(result.Length>MaximumNumberCharacters)throw new JsonSerializationException("Canonical13 number budget");return result;
        }
        public static string FloatToken(float value)
        {uint bits=unchecked((uint)BitConverter.SingleToInt32Bits(value));uint exponent=(bits>>23)&255,mantissa=bits&0x7fffff;if(exponent==255)throw new JsonSerializationException("Nonfinite canonical13 float");return Decimal((bits&0x80000000)!=0,exponent==0?mantissa:mantissa|0x800000,exponent==0?-149:(int)exponent-150);}
        public static string DoubleToken(double value)
        {ulong bits=unchecked((ulong)BitConverter.DoubleToInt64Bits(value));ulong exponent=(bits>>52)&2047,mantissa=bits&0xfffffffffffff;if(exponent==2047)throw new JsonSerializationException("Nonfinite canonical13 double");return Decimal((bits&0x8000000000000000)!=0,exponent==0?mantissa:mantissa|0x10000000000000,exponent==0?-1074:(int)exponent-1075);}
        sealed class Numbers:JsonConverter
        {
            public override bool CanRead=>false;public override bool CanConvert(Type type)=>type==typeof(float)||type==typeof(double);
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)=>writer.WriteRawValue(value is float f?FloatToken(f):DoubleToken((double)value));
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)=>throw new NotSupportedException();
        }
        sealed class Strings:JsonConverter
        {
            public override bool CanRead=>false;public override bool CanConvert(Type type)=>type==typeof(string);
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer){string text=(string)value;try{if(text!=null)Utf8.GetByteCount(text);}catch(EncoderFallbackException e){throw new JsonSerializationException("Malformed canonical13 string",e);}writer.WriteValue(text);}
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)=>throw new NotSupportedException();
        }
        sealed class Fields:DefaultContractResolver
        {
            protected override JsonContract CreateContract(Type type)
            {if(typeof(UnityEngine.Object).IsAssignableFrom(type)||typeof(IDictionary).IsAssignableFrom(type)||type==typeof(DateTime)||type==typeof(DateTimeOffset)||type==typeof(decimal))throw new JsonSerializationException("Canonical13 requires plain field DTOs, explicit timestamp strings and IEEE numbers");return base.CreateContract(type);}
            protected override IList<JsonProperty> CreateProperties(Type type,MemberSerialization serialization)
            {
                var fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>!f.IsStatic&&!Attribute.IsDefined(f,typeof(NonSerializedAttribute))).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();if(fields.GroupBy(f=>f.Name,StringComparer.Ordinal).Any(g=>g.Count()!=1))throw new JsonSerializationException("Ambiguous canonical13 field");var result=new List<JsonProperty>(fields.Length);
                foreach(var field in fields){if(field.IsInitOnly)throw new JsonSerializationException("Readonly canonical13 field needs explicit contract");var p=base.CreateProperty(field,MemberSerialization.OptOut);p.PropertyName=field.Name;p.UnderlyingName=field.Name;p.Readable=true;p.Writable=true;p.Ignored=false;p.ShouldSerialize=null;p.ShouldDeserialize=null;p.ValueProvider=new ReflectionValueProvider(field);result.Add(p);}return result;
            }
        }
    }
}
