using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EarthUnscripted.NewbridgePrototype;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class ScenerySchedulingVerification
 {
  const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  public static void Run()
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception("SCENERY SCHEDULING "+why);}
   Check(NewbridgeWorld.DeferredSceneryCanProgress(false,0),"unresolved off-road support cannot starve a drained geometry queue");
   Check(!NewbridgeWorld.DeferredSceneryCanProgress(false,1),"actionable urgent geometry retains priority");
   Check(NewbridgeWorld.DeferredSceneryCanProgress(true,1),"existing ready-corridor behavior retained");
   var root=new GameObject("Isolated scenery scheduler fixture");
   try
   {
    var world=root.AddComponent<NewbridgeWorld>();typeof(NewbridgeWorld).GetField("ready",Hidden).SetValue(world,true);
    var chunks=(Dictionary<Vector2Int,GameObject>)typeof(NewbridgeWorld).GetField("chunks",Hidden).GetValue(world);
    var queue=typeof(NewbridgeWorld).GetMethod("QueueChunkScenery",Hidden);var complete=typeof(NewbridgeWorld).GetMethod("CompleteNextScenery",Hidden);
    for(int i=0;i<31;i++)
    {
     var key=new Vector2Int(i,0);var chunk=new GameObject("Supported transport chunk "+i);chunk.transform.SetParent(root.transform,false);chunks.Add(key,chunk);
     var houses=new List<NewbridgeWorld.Building>();for(int j=0;j<9;j++)houses.Add(new NewbridgeWorld.Building{id="underground-"+j,underground=true});
     queue.Invoke(world,new object[]{key,chunk,null,houses,null,null});
    }
    Check(world.PendingSceneryChunks==31,"actual queued work matches reported31-chunk stall");
    void Drain(IEnumerator work){var stack=new Stack<IEnumerator>();stack.Push(work);int steps=0;while(stack.Count>0){if(++steps>10000)throw new Exception("fixture coroutine failed to terminate");if(!stack.Peek().MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}if(stack.Peek().Current is IEnumerator child)stack.Push(child);}}
    Drain((IEnumerator)complete.Invoke(world,null));
    var workItems=(IDictionary)typeof(NewbridgeWorld).GetField("chunkScenery",Hidden).GetValue(world);int completed=0;foreach(var work in workItems.Values)completed+=(int)work.GetType().GetField("NextBuilding").GetValue(work);
    Check(completed>0&&completed<=8&&world.PendingSceneryChunks==31,"short scenery quantum resumes without dropping unfinished chunks");
    Check(typeof(NewbridgeWorld).GetField("workingMap",Hidden).GetValue(world)==null,"quantum releases shared map ownership");
    int passes=1;while(world.PendingSceneryChunks>0&&passes++<150)if(NewbridgeWorld.DeferredSceneryCanProgress(false,0))Drain((IEnumerator)complete.Invoke(world,null));
    Check(world.PendingSceneryChunks==0&&passes<150,"actual31 deferred chunks drain despite unresolved future support");
    Check(chunks.Count==31,"scenery progress never removes supported transport chunks");foreach(var chunk in chunks.Values)Check(chunk.activeInHierarchy,"transport remains active throughout deferred completion");
    var record=typeof(NewbridgeWorld).GetMethod("RecordGenerationSlice",Hidden);record.Invoke(world,new object[]{"Older slow slice",12d});record.Invoke(world,new object[]{"Current small slice",.3d});
    Check(world.CurrentGenerationSliceMilliseconds==.3&&world.CurrentGenerationSliceStage=="Current small slice","current time/stage follow latest slice rather than old peak");
    Check(world.MaximumGenerationSliceMilliseconds==12&&world.MaximumGenerationSliceStage=="Older slow slice","worst time/stage remain independent");
    typeof(NewbridgeWorld).GetField("currentGenerationSliceFrame",Hidden).SetValue(world,Time.frameCount-2);
    Check(world.CurrentGenerationSliceMilliseconds==0&&world.CurrentGenerationSliceStage=="Idle","idle frames do not repeat stale build work");
    record.Invoke(world,new object[]{"Invalid",double.NaN});Check(world.MaximumGenerationSliceMilliseconds==12,"invalid measurement cannot poison stats");
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
   Debug.Log("SCENERY SCHEDULING PASS "+checks);
  }
 }
}
