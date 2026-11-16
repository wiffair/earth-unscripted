using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RoadRush.Core800;
using RoadRush.FunnyAnimals.Admission;
using UnityEngine;

namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Builds and queries immutable placement hazards from a private checked copy
    /// of the original canonical source, bound to an actual scoped ground lease.
    /// </summary>
    /// <remarks>
    /// Public instance APIs run on Unity's main thread. Only BuildPrivate runs on
    /// the managed preparation worker; it never reads Unity objects or the lease.
    /// X and Z are anchored source/world metres; Y is not a semantic input here.
    /// A clear answer covers static source hazards only. Ground support, banks,
    /// actual Town wall publication, actor completeness and species policy must
    /// still compose before gameplay admission. No ISafeWorld implementation or
    /// source/ground/registry capability is issued by this consumer.
    /// </remarks>
    public sealed class SourceHazards800 : IDisposable
    {
        // Existing defensive input limits, not observed counts or new admission
        // settings. The actual reviewed capture contains 1793 buildings/86 areas.
        const int MaximumSourceBuildings = 50000;
        const int MaximumSourceAreas = 2000;
        const int MaximumFootprintVertices = 500000;

        // Null means unbound. The component borrows this capability and the
        // revocation subscription; it never owns ground meshes or materials.
        GroundLease800 groundLease;
        CanonicalRaw13Document sourceOwner;
        ReviewedCanonicalRaw13Input reviewedInputOwner;
        IDisposable revocationSubscription;
        SafetyIndex hazardIndex;
        int groundGeneration;
        bool refused;
        int preparationRequest;
        CancellationTokenSource preparationCancellation;
        AreaReadModel800 areaReadModel;
        WorkBudget queryBudget;
        int queryFrame = -1;
        int lastPreparationFrame = -1;
        Task<Prepared> preparationWorker;

        /// <summary>True only for this static source index; not gameplay readiness.</summary>
        public bool ReadyStaticOnly
        {
            get; private set;
        }
        /// <summary>Remains false until the separate owner bindings exist.</summary>
        public bool GameplaySemanticsBound => false;
        /// <summary>Diagnostic refusal/preparation text; never an authority token.</summary>
        public string Reason { get; private set; } = "Unbound";
        /// <summary>Digest of the complete canonical raw document, including buildings/water.</summary>
        public string RawDocumentDigest
        {
            get; private set;
        }
        /// <summary>Road-source digest only; it does not cover all geographic features.</summary>
        public string SourceDigest
        {
            get; private set;
        }
        public int ShapeCount => hazardIndex?.ShapeCount ?? 0;
        public int IndexReferences => hazardIndex?.References ?? 0;

        internal sealed class Prepared
        {
            internal SafetyIndex Index; internal AreaReadModel800 Areas; internal int Buildings, OutsideScopeIndexOperations;
        }
        static void Need(bool value, string why)
        {
            if (!value)
                throw new InvalidDataException(why);
        }

        /// <summary>
        /// Prepares one private source index while advancing Unity frames. The
        /// existing reviewed input must already be selected by its source owner.
        /// Start and advance this iterator on the main thread before Town consumes
        /// the original canonical handle, or original.Write will safely refuse.
        /// </summary>
        /// <remarks>
        /// A single retained worker and at most one new request per frame prevent
        /// cancellation from spawning overlapping source-lock workers. Disposing
        /// or revoking cancels work; it cannot pretend a running worker terminated.
        /// Stale iterators cannot publish or invalidate a replacement request.
        /// Invalid source/lease/schema data throws and leaves the consumer unbound.
        /// No file/profile writes or original-source ownership transfer occurs.
        /// </remarks>
        public IEnumerator Prepare(CanonicalRaw13Document source,
            ReviewedCanonicalRaw13Input existingReviewedInput, GroundLease800 actualGround,
            CancellationToken cancellation = default)
        {
            // Cancellation is cooperative: retain the actual worker handle until
            // terminal, so repeated requests cannot overlap source-lock workers.
            if (lastPreparationFrame == Time.frameCount || preparationWorker != null && !preparationWorker.IsCompleted)
            {
                // The busy return is not acceptance of a replacement binding.
                // Invalidate the old publication immediately without pretending
                // cooperative cancellation made the retained worker terminal.
                if (!SourceBindingIdentity800.SameBinding(groundLease, sourceOwner, reviewedInputOwner,
                    actualGround, source, existingReviewedInput))
                {
                    ++preparationRequest;
                    Refuse("Different source/ground requested while previous preparation is busy");
                }
                Reason = "Preparation already requested this frame or worker still running";
                yield break;
            }
            lastPreparationFrame = Time.frameCount;
            Dispose();
            Need(source != null && existingReviewedInput != null && actualGround != null,
                "Actual source and ground bindings required");
            Need(actualGround.IsCurrent && actualGround.SourceSubsetOnly && !actualGround.FullWorldAdmitted
                && actualGround.Frame.Equals(Matrix4x4.identity)
                && actualGround.ScopeCoverageDigest == SourceScope800.ReviewedDigest,
                "Current reviewed800 scoped identity ground required");
            int capturedRequest = ++preparationRequest;
            groundLease = actualGround;
            groundGeneration = groundLease.Generation;
            refused = false;
            sourceOwner = source;
            reviewedInputOwner = existingReviewedInput;
            RawDocumentDigest = groundLease.RawDocumentDigest;
            SourceDigest = groundLease.SourceDigest;
            revocationSubscription = groundLease.BeforeRevoke((expired, why) =>
            {
                if (capturedRequest == preparationRequest && ReferenceEquals(expired, groundLease))
                    Refuse("Ground revoked: " + why);
            });
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            preparationCancellation = linked;
            string rawDigest = RawDocumentDigest, roadDigest = SourceDigest;
            // Raw decoding/index creation uses managed structures only. No owner
            // queries, Unity object access, Prepare(), Load() or shallow snapshots.
            var task = Task.Run(() => BuildPrivate(source, existingReviewedInput,
                rawDigest, roadDigest, linked.Token), linked.Token);
            preparationWorker = task;
            // Exactly one unconditional observer per created task: no status
            // sampling race, and repeated Dispose never adds continuations.
            task.ContinueWith(t => { var observed = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            bool completed = false;
            try
            {
                while (!task.IsCompleted)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Need(capturedRequest == preparationRequest && Current(), "Source hazard preparation lost its ground");
                    yield return null;
                }
                var result = task.GetAwaiter().GetResult();
                cancellation.ThrowIfCancellationRequested();
                Need(capturedRequest == preparationRequest && Current(), "Source hazard publication lost its ground");
                hazardIndex = result.Index;
                areaReadModel = result.Areas;
                ReadyStaticOnly = true;
                completed = true;
                Reason = "Static source hazards indexed; Town and actual actor bindings absent";
            }
            finally
            {
                linked.Cancel();
                if (ReferenceEquals(preparationCancellation, linked))
                    preparationCancellation = null;
                if (!completed && capturedRequest == preparationRequest)
                    Refuse("Static source hazard preparation cancelled/refused");
            }
        }

        // Managed worker entry, also used by the source-only verification probe.
        // Decode an independent complete envelope: GeoRegion.SnapshotCopy is a
        // shallow copy and would alias source arrays/cache state. The byte balance
        // guard at the end proves this index build did not mutate its private raw
        // source. It is not a proof of physical upload or native placement safety.
        internal static Prepared BuildPrivate(CanonicalRaw13Document source,
            ReviewedCanonicalRaw13Input input, string rawDigest, string roadDigest,
            CancellationToken token)
        {
            var canonicalBytes = source.Write(token);
            Need(World13DocumentCodec.Hash(canonicalBytes) == rawDigest, "Exact raw/ground digest mismatch");
            var privateDocument = CanonicalRaw13Codec.Read(input, canonicalBytes, token).TakeOwnedDocument(token);
            var sourceRegion = privateDocument.geography;
            Need(RoadSourceIdentity.Compute(sourceRegion.roads) == roadDigest, "Road source/ground mismatch");
            Need(sourceRegion.roadWidthSchema == 0 && (sourceRegion.roadWidthProfiles == null || sourceRegion.roadWidthProfiles.Length == 0),
                "Only original raw widths supported; prepared controls need explicit binding");
            Need(sourceRegion.buildings != null && sourceRegion.areas != null && sourceRegion.buildings.Length <= MaximumSourceBuildings
                && sourceRegion.areas.Length <= MaximumSourceAreas, "Complete bounded source arrays required");
            // A private read view reuses original-width road/water/raster logic.
            // Complete building and area polygons are added below, including suppressed source
            // features. These are placement hazards, NEVER physical wall witnesses.
            var readOnlyRegionView = new GeoRegion
            {
                minX = sourceRegion.minX,
                minZ = sourceRegion.minZ,
                width = sourceRegion.width,
                depth = sourceRegion.depth,
                roadWidthSchema = 0,
                roads = sourceRegion.roads,
                waters = sourceRegion.waters,
                buildings = Array.Empty<GeoBuilding>(),
                landCover = sourceRegion.landCover,
                landNx = sourceRegion.landNx,
                landNz = sourceRegion.landNz
            };
            var hazardIndex = new SafetyIndex();
            var areaReadModel = new AreaReadModel800();
            foreach (var unit in RegionIndexBuilder.Build(readOnlyRegionView, hazardIndex))
                token.ThrowIfCancellationRequested();
            int points = 0;
            int outsideScopeIndexOperations = 0;
            void AddWholeFootprint(SafetyIndex destination, Vector3[] polygon)
            {
                Need(polygon != null && polygon.Length >= 3
                    && polygon.Length <= SourceScope800.MaximumSourcePolygonVertices,
                    "Complete source footprint required");
                points = checked(points + polygon.Length);
                Need(points <= MaximumFootprintVertices, "Footprint vertex budget");
                var footprintPoints = new P[polygon.Length];
                for (int i = 0; i < polygon.Length; i++)
                {
                    var p = polygon[i];
                    token.ThrowIfCancellationRequested();
                    Need(!float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.z)
                        && !float.IsInfinity(p.z), "Finite source footprint");
                    footprintPoints[i] = new P(p.x, p.z);
                }
                if (!SourceScope800.MayAffect(footprintPoints))
                {
                    outsideScopeIndexOperations++;
                    return;
                }
                Need(PolygonFootprint800.TryCreate(footprintPoints, token, out var shape),
                    "Unsupported/degenerate/non-simple source polygon; semantics UNBOUND");
                foreach (var unit in destination.Add(shape))
                    token.ThrowIfCancellationRequested();
                Need(!destination.Failed, "Static hazard index budget/refusal");
            }
            foreach (var b in sourceRegion.buildings)
            {
                Need(b != null, "Missing original building");
                AddWholeFootprint(hazardIndex, b.points);
            }
            foreach (var a in sourceRegion.areas)
            {
                Need(a != null, "Missing original area");
                var kind = AreaTaxonomy800.Classify(a.kind);
                if (kind == AreaKind800.WaterHazard)
                    AddWholeFootprint(hazardIndex, a.points);
                Need(a.points != null, "Missing area geometry");
                var footprintPoints = new P[a.points.Length];
                for (int i = 0; i < footprintPoints.Length; i++)
                    footprintPoints[i] = new P(a.points[i].x, a.points[i].z);
                if (!SourceScope800.MayAffect(footprintPoints))
                {
                    outsideScopeIndexOperations++;
                    continue;
                }
                areaReadModel.Add(a.kind, footprintPoints, token);
            }
            token.ThrowIfCancellationRequested();
            hazardIndex.Finish();
            Need(hazardIndex.Ready && !hazardIndex.Failed, "Complete static index required");
            areaReadModel.Finish();
            Need(hazardIndex.References + areaReadModel.References <= SafetyIndex.MaxReferences,
                "Aggregate source hazard/area reference cap");
            Need(World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(privateDocument)) == rawDigest,
                "Private semantic indexing changed canonical source");
            token.ThrowIfCancellationRequested();
            return new Prepared
            {
                Index = hazardIndex,
                Areas = areaReadModel,
                Buildings = sourceRegion.buildings.Length,
                OutsideScopeIndexOperations = outsideScopeIndexOperations
            };
        }

        bool Current()
        {
            try
            {
                bool valid = !refused && groundLease != null && groundLease.Generation == groundGeneration && groundLease.IsCurrent;
                if (!valid)
                    Refuse("Ground lifetime is no longer current");
                return valid;
            }
            catch { Refuse("Unknown ground lifetime"); return false; }
        }
        /// <summary>
        /// Tests a complete radius-expanded XZ segment against original source
        /// hazards inside the reviewed scope. Distances are metres; radius and
        /// segment limits come from SourceScope800. Unknown includes exhausted
        /// work, stale/unbound leases and unsupported area kinds at this query.
        /// </summary>
        /// <remarks>
        /// Whole original building polygons remain placement hazards regardless
        /// of emission. They are not witnesses of physical walls or solid rooms.
        /// Shared per-frame budgets are private; callers cannot reset them to
        /// bypass limits. Fair scheduling between actors is a separate obligation.
        /// </remarks>
        public Verdict InspectSourceHazards(P from, P to, double radius)
        {
            // Callers cannot refresh budgets between actors or repeated attempts.
            // Fair actor scheduling remains a separate integration requirement.
            if (queryFrame != Time.frameCount)
            {
                queryFrame = Time.frameCount;
                queryBudget = new WorkBudget();
            }
            var work = queryBudget;
            // Spend a cheap gate BEFORE invoking the owner, even on exhausted calls.
            if (work == null || !work.Use(ref work.Obstacles) || !ReadyStaticOnly
                || !SourceScope800.Allows(from, to, radius) || !Current())
                return Verdict.Unknown;
            var result = hazardIndex.Query(from, to, radius, work);
            if (result == Verdict.Clear && areaReadModel.InspectUnknown(from, to, radius, work) != Verdict.Clear)
                return Verdict.Unknown;
            return Current() ? result : Verdict.Unknown;
        }
        /// <summary>
        /// Returns intersected original land-use categories, not whole-footprint
        /// habitat coverage. False means unknown, stale, unbound or over budget.
        /// None means no intersected source tag, never gameplay admission.
        /// </summary>
        public bool TryInspectAreaFacts(P from, P to, double radius, out AreaFacts800 facts)
        {
            facts = AreaFacts800.None;
            if (queryFrame != Time.frameCount)
            {
                queryFrame = Time.frameCount;
                queryBudget = new WorkBudget();
            }
            if (!queryBudget.Use(ref queryBudget.Obstacles) || !ReadyStaticOnly
                || !SourceScope800.Allows(from, to, radius) || !Current())
                return false;
            bool known = areaReadModel.TryInspect(from, to, radius, queryBudget, out facts);
            return Current() && known;
        }
        void Refuse(string why)
        {
            refused = true;
            ReadyStaticOnly = false;
            hazardIndex = null;
            areaReadModel = null;
            Reason = why;
            preparationCancellation?.Cancel();
        }
        /// <summary>
        /// Main-thread cleanup: invalidate publication, request cancellation and
        /// detach the borrowed revocation subscription. Running work retains its
        /// handle until terminal; repeated cleanup adds no task continuations.
        /// </summary>
        public void Dispose()
        {
            ++preparationRequest;
            Refuse("Unbound");
            revocationSubscription?.Dispose();
            revocationSubscription = null;
            groundLease = null;
            sourceOwner = null;
            reviewedInputOwner = null;
            queryBudget = null;
            queryFrame = -1;
        }
    }
}
