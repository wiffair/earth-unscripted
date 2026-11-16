using System;
using System.Collections.ObjectModel;
using System.Threading;
using UnityEngine;
using RoadRush.Core800;
using RoadRush.VehiclePopulation;

namespace RoadRush.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>Per-actor physical diagnostic; unavailable parts are retained, never omitted.</summary>
    public readonly struct RegisteredVehicleDiagnostic
    {
        public readonly string RuntimeHandle;
        public readonly int Model;
        public readonly RegisteredPhysicalEnvelope? Physical;
        public readonly string UnboundReason;

        internal RegisteredVehicleDiagnostic(ActualVehicleSample source,
            RegisteredPhysicalEnvelope? physical, string reason)
        {
            RuntimeHandle = source.RuntimeHandle;
            Model = source.Model;
            Physical = physical;
            UnboundReason = reason;
        }
    }

    /// <summary>
    /// Detached physical diagnostics plus PRIVATE actual source-token association.
    /// No mutable native handles or capability constructor escapes. A successful read
    /// is NOT whole-vehicle/presentation/placement clearance. Immediate commit must
    /// revalidate the actual snapshot and actual Town/ground/registry coherence.
    /// </summary>
    internal sealed class RegisteredVehicleDiagnosticBatch
    {
        internal readonly RegisteredVehicleEnvelopeReader Reader;
        internal readonly SessionVehiclePopulation Population;
        internal readonly ActualVehiclePopulationSnapshot Snapshot;
        internal readonly GroundLease800 Ground;
        internal readonly TownSemanticPublication800 Town;
        internal readonly ReadOnlyCollection<RegisteredVehicleDiagnostic> Vehicles;
        internal bool WholeVehicleClearanceBound => false;

        internal RegisteredVehicleDiagnosticBatch(RegisteredVehicleEnvelopeReader reader,
            SessionVehiclePopulation population, ActualVehiclePopulationSnapshot snapshot,
            GroundLease800 ground, TownSemanticPublication800 town, RegisteredVehicleDiagnostic[] values)
        {
            Reader = reader;
            Population = population;
            Snapshot = snapshot;
            Ground = ground;
            Town = town;
            Vehicles = Array.AsReadOnly(values);
        }
    }

    /// <summary>
    /// Read-only SAME-ACTUAL-ASSEMBLY consumer of Registry2/Owner3/Town3. It calls the
    /// real INTERNAL MatchesTownPublication800 predicate, never a bool/header/callback
    /// substitute. Primary owns coherent source compilation/rebase and all issuance.
    /// Construct/use on actual producer's validated main thread. Per-instance caps
    /// precede expensive owner reads; no global scheduler/native-cost acceptance.
    /// No scene discovery, actor behavior, physics writes, profile reads or activation.
    /// </summary>
    internal sealed class RegisteredVehicleEnvelopeReader : IDisposable
    {
        private const int MaximumActualActors = 128; // frozen actual registry publication fence
        private const int MaximumReadsPerFrame = 2;
        private const int MaximumCommitsPerFrame = 4;
        private readonly int owningThread = Thread.CurrentThread.ManagedThreadId;
        private int budgetFrame = -1;
        private int reads;
        private int commits;
        private bool disposed;
        private readonly ReaderPublicationFence<RegisteredVehicleDiagnosticBatch> publication =
            new ReaderPublicationFence<RegisteredVehicleDiagnosticBatch>();

        /// <summary>
        /// Acquires from actualPopulation itself, so an unrelated supplied snapshot
        /// cannot be paired with another owner. Unsupported per-vehicle geometry stays
        /// in diagnostic rows; it never creates an empty safe roster or a clearance grant.
        /// MUST be called in ordered LateUpdate AFTER actual registry driver30000
        /// (primary pump31000). Ordinary Update/pre-solve samples are never accepted.
        /// Presentation is intentionally UNBOUND until authentic producer integration.
        /// </summary>
        internal bool TryReadAfterActorFrame(SessionVehiclePopulation actualPopulation, GroundLease800 actualGround,
            TownSemanticPublication800 actualTown, out RegisteredVehicleDiagnosticBatch batch, out string reason)
        {
            batch = null;
            reason = "Actual source read unavailable";
            if (!Spend(false, out reason)) return false;
            if (!publication.TryBeginRead(out long operation))
            {
                reason = "Reader publication retired/exhausted";
                return false;
            }
            try
            {
                if (!Coherent(actualPopulation, actualGround, actualTown, out reason)) return false;
                if (!actualPopulation.TryCapture(out ActualVehiclePopulationSnapshot snapshot, out reason)) return false;
                // These trace checks supplement the actual reference/generation predicate;
                // strings cannot establish coherence on their own. Physical Town GUID is
                // deliberately NOT compared with semantic RuntimeWorldHandle.
                if (snapshot == null || snapshot.Phase != VehicleSamplePhase.AfterActorFrame
                    || snapshot.Frame != Time.frameCount || !snapshot.FixedTimeSeconds.Equals(Time.fixedTime)
                    || snapshot.GroundRuntimeHandle != actualGround.RuntimeOwnerHandle
                    || snapshot.SourceDigest != actualGround.SourceDigest
                    || snapshot.PhysicalDigest != actualGround.FinalBufferDigest
                    || snapshot.Vehicles.Count == 0 || snapshot.Vehicles.Count > MaximumActualActors)
                {
                    reason = "Actual roster/source trace unavailable; never an empty ready fallback";
                    return false;
                }
                var values = new RegisteredVehicleDiagnostic[snapshot.Vehicles.Count];
                for (int index = 0; index < values.Length; index++)
                {
                    ActualVehicleSample sample = snapshot.Vehicles[index];
                    if (sample == null)
                    {
                        reason = "Actual roster contains an unavailable actor";
                        return false;
                    }
                    bool available = ActualSamplePhysicalConverter.TryConvert(sample,
                        out RegisteredPhysicalEnvelope physical, out string diagnostic);
                    values[index] = new RegisteredVehicleDiagnostic(sample, available ? physical : null, diagnostic);
                }
                // Commit of this detached read must follow all numeric work immediately.
                // IsCurrent includes actual body/model/collider membership/pose checks.
                if (!Coherent(actualPopulation, actualGround, actualTown, out reason)
                    || snapshot.Phase != VehicleSamplePhase.AfterActorFrame || snapshot.Frame != Time.frameCount
                    || !snapshot.FixedTimeSeconds.Equals(Time.fixedTime) || !snapshot.IsCurrent)
                {
                    reason = "Actual source snapshot became stale before read publication";
                    return false;
                }
                var candidate = new RegisteredVehicleDiagnosticBatch(this, actualPopulation,
                    snapshot, actualGround, actualTown, values);
                if (disposed || !publication.TryPublish(operation, candidate))
                {
                    reason = "Read replaced/retired during actual owner callback";
                    return false;
                }
                batch = candidate;
                reason = "Physical diagnostics only; authentic complete presentation/native acceptance still UNBOUND";
                return true;
            }
            catch (Exception exception)
            {
                publication.ClearIfOwned(operation);
                reason = "Actual registry/town/ground UNBOUND: " + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Mandatory immediate caller commit boundary. Budget is spent BEFORE any owner
        /// predicate. Cached values cannot bypass actual snapshot.IsCurrent. Returns no
        /// authority and NEVER turns whole presentation/placement clearance true.
        /// </summary>
        internal bool TryCommitPhysicalDiagnostics(RegisteredVehicleDiagnosticBatch batch, out string reason)
        {
            if (!Spend(true, out reason)) return false;
            if (batch == null || !ReferenceEquals(batch.Reader, this)
                || !publication.TryObserveCurrent(batch, out long operation))
            {
                reason = "Different/retired diagnostic read";
                return false;
            }
            try
            {
                if (!Coherent(batch.Population, batch.Ground, batch.Town, out reason)
                    || batch.Snapshot.Phase != VehicleSamplePhase.AfterActorFrame
                    || batch.Snapshot.Frame != Time.frameCount
                    || !batch.Snapshot.FixedTimeSeconds.Equals(Time.fixedTime) || !batch.Snapshot.IsCurrent)
                {
                    publication.ClearIfOwned(operation, batch);
                    reason = "Actual snapshot stale at immediate diagnostic commit";
                    return false;
                }
                if (disposed || !publication.StillCurrent(operation, batch))
                {
                    reason = "Diagnostic commit replaced/retired during actual owner callback";
                    return false;
                }
                reason = "Fresh physical diagnostics; whole placement remains UNBOUND";
                return true;
            }
            catch (Exception exception)
            {
                publication.ClearIfOwned(operation, batch);
                reason = "Actual commit UNBOUND: " + exception.Message;
                return false;
            }
        }

        private static bool Coherent(SessionVehiclePopulation population, GroundLease800 ground,
            TownSemanticPublication800 town, out string reason)
        {
            reason = "Authentic matching current registry/ground/Town required";
            if (population == null || ground == null || town == null || !ReferenceEquals(town.Ground, ground)
                || !SameFrameBits(ground.Frame, town.Frame) || !ground.SourceSubsetOnly || ground.FullWorldAdmitted)
                return false;
            // ACTUAL Root member compares private World/Ground/Region references,
            // world/town generation domains and frame/currentness. Never substitute
            // equality of unrelated GUIDs, digests or caller asserted readiness.
            return ground.IsCurrent && town.IsCurrent && population.MatchesTownPublication800(town);
        }

        private bool Spend(bool commit, out string reason)
        {
            reason = "Read disposed, wrong thread or actual-frame budget exhausted";
            if (disposed || Thread.CurrentThread.ManagedThreadId != owningThread) return false;
            int frame = Time.frameCount;
            if (frame != budgetFrame)
            {
                budgetFrame = frame;
                reads = commits = 0;
            }
            if (commit)
            {
                if (commits >= MaximumCommitsPerFrame) return false;
                commits++;
            }
            else
            {
                if (reads >= MaximumReadsPerFrame) return false;
                reads++;
            }
            return true;
        }

        private static bool SameFrameBits(Matrix4x4 first, Matrix4x4 second)
        {
            for (int index = 0; index < 16; index++)
                if (BitConverter.SingleToInt32Bits(first[index]) != BitConverter.SingleToInt32Bits(second[index])) return false;
            return true;
        }

        /// <summary>No native destruction/state changes; retained batches cannot commit after disposal.</summary>
        public void Dispose()
        {
            disposed = true;
            publication.Dispose();
            // Do not reset counters to permit same-frame bypass. This reader is terminal.
        }
    }
}
