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
    public static class PortalFloorProof
    {
        static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,F).SetValue(obj,value);
        static object Get(object obj,string name)=>obj.GetType().GetField(name,F).GetValue(obj);
        public static void Run()
        {
            var root=new GameObject("Isolated physical portal proof");int code=1;var rows=new JArray();string error="";
            try
            {
                var world=root.AddComponent<World>();var source=JsonUtility.FromJson<World.Map>(File.ReadAllText("../Feedback8/HoleCase/0_1.json"));
                var index=typeof(World).GetMethod("BuildSpatialIndex",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
                ((IDictionary)Get(world,"areas")).Add(new Vector2Int(0,1),source);((IDictionary)Get(world,"spatialByMap")).Add(source,index);
                Set(world,"map",new World.Map{routes=new[]{new World.DriveRoute{points=new[]{new Vector3(1480,51,1746),new Vector3(1540,51,1746)},stations=new[]{0f,60f}}}});
                Set(world,"observer",new Vector3(1516,52,1743));typeof(World).GetMethod("MakeMaterials",F).Invoke(world,null);
                var work=(IEnumerator)typeof(World).GetMethod("BuildChunk",F).Invoke(world,new object[]{new Vector2Int(23,27)});while(work.MoveNext()){}
                Physics.SyncTransforms();int missing=0;
                foreach(var p in new[]{new Vector3(1517.69f,51.55f,1743.55f),new Vector3(1513.58f,51.2f,1742.45f),new Vector3(1489.9f,55.72f,1776.45f)})
                    for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                    {
                        var q=p+new Vector3(x*.8f,0,z*1.3f);bool hit=Physics.Raycast(q+Vector3.up,Vector3.down,out var support,2.5f,~0,QueryTriggerInteraction.Ignore);
                        rows.Add(new JObject{{"point",JsonUtility.ToJson(q)},{"supported",hit},{"collider",hit?support.collider.name:""},{"height",hit?support.point.y:0}});if(!hit)missing++;
                    }
                int corridorSamples=0,blocked=0;
                foreach(var feature in source.features)
                {
                    if(!feature.tunnel||feature.category!="road")continue;
                    for(int i=1;i<feature.points.Length;i++)
                    {
                        var a=feature.points[i-1];var b=feature.points[i];var middle=(a+b)*.5f;
                        if(middle.x<1473||middle.x>1535||middle.z<1729||middle.z>1791)continue;
                        var forward=Vector3.ProjectOnPlane(b-a,Vector3.up).normalized;var side=Vector3.Cross(Vector3.up,forward);
                        foreach(float lateral in new[]{-1.3f,0,1.3f})
                        {
                            corridorSamples++;var at=middle+side*lateral+Vector3.up*1.2f;
                            if(Physics.Raycast(at,forward,out var hit,1.5f) && hit.collider.name=="Terrain")blocked++;
                        }
                    }
                }
                rows.Add(new JObject{{"tunnelCorridorSamples",corridorSamples},{"terrainObstructions",blocked}});
                if(blocked>0)throw new Exception("New terrain bank blocks tunnel interior");
                if(missing>0)throw new Exception("Missing physical wheel/shoulder support at "+missing+" recorded footprint samples");
                code=0;
            }
            catch(Exception e){error=e.ToString();Debug.LogException(e);}
            finally{File.WriteAllText("../Feedback8/portal-floor-proof.json",new JObject{{"passed",code==0},{"error",error},{"samples",rows}}.ToString());var owner=root.GetComponent<World>();
                if(owner){((IList)Get(owner,"ownedRoots")).Clear();foreach(string name in new[]{"grass","asphalt","gravel","steel","sleeper","concrete","water","path","paint","wall","roof","redSignal","greenSignal"}){var material=Get(owner,name) as Material;if(material)UnityEngine.Object.DestroyImmediate(material);Set(owner,name,null);}}
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh)UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(code);}
        }
    }
}
