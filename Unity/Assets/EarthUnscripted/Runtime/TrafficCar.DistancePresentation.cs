using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class TrafficCar
    {
        // Ordinary traffic fades out before the recycle boundary. Terrain and
        // police retain their existing visibility rules and population budgets.
        internal const float TrafficCullMetres=360, TrafficSpawnMinimum=430;
        LODGroup distancePresentation;Camera trafficCamera;
        internal bool EnsureDistancePresentation()
        {
            if(!Visual||Police||ManagedPolice)return false;
            if(!distancePresentation)
            {
                using var edit=VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);
                if(Visual.GetComponentInChildren<LODGroup>())return false;
                var renderers=Visual.GetComponentsInChildren<MeshRenderer>();
                if(renderers.Length==0)return false;
                distancePresentation=Visual.gameObject.AddComponent<LODGroup>();
                distancePresentation.localReferencePoint=Vector3.up;
                distancePresentation.fadeMode=LODFadeMode.CrossFade;
                distancePresentation.animateCrossFading=false;
                distancePresentation.SetLODs(new[]{new LOD(.01f,renderers){fadeTransitionWidth=.00252525f}});
            }
            UpdateDistancePresentationSize();return true;
        }
        void UpdateDistancePresentationSize()
        {
            if(!trafficCamera&&Session&&Session.View)trafficCamera=Session.View.GetComponent<Camera>();
            float fov=trafficCamera?trafficCamera.fieldOfView:65;
            // A fixed 360m world boundary, independent of FOV/quality LOD bias.
            distancePresentation.size=TrafficCullMetres*2*Mathf.Tan(fov*Mathf.Deg2Rad*.5f)*.01f/Mathf.Max(.01f,QualitySettings.lodBias);
        }
        internal bool HasDistancePresentation=>distancePresentation;
    }
}
