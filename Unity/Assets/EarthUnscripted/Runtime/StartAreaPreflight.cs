using System;
using System.Collections;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public static class StartAreaPreflight
    {
        // Bundled circuits and samples have independent source histories. Only
        // refresh an extracted area from the exact country pack that produced it.
        public static WorldPlace Place(GeoRegion region)=>new WorldPlace{name=region.name?.Replace(" · real roads",""),latitude=region.latitude,longitude=region.longitude,country_code=region.countryCode,timezone=region.timeZone,drivingSide=region.drivingSide,size=Mathf.RoundToInt(region.width)};
        public static bool Newer(GeoRegion region,CountryPack pack)
        {
            string prefix=pack.Name+"/";
            if(region.datasetVersion==null||!region.datasetVersion.StartsWith(prefix,StringComparison.Ordinal)||!long.TryParse(region.datasetVersion.Substring(prefix.Length),out long previous)||!long.TryParse(pack.Version,out long next))return false;
            return next>previous||next==previous&&pack.RailwaysComplete&&region.railwaySourceIdentity!=pack.RailwayIdentity;
        }
        public static IEnumerator Prepare(GameSession session,Action<GeoRegion> complete)
        {
            var region=session.World.Route.Region;
            if(region==null||!RegionCache.Safe(region.id)||string.IsNullOrEmpty(region.datasetVersion)||!session.Updates.Enabled||session.Network.Active){complete(null);yield break;}
            CountryPack pack=null;
            try{pack=CountryPacks.Installed().FirstOrDefault(p=>p.Ready&&(string.IsNullOrEmpty(region.datasetSource)||region.datasetSource.TrimEnd('/')==p.ReplicationSource)&&region.datasetVersion.StartsWith(p.Name+"/",StringComparison.Ordinal)&&p.Covers(Place(region)));}
            catch(Exception e){session.Notify("Using saved roads · "+e.Message);}
            if(pack==null){complete(null);yield break;}
            yield return session.Updates.BeforePrepare(pack);
            try{pack=new CountryPack(pack.Folder);}catch(Exception e){session.Notify("Using saved roads · "+e.Message);complete(null);yield break;}
            if(!Newer(region,pack)){complete(null);yield break;}
            // Download selects the installed offline pack, performs extraction on
            // a worker and publishes a complete area snapshot atomically.
            if(session.Downloads.Busy){complete(null);yield break;}
            session.Downloads.PrepareOffline(Place(region),pack);
            while(session.Downloads.Busy)yield return null;
            var ready=session.Downloads.Ready;
            if(ready!=null&&ready.id!=region.id&&ready.latitude==region.latitude&&ready.longitude==region.longitude&&Newer(region,pack)&&ready.datasetVersion==pack.Name+"/"+pack.Version)complete(ready);
            else {session.Notify("Using saved roads · updated area will be retried later");complete(null);}
        }
    }
}
