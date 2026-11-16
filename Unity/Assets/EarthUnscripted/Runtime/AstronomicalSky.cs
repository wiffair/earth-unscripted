using System;
using System.Globalization;
using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    /// <summary>Owns the game sky material. Position/time calculations are cached
    /// for one second; local sky glow is a bounded settlement-light estimate.</summary>
    public sealed class AstronomicalSky : IDisposable
    {
        Material owned,previous;
        public static Material CurrentMaterial {get;private set;}
        double nextCalculation;
        public CelestialEphemeris.Sky Position {get;private set;}
        public DateTimeOffset Utc {get;private set;}
        public float Pollution {get;private set;}
        public static DateTimeOffset GameUtc(PlayerProfile profile,float hour,string zone)
        {
            var now=EnvironmentController.Clock(DateTimeOffset.UtcNow,zone,0,profile.clockMode==3);
            if(profile.clockMode>=2&&string.IsNullOrEmpty(profile.worldDate))return DateTimeOffset.UtcNow.AddHours(profile.clockOffset);
            DateTime day=now.Date;
            if(DateTime.TryParseExact(profile.worldDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var chosen))day=chosen.Date;
            var local=DateTime.SpecifyKind(day.AddHours(hour),DateTimeKind.Unspecified);
            TimeSpan offset=now.Offset;
            try{offset=TimeZoneInfo.FindSystemTimeZoneById(zone).GetUtcOffset(local);}catch(TimeZoneNotFoundException){}
            return new DateTimeOffset(local,offset).ToUniversalTime();
        }
        public void Update(GameSession session,float hour,float clouds,float daylight)
        {
            var region=session.World.Route.Region;
            if(Time.realtimeSinceStartupAsDouble>=nextCalculation)
            {
                nextCalculation=Time.realtimeSinceStartupAsDouble+1;
                Utc=GameUtc(session.Profile,hour,region?.timeZone??session.Profile.regionTimeZone);
                double latitude=region?.latitude??52.969,longitude=region?.longitude??-3.171;
                if(region?.AutomaticWorld==true)
                {
                    latitude+=session.Car.Body.position.z/111194.9266;
                    longitude+=session.Car.Body.position.x/(111194.9266*Math.Cos(region.latitude*Math.PI/180));
                }
                Position=CelestialEphemeris.Calculate(Utc,latitude,longitude);
                int buildings=0;
                foreach(var building in region?.buildings??Array.Empty<GeoBuilding>())
                {
                    if(building.points==null||building.points.Length==0)continue;
                    var delta=building.points[0]-session.Car.Body.position;delta.y=0;
                    if(delta.sqrMagnitude<600*600&&++buildings>=100)break;
                }
                Pollution=Mathf.InverseLerp(3,65,buildings);
                LatitudeRadians=(float)(latitude*Math.PI/180);
            }
            if(!owned)
            {
                previous=RenderSettings.skybox;
                var shader=Resources.Load<Shader>("AstronomicalSky");if(!shader)return;
                owned=new Material(shader){name="Sun moon phase and rural stars"};
                var photograph=previous&&previous.HasProperty("_MainTex")?previous.GetTexture("_MainTex") as Texture2D:null;
                if(!photograph){var scenery=Resources.Load<AutomaticSceneryResources>("AutomaticWorld/Scenery");photograph=scenery?scenery.sky:null;}
                if(photograph){owned.SetTexture("_MainTex",photograph);owned.SetFloat("_UsePanorama",1);}
                RenderSettings.skybox=owned;
            }
            owned.SetVector("_SunDirection",Vector(Position.Sun));owned.SetVector("_MoonDirection",Vector(Position.Moon));
            owned.SetFloat("_Daylight",daylight);owned.SetFloat("_Clouds",clouds);owned.SetFloat("_Pollution",Pollution);
            owned.SetFloat("_MoonPhase",(float)Position.Illumination);owned.SetFloat("_Latitude",LatitudeRadians);
            owned.SetFloat("_Sidereal",(float)(Position.LocalSiderealDegrees*Math.PI/180));
            CurrentMaterial=owned;
        }
        float LatitudeRadians;
        public static Vector3 Vector(CelestialEphemeris.Direction direction)=>new Vector3((float)direction.East,(float)direction.Up,(float)direction.North);
        public void Dispose(){if(RenderSettings.skybox==owned)RenderSettings.skybox=previous;if(CurrentMaterial==owned)CurrentMaterial=null;if(owned)UnityEngine.Object.Destroy(owned);}
    }
}
