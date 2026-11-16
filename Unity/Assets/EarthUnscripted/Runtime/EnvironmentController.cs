using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthUnscripted
{
    // Live weather is opt-in and uses only the chosen map's approximate coordinates.
    // Current conditions are numerical weather-model estimates, not a local sensor.
    public sealed class EnvironmentController : MonoBehaviour
    {
        [Serializable] public sealed class CurrentWeather { public long time; public float temperature_2m,precipitation,rain,cloud_cover,wind_speed_10m,relative_humidity_2m,dew_point_2m,snowfall; public int weather_code,is_day; }
        [Serializable] public sealed class WeatherResponse { public string timezone; public int utc_offset_seconds; public CurrentWeather current; public HourlyWeather hourly; }
        [Serializable] public sealed class HourlyWeather {public long[] time;public float[] snow_depth;}
        [Serializable] sealed class CachedWeather { public string region; public long fetched; public WeatherResponse data; }
        public GameSession Session;
        public float Rain {get;private set;} public float Wetness {get;private set;} public float Clouds {get;private set;}
        public float Hour {get;private set;} public float Daylight {get;private set;} public float Wind {get;private set;}
        public string WeatherStatus {get;private set;}="Simulated weather";
        public string ClockStatus {get;private set;}="Fixed time";
        public WeatherResponse Live {get;private set;}
        public bool NetworkOverride; float networkHour,networkRain,networkWetness,networkClouds,networkWind,networkFog=.0007f;
        public float Ice {get;private set;} public float Frost {get;private set;} public float Snow {get;private set;} public float Snowfall {get;private set;}
        public TunnelWeatherController Shelter;
        public float SurfaceTemperature=>Session.Weather==5&&Live?.current!=null?Live.current.temperature_2m:Session.Weather==6?-3:Session.Weather==7?-2:Session.Weather==8?-5:12;
        public WeatherSurfaceState SurfaceAt(Vector3 point)=>WeatherSurfaceRules.Sheltered(new WeatherSurfaceState(Wetness,Ice,Frost,Snow,Snowfall),Shelter?.Index?.Probe(point)??default,SurfaceTemperature);
        public float RainAt(Vector3 point){var e=Shelter?.Index?.Probe(point)??default;return Rain*(e.Covered?e.Rain:1);}
        public WeatherSurfaceState ApplySurface(VehicleController car)
        {var state=SurfaceAt(car.Body.position);car.SurfaceWetness=state.Wet;car.SurfaceSnow=state.Snow;car.SurfaceIce=state.Ice;car.SurfaceFrost=state.Frost;return state;}
        bool surfaceInitialized;int surfaceMode=-1;
        public float Fog=>RenderSettings.fogDensity;public int WeatherRequests {get;private set;}
        public void NetworkConditions(float hour,float rain,float wetness,float clouds,float wind,float fog=.0007f){NetworkOverride=true;networkHour=Mathf.Repeat(hour,24);networkRain=Mathf.Clamp01(rain);networkWetness=Mathf.Clamp01(wetness);networkClouds=Mathf.Clamp01(clouds);networkWind=Mathf.Clamp(wind,0,300);networkFog=Mathf.Clamp(fog,0,.03f);Hour=networkHour;Rain=networkRain;Wetness=networkWetness;Clouds=networkClouds;Wind=networkWind;RenderSettings.fogDensity=networkFog;}
        readonly AstronomicalSky celestial=new AstronomicalSky();
        public Core.CelestialEphemeris.Sky CelestialPosition=>celestial.Position;
        public DateTimeOffset SkyUtc=>celestial.Utc;
        float nextFetch,cycleHour=14,weatherClock;bool fetching;int previousMode=-1;long fetchedAt;UnityWebRequest request;
        string CachePath=>Path.Combine(string.IsNullOrEmpty(PlayerProfile.StorageOverride)?Application.temporaryCachePath:Path.GetDirectoryName(PlayerProfile.StorageOverride),"earth-unscripted-weather-"+(Session.World.Route.Region?.id??"blackwood")+".json");
        public bool NetworkingBusy=>fetching;
        public void CancelLocalFetch(){if(request!=null)request.Abort();}
        void Start(){ReadCache();}
        public static DateTimeOffset Clock(DateTimeOffset utc,string zone,float offset,bool computerLocal=false)
        {
            utc=utc.AddHours(double.IsNaN(offset)||double.IsInfinity(offset)?0:Math.Max(-24,Math.Min(24,offset)));
            DateTimeOffset local;
            if(computerLocal)local=utc.ToLocalTime();
            else
            {
                try{local=TimeZoneInfo.ConvertTime(utc,TimeZoneInfo.FindSystemTimeZoneById(zone));}
                catch(TimeZoneNotFoundException){try{local=TimeZoneInfo.ConvertTime(utc,TimeZoneInfo.FindSystemTimeZoneById(zone=="Europe/London"?"GMT Standard Time":zone=="Europe/Brussels"?"Romance Standard Time":"UTC"));}catch{local=utc;}}
            }
            return local;
        }
        void ReadCache()
        {
            try{if(!File.Exists(CachePath))return;var cache=JsonUtility.FromJson<CachedWeather>(File.ReadAllText(CachePath));if(cache.region==(Session.World.Route.Region?.id??"blackwood")&&Valid(cache.data)){Live=cache.data;fetchedAt=cache.fetched;}}
            catch(Exception){ /* Missing or corrupt cache must never prevent driving. */ }
        }
        static bool Valid(WeatherResponse data)=>data?.current!=null&&data.current.time>0&&!float.IsNaN(data.current.precipitation)&&data.current.precipitation>=0&&data.current.precipitation<1000;
        void Update()
        {
            using var timing=WorkTelemetry.Measure("frame.environment",null,8);
            if(!Session||Session.World==null)return;var p=Session.Profile;int mode=Session.Weather;
            if(mode!=previousMode){previousMode=mode;nextFetch=0;if(mode!=5&&request!=null)request.Abort();}
            if(!NetworkOverride&&!LanSession.HasPendingRejoin&&(!Session.Network||!Session.Network.Active||Session.Network.Hosting)&&mode==5&&!fetching&&Time.unscaledTime>=nextFetch)StartCoroutine(Fetch());
            float dt=Time.deltaTime;
            cycleHour=Mathf.Repeat(cycleHour+dt*24/(Mathf.Clamp(p.dayMinutes,2,120)*60),24);
            if(p.clockMode>=2)
            {
                var now=Clock(DateTimeOffset.UtcNow,Session.World.Route.Region?.timeZone??p.regionTimeZone,p.clockOffset,p.clockMode==3);Hour=(float)now.TimeOfDay.TotalHours;
                ClockStatus=(p.clockMode==3?"Computer local":"Map local")+" "+now.ToString("HH:mm")+(Mathf.Abs(p.clockOffset)>.01f?$" ({p.clockOffset:+0.0;-0.0}h)":"");
            }
            else {Hour=p.clockMode==1?Mathf.Repeat(cycleHour+p.clockOffset,24):Mathf.Repeat(p.fixedHour+p.clockOffset,24);ClockStatus=(p.clockMode==1?"Cycle ":"Fixed ")+TimeSpan.FromHours(Hour).ToString(@"hh\:mm");}
            if(NetworkOverride)Hour=networkHour;
            float clearFog=Session.World.AutomaticWorld ? .00012f : .0007f;
            float targetRain=0,targetCloud=.12f,targetFog=clearFog;Wind=0;
            if(mode==1){targetRain=.72f;targetCloud=.9f;}else if(mode==2){targetCloud=.7f;targetFog=.005f;}else if(mode==3){targetCloud=.3f;if(p.clockMode==0)Hour=18.5f;}
            else if(mode==4)
            {weatherClock+=dt;float wave=Mathf.PerlinNoise(weatherClock/240,7.3f);targetCloud=Mathf.Clamp01((wave-.2f)*1.6f);targetRain=Mathf.Clamp01((wave-.48f)*4);targetFog=Mathf.Lerp(clearFog,.0024f,targetRain);}
            else if(mode==5)
            {
                if(Valid(Live))
                {
                    var current=Live.current;targetRain=Mathf.Clamp01(current.precipitation/5);targetCloud=Mathf.Clamp01(current.cloud_cover/100);Wind=current.wind_speed_10m;
                    if(current.weather_code==45||current.weather_code==48)targetFog=.005f;
                    long age=DateTimeOffset.UtcNow.ToUnixTimeSeconds()-fetchedAt;
                    WeatherStatus=(age<=1800?"Live model":"Cached weather")+" · "+DateTimeOffset.FromUnixTimeSeconds(current.time).UtcDateTime.ToString("HH:mm 'UTC'")+" · "+current.temperature_2m.ToString("F0")+"°C";
                    if(current.weather_code>=71&&current.weather_code<=77||current.weather_code==85||current.weather_code==86)WeatherStatus+=" · Snow";
                }
                else WeatherStatus=fetching?"Fetching live weather…":"Weather unavailable · clear fallback";
            }
            if(mode>=6){targetCloud=mode==6?.9f:.15f;WeatherStatus=mode==6?"Simulated snow":mode==7?"Simulated frost":"Simulated ice";}else if(mode!=5)WeatherStatus=mode==4?"Dynamic simulated weather":"Fixed simulated weather";
            if(NetworkOverride){Hour=networkHour;targetRain=networkRain;targetCloud=networkClouds;Wind=networkWind;WeatherStatus="LAN host weather";ClockStatus="LAN host time "+TimeSpan.FromHours(Hour).ToString(@"hh\:mm");}
            float smoothing=1-Mathf.Exp(-Time.unscaledDeltaTime/12);
            Rain=Mathf.Lerp(Rain,targetRain,smoothing);Clouds=Mathf.Lerp(Clouds,targetCloud,smoothing);
            Wetness=Mathf.MoveTowards(Wetness,Rain>0.03f?1:0,dt*(Rain>0.03f?(.006f+Rain*.02f):.0015f));
            if(NetworkOverride){Rain=networkRain;Clouds=networkClouds;Wetness=networkWetness;}
            UpdateSurfaceWeather(mode);
            ApplyCycleLighting();
            Shader.SetGlobalVector("_EarthUnscriptedWeather",new Vector4(Wetness,Ice,Frost,Snow));
            Shader.SetGlobalFloat("_EarthUnscriptedTunnelExtremeCold",Mathf.InverseLerp(-10,-25,SurfaceTemperature));
            Shader.SetGlobalFloat("_EarthUnscriptedParticleRadiance",SceneryLighting.TreeRadiance(Daylight));
            RenderSettings.fogDensity=NetworkOverride?networkFog:Mathf.Lerp(RenderSettings.fogDensity,Mathf.Max(targetFog,Mathf.Lerp(Session.World.AutomaticWorld ? .00012f : .0007f,.002f,Rain)),smoothing);
            // Spatial weather shaders own the wet sheen; a global glossy base
            // would otherwise leave sheltered tunnel pavement looking wet.
            Session.World.Asphalt.SetFloat("_Smoothness",WeatherSurfaceRules.RoadBaseSmoothness(Session.World.AutomaticWorld||GameGraphics.HighDefinition,Wetness));
            Session.Car.Visual.WiperRain=RainAt(Session.Car.Body.position);
            bool lightsOn=Daylight<.55f||Rain>.2f||mode==2||(Shelter?.Index?.Probe(Session.Car.Body.position).Covered??false);
            Session.Car.Visual.SetAutomaticLights(lightsOn);
            if(Session.Network&&Session.Network.Other)
            {Session.Network.Other.Visual.WiperRain=Rain;Session.Network.Other.Visual.SetAutomaticLights(lightsOn);}
        }
        void UpdateSurfaceWeather(int mode)
        {
            bool reset=!surfaceInitialized||surfaceMode!=mode;surfaceInitialized=true;surfaceMode=mode;
            WeatherSurfaceState state;
            if(mode==5&&Valid(Live))
            {
                float depth=0;var hours=Live.hourly;
                if(hours?.time!=null&&hours.snow_depth!=null)
                {int nearest=-1;long gap=long.MaxValue;for(int i=0;i<hours.time.Length&&i<hours.snow_depth.Length;i++){long delta=Math.Abs(hours.time[i]-Live.current.time);if(delta<gap){gap=delta;nearest=i;}}if(nearest>=0&&gap<7200)depth=Mathf.Max(0,hours.snow_depth[nearest]);}
                var c=Live.current;state=WeatherSurfaceRules.Live(c.temperature_2m,c.relative_humidity_2m,c.dew_point_2m,c.precipitation,c.snowfall,depth,c.weather_code,Wetness);
            }
            else state=WeatherSurfaceRules.Simulated(mode,mode==1?1:Wetness);
            float blend=reset?1:1-Mathf.Exp(-Time.unscaledDeltaTime/8);
            Ice=Mathf.Lerp(Ice,state.Ice,blend);Frost=Mathf.Lerp(Frost,state.Frost,blend);Snow=Mathf.Lerp(Snow,state.Snow,blend);Snowfall=Mathf.Lerp(Snowfall,state.Snowfall,blend);
            if(reset||mode==5&&Valid(Live)&&Live.current.precipitation>.03f)Wetness=state.Wet;
            if(Snowfall>.01f)Rain=0;
        }
        void ApplyCycleLighting()
        {
            // UTC ephemeris uses map coordinates and the selected local calendar date.
            // A directional light's forward vector is the direction of its rays:
            // a source above the horizon must therefore have a negative forward.y.
            celestial.Update(Session,Hour,Clouds,Daylight);
            var solar=AstronomicalSky.Vector(celestial.Position.Sun);var lunar=AstronomicalSky.Vector(celestial.Position.Moon);
            float altitude=solar.y;
            Daylight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.12f,.35f,altitude));
            if(Session.World.Atmosphere)Session.World.Atmosphere.SetDaylight(Daylight);
            var sun=Session.World.Sun;var moon=Session.World.Moon;
            float sunlight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.18f,altitude));
            float moonlight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.18f,lunar.y));
            sun.transform.rotation=Quaternion.LookRotation(-solar,Mathf.Abs(solar.y)>.999f?Vector3.forward:Vector3.up);
            sun.intensity=2.1f*sunlight*Mathf.Lerp(1,.43f,Clouds)*(GameGraphics.HighDefinition?50000:1);
            // Streaming worlds have no baked bounce light. Keep daylight shade
            // readable with ambient fill and softer contrast, without another light.
            sun.shadowStrength=Mathf.Lerp(1,Mathf.Lerp(.78f,.50f,Clouds),Daylight);
            sun.color=Color.Lerp(new Color(1,.64f,.37f),new Color(1,.94f,.83f),Mathf.Clamp01(altitude*2));
            if(moon)
            {
                moon.transform.rotation=Quaternion.LookRotation(-lunar,Mathf.Abs(lunar.y)>.999f?Vector3.forward:Vector3.up);
                moon.intensity=(GameGraphics.HighDefinition?.22f:.055f)*(float)celestial.Position.Illumination*moonlight*(1-Daylight)*Mathf.Lerp(1,.22f,Clouds);
                moon.color=new Color(.48f,.62f,.95f);
            }
            // URP and the procedural sky use this dominant directional light.
            // The handover occurs where both light energies approach zero, so
            // switching source direction cannot produce a bright horizon jump.
            RenderSettings.sun=moon&&moon.intensity>sun.intensity?moon:sun;
            RenderSettings.ambientSkyColor=Color.Lerp(new Color(.008f,.013f,.025f),new Color(.51f,.64f,.78f),Daylight)*Mathf.Lerp(1,.7f,Clouds);
            RenderSettings.ambientEquatorColor=Color.Lerp(new Color(.005f,.008f,.016f),new Color(.48f,.52f,.55f),Daylight);
            RenderSettings.ambientGroundColor=Color.Lerp(new Color(.0025f,.004f,.008f),new Color(.28f,.30f,.27f),Daylight);
            RenderSettings.reflectionIntensity=Mathf.Lerp(.045f,1,Daylight)*Mathf.Lerp(1,.7f,Clouds);
            RenderSettings.fogColor=Color.Lerp(new Color(.012f,.019f,.034f),Color.Lerp(new Color(.65f,.75f,.82f),new Color(.46f,.5f,.53f),Clouds),Daylight);
            if(RenderSettings.skybox)
            {
                var sky=RenderSettings.skybox;
                if(sky.HasProperty("_Exposure"))sky.SetFloat("_Exposure",Mathf.Lerp(.035f,1.05f,Daylight));
                if(sky.HasProperty("_SkyTint"))sky.SetColor("_SkyTint",Color.Lerp(new Color(.13f,.22f,.42f),new Color(.5f,.5f,.5f),Daylight));
                if(sky.HasProperty("_GroundColor"))sky.SetColor("_GroundColor",Color.Lerp(new Color(.003f,.005f,.012f),new Color(.33f,.35f,.32f),Daylight));
            }
        }
        IEnumerator Fetch()
        {
            fetching=true;WeatherRequests++;nextFetch=Time.unscaledTime+900;var region=Session.World.Route.Region;
            double lat=region?.latitude??52.969,lon=region?.longitude??-3.171;
            string url="https://api.open-meteo.com/v1/forecast?latitude="+lat.ToString("F3",CultureInfo.InvariantCulture)+"&longitude="+lon.ToString("F3",CultureInfo.InvariantCulture)+"&current=temperature_2m,precipitation,rain,weather_code,cloud_cover,wind_speed_10m,is_day,relative_humidity_2m,dew_point_2m,snowfall&hourly=snow_depth&past_hours=1&forecast_hours=1&timezone=auto&timeformat=unixtime";
            using(var req=UnityWebRequest.Get(url))
            {
                request=req;req.timeout=10;yield return req.SendWebRequest();
                if(Session.Weather==5&&req.result==UnityWebRequest.Result.Success&&req.downloadHandler.data.Length<100000)
                {
                    WeatherResponse data=null;try{data=JsonUtility.FromJson<WeatherResponse>(req.downloadHandler.text);}catch(Exception){}
                    if(Valid(data))
                    {
                        Live=data;fetchedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        try{File.WriteAllText(CachePath,JsonUtility.ToJson(new CachedWeather{region=region?.id??"blackwood",fetched=fetchedAt,data=data}));}catch(Exception){}
                    }
                }
                else nextFetch=Time.unscaledTime+120;
                request=null;
            }
            fetching=false;
        }
        void OnDestroy(){Shader.SetGlobalVector("_EarthUnscriptedWeather",Vector4.zero);celestial.Dispose();if(request!=null)request.Abort();}
    }
}
