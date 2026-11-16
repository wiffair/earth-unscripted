using UnityEngine;
namespace EarthUnscripted
{
    // TextMesh font geometry may arrive a frame after creation on some backends.
    public sealed class FitWorldText : MonoBehaviour
    {
        public float Width,Height;int frames;
        public void Initialize(float width,float height){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
Width=width;Height=height;}
        void LateUpdate()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            var bounds=GetComponent<MeshRenderer>().localBounds;
            if(bounds.size.x>.001f&&bounds.size.y>.001f)transform.localScale=Vector3.one*Mathf.Min(Width/bounds.size.x,Height/bounds.size.y);
            if(++frames>=4)enabled=false;
        }
    }
}
