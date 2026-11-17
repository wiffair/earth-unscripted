using System;
using System.Globalization;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    // Numeric HUD/AI limits remain unchanged. Only explicit NSL source identity
    // selects the national-limit disc; a numeric60/70 alone never does.
    public static class UKSpeedLimitSigns
    {
        static bool Named(string value)=>value=="national"||value=="gb:nsl"||value=="gb:nsl_single"||value=="gb:nsl_dual";
        public static bool IsNational(NewbridgeWorld.Feature road,bool forward)
        {
            if(road.signNationalDirectional)return forward?road.signNationalForward:road.signNationalBackward;
            string raw=(forward?road.signLimitForward:road.signLimitBackward)??"";raw=raw.Trim().ToLowerInvariant();
            if(Named(raw))return true;
            if(!road.signNational)return false;
            float directed=forward?road.speedLimitForwardMps:road.speedLimitBackwardMps;
            if(directed>0&&road.speedLimitMps>0&&Mathf.Abs(directed-road.speedLimitMps)>.2f)return false;
            float mps=directed>0?directed:road.speedLimitMps;int mph=Mathf.RoundToInt(mps/.44704f);
            if(mph!=60&&mph!=70)return false;
            return raw.Length==0||float.TryParse(raw.Replace("mph",""),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)&&Mathf.Abs(value-mph)<.1f;
        }
        public static NewbridgeWorld.Control ForPresentation(NewbridgeWorld.Feature road,NewbridgeWorld.Control control,bool uk)
        {
            if(!uk||control.kind!="speed"||(!(control.id??"").Contains(":speed:")&&!control.repeater)||!IsNational(road,control.direction>=0)||control.value=="national")return control;
            // A distinct presentation clone leaves mapped control semantics and
            // source/cache bytes untouched, even when layout runs on a worker.
            return new NewbridgeWorld.Control{id=control.id,kind=control.kind,group=control.group,value="national",crossing=control.crossing,station=control.station,x=control.x,y=control.y,z=control.z,dx=control.dx,dz=control.dz,direction=control.direction,mountSide=control.mountSide,repeater=control.repeater,diameter=control.diameter};
        }
    }
}
