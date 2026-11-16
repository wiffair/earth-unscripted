using System;using System.IO;
namespace EarthUnscripted
{
    public static class LegacyRoadWidthPreparation
    {
        // Profile-absent/schema0 fixtures remain valid legacy worlds. Explicit
        // schema1 snapshots must validate strictly before installing any field.
        public static bool TryInstall(GeoRegion region,int schema,RoadWidthProfileDto[] profiles,out string diagnostic)
        {
            diagnostic=null;if(region==null){diagnostic="Missing region";return false;}
            if(schema==0&&(profiles==null||profiles.Length==0)){region.roadWidthSchema=0;region.roadWidthProfiles=Array.Empty<RoadWidthProfileDto>();region.Prepare();return true;}
            if(schema!=1){diagnostic="Unsupported explicit road width schema";return false;}
            try{var field=new EffectiveRoadWidthField(profiles,region.roads);region.roadWidthSchema=1;region.roadWidthProfiles=field.CopySnapshot();region.Prepare();return true;}
            catch(Exception e)when(e is InvalidDataException||e is ArgumentException||e is OverflowException){diagnostic=e.Message;return false;}
        }
    }
}
