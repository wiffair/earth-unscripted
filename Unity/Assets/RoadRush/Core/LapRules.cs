using System;
namespace RoadRush.Core
{
    public enum LapEvent { None,Started,Sector,Finished,Aborted }
    public sealed class LapTracker
    {
        readonly float length;readonly int gates;readonly RacingRuleSet rules;float previous,offTrack,split1,split2;bool primed,limitsApplied;
        public bool Armed {get;private set;} public bool Valid {get;private set;}=true;
        public int NextGate {get;private set;} public int CompletedLaps {get;private set;} public int Serial {get;private set;}
        double elapsed;public float Elapsed=>(float)elapsed;public float CrossingFraction {get;private set;} public float PeakSpeed {get;private set;} public float Penalties {get;private set;}
        public float LastTime {get;private set;} public float LastPeak {get;private set;} public bool LastValid {get;private set;}
        public float LastSector {get;private set;} public int Sector {get;private set;}
        public readonly float[] LastSectors=new float[3];public string Reason {get;private set;}="Cross the start line";
        public LapTracker(float length,int gates=24){this.length=Math.Max(100,length);this.gates=Math.Max(6,gates/3*3);rules=RacingRuleSet.LegacyDefaults;}
        public LapTracker(float length,RacingRuleSet settings):this(length,settings?.Gates??24){rules=settings??throw new ArgumentNullException(nameof(settings));}
        public void Reset(float progress)
        {previous=DrivingMath.Wrap(progress,length);primed=true;Armed=false;NextGate=CompletedLaps=Serial=0;elapsed=0;PeakSpeed=Penalties=LastTime=LastPeak=offTrack=split1=split2=0;Valid=true;Reason="Cross the start line";limitsApplied=false;}
        public void Invalidate(string reason){if(Armed){Valid=false;Reason=reason;}}
        public void Recover(float progress){previous=DrivingMath.Wrap(progress,length);primed=true;if(rules.RecoveryInvalidates)Invalidate("Recovery used");Penalties+=rules.RecoveryPenalty;}
        void StartLap(float remaining,bool onTrack){Armed=true;NextGate=1;elapsed=remaining;PeakSpeed=offTrack=split1=split2=0;Valid=onTrack;limitsApplied=false;Reason=onTrack?"Valid lap":"Track limits";Sector=1;Serial++;}
        public LapEvent Tick(float dt,float progress,bool onTrack,float speed,bool grounded)
        {
            if(dt<=0||dt>.11f||float.IsNaN(progress)||float.IsInfinity(progress)||float.IsNaN(speed)||float.IsInfinity(speed))return LapEvent.None;
            progress=DrivingMath.Wrap(progress,length);if(!primed){Reset(progress);return LapEvent.None;}
            float old=previous;previous=progress;if(Armed)elapsed+=dt;
            if(Armed&&onTrack&&grounded)PeakSpeed=Math.Max(PeakSpeed,Math.Max(0,speed));
            if(Armed){offTrack=onTrack?0:offTrack+dt;if(offTrack>rules.OffTrackGrace){Invalidate("Track limits");if(!limitsApplied){Penalties+=rules.TrackLimitPenalty;limitsApplied=true;}}}
            float delta=DrivingMath.SignedRouteDelta(old,progress,length);
            if(Math.Abs(delta)>Math.Max(5,Math.Abs(speed)*dt*2+1)){Armed=false;NextGate=0;Valid=false;Reason="Course discontinuity · restart at the line";elapsed=0;return LapEvent.Aborted;}
            if(delta<=.00001f||speed<.5f)return LapEvent.None;
            float startGap=DrivingMath.Wrap(-old,length);bool startCross=startGap<=delta+.002f&&(startGap>.00001f||!Armed||NextGate==0);
            if(startCross)
            {
                float fraction=DrivingMath.Clamp(startGap/delta,0,1);float remaining=dt*(1-fraction);CrossingFraction=fraction;
                if(Armed&&NextGate==0)
                {
                    LastTime=(float)(elapsed-remaining);LastPeak=PeakSpeed;LastValid=Valid&&onTrack;LastSectors[0]=split1;LastSectors[1]=Math.Max(0,split2-split1);LastSectors[2]=Math.Max(0,LastTime-split2);CompletedLaps++;
                    StartLap(remaining,onTrack);return LapEvent.Finished;
                }
                StartLap(remaining,onTrack);return LapEvent.Started;
            }
            if(!Armed||NextGate==0)return LapEvent.None;
            float gap=DrivingMath.Wrap(NextGate*length/gates-old,length);if(gap>delta+.002f)return LapEvent.None;
            if(!onTrack){Armed=false;NextGate=0;Reason="Missed checkpoint · restart at the line";Valid=false;return LapEvent.Aborted;}
            int passed=NextGate;NextGate=(NextGate+1)%gates;
            float time=Elapsed-dt*(1-DrivingMath.Clamp(gap/delta,0,1));
            if(passed==gates/3){split1=time;LastSector=time;Sector=2;return LapEvent.Sector;}
            if(passed==gates*2/3){split2=time;LastSector=time-split1;Sector=3;return LapEvent.Sector;}
            return LapEvent.None;
        }
    }
    [Serializable] public sealed class TrialRecord
    {
        public string key="",track="",car="",conditions="",utc="";public float lap,peak;public float[] sectors=new float[3];
        public TrialRecord Clone()=>new TrialRecord{key=key,track=track,car=car,conditions=conditions,utc=utc,lap=lap,peak=peak,sectors=sectors==null?new float[3]:(float[])sectors.Clone()};
    }
    public static class TrialRules
    {
        public static string SetupKey(string track,int mode,OwnedCar car,string conditions)
        {string parts="";for(int i=0;i<6;i++)parts+=car.Level(i).ToString();return track+"|"+mode+"|"+car.model+"|"+parts+"|damage"+(int)(car.damage/20)+"|"+conditions+"|rules2"+(car.model==3?"|"+(car.raceSetup??new RaceSetup()).Identity()+"|"+(car.raceTyres??new RaceTyreState()).RecordClass():"");}
        public static bool Better(TrialRecord existing,float lap,float peak,bool speedTrial)=>existing==null||(speedTrial?peak>existing.peak+.1f:lap<existing.lap-.05f);
    }
}
