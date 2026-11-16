using System;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace RoadRush.RouteProof3
{
    /// <summary>
    /// Main-thread observation of an actual factory-created, tuned and supported-rest
    /// vehicle. It never changes mesh readability, pose, physics, upgrades or source.
    /// Capturing shared metadata alone is deliberately insufficient: active rendered
    /// mesh vertices and real collider transforms are measured from this instance.
    /// Root-local axes are Unity car X-right/Y-up/Z-forward, in metres.
    /// </summary>
    public static class ActualRouteCarShape
    {
        private const int MaximumMeasuredVertices = 1_000_000;
        private const int CancellationStrideVertices = 1024;
        /// <summary>
        /// Serializable physical/render snapshot bound to the exact final ground and
        /// source capability. This is evidence, not a vehicle geometry or admission API.
        /// Body mass is kg; inertia tensor is kg m²; all position/size values are metres.
        /// </summary>
        [Serializable]
        public sealed class Snapshot
        {
            public string recipe, sourceDigest, finalBufferDigest, vehicleRecipe;
            public int model, layer, activeMeshCount, measuredVertices;
            public bool importedAppearance, physicsKinematic, useGravity;
            public float massKg, configuredWheelRadiusMetres, wheelbaseMetres, trackMetres;
            public float contactOffsetMetres, linearDampingPerSecond, angularDampingPerSecond;
            public string collisionDetectionMode, rigidbodyConstraints;
            public Vector3 rootWorldPosition, rigidbodyWorldPosition, rootScale, colliderCentreLocal, colliderSizeLocal;
            public Quaternion rootWorldRotation;
            public Vector3 centreOfMassLocal, inertiaTensorKgMetresSquared;
            public Quaternion inertiaTensorRotation;
            public Vector3 activeVisualMinimumLocal, activeVisualMaximumLocal;
            public Vector3[] actualColliderCornersRootLocal;
            public Vector3[] suspensionAnchorsRootLocal, visualWheelCentresRootLocal;
            public bool[] actualWheelGrounded;
            public Vector3[] actualContactPoints, actualContactNormals;
        }

        /// <summary>
        /// Must run on the Unity thread after production ApplyTuning and supported-rest
        /// initialization. Reads bounded arrays only; cancellation throws with no
        /// partial returned snapshot. Unreadable/absent active meshes are explicit
        /// refusals rather than inferred dimensions from the tuning table.
        /// </summary>
        public static Snapshot Capture(VehicleController vehicle, string sourceDigest, string finalBufferDigest, CancellationToken cancellation = default)
        {
            if (!vehicle || !vehicle.Body || !vehicle.Visual || vehicle.Visual.gameObject != vehicle.gameObject)
                throw new InvalidOperationException("Actual player-owned body/visual root required");
            if (vehicle.transform.lossyScale != Vector3.one || vehicle.transform.localScale != Vector3.one)
                throw new InvalidOperationException("Do not calibrate by scaling a physics root");
            if (sourceDigest == null || sourceDigest.Length != 64 || finalBufferDigest == null || finalBufferDigest.Length != 64)
                throw new ArgumentException("Actual source/final ground identities required");
            var box = vehicle.GetComponent<BoxCollider>();
            if (!box || !box.enabled || box.isTrigger || !vehicle.Visual.ImportedAppearance)
                throw new InvalidOperationException("Actual imported stock car and physical factory box required");
            var snapshot = new Snapshot
            {
                recipe = "ActualFactoryCarShape/1.1",
                sourceDigest = sourceDigest,
                finalBufferDigest = finalBufferDigest,
                vehicleRecipe = Core.VehicleDimensions.Recipe,
                model = vehicle.Metrics.Model,
                layer = vehicle.gameObject.layer,
                importedAppearance = vehicle.Visual.ImportedAppearance,
                physicsKinematic = vehicle.Body.isKinematic,
                useGravity = vehicle.Body.useGravity,
                massKg = vehicle.Body.mass,
                contactOffsetMetres = box.contactOffset,
                linearDampingPerSecond = vehicle.Body.linearDamping,
                angularDampingPerSecond = vehicle.Body.angularDamping,
                collisionDetectionMode = vehicle.Body.collisionDetectionMode.ToString(),
                rigidbodyConstraints = vehicle.Body.constraints.ToString(),
                configuredWheelRadiusMetres = vehicle.Metrics.Radius,
                wheelbaseMetres = vehicle.Metrics.Wheelbase,
                trackMetres = vehicle.Metrics.Track,
                rootWorldPosition = vehicle.transform.position,
                rigidbodyWorldPosition = vehicle.Body.position,
                rootWorldRotation = vehicle.transform.rotation,
                rootScale = vehicle.transform.localScale,
                colliderCentreLocal = box.center,
                colliderSizeLocal = box.size,
                centreOfMassLocal = vehicle.Body.centerOfMass,
                inertiaTensorKgMetresSquared = vehicle.Body.inertiaTensor,
                inertiaTensorRotation = vehicle.Body.inertiaTensorRotation,
                actualWheelGrounded = (bool[])vehicle.WheelGrounded.Clone(),
                actualContactPoints = (Vector3[])vehicle.ContactPoints.Clone(),
                actualContactNormals = (Vector3[])vehicle.ContactNormals.Clone(),
                suspensionAnchorsRootLocal = Enumerable.Range(0, 4).Select(i => VehicleGeometry.Anchor(vehicle.Metrics.Model, i)).ToArray(),
                visualWheelCentresRootLocal = vehicle.Visual.Wheels.Select(w => vehicle.transform.InverseTransformPoint(w.position)).ToArray()
            };
            snapshot.actualColliderCornersRootLocal = new Vector3[8];
            for (int index = 0; index < 8; index++)
            {
                var sign = new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1);
                var local = box.center + Vector3.Scale(sign, box.size * 0.5f);
                snapshot.actualColliderCornersRootLocal[index] = vehicle.transform.InverseTransformPoint(box.transform.TransformPoint(local));
            }

            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            foreach (var filter in vehicle.Visual.GetComponentsInChildren<MeshFilter>(true))
            {
                cancellation.ThrowIfCancellationRequested();
                var renderer = filter.GetComponent<Renderer>();
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                var mesh = filter.sharedMesh;
                if (!mesh || !mesh.isReadable)
                    throw new InvalidOperationException("Active visual mesh cannot be measured honestly");
                if (snapshot.measuredVertices + mesh.vertexCount > MaximumMeasuredVertices)
                    throw new InvalidOperationException("Actual shape measurement vertex cap exceeded");
                var vertices = mesh.vertices; // Owned read copy; never edit the shared mesh.
                for (int index = 0; index < vertices.Length; index++)
                {
                    if (index % CancellationStrideVertices == 0)
                        cancellation.ThrowIfCancellationRequested();
                    Vector3 point = vehicle.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[index]));
                    if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                        throw new InvalidOperationException("Nonfinite actual visual vertex");
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }

                snapshot.activeMeshCount++;
                snapshot.measuredVertices += vertices.Length;
            }

            if (snapshot.measuredVertices == 0 || snapshot.visualWheelCentresRootLocal.Length != 4)
                throw new InvalidOperationException("Complete active visual/wheel measurement required");
            snapshot.activeVisualMinimumLocal = minimum;
            snapshot.activeVisualMaximumLocal = maximum;
            return snapshot;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
