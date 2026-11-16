using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using World=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class GeometryStreamingProof
    {
        static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<string> checks=new List<string>();
        static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
        static object Call(object o,string n,params object[] v)=>o.GetType().GetMethod(n,F).Invoke(o,v);
        static void Exhaust(IEnumerator e){while(e.MoveNext())if(e.Current is IEnumerator c)Exhaust(c);}
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        static bool Support()=>Physics.Raycast(new Vector3(1517.69f,52.55f,1743.55f),Vector3.down,2.5f);
        public static void Run()
        {
            int code=1;string error="";var root=new GameObject("Isolated native geometry reuse proof");
            try
            {
                var world=root.AddComponent<World>();var source=JsonUtility.FromJson<World.Map>(File.ReadAllText("../Feedback8/HoleCase/0_1.json"));
                var index=typeof(World).GetMethod("BuildSpatialIndex",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
                ((IDictionary)Get(world,"areas")).Add(new Vector2Int(0,1),source);((IDictionary)Get(world,"spatialByMap")).Add(source,index);
                Set(world,"map",new World.Map{routes=new[]{new World.DriveRoute{points=new[]{new Vector3(1480,51,1746),new Vector3(1540,51,1746)},stations=new[]{0f,60f}}}});
                var origin=new Vector3(1516,52,1743);Set(world,"observer",origin);Set(world,"embedded",true);Set(world,"embeddedHeading",Vector3.right);Call(world,"MakeMaterials");
                var key=new Vector2Int(23,27);Exhaust((IEnumerator)Call(world,"BuildChunk",key));
                Physics.SyncTransforms();Check(Support(),"Built road has physical support at the recorded approach");
                var active=(IDictionary)Get(world,"chunks");var chunk=(GameObject)active[key];var mesh=chunk.GetComponentInChildren<MeshFilter>().sharedMesh;
                Call(world,"RetainGeometry",key,chunk);active.Remove(key);Physics.SyncTransforms();
                Check(!chunk.activeSelf&&!Support(),"Retained geometry is inactive and does not leave invisible collision");
                Check(world.WarmGeometryChunks==1&&world.WarmGeometryEstimatedBytes>0&&world.WarmGeometryEstimatedBytes<=128L*1024*1024,"Recent native geometry is retained within its estimated memory budget");
                Check((bool)Call(world,"RestoreGeometry",key),"Recently visited geometry is reusable without downloading or rebuilding");Physics.SyncTransforms();
                Check(ReferenceEquals(active[key],chunk)&&ReferenceEquals(chunk.GetComponentInChildren<MeshFilter>().sharedMesh,mesh)&&Support(),"Reuse restores the exact same mesh and supported collider");
                Check(world.ReusedGeometryChunks==1&&world.WarmGeometryChunks==0&&world.WarmGeometryEstimatedBytes==0,"Restored geometry leaves the warm accounting and records a reuse");
                Check(!(bool)Call(world,"RestoreGeometry",key),"An already active chunk is not restored twice");
                int before=JObject.Parse(world.GeometryDiagnosticsJson())["generatedChunks"].Value<int>();
                Set(world,"ready",true);
                var scheduler=(IEnumerator)Call(world,"EnsureAround",origin);bool moved=false;
                while(scheduler.MoveNext())
                    if(scheduler.Current is IEnumerator child)
                    {
                        Exhaust(child);
                        if(!moved){Set(world,"observer",origin+Vector3.right*100);moved=true;}
                    }
                int after=JObject.Parse(world.GeometryDiagnosticsJson())["generatedChunks"].Value<int>();
                Check(moved&&after-before==1,"Generation stops the stale queue after one completed chunk when the car moves");
                Check(!(bool)Get(world,"building"),"Replanning releases the numeric-data publication gate");
                Check(((Vector3)Get(world,"lastReconcilePosition")).x==float.MaxValue,"Unfinished geometry is scheduled for a fresh current-position pass");
                code=0;
            }
            catch(Exception e){error=e.ToString();Debug.LogException(e);}
            finally
            {
                File.WriteAllText("../Feedback9/geometry-streaming-proof.json",new JObject{{"passed",code==0},{"error",error},{"checks",new JArray(checks)}}.ToString());
                var owner=root.GetComponent<World>();if(owner){((IList)Get(owner,"ownedRoots")).Clear();foreach(string name in new[]{"grass","asphalt","gravel","steel","sleeper","concrete","water","path","paint","wall","roof","redSignal","greenSignal"}){var m=Get(owner,name) as Material;if(m)UnityEngine.Object.DestroyImmediate(m);Set(owner,name,null);}}
                foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh)UnityEngine.Object.DestroyImmediate(f.sharedMesh);
                UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(code);
            }
        }
    }
}
