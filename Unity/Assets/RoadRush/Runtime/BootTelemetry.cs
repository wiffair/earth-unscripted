using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace RoadRush
{
    // Disabled by default. No file writes, profile access or worker engine calls.
    public static class BootTelemetry
    {
        public static bool Enabled {get;private set;}
        [ThreadStatic]static Trace current;
        static readonly long origin=Stopwatch.GetTimestamp();static int nextId,sceneRequest;
        static readonly List<Trace> traces=new List<Trace>();static readonly Queue<Event> sceneEvents=new Queue<Event>();static int sceneDropped;
        public static Trace Current=>current;
        public static double Now=>(Stopwatch.GetTimestamp()-origin)*1000d/Stopwatch.Frequency;
        [Serializable]public sealed class Event
        {public FrameState environment;public string operation,context,kind;public int boot,thread,frame=-1,renderedFrame=-1;public long sequence;public double startMs,endMs,wallMs,cpuMs=-1;public bool cpuAvailable;}
        [Serializable]public sealed class FrameState{public bool focused,runInBackground;public int targetFrameRate,quality,vSync,width,height,renderInterval;public float timeScale;}
        [Serializable]public sealed class Aggregate
        {public string operation;public int count;public double totalWallMs,maxWallMs,totalCpuMs;public int cpuSamples;public Event worst;}
        [Serializable]public sealed class Snapshot
        {public int boot,dropped,activeScopes;public string region,status;public Event[] events;public Aggregate[] aggregates;}
        [Serializable]public sealed class Bundle
        {public string clock="Stopwatch monotonic milliseconds",cpu="calling-thread CPU; unavailable is explicit; no process/GPU substitution";public Snapshot[] boots;public Event[] scenes;public int sceneDropped;}
        public sealed class Trace
        {
            const int Limit=8192;readonly object sync=new object();readonly Queue<Event> events=new Queue<Event>();readonly Dictionary<string,Aggregate> aggregates=new Dictionary<string,Aggregate>();
            readonly int mainThread;public readonly int Id;public string Region {get;private set;}int dropped,openScopes;long sequence;string status="preparing";
            internal Trace(int id,string region){Id=id;Region=region;mainThread=Thread.CurrentThread.ManagedThreadId;}
            internal void Open()=>Interlocked.Increment(ref openScopes);internal void Close()=>Interlocked.Decrement(ref openScopes);
            internal bool Main=>mainThread==Thread.CurrentThread.ManagedThreadId;
            internal void Add(Event e)
            {
                lock(sync)
                {
                    e.boot=Id;e.sequence=++sequence;
                    if(!aggregates.TryGetValue(e.operation,out var a))
                    {if(aggregates.Count==256){dropped++;return;}a=new Aggregate{operation=e.operation};aggregates.Add(e.operation,a);}
                    a.count++;a.totalWallMs+=e.wallMs;if(e.cpuAvailable){a.totalCpuMs+=e.cpuMs;a.cpuSamples++;}
                    if(a.worst==null||e.wallMs>a.maxWallMs){a.maxWallMs=e.wallMs;a.worst=e;}
                    // Keep detailed expensive events; aggregates include EVERY scope.
                    if(e.wallMs<.1&&e.kind!="signal")return;
                    if(events.Count==Limit){events.Dequeue();dropped++;}events.Enqueue(e);
                }
            }
            public void SelectRegion(string value){if(!Main)throw new InvalidOperationException("Select boot region on main thread");Region=value;Mark("boot.region",value);}
            public void Mark(string name,string context=null)
            {double now=Now;Add(new Event{operation=name,context=context,kind="signal",startMs=now,endMs=now,thread=Thread.CurrentThread.ManagedThreadId,frame=Main?Time.frameCount:-1,renderedFrame=Main?Time.renderedFrameCount:-1});}
            public void Wall(string name,double start,string context=null)
            {double end=Now;Add(new Event{operation=name,context=context,kind="coroutine-wall",startMs=start,endMs=end,wallMs=end-start,thread=Thread.CurrentThread.ManagedThreadId,frame=Main?Time.frameCount:-1,renderedFrame=Main?Time.renderedFrameCount:-1});}
            public void Complete(string value){lock(sync)status=value;Mark("boot.outcome",value);}
            public Snapshot Capture(){lock(sync)return new Snapshot{boot=Id,region=Region,status=status,dropped=dropped,activeScopes=Volatile.Read(ref openScopes),events=events.ToArray(),aggregates=aggregates.Values.OrderBy(a=>a.operation,StringComparer.Ordinal).Select(a=>new Aggregate{operation=a.operation,count=a.count,totalWallMs=a.totalWallMs,maxWallMs=a.maxWallMs,totalCpuMs=a.totalCpuMs,cpuSamples=a.cpuSamples,worst=a.worst}).ToArray()};}
        }
        public struct Scope:IDisposable
        {
            readonly Trace owner,previous;readonly string name,kind;string detail;readonly int thread,frame,rendered;readonly double start,cpuStart;readonly bool cpu;
            internal Scope(Trace value,string operation,string context)
            {
                owner=value;previous=current;name=operation;detail=context;thread=Thread.CurrentThread.ManagedThreadId;frame=rendered=-1;start=cpuStart=0;cpu=false;kind=null;
                if(owner==null)return;owner.Open();
                bool main=owner.Main;kind=main?"main":"worker";frame=main?Time.frameCount:-1;rendered=main?Time.renderedFrameCount:-1;
                start=Now;cpu=BootThreadCpuClock.TryRead(out cpuStart);current=owner;
            }
            public void Context(string value){detail=value;}
            public void Dispose()
            {
                if(owner==null)return;
                double end=Now;double cpuEnd=-1;bool available=cpu&&thread==Thread.CurrentThread.ManagedThreadId&&BootThreadCpuClock.TryRead(out cpuEnd);
                double elapsedCpu=available?cpuEnd-cpuStart:-1;if(elapsedCpu<0)available=false;
                current=previous;owner.Add(new Event{operation=name,context=detail,kind=kind,thread=thread,frame=frame,renderedFrame=rendered,startMs=start,endMs=end,wallMs=end-start,cpuAvailable=available,cpuMs=available?elapsedCpu:-1});owner.Close();
            }
        }
        public static Scope Enter(Trace owner,string operation,string context=null)=>new Scope(owner,operation,context);
        public static Scope Measure(string operation,string context=null)=>new Scope(current,operation,context);
        public static Trace Begin(string region)
        {if(!Enabled)return null;if(traces.Count==8)return null;var trace=new Trace(++nextId,region);traces.Add(trace);trace.Mark("boot.begin");return trace;}
        public static void EnableIsolated()
        {
            if(string.IsNullOrEmpty(PlayerProfile.StorageOverride))throw new InvalidOperationException("Boot telemetry requires an isolated profile");
            if(Enabled)return;Enabled=true;SceneManager.sceneLoaded+=Loaded;SceneManager.sceneUnloaded+=Unloaded;
        }
        public static void SceneRequest(string name){if(!Enabled)return;sceneRequest++;Scene("scene.request",name);}
        static void Loaded(Scene scene,LoadSceneMode mode)=>Scene("scene.loaded.callback",scene.name);
        static void Unloaded(Scene scene)=>Scene("scene.unloaded.callback",scene.name);
        public static void Scene(string name,string context)
        {if(!Enabled)return;double now=Now;if(sceneEvents.Count==256){sceneEvents.Dequeue();sceneDropped++;}sceneEvents.Enqueue(new Event{boot=sceneRequest,operation=name,context=context,kind="signal",thread=Thread.CurrentThread.ManagedThreadId,frame=Time.frameCount,renderedFrame=Time.renderedFrameCount,startMs=now,endMs=now});}
        public static void FirstFrame(double begin){if(traces.Count>0)traces[traces.Count-1].Wall("initial.scene.to.first-update.wall",begin);}
        public static void Heartbeat(double previous,int previousFrame)
        {if(traces.Count==0)return;var trace=traces[traces.Count-1];double now=Now;trace.Add(new Event{environment=new FrameState{focused=Application.isFocused,runInBackground=Application.runInBackground,targetFrameRate=Application.targetFrameRate,quality=QualitySettings.GetQualityLevel(),vSync=QualitySettings.vSyncCount,width=Screen.width,height=Screen.height,renderInterval=UnityEngine.Rendering.OnDemandRendering.renderFrameInterval,timeScale=Time.timeScale},operation="frame.start.interval",context="from frame "+previousFrame,kind="frame-gap",thread=Thread.CurrentThread.ManagedThreadId,frame=Time.frameCount,renderedFrame=Time.renderedFrameCount,startMs=previous,endMs=now,wallMs=now-previous});}
        public static Bundle Capture()=>new Bundle{boots=traces.Select(t=>t.Capture()).ToArray(),scenes=sceneEvents.ToArray(),sceneDropped=sceneDropped};
    }
}
