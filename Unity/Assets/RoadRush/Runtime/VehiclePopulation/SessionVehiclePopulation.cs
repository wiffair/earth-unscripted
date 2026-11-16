using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace RoadRush.VehiclePopulation
{
    /// <summary>When the body was sampled relative to Unity's ordinary physics loop.</summary>
    public enum VehicleSamplePhase { BeforePhysicsSolve, AfterActorFrame }

    /// <summary>
    /// Actual box geometry in the vehicle root's local metre frame. This is physical
    /// collider data, not an inferred vehicle size or a pedestrian-clearance grant.
    /// Matrix values and vectors are returned by value; no native handles escape.
    /// </summary>
    public readonly struct VehicleBoxSample
    {
        public readonly Vector3 CentreMetres;
        public readonly Vector3 SizeMetres;
        public readonly Matrix4x4 BoxToRoot;
        public readonly bool ActiveForPhysics;

        // Convex tyre meshes expose a conservative local bounds envelope. The
        // flag distinguishes it from an exact box; no native mesh handle escapes.
        public readonly bool ConvexMeshEnvelope;
        internal VehicleBoxSample(Collider collider, Transform root)
        {
            ConvexMeshEnvelope = collider is MeshCollider;
            var bounds=collider is BoxCollider box?new Bounds(box.center,box.size):((MeshCollider)collider).sharedMesh.bounds;
            CentreMetres = bounds.center;
            SizeMetres = bounds.size;
            BoxToRoot = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
            ActiveForPhysics = collider.enabled && collider.gameObject.activeInHierarchy;
        }
    }

    /// <summary>Detached actual vehicle values. Role is descriptive, never write authority.</summary>
    public sealed class ActualVehicleSample
    {
        public readonly string RuntimeHandle;
        public readonly string Role;
        public readonly int Model;
        public readonly int ShapeRevision;
        public readonly int PoseDiscontinuityRevision;
        public readonly Vector3 BodyPositionMetres;
        public readonly Quaternion BodyRotation;
        public readonly Vector3 VelocityMetresPerSecond;
        public readonly Vector3 AngularVelocityRadiansPerSecond;
        public readonly Matrix4x4 VisibleRootToWorld;
        public readonly bool Kinematic;
        public readonly float MassKilograms;
        public readonly bool HasVehicleController;
        // The following controller values are unavailable (zero), not defaults,
        // when HasVehicleController is false for a kinematic TrafficCar.
        public readonly float ControllerPowerMultiplier;
        public readonly float ControllerSpeedLimitMetresPerSecond;
        public readonly float ControllerBrakeMultiplier;
        public readonly float CurrentGripMultiplier;
        public readonly float SurfaceWetness;
        public readonly Vector3 ReportedReplicaVelocityMetresPerSecond;
        public readonly ReadOnlyCollection<VehicleBoxSample> Boxes;
        /// <summary>Nullable actual observation; consumer must ask its actual owner to validate the private witness.</summary>
        public readonly RoadRush.FunnyAnimals.ActualRegistryConsumer.VehiclePresentationData Presentation;
        public readonly string AppearanceUnavailableReason;
        internal readonly object AppearanceWitness;

        internal ActualVehicleSample(ActualVehicleRosterDomain.Member member, Rigidbody body,
            CarVisual visual, VehicleBoxSample[] boxes,
            RoadRush.FunnyAnimals.ActualRegistryConsumer.VehiclePresentationData presentation,
            object appearanceWitness, string appearanceUnavailableReason)
        {
            Presentation = presentation;
            AppearanceWitness = appearanceWitness;
            AppearanceUnavailableReason = appearanceUnavailableReason;
            RuntimeHandle = member.RuntimeHandle;
            Role = member.VehicleRole.ToString();
            Model = visual.Model;
            ShapeRevision = member.ShapeRevision;
            PoseDiscontinuityRevision = member.PoseRevision;
            BodyPositionMetres = body.position;
            BodyRotation = body.rotation;
            VelocityMetresPerSecond = body.linearVelocity;
            AngularVelocityRadiansPerSecond = body.angularVelocity;
            VisibleRootToWorld = body.transform.localToWorldMatrix;
            Kinematic = body.isKinematic;
            MassKilograms = body.mass;
            var controller = body.GetComponent<VehicleController>();
            HasVehicleController = controller;
            ControllerPowerMultiplier = controller ? controller.PowerFactor : 0;
            ControllerSpeedLimitMetresPerSecond = controller ? controller.SpeedLimit : 0;
            ControllerBrakeMultiplier = controller ? controller.BrakeFactor : 0;
            CurrentGripMultiplier = controller ? controller.Grip : 0;
            SurfaceWetness = controller ? controller.SurfaceWetness : 0;
            ReportedReplicaVelocityMetresPerSecond = controller ? controller.ReplicaVelocity : Vector3.zero;
            Boxes = Array.AsReadOnly((VehicleBoxSample[])boxes.Clone());
        }
    }

    /// <summary>
    /// A transient exact population read, not a source/provider or future-motion
    /// capability. Scenery must check IsCurrent immediately before committing work.
    /// </summary>
    public sealed class ActualVehiclePopulationSnapshot
    {
        readonly SessionVehiclePopulation owner;
        internal readonly long RosterRevision;
        internal readonly long SampleSequence;
        public readonly int Frame;
        public readonly float FixedTimeSeconds;
        public readonly VehicleSamplePhase Phase;
        public readonly string GroundRuntimeHandle;
        public readonly string SourceDigest;
        public readonly string PhysicalDigest;
        public readonly string TownRuntimeHandle;
        public readonly ReadOnlyCollection<ActualVehicleSample> Vehicles;

        internal ActualVehiclePopulationSnapshot(SessionVehiclePopulation owner, long rosterRevision,
            long sequence, VehicleSamplePhase phase, GeoWorld.VehicleTownContext800 context,
            ActualVehicleSample[] samples)
        {
            this.owner = owner;
            RosterRevision = rosterRevision;
            SampleSequence = sequence;
            Frame = Time.frameCount;
            FixedTimeSeconds = Time.fixedTime;
            Phase = phase;
            GroundRuntimeHandle = context.GroundRuntimeHandle;
            SourceDigest = context.SourceDigest;
            PhysicalDigest = context.PhysicalDigest;
            TownRuntimeHandle = context.RuntimeHandle;
            Vehicles = Array.AsReadOnly((ActualVehicleSample[])samples.Clone());
        }

        public bool IsCurrent => owner.IsSnapshotCurrent(this);
    }

    /// <summary>
    /// Session-owned persistent roster. Construction/mutation is INTERNAL; consumers
    /// obtain it only from GameSession.ActualVehicles. All methods are main-thread.
    /// It remains UNBOUND without a real live scoped ground AND town context.
    /// </summary>
    public sealed partial class SessionVehiclePopulation
    {
        internal const int MaximumColliderParts = 12; // Current stock:1; F1 compound:5.
        // Publication fences only; they never clamp/change a real vehicle.
        // Local driving worlds are kilometres across, not planet-centred coordinates.
        const float MaximumWorldCoordinateMetres = 1000000;
        const float MaximumBoxSizeMetres = 100;
        readonly GameSession session;
        readonly int mainThread;
        readonly ActualVehicleRosterDomain roster;
        readonly Dictionary<GameObject, Collider[]> knownColliders = new Dictionary<GameObject, Collider[]>();
        GeoWorld.VehicleTownContext800 world;
        WorldBuilder boundSessionWorld;
        UnityEngine.SceneManagement.Scene boundSessionScene;
        readonly Dictionary<GameObject, int> pendingLayoutFrames = new Dictionary<GameObject, int>();
        readonly Dictionary<GameObject, object> stagedOwners = new Dictionary<GameObject, object>();
        readonly List<string> layoutDiagnostics = new List<string>();
        const int MaximumLayoutDiagnostics = 128;
        const int MaximumDiagnosticColliders = 64;
        long sampleSequence;
        bool revoked;
        ActualVehiclePopulationSnapshot latest;

        internal SessionVehiclePopulation(GameSession session, bool actualCircuitWorld)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            mainThread = Thread.CurrentThread.ManagedThreadId;
            roster = new ActualVehicleRosterDomain(actualCircuitWorld);
        }

        internal GameSession Session => session;
        internal ActualVehicleRosterDomain Domain => roster;
        public string UnboundReason => revoked ? "WorldRevoked" : world == null || !world.IsCurrent
            ? "ActualGroundTownBindingUnavailable" : roster.UnboundReason;

        void MainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Actual vehicle population requires its session main thread.");
        }

        internal void BindActualWorld(GeoWorld.VehicleTownContext800 context)
        {
            MainThread();
            if (context == null || !context.IsCurrent || !session.World ||
                !ReferenceEquals(context.World, session.World.GeographicWorld))
                throw new InvalidOperationException("Actual current session ground/town context required.");
            world = context;
            boundSessionWorld = session.World;
            boundSessionScene = session.gameObject.scene;
            latest = null;
            sampleSequence++;
        }

        /// <summary>
        /// Read-only binding predicate for the trusted Scenery consumer. A match
        /// associates the genuine publication with this session's captured physical
        /// Town context; it does not assert roster freshness or closed building
        /// volumes. Require a current captured snapshot and fresh publication
        /// semantic validation separately. Null, revoked or different lifetimes
        /// return false; calls from another thread are rejected.
        /// </summary>
        internal bool MatchesTownPublication800(TownSemanticPublication800 publication)
        {
            MainThread();
            return !revoked && world != null && CurrentSessionWorld() &&
                world.MatchesTownPublication800(publication);
        }

        internal void RegisterActual(ActualVehicleRosterDomain.FactoryTicket ticket)
        {
            MainThread();
            var root = ticket.ObservedRoot as GameObject;
            // Record actual physical factory provenance NOW. Disabled decorative
            // colliders queued for end-of-frame destruction are never accepted as
            // final geometry: this identity remains layout-pending until strict validation.
            ValidateRoot(root, ticket.VehicleRole, true, out _, out _, out _, false);
            object actualStage = session.CaptureActualVehicleStage(root);
            try
            {
                ticket.CommitObservedRoot();
                pendingLayoutFrames[root] = Time.frameCount;
                stagedOwners[root] = actualStage;
                RecordLayoutDiagnostic(root, ticket.VehicleRole, "factory-pending");
            }
            catch
            {
                if (roster.Find(root) == null) knownColliders.Remove(root);
                throw;
            }
        }

        /// <summary>Actual activated private stage receipt, never a caller bool.</summary>
        internal void ValidateAndSealActualBoot(object activatedStage)
        {
            MainThread();
            foreach (var member in roster.CopyMembers())
            {
                var root = member.Root as GameObject;
                if (!root || !stagedOwners.TryGetValue(root, out var actualStage) ||
                    !ReferenceEquals(actualStage, activatedStage))
                    throw new InvalidOperationException("Registered boot root has no matching actual stage provenance.");
                session.RequireActivatedActualVehicleStage(root, actualStage);
                ValidateRoot(root, member.VehicleRole, true, out _, out _, out _);
                roster.ConfirmPhysicalLayout(root);
                pendingLayoutFrames.Remove(root);
                RecordLayoutDiagnostic(root, member.VehicleRole, "activated-final");
            }
            roster.SealActualBootRoster();
            stagedOwners.Clear();
        }

        void RecordLayoutDiagnostic(GameObject root, ActualVehicleRosterDomain.Role role, string phase)
        {
            if (!root || layoutDiagnostics.Count >= MaximumLayoutDiagnostics) return;
            var body = root.GetComponent<Rigidbody>();
            var observed = root.GetComponentsInChildren<Collider>(true);
            string summary = phase + " count=" + observed.Length + " role=" + role + " root=" + root.name + " frame=" + Time.frameCount +
                " colliders=[" + string.Join(",", observed.Take(MaximumDiagnosticColliders).Select(collider =>
                    collider.gameObject.name + ":" + collider.GetType().Name + ":enabled=" + collider.enabled +
                    ":attached=" + (collider.attachedRigidbody == body))) + "]";
            layoutDiagnostics.Add(summary);
            if (!string.IsNullOrEmpty(PlayerProfile.StorageOverride)) Debug.Log("VEHICLE LAYOUT " + summary);
        }

        /// <summary>Detached diagnostic strings only, not a readiness or geometry grant.</summary>
        internal string[] CopyLayoutDiagnostics() => layoutDiagnostics.ToArray();

        void ValidateDeferredLayouts()
        {
            if(pendingLayoutFrames.Count==0)return;
            var members=roster.OrderedMembers;
            for(int index=0;index<members.Count;index++)
            {
                var member=members[index];
                var root = member.Root as GameObject;
                if (!member.LayoutPending || !root || !pendingLayoutFrames.TryGetValue(root, out int frame) ||
                    Time.frameCount <= frame || member.Retiring) continue;
                ValidateRoot(root, member.VehicleRole, true, out _, out _, out _);
                roster.ConfirmPhysicalLayout(root);
                pendingLayoutFrames.Remove(root);
            }
        }

        internal void MarkShape(GameObject root)
        {
            MainThread();
            if (roster.Find(root) != null)
            {
                roster.InvalidateShape(root);
                pendingLayoutFrames[root] = Time.frameCount;
            }
            latest = null;
        }

        internal void MarkPose(GameObject root)
        {
            MainThread();
            roster.InvalidatePose(root);
            latest = null;
        }

        internal void BeginRetirement(GameObject root)
        {
            MainThread();
            RetireAppearance(root);
            roster.BeginRetirement(root);
            latest = null;
        }

        internal void ObserveInactiveOrDestroyed(GameObject root)
        {
            MainThread();
            if (revoked) return;
            // Unity fake-null is intentionally accepted only as actual destruction.
            if (!root || !root.activeInHierarchy)
            {
                RetireAppearance(root);
                roster.ConfirmActualRetirement(root);
                knownColliders.Remove(root);
                pendingLayoutFrames.Remove(root);
                stagedOwners.Remove(root);
                latest = null;
            }
        }

        internal void ObservePresenceDisabled(GameObject root)
        {
            MainThread();
            if (revoked) return;
            if (root && root.activeInHierarchy)
                roster.Poison("Vehicle lifetime watcher disabled while its root is still active.");
            else ObserveInactiveOrDestroyed(root);
        }

        internal void Revoke()
        {
            MainThread();
            RevokeAppearance();
            revoked = true;
            roster.Revoke();
            latest = null;
            knownColliders.Clear();
            pendingLayoutFrames.Clear();
            stagedOwners.Clear();
        }

        void ValidateRoot(GameObject root, ActualVehicleRosterDomain.Role role, bool permitShapeChange,
            out Rigidbody body, out CarVisual visual, out VehicleBoxSample[] boxes,
            bool requireFinalLayout = true)
        {
            if (!root || root.scene != session.gameObject.scene || root.transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("Actual session vehicle root/frame unavailable.");
            body = root.GetComponent<Rigidbody>();
            visual = root.GetComponent<CarVisual>();
            var controller = root.GetComponent<VehicleController>();
            var marker = root.GetComponent<TrafficCar>();
            if (!body || !visual || visual.Model < 0 || visual.Model >= Core.GarageRules.Catalogue.Length ||
                controller && controller.Metrics.Model != visual.Model ||
                !controller && !marker)
                throw new InvalidOperationException("Actual body/model/controller binding is incomplete.");
            if (!Finite(body.position) || !Finite(body.linearVelocity) || !Finite(body.angularVelocity) ||
                !Finite(new Vector3(body.rotation.x, body.rotation.y, body.rotation.z)) ||
                !Finite(body.rotation.w) || !Finite(body.mass) || body.mass <= 0)
                throw new InvalidOperationException("Nonfinite actual vehicle pose/velocity.");
            if (Mathf.Max(Mathf.Abs(body.position.x), Mathf.Abs(body.position.y),
                Mathf.Abs(body.position.z)) > MaximumWorldCoordinateMetres)
                throw new InvalidOperationException("Vehicle exceeds bounded local-world snapshot coordinates.");
            if ((role == ActualVehicleRosterDomain.Role.Player || role == ActualVehicleRosterDomain.Role.RaceOpponent ||
                 role == ActualVehicleRosterDomain.Role.LanPeer) && !controller)
                throw new InvalidOperationException("This actual factory role requires its production controller.");
            if (role == ActualVehicleRosterDomain.Role.Traffic && (!marker || marker.Police))
                throw new InvalidOperationException("Ordinary traffic cannot masquerade as a patrol/rootless actor.");
            if (role == ActualVehicleRosterDomain.Role.Police &&
                (!marker || !marker.Police || !root.GetComponent<PoliceDriver>()))
                throw new InvalidOperationException("Patrol root must have the actual shared police components.");
            var physicalLayout = root.GetComponent<ActualVehiclePhysicalLayout>();
            if (!physicalLayout) throw new InvalidOperationException("Actual physical factory layout provenance missing.");
            var physical = requireFinalLayout ? physicalLayout.ReadValidatedBoxes(body, visual.Model) :
                physicalLayout.ReadConstructingBoxes(body, visual.Model);
            var observed = root.GetComponentsInChildren<Collider>(true);
            if (requireFinalLayout)
            {
                if (observed.Length == 0 || observed.Length > MaximumColliderParts || observed.Length != physical.Length ||
                    observed.Any(collider => !(collider is BoxCollider) && !(collider is MeshCollider mesh && mesh.convex && mesh.sharedMesh) || !physical.Contains(collider)))
                    throw new InvalidOperationException("Unknown or incomplete FINAL actual vehicle collider set: " + root.name);
            }
            else
            {
                // Only known physical references may be active, even during real
                // construction. Unknown disabled components are PENDING, not accepted.
                var active = observed.Where(collider => collider.enabled && collider.gameObject.activeInHierarchy).ToArray();
                var expectedActive = physical.Where(collider => collider.enabled && collider.gameObject.activeInHierarchy).ToArray();
                if (active.Length != expectedActive.Length || active.Any(collider => !expectedActive.Contains(collider)))
                    throw new InvalidOperationException("Unknown active collider during actual vehicle construction.");
            }
            Collider[] colliders = requireFinalLayout ? observed : physical.Cast<Collider>().ToArray();
            foreach(var collider in colliders)
            {
                var sample=new VehicleBoxSample(collider,root.transform);
                if(!Finite(sample.CentreMetres)||!Finite(sample.SizeMetres)||sample.SizeMetres.x<=0||sample.SizeMetres.y<=0||sample.SizeMetres.z<=0||Mathf.Max(sample.SizeMetres.x,sample.SizeMetres.y,sample.SizeMetres.z)>MaximumBoxSizeMetres)
                    throw new InvalidOperationException("Nonfinite/degenerate actual vehicle shape.");
            }
            int activeBoxes = colliders.Count(collider => collider.enabled && collider.gameObject.activeInHierarchy);
            int requiredBoxes = (visual.Model == 3 ? 5 : 1)+physicalLayout.ActiveSupplementCount;
            if (activeBoxes != requiredBoxes)
                throw new InvalidOperationException("Actual current model compound collider layout is incomplete.");
            if (!permitShapeChange && knownColliders.TryGetValue(root, out var previous) &&
                !previous.SequenceEqual(colliders))
                throw new InvalidOperationException("Vehicle collider set changed outside a structural mutation.");
            if (permitShapeChange && requireFinalLayout) knownColliders[root] = (Collider[])colliders.Clone();
            boxes = colliders.Select(collider =>
                new VehicleBoxSample(collider, root.transform)).ToArray();
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        /// <summary>
        /// Internal phase sampler. Physics samples are pre-solve; frame samples run
        /// after ordinary actor updates. No future trajectory/collision claim is made.
        /// </summary>
        internal void Sample(VehicleSamplePhase phase)
        {
            MainThread();
            latest = null;
            if (revoked) return;
            // Destroy is deferred. Its callback can occur before Unity fake-null
            // settles, so confirm retirement again from actual later lifecycle state.
            var currentMembers=roster.OrderedMembers;
            for(int index=0;index<currentMembers.Count;index++)
            {
                var member=currentMembers[index];
                if (!(member.Root as GameObject))
                {
                    roster.ConfirmActualRetirement(member.Root);
                    knownColliders.Remove((GameObject)member.Root);
                }
            }
            try
            {
                ValidateDeferredLayouts(); // Structural proof only; never publishes a world-less snapshot.
            }
            catch (Exception exception)
            {
                roster.Poison(exception.Message);
                return;
            }
            if (revoked || world == null || !world.IsCurrent || !CurrentSessionWorld() ||
                !roster.CanSampleActualMembers || roster.CopyMembers().Any(member => member.LayoutPending)) return;
            try
            {
                var result = new List<ActualVehicleSample>();
                var actualMembers = roster.CopyMembers();
                if (phase == VehicleSamplePhase.AfterActorFrame)
                {
                    try { PrepareAppearanceFrame(actualMembers); }
                    catch { PoisonAppearanceDomain(); } // Appearance failure preserves physical rows.
                }
                foreach (var member in actualMembers)
                {
                    var root = (GameObject)member.Root;
                    ValidateRoot(root, member.VehicleRole, member.ShapePending,
                        out var body, out var visual, out var boxes);
                    if (!root.activeInHierarchy)
                        throw new InvalidOperationException("An unretired vehicle became inactive.");
                    roster.ConfirmFreshActualSample(root);
                    RoadRush.FunnyAnimals.ActualRegistryConsumer.VehiclePresentationData presentation = null;
                    object appearanceWitness = null;
                    string appearanceReason = "AfterActorFrame appearance observation required";
                    if (phase == VehicleSamplePhase.AfterActorFrame)
                        CaptureAppearance(root, visual, out presentation, out appearanceWitness, out appearanceReason);
                    result.Add(new ActualVehicleSample(member, body, visual, boxes,
                        presentation, appearanceWitness, appearanceReason));
                }
                if (roster.IsComplete)
                    latest = new ActualVehiclePopulationSnapshot(this, roster.Revision,
                        ++sampleSequence, phase, world, result.ToArray());
            }
            catch (Exception exception)
            {
                roster.Poison(exception.Message);
            }
        }

        public bool TryCapture(out ActualVehiclePopulationSnapshot snapshot, out string reason)
        {
            MainThread();
            snapshot = latest;
            if (snapshot == null || !IsSnapshotCurrent(snapshot))
            {
                snapshot = null;
                reason = UnboundReason.Length == 0 ? "FreshPhaseSampleUnavailable" : UnboundReason;
                return false;
            }
            reason = "";
            return true;
        }

        bool CurrentSessionWorld() => session && boundSessionWorld &&
            session.gameObject.scene == boundSessionScene && boundSessionWorld.gameObject.scene == boundSessionScene &&
            ReferenceEquals(session.World, boundSessionWorld) &&
            ReferenceEquals(boundSessionWorld.GeographicWorld, world.World);

        internal bool IsSnapshotCurrent(ActualVehiclePopulationSnapshot snapshot)
        {
            MainThread();
            bool current = !revoked && world != null && world.IsCurrent && CurrentSessionWorld() && roster.IsComplete &&
                ReferenceEquals(latest, snapshot) && snapshot.RosterRevision == roster.Revision &&
                snapshot.SampleSequence == sampleSequence && snapshot.Frame == Time.frameCount &&
                snapshot.FixedTimeSeconds == Time.fixedTime;
            if (!current) return false;
            // A pre-solve sample must not remain current after physics moved a body
            // within the same rendered frame/fixed timestamp.
            var members = roster.CopyMembers();
            for (int index = 0; index < members.Length; index++)
            {
                var root = members[index].Root as GameObject;
                if (!root || root.scene != session.gameObject.scene) return false;
                var body = root.GetComponent<Rigidbody>();
                var sample = snapshot.Vehicles[index];
                var visual = root.GetComponent<CarVisual>();
                if (!body || !visual || visual.Model != sample.Model ||
                    !body.position.Equals(sample.BodyPositionMetres) ||
                    !body.rotation.Equals(sample.BodyRotation) ||
                    !body.linearVelocity.Equals(sample.VelocityMetresPerSecond) ||
                    !body.angularVelocity.Equals(sample.AngularVelocityRadiansPerSecond) ||
                    !body.transform.localToWorldMatrix.Equals(sample.VisibleRootToWorld))
                    return false;
                if (!root.activeInHierarchy || body.isKinematic != sample.Kinematic ||
                    !body.mass.Equals(sample.MassKilograms) ||
                    !knownColliders.TryGetValue(root, out var colliders) ||
                    colliders.Length != sample.Boxes.Count)
                    return false;
                // Exact registered-root subtree inspection is permitted; it is not
                // scene discovery or a completeness claim based on a global search.
                if (!colliders.SequenceEqual(root.GetComponentsInChildren<Collider>(true)))
                    return false;
                var controller = root.GetComponent<VehicleController>();
                if ((bool)controller != sample.HasVehicleController || controller &&
                    (!controller.PowerFactor.Equals(sample.ControllerPowerMultiplier) ||
                     !controller.SpeedLimit.Equals(sample.ControllerSpeedLimitMetresPerSecond) ||
                     !controller.BrakeFactor.Equals(sample.ControllerBrakeMultiplier) ||
                     !controller.Grip.Equals(sample.CurrentGripMultiplier) ||
                     !controller.SurfaceWetness.Equals(sample.SurfaceWetness) ||
                     !controller.ReplicaVelocity.Equals(sample.ReportedReplicaVelocityMetresPerSecond)))
                    return false;
                for (int boxIndex = 0; boxIndex < colliders.Length; boxIndex++)
                {
                    var box = colliders[boxIndex] as BoxCollider;
                    if (!box || box.isTrigger || box.GetComponentInParent<Rigidbody>(true) != body) return false;
                    var attachedBody = box.attachedRigidbody;
                    bool activeForPhysics = box.enabled && box.gameObject.activeInHierarchy;
                    if (activeForPhysics ? attachedBody != body : attachedBody && attachedBody != body)
                        return false;
                    var before = sample.Boxes[boxIndex];
                    if (!box.center.Equals(before.CentreMetres) || !box.size.Equals(before.SizeMetres) ||
                        !(root.transform.worldToLocalMatrix * box.transform.localToWorldMatrix).Equals(before.BoxToRoot) ||
                        (box.enabled && box.gameObject.activeInHierarchy) != before.ActiveForPhysics)
                        return false;
                }
            }
            return true;
        }
    }
}
