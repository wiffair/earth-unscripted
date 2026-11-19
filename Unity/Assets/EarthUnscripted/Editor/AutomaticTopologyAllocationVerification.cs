using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class AutomaticTopologyAllocationVerification
    {
        const BindingFlags PrivateStatic=BindingFlags.Static|BindingFlags.NonPublic;
        static readonly Type Facade=typeof(GeoRegion).Assembly.GetType("EarthUnscripted.AutomaticWorldFacade");
        static readonly MethodInfo Shared=Facade.GetMethod("SharedRoadNodes",PrivateStatic),Snapshot=Facade.GetMethod("QuerySnapshot",BindingFlags.Static|BindingFlags.Public),Sections=Facade.GetMethod("SourceSections",PrivateStatic),Clip=Facade.GetMethod("Clip",PrivateStatic),Touches=Facade.GetMethod("MayTouchCore",PrivateStatic);
        static object allocationSink;static int checks;
        static void Check(bool okay,string why){if(!okay)throw new Exception("AUTOMATIC TOPOLOGY FAIL "+why);checks++;}
        // Exact independent pre-change algorithm: keep every distinct way ID in
        // a per-node HashSet, then select nodes with more than one source way.
        static HashSet<string> OriginalShared(Native.Map[] maps)
        {
            var nodeWays=new Dictionary<string,HashSet<string>>();
            foreach(var feature in maps.SelectMany(map=>map.features).Where(feature=>feature.category=="road"))
                foreach(var node in feature.sourceNodeIds??Array.Empty<string>())
                {
                    if(!node.StartsWith("osm:",StringComparison.Ordinal))continue;
                    if(!nodeWays.TryGetValue(node,out var ways))nodeWays[node]=ways=new HashSet<string>();
                    ways.Add(feature.sourceId);
                }
            return new HashSet<string>(nodeWays.Where(row=>row.Value.Count>1).Select(row=>row.Key));
        }
        static HashSet<string> ActualShared(Native.Map[] maps)=>(HashSet<string>)Shared.Invoke(null,new object[]{maps});
        sealed class RoadRow {public string id,source,start,end;public string[] nodes;public Vector3[] points;}
        // Only topology changes in this patch. Feed the independent ORIGINAL
        // topology through the untouched section/clipping functions, then build
        // original node identities independently from the actual emitted roads.
        static RoadRow[] OriginalEmitted(Native.Map[] maps)
        {
            var shared=OriginalShared(maps);var rows=new List<RoadRow>();
            foreach(var map in maps)foreach(var feature in map.features)
            {
                if(feature.category!="road"||!(bool)Touches.Invoke(null,new object[]{feature.points,map}))continue;
                var identities=new Dictionary<Vector2Int,string>();
                Vector2Int Key(Vector3 p)=>new Vector2Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.z*1000));
                for(int i=0;i<feature.points.Length;i++)if(feature.sourceNodeIds!=null&&i<feature.sourceNodeIds.Length)identities[Key(feature.points[i])]=feature.sourceNodeIds[i];
                int part=0;
                foreach(var section in (IEnumerable<Vector3[]>)Sections.Invoke(null,new object[]{feature,shared}))
                foreach(var points in (IEnumerable<Vector3[]>)Clip.Invoke(null,new object[]{section,map}))
                {
                    string Node(Vector3 p)
                    {
                        bool known=identities.TryGetValue(Key(p),out var node);
                        if(known&&node.StartsWith("osm:",StringComparison.Ordinal))return node;
                        bool boundary=Mathf.Abs(p.x-map.coreMinX)<.002f||Mathf.Abs(p.x-map.coreMaxX)<.002f||Mathf.Abs(p.z-map.coreMinZ)<.002f||Mathf.Abs(p.z-map.coreMaxZ)<.002f;
                        if(known&&!boundary)return node;
                        return "boundary:"+feature.sourceId+":"+Mathf.RoundToInt(p.x*100)+":"+Mathf.RoundToInt(p.z*100);
                    }
                    var nodes=points.Select(Node).ToArray();rows.Add(new RoadRow{id="native:"+map.tileX+":"+map.tileZ+":"+feature.id+":"+part++,source=feature.sourceId,start=nodes[0],end=nodes[nodes.Length-1],nodes=nodes,points=points});
                }
            }
            return rows.ToArray();
        }
        static bool SameRoads(RoadRow[] expected,GeoRegion actual)
        {
            if(expected.Length!=actual.roads.Length)return false;
            for(int i=0;i<expected.Length;i++)
            {
                var a=expected[i];var b=actual.roads[i];
                if(a.id!=b.id||a.source!=b.NativeSourceWay||a.start!=b.start||a.end!=b.end||!a.nodes.SequenceEqual(b.sourceNodeIds)||!a.points.SequenceEqual(b.points))return false;
            }
            return true;
        }
        static Native.Feature Feature(string id,string source,string[] nodes,params Vector3[] points)
            =>new Native.Feature{id=id,sourceId=source,category="road",kind="residential",width=6,travelWidth=6,speedLimitMps=10,points=points,sourceNodeIds=nodes};
        static Vector3 P(float x,float z)=>new Vector3(x,10,z);
        static Native.Map[] Fixture()
        {
            var main=Feature("main","way-main",new[]{"osm:a","sample:10","osm:j","sample:30","osm:b"},P(0,0),P(10,0),P(20,0),P(30,0),P(40,0));
            var crossing=Feature("crossing","way-cross",new[]{"osm:c0","osm:j","osm:c1"},P(20,-10),P(20,0),P(20,10));
            var duplicate=Feature("same-way",new string("way-duplicate".ToCharArray()),new[]{"osm:d0","osm:duplicate-only","osm:d1"},P(0,5),P(10,5),P(20,5));
            var nullable=Feature("nullable",null,new[]{"osm:n0","osm:null-shared","osm:n1"},P(5,-5),P(10,-5),P(15,-5));
            var real=Feature("real","way-real",new[]{"osm:r0","osm:null-shared","osm:r1"},P(10,-10),P(10,-5),P(10,0));
            var nullOnly=Feature("null-only",null,new[]{"osm:q0","osm:null-only","osm:q1"},P(30,5),P(35,5),P(40,5));
            var ignored=Feature("non-road","way-rail",new[]{"osm:d0","osm:duplicate-only","osm:d1"},P(0,7),P(10,7),P(20,7));ignored.category="rail";
            var originals=new[]{main,crossing,duplicate,nullable,real,nullOnly,ignored};
            Native.Feature Copy(Native.Feature f)=>new Native.Feature{id=f.id,sourceId=f.sourceId==null?null:new string(f.sourceId.ToCharArray()),category=f.category,kind=f.kind,width=f.width,travelWidth=f.travelWidth,speedLimitMps=f.speedLimitMps,sourceNodeIds=f.sourceNodeIds.Select(n=>new string(n.ToCharArray())).ToArray(),points=(Vector3[])f.points.Clone()};
            Native.Map Map(int tile,float min,float max,Native.Feature[] features)=>new Native.Map{tileX=tile,tileZ=0,coreMinX=min,coreMaxX=max,coreMinZ=-15,coreMaxZ=15,minX=min,minZ=-15,maxX=max,maxZ=15,nx=2,nz=2,heights=new[]{0f,0f,0f,0f},features=features,buildings=Array.Empty<Native.Building>(),routes=Array.Empty<Native.DriveRoute>()};
            return new[]{Map(0,0,25,originals),Map(1,25,50,originals.Select(Copy).ToArray())};
        }
        static GeoRegion ActualSnapshot(Native.Map[] maps)=>(GeoRegion)Snapshot.Invoke(null,new object[]{new GeoRegion(),maps});
        static void MeasureAllocations()
        {
            const int nodes=10000,repetitions=5;
            var ids=Enumerable.Range(0,nodes).Select(i=>"osm:allocation-"+i).ToArray();
            var points=Enumerable.Range(0,nodes).Select(i=>P(i*2,0)).ToArray();
            var feature=Feature("allocation","way-allocation",ids,points);
            var maps=new[]{new Native.Map{features=new[]{feature,Feature("duplicate",new string(feature.sourceId.ToCharArray()),ids,(Vector3[])points.Clone())}}};
            for(int i=0;i<3;i++){allocationSink=OriginalShared(maps);allocationSink=ActualShared(maps);}
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<repetitions;i++)allocationSink=OriginalShared(maps);long oldBytes=GC.GetAllocatedBytesForCurrentThread()-before;
            before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<repetitions;i++)allocationSink=ActualShared(maps);long newBytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(OriginalShared(maps).Count==0&&ActualShared(maps).Count==0,"10,000 repeated single-way node identities preserve topology");
            if(oldBytes>0&&newBytes>0)
            {Check(newBytes<oldBytes*.7,"warmed current-thread topology allocation falls by more than30 percent");Debug.Log("AUTOMATIC TOPOLOGY ALLOCATION measured old="+oldBytes+" new="+newBytes+" saved="+(oldBytes-newBytes)+" bytes across"+repetitions+" passes; removed per-node HashSets="+(nodes*repetitions));}
            else Debug.Log("AUTOMATIC TOPOLOGY ALLOCATION current-thread counter unavailable; no measured byte claim. Per pass removes10,000 HashSets and their initial two backing arrays.");
        }
        public static void Run()
        {
            checks=0;var maps=Fixture();var before=maps.Select(JsonUtility.ToJson).ToArray();var expected=OriginalShared(maps);var actual=ActualShared(maps);
            Check(expected.SetEquals(new[]{"osm:j","osm:null-shared"})&&actual.SetEquals(expected),"original and new exact shared membership retain duplicates/null/non-road semantics");
            Check(!ReferenceEquals(maps[0].features[2].sourceId,maps[1].features[2].sourceId)&&maps[0].features[2].sourceId==maps[1].features[2].sourceId,"duplicate source IDs use value equality across independent halo arrays");
            var roads=OriginalEmitted(maps);var snapshot=ActualSnapshot(maps);Check(SameRoads(roads,snapshot),"actual emitted road sections, IDs, source node arrays, point arrays and order match independent original topology");
            var main=snapshot.roads.Where(r=>r.NativeSourceWay=="way-main").ToArray();
            Check(main.Length==3&&main[0].points.SequenceEqual(new[]{P(0,0),P(10,0),P(20,0)})&&main[1].points.SequenceEqual(new[]{P(20,0),P(25,0)})&&main[2].points.SequenceEqual(new[]{P(25,0),P(30,0),P(40,0)}),"real junction and core boundary emit the three exact expected main-road sections");
            Check(main[0].end=="osm:j"&&main[1].start=="osm:j"&&main[1].end==main[2].start&&main[1].end=="boundary:way-main:2500:0","junction and boundary source identities remain exact");
            var tasks=Enumerable.Range(0,8).Select(_=>Task.Run(()=>ActualSnapshot(maps))).ToArray();Task.WaitAll(tasks);
            Check(tasks.All(t=>SameRoads(roads,t.Result)),"eight simultaneous read-only map snapshots emit identical original roads");
            Check(before.SequenceEqual(maps.Select(JsonUtility.ToJson)),"source maps, point arrays and node identities remain unchanged after serial/concurrent snapshots");
            MeasureAllocations();Debug.Log("AUTOMATIC TOPOLOGY PASS "+checks+" exact original-output, immutable concurrent-input and allocation checks");
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
