using UnityEngine;
namespace EarthUnscripted
{
    public readonly struct WeatherSurfaceState
    {
        public readonly float Wet,Ice,Frost,Snow,Snowfall;
        public WeatherSurfaceState(float wet,float ice,float frost,float snow,float snowfall)
        {Wet=Mathf.Clamp01(wet);Ice=Mathf.Clamp01(ice);Frost=Mathf.Clamp01(frost);Snow=Mathf.Clamp01(snow);Snowfall=Mathf.Clamp01(snowfall);}
    }
    public static class WeatherSurfaceRules
    {
        public static WeatherSurfaceState Sheltered(WeatherSurfaceState outside,TunnelWeatherIndex.Exposure exposure,float temperature)
        {
            if(!exposure.Covered)return outside;
            float extreme=Mathf.InverseLerp(-10,-25,temperature);
            return new WeatherSurfaceState(outside.Wet*exposure.Wet,outside.Ice*exposure.Wet,outside.Frost*Mathf.Max(exposure.Snow,extreme*.12f),outside.Snow*exposure.Snow,outside.Snowfall*exposure.Snow);
        }
        public static float RoadBaseSmoothness(bool spatialWeather,float wet)=>spatialWeather?.22f:Mathf.Lerp(.22f,.79f,wet);
        public static float Grip(WeatherSurfaceState state,bool raceTyre=false)
        {float wet=raceTyre?1:Mathf.Lerp(1,.62f,state.Wet);return wet*Mathf.Lerp(1,.28f,state.Ice)*Mathf.Lerp(1,.72f,state.Frost)*Mathf.Lerp(1,.65f,state.Snow);}
        public static bool SnowCode(int code)=>code==71||code==73||code==75||code==77||code==85||code==86;
        public static WeatherSurfaceState Live(float temperature,float humidity,float dewpoint,float precipitation,float snowfall,float depth,int code,float previousWet)
        {
            float snow=SnowCode(code)||snowfall>0?Mathf.Max(.15f,Mathf.Clamp01(snowfall/2)):0;
            float cover=Mathf.Max(Mathf.Clamp01(depth/.025f),snow>0&&temperature<2?.3f+snow*.7f:0);
            bool freezing=code==56||code==57||code==66||code==67;
            float wet=precipitation>.03f&&snow==0||temperature>0&&depth>0?1:previousWet;
            float ice=(temperature<=0||freezing)&&wet>.1f?Mathf.Clamp01(.25f-temperature*.2f):0;
            float frost=temperature<2&&humidity>=75&&dewpoint<=0&&cover<.1f?Mathf.Clamp01((2-temperature)/5):0;
            return new WeatherSurfaceState(wet,ice,frost,cover,snow);
        }
        public static WeatherSurfaceState Simulated(int mode,float wet)=>mode==6?new WeatherSurfaceState(.2f,0,0,1,.8f):mode==7?new WeatherSurfaceState(0,0,.85f,0,0):mode==8?new WeatherSurfaceState(1,.9f,.2f,0,0):new WeatherSurfaceState(wet,0,0,0,0);
    }
}
