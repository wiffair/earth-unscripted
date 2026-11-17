using System;

namespace EarthUnscripted.Radio
{
    // Offline gain only: no nonlinear distortion, live limiter or audio-thread allocation.
    // Gate silent sections out of level measurement and reserve headroom for the game mix.
    public static class RadioAudioPolicy
    {
        public static bool NaturalEnd(int lastSample,int samples,int frequency,bool playing,bool configurationChanged,double dsp,double expectedEndDsp)
        {
            if(configurationChanged)return false;
            return lastSample>=samples-Math.Max(1,frequency/10)||!playing&&expectedEndDsp>0&&dsp>=expectedEndDsp-.02;
        }
        public sealed class Level { public float inputPeak,outputPeak,gain,gatedRms; }
        public static Level Normalize(float[] samples,int channels,int frequency,bool music)
        {
            if(samples==null||channels<1||frequency<1||samples.Length%channels!=0)throw new ArgumentException("Invalid radio PCM");
            int frames=samples.Length/channels;var dc=new double[channels];
            for(int i=0;i<samples.Length;i++){float value=samples[i];if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("Non-finite radio PCM");dc[i%channels]+=value;}
            for(int c=0;c<channels;c++)dc[c]/=Math.Max(1,frames);
            int block=Math.Max(channels,frequency*channels*2/5);double maximumEnergy=0;float peak=0;
            for(int i=0;i<samples.Length;i++){samples[i]-=(float)dc[i%channels];peak=Math.Max(peak,Math.Abs(samples[i]));}
            for(int start=0;start<samples.Length;start+=block){int end=Math.Min(samples.Length,start+block);double energy=0;for(int i=start;i<end;i++)energy+=(double)samples[i]*samples[i];maximumEnergy=Math.Max(maximumEnergy,energy/Math.Max(1,end-start));}
            double total=0;int count=0;
            for(int start=0;start<samples.Length;start+=block){int end=Math.Min(samples.Length,start+block);double energy=0;for(int i=start;i<end;i++)energy+=(double)samples[i]*samples[i];if(energy/Math.Max(1,end-start)>=maximumEnergy*.01){total+=energy;count+=end-start;}}
            float rms=(float)Math.Sqrt(total/Math.Max(1,count));float ceiling=music?.65f:.55f,target=music?.14f:.11f;
            float gain=peak<.000001f?1:Math.Min(4,Math.Min(ceiling/peak,target/Math.Max(.000001f,rms)));
            for(int i=0;i<samples.Length;i++)samples[i]*=gain;
            return new Level{inputPeak=peak,outputPeak=peak*gain,gain=gain,gatedRms=rms*gain};
        }
    }
}
