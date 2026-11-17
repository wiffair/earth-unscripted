using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class SurfaceQueryViewVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type Region=typeof(GeoRegion),Segment=Region.GetNestedType("Segment",BindingFlags.NonPublic),Group=Region.GetNestedType("RoadSamples",BindingFlags.NonPublic);
        static readonly FieldInfo Source=Region.GetField("sourceCells",Hidden),Surface=Region.GetField("surfaceCells",Hidden),View=Group.GetField("segments"),Road=Group.GetField("road");
        static readonly FieldInfo ViewSource=View.FieldType.GetField("source",Hidden),Indices=View.FieldType.GetField("indices",Hidden);
        static readonly MethodInfo Add=View.FieldType.GetMethod("Add"),Main=Region.GetMethod("PreferMainJunctionGrade",Hidden),Bridge=Region.GetMethod("MatchBridgeApproach",Hidden);
        static readonly Type Cell=Region.GetNestedType("SegmentCell",BindingFlags.NonPublic);
        static readonly Dictionary<object,IList> originalCopies=new Dictionary<object,IList>();
        static object[] Rows(object cell)=>((IEnumerable)cell).Cast<object>().ToArray();
        static object Wrap(IList values)
        {var array=Array.CreateInstance(Segment,values.Count);values.CopyTo(array,0);return Activator.CreateInstance(Cell,new object[]{array,Enumerable.Range(0,values.Count).ToList()});}
        static object allocationSink;static int checks;
        static void Check(bool okay,string why){if(!okay)throw new Exception("SURFACE QUERY VIEW FAIL "+why);checks++;}
        static GeoRoad R(string id,string start,string end,string kind,params Vector3[] points)=>new GeoRoad{id=id,start=start,end=end,kind=kind,name=kind=="primary"?"Through Road":id,width=8,points=points};
        static Vector3 P(float x,float y,float z)=>new Vector3(x,y,z);
        static GeoRegion Fixture()
        {
            var west=R("main-west","w","j","primary",P(-100,10,0),P(-20,11,0),P(0,12,0));
            var east=R("main-east","j","e","primary",P(0,12,0),P(20,13,0),P(100,14,0));
            var minor=R("minor","j","n","residential",P(0,12,0),P(0,20,40),P(0,20,100));
            var deck=R("deck","bs","be","primary",P(300,20,0),P(340,22,0),P(380,24,0));deck.bridge=true;deck.layer=1;deck.width=10;
            var before=R("before","bw","bs","primary",P(200,12,0),P(280,16,0),P(300,18,0));
            var after=R("after","be","bf","primary",P(380,22,0),P(400,20,0),P(480,14,0));
            var sharedPoints=Enumerable.Range(0,81).Select(i=>P(-400+i*2,8+i*.02f,-100)).ToArray();
            var first=R("duplicate-a","da","db","residential",sharedPoints);first.NativeSourceWay="same-source";
            var second=R("duplicate-b","dc","dd","residential",sharedPoints);second.NativeSourceWay="same-source";
            var nil=R("nullable-source","na","nb","residential",P(-400,6,-95),P(-240,9,-95));nil.NativeSourceWay=null;
            var roads=new[]{west,east,minor,deck,before,after,first,nil,second,first};
            return new GeoRegion{roads=roads,waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),minX=-600,minZ=-200,width=1200,depth=400,nx=2,nz=2,heights=new[]{8f,8f,8f,8f}};
        }
        // Independent ORIGINAL grouping: first copy every value into per-road
        // List<Segment>, including duplicate/noncontiguous source occurrences.
        // Wrap those copied lists only to feed the unchanged four consumers.
        static object OriginalCopiedGroups(GeoRegion region)
        {
            var result=(IDictionary)Activator.CreateInstance(Surface.FieldType);
            foreach(DictionaryEntry cell in (IDictionary)Source.GetValue(region))
            {
                var original=Rows(cell.Value);var copies=new Dictionary<GeoRoad,IList>();
                for(int i=0;i<original.Length;i++)
                {
                    var segment=original[i];var road=(GeoRoad)Segment.GetField("road").GetValue(segment);
                    if(!copies.TryGetValue(road,out var list))copies.Add(road,list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Segment)));
                    list.Add(segment);
                }
                var groups=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Group));
                foreach(var entry in copies)
                {
                    var group=Activator.CreateInstance(Group,new object[]{entry.Key,Wrap(entry.Value)});originalCopies[group]=entry.Value;var view=View.GetValue(group);
                    for(int i=0;i<entry.Value.Count;i++)Add.Invoke(view,new object[]{i});groups.Add(group);
                }
                result.Add(cell.Key,groups);
            }
            return result;
        }
        static void RebuildDerived(GeoRegion region)
        {foreach(string name in new[]{"ResetEstimatedClearanceProfiles","ResetEstimatedRailClearances","RebuildMainGradeJunctions","RebuildBridgeApproaches","RebuildEstimatedRailClearances"})Region.GetMethod(name,Hidden).Invoke(region,null);}
        static object[] GroupValues(object group)
        {
            var view=View.GetValue(group);var source=Rows(ViewSource.GetValue(view));var indices=(List<int>)Indices.GetValue(view);
            return indices.Select(index=>source[index]).ToArray();
        }
        static bool SameGroups(GeoRegion actual,GeoRegion baseline)
        {
            var aa=(IDictionary)Surface.GetValue(actual);var bb=(IDictionary)Surface.GetValue(baseline);if(aa.Count!=bb.Count)return false;
            foreach(DictionaryEntry cell in aa)
            {
                if(!bb.Contains(cell.Key))return false;var ag=(IList)cell.Value;var bg=(IList)bb[cell.Key];if(ag.Count!=bg.Count)return false;
                for(int g=0;g<ag.Count;g++)
                {
                    var ar=(GeoRoad)Road.GetValue(ag[g]);var br=(GeoRoad)Road.GetValue(bg[g]);
                    if(Array.IndexOf(actual.roads,ar)!=Array.IndexOf(baseline.roads,br))return false;
                    var av=GroupValues(ag[g]);var bv=GroupValues(bg[g]);if(av.Length!=bv.Length)return false;
                    for(int i=0;i<av.Length;i++)foreach(var field in Segment.GetFields())
                    {
                        object a=field.GetValue(av[i]),b=field.GetValue(bv[i]);
                        if(field.Name=="road"){if(Array.IndexOf(actual.roads,(GeoRoad)a)!=Array.IndexOf(baseline.roads,(GeoRoad)b))return false;}
                        else if(!Equals(a,b))return false;
                    }
                }
            }
            return true;
        }
        static (long slots,int groups) Storage(GeoRegion region,bool copied)
        {
            long slots=0;int groups=0;var sources=(IDictionary)Source.GetValue(region);
            foreach(DictionaryEntry cell in (IDictionary)Surface.GetValue(region))foreach(var group in (IList)cell.Value)
            {
                groups++;var view=View.GetValue(group);var list=ViewSource.GetValue(view);var indices=(List<int>)Indices.GetValue(view);
                if(!copied)Check(ReferenceEquals(list,sources[cell.Key]),"group borrows only its own immutable source-cell list");
                slots+=copied?(int)originalCopies[group].GetType().GetProperty("Capacity").GetValue(originalCopies[group]):indices.Capacity;
            }
            return(slots,groups);
        }
        static void CompareConsumers(GeoRegion actual,GeoRegion baseline,string label)
        {
            Check(SameGroups(actual,baseline),label+" exact road grouping, duplicate values, null source IDs and candidate order");
            bool equal=true;var random=new System.Random(38041);int mainChanged=0,bridgeChanged=0;
            for(int i=0;i<500;i++)
            {
                float x=i<250?(float)random.NextDouble()*80-40:270+(float)random.NextDouble()*140,z=(float)random.NextDouble()*26-13;
                equal&=actual.RoadSurfaceHeight(x,z)==baseline.RoadSurfaceHeight(x,z);
                foreach(int layer in new[]{0,1})equal&=actual.RoadSurfaceHeightOnLayer(x,z,layer)==baseline.RoadSurfaceHeightOnLayer(x,z,layer);
                object[] m={x,z,0,7.125f},b={x,z,0,null,7.125f};
                float am=(float)Main.Invoke(actual,m),bm=(float)Main.Invoke(baseline,m),ab=(float)Bridge.Invoke(actual,b),bb=(float)Bridge.Invoke(baseline,b);
                equal&=am==bm&&ab==bb;if(am!=7.125f)mainChanged++;if(ab!=7.125f)bridgeChanged++;
            }
            Check(equal,label+" all four surface/main-grade/bridge consumer outputs are bit-identical across500 positions");
            Check(mainChanged>0&&bridgeChanged>0,label+" actual main-grade and bridge-approach branches were exercised");
        }
        static void ConcurrentSnapshots()
        {
            var points=Enumerable.Range(0,80).Select(i=>P(i*2,10+i*.01f,0)).ToArray();
            var map=new Native.Map{tileX=0,tileZ=0,coreMinX=-10,coreMinZ=-50,coreMaxX=200,coreMaxZ=50,nx=2,nz=2,heights=new[]{0f,0f,0f,0f},buildings=Array.Empty<Native.Building>(),features=new[]{new Native.Feature{id="shared",sourceId=null,category="road",kind="residential",width=8,points=points,sourceNodeIds=Enumerable.Range(0,points.Length).Select(i=>"sample:"+i).ToArray()}}};
            string before=JsonUtility.ToJson(map);var maps=new[]{map};
            var facade=Region.Assembly.GetType("EarthUnscripted.AutomaticWorldFacade").GetMethod("QuerySnapshot",BindingFlags.Static|BindingFlags.Public);
            GeoRegion Snapshot()=>(GeoRegion)facade.Invoke(null,new object[]{new GeoRegion(),maps});
            var baseline=Snapshot();Surface.SetValue(baseline,OriginalCopiedGroups(baseline));var tasks=Enumerable.Range(0,8).Select(_=>Task.Run(Snapshot)).ToArray();Task.WaitAll(tasks);
            Check(tasks.All(task=>SameGroups(task.Result,baseline)),"eight concurrent immutable-map snapshots retain independent exact grouped output");
            var first=tasks[0].Result;var firstValues=((IDictionary)Source.GetValue(first)).Values.Cast<object>().ToArray();
            Check(tasks.Skip(1).All(task=>!((IDictionary)Source.GetValue(task.Result)).Values.Cast<object>().Any(source=>firstValues.Any(held=>ReferenceEquals(held,source)))),"concurrent snapshots never borrow another snapshot's source-cell lists");
            Check(JsonUtility.ToJson(map)==before,"concurrent snapshot queries leave canonical numeric map arrays unchanged");
        }
        static object[] BuildPayload<T>(object[] sources,int[][] positions,bool copied)
        {
            var result=new object[sources.Length];
            for(int g=0;g<sources.Length;g++)
            {
                if(copied){var list=new List<T>();var source=(List<T>)sources[g];foreach(int index in positions[g])list.Add(source[index]);result[g]=list;}
                else{var list=new List<int>();foreach(int index in positions[g])list.Add(index);result[g]=list;}
            }
            return result;
        }
        static void MeasurePayload<T>(object[] sources,int[][] positions)
        {
            sources=sources.Select(source=>{var values=new List<T>();foreach(T value in (IEnumerable)source)values.Add(value);return (object)values;}).ToArray();
            for(int i=0;i<3;i++){allocationSink=BuildPayload<T>(sources,positions,true);allocationSink=BuildPayload<T>(sources,positions,false);}
            const int repeats=5;long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<repeats;i++)allocationSink=BuildPayload<T>(sources,positions,true);long oldBytes=GC.GetAllocatedBytesForCurrentThread()-start;
            start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<repeats;i++)allocationSink=BuildPayload<T>(sources,positions,false);long newBytes=GC.GetAllocatedBytesForCurrentThread()-start;
            if(oldBytes>0&&newBytes>0){Check(newBytes+sources.Length*8L*repeats<oldBytes,"warmed grouped backing storage reduces available current-thread allocated bytes");Debug.Log("SURFACE VIEW STORAGE measured old="+oldBytes+" new="+newBytes+" across5 passes, excluding common group/dictionary objects; new view adds8 source-reference bytes/group. Not a whole-frame/hitch measurement.");}
            else Debug.Log("SURFACE VIEW STORAGE allocation counter unavailable; structural slot/payload savings only, no measured-byte claim.");
        }
        static void Benchmark(GeoRegion actual,GeoRegion baseline)
        {
            var a=Storage(actual,false);var b=Storage(baseline,true);long saved=b.slots*48-a.slots*4-a.groups*8L;
            Check(a.slots==b.slots&&a.groups==b.groups&&saved>0,"same grouped backing-slot counts replace48-byte GeoRegion values with4-byte indices");
            Debug.Log("SURFACE VIEW STORAGE groups="+a.groups+" old/new slots="+a.slots+" estimated retained payload saved="+saved+" bytes after extra source references. GeoRegion Segment48B; unrelated NativeWorld72B index counter is NOT a per-snapshot count.");
            var sources=new List<object>();var positions=new List<int[]>();
            foreach(DictionaryEntry cell in (IDictionary)Surface.GetValue(actual))foreach(var group in (IList)cell.Value)
            {var view=View.GetValue(group);sources.Add(ViewSource.GetValue(view));positions.Add(((List<int>)Indices.GetValue(view)).ToArray());}
            typeof(SurfaceQueryViewVerification).GetMethod("MeasurePayload",BindingFlags.NonPublic|BindingFlags.Static).MakeGenericMethod(Segment).Invoke(null,new object[]{sources.ToArray(),positions.ToArray()});
        }
        public static void Run()
        {
            checks=0;originalCopies.Clear();var actual=Fixture();var baseline=Fixture();string before=JsonUtility.ToJson(actual);
            actual.RebuildRenderQueryIndexesOnly();baseline.RebuildRenderQueryIndexesOnly();Surface.SetValue(baseline,OriginalCopiedGroups(baseline));RebuildDerived(baseline);
            CompareConsumers(actual,baseline,"first build");Benchmark(actual,baseline);
            actual.RebuildRenderQueryIndexesOnly();baseline.RebuildRenderQueryIndexesOnly();Surface.SetValue(baseline,OriginalCopiedGroups(baseline));RebuildDerived(baseline);CompareConsumers(actual,baseline,"rebuild");
            Check(JsonUtility.ToJson(actual)==before,"derived grouping never changes canonical road points/source identities");
            ConcurrentSnapshots();Debug.Log("SURFACE QUERY VIEW PASS "+checks+" original copied-group equivalence, all4 consumers, immutable ownership and allocation checks");
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
