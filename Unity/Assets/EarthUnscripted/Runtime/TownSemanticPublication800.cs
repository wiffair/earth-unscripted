using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using EarthUnscripted.Core800;

namespace EarthUnscripted
{
    /// <summary>
    /// A live, world-issued publication of this explicit 800-metre town subset.
    /// It binds the actual semantic source and ground generation, not a diagnostic
    /// ledger flag. It grants no provider identity, facade registry or safe-world
    /// authority. SourceRegion/BuildingInputs are owner-held read references: callers
    /// must not mutate them. ValidateSourceNow is the commit gate for a fresh readmodel.
    /// </summary>
    public sealed class TownSemanticPublication800
    {
        private readonly object issuerKey;
        private readonly int mainThreadId;
        private readonly TownPublicationNotifications800 notifications = new TownPublicationNotifications800();
        private bool live;
        internal GeoWorld World { get; }
        internal CanonicalRaw13Document SourceInput { get; }
        internal GeoRegion SourceRegion { get; }
        internal BuildingRenderInput.Plan BuildingInputs { get; }
        public GroundLease800 Ground { get; }
        public string RuntimeWorldHandle { get; }
        public int WorldGeneration { get; }
        public int TownGeneration { get; }
        public int SourceBuildingCount { get; }
        public Matrix4x4 Frame { get; }
        public string RawDocumentDigest => Ground.RawDocumentDigest;
        public string SourceDigest => Ground.SourceDigest;
        public string ControlDigest => Ground.ControlDigest;
        public string GeometryDigest => Ground.GeometryDigest;
        public string OwnerLedgerDigest => Ground.OwnerLedgerDigest;
        public string StructureLedgerDigest => Ground.StructureLedgerDigest;
        public string ScopeCoverageDigest => Ground.ScopeCoverageDigest;
        public string FinalBufferDigest => Ground.FinalBufferDigest;
        public bool SourceSubsetOnly => true;
        public bool WholeWorldAdmitted => false;
        public bool ClosedBuildingVolumes => false;

        // Only the actual GeoWorld publication path knows this per-world key.
        internal TownSemanticPublication800(GeoWorld world, object key, CanonicalRaw13Document source, GroundLease800 ground, GeoRegion region, BuildingRenderInput.Plan inputs, string worldHandle, int worldGeneration, int townGeneration, Matrix4x4 frame)
        {
            world.AuthorizeTownPublication800(key, source, ground, region, inputs, worldGeneration, townGeneration);
            World = world;
            issuerKey = key;
            SourceInput = source;
            Ground = ground;
            SourceRegion = region;
            BuildingInputs = inputs;
            RuntimeWorldHandle = worldHandle;
            WorldGeneration = worldGeneration;
            TownGeneration = townGeneration;
            SourceBuildingCount = inputs.SourceCount;
            Frame = frame;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            live = true;
        }

        /// <summary>Main-thread liveness/reference/body validation, cached once per frame. Full source data is immutable and revalidated at publication and binding.</summary>
        public bool IsCurrent
        {
            get
            {
                RequireMainThread();
                return live && World.CheckTownPublication800(this, false);
            }
        }

        /// <summary>
        /// Force fresh source/body witness validation before committing a semantic
        /// snapshot, including same-frame mutations. This cannot issue a publication.
        /// </summary>
        public bool ValidateSourceNow()
        {
            RequireMainThread();
            return live && World.CheckTownPublication800(this, true);
        }

        /// <summary>
        /// Subscribe on the main thread. Invalidation happens BEFORE callbacks;
        /// cleanup must not check IsCurrent inside the callback. Every registered
        /// callback is attempted before owned town geometry is disabled/destroyed.
        /// </summary>
        public IDisposable BeforeRevoke(Action<TownSemanticPublication800, string> callback)
        {
            RequireMainThread();
            if (callback == null || !IsCurrent)
                throw new InvalidOperationException("Live town publication callback required");
            return notifications.Subscribe(reason => callback(this, reason));
        }

        /// <summary>Defensive full raw envelope copy for readmodel input, never a standalone authority flag.</summary>
        public byte[] CopySemanticSourceDocument(CancellationToken cancellation = default)
        {
            RequireMainThread();
            cancellation.ThrowIfCancellationRequested();
            return World.CopyTownSourceForBinding800(this, cancellation);
        }

        public string[] CopyRevocationCallbackErrors()
        {
            RequireMainThread();
            return notifications.CopyErrors();
        }

        internal void InvalidateAndNotify(object key, string reason)
        {
            RequireMainThread();
            if (!ReferenceEquals(key, issuerKey))
                throw new InvalidOperationException("Only the actual world issuer can retire this publication");
            if (!live)
                return;
            live = false;
            notifications.Retire(reason);
        }

        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Town publication reads/cleanup require the issuing main thread");
        }
    }
}
