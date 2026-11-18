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
    foreach(int count in new[]{1,16,19,32})
    {
     string key=FinishedGeometryStore.Digest("actual batched mesh layout "+count);var source=Parts(count);store.Write(key,source);Wait();
     var restored=store.Read(key).GetAwaiter().GetResult();Check(restored!=null&&restored.Length==count,"actual disk roundtrip accepts supported layout "+count);
     for(int i=0;i<count;i++)Check(restored[i].vertices.SequenceEqual(source[i].vertices)&&restored[i].normals.SequenceEqual(source[i].normals)&&restored[i].indices.SequenceEqual(source[i].indices),"batch identity and exact vertices/normals/indices retained "+count+"/"+i);
    }
    string invalid=FinishedGeometryStore.Digest("over-limit layout");store.Write(invalid,Parts(33));Wait();Check(store.Read(invalid).GetAwaiter().GetResult()==null,"over-limit writer rejects rather than creating unreadable cache");
    Check(store.Hits==4&&store.Writes==4,"real supported writes are reusable hits");
    UnityEngine.Debug.Log("FINISHED GEOMETRY CACHE PASS "+checks+"; actual disk layouts1/16/19/32, including railway batches");
   }
   finally{Wait();if(Directory.Exists(folder))Directory.Delete(folder,true);}
  }
 }
}
