using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EarthUnscripted.FunnyAnimals.ActualRegistryConsumer;
using UnityEngine;

namespace EarthUnscripted.VehiclePopulation
{
    /// <summary>
    /// Source-only proposal extending the ACTUAL population, not another registry.
    /// Only its actual registered roots can own these private witnesses. Native draw,
    /// glyph/inverse agreement and cost remain unverified rollout gates; the immutable
    /// observation is not a whole-placement, visibility or future-motion grant.
    /// </summary>
    public sealed partial class SessionVehiclePopulation
    {
        // Conservative initial scheduling caps, not performance measurements. One
        // dirty mesh-tree scan per rendered frame avoids 28 simultaneous cold scans.
        // A cold actor may itself reach cache2.1's 500kV/1.5M-index limits and opaque
        // DeformationExtent scan. Warm visits allocate/inspect native hierarchy/text.
        private const int MaximumAppearanceRoots = 512;
        private const int MaximumWarmObservationsPerFrame = 28;
        private const int MaximumAppearanceRowsPerBatch = 28;
        private const int MaximumBorrowedFiltersPerRoot = 1024;
        private readonly Dictionary<GameObject, AppearanceState> appearanceStates =
            new Dictionary<GameObject, AppearanceState>();
        private int appearanceFrame = -1;
        private readonly AppearanceColdScheduler coldScheduler = new AppearanceColdScheduler();
        private GameObject coldRoot;
        private int warmObservations;
        private int lastAppearanceBatchFrame = -1;
        private bool appearancePoisoned;
        private readonly AppearanceKnownWriteSerial knownAppearanceWrites = new AppearanceKnownWriteSerial();

        /// <summary>Only actual known writer hooks call this; no clean state is supplied.</summary>
        internal IDisposable BeginKnownAppearanceWrite()
        {
            MainThread();
            return revoked ? null : knownAppearanceWrites.BeginWrite();
        }

        private sealed class AppearanceState
        {
            internal readonly AppearanceMutationEpoch Epoch = new AppearanceMutationEpoch();
            internal VehiclePresentationCache Cache;
            internal readonly HashSet<Mesh> BorrowedMeshes = new HashSet<Mesh>();
            internal long TemplateRevision = -1;
            internal readonly AppearanceRefusalHistory DiagnosticHistory = new AppearanceRefusalHistory();
        }

        private sealed class AppearanceWitness
        {
            internal readonly AppearanceState State;
            internal readonly AppearanceMutationEpoch.Observation Observation;
            internal readonly VehiclePresentationData Expected;
            internal AppearanceWitness(AppearanceState state,
                AppearanceMutationEpoch.Observation observation, VehiclePresentationData expected)
            { State = state; Observation = observation; Expected = expected; }
        }

        private AppearanceState AppearanceFor(GameObject root)
        {
            if (!appearanceStates.TryGetValue(root, out AppearanceState state))
            {
                if (appearanceStates.Count >= MaximumAppearanceRoots)
                    throw new InvalidOperationException("Appearance root budget exceeded");
                state = new AppearanceState();
                appearanceStates.Add(root, state);
            }
            return state;
        }

        /// <summary>Failure poisons appearance only; original physical diagnostics survive.</summary>
        internal void PoisonAppearanceDomain()
        {
            appearancePoisoned = true;
            knownAppearanceWrites.Retire();
            foreach (AppearanceState state in appearanceStates.Values) state.Epoch.Retire();
        }

        internal IDisposable BeginAppearanceWrite(GameObject actualRoot, bool content)
        {
            MainThread();
            if (revoked || appearancePoisoned || roster.Find(actualRoot) == null) return null;
            AppearanceState state = AppearanceFor(actualRoot);
            IDisposable scope = state.Epoch.BeginWrite(content);
            if (content) state.Cache?.InvalidateBeforeAuthorizedContentWrite();
            return scope;
        }

        internal IDisposable BeginBorrowedMeshWrite(Mesh actualMesh)
        {
            MainThread();
            if (revoked || appearancePoisoned || !actualMesh) return null;
            var scopes = new List<IDisposable>();
            foreach (AppearanceState state in appearanceStates.Values)
            {
                if (!state.BorrowedMeshes.Contains(actualMesh)) continue;
                scopes.Add(state.Epoch.BeginWrite(true));
                state.Cache?.InvalidateIfBorrowingMesh(actualMesh);
            }
            return new AppearanceScopeSet(scopes);
        }

        internal IDisposable BeginTextNativeChange()
        {
            MainThread();
            var scopes = new List<IDisposable>();
            foreach (AppearanceState state in appearanceStates.Values)
                scopes.Add(state.Epoch.BeginWrite(false));
            return new AppearanceScopeSet(scopes);
        }

        /// <summary>
        /// Select exactly one dirty actual member in rotating roster order. Called
        /// only by the real AfterActorFrame producer. Repeated same-frame Sample calls
        /// retain all spend/cursor state and cannot obtain another cold allowance.
        /// </summary>
        private void PrepareAppearanceFrame(ActualVehicleRosterDomain.Member[] members)
        {
            if (appearanceFrame == Time.frameCount) return;
            appearanceFrame = Time.frameCount;
            warmObservations = 0;
            coldRoot = null;
            if (appearancePoisoned || members.Length == 0) return;
            if (members.Length > MaximumAppearanceRoots) { PoisonAppearanceDomain(); return; }
            var dirty = new bool[members.Length];
            for (int index = 0; index < members.Length; index++)
            {
                GameObject root = members[index].Root as GameObject;
                if (!root) continue;
                AppearanceState state = AppearanceFor(root);
                dirty[index] = state.Epoch.CanObserve && (state.Cache == null ||
                    state.TemplateRevision != state.Epoch.ContentRevision);
            }
            int selected = coldScheduler.Select(appearanceFrame, dirty);
            if (selected >= 0) coldRoot = members[selected].Root as GameObject;
        }

        private void CaptureAppearance(GameObject root, CarVisual visual,
            out VehiclePresentationData data, out object witness, out string reason)
        {
            CaptureAppearanceCore(root, visual, out data, out witness, out reason);
            // Diagnostic history never affects cache/epoch decisions. Keep a prior
            // concrete helper failure visible after the same actor epoch retires.
            if (data == null)
            {
                // Post-attempt CLR bookkeeping only: no cache/native getter, owner
                // validation or clean grant. Unknown is distinct from false if no
                // genuine private actor state exists. First history predates the probe.
                bool hasState = appearanceStates.TryGetValue(root, out AppearanceState diagnosticState);
                string epochObservation = hasState ? diagnosticState.Epoch.CanObserve.ToString() : "unknown";
                string cacheReference = hasState ? (diagnosticState.Cache != null).ToString() : "unknown";
                reason = "postAttemptOwnerBookkeeping[epochCanObserve=" + epochObservation
                    + ",cacheReferencePresent=" + cacheReference + ",domainPoisoned=" + appearancePoisoned
                    + ",populationRevoked=" + revoked + "]; currentReason=" + reason;
                if (hasState) reason = diagnosticState.DiagnosticHistory.Describe(reason);
            }
        }

        private void CaptureAppearanceCore(GameObject root, CarVisual visual,
            out VehiclePresentationData data, out object witness, out string reason)
        {
            data = null;
            witness = null;
            reason = "Appearance domain poisoned or population revoked";
            if (appearancePoisoned || revoked) return;
            VehiclePresentationCache pendingCache = null;
            try
            {
                AppearanceState state = AppearanceFor(root);
                bool cold = state.Cache == null || state.TemplateRevision != state.Epoch.ContentRevision;
                if (cold)
                {
                    if (!ReferenceEquals(coldRoot, root)) { reason = "Cold actor not selected this frame"; return; }
                    coldRoot = null; // Spend even if an active epoch/global writer refuses below.
                }
                else
                {
                    if (warmObservations >= MaximumWarmObservationsPerFrame) { reason = "Warm observation allowance spent"; return; }
                    warmObservations++;
                }
                if (!state.Epoch.TryObserve(out var observation)) { reason = "Actual appearance epoch unavailable (active, retired or exhausted)"; return; }
                if (cold)
                {
                    // Spend is already retained even when an unrelated known writer
                    // invalidates this conservative cold attempt. No borrower map is
                    // assumed complete before the helper and index reads finish.
                    if (!knownAppearanceWrites.TryCapture(out long knownWriteStamp)) { reason = "Known-write serial unavailable before cold helper"; return; }
                    state.Cache?.Dispose();
                    state.Cache = null;
                    if (!VehiclePresentationCache.TryRebuildContent(root.transform, visual, observation.Content,
                        CancellationToken.None, out var rebuilt, out var candidate, out _, out reason))
                    {
                        state.DiagnosticHistory.Record("cold-helper", appearanceFrame, reason);
                        return;
                    }
                    pendingCache = rebuilt;
                    if (!state.Epoch.IsUnchanged(observation) || !knownAppearanceWrites.IsUnchanged(knownWriteStamp))
                    { reason = "Known content/font write during cold helper"; return; }
                    // Index actual aliases once per cold rebuild. The cache already
                    // bounded its tree, but this independent count must also refuse.
                    MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                    if (filters.Length > MaximumBorrowedFiltersPerRoot) { reason = "Appearance alias-index cap"; return; }
                    var nextBorrowers = new HashSet<Mesh>();
                    foreach (MeshFilter filter in filters)
                        if (filter && filter.sharedMesh) nextBorrowers.Add(filter.sharedMesh);
                    if (!state.Epoch.IsUnchanged(observation) || !knownAppearanceWrites.IsUnchanged(knownWriteStamp))
                    { reason = "Known content/font write during cold borrower indexing"; return; }
                    state.BorrowedMeshes.Clear();
                    state.BorrowedMeshes.UnionWith(nextBorrowers);
                    if (!state.Epoch.IsUnchanged(observation) || !knownAppearanceWrites.IsUnchanged(knownWriteStamp))
                    { reason = "Known content/font write before cold cache installation"; return; }
                    state.Cache = rebuilt;
                    pendingCache = null;
                    state.TemplateRevision = observation.Content;
                    data = candidate;
                }
                else
                {
                    if (!knownAppearanceWrites.CanObserve) { reason = "Known-write serial unavailable before warm helper"; return; }
                    VehiclePresentationCache attemptedCache = state.Cache;
                    bool refreshed = attemptedCache.TryRefreshPoseAfterActorFrame(observation.Content, CancellationToken.None,
                        out data, out _, out reason);
                    if (!state.Epoch.IsUnchanged(observation) || !ReferenceEquals(state.Cache, attemptedCache)
                        || !knownAppearanceWrites.CanObserve)
                    {
                        // An old stack must not dispose/retire a replacement candidate.
                        // Known writes already invalidated templates; a later fair turn
                        // may rebuild. Source capture supersession is not unknown content.
                        data = null;
                        reason = "Known write or newer observation superseded warm capture";
                        return;
                    }
                    if (!refreshed)
                    {
                        state.DiagnosticHistory.Record("warm-helper-before-retirement", appearanceFrame, reason);
                        attemptedCache.Dispose();
                        state.Cache = null;
                        // Unknown warm content/membership failure must NOT be silently
                        // laundered by next-frame cold acceptance of a changed tree.
                        // Retire this actor's appearance until a fresh real root lifetime.
                        state.Epoch.Retire();
                        return;
                    }
                }
                if (!state.Epoch.TryPublishCandidate(observation, data)) { data = null; reason = "Appearance epoch changed before candidate publication"; return; }
                witness = new AppearanceWitness(state, observation, data);
                reason = "";
            }
            catch (Exception error)
            {
                // Never convert a working physical roster into an empty safe scene.
                // Refuse this actor only; do not hide other actual physical rows.
                if (appearanceStates.TryGetValue(root, out AppearanceState failed))
                { failed.Epoch.Retire(); failed.Cache?.Dispose(); failed.Cache = null; }
                data = null;
                witness = null;
                reason = "Appearance exception: " + error.GetType().Name;
                if (appearanceStates.TryGetValue(root, out AppearanceState diagnosticFailure))
                    diagnosticFailure.DiagnosticHistory.Record("capture-exception", appearanceFrame, reason);
            }
            finally { pendingCache?.Dispose(); }
        }

        /// <summary>
        /// Authentic actual-owner FINAL batch check, not an issuer or clean capability.
        /// The consumer may inspect nullable sample.Presentation as UNTRUSTED geometry,
        /// then call this ONCE immediately before its action in the same main-thread
        /// turn. ALL actual rows must retain their private expected DTO and epoch; a
        /// single unsupported/refused row refuses the batch without omitting physical
        /// diagnostics. At most one ATTEMPT per actual rendered frame, success or fail.
        /// One pre/post whole-roster physical check plus <=28 cache pose validations;
        /// native font/raster/inverse agreement and performance remain rollout gates.
        /// A true return proves current observed source/witness state at this boundary
        /// only: no future-motion, whole-placement, native-accepted or Complete flag.
        /// </summary>
        internal bool TryValidateAppearanceBatchForCommit(ActualVehiclePopulationSnapshot snapshot,
            out string reason)
        {
            MainThread();
            reason = "Actual appearance batch unavailable";
            int actualFrame = Time.frameCount; // One native clock read, not an owner query.
            if (actualFrame <= lastAppearanceBatchFrame) return false;
            lastAppearanceBatchFrame = actualFrame; // Spend BEFORE owner/getter work; never reset on rebind.
            if (!knownAppearanceWrites.TryCapture(out long batchKnownWriteStamp)) return false;
            if (revoked || appearancePoisoned || snapshot == null ||
                snapshot.Phase != VehicleSamplePhase.AfterActorFrame || snapshot.Frame != actualFrame ||
                appearanceFrame != actualFrame || snapshot.Vehicles.Count == 0 ||
                snapshot.Vehicles.Count > MaximumAppearanceRowsPerBatch) return false;

            var expectedWorld = world;
            var expectedBuilder = boundSessionWorld;
            var expectedScene = boundSessionScene;
            long expectedRoster = roster.Revision;
            long expectedSequence = sampleSequence;
            var witnesses = new AppearanceWitness[snapshot.Vehicles.Count];
            var expectedCaches = new VehiclePresentationCache[witnesses.Length];
            AppearanceWitness currentlyValidating = null;
            bool BindingUnchanged() => !revoked && !appearancePoisoned &&
                ReferenceEquals(world, expectedWorld) && ReferenceEquals(boundSessionWorld, expectedBuilder) &&
                boundSessionScene == expectedScene && ReferenceEquals(latest, snapshot) &&
                roster.Revision == expectedRoster && sampleSequence == expectedSequence &&
                knownAppearanceWrites.IsUnchanged(batchKnownWriteStamp);
            bool WitnessesUnchanged()
            {
                for (int index = 0; index < witnesses.Length; index++)
                {
                    AppearanceWitness witness = witnesses[index];
                    ActualVehicleSample row = snapshot.Vehicles[index];
                    if (witness == null || !ReferenceEquals(row.AppearanceWitness, witness) ||
                        !ReferenceEquals(row.Presentation, witness.Expected) ||
                        !ReferenceEquals(witness.State.Cache, expectedCaches[index]) ||
                        !witness.State.Epoch.IsExpectedCandidate(witness.Expected, witness.Observation)) return false;
                }
                return true;
            }
            try
            {
                if (!BindingUnchanged() || !IsSnapshotCurrent(snapshot) || !BindingUnchanged()) return false;
                // Snapshot construction/rows come ONLY from this real private owner.
                // No caller DTO, expected signature or complete-roster flag is accepted.
                for (int index = 0; index < witnesses.Length; index++)
                {
                    ActualVehicleSample row = snapshot.Vehicles[index];
                    AppearanceWitness witness = row.AppearanceWitness as AppearanceWitness;
                    if (witness == null || row.Presentation == null ||
                        !ReferenceEquals(row.Presentation, witness.Expected) ||
                        !witness.State.Epoch.IsExpectedCandidate(witness.Expected, witness.Observation) ||
                        witness.State.Cache == null)
                    { reason = "Unbound appearance row: " + row.RuntimeHandle; return false; }
                    witnesses[index] = witness;
                    expectedCaches[index] = witness.State.Cache;
                }
                for (int index = 0; index < witnesses.Length; index++)
                {
                    currentlyValidating = witnesses[index];
                    if (!BindingUnchanged() || !expectedCaches[index].ValidatePoseCandidate(
                        currentlyValidating.Expected, currentlyValidating.Observation.Content,
                        CancellationToken.None, out _, out reason)) return false;
                    if (!BindingUnchanged() || !WitnessesUnchanged())
                    { reason = "Appearance changed during batch validation"; return false; }
                }
                // Check physical context AFTER all native/cache callbacks, then recheck
                // every managed expected identity/epoch AFTER that final physical read.
                if (!IsSnapshotCurrent(snapshot) || !BindingUnchanged() || !WitnessesUnchanged())
                { reason = "Actual context or appearance changed before final batch return"; return false; }
                reason = "";
                return true;
            }
            catch (Exception error)
            {
                currentlyValidating?.State.Epoch.Retire();
                reason = "Appearance batch exception: " + error.GetType().Name;
                return false;
            }
        }

        private void RetireAppearance(GameObject root)
        {
            if (!appearanceStates.TryGetValue(root, out AppearanceState state)) return;
            state.Epoch.Retire();
            state.Cache?.Dispose();
            state.BorrowedMeshes.Clear();
            appearanceStates.Remove(root);
        }

        private void RevokeAppearance()
        {
            appearancePoisoned = true;
            knownAppearanceWrites.Retire();
            foreach (GameObject root in appearanceStates.Keys.ToArray()) RetireAppearance(root);
            coldRoot = null;
        }
    }

    /// <summary>Owned managed write scopes only; never disposes borrowed engine objects.</summary>
    internal sealed class AppearanceScopeSet : IDisposable
    {
        private List<IDisposable> scopes;
        internal AppearanceScopeSet(List<IDisposable> scopes) { this.scopes = scopes; }
        public void Dispose()
        {
            List<IDisposable> previous = scopes;
            scopes = null;
            if (previous == null) return;
            for (int index = previous.Count - 1; index >= 0; index--) previous[index]?.Dispose();
        }
    }
}
