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
        // Three passes over one decoded, not-yet-playing clip. Only channel DC
        // sums and 400ms energies are retained, never an entire managed song.
        public sealed class BoundedNormalization
        {
            public const int MaximumSamples=64*1024*1024;
            public readonly int Channels,Frequency,Frames,WindowSamples;
            readonly int samples;readonly double[] dc,energies;
            int phase,position;float peak;Level level;
            public Level Result=>phase>=2?level:throw new InvalidOperationException("Radio level is not ready");
            public int RetainedDoubles=>dc.Length+energies.Length;
            public BoundedNormalization(int frames,int channels,int frequency)
            {
                if(frames<=0||channels<1||channels>8||frequency<8000||frequency>192000||(long)frames*channels>MaximumSamples)
                    throw new ArgumentException("Invalid bounded radio PCM dimensions");
                Frames=frames;Channels=channels;Frequency=frequency;samples=frames*channels;
                WindowSamples=Math.Max(channels,frequency*channels*2/5);
                dc=new double[channels];energies=new double[(samples+WindowSamples-1)/WindowSamples];
            }
            void Validate(float[] buffer,int count,int expectedPhase)
            {
                if(phase!=expectedPhase||buffer==null||count<=0||count>buffer.Length||count%Channels!=0||count>samples-position)
                    throw new ArgumentException("Invalid bounded radio PCM pass/block");
            }
            public void AccumulateDc(float[] buffer,int count)
            {
                Validate(buffer,count,0);
                for(int i=0;i<count;i++)
                {float value=buffer[i];if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("Non-finite radio PCM");dc[(position+i)%Channels]+=value;}
                position+=count;
            }
            public void FinishDc()
            {
                if(phase!=0||position!=samples)throw new InvalidOperationException("Incomplete radio DC pass");
                for(int c=0;c<Channels;c++)dc[c]/=Frames;phase=1;position=0;
            }
            public void Measure(float[] buffer,int count)
            {
                Validate(buffer,count,1);
                for(int i=0;i<count;i++)
                {
                    // Float rounding and chronological energy accumulation match Normalize.
                    float value=buffer[i]-(float)dc[(position+i)%Channels];
                    if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("Non-finite radio PCM");
                    peak=Math.Max(peak,Math.Abs(value));energies[(position+i)/WindowSamples]+=(double)value*value;
                }
                position+=count;
            }
            public Level FinishMeasurements(bool music)
            {
                if(phase!=1||position!=samples)throw new InvalidOperationException("Incomplete radio energy pass");
                double maximumEnergy=0;for(int w=0;w<energies.Length;w++)maximumEnergy=Math.Max(maximumEnergy,energies[w]/Math.Min(WindowSamples,samples-w*WindowSamples));
                double total=0;int count=0;
                for(int w=0;w<energies.Length;w++)
                {int length=Math.Min(WindowSamples,samples-w*WindowSamples);if(energies[w]/length>=maximumEnergy*.01){total+=energies[w];count+=length;}}
                float rms=(float)Math.Sqrt(total/Math.Max(1,count));float ceiling=music?.65f:.55f,target=music?.14f:.11f;
                float gain=peak<.000001f?1:Math.Min(4,Math.Min(ceiling/peak,target/Math.Max(.000001f,rms)));
                level=new Level{inputPeak=peak,outputPeak=peak*gain,gain=gain,gatedRms=rms*gain};phase=2;position=0;return level;
            }
            public void Apply(float[] buffer,int count)
            {
                Validate(buffer,count,2);
                for(int i=0;i<count;i++){float value=buffer[i]-(float)dc[(position+i)%Channels];buffer[i]=value*level.gain;}
                position+=count;
            }
            public void FinishApply()
            {if(phase!=2||position!=samples)throw new InvalidOperationException("Incomplete radio gain pass");phase=3;}
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
