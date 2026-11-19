using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
    public static class TerrainPavementClearanceVerification
    {
        sealed class Fixture
        {
            public readonly List<Vector3> terrain=new List<Vector3>(),roads=new List<Vector3>();public readonly List<int> indices=new List<int>();
            public readonly List<NewbridgeWorld.PavementTriangleRef> tops=new List<NewbridgeWorld.PavementTriangleRef>();
            public void GroundQuad(float x0,float z0,float x1,float z1,Func<float,float,float> height)
            {int n=terrain.Count;terrain.Add(new Vector3(x0,height(x0,z0),z0));terrain.Add(new Vector3(x0,height(x0,z1),z1));terrain.Add(new Vector3(x1,height(x1,z1),z1));terrain.Add(new Vector3(x1,height(x1,z0),z0));indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
            public void RoadTriangle(Vector3 a,Vector3 b,Vector3 c,bool ground=true)
            {tops.Add(new NewbridgeWorld.PavementTriangleRef(roads.Count,ground));roads.Add(a);roads.Add(b);roads.Add(c);}
            public void RoadQuad(float x0,float z0,float x1,float z1,Func<float,float,float> height,bool ground=true)
            {var a=new Vector3(x0,height(x0,z0),z0);var b=new Vector3(x0,height(x0,z1),z1);var c=new Vector3(x1,height(x1,z1),z1);var d=new Vector3(x1,height(x1,z0),z0);RoadTriangle(a,b,c,ground);RoadTriangle(a,c,d,ground);}
            public NewbridgeWorld.TerrainPavementResult Solve()=>NewbridgeWorld.ConstrainTerrainPavement(terrain,indices,roads,tops);
            public List<Vector3> Combined(NewbridgeWorld.TerrainPavementResult result){var all=new List<Vector3>(terrain);all.AddRange(result.addedVertices);return all;}
        }
        static float Sample(IReadOnlyList<Vector3> vertices,IReadOnlyList<int> indices,float x,float z)
        {
            float best=float.NegativeInfinity;
            for(int i=0;i+2<indices.Count;i+=3)
            {
                var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(denominator)<.0000001f)continue;
                float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/denominator,v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/denominator;
                if(u<-.00001f||v<-.00001f||u+v>1.00001f)continue;best=Mathf.Max(best,a.y*u+b.y*v+c.y*(1-u-v));
            }
            return best;
        }
        static double Area(IReadOnlyList<Vector3> vertices,IReadOnlyList<int> indices)
        {double area=0;for(int i=0;i+2<indices.Count;i+=3){var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];area+=Math.Abs((double)(b.x-a.x)*(c.z-a.z)-(double)(b.z-a.z)*(c.x-a.x))*.5;}return area;}
        public static void Batch(){try{Run();UnityEditor.EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}}
        public static void Run()
        {
            int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
            // Actual emitted Kessock west-landing terrain/asphalt, not merely
            // source profiles or a nearest-centreline predicate.
            var landing=new Fixture();landing.terrain.AddRange(new[]{new Vector3(-728,28.439135f,2520),new Vector3(-726,29.181807f,2522),new Vector3(-726,28.439135f,2520)});landing.indices.AddRange(new[]{0,1,2});
            landing.RoadTriangle(new Vector3(-729.0644f,28.44268f,2514.2522f),new Vector3(-728.6479f,28.609585f,2521.3276f),new Vector3(-726.76556f,28.44266f,2520.6516f),false);
            // The photographed edge coordinate lies on a millimetre-rounded
            // ribbon edge. Use a strict interior barycentric point of the SAME
            // actual triangle so the assertion never depends on edge tolerance.
            var probe=landing.roads[0]*.015f+landing.roads[1]*.197f+landing.roads[2]*.788f;float probeX=probe.x,probeZ=probe.z;
            float deck=Sample(landing.roads,new[]{0,1,2},probeX,probeZ),before=Sample(landing.terrain,landing.indices,probeX,probeZ);
            Check(!float.IsInfinity(deck)&&!float.IsInfinity(before),"Actual landing probe is strictly inside both emitted triangles");
            Check(before>deck+.20f,"Actual emitted landing reproduces grass above the fitted bridge ribbon");var repaired=landing.Solve();var vertices=landing.Combined(repaired);
            Check(Sample(vertices,repaired.indices,probeX,probeZ)<=deck-.118f,"Exact emitted asphalt footprint removes the landing intrusion");
            Check(Mathf.Abs(Sample(vertices,repaired.indices,-726,2522)-29.181807f)<.001f,"Adjacent higher approach/verge corner remains unchanged outside asphalt");
            Check(Math.Abs(Area(vertices,repaired.indices)-Area(landing.terrain,landing.indices))<.001,"Landing split covers original terrain exactly without projected holes/overlap");
            for(int i=0;i<repaired.indices.Count;i+=3)Check(Vector3.Cross(vertices[repaired.indices[i+1]]-vertices[repaired.indices[i]],vertices[repaired.indices[i+2]]-vertices[repaired.indices[i]]).y>0,"Every emitted replacement retains upward terrain winding");
            var island=new Fixture();island.GroundQuad(-6,-6,6,6,(x,z)=>2);island.RoadQuad(-5,-5,-1,5,(x,z)=>1);island.RoadQuad(1,-5,5,5,(x,z)=>1);island.RoadQuad(-1,-5,1,-1,(x,z)=>1);island.RoadQuad(-1,1,1,5,(x,z)=>1);
            var ir=island.Solve();var iv=island.Combined(ir);Check(Mathf.Abs(Sample(iv,ir.indices,0,0)-2)<.0001f,"Real central traffic island remains high where there is no asphalt polygon");Check(Mathf.Abs(Sample(iv,ir.indices,5.5f,0)-2)<.0001f,"Unpaved verge retains its original height");Check(Sample(iv,ir.indices,2,0)<=.881f,"Only the actual surrounding road footprint is recessed");Check(Math.Abs(Area(iv,ir.indices)-144)<.001,"Island and surrounding road retain complete terrain coverage");
            var lower=new Fixture();lower.GroundQuad(-2,-2,2,2,(x,z)=>1);lower.RoadQuad(-2,-2,2,2,(x,z)=>8,false);lower.RoadQuad(-2,-2,2,2,(x,z)=>0,true);var lr=lower.Solve();Check(Sample(lower.Combined(lr),lr.indices,0,0)<-.118f,"Upper bridge does not shelter stray grass obstructing an actual lower road");
            var protectedGround=new Fixture();protectedGround.GroundQuad(-2,-2,2,2,(x,z)=>7.8f);protectedGround.RoadQuad(-2,-2,2,2,(x,z)=>0,false);protectedGround.RoadQuad(-2,-2,2,2,(x,z)=>8,true);var pr=protectedGround.Solve();Check(pr.changedTriangles==0&&Mathf.Abs(Sample(protectedGround.Combined(pr),pr.indices,0,0)-7.8f)<.001f,"Lower crossing does not excavate soil supporting an actual higher ground road");
            var high=new Fixture();high.GroundQuad(-2,-2,2,2,(x,z)=>100);high.RoadQuad(-2,-2,2,2,(x,z)=>0,false);var hr=high.Solve();Check(Sample(high.Combined(hr),hr.indices,0,0)<-.118f,"High erroneous grass is not preserved by an arbitrary altitude cutoff");
            var affine=new Fixture();affine.GroundQuad(-2,-2,2,2,(x,z)=>2+.1f*x);affine.RoadQuad(-1,-1,1,1,(x,z)=>1+.2f*x+.1f*z,false);var ar=affine.Solve();var av=affine.Combined(ar);
            for(int z=-4;z<=4;z++)for(int x=-4;x<=4;x++){float px=x*.2f,pz=z*.2f;Check(Mathf.Abs(Sample(av,ar.indices,px,pz)-(1+.2f*px+.1f*pz-.12f))<.001f,"Changing asphalt plane is respected throughout its emitted triangles");}
            Check(Mathf.Abs(Sample(av,ar.indices,1.5f,0)-2.15f)<.001f,"Affine clipping never alters adjacent non-road terrain");
            var empty=new Fixture();empty.GroundQuad(-2,-2,2,2,(x,z)=>12);var er=empty.Solve();Check(er.changedTriangles==0&&er.addedVertices.Count==0,"No paved footprint leaves terrain topology untouched");
            var safe=new Fixture();safe.GroundQuad(-2,-2,2,2,(x,z)=>-.06f);safe.RoadQuad(-2,-2,2,2,(x,z)=>0,false);var sr=safe.Solve();
            Check(sr.changedTriangles==0&&sr.addedVertices.Count==0,"Already-safe soil6cm below asphalt keeps its original topology and height");
            var touching=new Fixture();touching.GroundQuad(-2,-2,2,2,(x,z)=>-.01f);touching.RoadQuad(-2,-2,2,2,(x,z)=>0,false);var tr=touching.Solve();Check(Sample(touching.Combined(tr),tr.indices,0,0)<-.118f,"Only terrain entering the top2cm safety band receives the12cm recess");
            var root=new GameObject("Tunnel/bridge triangle registration fixture");
            try
            {
                var world=root.AddComponent<NewbridgeWorld>();const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;var type=typeof(NewbridgeWorld);var batchType=type.GetNestedType("Batch",BindingFlags.NonPublic);
                object Render(bool tunnel,bool bridge)
                {
                    var batch=Activator.CreateInstance(batchType,flags|BindingFlags.Public,null,new object[]{"Proof asphalt",null,false},null);var feature=new NewbridgeWorld.Feature{id="proof",sourceId="proof",category="road",width=4,tunnel=tunnel,bridge=bridge,points=new[]{new Vector3(-2,0,0),new Vector3(2,0,0)}};
                    var rows=(IEnumerable<bool>)type.GetMethod("RoadRibbonRows",flags).Invoke(world,new object[]{batch,feature,feature.points[0],feature.points[1],Vector3.forward,Vector3.forward,2f});foreach(bool row in rows){}return batch;
                }
                var tunnelBatch=Render(true,false);var tunnelTops=(IReadOnlyList<NewbridgeWorld.PavementTriangleRef>)batchType.GetProperty("TerrainRoads").GetValue(tunnelBatch);
                Check(tunnelTops.Count==0,"Real tunnel road emission never registers a roof-cutting footprint");
                var tunnelResult=NewbridgeWorld.ConstrainTerrainPavement(empty.terrain,empty.indices,(IReadOnlyList<Vector3>)batchType.GetProperty("Vertices").GetValue(tunnelBatch),tunnelTops);Check(tunnelResult.changedTriangles==0,"Actual emitted tunnel floor preserves the complete terrain roof");
                var bridgeTops=(IReadOnlyList<NewbridgeWorld.PavementTriangleRef>)batchType.GetProperty("TerrainRoads").GetValue(Render(false,true));Check(bridgeTops.Count==2&&!bridgeTops[0].ground,"Bridge asphalt participates but never claims soil above a lower road");
                var groundTops=(IReadOnlyList<NewbridgeWorld.PavementTriangleRef>)batchType.GetProperty("TerrainRoads").GetValue(Render(false,false));Check(groundTops.Count==2&&groundTops[0].ground,"Ground-road asphalt carries explicit upper-level support authority");
                var sourceBatch=Render(false,false);var appended=Activator.CreateInstance(batchType,flags|BindingFlags.Public,null,new object[]{"Appended asphalt",null,false},null);
                batchType.GetMethod("Append").Invoke(appended,new[]{sourceBatch});batchType.GetMethod("Append").Invoke(appended,new[]{sourceBatch});
                var appendedTops=(IReadOnlyList<NewbridgeWorld.PavementTriangleRef>)batchType.GetProperty("TerrainRoads").GetValue(appended);int sourceVertices=(int)batchType.GetProperty("VertexCount").GetValue(sourceBatch);
                Check(appendedTops.Count==4&&appendedTops[2].vertex==appendedTops[0].vertex+sourceVertices&&appendedTops[3].ground,"Batch append preserves and offsets exact pavement-top metadata");
                var topMethod=type.GetMethod("RoadTopTriangle",flags);Check((bool)topMethod.GetParameters()[4].DefaultValue&&(bool)topMethod.GetParameters()[5].DefaultValue,"Junction asphalt default emission participates in terrain clearance and ground authority");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            // A representative64m fine chunk: ordinary terrain stays unchanged,
            // with just one raised edge vertex reproducing a shoreline-bank wedge.
            var dense=new Fixture();for(int z=0;z<64;z+=2)for(int x=0;x<64;x+=2)dense.GroundQuad(x,z,x+2,z+2,(px,pz)=>Mathf.Abs(px-32)<.01f&&Mathf.Abs(pz-26)<.01f?11:9.8f);
            for(int x=0;x<64;x+=2)dense.RoadQuad(x,20,x+2,25.4f,(px,pz)=>10,false);
            dense.Solve();long beforeBytes=GC.GetAllocatedBytesForCurrentThread(),events=0;NewbridgeWorld.TerrainPavementResult dr;
            using(var allocations=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",1,ProfilerRecorderOptions.CollectOnlyOnCurrentThread|ProfilerRecorderOptions.SumAllSamplesInFrame))
            {dr=dense.Solve();allocations.Stop();for(int i=0;i<allocations.Count;i++)events+=allocations.GetSample(i).Count;}
            long bytes=GC.GetAllocatedBytesForCurrentThread()-beforeBytes;if(bytes==0)bytes=-1;
            Check(dr.changedTriangles>0&&dr.changedTriangles<20,"Only the intrusive fine triangles are replaced in a real-sized chunk");Check(dr.addedVertices.Count<300,"Fine-chunk polygon output remains bounded around the intrusion");
            Debug.Log("TERRAIN PAVEMENT CLEARANCE PASS "+checks+" landingBefore="+before+" deck="+deck+" landingAfter="+Sample(vertices,repaired.indices,probeX,probeZ)+" denseWorkerMs="+dr.milliseconds+" changed="+dr.changedTriangles+" inputVertices="+dense.terrain.Count+" addedVertices="+dr.addedVertices.Count+" inputTriangles="+dense.indices.Count/3+" outputTriangles="+dr.indices.Count/3+" allocationEvents="+events+" allocatedBytes="+bytes);
        }
    }
}
