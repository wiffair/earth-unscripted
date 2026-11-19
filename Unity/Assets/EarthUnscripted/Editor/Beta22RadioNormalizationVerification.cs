using System;
using EarthUnscripted.Radio;
namespace EarthUnscripted.Editor
{
    public static class Beta22RadioNormalizationVerification
    {
        public static int Exercise()
        {
            int checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception("Bounded radio normalization: "+message);}
            void Reject(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
            foreach(int channels in new[]{1,2,3,8})foreach(int frequency in new[]{8000,8001,44100,192000})foreach(bool music in new[]{false,true})
            {
                int frames=channels==3?30001:8193;var original=new float[frames*channels];
                for(int i=0;i<original.Length;i++){int f=i/channels;original[i]=(i%channels)*.02f+.3f*(float)Math.Sin(f*.031)+(f<1000?.6f:0);if(f>frames-777)original[i]=(i%channels)*.02f;}
                var expected=(float[])original.Clone();var oracle=RadioAudioPolicy.Normalize(expected,channels,frequency,music);
                var bounded=new RadioAudioPolicy.BoundedNormalization(frames,channels,frequency);const int blockFrames=1027;
                for(int pass=0;pass<3;pass++)
                {
                    for(int f=0;f<frames;f+=blockFrames)
                    {
                        int count=Math.Min(blockFrames,frames-f)*channels;var buffer=new float[count];Array.Copy(original,f*channels,buffer,0,count);
                        if(pass==0)bounded.AccumulateDc(buffer,count);else if(pass==1)bounded.Measure(buffer,count);else{bounded.Apply(buffer,count);Array.Copy(buffer,0,original,f*channels,count);}
                    }
                    if(pass==0)bounded.FinishDc();else if(pass==1){var level=bounded.FinishMeasurements(music);Check(level.inputPeak==oracle.inputPeak&&level.outputPeak==oracle.outputPeak&&level.gain==oracle.gain&&level.gatedRms==oracle.gatedRms,"exact peak/gated RMS/gain over odd block and channel boundaries");}else bounded.FinishApply();
                }
                bool same=true;for(int i=0;i<original.Length;i++)if(original[i]!=expected[i]){same=false;break;}Check(same,"every normalized sample exactly equals unchanged oracle");
            }
            foreach(float value in new[]{0f,.3f})
            {
                var pcm=new float[8192*2];for(int i=0;i<pcm.Length;i++)pcm[i]=value;var b=new RadioAudioPolicy.BoundedNormalization(8192,2,48000);b.AccumulateDc(pcm,pcm.Length);b.FinishDc();b.Measure(pcm,pcm.Length);var level=b.FinishMeasurements(true);b.Apply(pcm,pcm.Length);b.FinishApply();Check(level.outputPeak==0&&level.gain==1&&Array.TrueForAll(pcm,x=>x==0),"silence and constant DC remain silence");
            }
            Reject(()=>new RadioAudioPolicy.BoundedNormalization(0,2,48000),"empty");Reject(()=>new RadioAudioPolicy.BoundedNormalization(1,9,48000),"channels");Reject(()=>new RadioAudioPolicy.BoundedNormalization(1,2,7999),"low rate");Reject(()=>new RadioAudioPolicy.BoundedNormalization(1,2,192001),"high rate");Reject(()=>new RadioAudioPolicy.BoundedNormalization(int.MaxValue,8,48000),"sample multiplication overflow");
            var maximum=new RadioAudioPolicy.BoundedNormalization(64*1024*1024,1,8000);Check(maximum.RetainedDoubles<=20973,"largest allowed clip retains at most167784 bytes of double measurements");
            var invalid=new RadioAudioPolicy.BoundedNormalization(1,2,48000);Reject(()=>invalid.FinishDc(),"short pass");Reject(()=>invalid.AccumulateDc(new float[3],3),"non-frame-aligned block");Reject(()=>invalid.Measure(new float[2],2),"wrong pass");Reject(()=>invalid.AccumulateDc(new[]{float.NaN,0f},2),"NaN");Reject(()=>new RadioAudioPolicy.BoundedNormalization(1,1,8000).AccumulateDc(new[]{float.PositiveInfinity},1),"infinity");
            return checks;
        }
        public static void Run()=>Console.WriteLine("BETA22_RADIO_NORMALIZATION_PASS "+Exercise());
    }
}
