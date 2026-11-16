using UnityEngine;
using RoadRush.Core;

namespace RoadRush
{
    /// <summary>Four real convex tyre cylinders for obstacle contact. Suspension
    /// queries use the rounded radial envelope; physics resolves the actual tyre
    /// width, so wheel clearance does not become a full-width chassis wall.</summary>
    public sealed class VehicleObstacleWheels : MonoBehaviour
    {
        readonly Transform[] wheels = new Transform[4];
        Mesh tyreMesh;
        PhysicsMaterial rubber;
        readonly RaycastHit[] obstacleHits = new RaycastHit[16];
        readonly RaycastHit[] topHits = new RaycastHit[16];
        public int TractionApplications { get; private set; }
        public static VehicleObstacleWheels Build(VehicleController car, ImportedVehicleSettings settings)
        {
            // Tuning can change without a visual rebuild (for example upgrades).
            // Retire the previous physical set before recording its replacement.
            var previous=car.transform.Find("Physical obstacle wheels");
            if(previous)
            {
                foreach(var collider in previous.GetComponentsInChildren<Collider>())collider.enabled=false;
                previous.SetParent(null);Destroy(previous.gameObject);
            }
            if (settings == null || !settings.WheelObstacleContact) return null;
            var root=new GameObject("Physical obstacle wheels");root.transform.SetParent(car.transform,false);
            var contacts = root.AddComponent<VehicleObstacleWheels>();
            contacts.Create(car);return contacts;
        }
        void Create(VehicleController car)
        {
            const int segments=20;
            var metric=car.Metrics;
            var vertices=new Vector3[segments*2];var indices=new System.Collections.Generic.List<int>();
            float radius=metric.Radius*.975f;
            for(int side=0;side<2;side++)for(int index=0;index<segments;index++)
            {
                float angle=index*Mathf.PI*2/segments;
                vertices[side*segments+index]=new Vector3((side==0?-.5f:.5f)*metric.TyreWidth,Mathf.Sin(angle)*radius,Mathf.Cos(angle)*radius);
            }
            for(int index=0;index<segments;index++)
            {
                int next=(index+1)%segments;
                indices.AddRange(new[]{index,index+segments,next,next,index+segments,next+segments});
                if(index>0&&index<segments-1){indices.AddRange(new[]{0,index+1,index,segments,segments+index,segments+index+1});}
            }
            tyreMesh=new Mesh{name="Convex physical tyre cylinder",vertices=vertices,triangles=indices.ToArray()};tyreMesh.RecalculateBounds();
            rubber=new PhysicsMaterial("Obstacle tyre contact"){dynamicFriction=.25f,staticFriction=.3f,bounciness=.03f};
            for(int index=0;index<4;index++)
            {
                var wheel=new GameObject("Off-road tyre contact "+index);wheel.layer=car.gameObject.layer;wheel.transform.SetParent(transform,false);
                var anchor=metric.Anchor(index);wheel.transform.localPosition=new Vector3(anchor.x,metric.RestWheelLocalY(car.Body.mass),anchor.z);
                var collider=wheel.AddComponent<MeshCollider>();collider.sharedMesh=tyreMesh;collider.convex=true;collider.sharedMaterial=rubber;
                wheels[index]=wheel.transform;
                VehiclePopulation.ActualVehiclePhysicalLayout.RecordSupplemental(car.transform,collider);
            }
        }
        public void Follow(int index,float y,float steering)
        {
            if(!wheels[index])return;
            var point=wheels[index].localPosition;point.y=y;wheels[index].localPosition=point;
            wheels[index].localRotation=Quaternion.Euler(0,index%2==1?steering:0,0);
        }
        /// <summary>
        /// Approximate driven tyre torque at a climbable step. The normal motor
        /// force acts at ground contacts; a stationary convex tyre alone has no
        /// angular motor and cannot produce the upward friction of a driven tyre
        /// against a bonnet edge. Supply that bounded traction force here, only
        /// when a real nearby edge and its reachable top are both ray-confirmed.
        /// No pose changes; equal opposite force is applied to movable obstacles.
        /// </summary>
        public void ApplyObstacleTraction(VehicleController car, float power, float frontDriveFraction)
        {
            if (Mathf.Abs(power) < .1f || car.AbsoluteSpeed > 12) return;
            float radius = car.Metrics.Radius;
            Vector3 forward = car.transform.forward * Mathf.Sign(power);
            Vector3 up = car.transform.up;
            for (int index = 0; index < wheels.Length; index++)
            {
                float axleShare = index % 2 == 1 ? frontDriveFraction : 1 - frontDriveFraction;
                if (axleShare <= 0) continue;
                Vector3 centre = wheels[index].position;
                int count = Physics.RaycastNonAlloc(centre, forward, obstacleHits, radius + .14f, ~0, QueryTriggerInteraction.Ignore);
                RaycastHit edge = default;
                float nearest = float.PositiveInfinity;
                for (int hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    var hit = obstacleHits[hitIndex];
                    if (hit.rigidbody == car.Body || hit.collider.transform.IsChildOf(car.transform)
                        || Vector3.Dot(hit.normal, up) > .55f || hit.distance >= nearest) continue;
                    edge = hit; nearest = hit.distance;
                }
                if (float.IsInfinity(nearest)) continue;
                Vector3 probe = edge.point + forward * .10f + up * radius * 1.75f;
                count = Physics.RaycastNonAlloc(probe, -up, topHits, radius * 2.1f, ~0, QueryTriggerInteraction.Ignore);
                bool reachable = false;
                for(int topIndex=0;topIndex<count;topIndex++)
                {
                    var hit=topHits[topIndex];
                    if (hit.rigidbody == car.Body || Vector3.Dot(hit.normal, up) < .65f) continue;
                    if (hit.collider != edge.collider && (!edge.rigidbody || hit.rigidbody != edge.rigidbody)) continue;
                    float riseAboveCentre = Vector3.Dot(hit.point - centre, up);
                    if (riseAboveCentre > radius * .75f || riseAboveCentre < -radius * .2f) continue;
                    reachable = true; break;
                }
                if (!reachable) continue;
                float force = Mathf.Min(Mathf.Abs(power) * 4300 * car.PowerFactor * axleShare,
                    car.Body.mass * Physics.gravity.magnitude * .35f) * Mathf.Clamp01(car.Grip);
                Vector3 traction = up * force;
                car.Body.AddForceAtPosition(traction, centre);
                if (edge.rigidbody && !edge.rigidbody.isKinematic)
                    edge.rigidbody.AddForceAtPosition(-traction, edge.point);
                TractionApplications++;
            }
        }
        void OnDestroy(){if(tyreMesh)Destroy(tyreMesh);if(rubber)Destroy(rubber);}
    }
}
