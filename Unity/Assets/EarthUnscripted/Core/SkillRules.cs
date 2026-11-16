using System;
using System.Collections.Generic;
namespace EarthUnscripted.Core
{
    public static class TyreRules
    {
        public static float PowerSlip(float driveForce,float normalLoad,float grip)
            =>DrivingMath.Clamp((Math.Abs(driveForce)-Math.Max(100,normalLoad*1.05f*grip))/(Math.Max(100,normalLoad*1.05f*grip)),0,1.5f);
    }
    public sealed class DriftSkills
    {
        public int Total {get;private set;} public float Combo {get;private set;} public int Multiplier {get;private set;}=1;
        public string Feedback {get;private set;}=""; public int LastCredits {get;private set;}
        readonly Dictionary<long,float> awarded=new Dictionary<long,float>();
        float clock,duration,recovery,anchorX,anchorZ,lastAngle,bankCooldown;bool tracking;
        static long Cell(float x,float z)=>((long)(int)Math.Floor(x/60)<<32)|(uint)(int)Math.Floor(z/60);
        public void Cancel(){Combo=duration=recovery=0;tracking=false;Multiplier=1;Feedback="Combo lost";bankCooldown=2;}
        public int Tick(float dt,float speed,float angle,float x,float z,bool grounded,bool allowed)
        {
            LastCredits=0;if(dt<=0||dt>.11f||float.IsNaN(angle)||float.IsNaN(speed)||float.IsInfinity(speed))return 0;
            clock+=dt;bankCooldown=Math.Max(0,bankCooldown-dt);float absolute=Math.Abs(angle);
            bool drift=grounded&&allowed&&speed>8&&absolute>=12&&absolute<=55&&bankCooldown<=0;
            if(!allowed||!grounded||absolute>70||speed<3){if(Combo>0)Cancel();return 0;}
            if(drift)
            {
                if(!tracking){tracking=true;anchorX=x;anchorZ=z;}
                duration+=dt;recovery=0;
                if(Math.Sign(angle)!=Math.Sign(lastAngle)&&duration>1)Multiplier=Math.Min(4,Multiplier+1);
                if(Math.Abs(angle)>15)lastAngle=angle;
                Combo=Math.Min(1200,Combo+dt*(absolute-8)*Multiplier*2);Feedback="DRIFT ×"+Multiplier;return 0;
            }
            if(!tracking)return 0;
            recovery+=dt;
            if(recovery<1.5f)return 0;
            float dx=x-anchorX,dz=z-anchorZ;int points=(int)Combo;long key=Cell(x,z);
            bool fresh=!awarded.TryGetValue(key,out float time)||clock-time>600;
            if(duration>=.7f&&dx*dx+dz*dz>=225&&absolute<10&&speed>6&&fresh)
            {
                Total+=points;LastCredits=Math.Min(20,points/20);awarded[key]=clock;
                // Bound the anti-farming history. Clearing oldest entries only after a
                // very long trip cannot help a stationary donut repeatedly cash out.
                if(awarded.Count>1024){long oldest=0;float stamp=float.MaxValue;foreach(var p in awarded)if(p.Value<stamp){oldest=p.Key;stamp=p.Value;}awarded.Remove(oldest);}
                Feedback="DRIFT BANKED +"+points+" · +"+LastCredits+" credits";
            }
            else Feedback=fresh?"Recover cleanly and travel farther to bank":"Explore new road for another bonus";
            Combo=duration=recovery=0;tracking=false;Multiplier=1;bankCooldown=1;return LastCredits;
        }
    }
}
