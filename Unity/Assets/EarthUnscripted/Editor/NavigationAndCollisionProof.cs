using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using World=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class NavigationAndCollisionProof
    {
        static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<string> checks=new List<string>();
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
        static GeoRoad Road(string id,Vector3[] p,string[] n,int oneWay=0)=>new GeoRoad{id=id,start=n[0],end=n[n.Length-1],sourceNodeIds=n,points=p,kind="residential",width=6,oneWay=oneWay};
        static void NoRoute(RoadNavigationGraph graph,Vector3 a,Vector3 b,string name){bool refused=false;try{graph.Plan(a,b);}catch(InvalidOperationException){refused=true;}Check(refused,name);}
        static void Graphs()
        {
            var straight=Road("a",new[]{Vector3.zero,new Vector3(100,0,0)},new[]{"A","B"});var graph=new RoadNavigationGraph(new[]{straight});
            Check(Mathf.Abs(graph.Plan(new Vector3(20,0,0),new Vector3(80,0,0)).Length-60)<.01f,"Road-interior endpoints produce exact distance");
            Check(Mathf.Abs(graph.Plan(new Vector3(80,0,0),new Vector3(20,0,0)).Length-60)<.01f,"Two-way road allows reverse direction");
            straight.oneWay=1;graph=new RoadNavigationGraph(new[]{straight});NoRoute(graph,new Vector3(80,0,0),new Vector3(20,0,0),"One-way road prohibits a backwards route");
            Check(Mathf.Abs(graph.Plan(new Vector3(20,0,0),new Vector3(80,0,0)).Length-60)<.01f,"One-way forward route remains valid");
            var horizontal=Road("h",new[]{Vector3.zero,new Vector3(5,0,0),new Vector3(10,0,0)},new[]{"H0","H1","H2"});
            var vertical=Road("v",new[]{new Vector3(5,0,-5),new Vector3(5,0,0),new Vector3(5,0,5)},new[]{"V0","V1","V2"});
            NoRoute(new RoadNavigationGraph(new[]{horizontal,vertical}),new Vector3(1,0,0),new Vector3(5,0,4),"Crossing XZ does not create a source junction");
            vertical.sourceNodeIds[1]="H1";Check(new RoadNavigationGraph(new[]{horizontal,vertical}).Plan(new Vector3(1,0,0),new Vector3(5,0,4)).Length>7.9f,"Explicit same-height shared node connects roads");
            for(int i=0;i<vertical.points.Length;i++)vertical.points[i].y=8;
            NoRoute(new RoadNavigationGraph(new[]{horizontal,vertical}),new Vector3(1,0,0),new Vector3(5,8,4),"Different road levels remain separate even with a shared source ID");
            NoRoute(new RoadNavigationGraph(new[]{horizontal}),Vector3.zero,new Vector3(5000,0,5000),"Destination outside the mapped roads is refused clearly");
            var route=new NavigationRoute(new[]{Vector3.zero,new Vector3(0,0,50),new Vector3(50,0,50)});Check(route.turns.Any(t=>t.angle>30),"Right turn produces a right-turn instruction");
            route=new NavigationRoute(new[]{Vector3.zero,new Vector3(0,0,50),new Vector3(-50,0,50)});Check(route.turns.Any(t=>t.angle<-30),"Left turn produces a left-turn instruction");
            Check(AutomaticScenery.TrunkRadius(24,5)>AutomaticScenery.TrunkRadius(5,1),"Larger trees have larger physical trunks");
        }
        static object Get(object obj,string name)=>obj.GetType().GetField(name,F).GetValue(obj);
        static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,F).SetValue(obj,value);
        static void Exhaust(IEnumerator work){while(work.MoveNext()){if(work.Current is IEnumerator child)Exhaust(child);}}
        public static void Run()
        {
            int code=1;string error="";GameObject root=null;
            try
            {
                Graphs();root=new GameObject("Isolated collision acceptance");var world=root.AddComponent<World>();
                var map=JsonUtility.FromJson<World.Map>(File.ReadAllText("../Feedback8/HoleCase/0_1.json"));
                var indexing=typeof(World).GetMethod("BuildSpatialIndex",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{map});
                ((IDictionary)Get(world,"areas")).Add(new Vector2Int(0,1),map);((IDictionary)Get(world,"spatialByMap")).Add(map,indexing);
                Set(world,"map",new World.Map{routes=new[]{new World.DriveRoute{points=new[]{new Vector3(1450,51,1746),new Vector3(1540,51,1746)},stations=new[]{0f,90f}}}});
                typeof(World).GetMethod("MakeMaterials",F).Invoke(world,null);
                var house=map.buildings.First(b=>b.centre.x>map.coreMinX+10&&b.centre.x<map.coreMaxX-10&&b.centre.z>map.coreMinZ+10&&b.centre.z<map.coreMaxZ-10&&b.points.Length>=4);
                var key=new Vector2Int(Mathf.FloorToInt(house.centre.x/64),Mathf.FloorToInt(house.centre.z/64));Set(world,"observer",house.centre);
                Exhaust((IEnumerator)typeof(World).GetMethod("BuildChunk",F).Invoke(world,new object[]{key}));Physics.SyncTransforms();
                var wall=root.GetComponentsInChildren<MeshCollider>().FirstOrDefault(c=>c.name=="Mapped building massing");
                Check(wall,"Real mapped building walls publish a physical mesh collider");
                var bounds=wall.bounds;Check(wall.Raycast(new Ray(bounds.center+Vector3.left*(bounds.extents.x+2),Vector3.right),out _,bounds.size.x+4),"A horizontal ray is blocked by the real building wall mesh");
                Check(root.GetComponentsInChildren<MeshCollider>().Any(c=>c.name=="Mapped roofs"),"Building roofs publish collision geometry");
                var host=root.AddComponent<AutomaticWorldHost>();Set(host,"<Renderer>k__BackingField",world);host.Observer=new GameObject("Collision observer").transform;host.Observer.SetParent(root.transform);host.Observer.position=new Vector3(1515,52,1743);
                var scenery=root.AddComponent<AutomaticScenery>();Set(scenery,"host",host);Set(scenery,"art",Resources.Load<AutomaticSceneryResources>("AutomaticWorld/Scenery"));
                Exhaust((IEnumerator)typeof(AutomaticScenery).GetMethod("BuildIndex",F).Invoke(scenery,null));
                var update=typeof(AutomaticScenery).GetMethod("UpdateTreeCollisions",F);for(int i=0;i<150;i++)update.Invoke(scenery,null);Physics.SyncTransforms();
                var trunks=root.GetComponentsInChildren<CapsuleCollider>();Check(trunks.Length>0,"Visible nearby tree records produce trunk colliders");
                var trunk=trunks[0];Check(trunk.Raycast(new Ray(trunk.bounds.center+Vector3.left*(trunk.radius+1),Vector3.right),out _,trunk.radius*2+2),"Tree trunk physically blocks a horizontal ray");
                Check(trunks.Length<=3072,"Tree collision population remains bounded");
                host.Observer.position+=Vector3.right*1000;for(int i=0;i<150;i++)update.Invoke(scenery,null);
                Check(root.GetComponentsInChildren<CapsuleCollider>().All(c=>Vector2.Distance(new Vector2(c.transform.position.x,c.transform.position.z),new Vector2(host.Observer.position.x,host.Observer.position.z))<=221),"Distant tree colliders are retired when driving onward");
                code=0;
            }
            catch(Exception e){error=e.ToString();Debug.LogException(e);}
            finally
            {
                File.WriteAllText("../Feedback8/navigation-collision-proof.json",new JObject{{"passed",code==0},{"error",error},{"checks",new JArray(checks)}}.ToString());
                if(root){var world=root.GetComponent<World>();if(world){((IList)Get(world,"ownedRoots")).Clear();foreach(string name in new[]{"grass","asphalt","gravel","steel","sleeper","concrete","water","path","paint","wall","roof","redSignal","greenSignal"}){var mat=Get(world,name) as Material;if(mat)UnityEngine.Object.DestroyImmediate(mat);Set(world,name,null);}}foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh)UnityEngine.Object.DestroyImmediate(filter.sharedMesh);UnityEngine.Object.DestroyImmediate(root);}
                EditorApplication.Exit(code);
            }
        }
    }
}
