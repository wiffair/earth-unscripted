using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Reflection;using System.Text;using Newtonsoft.Json;using Newtonsoft.Json.Serialization;
namespace RoadRush
{
    // Plain geographic DTO serialization only. Wire callers MUST execute the
    // codec's streaming syntax/typed budgets before Read (not a guard bypass).
    // Properties, private fields and NonSerialized runtime owners never enter it.
    public static class WorldFieldJson
    {
        const int MaximumBytes=32*1024*1024;
        static readonly FieldResolver Resolver=new FieldResolver();
        static readonly RoundTripNumberConverter Numbers=new RoundTripNumberConverter();
        static JsonSerializer Serializer()=>JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver=Resolver,TypeNameHandling=TypeNameHandling.None,
            MetadataPropertyHandling=MetadataPropertyHandling.Ignore,
            MissingMemberHandling=MissingMemberHandling.Error,
            NullValueHandling=NullValueHandling.Include,DefaultValueHandling=DefaultValueHandling.Include,
            ObjectCreationHandling=ObjectCreationHandling.Replace,
            ConstructorHandling=ConstructorHandling.AllowNonPublicDefaultConstructor,
            Culture=CultureInfo.InvariantCulture,DateParseHandling=DateParseHandling.None,
            FloatParseHandling=FloatParseHandling.Double,MaxDepth=16,
            Converters=new List<JsonConverter>{Numbers}
        });
        public static string Write(object value,bool pretty=false)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            using(var output=new StringWriter(CultureInfo.InvariantCulture))
            {
                using(var writer=new JsonTextWriter(output){Formatting=pretty?Formatting.Indented:Formatting.None,Culture=CultureInfo.InvariantCulture})Serializer().Serialize(writer,value);
                string text=output.ToString();Size(text);return text;
            }
        }
        public static T Read<T>(string text)
        {
            Size(text);using(var reader=new SourceTokenReader(text){MaxDepth=16,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double,Culture=CultureInfo.InvariantCulture})
            {
                var result=Serializer().Deserialize<T>(reader);if(reader.Read())throw new JsonSerializationException("Trailing geographic JSON content");return result;
            }
        }
        public static T Clone<T>(T value)=>Read<T>(Write(value));
        static void Size(string text){if(text==null||Encoding.UTF8.GetByteCount(text)>MaximumBytes)throw new InvalidDataException("Geographic JSON byte budget");}
        // Mono's numeric parser discards the sign of decimal zero. Recover only
        // that lexical bit using public forward-only line information. No package
        // internals, reflection, numeric strings or shared reader state are used.
        sealed class SourceTokenReader:JsonTextReader
        {
            readonly string source;int mappedLine=1,lineStart;
            internal bool NegativeZero{get;private set;}
            internal SourceTokenReader(string text):base(new StringReader(text)){source=text;}
            public override bool Read(){bool ok=base.Read();Capture();return ok;}
            public override double? ReadAsDouble(){var value=base.ReadAsDouble();Capture();return value==0&&NegativeZero?BitConverter.Int64BitsToDouble(long.MinValue):value;}
            void Capture()
            {
                NegativeZero=false;if(TokenType!=JsonToken.Float&&TokenType!=JsonToken.Integer)return;
                double parsed=Convert.ToDouble(Value,CultureInfo.InvariantCulture);if(parsed!=0)return;
                int target=Math.Max(1,LineNumber);
                if(target<mappedLine)throw new JsonSerializationException("Nonmonotonic geographic token position");
                while(mappedLine<target)
                {
                    int p=lineStart;while(p<source.Length&&source[p]!='\r'&&source[p]!='\n')p++;
                    if(p>=source.Length)throw new JsonSerializationException("Invalid geographic token line");
                    if(source[p++]=='\r'&&p<source.Length&&source[p]=='\n')p++;lineStart=p;mappedLine++;
                }
                int end=lineStart+LinePosition;if(end<1||end>source.Length)throw new JsonSerializationException("Invalid geographic token position");int start=end;
                while(start>0&&NumberCharacter(source[start-1]))start--;
                if(start==end||start>0&&!Delimiter(source[start-1]))throw new JsonSerializationException("Invalid geographic numeric lexeme");
                int i=start;bool negative=source[i]=='-';if(negative)i++;bool nonzero=false;
                if(i>=end)throw new JsonSerializationException("Incomplete geographic number");
                if(source[i]=='0'){i++;if(i<end&&Digit(source[i]))throw new JsonSerializationException("Noncanonical leading zero");}
                else {if(source[i]<'1'||source[i]>'9')throw new JsonSerializationException("Invalid geographic mantissa");while(i<end&&Digit(source[i])){nonzero|=source[i]!='0';i++;}}
                if(i<end&&source[i]=='.'){i++;int first=i;while(i<end&&Digit(source[i])){nonzero|=source[i]!='0';i++;}if(i==first)throw new JsonSerializationException("Incomplete geographic fraction");}
                if(i<end&&(source[i]=='e'||source[i]=='E')){i++;if(i<end&&(source[i]=='+'||source[i]=='-'))i++;int first=i;while(i<end&&Digit(source[i]))i++;if(i==first)throw new JsonSerializationException("Incomplete geographic exponent");}
                if(i!=end||nonzero)throw new JsonSerializationException("Geographic number underflow or invalid lexeme");NegativeZero=negative;
            }
            static bool Digit(char c)=>c>='0'&&c<='9';
            static bool NumberCharacter(char c)=>Digit(c)||c=='-'||c=='+'||c=='.'||c=='e'||c=='E';
            static bool Delimiter(char c)=>char.IsWhiteSpace(c)||c==':'||c==','||c=='[';
        }
        sealed class FieldResolver:DefaultContractResolver
        {
            protected override JsonContract CreateContract(Type type)
            {
                if(typeof(UnityEngine.Object).IsAssignableFrom(type))throw new JsonSerializationException("Geographic JSON cannot serialize engine objects");
                return base.CreateContract(type);
            }
            protected override IList<JsonProperty> CreateProperties(Type type,MemberSerialization serialization)
            {
                var fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>!f.IsStatic&&!Attribute.IsDefined(f,typeof(NonSerializedAttribute))).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();
                if(fields.GroupBy(f=>f.Name,StringComparer.Ordinal).Any(g=>g.Count()!=1))throw new JsonSerializationException("Ambiguous geographic field name");
                var result=new List<JsonProperty>(fields.Length);
                foreach(var field in fields)
                {
                    if(field.IsInitOnly)throw new JsonSerializationException("Readonly geographic fields require an explicit immutable contract");
                    var property=base.CreateProperty(field,MemberSerialization.OptOut);property.PropertyName=field.Name;property.UnderlyingName=field.Name;property.Readable=true;property.Writable=!field.IsInitOnly;property.Ignored=false;property.ShouldSerialize=null;property.ShouldDeserialize=null;property.ValueProvider=new ReflectionValueProvider(field);result.Add(property);
                }
                return result;
            }
        }
        sealed class RoundTripNumberConverter:JsonConverter
        {
            public override bool CanRead=>true;
            public override bool CanConvert(Type type)=>type==typeof(double)||type==typeof(float);
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                if(value is double d)
                {if(double.IsNaN(d)||double.IsInfinity(d))throw new JsonSerializationException("Nonfinite geographic double");writer.WriteRawValue(d==0&&BitConverter.DoubleToInt64Bits(d)<0?"-0.0":d.ToString("G17",CultureInfo.InvariantCulture));}
                else
                {float f=(float)value;if(float.IsNaN(f)||float.IsInfinity(f))throw new JsonSerializationException("Nonfinite geographic float");writer.WriteRawValue(f==0&&BitConverter.DoubleToInt64Bits(f)<0?"-0.0":f.ToString("G9",CultureInfo.InvariantCulture));}
            }
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)
            {
                if(reader.TokenType!=JsonToken.Integer&&reader.TokenType!=JsonToken.Float)throw new JsonSerializationException("Geographic number requires a numeric token");double d=Convert.ToDouble(reader.Value,CultureInfo.InvariantCulture);if(d==0&&reader is SourceTokenReader exact&&exact.NegativeZero)d=BitConverter.Int64BitsToDouble(long.MinValue);if(double.IsNaN(d)||double.IsInfinity(d))throw new JsonSerializationException("Nonfinite geographic number");if(type==typeof(double))return d;float f=(float)d;if(float.IsNaN(f)||float.IsInfinity(f))throw new JsonSerializationException("Unrepresentable geographic float");return f;
            }
        }
    }
}
