using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using EarthUnscripted.NewbridgePrototype;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class FinishedGeometryCacheVerification
 {
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){UnityEngine.Debug.LogException(e);EditorApplication.Exit(1);}}
  public static void Run()
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception("FINISHED CACHE "+why);}
   string folder=Path.Combine(Path.GetTempPath(),"EarthUnscriptedFinishedCache-"+Guid.NewGuid().ToString("N"));var store=new FinishedGeometryStore(folder);
   void Wait(){var watch=Stopwatch.StartNew();while(store.Pending>0){if(watch.Elapsed.TotalSeconds>10)throw new TimeoutException("Finished cache write");Thread.Sleep(1);}}
   FinishedGeometryStore.Part[] Parts(int count)=>Enumerable.Range(0,count).Select(i=>new FinishedGeometryStore.Part{vertices=new[]{new Vector3(i,0,0),new Vector3(i,0,1),new Vector3(i+1,0,0)},normals=new[]{Vector3.up,Vector3.up,Vector3.up},indices=new[]{0,1,2}}).ToArray();
   try
   {
    var encode=(Func<FinishedGeometryStore.Part[],byte[]>)Delegate.CreateDelegate(typeof(Func<FinishedGeometryStore.Part[],byte[]>),typeof(FinishedGeometryStore).GetMethod("Encode",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic));
    byte[] OriginalPayload(FinishedGeometryStore.Part[] parts)
    {
     using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write(parts.Length);
     foreach(var part in parts){writer.Write(part.vertices.Length);writer.Write(part.indices.Length);foreach(var points in new[]{part.vertices,part.normals})foreach(var point in points){writer.Write(point.x);writer.Write(point.y);writer.Write(point.z);}foreach(int index in part.indices)writer.Write(index);}return stream.ToArray();
    }
    var floats=new[]{0f,BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),float.Epsilon,-float.Epsilon,float.MaxValue,float.MinValue,1.25f,-17.125f};
    var dense=new[]{new FinishedGeometryStore.Part{vertices=Enumerable.Range(0,16000).Select(i=>new Vector3(floats[i%floats.Length],floats[(i+1)%floats.Length],i*.03125f)).ToArray(),normals=Enumerable.Range(0,16000).Select(i=>new Vector3(0,1,floats[(i+2)%floats.Length])).ToArray(),indices=Enumerable.Range(0,48000).Select(i=>i%16000).ToArray()}};
    var expected=OriginalPayload(dense);encode(Parts(1));long allocationStart=GC.GetAllocatedBytesForCurrentThread();var encoded=encode(dense);long allocated=GC.GetAllocatedBytesForCurrentThread()-allocationStart;
    Check(encoded.SequenceEqual(expected),"numeric payload is byte-identical to the original BinaryWriter format, including signed zero and finite float extremes");
    // Unity's non-generational Mono collector can expose this API as a zero
    // counter. Never report that unsupported measurement as zero allocation.
    if(allocated>0)Check(allocated<=encoded.Length+2048,"mesh cache encoding allocates one payload, without per-float temporary arrays");
    UnityEngine.Debug.Log("FINISHED CACHE ENCODE allocationCounterSupported="+(allocated>0)+" bytes="+allocated+" payload="+encoded.Length+"; native profiler verifies unsupported counters");
    foreach(int count in new[]{1,16,19,32})
    {
     string key=FinishedGeometryStore.Digest("actual batched mesh layout "+count);var source=Parts(count);store.Write(key,source);Wait();
     var restored=store.Read(key).GetAwaiter().GetResult();Check(restored!=null&&restored.Length==count,"actual disk roundtrip accepts supported layout "+count);
     for(int i=0;i<count;i++)Check(restored[i].vertices.SequenceEqual(source[i].vertices)&&restored[i].normals.SequenceEqual(source[i].normals)&&restored[i].indices.SequenceEqual(source[i].indices),"batch identity and exact vertices/normals/indices retained "+count+"/"+i);
    }
    var deferred=Parts(19);var tunnel=deferred[11];var river=deferred[12];var initial=deferred[14];
    deferred[14]=NewbridgeWorld.CombineBuildingParts(deferred[14],Parts(1)[0]);
    deferred[15]=NewbridgeWorld.CombineBuildingParts(deferred[15],Parts(1)[0]);
    string merged=FinishedGeometryStore.Digest("boot and deferred shells");store.Write(merged,deferred);Wait();
    var restoredMerged=store.Read(merged).GetAwaiter().GetResult();
    Check(restoredMerged[11].vertices.SequenceEqual(tunnel.vertices)&&restoredMerged[12].vertices.SequenceEqual(river.vertices),"deferred cache preserves tunnels and water slots");
    Check(restoredMerged[14].vertices.Take(3).SequenceEqual(initial.vertices)&&restoredMerged[14].vertices.Length==6&&restoredMerged[14].indices.SequenceEqual(new[]{0,1,2,3,4,5}),"boot and later walls both survive roundtrip with offset indices");
    var transport=NewbridgeWorld.TransportParts(deferred,14,15);string transportKey=NewbridgeWorld.TransportCacheKey(merged);
    Check(transportKey!=merged&&deferred[14].vertices.Length==6,"transport snapshot has a separate identity and does not mutate complete shells");
    store.Write(transportKey,transport);Wait();var restoredTransport=store.Read(transportKey).GetAwaiter().GetResult();
    Check(restoredTransport[14].vertices.Length==0&&restoredTransport[15].vertices.Length==0&&restoredTransport[4].vertices.SequenceEqual(deferred[4].vertices),"interrupted scenery retains exact roads without claiming building completion");
    string invalid=FinishedGeometryStore.Digest("over-limit layout");store.Write(invalid,Parts(33));Wait();Check(store.Read(invalid).GetAwaiter().GetResult()==null,"over-limit writer rejects rather than creating unreadable cache");
    var dependencyRoot=new GameObject("Actual mesh-cache source dependency fixture");
    try
    {
     var world=dependencyRoot.AddComponent<NewbridgeWorld>();world.enabled=false;
     var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=typeof(NewbridgeWorld);
     type.GetField("finishedStore",hidden).SetValue(world,store);
     var areas=(System.Collections.Generic.Dictionary<Vector2Int,NewbridgeWorld.Map>)type.GetField("areas",hidden).GetValue(world);
     var hashes=(System.Collections.Generic.Dictionary<NewbridgeWorld.Map,string>)type.GetField("finishedSourceHashes",hidden).GetValue(world);
     var indexes=(System.Collections.IDictionary)type.GetField("spatialByMap",hidden).GetValue(world);
     var spatialType=type.GetNestedType("SpatialData",System.Reflection.BindingFlags.NonPublic);
     NewbridgeWorld.Map Add(int x,int z,float height,string hash)
     {
      var map=new NewbridgeWorld.Map{tileX=x,tileZ=z,minX=x*1536,minZ=z*1536,coreMinX=x*1536,coreMinZ=z*1536,coreMaxX=(x+1)*1536,coreMaxZ=(z+1)*1536,gridStep=1536,nx=2,nz=2,heights=new[]{height,height,height,height},features=Array.Empty<NewbridgeWorld.Feature>(),buildings=Array.Empty<NewbridgeWorld.Building>()};
      areas[new Vector2Int(x,z)]=map;hashes[map]=hash;indexes[map]=Activator.CreateInstance(spatialType,true);return map;
     }
     string Key(Vector2Int cell)=>(string)type.GetMethod("FinishedChunkKey",hidden).Invoke(world,new object[]{cell});
     float Height(float x,float z)=>(float)type.GetMethod("RawHeight",hidden).Invoke(world,new object[]{x,z});
     var owner=Add(0,0,0,new string('a',64));type.GetField("workingMap",hidden).SetValue(world,owner);
     var interior=new Vector2Int(12,12);string original=Key(interior);float ground=Height(800,800);
     var diagonal=Add(1,1,50,new string('b',64));
     Check(Key(interior)==original&&Height(800,800)==ground,"irrelevant neighbouring residency preserves both the actual interior ground query and finished key");
     hashes[diagonal]=new string('c',64);Check(Key(interior)==original,"a remote neighbour revision cannot invalidate an interior mesh");
     var edge=new Vector2Int(23,12);string absentEdge=Key(edge);float edgeGround=Height(1536,800);
     var right=Add(1,0,20,new string('d',64));
     Check(Height(1536,800)!=edgeGround&&Key(edge)!=absentEdge,"the actual boundary terrain query and key both change when its missing owner becomes resident");
     string presentEdge=Key(edge);hashes[right]=new string('e',64);Check(Key(edge)!=presentEdge,"a relevant neighbouring source revision still invalidates edge meshes");
     hashes.Remove(right);Check(Key(edge)==null,"an unverified relevant source cannot produce a reusable mesh key");hashes[right]=new string('d',64);
     var houses=(System.Collections.Generic.Dictionary<Vector2Int,System.Collections.Generic.List<NewbridgeWorld.Building>>)spatialType.GetField("buildings").GetValue(indexes[owner]);
     houses[interior]=new System.Collections.Generic.List<NewbridgeWorld.Building>{new NewbridgeWorld.Building{points=new[]{new Vector3(-100,0,-100),new Vector3(1700,0,-100),new Vector3(1700,0,1700),new Vector3(-100,0,1700)}}};
     string large=Key(interior);float roofSample=Height(1700,1700);areas.Remove(new Vector2Int(1,1));
     Check(Height(1700,1700)!=roofSample&&Key(interior)!=large,"a deferred large building keeps the far corner's actual terrain dependency in its key");
     houses.Clear();hashes[owner]=new string('f',64);Check(Key(interior)!=original,"the owning numeric source always invalidates its finished geometry");
     houses[interior]=new System.Collections.Generic.List<NewbridgeWorld.Building>{new NewbridgeWorld.Building{points=new[]{new Vector3(-100000,0,-100000),new Vector3(100000,0,100000)}}};
     Check(Key(interior)==null,"extreme footprints decline disk caching instead of scanning an unbounded area");houses.Clear();
     foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
     {
      var map=Add(x,z,0,new string('9',64));var cell=new Vector2Int(x*24+12,z*24+12);
      string prior=Key(cell);Add(x+1,z+1,50,new string('8',64));Check(prior!=null&&Key(cell)==prior,"interior cache dependencies stay stable in every coordinate quadrant");
     }
    }
    finally{UnityEngine.Object.DestroyImmediate(dependencyRoot);}
    Check(store.Hits==6&&store.Writes==6,"real supported writes are reusable hits");
    UnityEngine.Debug.Log("FINISHED GEOMETRY CACHE PASS "+checks+"; actual disk layouts1/16/19/32, including railway batches");
   }
   finally{Wait();if(Directory.Exists(folder))Directory.Delete(folder,true);}
  }
 }
}
