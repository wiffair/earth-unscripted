using System;

namespace EarthUnscripted.Core
{
    /// <summary>Low-precision solar/lunar ephemeris, degrees and UTC. Keplerian
    /// elements with the principal lunar perturbations and observer parallax.
    /// Suitable for game sky placement, not navigation. Formula references:
    /// https://stjarnhimlen.se/comp/ppcomp.html and NOAA solar position notes.</summary>
    public static class CelestialEphemeris
    {
        const double Radians=Math.PI/180;
        public readonly struct Direction
        {
            public readonly double East,Up,North;
            public Direction(double east,double up,double north){East=east;Up=up;North=north;}
        }
        public readonly struct Sky
        {
            public readonly Direction Sun,Moon;
            public readonly double Illumination,Phase,LocalSiderealDegrees;
            public Sky(Direction sun,Direction moon,double illumination,double phase,double sidereal)
            {Sun=sun;Moon=moon;Illumination=illumination;Phase=phase;LocalSiderealDegrees=sidereal;}
        }
        static double Sin(double angle)=>Math.Sin(angle*Radians);
        static double Cos(double angle)=>Math.Cos(angle*Radians);
        static double Atan(double y,double x)=>Math.Atan2(y,x)/Radians;
        static double Wrap(double angle)=>angle-360*Math.Floor(angle/360);
        static double Eccentric(double mean,double eccentricity)
        {
            double anomaly=Wrap(mean)*Radians,estimate=anomaly;
            for(int iteration=0;iteration<7;iteration++)estimate-=(estimate-eccentricity*Math.Sin(estimate)-anomaly)/(1-eccentricity*Math.Cos(estimate));
            return estimate/Radians;
        }
        static Direction Horizontal(double x,double y,double z,double latitude,double sidereal,bool lunar)
        {
            if(lunar)
            {
                // Earth-radius lunar coordinates minus the observer's position.
                double geocentric=Atan(.99664719*Sin(latitude),Cos(latitude));
                double rho= .9983271+.0016764*Cos(2*latitude);
                x-=rho*Cos(geocentric)*Cos(sidereal);y-=rho*Cos(geocentric)*Sin(sidereal);z-=rho*Sin(geocentric);
            }
            double radius=Math.Sqrt(x*x+y*y+z*z);x/=radius;y/=radius;z/=radius;
            double meridian=x*Cos(sidereal)+y*Sin(sidereal);
            double east=-x*Sin(sidereal)+y*Cos(sidereal);
            return new Direction(east,meridian*Cos(latitude)+z*Sin(latitude),-meridian*Sin(latitude)+z*Cos(latitude));
        }
        public static Sky Calculate(DateTimeOffset utc,double latitude,double longitude)
        {
            double days=(utc.ToUniversalTime()-new DateTimeOffset(1999,12,31,0,0,0,TimeSpan.Zero)).TotalDays;
            double obliquity=23.4393-3.563e-7*days;
            double solarMean=Wrap(356.0470+.9856002585*days),solarPerihelion=282.9404+4.70935e-5*days;
            double eccentricity=.016709-1.151e-9*days,solarE=Eccentric(solarMean,eccentricity);
            double sx=Cos(solarE)-eccentricity,sy=Math.Sqrt(1-eccentricity*eccentricity)*Sin(solarE);
            double solarLongitude=Atan(sy,sx)+solarPerihelion;
            double sunX=Cos(solarLongitude),sunY=Sin(solarLongitude)*Cos(obliquity),sunZ=Sin(solarLongitude)*Sin(obliquity);
            double node=125.1228-.0529538083*days,perihelion=318.0634+.1643573223*days,mean=Wrap(115.3654+13.0649929509*days);
            double lunarE=Eccentric(mean,.0549),lx=60.2666*(Cos(lunarE)-.0549),ly=60.2666*Math.Sqrt(1-.0549*.0549)*Sin(lunarE);
            double anomaly=Atan(ly,lx),radius=Math.Sqrt(lx*lx+ly*ly);
            double x=radius*(Cos(node)*Cos(anomaly+perihelion)-Sin(node)*Sin(anomaly+perihelion)*Cos(5.1454));
            double y=radius*(Sin(node)*Cos(anomaly+perihelion)+Cos(node)*Sin(anomaly+perihelion)*Cos(5.1454));
            double z=radius*Sin(anomaly+perihelion)*Sin(5.1454);
            double longitudeMoon=Atan(y,x),latitudeMoon=Atan(z,Math.Sqrt(x*x+y*y));
            double elongation=mean+perihelion+node-solarMean-solarPerihelion,argument=mean+perihelion;
            longitudeMoon+=-1.274*Sin(mean-2*elongation)+.658*Sin(2*elongation)-.186*Sin(solarMean)-.059*Sin(2*mean-2*elongation)-.057*Sin(mean-2*elongation+solarMean)+.053*Sin(mean+2*elongation)+.046*Sin(2*elongation-solarMean)+.041*Sin(mean-solarMean)-.035*Sin(elongation)-.031*Sin(mean+solarMean)-.015*Sin(2*argument-2*elongation)+.011*Sin(mean-4*elongation);
            latitudeMoon+=-.173*Sin(argument-2*elongation)-.055*Sin(mean-argument-2*elongation)-.046*Sin(mean+argument-2*elongation)+.033*Sin(argument+2*elongation)+.017*Sin(2*mean+argument);
            radius+=-.58*Cos(mean-2*elongation)-.46*Cos(2*elongation);
            x=radius*Cos(longitudeMoon)*Cos(latitudeMoon);y=radius*Sin(longitudeMoon)*Cos(latitudeMoon);z=radius*Sin(latitudeMoon);
            double moonX=x,moonY=y*Cos(obliquity)-z*Sin(obliquity),moonZ=y*Sin(obliquity)+z*Cos(obliquity);
            double sidereal=Wrap(280.46061837+360.98564736629*(days-1.5)+longitude);
            double cosine=(moonX*sunX+moonY*sunY+moonZ*sunZ)/radius;
            return new Sky(Horizontal(sunX,sunY,sunZ,latitude,sidereal,false),Horizontal(moonX,moonY,moonZ,latitude,sidereal,true),Math.Max(0,Math.Min(1,(1-cosine)/2)),Wrap(longitudeMoon-solarLongitude)/360,sidereal);
        }
    }
}
