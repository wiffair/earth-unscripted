using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RoadRush.FunnyAnimals.Admission;
using RoadRush.FunnyAnimals.Semantics800Proposal;
using UnityEngine;

namespace RoadRush.Scenery800
{
    /// <summary>
    /// Actual town-publication consumer for immutable original-source placement
    /// hazards. It does not construct publications, replace their owners or issue
    /// vehicle, collision, habitat or ISafeWorld authority.
    /// </summary>
    /// <remarks>
    /// Prepare and queries are main-thread only. Copy the actual published raw
    /// envelope before yielding; worker code receives bytes and an existing
    /// reviewed input, never borrowed Region/Entry/GameObject references.
    /// Positions and radii are anchored world XZ metres. The source's original
    /// building polygons remain hazards even when their rendering was suppressed.
    /// Actual physical walls/steps and vehicle presentation are separate bindings.
    /// </remarks>
    public sealed class PublishedSourceHazards800 : IDisposable
    {
        TownSemanticPublication800 publication;
        ReviewedCanonicalRaw13Input reviewedInput;
        IDisposable revocationSubscription;
        CancellationTokenSource workerCancellation;
        sealed class PreparedSourceModel
        {
            internal readonly SourceHazards800.Prepared Hazards;
            internal readonly RasterHabitat800 Raster;
            internal PreparedSourceModel(SourceHazards800.Prepared hazards, RasterHabitat800 raster)
            { Hazards = hazards; Raster = raster; }
        }
        Task<PreparedSourceModel> worker;
        RasterHabitat800 raster;
        SourceHazards800.Prepared model;
        WorkBudget queryBudget;
        int request;
        int lastPreparationFrame = -1;
        int queryFrame = -1;
        bool refused = true;

        public bool StaticModelPrepared => !refused && model != null;
        public bool GameplaySemanticsBound => false;
        public string Reason { get; private set; } = "Actual town publication unbound";
        public TownSemanticPublication800 Publication => publication;

        /// <summary>
        /// Prepare from a private-issued actual publication, not exported ledger
        /// JSON or a consumable source handle already transferred into Town.
        /// Busy replacement invalidates the old request immediately; it never
        /// launches another worker until the retained old task is terminal.
        /// </summary>
        public IEnumerator Prepare(TownSemanticPublication800 actualPublication,
            ReviewedCanonicalRaw13Input actualReviewedInput, CancellationToken cancellation = default)
        {
            if (lastPreparationFrame == Time.frameCount || worker != null && !worker.IsCompleted)
            {
                if (!ReferenceEquals(publication, actualPublication)
                    || !ReferenceEquals(reviewedInput, actualReviewedInput))
                {
                    request++;
                    Refuse("Different publication/input requested while preparation is busy");
                }
                yield break;
            }
            lastPreparationFrame = Time.frameCount;
            Dispose();
            if (actualPublication == null || actualReviewedInput == null
                || !actualPublication.IsCurrent || !actualPublication.SourceSubsetOnly
                || actualPublication.WholeWorldAdmitted || !actualPublication.Frame.Equals(Matrix4x4.identity)
                || actualPublication.ScopeCoverageDigest != SourceScope800.ReviewedDigest)
                throw new InvalidOperationException("Actual current reviewed800 publication required");

            publication = actualPublication;
            reviewedInput = actualReviewedInput;
            refused = false;
            int capturedRequest = ++request;
            revocationSubscription = publication.BeforeRevoke((expired, reason) =>
            {
                // The token is already invalid here. Cleanup must not ask IsCurrent.
                if (capturedRequest == request && ReferenceEquals(expired, publication))
                    Refuse("Town retired before geometry teardown: " + reason);
            });

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            workerCancellation = linkedCancellation;
            bool committed = false;
            try
            {
                // This actual API forces fresh source/body validation and returns
                // an independent FULL envelope, including suppressed originals.
                byte[] canonicalBytes = publication.CopySemanticSourceDocument(linkedCancellation.Token);
                string rawDigest = publication.RawDocumentDigest;
                string roadDigest = publication.SourceDigest;
                CancellationToken workerToken = linkedCancellation.Token;
                worker = Task.Run(() =>
                {
                    workerToken.ThrowIfCancellationRequested();
                    if (World13DocumentCodec.Hash(canonicalBytes) != rawDigest)
                        throw new InvalidDataException("Publication/source document digest mismatch");
                    var privateSource = CanonicalRaw13Codec.Read(actualReviewedInput,
                        canonicalBytes, workerToken);
                    // Reuse the exact verified full-polygon/source builder. Its
                    // second defensive clone preserves existing source guards;
                    // allocation/serialization cost remains a bind-time gate.
                    var hazards = SourceHazards800.BuildPrivate(privateSource, actualReviewedInput,
                        rawDigest, roadDigest, workerToken);
                    // This is the FULL private canonical clone, not borrowed World.Region.
                    // The separate source builder consumed its own defensive clone.
                    var privateRegion = privateSource.TakeOwnedDocument(workerToken).geography;
                    if (!RasterHabitatSourceCopy800.TryCopyFromPrivateCanonicalClone(privateRegion, out var copiedRaster))
                        throw new InvalidDataException("Complete bounded source raster copy refused");
                    workerToken.ThrowIfCancellationRequested();
                    return new PreparedSourceModel(hazards, copiedRaster);
                }, workerToken);
                Task<PreparedSourceModel> capturedWorker = worker;
                capturedWorker.ContinueWith(faulted => { var observed = faulted.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                while (!capturedWorker.IsCompleted)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (capturedRequest != request || !Current())
                        throw new OperationCanceledException("Actual town/source binding replaced");
                    yield return null;
                }
                PreparedSourceModel prepared = capturedWorker.GetAwaiter().GetResult();
                cancellation.ThrowIfCancellationRequested();
                if (capturedRequest != request || !Current() || !publication.ValidateSourceNow()
                    || capturedRequest != request || refused || workerToken.IsCancellationRequested
                    || !ReferenceEquals(publication, actualPublication)
                    || !ReferenceEquals(reviewedInput, actualReviewedInput))
                    throw new OperationCanceledException("Actual publication changed before model commit");
                model = prepared.Hazards;
                raster = prepared.Raster;
                committed = true;
                Reason = "Published source model prepared; other safety bindings remain required";
            }
            finally
            {
                linkedCancellation.Cancel();
                if (ReferenceEquals(workerCancellation, linkedCancellation)) workerCancellation = null;
                if (!committed && capturedRequest == request) Refuse("Published source preparation refused/cancelled");
            }
        }

        bool SpendQuery(P from, P to, double radius)
        {
            if (queryFrame != Time.frameCount)
            {
                queryFrame = Time.frameCount;
                queryBudget = new WorkBudget();
            }
            // Bound repeated failures before calling a live owner. Full source
            // serialization occurs at bind/commit, never every logical query.
            return queryBudget.Use(ref queryBudget.Obstacles) && model != null
                && SourceScope800.Allows(from, to, radius) && Current();
        }

        public Verdict InspectSourceHazards(P from, P to, double radius)
        {
            if (!SpendQuery(from, to, radius)) return Verdict.Unknown;
            Verdict result = model.Index.Query(from, to, radius, queryBudget);
            if (result == Verdict.Clear && model.Areas.InspectUnknown(from, to, radius, queryBudget) != Verdict.Clear)
                return Verdict.Unknown;
            return Current() ? result : Verdict.Unknown;
        }

        public bool TryInspectAreaFacts(P from, P to, double radius, out AreaFacts800 facts)
        {
            facts = AreaFacts800.None;
            bool success = SpendQuery(from, to, radius)
                && model.Areas.TryInspect(from, to, radius, queryBudget, out facts) && Current();
            if (!success) facts = AreaFacts800.None;
            return success;
        }

        public bool TryWholeCapsuleInside(AreaKind800 requiredKind, P from, P to,
            double radius, out bool inside)
        {
            inside = false;
            bool success = SpendQuery(from, to, radius)
                && model.Areas.TryWholeCapsuleInside(requiredKind, from, to, radius, queryBudget, out inside)
                && Current();
            if (!success) inside = false;
            return success;
        }

        /// <summary>
        /// Strict whole-capsule raster predicate, separate from source-area polygons
        /// and all water/building/ground/actor gates. Query is fresh on every call,
        /// spends the existing shared source query gate BEFORE owner reads, and holds
        /// no mutable World.Region or dynamic result cache. No placement grant.
        /// </summary>
        public Verdict InspectRasterHabitat(int actualKind, float fromX, float fromZ,
            float toX, float toZ, float radiusMetres)
        {
            var from = new P(fromX, fromZ);
            var to = new P(toX, toZ);
            if (!SpendQuery(from, to, radiusMetres)) return Verdict.Unknown;
            var capturedRaster = raster;
            if (capturedRaster == null) return Verdict.Unknown;
            var observation = capturedRaster.Query(actualKind, fromX, fromZ, toX, toZ, radiusMetres);
            if (!Current() || !ReferenceEquals(capturedRaster, raster)) return Verdict.Unknown;
            switch (observation.Result)
            {
                case RasterHabitat800.Verdict.Clear: return Verdict.Clear;
                case RasterHabitat800.Verdict.Blocked: return Verdict.Blocked;
                default: return Verdict.Unknown;
            }
        }

        bool Current()
        {
            int capturedRequest = request;
            TownSemanticPublication800 capturedPublication = publication;
            ReviewedCanonicalRaw13Input capturedInput = reviewedInput;
            try
            {
                if (!refused && capturedPublication != null && capturedPublication.IsCurrent
                    && capturedPublication.Ground.IsCurrent && StillSameBinding()) return true;
            }
            catch { }
            // An owner check can retire A and synchronously bind B. This OLD
            // stack frame must neither cancel B's worker nor clear its model.
            if (StillSameBinding()) Refuse("Actual town/ground publication no longer current");
            return false;

            bool StillSameBinding() => capturedRequest == request
                && ReferenceEquals(capturedPublication, publication)
                && ReferenceEquals(capturedInput, reviewedInput);
        }

        void Refuse(string reason)
        {
            refused = true;
            model = null;
            raster = null;
            Reason = reason;
            workerCancellation?.Cancel();
        }

        public void Dispose()
        {
            request++;
            Refuse("Actual town publication unbound");
            revocationSubscription?.Dispose();
            revocationSubscription = null;
            publication = null;
            reviewedInput = null;
            // Retain worker and frame gates: cancellation is not termination,
            // and dispose/rebind cannot create unlimited same-frame queries.
        }
    }
}
