using System;
namespace EarthUnscripted
{
    // Distance policy only; never grants a spawn, reset or physical-world authority.
    public sealed class PoliceEncounterSchedule
    {
        public const double MinimumTravelMetres=800,MeanExtraTravelMetres=2500;
        public double RemainingMetres {get;private set;}
        public bool Due=>RemainingMetres<=0;
        public PoliceEncounterSchedule(double uniform){Reset(uniform);}
        public void Reset(double uniform)
        {if(double.IsNaN(uniform)||double.IsInfinity(uniform)||uniform<0||uniform>=1)throw new ArgumentOutOfRangeException(nameof(uniform));RemainingMetres=MinimumTravelMetres-MeanExtraTravelMetres*Math.Log(1-uniform);}
        public void Observe(double metres,bool eligible)
        {if(eligible&&!double.IsNaN(metres)&&!double.IsInfinity(metres)&&metres>0)RemainingMetres=Math.Max(0,RemainingMetres-metres);}
    }
}
