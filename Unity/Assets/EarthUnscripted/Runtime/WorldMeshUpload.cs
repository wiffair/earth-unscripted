using System;using System.Collections.Generic;using System.IO;using System.Threading;using UnityEngine;
namespace EarthUnscripted
{
    // Caller owns activation. Dispose a suspended iterator to release ONLY this
    // upload's owned subtree/meshes; shared materials and sibling objects survive.
    public static class WorldMeshUpload
    {
        public static IEnumerable<int> BuildHidden(Transform inactiveRoot,PreparedWorldMeshPlan plan,Func<PreparedWorldMeshPlan.Role,Material> sharedMaterial,CancellationToken token=default,Func<bool> stillCurrent=null,Action<GameObject> completed=null)
        {
            if(!inactiveRoot||inactiveRoot.gameObject.activeInHierarchy||plan==null||sharedMaterial==null)throw new InvalidOperationException("Inactive staged root, verified scoped plan and shared material resolver required");
            if(inactiveRoot.position!=Vector3.zero||inactiveRoot.rotation!=Quaternion.identity||inactiveRoot.lossyScale!=Vector3.one)throw new InvalidDataException("Current mesh adapter requires identity world root; packet origins are explicit");
            GameObject owned=null;bool finished=false;var allocatedMeshes=new List<Mesh>();
            void Check(){token.ThrowIfCancellationRequested();if(stillCurrent!=null&&!stillCurrent())throw new OperationCanceledException("World mesh upload request stale");}
            try
            {
                Check();owned=new GameObject("Verified scoped road-terrain "+plan.GeometryDigest.Substring(0,12));owned.transform.SetParent(inactiveRoot,false);
                foreach(var packet in plan.CopyPackets())
                {
                    Check();var data=packet.CopyFinalBuffers();var material=sharedMaterial(packet.SurfaceRole);if(!material)throw new InvalidDataException("Missing shared surface material");
                    var vertices=new Vector3[data.XYZ.Length/3];var uv=new Vector2[vertices.Length];
                    for(int i=0;i<vertices.Length;i++){if((i&1023)==0)Check();vertices[i]=new Vector3(data.XYZ[i*3],data.XYZ[i*3+1],data.XYZ[i*3+2]);uv[i]=new Vector2((float)(data.OriginX+data.XYZ[i*3]),(float)(data.OriginZ+data.XYZ[i*3+2]));}
                    var item=Geometry.Mesh(packet.Id,owned.transform,vertices,(int[])data.Indices.Clone(),uv,material,true);
                    allocatedMeshes.Add(item.GetComponent<MeshFilter>().sharedMesh);
                    item.transform.localPosition=new Vector3((float)data.OriginX,(float)data.OriginY,(float)data.OriginZ);
                    // MeshCollider and renderer reference the SAME generated Mesh.
                    if(item.GetComponent<MeshCollider>().sharedMesh!=item.GetComponent<MeshFilter>().sharedMesh)throw new InvalidDataException("Upload/query/collider ownership mismatch");
                    yield return 0; // Never yield through an active incomplete actor/world.
                }
                Check();completed?.Invoke(owned);finished=true;
            }
            finally
            {
                if(!finished)
                {
                    // Inactive child GOs can already be destroyed before parent
                    // coroutine disposal. Their OnDestroy may not release meshes.
                    // Actual allocation refs, NOT GO truthiness, own this cleanup.
                    if(owned)Geometry.ReleaseMeshes(owned);
                    foreach(var mesh in allocatedMeshes)if(mesh){if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);else UnityEngine.Object.DestroyImmediate(mesh);}
                    if(owned){if(Application.isPlaying)UnityEngine.Object.Destroy(owned);else UnityEngine.Object.DestroyImmediate(owned);}
                }
            }
        }
    }
}
