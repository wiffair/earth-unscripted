using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace EarthUnscripted
{
    // Bounded memory-only diagnostics, enabled explicitly in settings or an isolated proof.
    // Never opens a profile or writes a log itself.
    public static class WorkTelemetry
    {
        const int Limit=2048;
        static readonly bool Requested=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedLightweightTiming")<0 && (Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedDisableWorkTelemetry")<0 && (Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedAppearanceProof")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaSmoke")>=0));
        static readonly Queue<Event> events=new Queue<Event>();
        public static int Dropped { get; private set; }
        static bool runtimeEnabled;
        static readonly bool ExplicitlyDisabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedDisableWorkTelemetry")>=0;
        public static void SetRuntimeEnabled(bool value)
        {if(runtimeEnabled==value)return;runtimeEnabled=value;if(!value)Clear();}
        [Serializable] public sealed class Event
        {
            public string operation,context;public int frame,renderedFrame;public double realtimeSeconds,milliseconds;public long allocatedBytes,allocationCount;
        }
        public readonly struct Scope : IDisposable
        {
            readonly string operation,context;readonly double minimumMilliseconds;readonly long started,allocationStart;readonly int frame,renderedFrame;readonly double realtime;
            internal Scope(string name,string detail,double minimum)
            {
                bool enabled=Enabled;
                operation=enabled?name:null;context=detail;minimumMilliseconds=minimum;started=enabled?Stopwatch.GetTimestamp():0;
                frame=enabled?Time.frameCount:0;renderedFrame=enabled?Time.renderedFrameCount:0;
                realtime=enabled?Time.realtimeSinceStartupAsDouble:0;
                allocationStart=enabled?GC.GetAllocatedBytesForCurrentThread():0;
            }
            public void Dispose()
            {
                if(operation==null)return;double duration=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
                long allocated=GC.GetAllocatedBytesForCurrentThread()-allocationStart,allocationCount=-1;
                // Release players do not expose the native GC.Alloc counter.
                // Creating/destroying one for every measured scope contends on
                // the profiler's native mutex and can create the hitch measured.
                // Dedicated allocation tests own their separate recorder lifetime.
                // Mono may also return zero for .NET bytes: report unavailable.
                if(allocated==0)allocated=-1;
                if(duration<minimumMilliseconds&&allocated<1024*1024&&allocationCount<1024)return;
                if(events.Count==Limit){events.Dequeue();Dropped++;}
                events.Enqueue(new Event{operation=operation,context=context,frame=frame,renderedFrame=renderedFrame,realtimeSeconds=realtime,milliseconds=duration,allocatedBytes=allocated,allocationCount=allocationCount});
            }
        }
        public static bool Enabled=>!ExplicitlyDisabled&&(runtimeEnabled||(Requested&&!string.IsNullOrEmpty(PlayerProfile.StorageOverride)));
        public static IEnumerator WrapCoroutine(IEnumerator routine)=>Enabled?TraceCoroutine(routine):routine;
        static IEnumerator TraceCoroutine(IEnumerator routine)
        {
            string operation="coroutine."+routine.GetType().Name;
            try
            {
                while(true)
                {
                    bool more;object current=null;
                    using(Measure(operation,null,8))
                    {more=routine.MoveNext();if(more)current=routine.Current;}
                    if(!more)yield break;
                    if(current is IEnumerator nested)yield return TraceCoroutine(nested);else yield return current;
                }
            }
            finally{(routine as IDisposable)?.Dispose();}
        }
        public static Scope Measure(string operation,string context=null,double minimumMilliseconds=0)=>new Scope(operation,context,minimumMilliseconds);
        public static Event[] Capture()=>events.ToArray();
        public static void Clear(){events.Clear();Dropped=0;}
    }
}
