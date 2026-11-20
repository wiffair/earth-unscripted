using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using World=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class SourceEdgeRibbonVerification
    {
        const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        static int checks;
        static void Check(bool ok,string why){checks++;if(!ok)throw new Exception("Source edge ribbons: "+why);}
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Hidden).Invoke(target,args);
        static World.Map Core(float x=0,float z=0)=>new World.Map{coreMinX=x,coreMaxX=x+64,coreMinZ=z,coreMaxZ=z+64};
        static object Batch(World.Map map)
        {var type=typeof(World).GetNestedType("Batch",Hidden);var batch=Activator.CreateInstance(type,Hidden,null,new object[]{"Road asphalt",null,false},null);Call(batch,"ClipToSource",map);return batch;}
        static Vector3[] Vertices(object batch)=>((IReadOnlyList<Vector3>)batch.GetType().GetProperty("Vertices",Hidden).GetValue(batch)).ToArray();
        static int[] Indices(object batch)=>((IReadOnlyList<int>)batch.GetType().GetProperty("Indices",Hidden).GetValue(batch)).ToArray();
        static double Area(object batch)
        {var v=Vertices(batch);var t=Indices(batch);double area=0;for(int i=0;i<t.Length;i+=3)area+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5;return area;}
        static object Ranges(World.Map map,World.Feature f,Vector3 a,Vector3 b,float x=0,float z=0)=>typeof(World).GetMethod("SourceEdgeRanges",Hidden).Invoke(null,new object[]{map,f,a,b,x,z});
        static int Count(object ranges)=>(int)ranges.GetType().GetField("count",Hidden).GetValue(ranges);
        static Vector2 At(object ranges,int i)=>(Vector2)Call(ranges,"At",i);
        public static void BatchRun(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            checks=0;var core=Core();
            var inside=Batch(core);Call(inside,"Quad",new Vector3(1,0,1),new Vector3(1,0,3),new Vector3(3,0,3),new Vector3(3,0,1));
            Check(Vertices(inside).Length==4&&Indices(inside).Length==6&&Area(inside)==4,"interior quads keep their original vertices and triangles");
            var outside=Batch(core);Call(outside,"Quad",new Vector3(-4,0,1),new Vector3(-4,0,3),new Vector3(-2,0,3),new Vector3(-2,0,1));Check(Vertices(outside).Length==0,"exterior halo geometry cannot leak into a second core");
            var slope=Batch(core);Call(slope,"Quad",new Vector3(-4,-1,10),new Vector3(-4,-1,12),new Vector3(4,1,12),new Vector3(4,1,10));
            Check(Vertices(slope).All(v=>v.x>=0&&v.x<=64&&v.z>=0&&v.z<=64&&Mathf.Abs(v.y-v.x*.25f)<.00001f),"clipping preserves linear fitted road elevations");
            Check(Math.Abs(Area(slope)-8*Math.Sqrt(1+.25*.25))<.0001,"no surface area is invented at the source border");
            var left=Batch(core);var right=Batch(Core(64,0));var a=new Vector3(60,0,10);var b=new Vector3(60,0,12);var c=new Vector3(68,0,12);var d=new Vector3(68,0,10);
            Call(left,"Quad",a,b,c,d);Call(right,"Quad",a,b,c,d);Check(Math.Abs(Area(left)+Area(right)-16)<1e-6,"adjacent source owners partition the original ribbon without positive-area overlap");
            Check(Vertices(left).All(v=>v.x<=64)&&Vertices(right).All(v=>v.x>=64),"source partitions meet at the exact common plane");
            foreach(string category in new[]{"road","path","rail","water"})
            {
                var f=new World.Feature{id=category,sourceId=category,category=category,width=6.8f,travelWidth=6.8f,points=new[]{new Vector3(-1,10,4),new Vector3(-1,10,60)}};
                var ranges=Ranges(core,f,f.points[0],f.points[1]);Check(Count(ranges)==1&&At(ranges,0)==new Vector2(0,1),category+" borrows the fitted exterior centre line");
                var index=new Dictionary<Vector2Int,HashSet<World.Feature>>();typeof(World).GetMethod("IndexSourceEdgeRibbon",Hidden).Invoke(null,new object[]{core,f,f.points[0],f.points[1],index});Check(index.TryGetValue(Vector2Int.zero,out var set)&&set.Contains(f),category+" footprint reaches the owned edge chunk");
            }
            var road=new World.Feature{width=6.8f,travelWidth=6.8f,category="road"};var crossing=Ranges(core,road,new Vector3(-10,0,32),new Vector3(80,0,32));var ordered=Enumerable.Range(0,Count(crossing)).Select(i=>At(crossing,i)).OrderBy(v=>v.x).ToArray();
            Check(ordered.Length==3&&ordered.Zip(ordered.Skip(1),(l,r)=>l.y<=r.x+.000001f).All(v=>v),"owned and borrowed longitudinal intervals never double-emit the same centre-line piece");
            var negative=Batch(Core(-64,-64));Call(negative,"Quad",new Vector3(-2,0,-60),new Vector3(-2,0,-4),new Vector3(2,0,-4),new Vector3(2,0,-60));Check(Vertices(negative).All(v=>v.x>=-64&&v.x<=0&&v.z>=-64&&v.z<=0)&&Math.Abs(Area(negative)-112)<1e-6,"negative projected coordinates preserve exact ownership");
            // A halo interval extends longitudinally past its destination chunk
            // to retain complete joins. Its final footprint must not overlap the
            // borrowed portion emitted by the next chunk of the same core.
            var largeCore=new World.Map{coreMinX=0,coreMaxX=128,coreMinZ=0,coreMaxZ=128};
            var lower=Batch(largeCore);var upper=Batch(largeCore);Call(lower,"ClipToSourceChunk",largeCore,0f,0f);Call(upper,"ClipToSourceChunk",largeCore,0f,64f);
            foreach(var part in new[]{lower,upper})Call(part,"Quad",new Vector3(-4,0,0),new Vector3(-4,0,128),new Vector3(2,0,128),new Vector3(2,0,0));
            Check(Math.Abs(Area(lower)+Area(upper)-256)<.00002&&Vertices(lower).All(v=>v.z<=64)&&Vertices(upper).All(v=>v.z>=64),"borrowed ribbons partition adjacent chunks without duplicate faces (two float area ULPs)");
            var top=Batch(core);var method=typeof(World).GetMethod("RoadTopTriangle",Hidden);
            method.Invoke(null,new object[]{top,new Vector3(-8,1,4),new Vector3(-8,1,8),new Vector3(-4,1,4),true,true});
            Check(((System.Collections.ICollection)top.GetType().GetProperty("TerrainRoads",Hidden).GetValue(top)).Count==0,"discarded source triangles never leave invalid terrain-clearance references");
            method.Invoke(null,new object[]{top,new Vector3(-2,1,4),new Vector3(-2,1,8),new Vector3(4,1,4),true,true});
            var references=(IReadOnlyList<World.PavementTriangleRef>)top.GetType().GetProperty("TerrainRoads",Hidden).GetValue(top);
            Check(references.Count>0&&references.All(v=>v.vertex>=0&&v.vertex+2<Vertices(top).Length)&&references.Count*3==Vertices(top).Length,"all split road-top triangles have valid terrain-clearance references");
            var terrainPoints=new[]{new Vector3(0,2,4),new Vector3(0,2,8),new Vector3(4,2,4)};
            var cleared=World.ConstrainTerrainPavement(terrainPoints,new[]{0,1,2},Vertices(top),references);
            Check(cleared.roadTriangles==references.Count&&cleared.changedTriangles>0,"clipped road tops still recess terrain before publication");
            // Publish the actual clipped triangles into a physical collider.
            var colliderRoot=new GameObject("Borrowed road edge physical proof");var mesh=new Mesh();
            try
            {
                var edge=Batch(core);Call(edge,"Quad",new Vector3(-4.4f,10,4),new Vector3(-4.4f,10,60),new Vector3(2.4f,10,60),new Vector3(2.4f,10,4));
                mesh.vertices=Vertices(edge);mesh.triangles=Indices(edge);mesh.RecalculateBounds();colliderRoot.AddComponent<MeshCollider>().sharedMesh=mesh;Physics.SyncTransforms();
                Check(Physics.Raycast(new Vector3(1.5f,12,32),Vector3.down,out var hit,4)&&hit.collider.gameObject==colliderRoot&&Mathf.Abs(hit.point.y-10)<.0001f,"the previously missing inside-core road edge has real physical support");
                Check(!colliderRoot.GetComponent<MeshCollider>().Raycast(new Ray(new Vector3(-1,12,32),Vector3.down),out _,4),"borrowed collider cannot occupy the neighbouring core");
            }
            finally{UnityEngine.Object.DestroyImmediate(colliderRoot);UnityEngine.Object.DestroyImmediate(mesh);}
            Debug.Log("SOURCE EDGE RIBBON PASS "+checks+"; source partitions, widths, elevations and actual collider support");
        }
    }
}
