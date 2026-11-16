using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    public sealed partial class VehicleController
    {
        readonly RaycastHit[] tyreHits = new RaycastHit[24];
        VehicleObstacleWheels obstacleWheels;
        Vector3 incomingVelocity;
        float airborneSeconds, lastLanding = -10;
        public float StructuralStrength => importedSettings?.StructuralStrength ?? 1;
        public float SafeLandingSpeed => importedSettings?.SafeLandingSpeed ?? 5.5f;
        public float WadingDepth => importedSettings?.WadingDepth ?? .35f;
        public string EngineLayout => importedSettings?.EngineLayout ?? "standard";
        public float WaterDepth { get; internal set; }
        public float LastLandingSpeed { get; private set; }
        public float LastAppliedImpactDamage { get; private set; }

        void BeginContactStep() { incomingVelocity = Body.linearVelocity; }
        bool TryWheelContact(Vector3 origin, int wheel, out RaycastHit contact, out float distance)
        {
            if (importedSettings == null || !importedSettings.WheelObstacleContact)
            {
                bool found=Physics.Raycast(origin,-transform.up,out contact,Metrics.SuspensionRayLength,~(1<<8),QueryTriggerInteraction.Ignore);
                distance=found?contact.distance:0;return found;
            }
            // Start above the tyre: a cast beginning overlapped with a bonnet
            // reports a zero/undefined point. Include the solver's contact skin,
            // otherwise its physical cylinder stops just outside the probe radius.
            float probeRadius=Metrics.Radius+Physics.defaultContactOffset*3;
            float startLift=Metrics.Radius;
            int count=Physics.SphereCastNonAlloc(origin+transform.up*startLift,probeRadius,-transform.up,tyreHits,
                Mathf.Max(.01f,Metrics.SuspensionRayLength-probeRadius+startLift),~0,QueryTriggerInteraction.Ignore);
            contact=default;distance=float.MaxValue;
            for(int index=0;index<count;index++)
            {
                var hit=tyreHits[index];
                if(hit.rigidbody==Body||hit.collider.transform.IsChildOf(transform)||Vector3.Dot(hit.normal,transform.up)<.08f)continue;
                // Sphere queries provide radial stepping, while the physical
                // cylinder supplies the actual narrow tyre collision width.
                float across=Mathf.Abs(Vector3.Dot(hit.point-origin,transform.right));
                if(across>Metrics.TyreWidth*.5f+.12f)continue;
                float candidate=hit.distance+probeRadius-startLift;
                if(candidate>=distance)continue;distance=candidate;contact=hit;
            }
            if(distance<float.MaxValue)return true;
            // A broad sphere can first touch the SIDE of the same terrain mesh
            // outside the narrow tyre footprint, or start overlapped on a verge.
            // Rejecting that hit must not discard real ground under the wheel.
            // Keep the ordinary centre ray as a support fallback; it cannot
            // invent a contact or extend the suspension's reach.
            count=Physics.RaycastNonAlloc(origin,-transform.up,tyreHits,Metrics.SuspensionRayLength,~0,QueryTriggerInteraction.Ignore);
            for(int index=0;index<count;index++)
            {
                var hit=tyreHits[index];
                if(hit.rigidbody==Body||hit.collider.transform.IsChildOf(transform)||Vector3.Dot(hit.normal,transform.up)<.08f||hit.distance>=distance)continue;
                distance=hit.distance;contact=hit;
            }
            return distance<float.MaxValue;
        }
        void FinishContactStep(int contacts, float seconds)
        {
            if(contacts<2){airborneSeconds+=seconds;return;}
            if(airborneSeconds>.08f)
            {
                Vector3 normal=Vector3.zero;
                for(int index=0;index<4;index++)if(WheelGrounded[index])normal+=ContactNormals[index];
                normal.Normalize();
                ApplyLanding(Mathf.Max(0,-Vector3.Dot(incomingVelocity,normal)),Body.position);
            }
            airborneSeconds=0;
        }
        void ApplyLanding(float normalSpeed, Vector3 point)
        {
            if(Time.time-lastLanding<.65f)return;
            LastLandingSpeed=normalSpeed;RecordHapticImpact(normalSpeed);
            float amount=VehicleImpactRules.LandingDamage(normalSpeed,SafeLandingSpeed,StructuralStrength);
            if(amount<=.01f)return;
            lastLanding=Time.time;ApplyImpactDamage(amount,point,Vector3.up,normalSpeed);
        }
        void ApplyImpactDamage(float amount,Vector3 point,Vector3 normal,float severity)
        {
            LastAppliedImpactDamage=amount;
            Damage=Mathf.Clamp(Damage+amount,0,100);
            if(Visual)Visual.Dent(point,Mathf.Min(60,amount),normal);
            Crashed?.Invoke(severity,point);
        }
        public Vector3 DiagnosticCollisionImpulse,DiagnosticOtherVelocity;public string DiagnosticCollider;
        public readonly System.Collections.Generic.List<string> DiagnosticContactEvents=new System.Collections.Generic.List<string>();
        void DiagnosticContact(Collision c)
        {
            if(SinglePlayerBeta.VerificationOutput==null)return;
            DiagnosticContactEvents.Add(c.collider.name+" impulse="+c.impulse.ToString("R")+" otherVelocity="+(c.rigidbody?c.rigidbody.linearVelocity:Vector3.zero).ToString("R"));
            if(DiagnosticContactEvents.Count>8)DiagnosticContactEvents.RemoveAt(0);
        }
        void OnCollisionStay(Collision c){DiagnosticContact(c);}

        void OnCollisionEnter(Collision collision)
        {
            if(SinglePlayerBeta.VerificationOutput!=null){DiagnosticCollisionImpulse=collision.impulse;DiagnosticOtherVelocity=collision.rigidbody?collision.rigidbody.linearVelocity:Vector3.zero;DiagnosticCollider=collision.collider.name;}
            DiagnosticContact(collision);
            if(collision.contactCount==0)return;
            var contact=collision.GetContact(0);
            float closing=Mathf.Abs(Vector3.Dot(collision.relativeVelocity,contact.normal));
            RecordHapticImpact(closing);
            if(collision.rigidbody==null&&Vector3.Dot(contact.normal,Vector3.up)>.65f)
            {ApplyLanding(closing,contact.point);return;}
            if(Time.time-incidentTime<.35f||closing<1.5f)return;
            incidentTime=Time.time;
            var traffic=collision.collider.GetComponentInParent<TrafficCar>();
            var other=collision.collider.GetComponentInParent<VehicleController>();
            var tree=collision.collider.GetComponentInParent<TreeImpact>();
            var sign=collision.collider.GetComponentInParent<RoadSignImpact>();
            float otherMass=traffic?traffic.CollisionMassKg:collision.rigidbody&&!collision.rigidbody.isKinematic?collision.rigidbody.mass:float.PositiveInfinity;
            float scale=1;
            if(sign)
            {
                otherMass=RoadSignImpact.EffectiveMassKg;
                if(sign.Strike(Body.mass,closing,incomingVelocity))
                {
                    float energy=.5f*Body.mass*closing*closing;
                    float retained=Mathf.Sqrt(Mathf.Clamp01(1-RoadSignImpact.YieldEnergyJoules/Mathf.Max(1,energy)));
                    Body.linearVelocity=incomingVelocity*Mathf.Clamp(retained,.25f,.99f);
                }
            }
            if(tree)
            {
                otherMass=tree.EffectiveMassKg;scale=tree.Strike(Body.mass,closing,incomingVelocity);
                if(tree.Broken)
                {
                    float energy=.5f*Body.mass*closing*closing;
                    float retained=Mathf.Sqrt(Mathf.Clamp01(1-VehicleImpactRules.TreeBreakEnergy(tree.RadiusMetres)/Mathf.Max(1,energy)));
                    Body.linearVelocity=incomingVelocity*Mathf.Clamp(retained,.25f,.98f);
                }
            }
            Vector3 localNormal=transform.InverseTransformDirection(contact.normal);
            float vulnerability=Mathf.Abs(localNormal.y)>.6f?1.4f:Mathf.Abs(localNormal.x)>.7f?1.15f:1;
            float tyreProtection=contact.thisCollider is MeshCollider? .35f:1;
            float amount=VehicleImpactRules.CollisionDamage(closing,Body.mass,otherMass,StructuralStrength,vulnerability)*scale*tyreProtection;
            LastImpactWasPolice=traffic&&traffic.Police;LastImpactObject=collision.collider.name;
            if(traffic&&!other)traffic.Hit(contact.point,VehicleImpactRules.CollisionDamage(closing,otherMass,Body.mass,1));
            ApplyImpactDamage(amount,contact.point,contact.normal,closing);
        }
    }
}
