using System;
namespace RoadRush.Core
{
    // AI driver planning only. These limits never alter player forces or geographic heights.
    public static class RaceDrivingRules
    {
        static void Finite(float value){if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException();}
        public static float CornerSpeed(float curvature,float friction,float aeroPerMass,float margin)
        {
            Finite(curvature);Finite(friction);Finite(aeroPerMass);Finite(margin);
            if(curvature<0||friction<=0||aeroPerMass<0||margin<=0||margin>1)throw new ArgumentOutOfRangeException();
            float denominator=curvature-friction*aeroPerMass;
            return denominator<=.00001f?150:Math.Min(150,(float)Math.Sqrt(friction*9.81f/denominator)*margin);
        }
        public static float PreviewMetres(float speed,float brakingAcceleration)
        {Finite(speed);Finite(brakingAcceleration);if(speed<0||brakingAcceleration<=0)throw new ArgumentOutOfRangeException();return Math.Max(70,Math.Min(1024,speed*speed/(2*brakingAcceleration)+30));}
        public static float VisibleStoppingSpeed(float brakingAcceleration){Finite(brakingAcceleration);if(brakingAcceleration<=0)throw new ArgumentOutOfRangeException();return (float)Math.Sqrt(2*brakingAcceleration*(1024-30));}
        public static float RearThrottleBudget(float speed,float curvature,float friction,float aeroPerMass,float mass,float powerFactor,float lateralSlip,float powerSlip)
        {
            Finite(speed);Finite(curvature);Finite(friction);Finite(aeroPerMass);Finite(mass);Finite(powerFactor);Finite(lateralSlip);Finite(powerSlip);
            if(speed<0||curvature<0||friction<=0||aeroPerMass<0||mass<=0||powerFactor<=0||lateralSlip<0||powerSlip<0)throw new ArgumentOutOfRangeException();
            float load=mass*(9.81f+aeroPerMass*speed*speed)*.25f;
            float limit=load*friction;float lateral=mass*speed*speed*curvature*.25f;
            float reserve=(float)Math.Sqrt(Math.Max(0,limit*limit-lateral*lateral));
            float budget=Math.Min(1,reserve/(4300*powerFactor));
            // Do not keep accelerating into a measured slide or powered wheelspin.
            budget*=Math.Max(0,1-Math.Min(1,powerSlip/.3f));
            budget*=Math.Max(0,1-Math.Min(1,lateralSlip/3));
            return Math.Max(0,Math.Min(1,budget));
        }
    }
}
