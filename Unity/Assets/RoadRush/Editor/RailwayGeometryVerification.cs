using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class RailwayGeometryVerification
    {
        public static void Run()
        {
            int checks=0;var objects=new List<GameObject>();
            void Check(bool value,string reason){if(!value)throw new Exception("RAIL GEOMETRY FAIL "+reason);checks++;Debug.Log("RAIL GEOMETRY PASS "+reason);}
            GameObject Build(GeoRailway rail,Vector2Int key,float size,Func<Vector3,float,bool> allowed,out int stages)
            {var g=new GameObject("rail geometry fixture");objects.Add(g);stages=0;foreach(var unused in RailwayChunkGeometry.Build(g.transform,null,new[]{rail},key,size,(x,z)=>0,allowed))stages++;return g;}
            Mesh[] Meshes(GameObject g)=>g.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();
            string Fingerprint(GameObject g){var text=new StringBuilder();foreach(var m in Meshes(g)){text.Append(m.name);foreach(var p in m.vertices)text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"{0:R},{1:R},{2:R};",p.x,p.y,p.z);foreach(var n in m.triangles)text.Append(n).Append(',');}using var sha=SHA256.Create();return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));}
            GeoRailway Rail(string kind="rail",bool bridge=true)=>new GeoRailway{id="fixture",kind=kind,bridge=bridge,tracks=1,gauge=1.435f,width=3.8f,points=new[]{new Vector3(10,7,50),new Vector3(90,7,50)}};
            try
            {
                var rail=Rail();var before=(Vector3[])rail.points.Clone();var calls=new List<(Vector3 p,float radius)>();
                bool Clear(Vector3 p,float radius){calls.Add((p,radius));return Mathf.Abs(p.x-50)>radius+5;}
                var g=Build(rail,Vector2Int.zero,100,Clear,out int stages);
                Check(stages>4,"long railway yields staged work before upload completion");
                Check(rail.points.SequenceEqual(before),"renderer never mutates canonical host profile");
                Check(g.GetComponentsInChildren<MeshFilter>().Length<15,"rails and hundreds of sleepers use bounded shared batches, not per-piece objects");
                Check(g.GetComponentsInChildren<GeneratedMeshOwner>().Length==Meshes(g).Length,"every uploaded mesh has explicit chunk owner");
                Check(calls.Count>2&&calls.All(c=>c.radius>=Mathf.Sqrt(4.4f*4.4f+1.1f*1.1f)*.5f-.001f),"support veto receives circumscribed full footprint radius including widened abutments");
                var structure=g.GetComponentsInChildren<MeshCollider>().First(c=>c.name.Contains("deck"));var structureMesh=structure.sharedMesh;
                Check(Mathf.Abs(structureMesh.vertices.Max(v=>v.y)-6.55f)<.001f,"closed deck surface is railhead minus track buildup");
                Check(structureMesh.vertices.Any(v=>Mathf.Abs(v.y-5.8f)<.001f),"bridge underside is railhead minus complete 1.20m depth");
                Check(5.8f-1>=RailwayMetrics.MinimumRoadClearance,"fixture 1m-high road retains at least 4.6m physical underpass clearance");
                Physics.SyncTransforms();
                Check(structure.Raycast(new Ray(new Vector3(50,10,50),Vector3.down),out var hit,20)&&Mathf.Abs(hit.point.y-6.55f)<.002f,"deck top has outward collidable winding");
                Check(structure.Raycast(new Ray(new Vector3(50,2,50),Vector3.up),out hit,10)&&Mathf.Abs(hit.point.y-5.8f)<.002f,"closed bridge underside raycast proves downward winding and depth");
                Check(!structure.Raycast(new Ray(new Vector3(50,2,35),Vector3.forward),out hit,30),"road passage has no pillar or abutment collider across its full width");
                Check(g.GetComponentsInChildren<MeshCollider>().All(c=>!c.name.Contains("rails")&&!c.name.Contains("sleepers")&&!c.name.Contains("ballast")),"railheads sleepers ballast remain visual: no unrequested crossing bump physics");
                foreach(var mesh in Meshes(g))
                {Check(mesh.vertexCount<=10000,"bounded individual upload vertex count");Check(mesh.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)),"finite generated positions");Check(mesh.triangles.Length%3==0&&mesh.normals.All(n=>n.sqrMagnitude>.9f),"closed original pieces have nondegenerate normals");}
                var repeat=Build(Rail(),Vector2Int.zero,100,Clear,out int repeatStages);Check(Fingerprint(g)==Fingerprint(repeat)&&stages==repeatStages,"staged deterministic geometry fingerprint independent of frame consumption");
                var across=Rail();across.points=new[]{new Vector3(10,7,50),new Vector3(190,9,50)};
                var left=Build(across,Vector2Int.zero,100,(p,r)=>false,out _);var right=Build(across,new Vector2Int(1,0),100,(p,r)=>false,out _);
                Vector3[] Boundary(GameObject obj)=>obj.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.Contains("deck")).SelectMany(f=>f.sharedMesh.vertices).Where(v=>Mathf.Abs(v.x-100)<.0001f).Distinct().OrderBy(v=>v.y).ThenBy(v=>v.z).ToArray();
                Check(Boundary(left).Length==4&&Boundary(left).SequenceEqual(Boundary(right)),"sloping deck chunk boundary uses identical interpolated canonical cross-section");
                var combined=Build(across,Vector2Int.zero,200,(p,r)=>false,out _);
                int Sleepers(GameObject obj)=>obj.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.Contains("sleepers")).Sum(f=>f.sharedMesh.vertexCount/24);
                Check(Sleepers(left)+Sleepers(right)==Sleepers(combined),"global sleeper stations partition without duplication or loss at chunk seams");
                foreach(string kind in new[]{"proposed","construction","razed"}){var hidden=Build(Rail(kind),Vector2Int.zero,100,(p,r)=>true,out _);Check(Meshes(hidden).Length==0,kind+" lifecycle has no misleading operational structure");}
                var tunnel=Rail();tunnel.tunnel=true;Check(Meshes(Build(tunnel,Vector2Int.zero,100,(p,r)=>true,out _)).Length==0,"tunnels never become ground rails or exposed decks");
                var disused=Build(Rail("disused"),Vector2Int.zero,100,(p,r)=>false,out _);Check(disused.GetComponentsInChildren<MeshRenderer>().Any(r=>r.sharedMaterial.name.Contains("rusted")),"disused rails use distinct rusted material");
                var abandoned=Build(Rail("abandoned"),Vector2Int.zero,100,(p,r)=>false,out _);Check(abandoned.GetComponentsInChildren<MeshFilter>().All(f=>!f.name.Contains("rails")&&!f.name.Contains("sleepers")),"abandoned retains formation and mapped bridge, never running rails");
                var tram=Build(Rail("tram",false),Vector2Int.zero,100,(p,r)=>true,out _);Check(tram.GetComponentsInChildren<MeshFilter>().All(f=>!f.name.Contains("ballast")&&!f.name.Contains("sleepers"))&&tram.GetComponentsInChildren<MeshCollider>().Length==0,"on-grade tram has embedded visual rails without raised ballast or collision");
                var twin=Rail();twin.tracks=2;twin.width=8;var twinObj=Build(twin,Vector2Int.zero,100,(p,r)=>false,out _);
                var heads=twinObj.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.Contains("rails")).SelectMany(f=>f.sharedMesh.vertices).Where(p=>Mathf.Abs(p.y-7)<.001f).Select(p=>p.z).Distinct().OrderBy(z=>z).ToArray();
                Check(heads.Length==8&&Mathf.Abs((heads[0]+heads[1])*.5f-(50-2.1f-.7175f))<.001f,"double track exact 4.2m centres / 1.435m gauge / 70mm head width");
                var approach=Rail("rail",false);approach.points=new[]{new Vector3(10,5,50),new Vector3(90,5,50)};
                var earthApproach=Build(approach,Vector2Int.zero,100,(p,r)=>true,out int approachStages);var earthStats=earthApproach.GetComponent<RailwayChunkMeshOwner>();
                var earthMesh=earthApproach.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.Contains("earth embankment")).sharedMesh;
                Check(earthStats.ApproachEarthBodies==32&&earthStats.ApproachRetainingBodies==0&&approachStages>4,"80m raised approach builds staged 2.5m earth slices, not sparse giant piers");
                Check(Mathf.Abs(earthMesh.vertices.Max(v=>v.y)-4.55f)<.001f&&Mathf.Abs(earthMesh.vertices.Min(v=>v.y)+.15f)<.001f,"approach solid connects exact canonical bed bottom to terrain-embedded base");
                Check(Mathf.Abs(earthMesh.bounds.size.z-17.9f)<.002f,"4.70m earth body has exact 1.5:1 toes plus 3.8m trackbed width");
                Check(approach.points.All(p=>p.y==5),"ground sampling for embankment never changes canonical head height");
                var retainingApproach=Build(approach,Vector2Int.zero,100,(p,r)=>r<3,out _);var retainStats=retainingApproach.GetComponent<RailwayChunkMeshOwner>();
                Check(retainStats.ApproachEarthBodies==0&&retainStats.ApproachRetainingBodies==32,"broad toe exclusion independently retries compact retaining footprint");
                var retainingMesh=retainingApproach.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.Contains("retaining stone")).sharedMesh;
                Check(Mathf.Abs(retainingMesh.bounds.size.z-3.8f)<.001f,"compact stone fallback preserves trackbed footprint without earth toes");
                var roadApproach=Build(approach,Vector2Int.zero,100,(p,r)=>Mathf.Abs(p.x-50)>r+5,out _);var roadStats=roadApproach.GetComponent<RailwayChunkMeshOwner>();
                Check(roadStats.ApproachEarthBodies>0&&roadStats.ApproachRetainingBodies>0&&roadStats.ApproachExcluded>0,"road exclusion yields earth, compact retaining and truthful no-fill diagnostics");
                var approachColliders=roadApproach.GetComponentsInChildren<MeshCollider>();Physics.SyncTransforms();
                Check(approachColliders.All(c=>!c.Raycast(new Ray(new Vector3(50,2,30),Vector3.forward),out _,40)),"raised nonbridge approach never fills a vetoed underpass road corridor");
                Check(approachColliders.SelectMany(c=>c.sharedMesh.vertices).All(v=>Mathf.Abs(v.x-50)>5),"all actual fill vertices stay outside complete protected road width");
                var noFill=Build(approach,Vector2Int.zero,100,(p,r)=>false,out _);Check(noFill.GetComponentsInChildren<MeshCollider>().Length==0&&noFill.GetComponent<RailwayChunkMeshOwner>().ApproachExcluded==32,"both vetoes fail closed with explicit excluded counts, not a false supported claim");
                var leftBank=Build(approach,Vector2Int.zero,50,(p,r)=>true,out _);var rightBank=Build(approach,new Vector2Int(1,1),50,(p,r)=>true,out _);
                // The rail is exactly on z=50, so half-open ownership belongs only to row1.
                Check(Meshes(leftBank).Length==0,"approach on chunk boundary retains half-open row ownership");
                var groundLevel=Rail("rail",false);groundLevel.points=new[]{new Vector3(10,.45f,50),new Vector3(90,.45f,50)};
                var groundObj=Build(groundLevel,Vector2Int.zero,100,(p,r)=>true,out _);Check(groundObj.GetComponentsInChildren<MeshCollider>().Length==0,"ordinary ground-level track gains no unnecessary collision fill");
                var owned=Meshes(repeat);var shared=repeat.GetComponentInChildren<MeshRenderer>().sharedMaterial;UnityEngine.Object.DestroyImmediate(repeat);Check(owned.All(m=>!m)&&shared,"chunk destruction frees owned meshes and preserves shared materials");
                var cancelRoot=new GameObject("rail cancelled chunk");objects.Add(cancelRoot);var longRail=Rail();longRail.points=new[]{new Vector3(1,7,50),new Vector3(999,7,50)};
                var iterator=RailwayChunkGeometry.Build(cancelRoot.transform,null,new[]{longRail},Vector2Int.zero,1000,(x,z)=>0,(p,r)=>false).GetEnumerator();
                int consumed=0;while(iterator.MoveNext()&&Meshes(cancelRoot).Length==0&&consumed++<200){}
                var partiallyUploaded=Meshes(cancelRoot);Check(partiallyUploaded.Length>0,"cancel fixture reaches real upload before iterator completion");iterator.Dispose();
                UnityEngine.Object.DestroyImmediate(cancelRoot);Check(partiallyUploaded.All(m=>!m),"cancelling a half-built iterator and destroying chunk frees all uploaded railway meshes");
                var released=Build(Rail(),Vector2Int.zero,100,(p,r)=>false,out _);var releasedMeshes=Meshes(released);var preserved=released.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                Geometry.ReleaseMeshes(released);Check(releasedMeshes.All(m=>!m),"explicit GeoWorld-style release frees railway meshes first");
                UnityEngine.Object.DestroyImmediate(released);Check(preserved,"chunk fallback after prior explicit release remains idempotent and preserves shared material");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/railway-geometry.txt","PASS "+checks+" railway geometry checks\nInitial run failed standalone edit-mode ownership cleanup; retained /tmp/rr017-initial-RailwayGeometryVerification-Run.log. Fixed with ExecuteAlways per-chunk railway owner; original cleanup assertion retained.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{foreach(var obj in objects)if(obj)UnityEngine.Object.DestroyImmediate(obj);}
        }
    }
}
