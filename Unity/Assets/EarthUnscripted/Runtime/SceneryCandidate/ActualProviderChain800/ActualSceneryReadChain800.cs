using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using EarthUnscripted.Core800;
using EarthUnscripted.VehiclePopulation;
using EarthUnscripted.FunnyAnimals.Admission;
using EarthUnscripted.FunnyAnimals.ActualRegistryConsumer;
using CoreGroundVerdict = EarthUnscripted.FunnyAnimals.Core800Proposal.Verdict;
using Grounded800Candidate = EarthUnscripted.FunnyAnimals.Core800Proposal.Grounded800Candidate;
using EarthUnscripted.FunnyAnimals.Scheduling800;
using EarthUnscripted.FunnyAnimals.Semantics800Proposal;
using EarthUnscripted.FunnyAnimals.VehicleCaptureProposal;

namespace EarthUnscripted.Scenery800
{
    /// <summary>
    /// One-shot, main-thread observation chain for ONE actual ready scoped session.
    /// No scene writes, source/lease issuance, rebind, spawning, actor movement,
    /// automatic lifecycle or ISafeWorld. All query answers are provisional facts
    /// until TryCommitCurrentProbe; even that commit grants no gameplay admission.
    /// </summary>
    /// <remarks>
    /// The caller invokes frame reads from its actual ordered31000-or-later actor
    /// callback. This class cannot attest the caller's native callback order from
    /// a timestamp; it also requires genuine same-frame AfterActorFrame samples.
    /// Consumer-only disposal never retires the actual World/Town/Ground/session.
    /// </remarks>
    public sealed class ActualSceneryReadChain800 : IDisposable
    {
        public const int MaximumQueriesPerFrame = 64;
        private const int MaximumActualVehicles = 128;
        private const double MaximumPreparationSeconds = 300;
        private const double MaximumGroundSampleSpacingMetres = .5;
        private const double MaximumGroundSampleQuantizationMetres = .00002;
        private readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        private readonly GameSession session;
        private readonly WorldBuilder builder;
        private readonly GeoWorld world;
        private readonly GroundLease800 ground;
        private readonly TownSemanticPublication800 publication;
        private readonly SessionVehiclePopulation population;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken lifetimeToken;
        private readonly PublishedSourceHazards800 source = new PublishedSourceHazards800();
        private readonly RegisteredVehicleEnvelopeReader physicalReader = new RegisteredVehicleEnvelopeReader();
        private LeasedStaticGround800 groundMemo;
        private TownSemanticSourceBinding800 townBinding;
        private TownPhysicalObstacleExclusion800 townObstacles;
        private IDisposable townRetirement, groundRetirement;
        private CancellationTokenSource preparationCancellation;
        private CancellationToken externalCancellation;
        private bool disposed, prepareStarted, prepared;
        private long revision;
        private int queryFrame = -1, queryCount;
        private int beginAttemptFrame = -1, commitAttemptFrame = -1;
        private RegisteredVehicleDiagnosticBatch probeBatch;
        private ActualVehiclePopulationSnapshot probeSnapshot;
        private OccupiedBounds[] probeActorBounds;
        private int probeFrame = -1;
        private float probeFixedSeconds;

        private ActualSceneryReadChain800(GameSession actualSession, WorldBuilder actualBuilder,
            GeoWorld actualWorld, GroundLease800 actualGround, TownSemanticPublication800 actualPublication,
            SessionVehiclePopulation actualPopulation)
        {
            session = actualSession;
            builder = actualBuilder;
            world = actualWorld;
            ground = actualGround;
            publication = actualPublication;
            population = actualPopulation;
            lifetimeToken = lifetime.Token; // Captured before CTS disposal; safe managed cancellation read.
        }

        public bool Prepared => !disposed && prepared;
        public bool GameplayAdmission => false;
        public string Reason { get; private set; } = "Actual scoped observation chain unprepared";

        /// <summary>
        /// Capture only actual session-owned references; subscribe BEFORE any yield.
        /// Same-assembly actual internal scope/Town predicates are mandatory. A
        /// successful capture is not preparation, authority or a complete-world grant.
        /// </summary>
        public static bool TryCapture(GameSession actualSession, out ActualSceneryReadChain800 chain,
            out string reason)
        {
            chain = null;
            reason = "Actual ready scoped session unavailable";
            ActualSceneryReadChain800 candidate = null;
            IDisposable pendingTown = null, pendingGround = null;
            try
            {
                if (!actualSession || !actualSession.BootReady || !actualSession.IsScopedReaderSession800)
                    return false;
                WorldBuilder actualBuilder = actualSession.World;
                if (!actualBuilder || actualBuilder.ScopedSession800 == null) return false;
                GeoWorld actualWorld = actualBuilder.GeographicWorld;
                GroundLease800 actualGround = actualBuilder.ScopedGround800;
                TownSemanticPublication800 actualPublication = actualWorld ? actualWorld.CurrentTownPublication800 : null;
                SessionVehiclePopulation actualPopulation = actualSession.ActualVehicles;
                if (actualGround == null || actualPublication == null || actualPopulation == null
                    || !ReferenceEquals(actualPublication.Ground, actualGround)) return false;
                candidate = new ActualSceneryReadChain800(actualSession, actualBuilder, actualWorld,
                    actualGround, actualPublication, actualPopulation);
                long attempt = candidate.revision;
                if (!candidate.ContextCurrent(attempt)) return false;
                pendingTown = actualPublication.BeforeRevoke((expired, why) =>
                {
                    if (ReferenceEquals(expired, candidate.publication))
                        candidate.Retire("Actual Town retired before geometry teardown");
                });
                if (!candidate.ContextCurrent(attempt)) return false;
                pendingGround = actualGround.BeforeRevoke((expired, why) =>
                {
                    if (ReferenceEquals(expired, candidate.ground))
                        candidate.Retire("Actual ground retired before geometry teardown");
                });
                if (!candidate.ContextCurrent(attempt)) return false;
                candidate.townRetirement = pendingTown;
                candidate.groundRetirement = pendingGround;
                pendingTown = pendingGround = null;
                chain = candidate;
                reason = candidate.Reason = "Actual context captured; source/ground/Town preparation pending";
                return true;
            }
            catch (Exception error)
            {
                reason = "Actual context capture refused: " + error.GetType().Name;
                return false;
            }
            finally
            {
                try { pendingTown?.Dispose(); }
                finally { pendingGround?.Dispose(); }
                if (chain == null) candidate?.Retire(reason);
            }
        }

        /// <summary>
        /// One preparation attempt, no rebind. Actual source input is supplied by
        /// the existing reviewed factory, not a filename/header or consumed Town
        /// Source handle. All optional consumer failures retire THIS chain only.
        /// Caller must consume/dispose the iterator; no actual owner is disposed.
        /// </summary>
        public IEnumerator Prepare(ReviewedCanonicalRaw13Input genuineReviewedInput,
            CancellationToken cancellation = default)
        {
            RequireMainThread();
            if (disposed || prepareStarted)
            {
                Retire("One-shot chain cannot prepare again");
                yield break;
            }
            prepareStarted = true;
            externalCancellation = cancellation;
            IEnumerator work = PrepareCore(genuineReviewedInput, cancellation);
            try
            {
                while (true)
                {
                    bool more = false, failed = false;
                    object yielded = null;
                    try { more = work.MoveNext(); if (more) yielded = work.Current; }
                    catch (Exception error)
                    {
                        Retire("Optional provider preparation refused: " + error.GetType().Name);
                        failed = true;
                    }
                    if (failed || !more) break;
                    yield return yielded;
                }
            }
            finally
            {
                try { (work as IDisposable)?.Dispose(); }
                catch (Exception error) { Retire("Preparation iterator cleanup refused: " + error.GetType().Name); }
                if (!prepared) Retire("Provider preparation cancelled/refused");
            }
        }

        private IEnumerator PrepareCore(ReviewedCanonicalRaw13Input genuineReviewedInput,
            CancellationToken cancellation)
        {
            long attempt = revision;
            Need(genuineReviewedInput != null && ContextCurrent(attempt), "Genuine current reviewed context required");
            preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellation);
            CancellationToken token = preparationCancellation.Token;
            double preparationBegan = Time.realtimeSinceStartupAsDouble;
            void CheckPreparation()
            {
                token.ThrowIfCancellationRequested();
                Need(Time.realtimeSinceStartupAsDouble - preparationBegan <= MaximumPreparationSeconds,
                    "Optional provider preparation deadline exceeded");
                Need(ContextCurrent(attempt), "Actual context changed during provider preparation");
            }
            var temporaryGround = new LeasedStaticGround800(ground);
            if (!Same(attempt)) { temporaryGround.Dispose(); yield break; }
            groundMemo = temporaryGround;
            IEnumerator sourceWork = source.Prepare(publication, genuineReviewedInput, token);
            try
            {
                while (sourceWork.MoveNext())
                {
                    CheckPreparation();
                    groundMemo.BeginFrame();
                    Need(ContextCurrent(attempt), "Context changed during source staging");
                    yield return null;
                }
            }
            finally { (sourceWork as IDisposable)?.Dispose(); }
            Need(source.StaticModelPrepared && ContextCurrent(attempt), "Published whole source unavailable");
            Need(TownSemanticSourceBinding800.TryCapture(world, ground, token,
                out TownSemanticSourceBinding800 temporaryTown, out string captureReason), captureReason);
            if (!Same(attempt)) { temporaryTown.Dispose(); yield break; }
            townBinding = temporaryTown;
            Need(ReferenceEquals(townBinding.Publication, publication), "Town binding changed publication during capture");
            townObstacles = new TownPhysicalObstacleExclusion800();
            IEnumerator obstacleWork = townObstacles.Prepare(townBinding, token);
            try
            {
                while (obstacleWork.MoveNext())
                {
                    CheckPreparation();
                    groundMemo.BeginFrame();
                    Need(ContextCurrent(attempt), "Context changed during Town staging");
                    yield return null;
                }
            }
            finally { (obstacleWork as IDisposable)?.Dispose(); }
            while (!groundMemo.PreparationCompleted)
            {
                CheckPreparation();
                groundMemo.BeginFrame();
                yield return null;
            }
            Need(source.StaticModelPrepared && townObstacles.PreparationCompleted
                && ReferenceEquals(townObstacles.Binding, townBinding), "Incomplete prepared provider set");
            Need(townBinding.CommitSourceNow(out string commitReason), commitReason);
            Need(ContextCurrent(attempt), "Context changed at chain model commit");
            // Cooperative elapsed-only fence after the LAST owner checks. A slow
            // synchronous final slice may finish late; it must not publish Ready.
            // This cannot interrupt a native call or provide hard timing bounds.
            Need(Time.realtimeSinceStartupAsDouble - preparationBegan <= MaximumPreparationSeconds,
                "Optional provider preparation deadline exceeded at final publication");
            token.ThrowIfCancellationRequested();
            Need(Same(attempt), "Chain retired at model commit");
            prepared = true;
            Reason = "Actual copied readmodels prepared; actor frame observation still required";
        }

        /// <summary>
        /// Invoke only from the actual ordered actor callback31000-or-later. Reads
        /// real same-frame AfterActorFrame data. Every physical+appearance row is
        /// retained; nullable/unsupported rows refuse the WHOLE probe. Appearance
        /// observations remain untrusted until final authentic owner bulk commit.
        /// One attempt per frame, including failure; no stale/pre-solve fallback.
        /// </summary>
        public bool TryBeginAfterActorFrame(out string reason)
        {
            RequireMainThread();
            ClearProbe();
            int frame = Time.frameCount;
            float fixedSeconds = Time.fixedTime;
            if (frame <= beginAttemptFrame)
                return Fail("Actor probe already attempted this frame", out reason);
            beginAttemptFrame = frame; // Before actual owner queries.
            long attempt = revision;
            try
            {
                if (!PreparedProvidersCurrent(attempt)) return Fail("Prepared providers unavailable", out reason);
                if (!physicalReader.TryReadAfterActorFrame(population, ground, publication,
                    out RegisteredVehicleDiagnosticBatch batch, out reason)) return false;
                ActualVehiclePopulationSnapshot snapshot = batch.Snapshot;
                if (snapshot == null || snapshot.Phase != VehicleSamplePhase.AfterActorFrame
                    || snapshot.Frame != frame || !snapshot.FixedTimeSeconds.Equals(fixedSeconds)
                    || batch.Vehicles.Count == 0 || batch.Vehicles.Count > MaximumActualVehicles
                    || batch.Vehicles.Count != snapshot.Vehicles.Count)
                    return Fail("Actual complete same-frame actor rows unavailable", out reason);
                var actorBounds = new OccupiedBounds[batch.Vehicles.Count];
                for (int index = 0; index < actorBounds.Length; index++)
                {
                    var diagnostic = batch.Vehicles[index];
                    ActualVehicleSample row = snapshot.Vehicles[index];
                    if (row == null || !diagnostic.Physical.HasValue || row.Presentation == null
                        || diagnostic.RuntimeHandle != row.RuntimeHandle || diagnostic.Model != row.Model
                        || row.Presentation.Model != row.Model)
                        return Fail("Actual actor physical/presentation row unbound", out reason);
                    OccupiedBounds physical = diagnostic.Physical.Value.Physical.Bounds;
                    OccupiedBounds directWorld = row.Presentation.DirectWorldBoundsMetres;
                    if (!physical.Valid || !directWorld.Valid)
                        return Fail("Invalid actual physical/direct-world appearance bounds", out reason);
                    actorBounds[index] = EnvelopeMath.Union(physical, directWorld);
                }
                if (!PreparedProvidersCurrent(attempt) || !snapshot.IsCurrent || !Same(attempt))
                    return Fail("Actual actor observation changed during conversion", out reason);
                probeBatch = batch;
                probeSnapshot = snapshot;
                probeActorBounds = actorBounds;
                probeFrame = frame;
                probeFixedSeconds = fixedSeconds;
                reason = Reason = "Provisional physical/direct-world observations; final owner commit required";
                return true;
            }
            catch (Exception error) { return Fail("Actor observation refused: " + error.GetType().Name, out reason); }
        }

        /// <summary>
        /// Provisional exclusions for actual promoted planner radius, maximum8m
        /// whole capsule in +/-400m identity world. Source original/suppressed
        /// polygons, Town walls/steps, fresh whole-corridor ground and ALL actual actor bounds.
        /// Endpoints must be exact binary32 source coordinates; arbitrary double
        /// endpoints refuse without rounding/shrinking the requested capsule.
        /// Ground samples <=0.5m remain observations, not native support/contact.
        /// Global64 queries/frame includes source-area queries and failures, spent
        /// BEFORE any owner predicate. Ground ONLY is memoized; no verdict cache.
        /// </summary>
        public Verdict InspectExclusions(P from, P to)
        {
            return InspectExclusions(from, to, out _);
        }

        /// <summary>Diagnostic-only overload; first-stage evidence is provisional.
        /// Existing gates/early returns/order/budgets are unchanged; no extra owner
        /// or geometry query. Original source/Town blocker IDs remain unknown because
        /// those existing APIs return only Verdict, not an individual-hit witness.</summary>
        public Verdict InspectExclusions(P from, P to, out ExclusionObservation800 observation)
        {
            RequireMainThread();
            observation = new ExclusionObservation800(from, to, Grounded800Candidate.SupportedRadiusMetres);
            ExclusionObservation800 diagnostic = observation;
            if (!SpendQuery()) return Verdict.Unknown;
            diagnostic.Stage = "ExactEndpointCoordinates";
            if (!TryExactSourcePoint(from, out float fromX, out float fromZ)
                || !TryExactSourcePoint(to, out float toX, out float toZ)) return Verdict.Unknown;
            diagnostic.Stage = "ProbeBinding";
            if (!TryProbe(from, to, out Probe read)) return Verdict.Unknown;
            diagnostic.Frame = read.Frame;
            double radius = Grounded800Candidate.SupportedRadiusMetres;
            try
            {
                // Fresh continuous copied-face/support/bank witness, NEVER memoized.
                diagnostic.Stage = "WholeGroundCorridor";
                CoreGroundVerdict corridor = groundMemo.InspectCorridor(fromX, fromZ, toX, toZ, out var corridorEnvelope);
                diagnostic.GroundEnvelope = corridorEnvelope;
                diagnostic.WholeGroundLastStoredCandidateReason = ExclusionObservation800.BoundReason(groundMemo.LastCandidateReason);
                diagnostic.Detail = "Candidate Reason snapshot may predate an early Unknown; no extra owner query";
                switch (corridor)
                {
                    case CoreGroundVerdict.Clear: break;
                    case CoreGroundVerdict.Blocked: return FinishExclusionObservation(read, Verdict.Blocked, diagnostic);
                    default: return FinishExclusionObservation(read, Verdict.Unknown, diagnostic);
                }
                diagnostic.Stage = "OriginalSourceHazards";
                diagnostic.Detail = "Original index/area API returns verdict only; exact source blocker ID unknown";
                Verdict result = source.InspectSourceHazards(from, to, radius);
                if (result != Verdict.Clear) return FinishExclusionObservation(read, result, diagnostic);
                diagnostic.Stage = "ActualTownCollisionBounds";
                diagnostic.Detail = "Copied Town collider API returns verdict only; exact body/collider ID unknown";
                result = townObstacles.InspectCapsule(from, to, radius);
                if (result != Verdict.Clear) return FinishExclusionObservation(read, result, diagnostic);
                diagnostic.Stage = "ActorQueryEnvelope";
                diagnostic.Detail = null;
                if (!EnvelopeMath.TryObservedSweep(new Point3(from.X, 0, from.Z),
                    new Point3(to.X, 0, to.Z), radius, 0, out OccupiedBounds capsule)) return Verdict.Unknown;
                diagnostic.Stage = "ActualActorBounds";
                diagnostic.QueryBounds = capsule;
                int actorIndex = 0;
                foreach (var bounds in read.ActorBounds)
                {
                    if (capsule.OverlapsXZ(bounds))
                    {
                        ActualVehicleSample actor = read.Snapshot.Vehicles[actorIndex];
                        diagnostic.ActorIndex = actorIndex;
                        diagnostic.ActorRuntimeHandle = actor.RuntimeHandle;
                        diagnostic.ActorRole = actor.Role;
                        diagnostic.ActorModel = actor.Model;
                        diagnostic.ActorBounds = bounds;
                        return FinishExclusionObservation(read, Verdict.Blocked, diagnostic);
                    }
                    actorIndex++;
                }
                double dx = to.X - from.X, dz = to.Z - from.Z;
                double length = Math.Sqrt(dx * dx + dz * dz);
                int intervals = length == 0 ? 0 : (int)Math.Ceiling(length / MaximumGroundSampleSpacingMetres);
                for (int index = 0; index <= intervals; index++)
                {
                    double fraction = intervals == 0 ? 0 : (double)index / intervals;
                    double x = from.X + dx * fraction, z = from.Z + dz * fraction;
                    float sampleX = (float)x, sampleZ = (float)z;
                    // Height observations occur at these converted binary32 points.
                    // This conversion neither extends nor repairs the independent
                    // whole-corridor witness above. Rectangle padding is NOT an
                    // enlargement of LocalMeshGeometry's triangle-circle radius.
                    diagnostic.Stage = "PointGroundQuantization";
                    diagnostic.SampleIndex = index;
                    diagnostic.SampleX = sampleX; diagnostic.SampleZ = sampleZ;
                    if (Math.Abs((double)sampleX - x) > MaximumGroundSampleQuantizationMetres
                        || Math.Abs((double)sampleZ - z) > MaximumGroundSampleQuantizationMetres)
                        return Verdict.Unknown;
                    diagnostic.Stage = "PointGroundObservation";
                    StaticProbeStatus status = groundMemo.Query(sampleX, sampleZ, out _, out var pointObservation);
                    diagnostic.PointObservation = pointObservation;
                    diagnostic.Detail = pointObservation.CandidateProbeRan
                        ? ExclusionObservation800.BoundReason(pointObservation.LastCandidateReason)
                        : status == StaticProbeStatus.Blocked
                            ? "Memoized point Blocked; original memo did not retain rejection envelope/cause"
                            : "No actual candidate callback in this lookup; early/memo cause not retained by original memo";
                    if (status != StaticProbeStatus.Clear)
                        return FinishExclusionObservation(read, status == StaticProbeStatus.Blocked ? Verdict.Blocked : Verdict.Unknown, diagnostic);
                }
                diagnostic.Stage = "AllExclusionStagesClear";
                diagnostic.Detail = null;
                return FinishExclusionObservation(read, Verdict.Clear, diagnostic);
            }
            catch (Exception error)
            {
                diagnostic.Detail = "Existing exclusion query caught: " + error.GetType().Name;
                return Verdict.Unknown;
            }
        }

        // SAME final owner fence as the original Finish; subsequent assignments
        // are detached diagnostics only, never another owner read/permission.
        private Verdict FinishExclusionObservation(Probe read, Verdict provisional, ExclusionObservation800 observation)
        {
            observation.ProvisionalVerdict = provisional;
            Verdict result = Finish(read, provisional);
            observation.FinalVerdict = result;
            return result;
        }

        /// <summary>
        /// Separate original-raster fact at actual binary32 coordinates/radius.
        /// Kind codes are the existing raster contract, not inferred habitat or a
        /// species permission. Whole source-area facts must be checked separately.
        /// </summary>
        public Verdict InspectRasterHabitat(int requiredKind, float fromX, float fromZ, float toX, float toZ)
        {
            RequireMainThread();
            if (!SpendQuery()) return Verdict.Unknown;
            var from = new P(fromX, fromZ);
            var to = new P(toX, toZ);
            if (!TryProbe(from, to, out Probe read)) return Verdict.Unknown;
            try
            {
                Verdict result = source.InspectRasterHabitat(requiredKind, fromX, fromZ, toX, toZ,
                    (float)Grounded800Candidate.SupportedRadiusMetres);
                return Finish(read, result);
            }
            catch (Exception) { return Verdict.Unknown; }
        }

        /// <summary>Separate whole-original-area fact; false/unknown is NOT habitat admission.</summary>
        public bool TryWholeCapsuleInside(AreaKind800 requiredKind, P from, P to, out bool inside)
        {
            RequireMainThread();
            inside = false;
            if (!SpendQuery() || !TryProbe(from, to, out Probe read)) return false;
            try
            {
                bool success = source.TryWholeCapsuleInside(requiredKind, from, to,
                    Grounded800Candidate.SupportedRadiusMetres, out bool observedInside);
                if (!success || !ProbeCurrent(read)) return false;
                inside = observedInside;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Final ONCE/frame observation commit, never gameplay permission. All other
        /// owner checks precede physical-reader commit then AUTHENTIC appearance
        /// bulk commit LAST. Only managed cancellation/revision/copied stamp and
        /// exact local reference checks follow that final owner-dependent call.
        /// </summary>
        public bool TryCommitCurrentProbe(out string reason)
        {
            RequireMainThread();
            int frame = Time.frameCount;
            if (frame <= commitAttemptFrame) return Fail("Probe commit already attempted this frame", out reason);
            commitAttemptFrame = frame;
            if (!CaptureProbe(out Probe read)) return Fail("Actual actor probe unavailable", out reason);
            try
            {
                if (!ProbeCurrent(read) || !townBinding.CommitSourceNow(out reason)
                    || !ProbeCurrent(read)) return Fail("Actual providers changed before commit", out reason);
                if (!physicalReader.TryCommitPhysicalDiagnostics(read.Batch, out reason)) return false;
                if (!population.TryValidateAppearanceBatchForCommit(read.Snapshot, out reason)) return false;
                // DO NOT add owner/native getter calls below this boundary.
                if (!SameProbePure(read)) return Fail("Chain retired/replaced during final owner commit", out reason);
                reason = Reason = "Current observed source/physical/appearance probe; gameplay admission remains false";
                return true;
            }
            catch (Exception error) { return Fail("Final actual owner commit refused: " + error.GetType().Name, out reason); }
        }

        private readonly struct Probe
        {
            internal readonly long Revision;
            internal readonly RegisteredVehicleDiagnosticBatch Batch;
            internal readonly ActualVehiclePopulationSnapshot Snapshot;
            internal readonly OccupiedBounds[] ActorBounds;
            internal readonly int Frame;
            internal readonly float FixedSeconds;
            internal Probe(long revision, RegisteredVehicleDiagnosticBatch batch,
                ActualVehiclePopulationSnapshot snapshot, OccupiedBounds[] bounds, int frame, float fixedSeconds)
            { Revision = revision; Batch = batch; Snapshot = snapshot; ActorBounds = bounds; Frame = frame; FixedSeconds = fixedSeconds; }
        }

        private static bool TryExactSourcePoint(P point, out float x, out float z)
        {
            x = (float)point.X;
            z = (float)point.Z;
            return EnvelopeMath.Finite(point.X) && EnvelopeMath.Finite(point.Z)
                && (double)x == point.X && (double)z == point.Z;
        }

        private bool SpendQuery()
        {
            int frame = Time.frameCount;
            if (frame != queryFrame) { queryFrame = frame; queryCount = 0; }
            if (queryCount >= MaximumQueriesPerFrame) return false;
            queryCount++;
            return true;
        }

        private bool TryProbe(P from, P to, out Probe read)
        {
            if (!CaptureProbe(out read) || !SourceScope800.Allows(from, to,
                Grounded800Candidate.SupportedRadiusMetres)) return false;
            return ProbeCurrent(read);
        }

        private bool CaptureProbe(out Probe read)
        {
            read = new Probe(revision, probeBatch, probeSnapshot, probeActorBounds, probeFrame, probeFixedSeconds);
            return probeBatch != null && probeSnapshot != null && probeActorBounds != null
                && probeActorBounds.Length > 0 && probeActorBounds.Length == probeSnapshot.Vehicles.Count;
        }

        private bool ProbeCurrent(Probe read)
        {
            if (!SameProbePure(read) || read.Frame != Time.frameCount
                || !read.FixedSeconds.Equals(Time.fixedTime)) return false;
            return PreparedProvidersCurrent(read.Revision) && read.Snapshot.IsCurrent && SameProbePure(read);
        }

        private bool SameProbePure(Probe read) => Same(read.Revision)
            && ReferenceEquals(probeBatch, read.Batch) && ReferenceEquals(probeSnapshot, read.Snapshot)
            && ReferenceEquals(probeActorBounds, read.ActorBounds) && probeFrame == read.Frame
            && probeFixedSeconds.Equals(read.FixedSeconds) && read.Snapshot.Frame == read.Frame
            && read.Snapshot.FixedTimeSeconds.Equals(read.FixedSeconds);

        private Verdict Finish(Probe read, Verdict result) => ProbeCurrent(read) ? result : Verdict.Unknown;

        private bool PreparedProvidersCurrent(long attempt)
        {
            if (!prepared || groundMemo == null || townBinding == null || townObstacles == null
                || !source.StaticModelPrepared || !groundMemo.PreparationCompleted || !townObstacles.PreparationCompleted)
                return false;
            return ContextCurrent(attempt) && townBinding.IsCurrent && Same(attempt);
        }

        private bool ContextCurrent(long attempt)
        {
            if (!Same(attempt)) return false;
            bool current;
            try
            {
                current = session && session.BootReady && session.IsScopedReaderSession800 && builder && world
                    && ReferenceEquals(session.World, builder) && ReferenceEquals(builder.GeographicWorld, world)
                    && ReferenceEquals(builder.ScopedGround800, ground)
                    && ReferenceEquals(world.CurrentTownPublication800, publication)
                    && ReferenceEquals(session.ActualVehicles, population)
                    && ReferenceEquals(publication.Ground, ground)
                    && ground.ScopeCoverageDigest == SourceScope800.ReviewedDigest
                    && ground.Frame.Equals(Matrix4x4.identity) && ground.SourceSubsetOnly && !ground.FullWorldAdmitted
                    && publication.IsCurrent && ground.IsCurrent && population.MatchesTownPublication800(publication);
            }
            catch (Exception) { current = false; }
            if (!Same(attempt)) return false;
            if (!current) Retire("Actual scoped context no longer current");
            return current && Same(attempt);
        }

        private bool Same(long attempt) => !disposed && attempt == revision
            && !lifetimeToken.IsCancellationRequested && !externalCancellation.IsCancellationRequested;

        private void ClearProbe()
        {
            probeBatch = null;
            probeSnapshot = null;
            probeActorBounds = null;
            probeFrame = -1;
        }
        private bool Fail(string message, out string reason)
        {
            reason = message;
            if (!disposed) Reason = message;
            return false;
        }
        private static void Need(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Actual read chain requires its captured main thread");
        }
        private void Retire(string reason)
        {
            if (disposed) return;
            Reason = reason;
            Dispose();
        }
        public void Dispose()
        {
            RequireMainThread();
            if (disposed) return;
            disposed = true;
            revision++;
            prepared = false;
            ClearProbe();
            // Invalidate before callbacks; attempt every consumer cleanup. Retiring
            // observers must never call actual owner Control.Revoke/Destroy.
            try { lifetime.Cancel(); } catch (Exception) { }
            Cleanup(() => townRetirement?.Dispose());
            Cleanup(() => groundRetirement?.Dispose());
            Cleanup(() => physicalReader.Dispose());
            Cleanup(() => townObstacles?.Dispose());
            Cleanup(() => townBinding?.Dispose());
            Cleanup(() => groundMemo?.Dispose());
            Cleanup(() => source.Dispose());
            Cleanup(() => preparationCancellation?.Dispose());
            lifetime.Dispose();
        }
        private static void Cleanup(Action dispose)
        {
            try { dispose(); } catch (Exception) { }
        }
    }
}
