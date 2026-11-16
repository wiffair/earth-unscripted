using System;using System.IO;using System.Collections.Generic;using System.Threading;using UnityEngine;
namespace EarthUnscripted.Core800
{
    //16named320root owners, multiple EXACT original-origin mesh objects each.
    // Caller activates only after complete generation. No claim of16mesh objects.
    public static class WorldMeshRootUpload800
    {
        public static IEnumerable<int> BuildHidden(Transform inactiveRoot,PreparedWorldMeshPlan800 plan,PreparedWorldMeshPlan800.RootKey key,Func<PreparedWorldMeshPlan800.Role,Material> sharedMaterial,CancellationToken token=default,Func<bool> current=null,Action<GameObject> completed=null)
        {
            if(!inactiveRoot||inactiveRoot.gameObject.activeInHierarchy||inactiveRoot.position!=Vector3.zero||inactiveRoot.rotation!=Quaternion.identity||inactiveRoot.lossyScale!=Vector3.one||plan==null||sharedMaterial==null)throw new InvalidOperationException("Identity inactive root and actual scoped plan required");var ids=plan.CopyRootPacketIds(key);if(ids.Length==0)throw new InvalidDataException("Unowned320root; no old-geometry fill");
            GameObject owned=null;var meshes=new List<Mesh>();bool handed=false;
            void Check(){token.ThrowIfCancellationRequested();if(current!=null&&!current())throw new OperationCanceledException("800root generation stale");}
            try
            {
                Check();owned=new GameObject("Scoped800 root "+key);owned.transform.SetParent(inactiveRoot,false);
                foreach(string id in ids)
                {
                    Check();var data=plan.CopyForUpload(id);var material=sharedMaterial(plan.PacketRole(id));if(!material)throw new InvalidDataException("Shared role material missing");var v=new Vector3[data.XYZ.Length/3];var uv=new Vector2[v.Length];for(int i=0;i<v.Length;i++){if((i&1023)==0)Check();v[i]=new Vector3(data.XYZ[i*3],data.XYZ[i*3+1],data.XYZ[i*3+2]);uv[i]=new Vector2((float)(data.OriginX+data.XYZ[i*3]),(float)(data.OriginZ+data.XYZ[i*3+2]));}
                    var item=Geometry.Mesh(id,owned.transform,v,data.Indices,uv,material,true);meshes.Add(item.GetComponent<MeshFilter>().sharedMesh);item.transform.localPosition=new Vector3((float)data.OriginX,(float)data.OriginY,(float)data.OriginZ);if(item.GetComponent<MeshCollider>().sharedMesh!=item.GetComponent<MeshFilter>().sharedMesh)throw new InvalidDataException("Final renderer/collider mesh mismatch");yield return 0;
                }
                Check();completed?.Invoke(owned);handed=true;
            }
            finally
            {
                if(!handed){if(owned)Geometry.ReleaseMeshes(owned);foreach(var mesh in meshes)if(mesh){if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);else UnityEngine.Object.DestroyImmediate(mesh);}if(owned){if(Application.isPlaying)UnityEngine.Object.Destroy(owned);else UnityEngine.Object.DestroyImmediate(owned);}}
            }
        }
    }
}
