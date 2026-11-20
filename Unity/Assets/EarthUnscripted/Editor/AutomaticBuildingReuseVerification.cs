using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using World=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
using NativeWorld=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
 public static class AutomaticBuildingReuseVerification
 {
  static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
  static int checks,yieldObservations,reusedArrays;
  static object Get(object owner,string field)=>owner.GetType().GetField(field,Hidden).GetValue(owner);
  static void Set(object owner,string field,object value)=>owner.GetType().GetField(field,Hidden).SetValue(owner,value);
  static object Call(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Hidden).Invoke(owner,args);
  static void Check(bool value,string message){checks++;if(!value)throw new Exception("BUILDING REUSE: "+message);}
  sealed class OriginalGroup
  {public Vector3 owner;public Bounds bounds;public readonly List<Matrix4x4> frames=new List<Matrix4x4>();public readonly List<Matrix4x4> glass=new List<Matrix4x4>();}
  sealed class OriginalBuilder
  {
   public World renderer;public List<OriginalGroup> facades=new List<OriginalGroup>();bool indexed;
        public IEnumerator BuildIndex()
        {
            var nextOriginalGroup=new List<OriginalGroup>();
            var slice=System.Diagnostics.Stopwatch.StartNew();
            var maps=new List<NativeWorld.Map>(renderer.ResidentAreas);
            var seen=new HashSet<string>();var groups=new Dictionary<Vector2Int,OriginalGroup>();
            foreach(var map in maps)
            {
                if(map.buildings==null)continue;
                foreach(var house in map.buildings.Where(h=>!h.underground && h.minHeight<=0 && h.layer<=0))
                {
                    if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
                    var centre=house.centre;
                    if(centre.x<map.coreMinX || centre.x>=map.coreMaxX || centre.z<map.coreMinZ || centre.z>=map.coreMaxZ || !seen.Add(house.id))continue;
                    if(house.points==null || house.points.Length<4 || house.height<2.7f)continue;
                    var points=new List<Vector3>(house.points);
                    if(Vector3.Distance(points[0],points[points.Count-1])<.05f)points.RemoveAt(points.Count-1);
                    if(points.Count<3)continue;
                    float roof=0,area=0,longest=0;int doorEdge=0;
                    for(int i=0;i<points.Count;i++)
                    {
                        var a=points[i];var b=points[(i+1)%points.Count];
                        roof+=renderer.TryNaturalHeight(a.x,a.z,out var ground)?ground:a.y;
                        area+=a.x*b.z-b.x*a.z;
                        float length=Vector3.ProjectOnPlane(b-a,Vector3.up).magnitude;
                        if(length>longest){longest=length;doorEdge=i;}
                    }
                    roof=roof/points.Count+house.height;
                    if(house.heightIncludesRoof && (house.roofShape=="gabled" || house.roofShape=="pitched"))roof-=Mathf.Clamp(house.roofHeight,.5f,5);
                    var key=new Vector2Int(Mathf.FloorToInt(centre.x/64),Mathf.FloorToInt(centre.z/64));
                    var bounds=new Bounds(centre+Vector3.up*house.height*.5f,new Vector3(longest*2,house.height,longest*2));
                    if(!groups.TryGetValue(key,out var group))
                    {group=new OriginalGroup{owner=centre,bounds=bounds};groups.Add(key,group);}
                    else group.bounds.Encapsulate(bounds);
                    bool industrial=house.kind=="industrial" || house.kind=="warehouse" || house.kind=="shed";
                    for(int edge=0;edge<points.Count;edge++)
                    {
                        var a=points[edge];var b=points[(edge+1)%points.Count];
                        var delta=Vector3.ProjectOnPlane(b-a,Vector3.up);float length=delta.magnitude;
                        if(length<2.4f)continue;
                        var direction=delta/length;
                        var outward=Vector3.Cross(Vector3.up,direction)*(area>0?1:-1);
                        var rotation=Quaternion.LookRotation(outward);
                        int columns=Mathf.Min(24,Mathf.Max(1,Mathf.FloorToInt(length/2.8f)));
                        for(int column=0;column<columns;column++)
                        {
                            if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
                            float along=(column+.5f)/columns;var site=Vector3.Lerp(a,b,along);
                            float ground=renderer.TryNaturalHeight(site.x,site.z,out var y)?y:site.y;
                            if(edge==doorEdge && column==columns/2)
                            {
                                var door=site+outward*.045f;door.y=ground+1.05f;
                                if(door.y+1.05f<roof-.2f)Opening(group,door,rotation,outward,industrial?2.3f:.95f,2.1f,false);
                            }
                            if(industrial)continue;
                            for(int floor=0;floor<12;floor++)
                            {
                                float height=ground+1.75f+floor*2.9f;
                                if(height+.7f>roof-.3f)break;
                                if(floor==0 && edge==doorEdge && column==columns/2)continue;
                                site.y=height;Opening(group,site+outward*.045f,rotation,outward,1.08f,1.35f,true);
                            }
                        }
                    }
                }
            }
            foreach(var group in groups.Values)if(group.glass.Count>0)nextOriginalGroup.Add(group);
            facades=nextOriginalGroup;indexed=true;
        }
        void Opening(OriginalGroup group,Vector3 centre,Quaternion rotation,Vector3 outward,float width,float height,bool mullions)
        {
            if(!renderer.FacadeOpeningClear(centre,width,height))return;
            group.frames.Add(Matrix4x4.TRS(centre,rotation,new Vector3(width+.12f,height+.12f,1)));
            group.glass.Add(Matrix4x4.TRS(centre+outward*.008f,rotation,new Vector3(width,height,1)));
            if(!mullions)return;
            group.frames.Add(Matrix4x4.TRS(centre+outward*.014f,rotation,new Vector3(.035f,height,1)));
            group.frames.Add(Matrix4x4.TRS(centre+outward*.014f,rotation,new Vector3(width,.035f,1)));
        }
  }
  sealed class GroupState{public Vector3 owner;public Bounds bounds;public Matrix4x4[] frames,glass;}
  static List<GroupState> Snapshot(object groups)
  {
   var result=new List<GroupState>();foreach(object group in (IEnumerable)groups)result.Add(new GroupState{owner=(Vector3)Get(group,"owner"),bounds=(Bounds)Get(group,"bounds"),frames=((List<Matrix4x4>)Get(group,"frames")).ToArray(),glass=((List<Matrix4x4>)Get(group,"glass")).ToArray()});return result;
  }
  static bool Matrices(Matrix4x4[] a,Matrix4x4[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))return false;return true;}
  static void Same(List<GroupState> a,List<GroupState> b,string label)
  {Check(a.Count==b.Count,label+" group count/order");for(int i=0;i<a.Count;i++)Check(a[i].owner.Equals(b[i].owner)&&a[i].bounds.Equals(b[i].bounds)&&Matrices(a[i].frames,b[i].frames)&&Matrices(a[i].glass,b[i].glass),label+" exact owner/bounds/ordered matrices "+i);}
  static HashSet<object> Arrays(object groups)
  {
   var result=new HashSet<object>();var array=typeof(List<Matrix4x4>).GetFields(Hidden).Single(field=>!field.IsStatic&&field.FieldType==typeof(Matrix4x4[]));
   foreach(object group in (IEnumerable)groups)foreach(string field in new[]{"frames","glass"}){var list=(List<Matrix4x4>)Get(group,field);if(list.Capacity>0)result.Add(array.GetValue(list));}return result;
  }
  static long Capacity(object groups){long count=0;foreach(object group in (IEnumerable)groups)count+=((List<Matrix4x4>)Get(group,"frames")).Capacity+((List<Matrix4x4>)Get(group,"glass")).Capacity;return count*64;}
  static long Needed(int count){if(count==0)return 0;long capacity=4;while(capacity<count)capacity*=2;return capacity;}
  static long Demand(object groups){long count=0;foreach(object group in (IEnumerable)groups)count+=Needed(((List<Matrix4x4>)Get(group,"frames")).Count)+Needed(((List<Matrix4x4>)Get(group,"glass")).Count);return Math.Min(count*64,32L*1024*1024);}
  static void Pool(AutomaticBuildingDetails details,string label)
  {
   var inactive=Get(details,"inactiveFacades");long bytes=Capacity(inactive);Check(bytes==details.InactiveMatrixBytes&&bytes<=32L*1024*1024&&bytes<=Demand(Get(details,"facades")),label+" exact capacity budget bounded by actual counts");
   Check(details.InactiveGroups<=2048&&details.InactiveGroups<=((IList)Get(details,"facades")).Count,label+" group bound follows current demand");
   Check(!Arrays(inactive).Overlaps(Arrays(Get(details,"facades"))),label+" published/inactive arrays never alias");
   foreach(object group in (IEnumerable)inactive)Check(((List<Matrix4x4>)Get(group,"frames")).Count==0&&((List<Matrix4x4>)Get(group,"glass")).Count==0,label+" retired counts cleared");
  }
  static World.Building House(string id,float x,float z,float width=22,float depth=18,float height=14,string kind="house",bool reverse=false)
  {
   var points=new[]{new Vector3(x-width*.5f,3,z-depth*.5f),new Vector3(x+width*.5f,3,z-depth*.5f),new Vector3(x+width*.5f,3,z+depth*.5f),new Vector3(x-width*.5f,3,z+depth*.5f),new Vector3(x-width*.5f,3,z-depth*.5f)};if(reverse)Array.Reverse(points);
   return new World.Building{id=id,points=points,height=height,kind=kind,roofShape="gabled",roofHeight=2,heightIncludesRoof=true};
  }
  static World.Map Map(int tile,int count,bool dense=false)
  {
   float west=tile*1536;var houses=new List<World.Building>();for(int i=0;i<count;i++)houses.Add(House(tile+"/"+i,west+(dense?200:80+i%12*80),dense?200:100+i/12*80,dense?500:22,dense?500:18,dense?40:14+i%3*3,i%9==8?"industrial":"house",i%2==0));
   houses.Add(House("underground",west+250,200));houses[houses.Count-1].underground=true;
   var map=new World.Map{tileX=tile,tileZ=0,coreMinX=west,coreMaxX=west+1536,coreMinZ=0,coreMaxZ=1536,minX=west,minZ=0,nx=2,nz=2,gridStep=1536,heights=new[]{2f,4f,3f,5f},preparedPaving=new List<World.PavingPatch>(),buildings=houses.ToArray(),features=new[]{new World.Feature{id="frontage/"+tile,sourceId="frontage/"+tile,category="road",width=7,points=new[]{new Vector3(west+10,4,111),new Vector3(west+1500,4,111)}}}};
   return map;
  }
  static void Adopt(World renderer,World.Map[] maps)
  {
   var areas=(IDictionary)Get(renderer,"areas");var spatial=(IDictionary)Get(renderer,"spatialByMap");areas.Clear();spatial.Clear();
   var build=typeof(World).GetMethod("BuildSpatialIndex",Hidden);
   foreach(var map in maps){areas.Add(new Vector2Int(map.tileX,map.tileZ),map);spatial.Add(map,build.Invoke(null,new object[]{map}));}Set(renderer,"workingMap",maps[0]);
  }
  static void Case(AutomaticBuildingDetails candidate,World renderer,OriginalBuilder original,string name,World.Map[] maps,bool expectReuse=false)
  {
   Adopt(renderer,maps);var oldPublished=Get(candidate,"facades");var before=Snapshot(oldPublished);var available=Arrays(Get(candidate,"inactiveFacades"));int reuseBefore=candidate.ReusedFacadeGroups,retainedBefore=candidate.RetainedFacadeGroups;var oldArrays=Arrays(oldPublished);
   var baseline=original.BuildIndex();while(baseline.MoveNext()){}
   var work=(IEnumerator)Call(candidate,"BuildIndex");int steps=0;
   while(work.MoveNext()){Check(ReferenceEquals(oldPublished,Get(candidate,"facades")),name+" no partial publication");Same(before,Snapshot(Get(candidate,"facades")),name+" published invariant at yield");yieldObservations++;if(++steps>100000)throw new Exception("Unbounded facade iteration");}
   Same(Snapshot(original.facades),Snapshot(Get(candidate,"facades")),name+" independent original output");Pool(candidate,name);
   int reused=Arrays(Get(candidate,"facades")).Count(array=>available.Contains(array));reusedArrays+=reused;
   int retained=Arrays(Get(candidate,"facades")).Count(array=>oldArrays.Contains(array));
   if(expectReuse)Check(candidate.ReusedFacadeGroups>reuseBefore&&reused>0||candidate.RetainedFacadeGroups>retainedBefore&&retained>0,name+" actual backing arrays reused or unchanged published groups retained");
   if(maps.All(map=>map.buildings!=null)&&candidate.RetainedFacadeGroups>retainedBefore)Check(retained>0,name+" retained groups preserve actual original backing arrays");
   // Exact bounds/owner/matrices make the unchanged visibility predicate identical.
   foreach(var position in new[]{new Vector3(0,10,-40),new Vector3(500,30,500),new Vector3(1700,50,300)})
   {
    var planes=GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(65,1.6f,.3f,500)*Matrix4x4.TRS(position,Quaternion.Euler(15,0,0),Vector3.one).inverse);
    var a=Snapshot(original.facades).Select(group=>group.bounds.SqrDistance(position)<=450*450&&GeometryUtility.TestPlanesAABB(planes,group.bounds)).ToArray();
    var b=Snapshot(Get(candidate,"facades")).Select(group=>group.bounds.SqrDistance(position)<=450*450&&GeometryUtility.TestPlanesAABB(planes,group.bounds)).ToArray();Check(a.SequenceEqual(b),name+" unchanged distance/frustum visibility selection");
   }
   Debug.Log("BUILDING REUSE CASE "+name+" groups="+((IList)Get(candidate,"facades")).Count+" activeCapacityBytes="+Capacity(Get(candidate,"facades"))+" inactiveBytes="+candidate.InactiveMatrixBytes+" reusedArrays="+reused+" reusedGroupEvents="+(candidate.ReusedFacadeGroups-reuseBefore)+" newFinalArrays="+(Arrays(Get(candidate,"facades")).Count-reused)+" yields="+steps);
  }
  static void CancelAndFault(AutomaticBuildingDetails candidate,World renderer,World.Map map)
  {
   Adopt(renderer,new[]{map});var before=Snapshot(Get(candidate,"facades"));var published=Get(candidate,"facades");var work=(IEnumerator)Call(candidate,"BuildIndex");int reuse=candidate.ReusedFacadeGroups;bool yielded=false;
   for(int i=0;i<10000&&work.MoveNext();i++){yielded=true;if(candidate.ReusedFacadeGroups>reuse)break;}
   Check(yielded,"Real build supplies a paused scratch generation");Same(before,Snapshot(Get(candidate,"facades")),"Paused output unchanged");
   var overlapping=(IEnumerator)Call(candidate,"BuildIndex");bool refused=false;try{overlapping.MoveNext();}catch(InvalidOperationException){refused=true;}Check(refused,"Overlapping builder explicitly refused");
   ((IDisposable)work).Dispose();Check(!(bool)Get(candidate,"buildingDetails")&&ReferenceEquals(published,Get(candidate,"facades")),"Disposed scratch releases build guard without publishing");Same(before,Snapshot(Get(candidate,"facades")),"Disposed output unchanged");Pool(candidate,"disposed");
   work=(IEnumerator)Call(candidate,"BuildIndex");Check(work.MoveNext(),"Failure fixture pauses before completion");var heights=map.heights;map.heights=null;bool failed=false;
   try{while(work.MoveNext()){} }catch(NullReferenceException){failed=true;}finally{map.heights=heights;((IDisposable)work).Dispose();}
   Check(failed&&!(bool)Get(candidate,"buildingDetails"),"Actual terrain-query fault releases scratch and guard");Same(before,Snapshot(Get(candidate,"facades")),"Fault output unchanged");Pool(candidate,"fault");
  }
  static object Group(int frameCount,int glassCount,int frameCapacity=0,int glassCapacity=0)
  {
   var type=typeof(AutomaticBuildingDetails).GetNestedType("Facades",Hidden);object group=Activator.CreateInstance(type,true);var frames=(List<Matrix4x4>)Get(group,"frames");var glass=(List<Matrix4x4>)Get(group,"glass");
   frames.Capacity=Math.Max(frameCount,frameCapacity);glass.Capacity=Math.Max(glassCount,glassCapacity);for(int i=0;i<frameCount;i++)frames.Add(Matrix4x4.identity);for(int i=0;i<glassCount;i++)glass.Add(Matrix4x4.identity);return group;
  }
  static IList Groups(params object[] groups){var type=typeof(AutomaticBuildingDetails).GetNestedType("Facades",Hidden);var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));foreach(var group in groups)list.Add(group);return list;}
  static void ShutdownPaused(AutomaticWorldHost host,World renderer)
  {
   Adopt(renderer,new[]{Map(0,96)});var root=new GameObject("Paused facade shutdown");root.SetActive(false);var owner=root.AddComponent<AutomaticBuildingDetails>();Set(owner,"host",host);
   try
   {
    var work=(IEnumerator)Call(owner,"BuildIndex");Check(work.MoveNext(),"Shutdown case has actual unpublished scratch");Call(owner,"OnDestroy");((IDisposable)work).Dispose();
    Check(!(bool)Get(owner,"buildingDetails")&&owner.InactiveGroups==0&&owner.InactiveMatrixBytes==0&&((IList)Get(owner,"facades")).Count==0,"Disposal after shutdown drops unpublished scratch and both generations");
    var dictionaries=work.GetType().GetFields(Hidden).Select(field=>field.GetValue(work)).OfType<IDictionary>().ToArray();Check(dictionaries.Length>0&&dictionaries.All(dictionary=>dictionary.Count==0),"Disposed scratch dictionary releases group ownership");
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  static void Limits()
  {
   var root=new GameObject("Facade explicit capacity bounds");var owner=root.AddComponent<AutomaticBuildingDetails>();
   try
   {
    var old=Enumerable.Range(0,4).Select(_=>Group(1,1,131072,65536)).ToArray();var current=Enumerable.Range(0,4).Select(_=>Group(131072,65536)).ToArray();Set(owner,"facades",Groups(old));Call(owner,"Publish",Groups(current));Pool(owner,"32MiB ceiling");Check(owner.InactiveGroups==2&&owner.InactiveMatrixBytes==24L*1024*1024,"Numeric byte ceiling rejects third12MiB retired group");
    var many=Enumerable.Range(0,2050).Select(_=>Group(1,1)).ToArray();Set(owner,"facades",Groups(many));Call(owner,"Publish",Groups(many.Select(_=>Group(1,1)).ToArray()));Pool(owner,"2048 ceiling");Check(owner.InactiveGroups==2048,"Numeric inactive group ceiling enforced");
    Set(owner,"facades",Groups(Group(1,1,262144,131072)));Call(owner,"Publish",Groups(Group(1,1,262144,131072)));Pool(owner,"inherited oversized tiny group");Check(owner.InactiveGroups==0&&owner.InactiveMatrixBytes==0,"Actual counts cannot self-justify inherited huge buffers");
    Call(owner,"OnDestroy");Check(((IList)Get(owner,"facades")).Count==0&&owner.InactiveGroups==0&&owner.InactiveMatrixBytes==0,"Shutdown releases published and inactive references");var stopped=(IEnumerator)Call(owner,"BuildIndex");Check(!stopped.MoveNext(),"Shutdown component cannot start another build");
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void Run()
  {
   checks=yieldObservations=reusedArrays=0;var root=new GameObject("Actual facade matrix reuse fixture");root.SetActive(false);var renderer=root.AddComponent<World>();var host=root.AddComponent<AutomaticWorldHost>();Set(host,"<Renderer>k__BackingField",renderer);var candidate=root.AddComponent<AutomaticBuildingDetails>();Set(candidate,"host",host);var original=new OriginalBuilder{renderer=renderer};
   try
   {
    var a=Map(0,36);var b=Map(1,20);b.buildings[0].id=a.buildings[0].id;
    Case(candidate,renderer,original,"first",new[]{a,b});Case(candidate,renderer,original,"repeat2",new[]{a,b});Case(candidate,renderer,original,"repeat3",new[]{a,b},true);
    int retainedBefore=candidate.RetainedFacadeGroups;Case(candidate,renderer,original,"retained-repeat",new[]{a,b},true);Check(candidate.RetainedFacadeGroups>retainedBefore,"Unchanged resident maps retain published facade groups");
    Case(candidate,renderer,original,"duplicate-owner-reorder",new[]{b,a});Case(candidate,renderer,original,"duplicate-owner-reorder-repeat",new[]{b,a},true);
    Case(candidate,renderer,original,"grow",new[]{Map(0,96),Map(1,64)});Case(candidate,renderer,original,"grow-repeat",new[]{Map(0,96),Map(1,64)});Case(candidate,renderer,original,"grow-reuse",new[]{Map(0,96),Map(1,64)},true);
    CancelAndFault(candidate,renderer,Map(0,96));
    Case(candidate,renderer,original,"reordered",new[]{b,a});Case(candidate,renderer,original,"shrink",new[]{Map(0,1)});Case(candidate,renderer,original,"empty",new[]{Map(0,0)});Check(candidate.InactiveMatrixBytes==0&&candidate.InactiveGroups==0,"Empty map drops all spare matrix arrays");
    Case(candidate,renderer,original,"single-huge1",new[]{Map(0,40,true)});Case(candidate,renderer,original,"single-huge2",new[]{Map(0,40,true)});
    Case(candidate,renderer,original,"single-tiny1",new[]{Map(0,1)});Check(((IList)Get(candidate,"facades")).Count==1&&Capacity(Get(candidate,"facades"))>Demand(Get(candidate,"facades"))&&candidate.InactiveMatrixBytes==0,"One real tiny group inherited a huge buffer without retaining a huge inactive generation");Case(candidate,renderer,original,"single-tiny2",new[]{Map(0,1)});Check(((IList)Get(candidate,"facades")).Count==1&&Capacity(Get(candidate,"facades"))<=Demand(Get(candidate,"facades")),"Second tiny generation drops previously inherited huge active arrays");Case(candidate,renderer,original,"single-tiny3",new[]{Map(0,1)});
    var arguments=Environment.GetCommandLineArgs();int fixture=Array.IndexOf(arguments,"-earthUnscriptedFacadeFixture");
    if(fixture>=0)
    {
     if(fixture+1>=arguments.Length)throw new ArgumentException("-earthUnscriptedFacadeFixture requires a prepared map JSON path");
     var real=JsonUtility.FromJson<World.Map>(File.ReadAllText(arguments[fixture+1]));Case(candidate,renderer,original,"explicit-real-core-first",new[]{real});Case(candidate,renderer,original,"explicit-real-core-repeat",new[]{real});Case(candidate,renderer,original,"explicit-real-core-reuse",new[]{real},true);Debug.Log("BUILDING REUSE explicit real fixture exercised");
    }
    else Debug.Log("BUILDING REUSE real fixture not requested; deterministic synthetic cases only");
    Check(yieldObservations>0&&reusedArrays>0,"Actual yielded builds and backing-array reuse were exercised");ShutdownPaused(host,renderer);Limits();
    Call(candidate,"OnDestroy");Check(candidate.InactiveMatrixBytes==0&&candidate.InactiveGroups==0&&((IList)Get(candidate,"facades")).Count==0,"Actual component shutdown releases both generations");
    Debug.Log("AUTOMATIC BUILDING REUSE PASS "+checks+" exact original geometry/order/visibility, yield observations="+yieldObservations+", reusedBackingArrays="+reusedArrays+"; array capacities/references only, no GC-byte or native-hitch claim");
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
 }
}
