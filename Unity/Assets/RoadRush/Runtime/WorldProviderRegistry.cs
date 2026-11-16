using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    // Same versioned catalogue is packaged with the world worker. Source formats
    // and credentials stay outside gameplay and the player's career file.
    public static class WorldProviderRegistry
    {
        static JObject catalogue;
        public static JObject Catalogue=>catalogue??=JObject.Parse(Resources.Load<TextAsset>("WorldData/ProviderCatalogue").text);
        public static string Version=>(string)Catalogue["version"];
        public readonly struct Selection
        {
            public readonly string Id,Label,Reason;public readonly bool Available;
            public Selection(string id,string label,string reason){Id=id;Label=label;Reason=reason;Available=reason=="ready";}
        }
        public static string Country(string value)
        {value=(value??"").ToUpperInvariant();return value=="UK"||value.StartsWith("GB-",StringComparison.Ordinal)?"GB":value;}
        public static Selection[] Resolve(double longitude,double latitude,string country,string capability,DateTime? date=null)
        {
            if(!double.IsFinite(longitude)||!double.IsFinite(latitude)||Math.Abs(longitude)>180||Math.Abs(latitude)>90)throw new ArgumentException("Invalid provider query location");
            country=Country(country);string day=(date??DateTime.UtcNow).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            return Catalogue["providers"].OfType<JObject>().Where(p=>p["capabilities"].Any(c=>(string)c==capability)).Where(p=>
            {
                var b=p["bounds"];double west=(double)b[0],south=(double)b[1],east=(double)b[2],north=(double)b[3];
                return latitude>=south&&latitude<=north&&(west<=east?longitude>=west&&longitude<=east:longitude>=west||longitude<=east)&&(!p["countries"].Any()||p["countries"].Any(c=>(string)c==country));
            }).OrderByDescending(p=>(int)p["priority"]).Select(p=>new Selection((string)p["id"],(string)p["label"],
                (string)p["status"]!="active"?(string)p["status"]:p["credentialRef"]!=null?"credentials required":
                string.CompareOrdinal(day,(string)p["validFrom"]??"0000")<0||string.CompareOrdinal(day,(string)p["validUntil"]??"9999")>0?"outside validity dates":"ready")).OrderBy(s=>!s.Available).ToArray();
        }
        public static JObject Rules(string country)=>(JObject)(Catalogue["rules"][Country(country)]??Catalogue["rules"]["*"]);
    }
}
