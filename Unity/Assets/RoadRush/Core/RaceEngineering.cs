using System;
using System.Globalization;
namespace RoadRush.Core
{
    public enum RaceCompound { Soft, Medium, Hard, Intermediate, Wet }
    [Serializable] public sealed class RaceSetup
    {
        public float finalDrive=1,brakeBias=.56f,spring=1,differential=.45f,frontWing=.5f,rearWing=.6f;
        public RaceCompound compound=RaceCompound.Medium;
        public RaceSetup Clone()=>(RaceSetup)MemberwiseClone();
        static float Bound(float v,float low,float high,float fallback)=>float.IsNaN(v)||float.IsInfinity(v)?fallback:Math.Max(low,Math.Min(high,v));
        public void Validate(){finalDrive=Bound(finalDrive,.8f,1.2f,1);brakeBias=Bound(brakeBias,.48f,.65f,.56f);spring=Bound(spring,.7f,1.3f,1);differential=Bound(differential,.1f,.9f,.45f);frontWing=Bound(frontWing,0,1,.5f);rearWing=Bound(rearWing,0,1,.6f);if((int)compound<0||(int)compound>4)compound=RaceCompound.Medium;}
        public string Identity(){var s=Clone();s.Validate();return "race-setup1|"+(int)s.compound+"|"+string.Join("|",Array.ConvertAll(new[]{s.finalDrive,s.brakeBias,s.spring,s.differential,s.frontWing,s.rearWing},v=>v.ToString("R",CultureInfo.InvariantCulture)));}
    }
    [Serializable] public sealed class RaceTyreState
    {
        public RaceCompound compound=RaceCompound.Medium;public float temperatureC=20,wear;
        public RaceTyreState Clone()=>(RaceTyreState)MemberwiseClone();
        public void Validate(){if((int)compound<0||(int)compound>4)compound=RaceCompound.Medium;if(float.IsNaN(temperatureC)||float.IsInfinity(temperatureC))temperatureC=20;temperatureC=Math.Max(-50,Math.Min(200,temperatureC));if(float.IsNaN(wear)||float.IsInfinity(wear))wear=0;wear=Math.Max(0,Math.Min(1,wear));}
        static readonly float[] Dry={1.12f,1.05f,1,.84f,.70f},Wet={.44f,.47f,.50f,1.04f,1.13f},WarmSeconds={30,45,65,35,40},LifeSeconds={900,1500,2200,1300,1100};
        static float Clamp(float v,float low,float high)=>Math.Max(low,Math.Min(high,v));
        void Check(){if((int)compound<0||(int)compound>4||float.IsNaN(temperatureC)||float.IsInfinity(temperatureC)||float.IsNaN(wear)||float.IsInfinity(wear)||wear<0||wear>1)throw new ArgumentOutOfRangeException("tyre state");}
        public float Grip(float wetness){Check();if(float.IsNaN(wetness)||float.IsInfinity(wetness))throw new ArgumentOutOfRangeException(nameof(wetness));int c=(int)compound;float w=Clamp(wetness,0,1);float target=c>=3?60:90;float warmth=Clamp(1-Math.Abs(temperatureC-target)/180,.65f,1);return (Dry[c]*(1-w)+Wet[c]*w)*warmth*(1-.35f*wear);}
        public void Tick(float seconds,float speed,float slip,float wetness,float ambientC)
        {
            Check();foreach(float v in new[]{seconds,speed,slip,wetness,ambientC})if(float.IsNaN(v)||float.IsInfinity(v))throw new ArgumentOutOfRangeException();if(seconds<0||seconds>1||speed<0||slip<0||ambientC< -50||ambientC>60)throw new ArgumentOutOfRangeException();int c=(int)compound;float load=Clamp(speed/35,0,1)*Clamp(.3f+slip,0,2);float target=ambientC+load*(c>=3?45:80);temperatureC+=(target-temperatureC)*(1-(float)Math.Exp(-seconds/WarmSeconds[c]));float mismatch=c>=3?1+3*(1-Clamp(wetness,0,1)):1;wear=Clamp(wear+seconds*load*mismatch/LifeSeconds[c],0,1);
        }
        public void Replace(RaceCompound next,float ambientC){if((int)next<0||(int)next>4||float.IsNaN(ambientC)||float.IsInfinity(ambientC)||ambientC< -50||ambientC>60)throw new ArgumentOutOfRangeException();compound=next;wear=0;temperatureC=ambientC;}
        public string RecordClass(){Check();return "tyre-class1|"+(int)compound+"|wear"+(int)(wear*10)+"|"+(temperatureC<50?"cold":temperatureC>115?"hot":"working");}
        public string ConditionKey(){Check();return "tyre-condition1|"+(int)compound+"|"+temperatureC.ToString("R",CultureInfo.InvariantCulture)+"|"+wear.ToString("R",CultureInfo.InvariantCulture);}
    }
    public enum RacePitPhase { Idle, BringTyres, RemoveWheels, FitWheels, ReturnOldTyres, Complete, Aborted }
    // Pure staged transaction. Caller locks car, animates crew/props, and commits exactly once on Complete.
    public sealed class RacePitTransaction
    {
        public RacePitPhase Phase {get;private set;}public float Elapsed {get;private set;}public RaceSetup Pending {get;private set;}
        public bool Locked=>Phase>=RacePitPhase.BringTyres&&Phase<=RacePitPhase.ReturnOldTyres;
        public bool Start(RaceSetup requested,bool stationary,bool inBay){if(Locked||requested==null||!stationary||!inBay)return false;Pending=requested.Clone();Pending.Validate();Elapsed=0;Phase=RacePitPhase.BringTyres;return true;}
        public bool Tick(float dt,bool stationary,bool inBay){if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<0||dt>.25f)throw new ArgumentOutOfRangeException(nameof(dt));if(!Locked)return false;if(!stationary||!inBay){Phase=RacePitPhase.Aborted;Pending=null;return false;}Elapsed+=dt;Phase=Elapsed<1.5f?RacePitPhase.BringTyres:Elapsed<3?RacePitPhase.RemoveWheels:Elapsed<4.5f?RacePitPhase.FitWheels:Elapsed<6?RacePitPhase.ReturnOldTyres:RacePitPhase.Complete;return Phase==RacePitPhase.Complete;}
        public RaceSetup Commit(){if(Phase!=RacePitPhase.Complete||Pending==null)return null;var result=Pending.Clone();Pending=null;return result;}
    }
}
