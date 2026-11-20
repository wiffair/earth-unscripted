using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
    public static class CoastalRefreshBudgetVerification
    {
        public static void Run()
        {
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            var root=new GameObject("Cumulative unchanged coastal terrain budget");var mesh=new Mesh{name="Unchanged inland support"};
            try
            {
                mesh.vertices=new[]{new Vector3(0,20,0),new Vector3(0,20,2),new Vector3(2,20,2),new Vector3(2,20,0)};
                mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
                var world=root.AddComponent<NewbridgeWorld>();world.enabled=false;var type=typeof(NewbridgeWorld);
                var chunks=(Dictionary<Vector2Int,GameObject>)type.GetField("chunks",hidden).GetValue(world);
                var hiddenTerrain=(Dictionary<MeshRenderer,bool>)type.GetField("coastalHiddenTerrain",hidden).GetValue(world);
                var renderers=new List<MeshRenderer>();
                for(int i=63;i>=0;i--)
                {
                    var chunk=new GameObject("Existing chunk "+i);chunk.transform.SetParent(root.transform,false);chunk.transform.localPosition=new Vector3(i*64,0,0);
                    var terrain=new GameObject("Terrain");terrain.transform.SetParent(chunk.transform,false);
                    terrain.AddComponent<MeshFilter>().sharedMesh=mesh;terrain.AddComponent<MeshCollider>().sharedMesh=mesh;
                    var renderer=terrain.AddComponent<MeshRenderer>();renderer.enabled=false;hiddenTerrain.Add(renderer,true);renderers.Insert(0,renderer);chunks.Add(new Vector2Int(i,0),chunk);
                }
                var work=(IEnumerator)type.GetMethod("RefreshCoastalBeds",hidden).Invoke(world,null);int yields=0,last=0;bool slicedUnchanged=false;
                while(work.MoveNext())
                {
                    if(++yields>1000)throw new Exception("Unchanged coastal refresh did not finish");
                    int completed=world.CoastalVisualChunksProcessed;
                    if(completed-last>8)throw new Exception("Cheap unchanged meshes escaped the cumulative eight-mesh scheduling bound");
                    if(completed>0&&completed<64)slicedUnchanged=true;
                    for(int i=0;i<renderers.Count;i++)
                    {
                        if(renderers[i].enabled!=(i<completed))throw new Exception("Coastal terrain no longer refreshes nearest bounds first");
                        if(renderers[i].GetComponent<MeshFilter>().sharedMesh!=mesh||renderers[i].GetComponent<MeshCollider>().sharedMesh!=mesh)
                            throw new Exception("Yielding unchanged coastal terrain altered its visual or physical support");
                    }
                    last=completed;
                }
                if(!slicedUnchanged||world.CoastalVisualChunksProcessed!=64||world.CoastalVisualChunksTotal!=64||hiddenTerrain.Count!=0)
                    throw new Exception("Every unchanged coastal mesh must finish across real scheduling turns and restore its visibility");
                Debug.Log("COASTAL REFRESH BUDGET PASS 64 meshes; nearest-first output, bounded unchanged scans, unchanged colliders and completed visibility");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
}
