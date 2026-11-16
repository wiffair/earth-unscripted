using System;

namespace RoadRush.Core
{
    // Linked-channel peak gain control with 5ms of lookahead. Quiet samples are
    // delayed unchanged; peaks reduce gain rather than flattening the waveform.
    // Construct on the main thread. Process performs no allocations or Unity calls.
    public sealed class AudioPeakLimiter
    {
        public const float Ceiling=.94f;
        const int MaxChannels=16;
        readonly int delay,capacity;
        readonly float[] samples,peaks;
        readonly long[] positions;
        readonly float attack,release;
        int ring,head,tail,channels;
        long frame;
        float gain=1;
        public float InputPeak {get;private set;}
        public float OutputPeak {get;private set;}
        public long LimitedFrames {get;private set;}
        public long ProcessedFrames=>frame;
        public int DelayFrames=>delay;
        public AudioPeakLimiter(int sampleRate)
        {
            sampleRate=Math.Max(8000,sampleRate);delay=Math.Max(1,sampleRate/200);capacity=delay+2;
            samples=new float[delay*MaxChannels];peaks=new float[capacity];positions=new long[capacity];
            attack=1-(float)Math.Exp(-1d/(sampleRate*.0003));release=1-(float)Math.Exp(-1d/(sampleRate*.075));
        }
        public void Process(float[] data,int channelCount)
        {
            if(channelCount<1||channelCount>MaxChannels||data.Length%channelCount!=0)throw new ArgumentException("Audio channels");
            if(channels!=0&&channels!=channelCount){Array.Clear(samples,0,samples.Length);head=tail=ring=0;gain=1;}
            channels=channelCount;
            for(int i=0;i<data.Length;i+=channels,frame++)
            {
                float peak=0;
                for(int c=0;c<channels;c++)
                {float value=data[i+c];if(float.IsNaN(value)||float.IsInfinity(value))value=data[i+c]=0;peak=Math.Max(peak,Math.Abs(value));}
                InputPeak=Math.Max(InputPeak,peak);
                while(head!=tail&&positions[head]<frame-delay)head=(head+1)%capacity;
                while(head!=tail&&peaks[(tail+capacity-1)%capacity]<=peak)tail=(tail+capacity-1)%capacity;
                peaks[tail]=peak;positions[tail]=frame;tail=(tail+1)%capacity;
                float target=peaks[head]>Ceiling?Ceiling/peaks[head]:1;
                gain+=(target-gain)*(target<gain?attack:release);
                int at=ring*MaxChannels;float delayedPeak=0;
                for(int c=0;c<channels;c++)delayedPeak=Math.Max(delayedPeak,Math.Abs(samples[at+c]));
                // Numerical safety for the very first unexpected transient. One
                // gain is shared across channels, so stereo balance is preserved.
                if(delayedPeak*gain>Ceiling)gain=Ceiling/delayedPeak;
                if(gain<.999f)LimitedFrames++;
                for(int c=0;c<channels;c++)
                {float value=samples[at+c]*gain;samples[at+c]=data[i+c];data[i+c]=value;OutputPeak=Math.Max(OutputPeak,Math.Abs(value));}
                ring=(ring+1)%delay;
            }
        }
    }
}
