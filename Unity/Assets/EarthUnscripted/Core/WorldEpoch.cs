using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace EarthUnscripted.Core
{
    public static class WorldEpoch
    {
        public static string Compute(string compatibility,string region,string snapshot)
        {
            if(string.IsNullOrEmpty(compatibility)||compatibility.Length>128||!DataPackageRules.SafeId(region)||snapshot==null||Encoding.UTF8.GetByteCount(snapshot)>DataPackageRules.MaximumFileBytes)throw new ArgumentException("World is outside the bounded LAN snapshot format");
            // Length-prefixed UTF-8 fields prevent ambiguous concatenation and
            // produce the same digest on Mac and Windows, independent of locale.
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write("EarthUnscriptedWorld1");writer.Write(compatibility);writer.Write(region);writer.Write(snapshot);writer.Flush();stream.Position=0;return DataPackageRules.Hash(stream);
            }
        }
        public static bool Valid(string digest)=>digest!=null&&Regex.IsMatch(digest,@"^[a-fA-F0-9]{64}$");
        public static bool Matches(string expected,string actual)=>Valid(expected)&&Valid(actual)&&string.Equals(expected,actual,StringComparison.OrdinalIgnoreCase);
    }
}
