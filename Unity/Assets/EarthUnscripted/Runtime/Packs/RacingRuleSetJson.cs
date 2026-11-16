using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;

namespace EarthUnscripted.Packs
{
    /// <summary>No file resolution. Parent is an explicitly supplied already-resolved value.</summary>
    public static class RacingRuleSetJson
    {
        public const int MaximumBytes=65536;
        static void Need(bool ok, string why) { if (!ok) throw new InvalidDataException(why); }
        static readonly string[] RootFields={"format","schema","id","name","revision","parent","settings"};
        static readonly string[] Settings={"gates","offTrackGrace","trackLimitPenalty","recoveryPenalty","recoveryInvalidates","collisionInvalidates","racePitLaneAllowed","racePitLaneSpeed","raceCountdown","trialCountdown","raceFinishWait","raceOpponents","allowedVehicleClasses"};
        public static RacingRuleSet Parse(byte[] bytes, RacingRuleSet parent=null)
        {
            JObject root=ReadStrictObject(bytes,MaximumBytes);
            Need(root.Properties().All(p=>RootFields.Contains(p.Name,StringComparer.Ordinal)),"Unknown racing rule property");
            string String(JObject obj,string field) { Need(obj[field]?.Type==JTokenType.String,"Racing rule string "+field); return (string)obj[field]; }
            Need(String(root,"format")=="EarthUnscriptedRacingRules" && root["schema"]?.Type==JTokenType.Integer && (long)root["schema"]==1,"Racing rule format/schema");
            Need(root["revision"]?.Type==JTokenType.Integer,"Racing rule revision integer");
            string id=String(root,"id"), name=String(root,"name"); long revision=(long)root["revision"];
            string parentId=root.Property("parent") == null ? null : String(root,"parent");
            Need(parentId == null ? parent == null : parent != null && parent.Id==parentId, "Explicit racing rule parent mismatch");
            var defaults=parent??RacingRuleSet.LegacyDefaults;
            Need(root["settings"] is JObject,"Racing rule settings object required"); var settings=(JObject)root["settings"];
            Need(settings.Properties().All(p=>Settings.Contains(p.Name,StringComparer.Ordinal)),"Unknown racing behaviour property");
            int Int(string key,int fallback) { var t=settings[key]; if(t==null)return fallback; Need(t.Type==JTokenType.Integer,"Racing rule integer "+key); long v=(long)t; Need(v>=int.MinValue && v<=int.MaxValue,"Racing rule integer range");return (int)v; }
            float Number(string key,float fallback) { var t=settings[key]; if(t==null)return fallback; Need(t.Type==JTokenType.Integer||t.Type==JTokenType.Float,"Racing rule number "+key); double v=(double)t; Need(!double.IsNaN(v)&&!double.IsInfinity(v)&&v>=0,"Racing rule finite number "+key); return (float)v; }
            bool Bool(string key,bool fallback) { var t=settings[key]; if(t==null)return fallback;Need(t.Type==JTokenType.Boolean,"Racing rule boolean "+key);return (bool)t; }
            var classes=defaults.CopyAllowedVehicleClasses();
            if(settings.Property("allowedVehicleClasses")!=null)
            {
                Need(settings["allowedVehicleClasses"] is JArray,"Racing rule classes array");var array=(JArray)settings["allowedVehicleClasses"];
                Need(array.Count>=1 && array.Count<=2 && array.All(t=>t.Type==JTokenType.String),"Racing rule classes values");classes=array.Select(t=>(string)t).ToArray();
            }
            return RacingRuleSet.Create(id,name,revision,parentId,Int("gates",defaults.Gates),Number("offTrackGrace",defaults.OffTrackGrace),
                Number("trackLimitPenalty",defaults.TrackLimitPenalty),Number("recoveryPenalty",defaults.RecoveryPenalty),
                Bool("recoveryInvalidates",defaults.RecoveryInvalidates),Bool("collisionInvalidates",defaults.CollisionInvalidates),
                Bool("racePitLaneAllowed",defaults.RacePitLaneAllowed),Number("racePitLaneSpeed",defaults.RacePitLaneSpeed),
                Number("raceCountdown",defaults.RaceCountdown),Number("trialCountdown",defaults.TrialCountdown),
                Number("raceFinishWait",defaults.RaceFinishWait),Int("raceOpponents",defaults.RaceOpponents),classes);
        }

        internal static JObject ReadStrictObject(byte[] bytes,int maximum)
        {
            Need(bytes!=null&&bytes.Length>0&&bytes.Length<=maximum,"JSON byte budget");
            string text=new UTF8Encoding(false,true).GetString(bytes);new Syntax(text).Check();
            using(var reader=new JsonTextReader(new StringReader(text)){MaxDepth=8,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})
            {
                var root=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                Need(!reader.Read(),"Trailing JSON");return root;
            }
        }

        // Small bounded grammar check, not another data serializer. Only standard JSON tokens reach Newtonsoft.
        sealed class Syntax
        {
            readonly string text;int at;
            internal Syntax(string text) { this.text=text; }
            void Fail() { throw new InvalidDataException("Strict racing rule JSON syntax at "+at); }
            void Space() { while(at<text.Length && (text[at]==' '||text[at]=='\t'||text[at]=='\r'||text[at]=='\n'))at++; }
            bool Eat(char c) { Space();if(at<text.Length && text[at]==c){at++;return true;}return false; }
            internal void Check() { Value(1);Space();if(at!=text.Length)Fail(); }
            void Value(int depth)
            {
                if(depth>8)Fail();Space();if(at==text.Length)Fail();char c=text[at];
                if(c=='{'){at++;if(Eat('}'))return;do{Space();if(at==text.Length||text[at]!='"')Fail();String();if(!Eat(':'))Fail();Value(depth+1);if(Eat('}'))return;}while(Eat(','));Fail();}
                else if(c=='['){at++;if(Eat(']'))return;do{Value(depth+1);if(Eat(']'))return;}while(Eat(','));Fail();}
                else if(c=='"')String();
                else if(c=='t')Literal("true");else if(c=='f')Literal("false");else if(c=='n')Literal("null");else Number();
            }
            void Literal(string word) { if(at+word.Length>text.Length||string.CompareOrdinal(text,at,word,0,word.Length)!=0)Fail();at+=word.Length; }
            void String()
            {
                at++;while(at<text.Length)
                {
                    char c=text[at++];if(c=='"')return;if(c<32)Fail();
                    if(c=='\\') { if(at==text.Length)Fail();c=text[at++];if(c=='u'){for(int i=0;i<4;i++){if(at==text.Length||!Uri.IsHexDigit(text[at++]))Fail();}}else if("\"\\/bfnrt".IndexOf(c)<0)Fail(); }
                }
                Fail();
            }
            bool Digit()=>at<text.Length && text[at]>='0' && text[at]<='9';
            void Number()
            {
                if(at<text.Length&&text[at]=='-')at++;
                if(!Digit())Fail();if(text[at]=='0')at++;else while(Digit())at++;
                if(at<text.Length&&text[at]=='.'){at++;if(!Digit())Fail();while(Digit())at++;}
                if(at<text.Length&&(text[at]=='e'||text[at]=='E')){at++;if(at<text.Length&&(text[at]=='+'||text[at]=='-'))at++;if(!Digit())Fail();while(Digit())at++;}
            }
        }
    }
}
