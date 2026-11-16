using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace EarthUnscripted
{
    // Memory-only diagnostics for an explicitly requested isolated native proof.
    // Disabled during normal play; never opens a profile or writes a log itself.
    public static class WorkTelemetry
    {
        const int Limit=2048;
        static readonly bool Requested=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedAppearanceProof")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaSmoke")>=0;
        static readonly Queue<Event> events=new Queue<Event>();
        public static int Dropped { get; private set; }
        [Serializable] public sealed class Event
        {
            public string operation,context;public int frame,renderedFrame;public double realtimeSeconds,milliseconds;
        }
        public readonly struct Scope : IDisposable
        {
            readonly string operation,context;readonly double minimumMilliseconds;readonly long started;readonly int frame,renderedFrame;readonly double realtime;
            internal Scope(string name,string detail,double minimum)
            {
                bool enabled=Requested&&!string.IsNullOrEmpty(PlayerProfile.StorageOverride);
                operation=enabled?name:null;context=detail;minimumMilliseconds=minimum;started=enabled?Stopwatch.GetTimestamp():0;
                frame=enabled?Time.frameCount:0;renderedFrame=enabled?Time.renderedFrameCount:0;
                realtime=enabled?Time.realtimeSinceStartupAsDouble:0;
            }
            public void Dispose()
            {
                if(operation==null)return;double duration=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;if(duration<minimumMilliseconds)return;
                if(events.Count==Limit){events.Dequeue();Dropped++;}
                events.Enqueue(new Event{operation=operation,context=context,frame=frame,renderedFrame=renderedFrame,realtimeSeconds=realtime,milliseconds=duration});
            }
        }
        public static bool Enabled=>Requested&&!string.IsNullOrEmpty(PlayerProfile.StorageOverride);
        public static IEnumerator WrapCoroutine(IEnumerator routine)=>Enabled?TraceCoroutine(routine):routine;
        static IEnumerator TraceCoroutine(IEnumerator routine)
        {
            try
            {
                while(true)
                {
                    bool more;object current=null;
                    using(Measure("coroutine."+routine.GetType().Name,null,8))
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
