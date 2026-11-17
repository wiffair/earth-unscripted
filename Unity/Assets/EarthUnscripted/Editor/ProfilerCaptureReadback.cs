using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    // Offline analysis only. Never attached to an owner session.
    public static class ProfilerCaptureReadback
    {
        public static void Batch()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();
                string Arg(string name){int at=Array.IndexOf(args,name);if(at<0||at+1>=args.Length)throw new ArgumentException(name);return Path.GetFullPath(args[at+1]);}
                string input=Arg("-earthProfilerInput"),output=Arg("-earthProfilerOutput");
                ProfilerDriver.enabled=false;
                typeof(ProfilerDriver).GetMethod("SetMaxFrameHistoryLength",BindingFlags.Static|BindingFlags.NonPublic)?.Invoke(null,new object[]{2000});
                if(!ProfilerDriver.LoadProfile(input,false))throw new IOException("Profiler capture could not load");
                var frames=new List<Tuple<int,double,double>>();
                for(int index=ProfilerDriver.firstFrameIndex;index<=ProfilerDriver.lastFrameIndex;index++)
                    using(var data=ProfilerDriver.GetRawFrameDataView(index,0))
                        if(data.valid)frames.Add(Tuple.Create(index,(double)data.frameTimeMs,(double)data.frameStartTimeMs));
                var rows=new JArray();
                double first=frames.Min(f=>f.Item3);
                foreach(var frame in frames.Where(f=>f.Item3-first>18000).OrderByDescending(f=>f.Item2).Take(24))
                    using(var data=ProfilerDriver.GetRawFrameDataView(frame.Item1,0))
                    {
                        var samples=new JArray();
                        for(int i=0;i<data.sampleCount;i++)
                        {
                            double ms=data.GetSampleTimeMs(i);if(ms<2)continue;
                            samples.Add(new JObject{{"index",i},{"name",data.GetSampleName(i)},{"milliseconds",ms},{"children",data.GetSampleChildrenCount(i)}});
                        }
                        rows.Add(new JObject{{"frame",frame.Item1},{"milliseconds",frame.Item2},{"elapsedSeconds",(frame.Item3-first)/1000},{"samples",samples}});
                    }
                var report=new JObject{{"firstFrame",ProfilerDriver.firstFrameIndex},{"lastFrame",ProfilerDriver.lastFrameIndex},{"validFrames",frames.Count},{"worstFrames",rows}};
                File.WriteAllText(output,report.ToString());Debug.Log("PROFILER READBACK PASS "+frames.Count+" frames");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
