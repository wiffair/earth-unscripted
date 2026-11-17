using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class GeoSegmentStorageVerification
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly Type Region=typeof(GeoRegion),Segment=Region.GetNestedType("Segment",Flags);
        static readonly FieldInfo Storage=Region.GetField("querySegmentStorage",Flags),Source=Region.GetField("sourceCells",Flags),Render=Region.GetField("cells",Flags);
        static int checks;
        static void Check(bool ok,string why){if(!ok)throw new Exception("GEO SEGMENT STORAGE FAIL "+why);checks++;}
        static int ManagedStride()
        {var method=new DynamicMethod("GeoSegmentManagedStride",typeof(int),Type.EmptyTypes,Region,true);var il=method.GetILGenerator();il.Emit(OpCodes.Sizeof,Segment);il.Emit(OpCodes.Ret);return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();}
        static IEnumerable<object> Cells(GeoRegion region)
        {foreach(DictionaryEntry cell in (IDictionary)Source.GetValue(region))yield return cell.Value;if(!ReferenceEquals(Source.GetValue(region),Render.GetValue(region)))foreach(DictionaryEntry cell in (IDictionary)Render.GetValue(region))yield return cell.Value;}
        static void Ownership(GeoRegion region,string label)
        {
            var storage=(Array)Storage.GetValue(region);int pairs=0;checked{foreach(var road in region.roads)pairs+=Math.Max(0,road.points.Length-1);}
            Check(storage.Length==pairs&&region.RoadQueryUniqueSegments==pairs,label+" exact occurrence count includes duplicate roads and points");
            long count=0,capacity=0;foreach(var cell in Cells(region))
            {var type=cell.GetType();Check(ReferenceEquals(type.GetField("shared",Flags).GetValue(cell),storage),label+" every source/render cell borrows this snapshot's own value array");count+=(int)type.GetProperty("Count").GetValue(cell);capacity+=(int)type.GetProperty("Capacity").GetValue(cell);}
            Check(count==region.RoadQueryMemberships&&capacity==region.RoadQueryMembershipSlots,label+" counters retain exact membership and backing capacity");
            Check(region.SourceCellCount==((IDictionary)Source.GetValue(region)).Count,label+" source-cell count contract remains intact");
        }
        static GeoRegion Fixture(Type fixture)=> (GeoRegion)fixture.GetMethod("Fixture",Flags).Invoke(null,null);
        static void FootprintOracle(bool profiled)
        {
            var type=typeof(RoadQueryIndexSharingVerification);var actual=Fixture(type);var baseline=Fixture(type);actual.RebuildRenderQueryIndexesOnly(true);baseline.RebuildRenderQueryIndexesOnly(true);
            if(profiled){type.GetMethod("SetProfiles",Flags).Invoke(null,new object[]{actual});type.GetMethod("SetProfiles",Flags).Invoke(null,new object[]{baseline});actual.RebuildRenderQueryIndexesOnly(true);baseline.RebuildRenderQueryIndexesOnly(true);}
            var original=type.GetMethod("OriginalIndex",Flags);Source.SetValue(baseline,original.Invoke(null,new object[]{baseline,false}));Render.SetValue(baseline,original.Invoke(null,new object[]{baseline,true}));
            var random=new System.Random(88163);bool equal=true;
            for(int i=0;i<240;i++)
            {
                float x=(float)random.NextDouble()*640-320,z=(float)random.NextDouble()*640-320;var point=new Vector3(x,10,z);
                var ar=actual.Nearest(point,out var ap,out float ad);var br=baseline.Nearest(point,out var bp,out float bd);equal&=ar?.id==br?.id&&ap==bp&&ad==bd;
                var polygon=new[]{new Vector3(x-2,0,z-3),new Vector3(x+2,0,z-3),new Vector3(x+2,0,z+3),new Vector3(x-2,0,z+3)};
                equal&=actual.FootprintOverlapsRoad(polygon)==baseline.FootprintOverlapsRoad(polygon);
                equal&=actual.FootprintOverlapsRoadWithMargins(polygon,r=>r.width*.1f)==baseline.FootprintOverlapsRoadWithMargins(polygon,r=>r.width*.1f);
                equal&=actual.RoadSurfaceHeight(x,z)==baseline.RoadSurfaceHeight(x,z)&&actual.RoadSurfaceHeightOnLayer(x,z,0)==baseline.RoadSurfaceHeightOnLayer(x,z,0)&&actual.TerrainHeight(x,z)==baseline.TerrainHeight(x,z);
                var av=actual.ProjectRoadForVehicle(point,10);var bv=baseline.ProjectRoadForVehicle(point,10);equal&=av.Source.Road?.id==bv.Source.Road?.id&&av.Source.Point==bv.Source.Point&&av.SurfaceHeight==bv.SurfaceHeight;
            }
            Check(equal,(profiled?"profiled":"shared narrow")+" independent original value-index oracle retains nearest/footprint/surface/terrain/vehicle queries");Ownership(actual,profiled?"profiled":"narrow");
        }
        public static void Run()
        {
            checks=0;int stride=ManagedStride();Check(stride==48,"Managed IL sizeof confirms actual48-byte Segment stride (not Marshal.SizeOf)");
            FootprintOracle(false);FootprintOracle(true);
            var duplicate=Fixture(typeof(SurfaceQueryViewVerification));duplicate.RebuildRenderQueryIndexesOnly(true);Ownership(duplicate,"duplicate/noncontiguous occurrence");
            var previous=Storage.GetValue(duplicate);duplicate.RebuildRenderQueryIndexesOnly(true);Check(!ReferenceEquals(previous,Storage.GetValue(duplicate)),"Rebuild publishes a fresh owned array");Ownership(duplicate,"repeat rebuild");
            var owner=Fixture(typeof(SurfaceQueryViewVerification));owner.RebuildRenderQueryIndexesOnly(true);var old=Storage.GetValue(owner);Region.GetMethod("AdoptAutomaticQueries",Flags).Invoke(owner,new object[]{duplicate});
            Check(ReferenceEquals(Storage.GetValue(owner),Storage.GetValue(duplicate))&&!ReferenceEquals(old,Storage.GetValue(owner)),"Atomic adoption replaces the owner's previous array and adopts the new snapshot storage");Ownership(owner,"adopted snapshot");
            var first=new GeoRoad{id=null,start="a",end="b",NativeSourceWay=null,width=8,points=new[]{new Vector3(-10,2,0),new Vector3(10,2,0)}};var second=new GeoRoad{id="",start="c",end="d",NativeSourceWay="",width=8,points=(Vector3[])first.points.Clone()};
            var nullable=new GeoRegion{roads=new[]{first,second,first},waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),nx=2,nz=2,minX=-20,minZ=-20,width=40,depth=40,heights=new[]{0f,0f,0f,0f}};nullable.RebuildRenderQueryIndexesOnly(true);
            Check(ReferenceEquals(nullable.Nearest(Vector3.zero,out _,out _),first),"Null/empty IDs and equal-score ties retain first occurrence ownership");Ownership(nullable,"null/empty source identities");
            nullable.roads=Array.Empty<GeoRoad>();nullable.RebuildRenderQueryIndexesOnly(true);Check(nullable.RoadQueryUniqueSegments==0&&nullable.SourceCellCount==0&&nullable.RoadQueryMembershipSlots==0,"Empty rebuild releases old memberships and values");
            var emptyRoad=new GeoRoad{id="empty",start="e",end="f",width=8,points=Array.Empty<Vector3>()};nullable.roads=new[]{emptyRoad};nullable.RebuildRenderQueryIndexesOnly(true);Check(nullable.RoadQueryUniqueSegments==0&&nullable.SourceCellCount==0,"Empty point arrays create no phantom segment");
            var map=new Native.Map{coreMinX=-50,coreMaxX=200,coreMinZ=-50,coreMaxZ=50,nx=2,nz=2,heights=new[]{0f,0f,0f,0f},buildings=Array.Empty<Native.Building>(),features=new[]{new Native.Feature{id="road",sourceId="road",category="road",kind="residential",width=8,points=Enumerable.Range(0,80).Select(i=>new Vector3(i*2,10,0)).ToArray(),sourceNodeIds=Enumerable.Range(0,80).Select(i=>"sample:"+i).ToArray()}}};
            string source=JsonUtility.ToJson(map);var facade=Region.Assembly.GetType("EarthUnscripted.AutomaticWorldFacade").GetMethod("QuerySnapshot",Flags);
            var tasks=Enumerable.Range(0,4).Select(_=>Task.Run(()=>(GeoRegion)facade.Invoke(null,new object[]{new GeoRegion(),new[]{map}}))).ToArray();Task.WaitAll(tasks);
            Check(tasks.Select(task=>Storage.GetValue(task.Result)).Distinct().Count()==4,"Concurrent snapshots never borrow another snapshot's segment array");foreach(var task in tasks)Ownership(task.Result,"concurrent immutable snapshot");Check(JsonUtility.ToJson(map)==source,"Concurrent snapshots never alter their canonical numeric map");
            Debug.Log("GEO SEGMENT STORAGE PASS "+checks+" managedStride="+stride);
        }
        static void Payload()
        {
            var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"-earthUnscriptedGeoSnapshotFixture");if(flag<0||flag+1>=args.Length)throw new InvalidDataException("Prepared input directory required (-earthUnscriptedGeoSnapshotFixture)");
            var directory=Path.GetFullPath(args[flag+1]);var maps=new List<Native.Map>();var facade=Region.Assembly.GetType("EarthUnscripted.AutomaticWorldFacade").GetMethod("QuerySnapshot",Flags);int stride=ManagedStride();
            foreach(string tile in new[]{"-1_1","0_1","-1_2","0_2","-2_1","-2_2","-1_0","0_0"})
            {
                maps.Add(JsonUtility.FromJson<Native.Map>(File.ReadAllText(Path.Combine(directory,tile+".json"))));if(maps.Count!=4&&maps.Count!=8)continue;
                var snapshot=(GeoRegion)facade.Invoke(null,new object[]{new GeoRegion(),maps.ToArray()});Ownership(snapshot,"actual "+maps.Count+"-core snapshot");
                long oldPayload=snapshot.RoadQueryMembershipSlots*stride,newPayload=snapshot.RoadQueryUniqueSegments*(long)stride+snapshot.RoadQueryMembershipSlots*4;
                // Charge a conservative64B per new cell wrapper. Dictionary/list
                // headers common to both constructions are excluded.
                long conservativeSaved=oldPayload-newPayload-snapshot.RoadQueryCellCount*64L;
                if(maps.Count==8)Check(conservativeSaved>10000000,"Actual8-core query snapshot saves more than10MB even after conservative wrapper overhead");
                Debug.Log("GEO ACTUAL PAYLOAD cores="+maps.Count+" unique="+snapshot.RoadQueryUniqueSegments+" memberships="+snapshot.RoadQueryMemberships+" slots="+snapshot.RoadQueryMembershipSlots+" cells="+snapshot.RoadQueryCellCount+" oldValueBytes="+oldPayload+" newValueAndIndexBytes="+newPayload+" conservativeSavedBytes="+conservativeSaved+" managedStride="+stride+"; retained structural payload, not a GC/frame attribution");
            }
        }
        public static void Batch(){try{RoadQueryIndexSharingVerification.Run();SurfaceQueryViewVerification.Run();Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void PayloadBatch(){try{checks=0;Payload();Debug.Log("GEO ACTUAL PAYLOAD PASS "+checks);EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
