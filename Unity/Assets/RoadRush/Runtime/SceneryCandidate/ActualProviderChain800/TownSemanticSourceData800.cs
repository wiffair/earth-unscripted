using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Data-only disposition of one exact published Entry ordinal. Suppressed has
    /// no body barrier; its original source footprints remain placement hazards.
    /// Neither value describes an interior, roof/floor collision or door passage.
    /// </summary>
    public enum TownEntryDisposition800 { Suppressed, Emitted }

    /// <summary>Copied input geometry of an actual enabled nontrigger collider.</summary>
    public enum TownCollisionKind800 { TriangleMesh, Box }

    /// <summary>
    /// Defensive full-source data and published entry/body associations. No raw
    /// GeoRegion/Entry/GameObject/Mesh references escape a synchronous borrow.
    /// Arrays are privately owned and exposed through immutable value collections.
    /// The data can outlive revocation for worker cancellation/diagnostics, but is
    /// NEVER a current-source/ground/actor/Town authority without its live binding.
    /// </summary>
    public sealed class TownSemanticSourceData800
    {
        private readonly byte[] canonicalEnvelope;
        public IReadOnlyList<TownSourceBuilding800> OriginalBuildings { get; }
        public IReadOnlyList<TownSourceEntry800> Entries { get; }
        public Matrix4x4 Frame { get; }
        public string RuntimeWorldHandle { get; }
        public int WorldGeneration { get; }
        public int TownGeneration { get; }
        public string RawDocumentDigest { get; }
        public string SourceDigest { get; }
        public string ControlDigest { get; }
        public string OwnerLedgerDigest { get; }
        public string StructureLedgerDigest { get; }
        public string ScopeCoverageDigest { get; }
        public string FinalBufferDigest { get; }
        public string GeometryDigest { get; }
        public int CanonicalEnvelopeByteCount => canonicalEnvelope.Length;

        internal TownSemanticSourceData800(byte[] ownedEnvelope, TownSourceBuilding800[] ownedBuildings,
            TownSourceEntry800[] ownedEntries, Matrix4x4 frame, string worldHandle, int worldGeneration,
            int townGeneration, string[] digests)
        {
            canonicalEnvelope = ownedEnvelope;
            OriginalBuildings = Array.AsReadOnly(ownedBuildings);
            Entries = Array.AsReadOnly(ownedEntries);
            Frame = frame;
            RuntimeWorldHandle = worldHandle;
            WorldGeneration = worldGeneration;
            TownGeneration = townGeneration;
            RawDocumentDigest = digests[0];
            SourceDigest = digests[1];
            ControlDigest = digests[2];
            OwnerLedgerDigest = digests[3];
            StructureLedgerDigest = digests[4];
            ScopeCoverageDigest = digests[5];
            FinalBufferDigest = digests[6];
            GeometryDigest = digests[7];
        }

        /// <summary>
        /// Clone the complete canonical raw envelope, including roads/waters/areas
        /// and every original building. Worker-safe; bytes alone issue no source
        /// capability. Retain/recheck the actual binding at model commit/query.
        /// </summary>
        public byte[] CopyCanonicalSourceDocument() => (byte[])canonicalEnvelope.Clone();
    }

    /// <summary>
    /// One unchanged original building record, indexed only within this canonical
    /// envelope. Raw XYZ/height metadata is retained in metres, never treated as a
    /// game floor, residential classification or actual closed occupied volume.
    /// SourceIndex/SourceId are association/metadata, not a new provider identity.
    /// </summary>
    public sealed class TownSourceBuilding800
    {
        public int SourceIndex { get; }
        public string SourceId { get; }
        public string SourceKind { get; }
        public string SourceRoof { get; }
        public float RawSourceHeightMetres { get; }
        public bool RawMeasuredHeight { get; }
        public IReadOnlyList<Vector3> OriginalPoints { get; }

        internal TownSourceBuilding800(int index, GeoBuilding source)
        {
            SourceIndex = index;
            SourceId = source.id;
            SourceKind = source.kind;
            SourceRoof = source.roof;
            RawSourceHeightMetres = source.height;
            RawMeasuredHeight = source.measuredHeight;
            OriginalPoints = Array.AsReadOnly((Vector3[])source.points.Clone());
        }
    }

    /// <summary>
    /// Reference-exact original-source association captured synchronously. Entry
    /// ordinal and ContentKey never substitute for source identity or native body
    /// identity. A body is present ONLY when the actual publication's dictionary
    /// contains this exact Entry reference; no ledger name/count/JSON infers it.
    /// </summary>
    public sealed class TownSourceEntry800
    {
        public int EntryOrdinal { get; }
        public IReadOnlyList<int> OriginalSourceIndices { get; }
        public string DiagnosticContentKey { get; }
        public TownEntryDisposition800 Disposition { get; }
        public TownPublishedBody800 Body { get; }

        internal TownSourceEntry800(int ordinal, int[] ownedSourceIndices, string contentKey, TownPublishedBody800 body)
        {
            EntryOrdinal = ordinal;
            OriginalSourceIndices = Array.AsReadOnly(ownedSourceIndices);
            DiagnosticContentKey = contentKey;
            Body = body;
            Disposition = body == null ? TownEntryDisposition800.Suppressed : TownEntryDisposition800.Emitted;
        }
    }

    /// <summary>
    /// Copied collision input of an actual published body. Entity IDs are opaque
    /// transient Unity ulong IDs, valid only with the exact live publication/generations;
    /// they are not provider IDs or a registry mint. No native references survive.
    /// Excluded colliders were inactive/disabled/triggers, not body barriers at bind.
    /// </summary>
    public sealed class TownPublishedBody800
    {
        public ulong RuntimeBodyEntityId { get; }
        public IReadOnlyList<TownCollisionInput800> CollisionInputs { get; }
        public int ExcludedInactiveColliders { get; }
        public int ExcludedDisabledColliders { get; }
        public int ExcludedTriggerColliders { get; }

        internal TownPublishedBody800(ulong bodyId, TownCollisionInput800[] ownedInputs, int inactive, int disabled, int triggers)
        {
            RuntimeBodyEntityId = bodyId;
            CollisionInputs = Array.AsReadOnly(ownedInputs);
            ExcludedInactiveColliders = inactive;
            ExcludedDisabledColliders = disabled;
            ExcludedTriggerColliders = triggers;
        }
    }

    /// <summary>
    /// Source-local collider inputs copied on the issuing Unity main thread.
    /// LocalToWorld is exact binary32 by value; vertices/box dimensions are local
    /// metres. No fitting, whole-prefab scaling, winding repair or CPU vertex bake
    /// occurs. Mesh input is not a reverse-engineering of PhysX cooked geometry.
    /// These surfaces grant no closed-volume, roof, door or gaze visibility policy.
    /// </summary>
    public sealed class TownCollisionInput800
    {
        public TownCollisionKind800 Kind { get; }
        public ulong RuntimeColliderEntityId { get; }
        public ulong RuntimeMeshEntityId { get; }
        public int Layer { get; }
        public Matrix4x4 LocalToWorld { get; }
        public Vector3 BoxCentreMetres { get; }
        public Vector3 BoxSizeMetres { get; }
        public int MeshCookingOptions { get; }
        public IReadOnlyList<Vector3> MeshVertices { get; }
        public IReadOnlyList<int> TriangleIndices { get; }
        public IReadOnlyList<int> SubmeshIndexCounts { get; }

        internal TownCollisionInput800(TownCollisionKind800 kind, ulong colliderId, ulong meshId, int layer,
            Matrix4x4 frame, Vector3 boxCentre, Vector3 boxSize, int cookingOptions,
            Vector3[] ownedVertices, int[] ownedIndices, int[] ownedSubmeshCounts)
        {
            Kind = kind;
            RuntimeColliderEntityId = colliderId;
            RuntimeMeshEntityId = meshId;
            Layer = layer;
            LocalToWorld = frame;
            BoxCentreMetres = boxCentre;
            BoxSizeMetres = boxSize;
            MeshCookingOptions = cookingOptions;
            MeshVertices = Array.AsReadOnly(ownedVertices);
            TriangleIndices = Array.AsReadOnly(ownedIndices);
            SubmeshIndexCounts = Array.AsReadOnly(ownedSubmeshCounts);
        }
    }
}
