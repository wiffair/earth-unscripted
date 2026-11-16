using System;

namespace RoadRush.Core
{
    // Engine-independent rules: metres, seconds, metres/second throughout.
    public static class DrivingMath
    {
        public const float MphPerMetreSecond = 2.2369363f;
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        public static float Deadzone(float value, float deadzone)
        {
            deadzone = Clamp(deadzone, 0, .95f);
            return Math.Abs(value) <= deadzone ? 0 : Math.Sign(value) * (Math.Abs(value) - deadzone) / (1 - deadzone);
        }
        public static float Calibrate(float raw, float rest, float pressed)
        {
            if (Math.Abs(pressed - rest) < .05f) return 0;
            return Clamp((raw - rest) / (pressed - rest), 0, 1);
        }
        public static float Steering(float raw, float left, float centre, float right)
        {
            return (raw-centre)*(right-centre) >= 0
                ? Calibrate(raw, centre, right)
                : -Calibrate(raw, centre, left);
        }
        public static float Wrap(float distance, float length) => length <= 0 ? 0 : (distance % length + length) % length;
        public static float SignedRouteDelta(float from,float to,float length) => length<=0?0:Wrap(to-from+length*.5f,length)-length*.5f;
        // Forces expire in the native driver too; never latch an infinite force.
        public static float WheelTorque(float steering, float lateralSlip, float speed, float strength, bool active)
        {
            if (!active || Math.Abs(speed) < .5f) return 0;
            var speedFactor = Clamp(Math.Abs(speed) / 14, 0, 1);
            return Clamp((-steering * .55f - lateralSlip * .035f) * speedFactor * Clamp(strength, 0, .65f), -.65f, .65f);
        }
    }

    public sealed class AutomaticPedals
    {
        public bool Reverse { get; private set; }
        public float Power { get; private set; }
        public float Brake { get; private set; }
        float stoppedHold;
        public void Reset(){Reverse=false;Power=Brake=stoppedHold=0;}
        public void Tick(float throttle,float brake,float speed,float dt,bool handbrake)
        {
            Power=Brake=0;
            throttle=DrivingMath.Clamp(throttle,0,1);brake=DrivingMath.Clamp(brake,0,1);
            if(throttle>.05f&&brake>.05f){Brake=Math.Max(throttle,brake);stoppedHold=0;return;}
            bool wantsReverse=brake>.05f;float pedal=wantsReverse?brake:throttle;
            if(pedal<=.05f){stoppedHold=0;return;}
            if(wantsReverse!=Reverse)
            {
                Brake=pedal;
                if(Math.Abs(speed)>.35f||handbrake){stoppedHold=0;return;}
                stoppedHold+=dt;
                if(stoppedHold<.35f)return;
                Reverse=wantsReverse;stoppedHold=0;
            }
            else stoppedHold=0;
            Power=(Reverse?-1:1)*pedal;Brake=0;
        }
    }

    public sealed class DriveEconomy
    {
        public int Credits { get; private set; }
        public int Skill { get; private set; }
        public int Streak { get; private set; }
        public float CleanMetres { get; private set; }
        float creditMetres, skillMetres, incidentCooldown;
        public DriveEconomy(int credits = 1200) { Credits = Math.Max(0, Math.Min(10000000, credits)); }
        public void Tick(float dt, float speed, bool onRoad, bool withinLimit, bool wanted, bool grounded)
        {
            if (dt <= 0 || dt > .11f || float.IsNaN(speed) || float.IsInfinity(speed)) return;
            incidentCooldown = Math.Max(0, incidentCooldown - dt);
            if (!onRoad || !withinLimit || wanted || !grounded || speed < 2 || incidentCooldown > 0)
            { Streak = 0; skillMetres = 0; return; }
            var metres = Math.Min(speed, 70) * dt;
            CleanMetres += metres; creditMetres += metres; skillMetres += metres;
            while (creditMetres >= 100) { creditMetres -= 100; AddCredits(30); }
            while (skillMetres >= 50) { skillMetres -= 50; Streak++; Skill += 10 * Math.Min(5, 1 + Streak / 10); }
        }
        public void Incident(int penalty)
        {
            Credits = Math.Max(0, Credits - Math.Max(0, penalty));
            Streak = 0; skillMetres = 0; incidentCooldown = 8;
        }
        public void AddCredits(int amount) { Credits = (int)Math.Max(0L, Math.Min(10000000L, (long)Credits + amount)); }
        public bool Spend(int amount)
        {
            if (amount < 0 || Credits < amount) return false;
            Credits -= amount; return true;
        }
    }

    public sealed class CheckpointRun
    {
        public int Next { get; private set; }
        public bool Active { get; private set; }
        public bool Finished { get; private set; }
        public float Elapsed { get; private set; }
        public float Penalties { get; private set; }
        public float Result => Elapsed + Penalties;
        readonly int count;
        public CheckpointRun(int checkpointCount) { count = Math.Max(2, checkpointCount); }
        public void Start() { Next = 0; Active = true; Finished = false; Elapsed = Penalties = 0; }
        public void Cancel() { Active = false; Finished = false; }
        public void Tick(float dt) { if (Active && dt > 0 && dt <= .11f) Elapsed += dt; }
        public void Collision() { if (Active) Penalties += 3; }
        public bool Pass(int index, bool forwards)
        {
            if (!Active || index != Next || !forwards) return false;
            Next++;
            if (Next == count) { Active = false; Finished = true; }
            return true;
        }
        public string Medal(float gold, float silver) => Result <= gold ? "GOLD" : Result <= silver ? "SILVER" : "BRONZE";
    }
}
