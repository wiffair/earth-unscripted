using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class RoadMarkingUploadVerification
    {
        public static void Run()
        {
            int checks=0;var roots=new List<GameObject>();var iterators=new List<IEnumerator<int>>();
            void Check(bool ok,string why){if(!ok)throw new Exception("ROAD PAINT UPLOAD FAIL "+why);checks++;Debug.Log("ROAD PAINT UPLOAD PASS "+why);}
            GameObject Root(string name){var root=new GameObject(name);roots.Add(root);root.SetActive(false);return root;}
            Vector3[] GeometryVertices(GameObject root)=>root.GetComponentsInChildren<MeshFilter>(true).SelectMany(f=>f.sharedMesh.vertices).ToArray();
            int[] GeometryIndices(GameObject root)=>root.GetComponentsInChildren<MeshFilter>(true).SelectMany(f=>f.sharedMesh.triangles).ToArray();
            try
            {
                var paint=Geometry.Material("Map road paint",new Color(.82f,.81f,.73f));var beforeColor=paint.color;
                var road=new GeoRoad{id="fixture",start="first",end="last",kind="residential",width=8,limit=30,points=new[]{Vector3.zero,new Vector3(10,0,0)}};var plan=new RoadMarkingPlan();
                for(int i=0;i<4000;i++)plan.Strokes.Add(new RoadMarkingPlan.Stroke{RoadId="fixture",ChainId="unchanged-chain-phase",A=new Vector3(1,0,1),B=new Vector3(6,0,1),RightA=new Vector3(0,0,-1),RightB=new Vector3(0,0,-1),ArcStart=i*12,ArcEnd=i*12+5});
                var prepared=new RoadMarkingChunkGeometry.Prepared(plan,new[]{road},320);Check(prepared.StrokeCount==4000&&prepared.CandidateCount(Vector2Int.zero)==4000&&prepared.CandidateCount(new Vector2Int(50,50))==0,"prepared per-chunk lookup excludes distant source scan");
                var sync=Root("Synchronous paint iterator drain");foreach(var step in RoadMarkingChunkGeometry.Build(sync.transform,prepared,Vector2Int.zero,paint,(x,z)=>2)){}
                var staged=Root("Staged paint iterator drain");var work=RoadMarkingChunkGeometry.Build(staged.transform,prepared,Vector2Int.zero,paint,(x,z)=>2).GetEnumerator();iterators.Add(work);int checkpoints=0;while(work.MoveNext())checkpoints++;
                var a=sync.GetComponentsInChildren<MeshFilter>(true);var b=staged.GetComponentsInChildren<MeshFilter>(true);Check(a.Length>1&&a.Length<16&&a.Length==b.Length,"thousands of dash fragments remain bounded batched objects");Check(checkpoints>=4000/RoadMarkingChunkGeometry.MaximumStrokesPerSlice,"actual upload iterator exposes cooperative checkpoints");
                Check(GeometryVertices(sync).SequenceEqual(GeometryVertices(staged))&&GeometryIndices(sync).SequenceEqual(GeometryIndices(staged)),"sync and staged paths generate bit-identical vertex/index sequences");
                for(int j=0;j<a.Length;j++)
                {
                    var m=a[j].sharedMesh;Check(m.vertexCount<=8192&&m.uv.Length==m.vertexCount&&m.normals.Length==m.vertexCount,"actual Unity upload has bounded complete arrays "+j);
                    Check(m.vertices.All(v=>!float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&Mathf.Abs(v.y-2.025f)<.0001f)&&m.normals.All(n=>n.y>.99f),"actual paint retains surface+.025 and upward winding "+j);
                    Check(a[j].GetComponent<Renderer>().sharedMaterial==paint&&b[j].GetComponent<Renderer>().sharedMaterial==paint,"all uploads retain identical shared white material "+j);
                    Check(a[j].GetComponent<GeneratedMeshOwner>()&&a[j].GetComponent<GeneratedMeshOwner>().Mesh==m,"every upload has explicit generated ownership "+j);
                    Check(m.uv.SequenceEqual(b[j].sharedMesh.uv),"staged/sync world metric UV/phase identical "+j);
                }
                Check(sync.GetComponentsInChildren<Collider>(true).Length==0&&staged.GetComponentsInChildren<Collider>(true).Length==0,"cosmetic paint creates no collider or tyre-support change");
                var cancelledBefore=Root("Cancel paint before first upload");var before=RoadMarkingChunkGeometry.Build(cancelledBefore.transform,prepared,Vector2Int.zero,paint,(x,z)=>2).GetEnumerator();iterators.Add(before);Check(before.MoveNext()&&cancelledBefore.GetComponentsInChildren<MeshFilter>(true).Length==0,"first cooperative checkpoint precedes first dense upload");before.Dispose();Geometry.ReleaseMeshes(cancelledBefore);UnityEngine.Object.DestroyImmediate(cancelledBefore);Check(paint&&paint.color==beforeColor,"cancel-before-upload preserves shared material");
                var cancelledAfter=Root("Cancel paint after first upload");var after=RoadMarkingChunkGeometry.Build(cancelledAfter.transform,prepared,Vector2Int.zero,paint,(x,z)=>2).GetEnumerator();iterators.Add(after);int guard=0;while(cancelledAfter.GetComponentsInChildren<MeshFilter>(true).Length==0&&after.MoveNext())if(++guard>1000)throw new Exception("No upload before bounded cancellation guard");var owned=cancelledAfter.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();Check(owned.Length==1,"cancellation fixture reaches exactly first owned batch");after.Dispose();Geometry.ReleaseMeshes(cancelledAfter);UnityEngine.Object.DestroyImmediate(cancelledAfter);Check(owned.All(m=>!m)&&paint&&paint.color==beforeColor,"cancel-after-upload destroys owned meshes without deleting shared paint");
                var malformed=new RoadMarkingPlan();malformed.Strokes.Add(new RoadMarkingPlan.Stroke{RoadId="missing",ChainId="bad",A=Vector3.zero,B=Vector3.forward,RightA=Vector3.right,RightB=Vector3.right,ArcStart=0,ArcEnd=1});Check(!RoadMarkingChunkGeometry.TryPrepare(malformed,new[]{road},320,out var rejected,out var error)&&rejected==null&&!string.IsNullOrEmpty(error),"invalid optional plan exposes fallback reason without creating world objects");
                var failed=Root("Bad cosmetic height");foreach(var step in RoadMarkingChunkGeometry.Build(failed.transform,prepared,Vector2Int.zero,paint,(x,z)=>float.NaN)){}Check(failed.GetComponentsInChildren<MeshFilter>(true).Length==0&&prepared.RejectedStrokes==4000&&!string.IsNullOrEmpty(prepared.LastError),"nonfinite cosmetic surface publishes no partial dash and exposes diagnostic");
                Check(plan.Strokes[0].A==new Vector3(1,0,1)&&plan.Strokes[0].ArcStart==0&&plan.Strokes[3999].ArcStart==3999*12&&road.width==8,"upload/cancellation never rewrites canonical road or chain phase");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/road-marking-upload.txt","PASS "+checks+" actual Unity batching/staging/material/no-collider/ownership/cancellation checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{foreach(var iterator in iterators)iterator.Dispose();foreach(var root in roots)if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}}
        }
    }
}
