using UnityEngine;
namespace EarthUnscripted
{
    /// <summary>Light roadside post yields at its base; paint/stop lines remain
    /// on the road. Colliders retire immediately so a bent post cannot pin a car.</summary>
    public sealed class RoadSignImpact:MonoBehaviour
    {
        public const float EffectiveMassKg=100, YieldEnergyJoules=3000;
        public bool Bent {get;private set;}
        Quaternion from,to;float progress;
        public bool Strike(float carMass,float closingSpeed,Vector3 direction)
        {
            if(Bent)return true;
            if(.5f*carMass*closingSpeed*closingSpeed<YieldEnergyJoules)return false;
            Bent=true;foreach(var c in GetComponents<Collider>())c.enabled=false;
            var lean=Vector3.ProjectOnPlane(direction,Vector3.up);if(lean.sqrMagnitude<.01f)lean=transform.forward;
            from=transform.rotation;to=Quaternion.FromToRotation(Vector3.up,(lean.normalized+Vector3.up*.20f).normalized)*from;progress=0;return true;
        }
        void Update(){if(!Bent||progress>=1)return;progress=Mathf.Min(1,progress+Time.deltaTime*2.5f);transform.rotation=Quaternion.Slerp(from,to,1-(1-progress)*(1-progress));}
    }
}
