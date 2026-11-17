using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Debug=UnityEngine.Debug;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using World=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;

namespace EarthUnscripted.Editor
{
    // Private draft: original per-cell value-copy construction is the oracle,
    // not the optimized builder's membership array or counters.
    public static class NativeSegmentIndexVerification
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly Type Segment=typeof(World).GetNestedType("Segment",Flags);
        static readonly Type Spatial=typeof(World).GetNestedType("SpatialData",Flags);
        static readonly Type Candidates=typeof(World).GetNestedType("SegmentCandidates",Flags);
        static readonly Type SegmentList=typeof(List<>).MakeGenericType(Segment);
        static int checks;
        struct Row
        {
            public World.Feature feature;public Vector3 a,b,centreA,centreB;public float station;public int pointIndex;public bool surfaceStart,surfaceEnd;
        }
        sealed class Original
        {
            public int unique;
            public readonly Dictionary<Vector2Int,List<Row>> cells=new Dictionary<Vector2Int,List<Row>>();
            public readonly Dictionary<World.Feature,Vector4> bounds=new Dictionary<World.Feature,Vector4>();
        }
        static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        static object Get(object owner,string name)=>owner.GetType().GetField(name,Flags).GetValue(owner);
        static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Flags).SetValue(owner,value);
        static object Call(object owner,string method,params object[] args)
        {
            var target=typeof(World).GetMethod(method,Flags);var parameters=target.GetParameters();
            if(args.Length<parameters.Length){var expanded=new object[parameters.Length];Array.Copy(args,expanded,args.Length);for(int i=args.Length;i<parameters.Length;i++)expanded[i]=parameters[i].DefaultValue;args=expanded;}
            return target.Invoke(owner,args);
        }
        static Row Read(object boxed)=>new Row{feature=(World.Feature)Get(boxed,"feature"),a=(Vector3)Get(boxed,"a"),b=(Vector3)Get(boxed,"b"),centreA=(Vector3)Get(boxed,"centreA"),centreB=(Vector3)Get(boxed,"centreB"),station=(float)Get(boxed,"station"),pointIndex=(int)Get(boxed,"pointIndex"),surfaceStart=(bool)Get(boxed,"surfaceStart"),surfaceEnd=(bool)Get(boxed,"surfaceEnd")};
        static object Box(Row row){object value=Activator.CreateInstance(Segment);foreach(var field in typeof(Row).GetFields(Flags))Set(value,field.Name,field.GetValue(row));return value;}
        static bool Equal(Row a,Row b)=>ReferenceEquals(a.feature,b.feature)&&a.a.Equals(b.a)&&a.b.Equals(b.b)&&a.centreA.Equals(b.centreA)&&a.centreB.Equals(b.centreB)&&a.station==b.station&&a.pointIndex==b.pointIndex&&a.surfaceStart==b.surfaceStart&&a.surfaceEnd==b.surfaceEnd;
        static List<Row> ReadRows(object rows)
        {
            if(rows==null)return null;
            if(rows is IEnumerable enumerable)return enumerable.Cast<object>().Select(Read).ToList();
            // OwnSegments intentionally has only a pattern-based struct iterator.
            object iterator=rows.GetType().GetMethod("GetEnumerator",Flags).Invoke(rows,null);var type=iterator.GetType();var result=new List<Row>();
            while((bool)type.GetMethod("MoveNext",Flags).Invoke(iterator,null))result.Add(Read(type.GetProperty("Current",Flags).GetValue(iterator)));
            return result;
        }
        static void Same(List<Row> actual,List<Row> expected,string label)
        {Check((actual==null)==(expected==null),label+" null contract");if(actual==null)return;Check(actual.Count==expected.Count,label+" count");for(int i=0;i<actual.Count;i++)Check(Equal(actual[i],expected[i]),label+" ordered value "+i);}
        static Vector3 Side(Vector3 a,Vector3 b){var delta=b-a;delta.y=0;return delta.sqrMagnitude<1e-12f?Vector3.zero:new Vector3(delta.z,0,-delta.x).normalized;}
        static Vector3 Join(Vector3[] points,int i)
        {
            if(i==0)return Side(points[0],points[1]);if(i==points.Length-1)return Side(points[i-1],points[i]);
            var before=Side(points[i-1],points[i]);var after=Side(points[i],points[i+1]);if(before==Vector3.zero)return after;if(after==Vector3.zero)return before;
            var sum=before+after;if(sum.sqrMagnitude<.01f)return after;var normal=sum.normalized;return normal/Mathf.Max(.7f,Vector3.Dot(normal,after));
        }
        static Original Baseline(World.Map map,bool bounded)
        {
            var result=new Original();bool core=bounded&&map.coreMaxX>map.coreMinX&&map.coreMaxZ>map.coreMinZ;
            int minX=core?Mathf.FloorToInt((map.coreMinX-64)/16):int.MinValue,maxX=core?Mathf.FloorToInt((map.coreMaxX+64)/16):int.MaxValue;
            int minZ=core?Mathf.FloorToInt((map.coreMinZ-64)/16):int.MinValue,maxZ=core?Mathf.FloorToInt((map.coreMaxZ+64)/16):int.MaxValue;
            foreach(var feature in map.features)
            {
                float station=0;var bounds=new Vector4(float.PositiveInfinity,float.PositiveInfinity,float.NegativeInfinity,float.NegativeInfinity);
                for(int i=1;i<feature.points.Length;i++)
                {
                    var row=new Row{feature=feature,a=feature.points[i-1],b=feature.points[i],centreA=feature.points[i-1],centreB=feature.points[i],pointIndex=i,station=station};
                    station+=Vector2.Distance(new Vector2(row.a.x,row.a.z),new Vector2(row.b.x,row.b.z));
                    if(feature.category=="road"&&feature.roadCentreOffset!=0){row.a+=Join(feature.points,i-1)*feature.roadCentreOffset;row.b+=Join(feature.points,i)*feature.roadCentreOffset;}
                    float margin=feature.width*.5f+12;int west=Mathf.FloorToInt((Mathf.Min(row.a.x,row.b.x)-margin)/16),east=Mathf.FloorToInt((Mathf.Max(row.a.x,row.b.x)+margin)/16);
                    int south=Mathf.FloorToInt((Mathf.Min(row.a.z,row.b.z)-margin)/16),north=Mathf.FloorToInt((Mathf.Max(row.a.z,row.b.z)+margin)/16);
                    bounds=new Vector4(Mathf.Min(bounds.x,west),Mathf.Min(bounds.y,south),Mathf.Max(bounds.z,east),Mathf.Max(bounds.w,north));
                    if(Mathf.Max(minZ,south)<=Mathf.Min(maxZ,north)&&Mathf.Max(minX,west)<=Mathf.Min(maxX,east))result.unique++;
                    for(int z=Mathf.Max(minZ,south);z<=Mathf.Min(maxZ,north);z++)for(int x=Mathf.Max(minX,west);x<=Mathf.Min(maxX,east);x++)
                    {var cell=new Vector2Int(x,z);if(!result.cells.TryGetValue(cell,out var rows))result.cells[cell]=rows=new List<Row>();rows.Add(row);}
                }
                result.bounds[feature]=bounds;
            }
            return result;
        }
        static object OriginalSpatial(Original original)
        {
            object spatial=Activator.CreateInstance(Spatial,true);var cells=(IDictionary)Get(spatial,"segments");var sources=(IDictionary)Get(spatial,"sourceSegments");var bounds=(IDictionary)Get(spatial,"queryFeatureBounds");
            foreach(var entry in original.cells)
            {
                var list=(IList)Activator.CreateInstance(SegmentList);var source=new Dictionary<string,List<int>>(StringComparer.Ordinal);
                foreach(var row in entry.Value){string identity=row.feature.sourceId??row.feature.id;if(!source.TryGetValue(identity,out var positions))source[identity]=positions=new List<int>();positions.Add(list.Count);list.Add(Box(row));}
                cells.Add(entry.Key,Activator.CreateInstance(Candidates,Flags,null,new object[]{list},null));sources.Add(entry.Key,source);
            }
            foreach(var entry in original.bounds)bounds.Add(entry.Key,entry.Value);return spatial;
        }
        static World Host(World.Map map,object spatial,List<GameObject> roots)
        {
            var root=new GameObject("Segment index isolated fixture");root.SetActive(false);roots.Add(root);var world=root.AddComponent<World>();
            Set(world,"map",map);Set(world,"workingMap",map);((IDictionary)Get(world,"spatialByMap")).Add(map,spatial);((IDictionary)Get(world,"areas")).Add(new Vector2Int(map.tileX,map.tileZ),map);return world;
        }
        static World.Feature Feature(string id,string source,string category,float width,params Vector3[] points)=>new World.Feature{id=id,sourceId=source,category=category,kind=category=="road"?"residential":category,width=width,points=points,controls=Array.Empty<World.Control>(),level="0"};
        static World.Map Fixture()
        {
            var first=Feature("road-a","shared","road",8,new Vector3(-128,4,32),new Vector3(32,5,32),new Vector3(96,7,64),new Vector3(320,8,64));first.roadCentreOffset=1.8f;
            first.controls=new[]{new World.Control{id="stop-a",kind="stop",x=32,y=5,z=32},new World.Control{id="speed-a",kind="speed",value="30",x=64,y=6,z=48}};
            var duplicate=Feature("road-a","shared","road",5,new Vector3(20,11,-128),new Vector3(20,12,64),new Vector3(20,13,320));duplicate.bridge=true;duplicate.layer=1;
            var path=Feature("path",null,"path",2,new Vector3(-96,3,80),new Vector3(48,4,80),new Vector3(300,6,80));
            var bore=Feature("bore","bore","road",6,new Vector3(-90,-4,100),new Vector3(64,-3,100),new Vector3(250,-2,100));bore.tunnel=true;bore.tunnelClearance=5;
            var water=Feature("river",null,"water",3,new Vector3(80,-1,-100),new Vector3(80,-1,250));
            var empty=Feature("single",null,"rail",3,new Vector3(40,5,40));
            return new World.Map{tileX=0,tileZ=0,coreMinX=0,coreMinZ=0,coreMaxX=128,coreMaxZ=128,minX=-256,minZ=-256,maxX=512,maxZ=512,nx=4,nz=4,gridStep=256,heights=Enumerable.Repeat(2f,16).ToArray(),features=new[]{first,duplicate,path,bore,water,empty,first},buildings=Array.Empty<World.Building>(),trees=Array.Empty<World.TreeSeed>(),routes=Array.Empty<World.DriveRoute>(),waterAreas=Array.Empty<World.WaterArea>()};
        }
        static object Build(World.Map map)=>Call(null,"BuildSpatialIndex",map);
        static object OriginalBuild(World.Map map){Call(null,"ResolveSurfaceJoins",map);return OriginalSpatial(Baseline(map,true));}
        static Type FailureType(Func<object> action){try{action();return null;}catch(Exception error){return error is TargetInvocationException wrapped?wrapped.InnerException.GetType():error.GetType();}}
        static int Stride(){var method=new DynamicMethod("ManagedSegmentStride",typeof(int),Type.EmptyTypes,typeof(World),true);var il=method.GetILGenerator();il.Emit(OpCodes.Sizeof,Segment);il.Emit(OpCodes.Ret);return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();}
        public static void Run()
        {
            checks=0;Check(typeof(IEnumerable<>).MakeGenericType(Segment).IsAssignableFrom(Candidates),"Nearby retains generic LINQ enumeration contract");var roots=new List<GameObject>();
            try
            {
                foreach(bool bounded in new[]{true,false})
                {
                    var map=Fixture();if(!bounded)map.coreMaxX=map.coreMinX;
                    string input=JsonUtility.ToJson(map);object spatial=Build(map);var original=Baseline(map,true);var all=Baseline(map,false);var cells=(IDictionary)Get(spatial,"segments");
                    Check(cells.Count==original.cells.Count,"Independent original cell set");
                    foreach(var entry in original.cells)Same(ReadRows(cells[entry.Key]),entry.Value,"cell "+entry.Key);
                    var bounds=(IDictionary)Get(spatial,"queryFeatureBounds");foreach(var entry in original.bounds)Check(((Vector4)bounds[entry.Key]).Equals(entry.Value),"Original full source bounds retained");
                    var world=Host(map,spatial,roots);var old=Host(map,OriginalSpatial(original),roots);long initialSlots=world.QueryIndexMembershipSlots;
                    object storage=Get(spatial,"segmentStorage");int stored=((Array)storage).Length;
                    var queries=new List<Vector2>();for(int z=-112;z<=288;z+=16)for(int x=-112;x<=288;x+=16)queries.Add(new Vector2(x+.125f,z+.25f));
                    queries.AddRange(new[]{new Vector2(-64,32),new Vector2(192,64),new Vector2(208,100),new Vector2(-80,100),new Vector2(500,500)});
                    foreach(var point in queries)
                    {
                        var cell=new Vector2Int(Mathf.FloorToInt(point.x/16),Mathf.FloorToInt(point.y/16));bool inside=!bounded||(cell.x>=-4&&cell.x<=12&&cell.y>=-4&&cell.y<=12);
                        List<Row> expected=null;(inside?original:all).cells.TryGetValue(cell,out expected);if(!inside&&expected==null)expected=new List<Row>();
                        var actual=ReadRows(Call(world,"Nearby",point.x,point.y));Same(actual,expected,"Nearby "+point);
                        // Exercise the nullable LINQ control/feature contract as used by tunnel/population consumers.
                        var actualControls=actual?.Select(row=>row.feature).SelectMany(feature=>feature.controls??Array.Empty<World.Control>()).Select(control=>control.id).ToArray();
                        var expectedControls=expected?.Select(row=>row.feature).SelectMany(feature=>feature.controls??Array.Empty<World.Control>()).Select(control=>control.id).ToArray();
                        Check((actualControls==null)==(expectedControls==null)&&(actualControls==null||actualControls.SequenceEqual(expectedControls)),"Ordered control projection");
                        foreach(var feature in map.features)
                        {
                            var own=ReadRows(Call(world,"OwnSegments",feature,point.x,point.y));string identity=feature.sourceId??feature.id;
                            Same(own,(expected??new List<Row>()).Where(row=>(row.feature.sourceId??row.feature.id)==identity).ToList(),"Own source "+identity);
                            var at=new Vector3(point.x,4,point.y);
                            Check(((Vector3)Call(world,"RoadSurfacePoint",feature,at)).Equals((Vector3)Call(old,"RoadSurfacePoint",feature,at)),"Actual road/control surface height");
                            Check(((Vector3)Call(world,"BridgeSurfacePoint",feature,at)).Equals((Vector3)Call(old,"BridgeSurfacePoint",feature,at)),"Actual bridge/path height");
                            Check((float)Call(world,"PathSurfaceLift",feature,at)==(float)Call(old,"PathSurfaceLift",feature,at),"Actual path lift");
                        }
                        Check((float)Call(world,"Ground",point.x,point.y)==(float)Call(old,"Ground",point.x,point.y),"Actual ground/rail/water height");
                        Check((bool)Call(world,"InsideBore",new Vector3(point.x,-2,point.y))==(bool)Call(old,"InsideBore",new Vector3(point.x,-2,point.y)),"Actual tunnel membership");
                        object[] a={point.x,point.y,0f},b={point.x,point.y,0f};Check((bool)Call(world,"TryPortalFloor",a)==(bool)Call(old,"TryPortalFloor",b)&&(float)a[2]==(float)b[2],"Actual tunnel portal height");
                        Check(world.FacadeOpeningClear(new Vector3(point.x,5,point.y),2,4)==old.FacadeOpeningClear(new Vector3(point.x,5,point.y),2,4),"Actual building/road clearance");
                    }
                    Check(ReferenceEquals(storage,Get(spatial,"segmentStorage"))&&((Array)storage).Length==stored,"Fallback never appends/replaces central immutable storage");
                    Check(((IDictionary)Get(spatial,"fallbackCells")).Count<=4,"Fallback retains original four-cell bound");
                    Check(world.QueryIndexSegmentCopies==original.cells.Values.Sum(rows=>(long)rows.Count),"Legacy counter retains cell-membership semantics");
                    Check(world.QueryIndexUniqueSegments==stored,"New storage counter counts actual distinct stored values");
                    long actualCapacity=0;foreach(DictionaryEntry cell in cells)actualCapacity+=(int)Candidates.GetProperty("MembershipCapacity",Flags).GetValue(cell.Value);Check(world.QueryIndexMembershipSlots==actualCapacity&&world.QueryIndexMembershipSlots==initialSlots,"Cached membership capacity matches bounded/unbounded cell capacities and never grows during fallback");
                    long originalCapacity=original.cells.Values.Sum(rows=>(long)rows.Capacity);long membershipCapacity=world.QueryIndexMembershipSlots;long oldPayload=originalCapacity*Stride(),newPayload=stored*(long)Stride()+membershipCapacity*4;
                    Check(newPayload<oldPayload,"Actual managed stride and backing capacities reduce retained payload");
                    Debug.Log("NATIVE_SEGMENT_STORAGE bounded="+bounded+" stride="+Stride()+" unique="+stored+" memberships="+world.QueryIndexSegmentCopies+" originalSlots="+originalCapacity+" membershipSlots="+membershipCapacity+" payloadSavedBytes="+(oldPayload-newPayload)+" excludes object headers");
                    Check(JsonUtility.ToJson(map)==input,"Serialized map inputs unchanged");
                    var snapshots=Enumerable.Range(0,8).Select(_=>Fixture()).ToArray();var indexes=snapshots.Select(Build).ToArray();
                    Check(indexes.Select(index=>Get(index,"segmentStorage")).Distinct().Count()==8,"Concurrent snapshots never share mutable storage ownership");
                    var tasks=indexes.Select((index,i)=>Task.Run(()=>{var result=(IDictionary)Get(index,"segments");foreach(DictionaryEntry entry in result){var rows=ReadRows(entry.Value);if(rows.Any(row=>!snapshots[i].features.Contains(row.feature)))throw new Exception("Cross-snapshot feature alias");}return result.Count;})).ToArray();Task.WaitAll(tasks);Check(tasks.All(task=>task.Result==original.cells.Count)||!bounded,"Concurrent immutable cell reads preserve snapshots");
                }
                var empty=Fixture();empty.features=Array.Empty<World.Feature>();var emptyIndex=Build(empty);Check(((IDictionary)Get(emptyIndex,"segments")).Count==0&&((Array)Get(emptyIndex,"segmentStorage")).Length==0&&(long)Get(emptyIndex,"indexedMembershipSlots")==0,"Empty map has no central or per-cell storage");
                foreach(int invalid in new[]{0,1,2})
                {
                    var broken=Fixture();if(invalid==0)broken.features=null;else if(invalid==1)broken.features[0].points=null;else{broken.features[0].id=null;broken.features[0].sourceId=null;}
                    var actualFailure=FailureType(()=>Build(broken));var originalFailure=FailureType(()=>OriginalBuild(broken));Check(actualFailure==originalFailure,"Original invalid-null rejection is unchanged "+invalid+" actual="+actualFailure+" original="+originalFailure);
                }
                Debug.Log("NATIVE SEGMENT INDEX PASS "+checks+" independent original topology/order, complete values, actual height/control/source/fallback queries and storage ownership");
            }
            finally{foreach(var root in roots)if(root)UnityEngine.Object.DestroyImmediate(root);}
        }
        [Serializable] sealed class Measurement
        {public string core;public int stride,cells,unique;public long memberships,originalCapacity,membershipCapacity,oldPayloadBytes,newPayloadBytes;public double productionBuildMilliseconds;}
        [Serializable] sealed class Measurements{public Measurement[] cores;public int checks;}
        public static void MeasurePreparedBatch()
        {
            try
            {
                checks=0;var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-nativeIndexPreparedRoot");
                if(at<0||at+1>=args.Length)throw new ArgumentException("-nativeIndexPreparedRoot is required");
                var records=new List<Measurement>();
                foreach(string key in new[]{"-1_1","0_1","-1_0","0_0"})
                {
                    var map=JsonUtility.FromJson<World.Map>(File.ReadAllText(Path.Combine(args[at+1],key+".json")));
                    var clock=Stopwatch.StartNew();object spatial=Build(map);double milliseconds=clock.Elapsed.TotalMilliseconds;clock.Stop();
                    var baseline=Baseline(map,true);var cells=(IDictionary)Get(spatial,"segments");long memberships=0,slots=0;
                    Check(cells.Count==baseline.cells.Count,"Prepared input original cell count "+key);
                    foreach(var entry in baseline.cells)
                    {
                        object cell=cells[entry.Key];int count=(int)Candidates.GetProperty("Count",Flags).GetValue(cell);Check(count==entry.Value.Count,"Prepared membership count");memberships+=count;slots+=(int)Candidates.GetProperty("MembershipCapacity",Flags).GetValue(cell);
                        foreach(int index in new[]{0,count/2,count-1}.Distinct())Check(Equal(Read(Candidates.GetProperty("Item",Flags).GetValue(cell,new object[]{index})),entry.Value[index]),"Prepared first/middle/last ordered candidate fields");
                    }
                    Check((long)Get(spatial,"indexedMembershipSlots")==slots,"Prepared cached capacity matches actual per-cell capacities");
                    int stride=Stride(),unique=((Array)Get(spatial,"segmentStorage")).Length;Check(unique==baseline.unique,"Prepared exact unique occurrence count");long oldCapacity=baseline.cells.Values.Sum(rows=>(long)rows.Capacity);
                    var record=new Measurement{core=key,stride=stride,cells=cells.Count,unique=unique,memberships=memberships,originalCapacity=oldCapacity,membershipCapacity=slots,oldPayloadBytes=oldCapacity*stride,newPayloadBytes=unique*(long)stride+slots*4,productionBuildMilliseconds=milliseconds};records.Add(record);
                    Debug.Log("NATIVE_SEGMENT_PREPARED "+JsonUtility.ToJson(record));
                }
                int output=Array.IndexOf(args,"-nativeIndexMeasurementOut");if(output>=0&&output+1<args.Length)File.WriteAllText(args[output+1],JsonUtility.ToJson(new Measurements{cores=records.ToArray(),checks=checks},true));
                Debug.Log("NATIVE SEGMENT PREPARED PASS "+checks+" deterministic payload excludes object headers; production build time includes unchanged preparation, no baseline timing claim");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
