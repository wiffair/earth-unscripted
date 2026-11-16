using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EarthUnscripted.NewbridgePrototype;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    // Probe emitted production triangles against their real terrain collider.
    // Include stacked paths so a valid bridge cannot stand in for ground pavement.
    public static class GroundPathSupportVerification
    {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Type World = typeof(NewbridgeWorld);
        static readonly Type Batch = World.GetNestedType("Batch", BindingFlags.NonPublic);
        static object Call(NewbridgeWorld world, string method, params object[] args) => World.GetMethod(method, Hidden).Invoke(world,args);
        static object NewBatch(string name) => Activator.CreateInstance(Batch, new object[]{name,null,true});
        static MeshCollider Publish(GameObject root, object batch, List<Mesh> meshes)
        {
            var vertices=((IEnumerable<Vector3>)Batch.GetProperty("Vertices").GetValue(batch)).ToArray();
            var indices=((IEnumerable<int>)Batch.GetProperty("Indices").GetValue(batch)).ToArray();
            if(indices.Length==0)throw new InvalidDataException("Empty path-support fixture");
            var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateBounds();meshes.Add(mesh);
            var child=new GameObject("Path support probe");child.transform.SetParent(root.transform,false);var collider=child.AddComponent<MeshCollider>();collider.sharedMesh=mesh;return collider;
        }
        public static void Run()
        {
            int samples=0;float maximum=0;
            foreach(bool kerb in new[]{false,true})
            {
                var root=new GameObject("Ground path support fixture");var meshes=new List<Mesh>();
                try
                {
                    var world=root.AddComponent<NewbridgeWorld>();float x=kerb?4.4f:1.2f;
                    var path=new NewbridgeWorld.Feature{id="path",sourceId="path",category="path",kind="footway",level="0",width=1.6f,points=new[]{new Vector3(x,20,-9),new Vector3(x+.6f,20,9)}};
                    var bridge=new NewbridgeWorld.Feature{id="bridge",sourceId="bridge",category="path",kind="cycleway",level="0",layer=1,bridge=true,width=2.8f,points=new[]{new Vector3(-10,28.7f,0),new Vector3(10,28.7f,0)}};
                    var road=new NewbridgeWorld.Feature{id="road",sourceId="road",category="road",kind="residential",level="0",width=6,points=new[]{new Vector3(0,20,-15),new Vector3(0,20,15)}};
                    var map=new NewbridgeWorld.Map{minX=-16,minZ=-16,maxX=16,maxZ=16,gridStep=2,nx=17,nz=17,heights=new float[289],features=kerb?new[]{path,bridge,road}:new[]{path,bridge},buildings=Array.Empty<NewbridgeWorld.Building>()};
                    for(int iz=0;iz<17;iz++)for(int ix=0;ix<17;ix++)map.heights[iz*17+ix]=20+(ix%3)*1.7f+(iz%4)*.9f;
                    World.GetField("workingMap",Hidden).SetValue(world,map);Call(world,"IndexMap",map);
                    var terrain=NewBatch("Terrain");
                    Vector3 Point(float px,float pz)=>new Vector3(px,(float)Call(world,"Ground",px,pz),pz);
                    for(float z=-16;z<16;z+=2)for(float tx=-16;tx<16;tx+=2)Call(world,"TerrainCell",terrain,Point(tx,z),Point(tx,z+2),Point(tx+2,z+2),Point(tx+2,z));
                    var groundPaths=NewBatch("Footpaths");var raisedPaths=NewBatch("Footbridges / raised paths");
                    foreach(var feature in new[]{path,bridge})
                    {var a=feature.points[0];var b=feature.points[1];var side=Vector3.Cross(Vector3.up,(b-a).normalized).normalized;Call(world,"FootpathStrip",feature.bridge?raisedPaths:groundPaths,feature,a,b,side,side,feature.width*.5f);}
                    var terrainCollider=Publish(root,terrain,meshes);var pathCollider=Publish(root,groundPaths,meshes);var bridgeCollider=Publish(root,raisedPaths,meshes);Physics.SyncTransforms();
                    var v=pathCollider.sharedMesh.vertices;var indices=pathCollider.sharedMesh.triangles;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var a=v[indices[i]];var b=v[indices[i+1]];var c=v[indices[i+2]];
                        if(Vector3.Cross(b-a,c-a).y<=.00001f)continue;
                        var at=(a+b+c)/3;var ray=new Ray(at+Vector3.up*20,Vector3.down);
                        if(!terrainCollider.Raycast(ray,out var hit,40))throw new InvalidDataException("Ground path has no supporting terrain");
                        float gap=at.y-hit.point.y;maximum=Mathf.Max(maximum,gap);samples++;
                        if(gap<-.002f||gap>.302f)throw new InvalidDataException("Emitted ground path/kerb support gap "+gap+" at "+at);
                    }
                    var crossing=new Ray(new Vector3(x,40,0),Vector3.down);
                    if(!bridgeCollider.Raycast(crossing,out var upper,50)||!pathCollider.Raycast(crossing,out var lower,50)||upper.point.y-lower.point.y<6)
                        throw new InvalidDataException("Independent bridge clearance lost at stacked paths");
                    if(kerb&&maximum<.29f)throw new InvalidDataException("Raised supported kerb was not exercised");
                }
                finally{UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
            }
            if(samples<100)throw new InvalidDataException("Insufficient actual path-triangle samples");
            Debug.Log("GROUND_PATH_SUPPORT_PASS samples="+samples+" maximumGap="+maximum);
        }
    }
}
