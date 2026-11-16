using System;
using System.IO;
using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted.Editor
{
    public static class Beta43PolicyVerification
    {
        public static void Run()
        {
            int checks=0;void Need(bool ok,string message){checks++;if(!ok)throw new IOException("Beta43: "+message);}
            foreach(int rate in new[]{44100,48000,96000})
            {
                var quiet=new AudioPeakLimiter(rate);int frames=rate/4;var input=new float[frames*2];
                for(int i=0;i<frames;i++){input[i*2]=.3f*(float)Math.Sin(i*.05);input[i*2+1]=input[i*2]*.4f;}
                var original=(float[])input.Clone();quiet.Process(input,2);
                float error=0;for(int i=quiet.DelayFrames;i<frames;i++)error=Math.Max(error,Math.Abs(input[i*2]-original[(i-quiet.DelayFrames)*2]));
                Need(error<.000001f,"quiet mix is delayed without waveshaping or gain reduction");
                var loud=new AudioPeakLimiter(rate);var over=new float[frames*2];
                for(int i=0;i<frames;i++){over[i*2]=i==rate/10?4:2.2f*(float)Math.Sin(i*.04);over[i*2+1]=over[i*2]*.25f;}
                loud.Process(over,2);Need(loud.InputPeak>2&&loud.OutputPeak<=AudioPeakLimiter.Ceiling+.000001f&&loud.LimitedFrames>0,"summed mix peaks are gain-limited before device clipping");
                float balance=0;for(int i=0;i<frames;i++)balance=Math.Max(balance,Math.Abs(over[i*2+1]-over[i*2]*.25f));
                Need(balance<.000001f,"linked gain preserves stereo image");
                var blocked=new AudioPeakLimiter(rate);var full=new AudioPeakLimiter(rate);var data=new float[frames*2];for(int i=0;i<data.Length;i++)data[i]=(float)Math.Sin(i*.123)*1.8f;
                var whole=(float[])data.Clone();full.Process(whole,2);
                for(int offset=0;offset<data.Length;){int size=Math.Min(254,data.Length-offset);var block=new float[size];Array.Copy(data,offset,block,0,size);blocked.Process(block,2);Array.Copy(block,0,data,offset,size);offset+=size;}
                Need(System.Linq.Enumerable.SequenceEqual(data,whole),"limiter output is independent of audio callback block size");
                var bad=new float[]{float.NaN,float.PositiveInfinity,0,0};new AudioPeakLimiter(rate).Process(bad,2);Need(Array.TrueForAll(bad,v=>!float.IsNaN(v)&&!float.IsInfinity(v)),"invalid decoded samples cannot poison the mixer");
            }
            Debug.Log("BETA43_POLICY_PASS "+checks+" checks");
        }
    }
}
