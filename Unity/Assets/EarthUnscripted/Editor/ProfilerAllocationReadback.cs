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
    // Opt-in, offline diagnostic. This is never attached to the game or a save.
    public static class ProfilerAllocationReadback
    {
        sealed class Group { public ulong[] stack; public string[] methods; public string thread; public long bytes,samples; }
        public static void Batch()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();
                string Arg(string name){int at=Array.IndexOf(args,name);if(at<0||at+1>=args.Length)throw new ArgumentException(name);return Path.GetFullPath(args[at+1]);}
                string input=Arg("-earthProfilerInput"),output=Arg("-earthProfilerOutput");if(File.Exists(output))throw new IOException("Preserve existing diagnostic output");
                ProfilerDriver.enabled=false;
                typeof(ProfilerDriver).GetMethod("SetMaxFrameHistoryLength",BindingFlags.Static|BindingFlags.NonPublic)?.Invoke(null,new object[]{12000});
                if(!ProfilerDriver.LoadProfile(input,false))throw new IOException("Profiler capture could not load");
                var groups=new Dictionary<ulong,List<Group>>();var methods=new Dictionary<ulong,string>();var stack=new List<ulong>();var threadTotals=new Dictionary<string,long[]>();var frames=new JArray();var collections=new JArray();
                long total=0,samples=0,withoutStackBytes=0,withoutStackSamples=0,droppedGroupBytes=0;int groupCount=0,validFrames=0,maxThreads=0;double first=double.NaN,last=0;
                for(int frame=ProfilerDriver.firstFrameIndex;frame<=ProfilerDriver.lastFrameIndex;frame++)
                {
                    long frameBytes=0,frameSamples=0;double ms=0,elapsed=0;int threadCount=0;
                    for(int thread=0;thread<256;thread++)using(var data=ProfilerDriver.GetRawFrameDataView(frame,thread))
                    {
                        if(!data.valid)break;threadCount++;
                        if(thread==0){validFrames++;if(double.IsNaN(first))first=data.frameStartTimeMs;last=data.frameStartTimeMs;ms=data.frameTimeMs;elapsed=(last-first)/1000;}
                        string threadName=data.threadName??("thread-"+thread);if(!threadTotals.TryGetValue(threadName,out var totals))threadTotals[threadName]=totals=new long[2];
                        int marker=data.GetMarkerId("GC.Alloc");
                        for(int sample=0;sample<data.sampleCount;sample++)
                        {
                            if(data.GetSampleMarkerId(sample)!=marker||marker<0)
                            {
                                double cost=data.GetSampleTimeMs(sample);if(cost<.5)continue;string name=data.GetSampleName(sample);if(string.IsNullOrEmpty(name))continue;
                                if(name.IndexOf("GC.Collect",StringComparison.Ordinal)>=0||name.IndexOf("GarbageCollect",StringComparison.Ordinal)>=0)
                                    if(collections.Count<10000)collections.Add(new JObject{{"frame",frame},{"thread",threadName},{"name",name},{"milliseconds",cost}});
                                continue;
                            }
                            long bytes=data.GetSampleMetadataCount(sample)>0?data.GetSampleMetadataAsLong(sample,0):0;if(bytes<0)throw new InvalidDataException("Negative GC.Alloc metadata");
                            total+=bytes;samples++;frameBytes+=bytes;frameSamples++;totals[0]+=bytes;totals[1]++;
                            stack.Clear();data.GetSampleCallstack(sample,stack);
                            if(stack.Count==0){withoutStackBytes+=bytes;withoutStackSamples++;continue;}
                            ulong hash=14695981039346656037UL;foreach(ulong addr in stack)hash=unchecked((hash^addr)*1099511628211UL);foreach(char ch in threadName)hash=unchecked((hash^ch)*1099511628211UL);
                            Group match=null;if(groups.TryGetValue(hash,out var bucket))foreach(var candidate in bucket)if(candidate.thread==threadName&&candidate.stack.SequenceEqual(stack)){match=candidate;break;}
                            if(match==null)
                            {
                                if(groupCount>=10000){droppedGroupBytes+=bytes;continue;}
                                var names=new string[stack.Count];for(int i=0;i<stack.Count;i++)
                                {
                                    ulong addr=stack[i];if(!methods.TryGetValue(addr,out string name))
                                    {var method=data.ResolveMethodInfo(addr);name=string.IsNullOrEmpty(method.methodName)?"unresolved:0x"+addr.ToString("x"):method.methodName;methods[addr]=name;}
                                    names[i]=name;
                                }
                                match=new Group{stack=stack.ToArray(),methods=names,thread=threadName};if(bucket==null)groups[hash]=bucket=new List<Group>();bucket.Add(match);groupCount++;
                            }
                            match.bytes+=bytes;match.samples++;
                        }
                    }
                    maxThreads=Math.Max(maxThreads,threadCount);if(threadCount>0)frames.Add(new JObject{{"frame",frame},{"elapsedSeconds",elapsed},{"milliseconds",ms},{"allocationBytes",frameBytes},{"allocationSamples",frameSamples}});
                }
                var report=new JObject{{"definition","All retained frames/threads. Allocation call stacks change runtime cost; this is not a normal FPS baseline. Allocation volume alone does not prove a GC pause. Profiler activation may affect the first frames."},{"firstFrame",ProfilerDriver.firstFrameIndex},{"lastFrame",ProfilerDriver.lastFrameIndex},{"validFrames",validFrames},{"loadedWindowSeconds",(last-first)/1000},{"maximumThreads",maxThreads},{"allocationBytes",total},{"allocationSamples",samples},{"withoutStackBytes",withoutStackBytes},{"withoutStackSamples",withoutStackSamples},{"unretainedGroupBytes",droppedGroupBytes},{"stackGroups",groupCount},{"threads",new JArray(threadTotals.OrderByDescending(v=>v.Value[0]).Select(v=>new JObject{{"thread",v.Key},{"bytes",v.Value[0]},{"samples",v.Value[1]}}))},{"topStacks",new JArray(groups.Values.SelectMany(v=>v).OrderByDescending(v=>v.bytes).Take(80).Select(v=>new JObject{{"thread",v.thread},{"bytes",v.bytes},{"samples",v.samples},{"methods",new JArray(v.methods)}}))},{"collectionSamples",collections},{"worstFrames",new JArray(frames.OrderByDescending(v=>(double)v["milliseconds"]).Take(32))}};
                File.WriteAllText(output,report.ToString());Debug.Log("PROFILER ALLOCATION READBACK PASS "+validFrames+" frames, "+total+" bytes, "+groupCount+" stack groups");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
