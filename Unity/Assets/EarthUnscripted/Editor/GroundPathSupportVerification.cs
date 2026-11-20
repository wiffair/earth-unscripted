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
        static void SlopingTunnelCover()
        {
            var root=new GameObject("Sloping tunnel nearest segment support fixture");var meshes=new List<Mesh>();
            try
            {
                var world=root.AddComponent<NewbridgeWorld>();world.enabled=false;
                // A descending entrance runs obliquely beside the upper road.
                // Farther uphill segments overlap the 2.9m terrain apron, but
                // only the closest source segment determines its local roof.
                var lower=new NewbridgeWorld.Feature{id="slope",sourceId="slope",category="path",kind="footway",layer=-1,tunnel=true,width=1.8f,tunnelClearance=2.7f,
                    points=new[]{new Vector3(18.1878f,12.30152f,14.2902f),new Vector3(16.3789f,11.944983f,13.4372f),new Vector3(14.5699f,11.556156f,12.5842f),new Vector3(12.7609f,11.247329f,11.7312f),new Vector3(10.952f,10.895932f,10.8782f),new Vector3(9.143f,10.544534f,10.0251f)}};
                var upper=new NewbridgeWorld.Feature{id="surface",sourceId="surface",category="road",kind="secondary",width=6,points=new[]{new Vector3(1,14.55f,16),new Vector3(31,14.55f,16)}};
                var map=new NewbridgeWorld.Map{minX=0,minZ=0,maxX=32,maxZ=32,gridStep=32,nx=2,nz=2,heights=new[]{14.878f,14.878f,14.878f,14.878f},features=new[]{upper,lower},buildings=Array.Empty<NewbridgeWorld.Building>()};
                World.GetField("workingMap",Hidden).SetValue(world,map);Call(world,"IndexMap",map);
                var terrain=NewBatch("Terrain");Vector3 Point(float x,float z)=>new Vector3(x,(float)Call(world,"Ground",x,z),z);
                for(float z=12;z<20;z+=2)for(float x=6;x<16;x+=2)Call(world,"TerrainCell",terrain,Point(x,z),Point(x,z+2),Point(x+2,z+2),Point(x+2,z));
                var collider=Publish(root,terrain,meshes);Physics.SyncTransforms();
                foreach(var p in new[]{new Vector3(10.47f,14.55f,14.59f),new Vector3(11.72f,14.55f,13.02f),new Vector3(10.54f,14.55f,13.7f),new Vector3(11.05f,14.55f,12.05f)})
                    if(!collider.Raycast(new Ray(p+Vector3.up*1.5f,Vector3.down),out var hit,4)||p.y-hit.point.y>.25f||p.y-hit.point.y<.1f)
                        throw new InvalidDataException("Farther uphill tunnel segment excavated supported road soil at "+p);
                Debug.Log("SLOPING_TUNNEL_COVER_PASS 4 actual terrain collider samples");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
        }
        static void ShallowTunnelCover()
        {
            int cases=0;
            foreach(var spec in new[]{(kind:"path",floor:14.5f,clearance:2.7f),(kind:"road",floor:12.1f,clearance:5.1f),(kind:"rail",floor:11.6f,clearance:5.6f)})
            {
                var root=new GameObject("Actual shallow tunnel roof and portal fixture");var meshes=new List<Mesh>();
                try
                {
                    var world=root.AddComponent<NewbridgeWorld>();world.enabled=false;
                    var upper=new NewbridgeWorld.Feature{id="upper",sourceId="upper",category="road",kind="secondary",level="0",width=8,points=new[]{new Vector3(1,18,9),new Vector3(31,18,9)}};
                    var lower=new NewbridgeWorld.Feature{id="lower",sourceId="lower",category=spec.kind,kind=spec.kind=="path"?"footway":spec.kind=="road"?"secondary":"rail",level="0",layer=-1,tunnel=true,width=1.8f,tunnelClearance=spec.clearance,points=new[]{new Vector3(9,spec.floor,1),new Vector3(9,spec.floor,31)}};
                    NewbridgeWorld.Map Source(float height,params NewbridgeWorld.Feature[] features)=>new NewbridgeWorld.Map{minX=0,minZ=0,maxX=32,maxZ=32,coreMinX=0,coreMinZ=0,coreMaxX=32,coreMaxZ=32,gridStep=32,nx=2,nz=2,heights=new[]{height,height,height,height},features=features,buildings=Array.Empty<NewbridgeWorld.Building>()};
                    var map=Source(18.3f,upper,lower);World.GetField("workingMap",Hidden).SetValue(world,map);Call(world,"IndexMap",map);
                    float Floor()=>(float)Call(world,"PortalCellFloor",8f,8f);
                    if(!float.IsPositiveInfinity(Floor()))throw new InvalidDataException("A valid shallow "+spec.kind+" roof must retain overlying road soil");
                    Vector3 Point(float x,float z)=>new Vector3(x,(float)Call(world,"Ground",x,z),z);
                    var cover=NewBatch("Terrain");Call(world,"TerrainCell",cover,Point(8,8),Point(8,10),Point(10,10),Point(10,8));
                    var collider=Publish(root,cover,meshes);Physics.SyncTransforms();
                    if(!collider.Raycast(new Ray(new Vector3(9,19.5f,9),Vector3.down),out var hit,3)||Mathf.Abs(hit.point.y-17.8f)>.002f)
                        throw new InvalidDataException("Published terrain no longer supports the road over a shallow "+spec.kind+" tunnel");
                    if(collider.Raycast(new Ray(new Vector3(9,spec.floor+spec.clearance-.01f,9),Vector3.down),out _,spec.clearance-.02f))
                        throw new InvalidDataException("Preserved roof cover enters required "+spec.kind+" headroom");
                    var portal=Source(spec.floor+1,lower);World.GetField("workingMap",Hidden).SetValue(world,portal);Call(world,"IndexMap",portal);
                    ((System.Collections.IDictionary)World.GetField("portalCellFloors",Hidden).GetValue(world)).Clear();
                    float floor=Floor();if(float.IsPositiveInfinity(floor)||floor>=spec.floor)throw new InvalidDataException("A genuinely low "+spec.kind+" portal still needs excavation");
                    var opening=NewBatch("Portal terrain");Call(world,"TerrainCell",opening,Point(8,8),Point(8,10),Point(10,10),Point(10,8));
                    var opened=Publish(root,opening,meshes);Physics.SyncTransforms();
                    if(!opened.Raycast(new Ray(new Vector3(9,spec.floor+2,9),Vector3.down),out var bed,4)||bed.point.y>spec.floor-.1f)
                        throw new InvalidDataException("Actual "+spec.kind+" entrance terrain blocks its opening");
                    cases++;
                }
                finally{UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
            }
            Debug.Log("SHALLOW_TUNNEL_COVER_PASS "+cases+"; actual road soil/headroom and excavated portal colliders");
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
            ShallowTunnelCover();SlopingTunnelCover();
            Debug.Log("GROUND_PATH_SUPPORT_PASS samples="+samples+" maximumGap="+maximum);
        }
    }
}
