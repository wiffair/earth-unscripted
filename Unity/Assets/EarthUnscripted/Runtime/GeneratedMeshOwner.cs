using UnityEngine;
namespace EarthUnscripted
{
    // Generated meshes belong to one object. Primitive meshes belong to Unity and
    // must survive chunk unloads, car rebuilds and showroom preview replacement.
    public sealed class GeneratedMeshOwner : MonoBehaviour
    {
        public Mesh Mesh;
        public System.Threading.Tasks.Task UseFence;
        public void Release(){
            if(UseFence!=null){try{UseFence.GetAwaiter().GetResult();}catch(System.Exception){/* Fault observed; release the mesh safely. */}UseFence=null;}
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(Mesh);
            var mesh=Mesh;Mesh=null;if(!mesh)return;if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);
        }
        void OnDestroy(){Release();}
    }
}
