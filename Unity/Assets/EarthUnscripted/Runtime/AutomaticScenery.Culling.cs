using NativeWorld = EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
using System.Collections.Generic;using Unity.Collections;using Unity.Jobs;using UnityEngine;
namespace EarthUnscripted {
 public sealed partial class AutomaticScenery {
  NativeArray<TreeCullInput> cullInputs;NativeArray<TreeCullOutput> cullOutputs;NativeArray<TreeCullGroup> cullBounds;NativeArray<byte> cullAllowed,cullGroups;NativeArray<Plane> cullPlanes;
  TreeCullOutput[] cullManagedOutput;readonly List<Tree> cullRecords=new List<Tree>();List<Grove> cullSource;int cullGroupCount,cullGroundRevision=int.MinValue,cullCoastRevision=int.MinValue,cullDamageRevision=-1,treeDamageRevision;
  static void Grow<T>(ref NativeArray<T> data,int count) where T:struct {if(data.IsCreated&&data.Length>=count)return;if(data.IsCreated)data.Dispose();data=new NativeArray<T>(Mathf.Max(128,count),Allocator.Persistent,NativeArrayOptions.UninitializedMemory);}
  void DisposeTreeCulling(){if(cullInputs.IsCreated)cullInputs.Dispose();if(cullOutputs.IsCreated)cullOutputs.Dispose();if(cullBounds.IsCreated)cullBounds.Dispose();if(cullAllowed.IsCreated)cullAllowed.Dispose();if(cullGroups.IsCreated)cullGroups.Dispose();if(cullPlanes.IsCreated)cullPlanes.Dispose();cullRecords.Clear();cullManagedOutput=null;cullSource=null;}
  void PrepareTreeCulling(NativeWorld renderer,int groundRevision,int coastRevision)
  {
   if(!ReferenceEquals(cullSource,groves))
   {
    cullSource=groves;cullRecords.Clear();foreach(var g in groves)cullRecords.AddRange(g.trees);cullGroupCount=groves.Count;
    if(cullManagedOutput==null||cullManagedOutput.Length<cullRecords.Count)cullManagedOutput=new TreeCullOutput[Mathf.Max(128,cullRecords.Count)];
    Grow(ref cullInputs,cullRecords.Count);Grow(ref cullOutputs,cullRecords.Count);Grow(ref cullAllowed,cullRecords.Count);Grow(ref cullBounds,cullGroupCount);Grow(ref cullGroups,cullGroupCount);Grow(ref cullPlanes,6);
    int index=0;for(int g=0;g<groves.Count;g++){var group=groves[g];cullBounds[g]=new TreeCullGroup{centre=group.bounds.center,extents=group.bounds.extents};foreach(var t in group.trees)cullInputs[index++]=new TreeCullInput{position=new Vector3(t.source.x,t.groundY,t.source.z),born=t.born,group=g,seed=t.source.seed};}
    cullGroundRevision=int.MinValue;
   }
   if(cullGroundRevision!=groundRevision||cullCoastRevision!=coastRevision||cullDamageRevision!=treeDamageRevision)
   {
    // All Unity object/support queries remain on the main thread. A changed
    // terrain, coast, tree population or break invalidates eligibility before
    // any numeric job or draw; unchanged frames reuse these verified flags.
    for(int i=0;i<cullRecords.Count;i++){var t=cullRecords[i];bool ok=!t.broken&&t.supported&&t.coastRevision==coastRevision;if(ok&&t.groundRevision!=groundRevision)ok=t.supportStamp!=0&&t.supportStamp==renderer.PublishedScenerySupportStamp(t.source.x,t.source.z);cullAllowed[i]=(byte)(ok?1:0);}
    cullGroundRevision=groundRevision;cullCoastRevision=coastRevision;cullDamageRevision=treeDamageRevision;
   }
  }
  void DrawCulledTrees(Camera camera,Vector3 eye,float range,float detail,int stride,float now,NativeWorld renderer,int groundRevision,int coastRevision)
  {
   VisibleTrees=DetailedTrees=0;if(captureGrounding)groundingProof.Clear();for(int i=0;i<3;i++){farCount[i]=0;nearCount[i]=0;}
   PrepareTreeCulling(renderer,groundRevision,coastRevision);if(cullRecords.Count==0)return;for(int i=0;i<6;i++)cullPlanes[i]=planes[i];
   var groups=new TreeGroupCullJob{groups=cullBounds,planes=cullPlanes,visible=cullGroups,eye=eye,rangeSquared=range*range}.Schedule(cullGroupCount,64);
   var trees=new TreeVisibilityJob{trees=cullInputs,allowed=cullAllowed,groups=cullGroups,output=cullOutputs,eye=eye,time=now,range=range,detail=detail,stride=stride}.Schedule(cullRecords.Count,256,groups);
   // Complete before scene publication/retirement can change source ownership.
   // The jobs own numeric arrays only; no Unity scene or rendering API is used.
   trees.Complete();
   // Copy once: repeatedly crossing the NativeArray indexer from Mono costs
   // more than this small contiguous transfer on dense forests.
   NativeArray<TreeCullOutput>.Copy(cullOutputs,0,cullManagedOutput,0,cullRecords.Count);
   for(int i=0;i<cullRecords.Count;i++){var o=cullManagedOutput[i];if(o.mask==0)continue;var record=cullRecords[i];int variant=record.source.seed%3;if((o.mask&1)!=0){nearFade[variant][nearCount[variant]]=new Vector4(o.visibility,o.nearWeight,1,0);near[variant][nearCount[variant]++]=record.near;DetailedTrees++;if(nearCount[variant]==1023)Draw(variant,true,camera);}if((o.mask&2)!=0){farFade[variant][farCount[variant]]=new Vector4(o.visibility,o.nearWeight,0,0);far[variant][farCount[variant]++]=record.far;if(farCount[variant]==1023)Draw(variant,false,camera);}if(captureGrounding&&o.visibility>.5f&&groundingProof.Count<128)groundingProof.Add(record);VisibleTrees++;}
   for(int i=0;i<3;i++){Draw(i,true,camera);Draw(i,false,camera);}
  }
 }
}
