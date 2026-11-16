using System;
namespace RoadRush.Core
{
    /// <summary>Allocation-free cross-plane V8: separate irregular bank pulses,
    /// low exhaust pressure, crank rumble and filtered intake under load.</summary>
    public sealed class EngineSoundSynth
    {
        static readonly bool[] RightBank={false,true,true,false,true,false,false,true};
        double firingPhase,left,right,leftPipe,rightPipe,dc,crankPhase,intake;
        int firingIndex;uint noise=194516;
        public static float OffroadRpm(float metresPerSecond,float load,float slip)
        {
            float mph=Math.Max(0,metresPerSecond)*2.23694f;
            float ratio=mph<22?215:mph<45?112:mph<75?73:52;
            return Math.Max(850,Math.Min(6800,900+mph*ratio+Math.Max(0,load)*450+Math.Max(0,slip)*950));
        }
        public float Sample(float rpm,float load,int sampleRate)
        {
            if(rpm<=0||sampleRate<=0)return 0;
            load=Math.Max(0,Math.Min(1,load));
            firingPhase+=rpm*4d/(60*sampleRate);
            if(firingPhase>=1)
            {
                firingPhase-=1;
                double impulse=.72+load*.65;
                if(RightBank[firingIndex])right+=impulse;else left+=impulse;
                firingIndex=(firingIndex+1)%8;
            }
            double decay=Math.Exp(-75d/sampleRate);left*=decay;right*=decay;
            // Keep the low bank-pressure pulses. The old 130 Hz high-pass
            // discarded their weight and left a thin firing-frequency buzz.
            leftPipe+=(left-leftPipe)*(1-Math.Exp(-2*Math.PI*340/sampleRate));
            rightPipe+=(right-rightPipe)*(1-Math.Exp(-2*Math.PI*290/sampleRate));
            double pressure=leftPipe+rightPipe*.91;
            dc+=(pressure-dc)*(1-Math.Exp(-2*Math.PI*22/sampleRate));
            crankPhase+=rpm/(60d*sampleRate);crankPhase-=Math.Floor(crankPhase);
            noise^=noise<<13;noise^=noise>>17;noise^=noise<<5;
            double random=((noise/(double)uint.MaxValue)*2-1);
            intake+=(random-intake)*(1-Math.Exp(-2*Math.PI*950/sampleRate));
            double exhaust=(pressure-dc)*.66+Math.Sin(crankPhase*Math.PI*2)*.16;
            return (float)Math.Tanh(exhaust+intake*load*.12);
        }
    }
}
