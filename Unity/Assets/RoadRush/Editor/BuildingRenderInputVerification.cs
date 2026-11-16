using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class BuildingRenderInputVerification
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception("BUILDING INPUT FAIL: "+text);checks++;Debug.Log("BUILDING INPUT PASS: "+text);}
        static GeoBuilding Box(string id,float x,float z,float height=6)=>new GeoBuilding{id=id,kind="building",roof="gabled",height=height,points=new[]{new Vector3(x,2,z),new Vector3(x+8,2,z),new Vector3(x+8,2,z+8),new Vector3(x,2,z+8),new Vector3(x,2,z)}};
        static GeoRegion Fixture()
        {
            var open=Box("open",40,18);open.points=open.points.Take(3).ToArray();var near=Box("near",60,18);near.points[4].x+=.0005f;var zero=Box("zero",80,18,0);var large=Box("large-convex",0,0,500);large.points=Enumerable.Range(0,4096).Select(i=>new Vector3((float)Math.Cos(i*Math.PI/2048)*25,2,65+(float)Math.Sin(i*Math.PI/2048)*25)).ToArray();
            var concave=Box(null,0,0);concave.points=Enumerable.Range(0,260).Select(i=>{double a=i*Math.PI/130;float radius=i%2==0?20:18;return new Vector3(-70+(float)Math.Cos(a)*radius,2,65+(float)Math.Sin(a)*radius);}).Concat(new[]{new Vector3(-50,2,65)}).ToArray();
            var first=Box("same",-70,18);var second=Box("same",-50,18);var duplicate=JsonUtility.FromJson<GeoBuilding>(JsonUtility.ToJson(first));var triangle=Box("closed-triangle",100,18);triangle.points=new[]{triangle.points[0],triangle.points[1],triangle.points[2],triangle.points[0]};
            var r=new GeoRegion{id="download-render-input-fixture",name="Input fixture",preparedWidths=true,drivingSide=-1,latitude=51,longitude=-3,countryCode="GB",minX=-160,minZ=-160,width=320,depth=320,nx=2,nz=2,heights=new[]{2f,2f,2f,2f},landNx=2,landNz=2,landCover=new int[4],roads=new[]{new GeoRoad{id="road",start="a",end="b",kind="residential",width=8,limit=30,points=new[]{new Vector3(-150,2,0),new Vector3(150,2,0)}}},waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=new[]{open,near,zero,large,concave,first,second,duplicate,triangle},route=new[]{new Vector3(-100,2,0),new Vector3(100,2,0)}};r.Prepare();return r;
        }
        static string Keys(BuildingRenderInput.Plan p)=>string.Join("|",p.Renderable.Select(e=>e.Geometry.id+":"+e.ContentKey));
        public static void Run()
        {
            GeoWorld world=null;
            try
            {
                var region=Fixture();string before=WorldSnapshotCodec.CanonicalJson(region);var decoded=WorldSnapshotCodec.Decode(before,WorldSnapshotCodec.NetworkId(region),WorldSnapshotCodec.Epoch(region));Check(decoded.buildings.Length==9,"existing network codec accepts the open zero-height large null-ID and duplicate-ID fixture without contract changes");
                var plan=BuildingRenderInput.Create(region);Check(plan.SourceCount==9&&plan.Renderable.Length==7&&plan.SkippedCount==1&&plan.CoalescedCount==1,"only zero-height visuals are skipped and one exact duplicate descriptor coalesces");Check(plan.Renderable.Select(e=>e.Geometry.id).Distinct().Count()==7&&plan.Renderable.All(e=>!string.IsNullOrEmpty(e.Geometry.id)),"every renderable cache key is nonempty and unique");
                var open=plan.Entries.Single(e=>e.OriginalSource.id=="open");Check(open.Geometry.points.Length==4&&open.Geometry.points[0].Equals(open.Geometry.points[3])&&(open.Diagnostics&BuildingRenderInput.Flags.ClosedOpenRing)!=0,"open three-corner triangle closes without dropping its final real corner");
                var near=plan.Entries.Single(e=>e.OriginalSource.id=="near");Check(near.Geometry.points[0].Equals(near.Geometry.points.Last())&&(near.Diagnostics&BuildingRenderInput.Flags.SnappedClosingPoint)!=0,"near-closing endpoint snaps locally to an exact first point");
                Check(plan.Entries.Single(e=>e.OriginalSource.id=="zero").Skipped&&plan.Entries.Count(e=>(e.Diagnostics&BuildingRenderInput.Flags.ConflictingIdentity)!=0)==2,"zero height and conflicting source identities have explicit bounded diagnostics");Check(plan.Renderable.All(e=>!ReferenceEquals(e.Geometry,e.OriginalSource))&&plan.Renderable.All(e=>plan.TryGet(e.Geometry,out var found)&&found==e&&e.Owns(e.OriginalSource)),"normalized copies retain original reference lineage and support either reference lookup");
                string stable=Keys(plan);Array.Reverse(region.buildings);Check(Keys(BuildingRenderInput.Create(region))==stable&&Keys(BuildingRenderInput.Create(decoded))==stable,"content identities and exact descriptor coalescing survive array reorder and actual canonical network round-trip");Array.Reverse(region.buildings);Check(WorldSnapshotCodec.CanonicalJson(region)==before,"normalization and identity resolution leave source canonical bytes unchanged");
                var big=plan.Renderable.Single(e=>e.OriginalSource.id=="large-convex").Geometry;Check(big.height==500&&big.points.Length==4097,"accepted4096-point open geometry and500m height stay intact in the local closed copy");int calls=0;var site=BuildingSitePlan.Create(region,big,(x,z)=>{calls++;return 2;});Check(site.SampleCount==64&&calls==64&&site.ComplexFootprint,"large site keeps the64-query bound and explicit complex-site status");
                var roof=BuildingRoofGeometry.Create(big.points,502,"gabled");Check(!roof.Omitted&&!roof.Pitched&&roof.Triangles.Length>0&&roof.Vertices.Length==4096,"proven simple convex4096-corner outline uses bounded linear flat fan");bool up=true;double area=0;for(int i=0;i<roof.Triangles.Length;i+=3){var a=roof.Vertices[roof.Triangles[i]];var b=roof.Vertices[roof.Triangles[i+1]];var c=roof.Vertices[roof.Triangles[i+2]];float y=Vector3.Cross(b-a,c-a).y;up&=y>0;area+=y*.5;}Check(up&&Math.Abs(area-Math.PI*25*25)<.1,"large flat fan has upward nondegenerate faces and preserves the convex outline area");
                var complex=plan.Renderable.Single(e=>e.OriginalSource.id==null).Geometry;var omitted=BuildingRoofGeometry.Create(complex.points,8,"gabled");Check(omitted.Omitted&&omitted.Triangles.Length==0&&omitted.Vertices.Length==0&&!string.IsNullOrEmpty(omitted.FallbackReason),"large concave roof is explicitly omitted rather than bridged by a fan or bounding box");
                // Exercise production GeoWorld against the ORIGINAL accepted region;
                // its local adapter must index copies without rewriting Region.buildings.
                world=new GameObject("Input compatibility road fixture").AddComponent<GeoWorld>();world.Initialize(region,Geometry.Material("Input verification asphalt",Color.gray));Physics.SyncTransforms();
                Check(world.LoadedChunks>0&&Physics.RaycastAll(new Vector3(0,10,0),Vector3.down,20).Any(h=>h.collider.name=="Mapped road"&&h.collider.GetComponentInParent<GeoWorld>()==world),"actual accepted source inputs still publish driveable critical road colliders");
                Check(world.GetComponentsInChildren<MeshCollider>(true).Any(c=>c.name.StartsWith("Building large-convex ",StringComparison.Ordinal))&&!world.GetComponentsInChildren<MeshCollider>(true).Any(c=>c.name.StartsWith("Building zero ",StringComparison.Ordinal)),"large exact wall footprint remains physical while zero-height source creates no fabricated building");
                Check(WorldSnapshotCodec.CanonicalJson(region)==before,"production rendering never rewrites the original canonical world");UnityEngine.Object.DestroyImmediate(world.gameObject);world=null;
                var pantside=JsonUtility.FromJson<GeoRegion>(File.ReadAllText("Builds/Verification/BuildingFacade/native-world.json"));string sourceBytes=WorldSnapshotCodec.CanonicalJson(pantside);var unchanged=BuildingRenderInput.Create(pantside);
                Check(unchanged.Renderable.Length==pantside.buildings.Length&&unchanged.SkippedCount==0&&unchanged.CoalescedCount==0&&unchanged.Renderable.All(e=>e.Geometry.id==e.OriginalSource.id&&e.Geometry.height==e.OriginalSource.height&&e.Geometry.kind==e.OriginalSource.kind&&e.Geometry.roof==e.OriginalSource.roof&&e.Geometry.measuredHeight==e.OriginalSource.measuredHeight&&e.Geometry.points.SequenceEqual(e.OriginalSource.points)),"all current Pantside source geometry IDs heights classes and collider outlines remain exactly unchanged");Check(WorldSnapshotCodec.CanonicalJson(pantside)==sourceBytes,"current real-world source snapshot bytes remain immutable");
                var degenerate=Box("degenerate",0,0);degenerate.points=new[]{Vector3.zero,Vector3.right,Vector3.right*2};region.buildings=new[]{degenerate};Check(BuildingRenderInput.Create(region).SkippedCount==1,"degenerate footprint returns explicit no-visual fallback instead of a chunk exception");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/building-render-input.txt",$"PASS {checks} actual codec compatibility normalization bounded large geometry identity lineage convex roof fallback and critical-road checks\nNo network-schema mutation or native pixel/performance acceptance claim.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{if(world)UnityEngine.Object.DestroyImmediate(world.gameObject);}
        }
    }
}
