using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class ControlPlacementCacheVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static int checks;
        static void Check(bool valid,string why){if(!valid)throw new Exception("CONTROL PLACEMENT CACHE FAIL "+why);checks++;}
        static object Invoke(Native world,string name,params object[] args)=>typeof(Native).GetMethod(name,Hidden).Invoke(world,args);
        static IDictionary Cache(Native world)=>(IDictionary)typeof(Native).GetField("controlPlacementCache",Hidden).GetValue(world);
        static Native.Report Report(Native world)=>(Native.Report)typeof(Native).GetField("report",Hidden).GetValue(world);
        static Task<EarthUnscripted.NewbridgePrototype.TrafficControlLayout.Placement[]> Get(Native world,Native.Map map,int side=-1)=>(Task<EarthUnscripted.NewbridgePrototype.TrafficControlLayout.Placement[]>)Invoke(world,"TrafficPlacements",map,side);
        static T Wait<T>(Task<T> task){Check(task.Wait(TimeSpan.FromSeconds(30)),"bounded numeric placement task completes");return task.GetAwaiter().GetResult();}
        static Native.Map Map(int x,int z,string id)
        {
            float px=x*1536+32,pz=z*1536+32;
            return new Native.Map{tileX=x,tileZ=z,coreMinX=x*1536,coreMinZ=z*1536,coreMaxX=(x+1)*1536,coreMaxZ=(z+1)*1536,frame=new Native.FrameInfo{country_code="GB",traffic_side="left"},features=new[]{new Native.Feature{id=id,sourceId=id,category="road",width=7,points=new[]{new Vector3(px,0,pz-50),new Vector3(px,0,pz+50)},controls=new[]{new Native.Control{id=id+":mapped-stop",kind="stop",x=px,z=pz,dz=1,direction=1}}}}};
        }
        static void Same(EarthUnscripted.NewbridgePrototype.TrafficControlLayout.Placement[] actual,EarthUnscripted.NewbridgePrototype.TrafficControlLayout.Placement[] expected,string label)
        {
            Check(actual.Length==expected.Length,label+" placement count");
            for(int i=0;i<actual.Length;i++)Check(ReferenceEquals(actual[i].road,expected[i].road)&&actual[i].position==expected[i].position&&JsonUtility.ToJson(actual[i].control)==JsonUtility.ToJson(expected[i].control),label+" original placement order, source road, position and control semantics");
        }
        static void Pending(Native world,Native.Map map,Task<EarthUnscripted.NewbridgePrototype.TrafficControlLayout.Placement[]> task)
        {
            var entry=typeof(Native).GetNestedType("ControlPlacementCache",BindingFlags.NonPublic);
            Cache(world).Add(map,Activator.CreateInstance(entry,new object[]{-1,task}));
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference AbandonedFault(Native world)
        {
            var completion=new TaskCompletionSource<EarthUnscripted.NewbridgePrototype.TrafficControlLayout.Placement[]>();Pending(world,Map(7,0,"abandoned"),completion.Task);
            Invoke(world,"OnDestroy");Check(Cache(world).Count==0&&Report(world).controlLayoutCachedMaps==0,"actual world destruction callback clears in-flight cache");
            completion.SetException(new InvalidDataException("expected abandoned numeric task failure"));return new WeakReference(completion.Task);
        }
        public static void Run()
        {
            checks=0;var root=new GameObject("Actual immutable-map control placement cache verification");var world=root.AddComponent<Native>();world.enabled=false;
            try
            {
                var a=Map(0,0,"a");var b=Map(1,0,"b");string source=JsonUtility.ToJson(a);
                var first=Get(world,a);var second=Get(world,b);var again=Get(world,a);
                Check(ReferenceEquals(first,again)&&!ReferenceEquals(first,second),"A to B to A reuses original map task rather than rebuilding");
                Same(Wait(first),new EarthUnscripted.NewbridgePrototype.TrafficControlLayout(a,-1).Items.ToArray(),"A baseline");Same(Wait(second),new EarthUnscripted.NewbridgePrototype.TrafficControlLayout(b,-1).Items.ToArray(),"B baseline");
                Check(Report(world).controlLayoutBuilds==2&&Report(world).controlLayoutCacheHits==1,"only two actual constructors for A B A");
                Get(world,a);Check(Report(world).controlLayoutCachedMaps==2&&Report(world).controlLayoutCachedPlacements==first.Result.Length+second.Result.Length,"bounded live map/placement counters reflect completed arrays");
                Check(JsonUtility.ToJson(a)==source,"cache and constructor do not mutate canonical source map");
                var inflight=Map(2,0,"pending");var signal=new TaskCompletionSource<EarthUnscripted.NewbridgePrototype.TrafficControlLayout.Placement[]>();Pending(world,inflight,signal.Task);int builds=Report(world).controlLayoutBuilds;
                Check(!signal.Task.IsCompleted&&ReferenceEquals(Get(world,inflight),signal.Task)&&ReferenceEquals(Get(world,inflight),signal.Task),"exact production lookup reuses deterministic in-flight entry");
                Check(Report(world).controlLayoutBuilds==builds,"in-flight lookup starts no duplicate constructor");signal.SetResult(new EarthUnscripted.NewbridgePrototype.TrafficControlLayout(inflight,-1).Items.ToArray());
                var replacement=Map(0,0,"replacement");var fresh=Get(world,replacement);Wait(fresh);Check(!Cache(world).Contains(a)&&Cache(world).Contains(replacement)&&!ReferenceEquals(first,fresh),"source replacement evicts previous immutable map at the same tile");
                var otherSide=Get(world,replacement,1);Same(Wait(otherSide),new EarthUnscripted.NewbridgePrototype.TrafficControlLayout(replacement,1).Items.ToArray(),"new driving side");Check(!ReferenceEquals(fresh,otherSide),"different side rebuilds instead of reusing opposite placements");
                Invoke(world,"ClearControlPlacements");var areas=(Dictionary<Vector2Int,Native.Map>)typeof(Native).GetField("areas",Hidden).GetValue(world);
                for(int x=10;x<=26;x++)areas.Add(new Vector2Int(x,0),Map(x,0,"retire-"+x));var far=areas[new Vector2Int(26,0)];var near=areas[new Vector2Int(10,0)];Wait(Get(world,far));Wait(Get(world,near));
                Invoke(world,"RetireDataAreas");Check(!areas.ContainsKey(new Vector2Int(26,0))&&!Cache(world).Contains(far)&&Cache(world).Contains(near),"actual resident-area retirement evicts only retired map placements");
                string path="Assets/EarthUnscripted/Editor/Fixtures/CardiffSignsFixture.json";
                if(File.Exists(path))
                {
                    var cardiff=JsonUtility.FromJson<Native.Map>(File.ReadAllText(path));var expected=new EarthUnscripted.NewbridgePrototype.TrafficControlLayout(cardiff,-1).Items.ToArray();var actual=Get(world,cardiff);Same(Wait(actual),expected,"dense real Cardiff");Check(ReferenceEquals(actual,Get(world,cardiff)),"dense Cardiff placements reused as one immutable array task");Debug.Log("CONTROL CACHE CARDIFF placements="+expected.Length);
                }
                int unobserved=0;EventHandler<UnobservedTaskExceptionEventArgs> handler=(sender,args)=>{Interlocked.Increment(ref unobserved);args.SetObserved();};TaskScheduler.UnobservedTaskException+=handler;
                try
                {
                    var abandoned=AbandonedFault(world);for(int i=0;i<8&&abandoned.IsAlive;i++){GC.Collect();GC.WaitForPendingFinalizers();Thread.Sleep(10);}
                    Check(!abandoned.IsAlive&&unobserved==0,"destroyed-world task faults are observed without retaining their array/task");
                }
                finally{TaskScheduler.UnobservedTaskException-=handler;}
                Check(Report(world).controlLayoutCachedPlacements==0,"destruction resets live placement accounting");
                Debug.Log("CONTROL PLACEMENT CACHE PASS "+checks+"; actual per-map lookup, construction parity and lifecycle");
            }
            finally{Invoke(world,"ClearControlPlacements");UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
