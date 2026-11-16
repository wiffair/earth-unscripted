using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using EarthUnscripted.FunnyAnimals.Admission;
using EarthUnscripted.FunnyAnimals.VehicleCaptureProposal;

namespace EarthUnscripted.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Conservative XZ exclusion from an ACTUAL Town binding's copied enabled,
    /// nontrigger MeshCollider/BoxCollider inputs, including steps beyond raw
    /// source footprints. Source footprints, ground, actors and habitat remain
    /// separate mandatory checks. Clear means only disjoint copied input bounds;
    /// it is never a closed-volume, inside, door, physics or movement grant.
    /// </summary>
    /// <remarks>
    /// Main-thread lifecycle/query API. Preparation yields only while retaining
    /// immutable copied data and the actual binding; no borrowed native objects.
    /// The caller owns that binding and all scene/lease lifetimes. One shared
    /// instance per composite is intended; caps are per instance, not global.
    /// Direct construction cannot make preparation/currentness true.
    /// </remarks>
    public sealed class TownPhysicalObstacleExclusion800 : IDisposable
    {
        public const int MaximumQueriesPerFrame = 64;
        public const int MaximumComparisonsPerFrame = 32768;
        public const int MaximumPreparationOperationsPerFrame = 4096;
        // Match or tighten the real snapshot copy caps. Never accept a prefix.
        private const int MaximumEntries = 2500;
        private const int MaximumColliders = 8192;
        private const int MaximumVertices = 2000000;
        private const int MaximumIndices = 12000000;
        private const int MaximumSubmeshesPerCollider = 64;
        private readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        private TownSemanticSourceBinding800 binding;
        private TownSemanticSourceData800 snapshot;
        private IDisposable townSubscription, groundSubscription;
        private OccupiedBounds[] model;
        private long revision;
        private CancellationToken modelCancellation;
        private bool refused = true, preparing;
        private int lastPreparationAttemptFrame = -1;
        private int preparationBudgetFrame = -1, preparationOperations;
        private int queryBudgetFrame = -1, queries, comparisons;

        /// <summary>Staging only; InspectCapsule still validates actual currency.</summary>
        public bool PreparationCompleted => !refused && model != null;
        public TownSemanticSourceBinding800 Binding => binding;
        public int ColliderBoundsCount => model == null ? 0 : model.Length;
        public string Reason { get; private set; } = "Actual Town obstacle binding unprepared";

        /// <summary>
        /// Prepare all copied actual emitted collision inputs or refuse the whole
        /// model. Consume/dispose this iterator before starting another. Repeated
        /// attempts in one frame or while busy invalidate and refuse; no hidden
        /// worker or replacement native owner is created. No caller DTO factory.
        /// </summary>
        public IEnumerator Prepare(TownSemanticSourceBinding800 actualBinding,
            CancellationToken cancellation = default)
        {
            RequireMainThread();
            if (preparing || lastPreparationAttemptFrame == Time.frameCount)
            {
                Invalidate("Town obstacle preparation busy or already attempted this frame");
                yield break;
            }
            lastPreparationAttemptFrame = Time.frameCount;
            Dispose();
            if (actualBinding == null)
                throw new ArgumentException("Actual TownSemanticSourceBinding800 required");
            binding = actualBinding;
            snapshot = actualBinding.Snapshot;
            refused = false;
            long attempt = ++revision;
            var capturedSnapshot = snapshot;
            bool committed = false;
            IDisposable pendingTown = null, pendingGround = null;
            modelCancellation = cancellation;
            preparing = true;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                Need(capturedSnapshot != null && Current(attempt, actualBinding, capturedSnapshot),
                    "Actual current complete copied Town binding required");
                Need(capturedSnapshot.Frame.Equals(Matrix4x4.identity),
                    "Reviewed +/-400m source profile requires the original identity world frame");
                // Tokens are already invalid in these callbacks. Never ask owner
                // currency before retiring this exact consumer publication.
                pendingTown = actualBinding.Publication.BeforeRevoke((expired, why) =>
                {
                    if (Same(attempt, actualBinding, capturedSnapshot)
                        && ReferenceEquals(expired, actualBinding.Publication))
                        Invalidate("Town revoked before geometry teardown");
                });
                Need(Current(attempt, actualBinding, capturedSnapshot), "Town changed during registration");
                pendingGround = actualBinding.Ground.BeforeRevoke((expired, why) =>
                {
                    if (Same(attempt, actualBinding, capturedSnapshot)
                        && ReferenceEquals(expired, actualBinding.Ground))
                        Invalidate("Ground revoked before geometry teardown");
                });
                Need(Current(attempt, actualBinding, capturedSnapshot), "Ground changed during registration");
                Need(capturedSnapshot.Entries != null && capturedSnapshot.Entries.Count <= MaximumEntries,
                    "Complete bounded Town entries required");
                BeginPreparationFrame();
                var staged = new List<OccupiedBounds>();
                var colliderIds = new HashSet<ulong>();
                var bodyIds = new HashSet<ulong>();
                int totalVertices = 0, totalIndices = 0;
                for (int ordinal = 0; ordinal < capturedSnapshot.Entries.Count; ordinal++)
                {
                    while (!SpendPreparation())
                    {
                        yield return null;
                        CheckResume(attempt, actualBinding, capturedSnapshot, cancellation);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    var entry = capturedSnapshot.Entries[ordinal];
                    Need(entry != null && entry.EntryOrdinal == ordinal, "Missing/changed copied entry order");
                    if (entry.Disposition == TownEntryDisposition800.Suppressed)
                    {
                        Need(entry.Body == null, "Suppressed source must not invent a physical body");
                        continue;
                    }
                    Need(entry.Disposition == TownEntryDisposition800.Emitted && entry.Body != null,
                        "Unknown/unavailable emitted body disposition");
                    var body = entry.Body;
                    Need(body.RuntimeBodyEntityId != 0 && bodyIds.Add(body.RuntimeBodyEntityId)
                        && body.CollisionInputs != null && body.CollisionInputs.Count > 0,
                        "Every emitted body requires its own actual collision input");
                    foreach (var input in body.CollisionInputs)
                    {
                        while (!SpendPreparation())
                        {
                            yield return null;
                            CheckResume(attempt, actualBinding, capturedSnapshot, cancellation);
                        }
                        cancellation.ThrowIfCancellationRequested();
                        Need(input != null && input.RuntimeColliderEntityId != 0
                            && colliderIds.Add(input.RuntimeColliderEntityId) && colliderIds.Count <= MaximumColliders,
                            "Unknown/aliased/over-budget actual collider");
                        Need(TryFrame(input.LocalToWorld, out Affine3 frame), "Original affine collider frame required");
                        OccupiedBounds bounds = default;
                        if (input.Kind == TownCollisionKind800.Box)
                        {
                            Need(TownObstacleGeometry800.TryBox(Point(input.BoxCentreMetres),
                                Point(input.BoxSizeMetres), frame, out bounds), "Box bounds arithmetic refused");
                        }
                        else if (input.Kind == TownCollisionKind800.TriangleMesh)
                        {
                            Need(input.MeshVertices != null && input.TriangleIndices != null
                                && input.SubmeshIndexCounts != null && input.MeshVertices.Count >= 3
                                && input.TriangleIndices.Count > 0 && input.TriangleIndices.Count % 3 == 0
                                && input.SubmeshIndexCounts.Count > 0
                                && input.SubmeshIndexCounts.Count <= MaximumSubmeshesPerCollider,
                                "Unsupported copied triangle mesh layout");
                            AddCount(ref totalVertices, input.MeshVertices.Count, MaximumVertices);
                            AddCount(ref totalIndices, input.TriangleIndices.Count, MaximumIndices);
                            int submeshIndices = 0;
                            foreach (int count in input.SubmeshIndexCounts)
                            {
                                while (!SpendPreparation())
                                {
                                    yield return null;
                                    CheckResume(attempt, actualBinding, capturedSnapshot, cancellation);
                                }
                                Need(count >= 0 && count % 3 == 0, "Invalid complete triangle submesh");
                                AddCount(ref submeshIndices, count, MaximumIndices);
                            }
                            Need(submeshIndices == input.TriangleIndices.Count, "Submesh indices must cover whole copied mesh");
                            foreach (int index in input.TriangleIndices)
                            {
                                while (!SpendPreparation())
                                {
                                    yield return null;
                                    CheckResume(attempt, actualBinding, capturedSnapshot, cancellation);
                                }
                                Need(index >= 0 && index < input.MeshVertices.Count, "Invalid original triangle index");
                            }
                            bool hasBounds = false;
                            foreach (var vertex in input.MeshVertices)
                            {
                                while (!SpendPreparation())
                                {
                                    yield return null;
                                    CheckResume(attempt, actualBinding, capturedSnapshot, cancellation);
                                }
                                Need(TownObstacleGeometry800.TryIncludeVertex(Point(vertex), frame,
                                    ref bounds, ref hasBounds), "Original vertex bounds arithmetic refused");
                            }
                            Need(hasBounds, "Empty actual mesh cannot become free space");
                        }
                        else throw new InvalidDataException("Unsupported actual Town collider kind");
                        Need(bounds.Valid, "Unavailable collider bound");
                        staged.Add(bounds);
                    }
                }
                var completedBounds = staged.ToArray();
                // Force source/body/eight-digest validation AFTER all copied-data
                // work, then only pure exact-attempt checks before publishing.
                Need(actualBinding.CommitSourceNow(out string reason), reason);
                cancellation.ThrowIfCancellationRequested();
                Need(Same(attempt, actualBinding, capturedSnapshot)
                    && !actualBinding.LifetimeCancellation.IsCancellationRequested,
                    "Town obstacle model changed at commit");
                townSubscription = pendingTown;
                groundSubscription = pendingGround;
                pendingTown = pendingGround = null;
                model = completedBounds;
                committed = true;
                Reason = "Copied actual Town XZ exclusion prepared; no movement admission";
            }
            finally
            {
                preparing = false;
                try { pendingTown?.Dispose(); }
                finally { pendingGround?.Dispose(); }
                if (!committed && Same(attempt, actualBinding, capturedSnapshot))
                    Invalidate("Town obstacle preparation refused/cancelled");
            }
        }

        /// <summary>
        /// Shared-instance query for a whole SourceScope radius0.01..1.5m capsule, maximum8m segment
        /// entirely within reviewed +/-400m XZ. The actual supplied radius is used
        /// unchanged, including the planner's promoted0.85f value. No clamping.
        /// Invalid/unsupported/stale/exhausted
        /// returns Unknown. All-height AABB overlap is conservatively Blocked.
        /// Budget is reserved BEFORE actual owner calls, including failed queries.
        /// Source footprints/interiors remain separate even if all walls are distant.
        /// </summary>
        public Verdict InspectCapsule(P from, P to, double radius)
        {
            RequireMainThread();
            int frame = Time.frameCount;
            if (queryBudgetFrame != frame)
            {
                queryBudgetFrame = frame;
                queries = comparisons = 0;
            }
            if (queries >= MaximumQueriesPerFrame) return Verdict.Unknown;
            queries++;
            var capturedModel = model;
            var actualBinding = binding;
            var capturedSnapshot = snapshot;
            long attempt = revision;
            if (refused || capturedModel == null
                || !SourceScope800.Allows(from, to, radius)
                || capturedModel.Length > MaximumComparisonsPerFrame - comparisons)
                return Verdict.Unknown;
            comparisons += capturedModel.Length; // Reserve the entire scan, even if early Blocked.
            if (!TownObstacleGeometry800.TryCapsuleBounds(from.X, from.Z, to.X, to.Z,
                    radius, out OccupiedBounds capsule)
                || !Current(attempt, actualBinding, capturedSnapshot)) return Verdict.Unknown;
            Verdict result = Verdict.Clear;
            foreach (var bounds in capturedModel)
                if (capsule.OverlapsXZ(bounds)) { result = Verdict.Blocked; break; }
            return Current(attempt, actualBinding, capturedSnapshot) ? result : Verdict.Unknown;
        }

        private void BeginPreparationFrame()
        {
            int frame = Time.frameCount;
            if (preparationBudgetFrame == frame) return;
            preparationBudgetFrame = frame;
            preparationOperations = MaximumPreparationOperationsPerFrame;
        }

        private bool SpendPreparation()
        {
            if (preparationOperations <= 0) return false;
            preparationOperations--;
            return true;
        }

        private void CheckResume(long attempt, TownSemanticSourceBinding800 actualBinding,
            TownSemanticSourceData800 capturedSnapshot, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            BeginPreparationFrame();
            Need(Current(attempt, actualBinding, capturedSnapshot), "Actual Town binding changed during slice");
        }

        private bool Same(long attempt, TownSemanticSourceBinding800 actualBinding,
            TownSemanticSourceData800 capturedSnapshot) => !refused && attempt == revision
                && ReferenceEquals(binding, actualBinding) && ReferenceEquals(snapshot, capturedSnapshot);

        private bool Current(long attempt, TownSemanticSourceBinding800 actualBinding,
            TownSemanticSourceData800 capturedSnapshot)
        {
            if (!Same(attempt, actualBinding, capturedSnapshot) || actualBinding == null) return false;
            if (modelCancellation.IsCancellationRequested)
            {
                Invalidate("Town obstacle consumer cancellation requested");
                return false;
            }
            bool live;
            try { live = actualBinding.IsCurrent && !actualBinding.LifetimeCancellation.IsCancellationRequested; }
            catch (Exception) { live = false; }
            if (!Same(attempt, actualBinding, capturedSnapshot)) return false;
            if (!live) { Invalidate("Actual Town binding no longer current"); return false; }
            return true;
        }

        private static bool TryFrame(Matrix4x4 original, out Affine3 frame)
        {
            frame = new Affine3(new Point3(original.m00, original.m10, original.m20),
                new Point3(original.m01, original.m11, original.m21),
                new Point3(original.m02, original.m12, original.m22),
                new Point3(original.m03, original.m13, original.m23));
            return frame.Finite && original.m30 == 0 && original.m31 == 0
                && original.m32 == 0 && original.m33 == 1;
        }

        private static Point3 Point(Vector3 point) => new Point3(point.x, point.y, point.z);
        private static void AddCount(ref int total, int amount, int maximum)
        {
            Need(amount >= 0 && amount <= maximum - total, "Whole copied geometry exceeds consumer cap");
            total += amount;
        }
        private static void Need(bool condition, string reason)
        {
            if (!condition) throw new InvalidDataException(reason);
        }
        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Town obstacle lifecycle/queries require captured main thread");
        }
        private void Invalidate(string reason)
        {
            refused = true;
            model = null;
            revision++;
            Reason = reason;
        }

        /// <summary>Unsubscribe consumer only; never dispose supplied binding/owner.</summary>
        public void Dispose()
        {
            RequireMainThread();
            Invalidate("Town obstacle consumer disposed");
            var town = townSubscription;
            var ground = groundSubscription;
            townSubscription = groundSubscription = null;
            binding = null;
            snapshot = null;
            try { town?.Dispose(); }
            finally { ground?.Dispose(); }
            // Preserve per-frame budgets/active-iterator guard across replacement.
        }
    }
}
