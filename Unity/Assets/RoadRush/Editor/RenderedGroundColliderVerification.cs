using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    // Tests the proposed CPU descriptor against the CURRENT renderer/colliders.
    // Does not upload replacement terrain or change asphalt. Terrain gates belong
    // to the separate paired grounding test after this equivalence gate passes.
    public static class RenderedGroundColliderVerification
    {
        public static void Run()
        {
            WorldBuilder world=null;string previous=PlayerProfile.StorageOverride;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushWidthWorld");
                if(at<0||at+1>=args.Length)throw new InvalidDataException("Explicit authoritative world fixture required");
                string output=Path.GetFullPath("Builds/Verification/RenderedGroundCollider/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));Directory.CreateDirectory(output);
                PlayerProfile.StorageOverride=Path.Combine(output,"isolated-profile.json");
                var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(args[at+1]));
                string sourceBefore=JsonUtility.ToJson(region);
                var prepared=WorldDataFinalizer.AttachAuthoritative(region,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                RenderedGroundGeometry descriptor=null;foreach(var step in RenderedGroundGeometry.Prepare(region,prepared.RoadSurfaces,x=>descriptor=x,CancellationToken.None)){}
                ConformingTerrainSupport support=null;foreach(var step in ConformingTerrainSupport.Prepare(descriptor,x=>support=x,CancellationToken.None)){}
                string preparedBefore=JsonUtility.ToJson(region);
                var selected=new HashSet<Vector2Int>();
                foreach(var part in prepared.RoadSurfaces.Parts.Values)foreach(var p in part.Surface.Vertices)selected.Add(Cell(p));
                int seedAt=Array.IndexOf(args,"-roadRushWidthSeed");
                if(seedAt>=0){if(seedAt+1>=args.Length)throw new InvalidDataException("Missing explicit seed fixture");var seed=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(args[seedAt+1]));var seedPrepared=WorldDataFinalizer.AttachAuthoritative(seed,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);foreach(var part in seedPrepared.RoadSurfaces.Parts.Values)foreach(var p in part.Surface.Vertices)selected.Add(Cell(p));}
                if(selected.Count==0)throw new InvalidDataException("Expected changed-road fixture or explicit -roadRushWidthSeed candidate");
                var requested=new HashSet<Vector2Int>(selected);
                foreach(var cell in selected)for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)requested.Add(new Vector2Int(cell.x+x,cell.y+z));
                world=new GameObject("Unchanged asphalt descriptor equivalence").AddComponent<WorldBuilder>();world.BuildPrepared(prepared);var geo=world.GeographicWorld;
                var build=typeof(GeoWorld).GetMethod("Build",BindingFlags.Instance|BindingFlags.NonPublic);
                foreach(var cell in requested.OrderBy(p=>p.y).ThenBy(p=>p.x))build.Invoke(geo,new object[]{cell,"descriptor equivalence"});Physics.SyncTransforms();
                // Read the independent meshes actually submitted to Unity. A
                // single ray hit per collider cannot expose the next triangle
                // after an extrapolated edge hit is excluded.
                var uploaded=new RenderedGroundGeometry();
                var records=(List<RenderedGroundGeometry.Record>)typeof(RenderedGroundGeometry).GetField("records",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(uploaded);
                foreach(var collider in geo.GetComponentsInChildren<MeshCollider>())
                {
                    if(!GroundRoad(collider.name)||!collider.sharedMesh)continue;
                    var mesh=collider.sharedMesh;
                    records.Add(new RenderedGroundGeometry.Record{Id="actual-mesh-"+records.Count,Kind=collider.name,Mesh=new RenderedGroundGeometry.Data{Vertices=mesh.vertices.Select(v=>collider.transform.TransformPoint(v)).ToArray(),Triangles=mesh.triangles,UV=mesh.uv}});
                }
                ConformingTerrainSupport actualMeshIndex=null;foreach(var step in ConformingTerrainSupport.Prepare(uploaded,x=>actualMeshIndex=x,CancellationToken.None)){}
                int samples=0,missing=0,mismatch=0,actualTriangles=0,rawTopOutside=0,rawDifferences=0,strictRayDifferences=0;float worst=0,rawWorst=0;var diagnostics=new JArray();var rawDiagnostics=new JArray();
                void Probe(Vector3 p,string label)
                {
                    if(!selected.Contains(Cell(p)))return;
                    bool predicted=support.TryAsphalt(p.x,p.z,out float top);bool hasActualMesh=actualMeshIndex.TryAsphalt(p.x,p.z,out float actual);float strictRay=float.MinValue,rawActual=float.MinValue;bool rawOutside=false;
                    float expected=region.RoadSurfaceHeight(p.x,p.z);
                    var hits=Physics.RaycastAll(new Vector3(p.x,Mathf.Max(expected,p.y)+80,p.z),Vector3.down,180);
                    foreach(var hit in hits)
                    {
                        if(hit.collider.GetComponentInParent<GeoWorld>()!=geo||!GroundRoad(hit.collider.name))continue;
                        // Descriptors intentionally exclude bridge/tunnel structures.
                        // Current ground meshes can overlap a separately named deck;
                        // only accepted ground-road collider names enter this proof.
                        var bary=hit.barycentricCoordinate;bool inside=bary.x>=-1e-7f&&bary.y>=-1e-7f&&bary.z>=-1e-7f;
                        if(hit.point.y>rawActual){rawActual=hit.point.y;rawOutside=!inside;}
                        if(inside)strictRay=Mathf.Max(strictRay,hit.point.y);
                    }
                    if(hasActualMesh&&strictRay!=float.MinValue&&Mathf.Abs(strictRay-actual)>.0002f)strictRayDifferences++;
                    if(rawOutside)rawTopOutside++;
                    if(predicted&&rawActual!=float.MinValue)rawWorst=Mathf.Max(rawWorst,Mathf.Abs(rawActual-top));
                    if(actual!=float.MinValue&&rawActual-actual>.0002f)
                    {rawDifferences++;if(rawDiagnostics.Count<64)rawDiagnostics.Add(new JObject{{"label",label},{"x",p.x},{"z",p.z},{"descriptor",top},{"strictMeshHit",actual},{"rawPhysicsHit",rawActual},{"extraHeight",rawActual-actual},{"hits",new JArray(hits.Where(h=>h.collider.GetComponentInParent<GeoWorld>()==geo&&GroundRoad(h.collider.name)).OrderByDescending(h=>h.point.y).Take(12).Select(h=>HitDetails(h,p)))}});}
                    samples++;if(!predicted||!hasActualMesh){missing++;if(diagnostics.Count<64)diagnostics.Add(new JObject{{"kind","missing"},{"label",label},{"point",p.ToString("R")},{"predicted",predicted},{"actualHit",hasActualMesh}});return;}
                    float error=Mathf.Abs(top-actual);worst=Mathf.Max(worst,error);
                    if(error>.0002f){mismatch++;if(diagnostics.Count<64)diagnostics.Add(new JObject{{"kind","height"},{"label",label},{"x",p.x},{"z",p.z},{"descriptor",top},{"actual",actual},{"error",error},{"actualHits",new JArray(hits.Where(h=>h.collider.GetComponentInParent<GeoWorld>()==geo&&GroundRoad(h.collider.name)).OrderByDescending(h=>h.point.y).Take(12).Select(h=>HitDetails(h,p)))}});}
                }
                // Independent actual mesh triangles, including current chunk cuts,
                // original legacy strips and caps. Sample strict interior centroids
                // to avoid an edge ray-casting convention deciding coverage.
                foreach(var collider in geo.GetComponentsInChildren<MeshCollider>())
                {
                    if(!GroundRoad(collider.name)||!collider.sharedMesh)continue;
                    var mesh=collider.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;
                    for(int i=0;i<t.Length;i+=3)
                    {
                        var a=collider.transform.TransformPoint(v[t[i]]);var b=collider.transform.TransformPoint(v[t[i+1]]);var c=collider.transform.TransformPoint(v[t[i+2]]);
                        if(Vector3.Cross(b-a,c-a).y<=1e-8f)continue;
                        var centre=(a+b+c)/3;if(!selected.Contains(Cell(centre)))continue;
                        actualTriangles++;Probe(centre,collider.name+"/tri"+(i/3));
                    }
                }
                // Reverse direction: every descriptor triangle within resident core
                // must also be represented by a current collider at its centroid.
                foreach(var record in descriptor.Records){var d=record.Mesh;for(int i=0;i<d.Triangles.Length;i+=3){var a=d.Vertices[d.Triangles[i]];var b=d.Vertices[d.Triangles[i+1]];var c=d.Vertices[d.Triangles[i+2]];if(Vector3.Cross(b-a,c-a).y>1e-8f)Probe((a+b+c)/3,record.Kind+"/"+record.Id);}}
                bool unchanged=preparedBefore==JsonUtility.ToJson(region);
                var receipt=new JObject{{"status",samples>1000&&missing==0&&mismatch==0&&unchanged?"PASS":"FAIL"},{"samples",samples},{"actualTriangles",actualTriangles},{"selectedCells",selected.Count},{"requestedCellsWithHalo",requested.Count},{"missing",missing},{"mismatches",mismatch},{"maxAbsoluteMetres",worst},{"toleranceMetres",.0002},{"strictTriangleBarycentricTolerance",1e-7},{"rawTopOutsideTriangleSamples",rawTopOutside},{"rawPhysicsExtraHeightSamples",rawDifferences},{"filteredSingleRayVsUploadedMeshDifferences",strictRayDifferences},{"maxRawPhysicsVsDescriptorMetres",rawWorst},{"rawPhysicsDiagnostics",rawDiagnostics},{"preparedCanonicalUnchanged",unchanged},{"inputSourceSha256",Hash(File.ReadAllBytes(args[at+1]))},{"diagnostics",diagnostics},{"scope","Independent actual Unity renderer/collider INPUT meshes vs proposed CPU descriptors, strict triangle geometry and same0.2mm gate. Actual mesh data is read from existing MeshColliders, not regenerated by descriptor factory. Raw Physics edge-extrapolated hits retained separately, not claimed equivalent; one-hit-per-collider filtering is insufficient and not the geometry oracle. No replacement terrain, physical grounding or native performance acceptance; final grounding keeps raw ray gates unchanged."}};
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Debug.Log("GROUND DESCRIPTOR "+receipt.ToString(Newtonsoft.Json.Formatting.None));
                if((string)receipt["status"]!="PASS")throw new Exception("Current asphalt descriptor equivalence failed: "+output);
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;if(world){Geometry.ReleaseMeshes(world.gameObject);UnityEngine.Object.DestroyImmediate(world.gameObject);}}
        }
        static bool GroundRoad(string name)=>name=="Mapped road"||name=="Junction apron"||name.StartsWith("Finalized mapped asphalt",StringComparison.Ordinal);
        static JArray V(Vector3 p)=>new JArray(p.x,p.y,p.z);
        static JObject HitDetails(RaycastHit hit,Vector3 probe)
        {
            var result=new JObject{{"collider",hit.collider.name},{"triangleIndex",hit.triangleIndex},{"point",V(hit.point)},{"barycentric",V(hit.barycentricCoordinate)},{"normal",V(hit.normal)}};
            if(!(hit.collider is MeshCollider collider)||!collider.sharedMesh)return result;
            var mesh=collider.sharedMesh;var indices=mesh.triangles;var vertices=mesh.vertices;int i=hit.triangleIndex*3;
            if(i<0||i+2>=indices.Length)return result;
            var a=collider.transform.TransformPoint(vertices[indices[i]]);var b=collider.transform.TransformPoint(vertices[indices[i+1]]);var c=collider.transform.TransformPoint(vertices[indices[i+2]]);
            bool inside=ConformingTerrainSupport.Plane(probe,a,b,c,out float height);
            double ax=a.x,az=a.z,bx=b.x,bz=b.z,cx=c.x,cz=c.z,px=probe.x,pz=probe.z;
            double denominator=(bz-cz)*(ax-cx)+(cx-bx)*(az-cz),u=((bz-cz)*(px-cx)+(cx-bx)*(pz-cz))/denominator,v=((cz-az)*(px-cx)+(ax-cx)*(pz-cz))/denominator;
            double exact=u*a.y+v*b.y+(1-u-v)*c.y;
            result["a"]=V(a);result["b"]=V(b);result["c"]=V(c);result["descriptorPlaneAtInput"]=height;result["descriptorPlaneContainsInput"]=inside;result["allDoublePlaneAtInput"]=exact;result["allDoubleBarycentric"]=new JArray(u,v,1-u-v);result["cookingOptions"]=collider.cookingOptions.ToString();
            var low=new JArray();foreach(float offset in new[]{2f,10f,80f})
            {
                bool found=collider.Raycast(new Ray(new Vector3(probe.x,(float)exact+offset,probe.z),Vector3.down),out var repeat,offset+5);
                low.Add(new JObject{{"originAbovePlane",offset},{"hit",found},{"point",found?V(repeat.point):null},{"triangleIndex",found?repeat.triangleIndex:-1},{"barycentric",found?V(repeat.barycentricCoordinate):null}});
            }
            result["sameColliderRepeatRays"]=low;return result;
        }
        static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/320),Mathf.FloorToInt(p.z/320));
        static string Hash(byte[] bytes){using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    }
}
