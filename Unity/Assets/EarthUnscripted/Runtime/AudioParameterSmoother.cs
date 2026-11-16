using System;
namespace EarthUnscripted
{
    // A few milliseconds of sample-domain interpolation remove frame-boundary
    // steps without changing engine character, bump haptics or impact damage.
    public static class AudioParameterSmoother
    {
        public static float Coefficient(int sampleRate,float milliseconds)
            => (float)(1-Math.Exp(-1/(Math.Max(8000,sampleRate)*milliseconds*.001)));
        public static float Step(float current,float target,float coefficient)
            => current+(target-current)*coefficient;
    }
}
