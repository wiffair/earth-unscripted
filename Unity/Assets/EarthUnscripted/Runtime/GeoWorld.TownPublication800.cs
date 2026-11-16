using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using EarthUnscripted.Core800;

namespace EarthUnscripted
{
    public sealed partial class GeoWorld
    {
        // Runtime identity is deliberately unrelated to provider IDs/content keys.
        private readonly object townSemanticIssuerKey800 = new object ();
        private readonly string townSemanticWorldHandle800 = Guid.NewGuid().ToString("N");
        private TownSemanticState800 townSemanticState800;
        private TownSemanticPublication800 townSemanticPublication800;
        private bool townResetInProgress800;
        private sealed class TownSemanticState800
        {
            internal CanonicalRaw13Document Source;
            internal PreparedWorldMeshPlan800 Plan;
            internal GroundLease800 Ground;
            internal World13Document Document;
            internal GeoRegion Region;
            internal BuildingRenderInput.Plan Inputs;
            internal int WorldGeneration;
            internal int TownGeneration;
            internal Matrix4x4 Frame;
            internal IDisposable GroundSubscription;
            internal bool Committed;
            internal int LiveValidationFrame = int.MinValue;
            internal GeoBuilding[] BuildingArray;
            internal GeoRoad[] RoadArray;
            internal BuildingRenderInput.Entry[] EntryArray;
            internal GeoBuilding[][] EntryOriginalReferences;
            internal GeoBuilding[] EntryGeometryReferences;
            internal string[] EntryGeometryCommits;
            internal GeoBuilding[] RawBuildingReferences;
            internal GeoRoad[] RawRoadReferences;
            internal BuildingRenderInput.Entry[] EntryReferences;
            internal readonly Dictionary<BuildingRenderInput.Entry, GameObject> Bodies = new Dictionary<BuildingRenderInput.Entry, GameObject>();
            internal BodyWitness800[] BodyWitnesses = Array.Empty<BodyWitness800>();
        }

        private sealed class BodyWitness800
        {
            internal GameObject Root;
            internal Matrix4x4 Frame;
            // Exact component membership binds additions/removals, not just existing meshes.
            internal Collider[] Membership;
            internal BoxCollider[] Boxes;
            internal Vector3[] BoxCentres, BoxSizes;
            internal Matrix4x4[] BoxFrames;
            internal MeshCollider[] Colliders;
            internal MeshFilter[] Filters;
            internal GeneratedMeshOwner[] Owners;
            internal Mesh[] Meshes;
            internal bool[] MeshConvex;
            internal Matrix4x4[] ColliderFrames;
        }

        /// <summary>
        /// Only the typed GroundLease path can populate this property. A successful
        /// old Func-based diagnostic town still returns null here.
        /// </summary>
        public TownSemanticPublication800 CurrentTownPublication800
        {
            get
            {
                var publication = townSemanticPublication800;
                return publication != null && publication.IsCurrent ? publication : null;
            }
        }

        private static bool SameTownFrame800(Matrix4x4 first, Matrix4x4 second)
        {
            for (int i = 0; i < 16; i++)
                if (BitConverter.SingleToInt32Bits(first[i]) != BitConverter.SingleToInt32Bits(second[i]))
                    return false;
            return true;
        }

        private void RequireTypedTownGround800(CanonicalRaw13Document source, PreparedWorldMeshPlan800 plan, GroundLease800 ground)
        {
            if (source == null || plan == null || ground == null || !ground.IsCurrent || !ground.OwnsPlan(plan) || !ground.SourceSubsetOnly || ground.FullWorldAdmitted)
                throw new InvalidDataException("Actual live ground must own this exact prepared plan object");
            string[] actual =
            {
                ground.RawDocumentDigest,
                ground.SourceDigest,
                ground.ControlDigest,
                ground.OwnerLedgerDigest,
                ground.StructureLedgerDigest,
                ground.ScopeCoverageDigest,
                ground.FinalBufferDigest,
                ground.GeometryDigest
            };
            string[] expected =
            {
                plan.RawDocumentDigest,
                plan.SourceDigest,
                plan.ControlDigest,
                plan.OwnerLedgerDigest,
                plan.StructureLedgerDigest,
                plan.ScopeCoverageDigest,
                plan.FinalBufferDigest,
                plan.GeometryDigest
            };
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal) || !SameTownFrame800(ground.Frame, transform.localToWorldMatrix))
                throw new InvalidDataException("Town/ground source, control, geometry, scope or frame mismatch");
        }

        private void BeginTypedTown800(CanonicalRaw13Document source, PreparedWorldMeshPlan800 plan, GroundLease800 ground, int worldGeneration, int townGeneration)
        {
            RequireTypedTownGround800(source, plan, ground);
            var state = new TownSemanticState800
            {
                Source = source,
                Plan = plan,
                Ground = ground,
                WorldGeneration = worldGeneration,
                TownGeneration = townGeneration,
                Frame = transform.localToWorldMatrix
            };
            townSemanticState800 = state;
            // Subscribe BEFORE source transfer/worker start. The physical owner has
            // already invalidated its lease when notification runs: never test
            // Ground.IsCurrent in this cleanup callback.
            state.GroundSubscription = ground.BeforeRevoke((revoked, reason) =>
            {
                if (ReferenceEquals(townSemanticState800, state) && ReferenceEquals(revoked, state.Ground) && generation == state.WorldGeneration && townLifetime800 == state.TownGeneration)
                    RevokeScopedTown800Support();
            });
            if (!ground.IsCurrent)
                throw new OperationCanceledException("Ground revoked during town subscription");
        }

        private void BindTypedTownData800(World13Document document, BuildingRenderInput.Plan inputs)
        {
            var state = townSemanticState800;
            if (state == null)
                return;
            state.Document = document;
            state.Region = document.geography;
            state.Inputs = inputs;
            state.BuildingArray = state.Region.buildings;
            state.RoadArray = state.Region.roads;
            state.EntryArray = inputs.Entries;
            state.EntryOriginalReferences = inputs.Entries.Select(e => (GeoBuilding[])e.OriginalSources.Clone()).ToArray();
            state.EntryGeometryReferences = inputs.Entries.Select(e => e.Geometry).ToArray();
            state.EntryGeometryCommits = inputs.Entries.Select(e => e.Geometry == null ? null : SceneryCandidate.OwnedFacadeHash.RenderGeometry(e.Geometry)).ToArray();
            state.RawBuildingReferences = (GeoBuilding[])state.Region.buildings.Clone();
            state.RawRoadReferences = (GeoRoad[])state.Region.roads.Clone();
            state.EntryReferences = (BuildingRenderInput.Entry[])inputs.Entries.Clone();
            ValidateTypedTownSource800(state);
        }

        private void RecordTypedTownBody800(BuildingRenderInput.Entry entry, GameObject body)
        {
            var state = townSemanticState800;
            if (state != null)
                state.Bodies.Add(entry, body);
        }

        private void ValidateTypedTownSource800(TownSemanticState800 state)
        {
            if (state.Region == null || state.Region.buildings == null || state.Region.roads == null || state.Inputs == null || state.Inputs.Entries == null)
                throw new InvalidDataException("Missing owned town source arrays");
            ScopedTownLineage800.RequireComplete(state.Inputs);
            if (!ReferenceEquals(Region, state.Region) || !ReferenceEquals(BuildingInputs, state.Inputs) || state.Inputs.SourceCount != state.Region.buildings.Length || !ReferenceEquals(state.BuildingArray, state.Region.buildings) || !ReferenceEquals(state.RoadArray, state.Region.roads) || !ReferenceEquals(state.EntryArray, state.Inputs.Entries) || !state.RawBuildingReferences.SequenceEqual(state.Region.buildings) || !state.RawRoadReferences.SequenceEqual(state.Region.roads) || !state.EntryReferences.SequenceEqual(state.Inputs.Entries) || World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(state.Document)) != state.Plan.RawDocumentDigest || RoadSourceIdentity.Compute(state.Region.roads) != state.Plan.SourceDigest)
                throw new InvalidDataException("Owned semantic source/input changed; no town publication");
            for (int i = 0; i < state.Inputs.Entries.Length; i++)
            {
                var entry = state.Inputs.Entries[i];
                if (!entry.OriginalSources.SequenceEqual(state.EntryOriginalReferences[i]) || !ReferenceEquals(entry.Geometry, state.EntryGeometryReferences[i]) || (entry.Geometry == null ? null : SceneryCandidate.OwnedFacadeHash.RenderGeometry(entry.Geometry)) != state.EntryGeometryCommits[i])
                    throw new InvalidDataException("Owned normalized entry/source binding changed");
            }
        }

        private void PublishTypedTown800(Action<TownSemanticPublication800> completed)
        {
            var state = townSemanticState800;
            if (state == null)
                return; // Old harness path must never issue a token.
            if (townSemanticPublication800 != null)
                throw new InvalidOperationException("Retire previous publication before replacement");
            ValidateTypedTownSource800(state);
            RequireTypedTownGround800(state.Source, state.Plan, state.Ground);
            if (!scopedTownRoot800 || !scopedTownRoot800.activeInHierarchy || generation != state.WorldGeneration || townLifetime800 != state.TownGeneration || townLedger800.buildings.Count != state.Inputs.Entries.Length || townLedger800.buildings.Any(row => row.status != "Emitted" && row.status != "Suppressed"))
                throw new InvalidDataException("Actual complete active town/terminal source balance required");
            for (int i = 0; i < state.Inputs.Entries.Length; i++)
            {
                var entry = state.Inputs.Entries[i];
                bool emitted = state.Bodies.TryGetValue(entry, out var body);
                if (emitted != (townLedger800.buildings[i].status == "Emitted") || emitted && (!body || !body.activeInHierarchy || body.GetComponentsInChildren<MeshCollider>(true).Length == 0))
                    throw new InvalidDataException("Emission disposition must match actual owned physical body");
            }

            state.BodyWitnesses = state.Bodies.Values.Select(CaptureTownBodyWitness800).ToArray();
            state.Committed = true;
            townSemanticPublication800 = new TownSemanticPublication800(this, townSemanticIssuerKey800, state.Source, state.Ground, state.Region, state.Inputs, townSemanticWorldHandle800, state.WorldGeneration, state.TownGeneration, state.Frame);
            completed?.Invoke(townSemanticPublication800);
        }

        private static BodyWitness800 CaptureTownBodyWitness800(GameObject body)
        {
            var membership = body.GetComponentsInChildren<Collider>(true);
            // Physical access primitives may be boxes. Unknown shapes need an explicit
            // witness before they can participate in this semantic publication.
            if (membership.Any(c => !(c is MeshCollider) && !(c is BoxCollider)))
                throw new InvalidDataException("Unsupported owned town collider type");
            var boxes = membership.OfType<BoxCollider>().ToArray();
            var colliders = membership.OfType<MeshCollider>().ToArray();
            var witness = new BodyWitness800
            {
                Root = body,
                Frame = body.transform.localToWorldMatrix,
                Membership = membership,
                Boxes = boxes,
                BoxCentres = boxes.Select(c => c.center).ToArray(),
                BoxSizes = boxes.Select(c => c.size).ToArray(),
                BoxFrames = boxes.Select(c => c.transform.localToWorldMatrix).ToArray(),
                Colliders = colliders,
                Filters = colliders.Select(c => c.GetComponent<MeshFilter>()).ToArray(),
                Owners = colliders.Select(c => c.GetComponent<GeneratedMeshOwner>()).ToArray(),
                Meshes = colliders.Select(c => c.sharedMesh).ToArray(),
                MeshConvex = colliders.Select(c => c.convex).ToArray(),
                ColliderFrames = colliders.Select(c => c.transform.localToWorldMatrix).ToArray()
            };
            if (!TownBodyWitnessCurrent800(witness))
                throw new InvalidDataException("Actual town collider/generated-mesh witness incomplete");
            return witness;
        }

        private static bool TownBodyWitnessCurrent800(BodyWitness800 witness)
        {
            if (!witness.Root || !witness.Root.activeInHierarchy || !SameTownFrame800(witness.Frame, witness.Root.transform.localToWorldMatrix))
                return false;
            // Re-enumerate actual descendants on the existing once-per-frame/fresh-bind
            // path. An added collider cannot hide outside the original witness list.
            if (!witness.Membership.SequenceEqual(witness.Root.GetComponentsInChildren<Collider>(true)))
                return false;
            for (int i = 0; i < witness.Boxes.Length; i++)
            {
                var box = witness.Boxes[i];
                if (!box || !box.enabled || box.isTrigger || !box.gameObject.activeInHierarchy ||
                    !FiniteTownBox800(box.center, box.size) ||
                    !SameTownVectorBits800(witness.BoxCentres[i], box.center) ||
                    !SameTownVectorBits800(witness.BoxSizes[i], box.size) ||
                    !SameTownFrame800(witness.BoxFrames[i], box.transform.localToWorldMatrix))
                    return false;
            }
            for (int i = 0; i < witness.Colliders.Length; i++)
                if (!witness.Colliders[i] || !witness.Colliders[i].enabled || !witness.Colliders[i].gameObject.activeInHierarchy || witness.Colliders[i].isTrigger || witness.Colliders[i].convex != witness.MeshConvex[i] || !witness.Filters[i] || !witness.Owners[i] || !witness.Meshes[i] || witness.Colliders[i].sharedMesh != witness.Meshes[i] || witness.Filters[i].sharedMesh != witness.Meshes[i] || witness.Owners[i].Mesh != witness.Meshes[i] || !SameTownFrame800(witness.ColliderFrames[i], witness.Colliders[i].transform.localToWorldMatrix))
                    return false;
            return true;
        }

        private static bool FiniteTownBox800(Vector3 centre, Vector3 size)
        {
            for (int axis = 0; axis < 3; axis++)
                if (float.IsNaN(centre[axis]) || float.IsInfinity(centre[axis]) ||
                    float.IsNaN(size[axis]) || float.IsInfinity(size[axis]) || size[axis] <= 0)
                    return false;
            return true;
        }

        private static bool SameTownVectorBits800(Vector3 first, Vector3 second)
        {
            for (int axis = 0; axis < 3; axis++)
                if (BitConverter.SingleToInt32Bits(first[axis]) != BitConverter.SingleToInt32Bits(second[axis]))
                    return false;
            return true;
        }

        internal void AuthorizeTownPublication800(object key, CanonicalRaw13Document source, GroundLease800 ground, GeoRegion region, BuildingRenderInput.Plan inputs, int worldGeneration, int townGeneration)
        {
            var state = townSemanticState800;
            if (!ReferenceEquals(key, townSemanticIssuerKey800) || state == null || !state.Committed || !ReferenceEquals(state.Source, source) || !ReferenceEquals(state.Ground, ground) || !ReferenceEquals(state.Region, region) || !ReferenceEquals(state.Inputs, inputs) || worldGeneration != generation || townGeneration != townLifetime800 || !scopedTownRoot800 || !scopedTownRoot800.activeInHierarchy || !ground.IsCurrent)
                throw new InvalidOperationException("Only this actual completed town can issue publication");
        }

        internal bool CheckTownPublication800(TownSemanticPublication800 publication, bool forceSourceValidation)
        {
            if (Thread.CurrentThread.ManagedThreadId != townMainThread800)
                throw new InvalidOperationException("Town publication validation requires main thread");
            var state = townSemanticState800;
            if (!ReferenceEquals(publication, townSemanticPublication800) || state == null)
                return false;
            bool valid = state.Committed && generation == state.WorldGeneration && townLifetime800 == state.TownGeneration && ReferenceEquals(Region, state.Region) && ReferenceEquals(BuildingInputs, state.Inputs) && scopedTownRoot800 && scopedTownRoot800.activeInHierarchy && SameTownFrame800(state.Frame, transform.localToWorldMatrix) && state.Ground.IsCurrent && state.Ground.OwnsPlan(state.Plan);
            if (valid && (forceSourceValidation || state.LiveValidationFrame != Time.frameCount))
            {
                valid = ReferenceEquals(state.BuildingArray, state.Region.buildings) && ReferenceEquals(state.RoadArray, state.Region.roads) && ReferenceEquals(state.EntryArray, state.Inputs.Entries) && state.Region.buildings != null && state.Region.roads != null && state.Inputs.Entries != null && state.RawBuildingReferences.SequenceEqual(state.Region.buildings) && state.RawRoadReferences.SequenceEqual(state.Region.roads) && state.EntryReferences.SequenceEqual(state.Inputs.Entries) && state.BodyWitnesses.All(TownBodyWitnessCurrent800);
                state.LiveValidationFrame = Time.frameCount;
            }

            if (valid && forceSourceValidation)
            {
                try
                {
                    ValidateTypedTownSource800(state);
                }
                catch (Exception exception)when (!(exception is OperationCanceledException))
                {
                    valid = false;
                }
            }

            // Ground.IsCurrent may synchronously notify consumers. A later scene
            // coordinator callback can start another generation; this OLD check
            // must never retire that replacement.
            if (!valid && ReferenceEquals(townSemanticState800, state) && ReferenceEquals(townSemanticPublication800, publication))
                ResetScopedTown800();
            return valid;
        }

        internal byte[] CopyTownSourceForBinding800(TownSemanticPublication800 publication, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!CheckTownPublication800(publication, true))
                throw new InvalidOperationException("Expired/changed town cannot bind a semantic snapshot");
            var bytes = CanonicalGeography13.WriteBytes(townSemanticState800.Document);
            cancellation.ThrowIfCancellationRequested();
            return bytes; // Fresh buffer, no mutable owned references escape.
        }

        // Trusted in-assembly readmodel construction only. References are exclusively
        // owned by this town and may only be borrowed until the next main-thread yield.
        internal (GeoRegion Region, BuildingRenderInput.Plan Inputs, IReadOnlyDictionary<BuildingRenderInput.Entry, GameObject> EmittedBodies) BorrowTownSourceForBinding800(TownSemanticPublication800 publication)
        {
            if (!CheckTownPublication800(publication, true))
                throw new InvalidOperationException("Expired town semantic source");
            var state = townSemanticState800;
            return (state.Region, state.Inputs, state.Bodies);
        }

        private void RetireTownPublication800(string reason)
        {
            var publication = townSemanticPublication800;
            townSemanticPublication800 = null; // Revoke lookup BEFORE consumer callbacks.
            var state = townSemanticState800;
            if (state != null)
                state.Committed = false;
            try
            {
                publication?.InvalidateAndNotify(townSemanticIssuerKey800, reason);
            }
            finally
            {
                townSemanticState800 = null;
                state?.GroundSubscription?.Dispose();
            }
        }
    }
}
