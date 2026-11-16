using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
namespace EarthUnscripted
{
    public static class RailwayTags
    {
        static readonly string[] LifecycleKeys={"disused:railway","abandoned:railway","razed:railway","demolished:railway","construction:railway","proposed:railway"};
        static string Text(JObject tags,string key)=>tags?[key]?.Type==JTokenType.String?(string)tags[key]:null;
        static bool DescribesTrack(string value)=>RailwayMetrics.Supported(value)||value=="yes";
        public static bool IsTrack(JObject tags)
        {if(RailwayMetrics.Supported(Text(tags,"railway")))return true;foreach(string key in LifecycleKeys)if(DescribesTrack(Text(tags,key)))return true;return false;}
        public static string Lifecycle(JObject tags)
        {
            foreach(string key in LifecycleKeys)if(DescribesTrack(Text(tags,key)))return key.Split(':')[0];
            string value=Text(tags,"railway");if(value=="disused"||value=="abandoned"||value=="razed"||value=="demolished"||value=="construction"||value=="proposed")return value;
            foreach(string key in new[]{"razed","demolished","abandoned","disused","construction","proposed"})if(Text(tags,key)=="yes")return key;
            return RailwayMetrics.Supported(value)?value:null;
        }
        public static int Layer(JObject tags)
        {return int.TryParse(Text(tags,"layer"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)?Math.Max(-20,Math.Min(20,value)):0;}
        public static float Metres(string value,float fallback=0)
        {
            if(string.IsNullOrWhiteSpace(value)||value.Length>80)return fallback;
            var imperial=Regex.Match(value.Trim(),"^(?<feet>[0-9]+(?:[.][0-9]+)?)\\s*(?:'|ft|feet)\\s*(?:(?<inches>[0-9]+(?:[.][0-9]+)?)\\s*(?:\"|in|inches)?)?$",RegexOptions.IgnoreCase);
            if(imperial.Success)
            {
                double feet=double.Parse(imperial.Groups["feet"].Value,CultureInfo.InvariantCulture),inches=imperial.Groups["inches"].Success?double.Parse(imperial.Groups["inches"].Value,CultureInfo.InvariantCulture):0;
                double result=feet*.3048+inches*.0254;return result>0&&result<100?(float)result:fallback;
            }
            var metric=Regex.Match(value.Trim(),"^(?<number>[0-9]+(?:[.][0-9]+)?)\\s*(?:m|metres|meters)?$",RegexOptions.IgnoreCase);
            return metric.Success&&float.TryParse(metric.Groups["number"].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float metres)&&metres>0&&metres<100?metres:fallback;
        }
    }
}
