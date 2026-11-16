using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using World=RoadRush.NewbridgePrototype.NewbridgeWorld;
namespace RoadRush.Editor
{
    public static class CoverageFallbackProof
    {
        static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<string> checks=new List<string>();
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        public static void Run()
        {
            var root=new GameObject("Isolated empty-frontier coverage proof");int code=1;string error="";
            try
            {
                var world=root.AddComponent<World>();var areas=(IDictionary)typeof(World).GetField("areas",F).GetValue(world);
                var missing=new Vector2Int(-11,-10);
                for(int x=-13;x<=-9;x++)for(int z=-11;z<=-7;z++)if(new Vector2Int(x,z)!=missing)areas.Add(new Vector2Int(x,z),new World.Map{tileX=x,tileZ=z});
                typeof(World).GetField("observer",F).SetValue(world,new Vector3(-15800,60,-13780));
                typeof(World).GetField("embedded",F).SetValue(world,true);typeof(World).GetField("embeddedHeading",F).SetValue(world,new Vector3(-1,0,-1).normalized);
                object Candidate()=>typeof(World).GetMethod("NearbyDataCandidate",F).Invoke(world,null);
                int I(object o,string n)=>(int)o.GetType().GetField(n).GetValue(o);
                float V(object o,string n)=>(float)o.GetType().GetField(n).GetValue(o);
                var c=Candidate();Check(c!=null,"An empty road frontier still acquires a nearby missing core while stationary");
                Check(I(c,"tileX")==missing.x&&I(c,"tileZ")==missing.y,"The A35 boundary reproduction selects its missing southern neighbour");
                Check(Mathf.FloorToInt(V(c,"x")/1536)==missing.x&&Mathf.FloorToInt(V(c,"z")/1536)==missing.y,"Fallback request lies strictly inside the missing core, including negative coordinates");
                Check(V(c,"distanceMetres")<50,"Boundary acquisition does not require the driver to enter missing geometry");
                var retry=(IDictionary)typeof(World).GetField("areaRetryAfter",F).GetValue(world);retry.Add(missing,1e9);
                Check(Candidate()==null,"A failed core respects its existing retry backoff");retry.Clear();
                areas.Add(missing,new World.Map{tileX=missing.x,tileZ=missing.y});Check(Candidate()==null,"Fully resident coverage causes no repeated source requests");
                areas.Remove(missing);typeof(World).GetField("observer",F).SetValue(world,new Vector3(-15800,60,-10000));
                c=Candidate();Check(c!=null&&V(c,"distanceMetres")<=1800&&(I(c,"tileX")!=missing.x||I(c,"tileZ")!=missing.y),"Fallback acquires local coverage rather than a distant missing core");
                code=0;
            }
            catch(Exception e){error=e.ToString();Debug.LogException(e);}
            finally{File.WriteAllText("../Feedback10/coverage-fallback-proof.json",new JObject{{"passed",code==0},{"checks",new JArray(checks)},{"error",error}}.ToString());UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(code);}
        }
    }
}
