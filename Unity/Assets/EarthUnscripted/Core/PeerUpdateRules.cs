using System;
using System.Globalization;
using System.Collections.Generic;
namespace EarthUnscripted.Core
{
    [Serializable] public sealed class PeerUpdateInterest {public string pack="",version="";}
    // The connected host authenticates bytes, not the upstream map publisher.
    // A peer cannot select a different country, origin, sequence or state date.
    public static class PeerUpdateRules
    {
        public const int MaximumOffers=16,MaximumAhead=100;
        public const long MaximumStagedBytes=256L*1024*1024;
        public static bool ValidInterest(PeerUpdateInterest interest)=>interest!=null&&DataPackageRules.SafeId(interest.pack)&&long.TryParse(interest.version,NumberStyles.None,CultureInfo.InvariantCulture,out long version)&&version>=1&&version<999999999;
        public static string StagedTip(string installed,IEnumerable<long> verified)
        {
            if(!long.TryParse(installed,NumberStyles.None,CultureInfo.InvariantCulture,out long current)||current<1)return installed;
            var have=new HashSet<long>(verified);long tip=current;while(tip-current<MaximumAhead&&have.Contains(tip+1))tip++;return tip.ToString(CultureInfo.InvariantCulture);
        }
        public static bool Matches(DataFileManifest item,string pack,string installed,string source)
        {
            if(string.IsNullOrEmpty(source)||!DataPackageRules.Valid(item)||item.format!=2||item.kind!="map-diff"||item.pack!=pack||item.licence!="ODbL-1.0"||!long.TryParse(installed,NumberStyles.None,CultureInfo.InvariantCulture,out long current)||!long.TryParse(item.fromVersion,NumberStyles.None,CultureInfo.InvariantCulture,out long from)||!long.TryParse(item.toVersion,NumberStyles.None,CultureInfo.InvariantCulture,out long to)||current<1||to<1||to>999999999||from!=to-1||from<current||to-current>MaximumAhead)return false;
            string sequence=$"{to/1000000:D3}/{to/1000%1000:D3}/{to%1000:D3}";
            return item.id==pack+"-"+to.ToString(CultureInfo.InvariantCulture)&&item.source==source.TrimEnd('/')+"/"+sequence+".osc.gz";
        }
    }
}
