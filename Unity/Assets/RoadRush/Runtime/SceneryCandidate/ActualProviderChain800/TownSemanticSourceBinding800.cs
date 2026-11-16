using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using RoadRush.Core800;
using UnityEngine;

namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Trusted in-assembly readmodel binding to an ACTUAL world-issued Town token
    /// and the exact expected uploaded ground lease. This is not a new issuer,
    /// source registry, facade admission, closed-volume or ISafeWorld capability.
    /// Capture and all liveness/commit/disposal calls run on the issuing main
    /// thread. Snapshot and LifetimeCancellation may be used by managed workers.
    /// </summary>
    public sealed class TownSemanticSourceBinding800 : IDisposable
    {
        private const int MaximumLocalErrors = 16;
        private const int MaximumErrorCharacters = 16384;
        private readonly GeoWorld world;
        private readonly int mainThreadId;
        private readonly CancellationTokenSource lifetimeCancellation;
        private readonly CancellationToken workerCancellation;
        private readonly List<string> localErrors = new List<string>();
        private IDisposable townRevocation;
        private IDisposable groundRevocation;
        private bool invalidated;
        private bool disposed;
        private TownSemanticSourceData800 snapshot;

        /// <summary>The exact existing publication; never reconstructed from data.</summary>
        public TownSemanticPublication800 Publication { get; }
        /// <summary>Exact existing physical owner lease, not an equal-digest substitute.</summary>
        public GroundLease800 Ground { get; }
        /// <summary>Immutable copied data only; presence does not establish currency.</summary>
        public TownSemanticSourceData800 Snapshot => snapshot;
        /// <summary>Worker-safe cancellation; callbacks must not perform Unity work.</summary>
        public CancellationToken LifetimeCancellation => workerCancellation;
        public string Reason { get; private set; } = "Capturing";

        private TownSemanticSourceBinding800(GeoWorld world, TownSemanticPublication800 publication,
            GroundLease800 ground, CancellationToken cancellation)
        {
            this.world = world;
            Publication = publication;
            Ground = ground;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            workerCancellation = lifetimeCancellation.Token;
        }

        /// <summary>
        /// Synchronously copy a complete current publication before any yield.
        /// The internal World borrow is stack-local only. Failure returns no
        /// binding and retires only this consumer, never the Town/ground owner.
        /// Null/mismatched/expired data, unknown active collider types/layouts,
        /// geometry/source budgets and cancellation refuse; no partial fallback.
        /// This method must compile in the same assembly as the actual Town API.
        /// </summary>
        public static bool TryCapture(GeoWorld world, GroundLease800 expectedGround,
            CancellationToken cancellation, out TownSemanticSourceBinding800 binding, out string reason)
        {
            binding = null;
            TownSemanticSourceBinding800 candidate = null;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (ReferenceEquals(world, null) || expectedGround == null)
                    throw new InvalidDataException("Actual World and expected GroundLease800 required");
                var publication = world.CurrentTownPublication800;
                if (publication == null || !ReferenceEquals(publication.Ground, expectedGround))
                    throw new InvalidDataException("Actual current Town publication must own exact expected ground reference");
                candidate = new TownSemanticSourceBinding800(world, publication, expectedGround, cancellation);
                candidate.RequireCurrent(false);
                // Authority is already false in these callbacks. Invalidate/cancel
                // unconditionally for the exact captured objects; never query it.
                candidate.townRevocation = publication.BeforeRevoke((expired, why) =>
                {
                    if (ReferenceEquals(expired, candidate.Publication)) candidate.Invalidate("Town revoked: " + why);
                });
                candidate.groundRevocation = expectedGround.BeforeRevoke((expired, why) =>
                {
                    if (ReferenceEquals(expired, candidate.Ground)) candidate.Invalidate("Ground revoked: " + why);
                });
                candidate.RequireCurrent(true);
                candidate.snapshot = candidate.CaptureImmutableData(candidate.workerCancellation);
                if (!candidate.CommitSourceNow(out reason))
                    throw new InvalidDataException(reason);
                candidate.Reason = "Bound copied source/body data; no gameplay admission";
                binding = candidate;
                reason = candidate.Reason;
                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException) && !(exception is StackOverflowException))
            {
                candidate?.Dispose();
                reason = "Town semantic capture refused: " + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Main-thread cheap currency for normal composite queries. Actual token
        /// validates world/request/ground/frame and its once-per-frame body witness;
        /// this never serializes the full raw document per logical query.
        /// Authorized mutations must retire their actual owner first.
        /// </summary>
        public bool IsCurrent
        {
            get
            {
                RequireMainThread();
                if (invalidated || workerCancellation.IsCancellationRequested || snapshot == null) return false;
                try
                {
                    RequireCurrent(false);
                    return true;
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException) && !(exception is StackOverflowException))
                {
                    Invalidate("Town semantic currency refused: " + exception.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// Main-thread fresh bind/model-commit gate: force actual source/body
        /// validation and compare captured eight digests/frame. Call after worker
        /// completion before publishing a model. Does not issue/replace a token.
        /// </summary>
        public bool CommitSourceNow(out string reason)
        {
            RequireMainThread();
            try
            {
                if (snapshot == null) throw new InvalidOperationException("Complete captured source data required");
                RequireCurrent(true);
                if (!TownSourceCapturePolicy800.SameFrame(snapshot.Frame, Publication.Frame)
                    || !DigestValues(Publication).SequenceEqual(DigestValues(snapshot), StringComparer.Ordinal))
                    throw new InvalidDataException("Captured Town source/support commitments changed");
                reason = "Current actual source/body binding at model commit";
                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException) && !(exception is StackOverflowException))
            {
                Invalidate("Town model commit refused: " + exception.Message);
                reason = Reason;
                return false;
            }
        }

        private void RequireCurrent(bool forceSource)
        {
            RequireMainThread();
            workerCancellation.ThrowIfCancellationRequested();
            // CurrentTownPublication800 already invokes the actual token's cheap
            // current gate. Normal queries allocate no digest arrays, read no
            // source borrow and never serialize the canonical document.
            if (invalidated || !ReferenceEquals(world.CurrentTownPublication800, Publication)
                || !ReferenceEquals(Publication.Ground, Ground))
                throw new InvalidDataException("Actual Town/world/ground binding expired or mismatched");
            if (!forceSource) return;
            if (!Ground.IsCurrent || !Publication.SourceSubsetOnly || Publication.WholeWorldAdmitted
                || Publication.ClosedBuildingVolumes || !Ground.SourceSubsetOnly || Ground.FullWorldAdmitted
                || !TownSourceCapturePolicy800.SameFrame(Publication.Frame, Ground.Frame)
                || !TownSourceCapturePolicy800.SameFrame(Publication.Frame, world.transform.localToWorldMatrix)
                || !DigestValues(Publication).SequenceEqual(DigestValues(Ground), StringComparer.Ordinal)
                || !Publication.ValidateSourceNow())
                throw new InvalidDataException("Actual Town source/body/frame/support fresh validation refused");
            if (invalidated || workerCancellation.IsCancellationRequested
                || !ReferenceEquals(world.CurrentTownPublication800, Publication))
                throw new OperationCanceledException("Town semantic binding retired during validation");
        }

        private TownSemanticSourceData800 CaptureImmutableData(CancellationToken cancellation)
        {
            byte[] envelope = Publication.CopySemanticSourceDocument(cancellation);
            if (envelope == null || envelope.Length == 0 || envelope.Length > World13DocumentCodec.MaximumBytes
                || World13DocumentCodec.Hash(envelope) != Publication.RawDocumentDigest)
                throw new InvalidDataException("Defensive complete canonical Town envelope required");

            // NO yield/await/task escapes this borrow. Native and source references
            // are converted to private value arrays before this method returns.
            var borrowed = world.BorrowTownSourceForBinding800(Publication);
            if (borrowed.Region == null || borrowed.Inputs == null || borrowed.EmittedBodies == null
                || borrowed.Region.buildings == null || borrowed.Inputs.SourceCount != Publication.SourceBuildingCount
                || borrowed.Region.buildings.Length != Publication.SourceBuildingCount
                || RoadSourceIdentity.Compute(borrowed.Region.roads) != Publication.SourceDigest)
                throw new InvalidDataException("Exact complete published source/entry association required");
            var buildings = TownSourceCapturePolicy800.CopyOriginalBuildings(borrowed.Region.buildings,
                cancellation, out var sourceIndices);
            var copiedBodyEntries = new HashSet<BuildingRenderInput.Entry>(TownSourceReferenceIdentityComparer800<BuildingRenderInput.Entry>.Instance);
            var capture = new NativeCollisionCopy800();
            var entries = TownSourceCapturePolicy800.CopyEntries(borrowed.Inputs, sourceIndices, entry =>
            {
                if (!borrowed.EmittedBodies.TryGetValue(entry, out var body)) return null;
                if (!copiedBodyEntries.Add(entry)) throw new InvalidDataException("Repeated actual body-entry lookup");
                return capture.CopyBody(body, cancellation);
            }, cancellation);
            if (copiedBodyEntries.Count != borrowed.EmittedBodies.Count
                || borrowed.EmittedBodies.Keys.Any(entry => !copiedBodyEntries.Contains(entry)))
                throw new InvalidDataException("Foreign/unaccounted actual emitted Entry body");
            cancellation.ThrowIfCancellationRequested();
            return new TownSemanticSourceData800(envelope, buildings, entries, Publication.Frame,
                Publication.RuntimeWorldHandle, Publication.WorldGeneration, Publication.TownGeneration,
                DigestValues(Publication));
        }

        private static string[] DigestValues(TownSemanticPublication800 value) => new[]
        {
            value.RawDocumentDigest, value.SourceDigest, value.ControlDigest, value.OwnerLedgerDigest,
            value.StructureLedgerDigest, value.ScopeCoverageDigest, value.FinalBufferDigest, value.GeometryDigest
        };
        private static string[] DigestValues(GroundLease800 value) => new[]
        {
            value.RawDocumentDigest, value.SourceDigest, value.ControlDigest, value.OwnerLedgerDigest,
            value.StructureLedgerDigest, value.ScopeCoverageDigest, value.FinalBufferDigest, value.GeometryDigest
        };
        private static string[] DigestValues(TownSemanticSourceData800 value) => new[]
        {
            value.RawDocumentDigest, value.SourceDigest, value.ControlDigest, value.OwnerLedgerDigest,
            value.StructureLedgerDigest, value.ScopeCoverageDigest, value.FinalBufferDigest, value.GeometryDigest
        };

        private void Invalidate(string reason)
        {
            // Mark false FIRST, even if a worker's cancellation callback fails.
            if (invalidated) return;
            invalidated = true;
            Reason = reason;
            try { lifetimeCancellation.Cancel(); }
            catch (Exception exception) { RememberError(exception); }
        }

        public string[] CopyLocalErrors()
        {
            RequireMainThread();
            return localErrors.ToArray();
        }

        private void RememberError(Exception exception)
        {
            if (localErrors.Count >= MaximumLocalErrors) return;
            string text = exception.ToString();
            localErrors.Add(text.Length <= MaximumErrorCharacters ? text : text.Substring(0, MaximumErrorCharacters));
        }

        /// <summary>Retire only this readmodel consumer; never destroy borrowed Town/ground.</summary>
        public void Dispose()
        {
            RequireMainThread();
            if (disposed) return;
            disposed = true;
            Invalidate("Town semantic binding disposed");
            var town = townRevocation; townRevocation = null;
            var ground = groundRevocation; groundRevocation = null;
            try { town?.Dispose(); } catch (Exception exception) { RememberError(exception); }
            try { ground?.Dispose(); } catch (Exception exception) { RememberError(exception); }
            lifetimeCancellation.Dispose();
        }

        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Town semantic binding/currency/disposal require issuing main thread");
        }
    }

    /// <summary>
    /// Main-thread copier of actual native collision inputs. This object stays on
    /// the capture stack only. Unsupported active kinds, dynamic/convex/sheared/
    /// mirrored layouts or any aggregate budget violation refuse the WHOLE bind.
    /// No native object/mesh/material ownership is transferred or mutated.
    /// </summary>
    internal sealed class NativeCollisionCopy800
    {
        private int colliderCount, vertexCount, indexCount;
        private readonly HashSet<ulong> colliderIds = new HashSet<ulong>();

        internal TownPublishedBody800 CopyBody(GameObject body, CancellationToken cancellation)
        {
            if (!body || !body.activeInHierarchy) throw new InvalidDataException("Actual live published Town body required");
            var colliders = body.GetComponentsInChildren<Collider>(true);
            TownSourceCapturePolicy800.AddCount(ref colliderCount, colliders.Length,
                TownSourceCapturePolicy800.MaximumColliders, "colliders");
            var inputs = new List<TownCollisionInput800>();
            int inactive = 0, disabled = 0, triggers = 0;
            foreach (var collider in colliders)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!collider) throw new InvalidDataException("Dead Town collider during capture");
                if (!collider.gameObject.activeInHierarchy) { inactive++; continue; }
                if (!collider.enabled) { disabled++; continue; }
                if (collider.isTrigger) { triggers++; continue; }
                ulong colliderId = EntityId.ToULong(collider.GetEntityId());
                if (colliderId == 0 || !colliderIds.Add(colliderId))
                    throw new InvalidDataException("Aliased/invalid actual Town collider identity");
                if (collider.attachedRigidbody)
                    throw new InvalidDataException("Dynamic/rigidbody Town collision layout unsupported");
                var frame = collider.transform.localToWorldMatrix;
                TownSourceCapturePolicy800.RequireColliderFrame(frame);
                if (collider is BoxCollider box)
                {
                    var centre = box.center;
                    var size = box.size;
                    TownSourceCapturePolicy800.RequireBox(centre, size, frame);
                    inputs.Add(new TownCollisionInput800(TownCollisionKind800.Box, colliderId, 0,
                        box.gameObject.layer, frame, centre, size, 0, Array.Empty<Vector3>(),
                        Array.Empty<int>(), Array.Empty<int>()));
                }
                else if (collider is MeshCollider meshCollider)
                {
                    inputs.Add(CopyMesh(meshCollider, colliderId, frame, cancellation));
                }
                else throw new InvalidDataException("Unsupported active Town collider kind: " + collider.GetType().FullName);
            }
            if (inputs.Count == 0) throw new InvalidDataException("Published body has no supported active collision input");
            return new TownPublishedBody800(EntityId.ToULong(body.GetEntityId()), inputs.ToArray(), inactive, disabled, triggers);
        }

        private TownCollisionInput800 CopyMesh(MeshCollider collider, ulong colliderId,
            Matrix4x4 frame, CancellationToken cancellation)
        {
            var mesh = collider.sharedMesh;
            var filter = collider.GetComponent<MeshFilter>();
            var owner = collider.GetComponent<GeneratedMeshOwner>();
            if (collider.convex || !mesh || !filter || !owner || filter.sharedMesh != mesh || owner.Mesh != mesh
                || !mesh.isReadable || mesh.vertexCount < 3 || mesh.subMeshCount < 1
                || mesh.subMeshCount > TownSourceCapturePolicy800.MaximumSubmeshesPerCollider)
                throw new InvalidDataException("Unsupported convex/unreadable/unowned Town mesh collider layout");
            TownSourceCapturePolicy800.AddCount(ref vertexCount, mesh.vertexCount,
                TownSourceCapturePolicy800.MaximumMeshVertices, "copied mesh vertices");
            int meshIndices = 0;
            var counts = new int[mesh.subMeshCount];
            for (int submesh = 0; submesh < counts.Length; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    throw new InvalidDataException("Nontriangle Town mesh collider topology unsupported");
                long count = mesh.GetIndexCount(submesh);
                if (count > int.MaxValue || count % 3 != 0)
                    throw new InvalidDataException("Invalid Town triangle index count");
                counts[submesh] = (int)count;
                TownSourceCapturePolicy800.AddCount(ref meshIndices, (int)count,
                    TownSourceCapturePolicy800.MaximumTriangleIndices, "mesh triangle indices");
            }
            TownSourceCapturePolicy800.AddCount(ref indexCount, meshIndices,
                TownSourceCapturePolicy800.MaximumTriangleIndices, "all copied triangle indices");
            cancellation.ThrowIfCancellationRequested();
            var vertices = mesh.vertices; // Defensive Unity-managed array copy.
            var indices = new int[meshIndices];
            int offset = 0;
            for (int submesh = 0; submesh < counts.Length; submesh++)
            {
                cancellation.ThrowIfCancellationRequested();
                var part = mesh.GetIndices(submesh); // Actual base-vertex-applied input indices.
                if (part.Length != counts[submesh]) throw new InvalidDataException("Town index buffer changed while copying");
                Array.Copy(part, 0, indices, offset, part.Length);
                offset += part.Length;
            }
            if (vertices.Length != mesh.vertexCount) throw new InvalidDataException("Town vertex buffer changed while copying");
            TownSourceCapturePolicy800.RequireTriangleIndices(vertices, indices);
            return new TownCollisionInput800(TownCollisionKind800.TriangleMesh, colliderId, EntityId.ToULong(mesh.GetEntityId()),
                collider.gameObject.layer, frame, default, default, (int)collider.cookingOptions, vertices, indices, counts);
        }
    }
}
