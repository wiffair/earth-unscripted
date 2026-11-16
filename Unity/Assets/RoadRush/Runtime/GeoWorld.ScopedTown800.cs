using System;
using System.Threading.Tasks;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using RoadRush.Core800;

namespace RoadRush
{
    [Serializable]
    public sealed class ScopedTownLedger800
    {
        public string rawDocumentDigest, sourceDigest, supportGeometryDigest, rawBefore, rawAfter;
        public bool fullWorldAdmitted = false, registryIssued = false, closedBuildingVolumes = false;
        public List<BuildingRecord> buildings = new List<BuildingRecord>();
        [Serializable]
        public sealed class BuildingRecord
        {
            public string contentKey, geometryId, status, reason;
            public string[] originalIds, rawSourceHashes, rawFootprintHashes;
            public int wallColliders, meshes, windows, doors, stepFlights, sections;
            public float minX, minZ, maxX, maxZ, minFloorY, maxFloorY;
            public bool wallsOnlyCollision = true;
            public SectionRecord[] parts = Array.Empty<SectionRecord>();
        }

        [Serializable]
        public sealed class SectionRecord
        {
            public string partId;
            public bool hasNearbyPavement;
            public float levelFloorY, foundationBottomY, boundaryMinY, boundaryMaxY, nearestPavementY, pavementSeparation;
            public Vector3 centre;
        }

        public string ExportJson() => WorldFieldJson.Write(this);
    }

    internal static class ScopedTownLineage800
    {
        internal static void RequireComplete(BuildingRenderInput.Plan p)
        {
            if (p == null || p.SourceCount < 0 || p.SourceCount > 2500 || p.Entries == null || p.Entries.Any(e => e == null || e.OriginalSources == null || e.OriginalSources.Any(b => b == null)) || p.Entries.Sum(e => (long)e.OriginalSources.Length) != p.SourceCount)
                throw new InvalidDataException("Render source lineage incomplete; no unsupported sentinel publication");
        }
    }

    public sealed partial class GeoWorld
    {
        ScopedTownSupport800 scopedTownSupport800;
        GameObject scopedTownRoot800;
        readonly HashSet<Mesh> scopedTownMeshes800 = new HashSet<Mesh>();
        int townLifetime800, townMainThread800;
        bool scopedTownRejectQueries800, scopedTownPreparing800;
        ScopedTownLedger800 townLedger800;
        long townVertexCount800, townTriangleCount800;
        CancellationTokenSource townPreparationCancel800;
        Func<bool> townSupportCurrent800;
        bool IsScopedTown800 => scopedTownPreparing800 || scopedTownSupport800 != null || scopedTownRejectQueries800;

        public string ExportScopedTownLedger800() => townLedger800 == null ? "{}" : townLedger800.ExportJson(); // diagnostic only, no issuance/metadata capability
        bool TownRoadOverlap(Vector3[] p, float margin) => scopedTownSupport800 != null ? scopedTownSupport800.IntersectsPavement(p, margin) : Region.FootprintOverlapsRoad(p, margin);
        /// <summary>Legacy isolated harness path: never issues semantic publication.</summary>
        public IEnumerator BuildScopedTown800(CanonicalRaw13Document source, PreparedWorldMeshPlan800 support, CancellationToken token = default, Func<bool> current = null) => BuildScopedTown800Core(source, support, token, current, null, null);
        /// <summary>Typed live ground path. Equal hashes are insufficient: the actual lease must own this exact plan.</summary>
        public IEnumerator BuildScopedTown800(CanonicalRaw13Document source, PreparedWorldMeshPlan800 support, GroundLease800 ground, CancellationToken token = default, Action<TownSemanticPublication800> published = null)
        {
            if (ground == null)
                throw new ArgumentNullException(nameof(ground));
            return BuildScopedTown800Core(source, support, token, () => ground.IsCurrent, ground, published);
        }

        IEnumerator BuildScopedTown800Core(CanonicalRaw13Document source, PreparedWorldMeshPlan800 support, CancellationToken token, Func<bool> current, GroundLease800 actualGround, Action<TownSemanticPublication800> published)
        {
            if (transform.position != Vector3.zero || transform.rotation != Quaternion.identity || transform.lossyScale != Vector3.one || string.IsNullOrEmpty(PlayerProfile.StorageOverride) || Region != null || loaded.Count != 0 || Observer || SecondaryObserver || RaceObservers.Count != 0 || source == null || support == null || current == null || !current() || ScopedMeshPlan != null || scopedTownRoot800)
                throw new InvalidOperationException("Fresh isolated GeoWorld and owned scoped800 source required");
            townMainThread800 = Thread.CurrentThread.ManagedThreadId;
            bootPreparing = true;
            scopedTownPreparing800 = true;
            scopedTownRejectQueries800 = true;
            townSupportCurrent800 = current;
            int request = ++townLifetime800, worldGeneration = ++generation;
            bool completed = false;
            try
            {
                if (actualGround != null)
                    BeginTypedTown800(source, support, actualGround, worldGeneration, request);
            }
            catch
            {
                if (request == townLifetime800 && worldGeneration == generation)
                    ResetScopedTown800();
                throw;
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            townPreparationCancel800 = cancellation;
            var preparation = Task.Run(() => PrepareTownData800(source, support, cancellation.Token), cancellation.Token);
            void Check()
            {
                token.ThrowIfCancellationRequested();
                if (request != townLifetime800 || generation != worldGeneration || (current != null && !current()))
                    throw new OperationCanceledException("Town generation superseded");
            }

            try
            {
                while (!preparation.IsCompleted)
                {
                    Check();
                    yield return null;
                }

                Check();
                var data = preparation.GetAwaiter().GetResult();
                var doc = data.Document;
                var copy = doc.geography;
                var before = data.Ledger.rawBefore;
                var physical = data.Support;
                Region = copy;
                BuildingInputs = data.Inputs;
                BindTypedTownData800(doc, data.Inputs);
                buildingSites.Clear();
                buildings.Clear();
                foreach (var input in BuildingInputs.Renderable)
                    Index(buildings, input.Geometry, input.Geometry.points);
                scopedTownPreparing800 = false;
                scopedTownRejectQueries800 = false;
                scopedTownSupport800 = physical;
                townLedger800 = data.Ledger;
                var root = new GameObject("Scoped800 town (walls-only collision)");
                root.SetActive(false);
                root.transform.SetParent(transform, false);
                scopedTownRoot800 = root;
                int recordIndex = 0;
                roof = BuildingAppearanceMaterials.Roof;
                var sliceBudget = new TownConstructionSliceBudget800();
                foreach (var input in BuildingInputs.Entries)
                {
                    Check();
                    var record = townLedger800.buildings[recordIndex++];
                    record.status = "Pending";
                    if (input.Skipped)
                    {
                        record.status = "Suppressed";
                        record.reason = "RenderInput:" + input.Diagnostic;
                        if (sliceBudget.CompleteStep())
                        {
                            yield return null;
                            sliceBudget.ResumeAfterYield();
                        }
                        continue;
                    }

                    var b = input.Geometry;
                    record.minX = b.points.Min(p => p.x);
                    record.maxX = b.points.Max(p => p.x);
                    record.minZ = b.points.Min(p => p.z);
                    record.maxZ = b.points.Max(p => p.z);
                    if (!physical.ContainsFootprintWithReach(b.points))
                    {
                        record.status = "Suppressed";
                        record.reason = "OutsideProvedSupportOrAccessReach4m";
                        if (sliceBudget.CompleteStep())
                        {
                            yield return null;
                            sliceBudget.ResumeAfterYield();
                        }
                        continue;
                    }

                    if (physical.IntersectsPavement(b.points, 1))
                    {
                        record.status = "Suppressed";
                        record.reason = "FinalPavementClearance1m";
                        if (sliceBudget.CompleteStep())
                        {
                            yield return null;
                            sliceBudget.ResumeAfterYield();
                        }
                        continue;
                    }

                    var body = new GameObject("Source building " + b.id);
                    body.transform.SetParent(root.transform, false);
                    bool failed = false;
                    var work = BuildingSteps(body.transform, b).GetEnumerator();
                    try
                    {
                        while (true)
                        {
                            Check();
                            bool more = false;
                            try
                            {
                                more = work.MoveNext();
                            }
                            catch (MissingScopedSupportException e)
                            {
                                failed = true;
                                record.reason = "MissingFinalSupport:" + e.X + "," + e.Z;
                            }
                            catch (ArgumentException e)
                            {
                                failed = true;
                                record.reason = "BuilderRefusal:" + e.Message;
                            }

                            CaptureTownMeshes800(body);
                            if (failed || !more)
                                break;
                            // Unknown waiting instructions retain their original Unity semantics.
                            object checkpoint = work.Current;
                            if (!TownConstructionSliceBudget800.IsCooperativeCheckpoint(checkpoint))
                            {
                                sliceBudget.CompleteStep();
                                yield return checkpoint;
                                sliceBudget.ResumeAfterYield();
                            }
                            else if (sliceBudget.CompleteStep())
                            {
                                yield return null;
                                sliceBudget.ResumeAfterYield();
                            }
                        }
                    }
                    finally
                    {
                        work.Dispose();
                    }

                    if (failed)
                    {
                        record.status = "Suppressed";
                        Release(body);
                        if (sliceBudget.CompleteStep())
                        {
                            yield return null;
                            sliceBudget.ResumeAfterYield();
                        }
                        continue;
                    }

                    body.GetComponent<BuildingAccessChunk>()?.Commit();
                    var filters = body.GetComponentsInChildren<MeshFilter>(true);
                    CaptureTownMeshes800(body);
                    record.meshes = filters.Length;
                    record.wallColliders = body.GetComponentsInChildren<MeshCollider>(true).Count(c => c.name.StartsWith("Building ", StringComparison.Ordinal));
                    var sites = buildingSites.Where(p => p.Key == b.id || p.Key.StartsWith(b.id + "/", StringComparison.Ordinal)).Select(p => p.Value).ToArray();
                    record.parts = buildingSites.Where(p => p.Key == b.id || p.Key.StartsWith(b.id + "/", StringComparison.Ordinal)).Select(p => new ScopedTownLedger800.SectionRecord { partId = p.Key, levelFloorY = p.Value.LevelFloorY, foundationBottomY = p.Value.FoundationBottomY, boundaryMinY = p.Value.BoundaryMinY, boundaryMaxY = p.Value.BoundaryMaxY, nearestPavementY = p.Value.NearestRoadY, hasNearbyPavement = physical.NearestPavement(p.Value.Centre).Road != null, pavementSeparation = physical.NearestPavement(p.Value.Centre).Road == null ? 0 : physical.NearestPavement(p.Value.Centre).Separation, centre = p.Value.Centre }).ToArray();
                    var features = body.GetComponentsInChildren<BuildingFacadeChunk>(true).SelectMany(c => c.Features).ToArray();
                    record.windows = features.Count(f => !f.IsDoor);
                    record.doors = features.Count(f => f.IsDoor);
                    record.stepFlights = body.GetComponentsInChildren<BuildingAccessChunk>(true).Sum(c => c.FlightCount);
                    record.sections = sites.Length;
                    if (sites.Length > 0)
                    {
                        record.minFloorY = sites.Min(p => p.LevelFloorY);
                        record.maxFloorY = sites.Max(p => p.LevelFloorY);
                    }

                    record.status = record.meshes > 0 ? "Emitted" : "Suppressed";
                    if (record.meshes > 0)
                        RecordTypedTownBody800(input, body);
                    record.reason = record.meshes > 0 ? "ExistingShellRoofFacadeAccess;WallsOnlyCollider" : "ExistingBuilderRefused";
                    if (sliceBudget.CompleteStep())
                    {
                        yield return null;
                        sliceBudget.ResumeAfterYield();
                    }
                }

                Check();
                if (World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(doc)) != before)
                    throw new InvalidDataException("Building emission changed owned raw source");
                if (townLedger800.buildings.Any(r => r.status == "Pending"))
                    throw new InvalidDataException("Unbalanced town ledger");
                root.SetActive(true);
                if (actualGround != null)
                    Physics.SyncTransforms();
                PublishTypedTown800(published);
                completed = true;
            }
            finally
            {
                if (ReferenceEquals(townPreparationCancel800, cancellation))
                    townPreparationCancel800 = null;
                cancellation.Cancel();
                if (!preparation.IsCompleted)
                    preparation.ContinueWith(t =>
                    {
                        var observed = t.Exception;
                    }, TaskContinuationOptions.OnlyOnFaulted);
                if (!completed && request == townLifetime800)
                {
                    foreach (var row in townLedger800?.buildings ?? new List<ScopedTownLedger800.BuildingRecord>())
                        if (row.status == "Queued" || row.status == "Pending" || row.status == "Emitted")
                        {
                            row.status = "Cancelled";
                            row.reason = "StagedTownNotPublished";
                        }

                    ResetScopedTown800();
                }
            }
        }

        sealed class TownData800
        {
            internal World13Document Document;
            internal ScopedTownSupport800 Support;
            internal BuildingRenderInput.Plan Inputs;
            internal ScopedTownLedger800 Ledger;
        }

        static TownData800 PrepareTownData800(CanonicalRaw13Document source, PreparedWorldMeshPlan800 support, CancellationToken token)
        {
            byte[] bytes = source.Write(token);
            if (World13DocumentCodec.Hash(bytes) != support.RawDocumentDigest)
                throw new InvalidDataException("Town source/support mismatch");
            var doc = source.TakeOwnedDocument(token);
            var copy = doc.geography;
            string before = World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(doc));
            string sourceBefore = RoadSourceIdentity.Compute(copy.roads);
            copy.RebuildRenderQueryIndexesOnly();
            string after = World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(doc));
            if (before != after || sourceBefore != RoadSourceIdentity.Compute(copy.roads) || sourceBefore != support.SourceDigest)
                throw new InvalidDataException("Render query rebuild mutated owned raw source");
            var data = new TownData800
            {
                Document = doc,
                Support = new ScopedTownSupport800(support, copy, token),
                Inputs = BuildingRenderInput.Create(copy),
                Ledger = new ScopedTownLedger800
                {
                    rawDocumentDigest = support.RawDocumentDigest,
                    sourceDigest = support.SourceDigest,
                    supportGeometryDigest = support.GeometryDigest,
                    rawBefore = before,
                    rawAfter = after
                }
            };
            ScopedTownLineage800.RequireComplete(data.Inputs);
            foreach (var input in data.Inputs.Entries)
            {
                token.ThrowIfCancellationRequested();
                var record = new ScopedTownLedger800.BuildingRecord
                {
                    contentKey = input.ContentKey,
                    geometryId = input.Geometry?.id,
                    status = "Queued",
                    originalIds = input.OriginalSources.Select(b => b.id).ToArray(),
                    rawSourceHashes = input.OriginalSources.Select(SceneryCandidate.OwnedFacadeHash.RawSource).ToArray(),
                    rawFootprintHashes = input.OriginalSources.Select(SceneryCandidate.OwnedFacadeHash.RawFootprint).ToArray()
                };
                data.Ledger.buildings.Add(record);
            }

            token.ThrowIfCancellationRequested();
            return data;
        }

        // Cleanup-only integration hook. The actual uploaded ground owner must
        // call this on main BEFORE changing generation/destroying ground.
        public void RevokeScopedTown800Support()
        {
            if (!IsScopedTown800)
                return;
            if (Thread.CurrentThread.ManagedThreadId != townMainThread800)
                throw new InvalidOperationException("Town support revocation requires main thread");
            if (townLedger800 != null)
                foreach (var row in townLedger800.buildings)
                    if (row.status == "Emitted" || row.status == "Pending" || row.status == "Queued")
                    {
                        row.status = "Revoked";
                        row.reason = "GroundOwnerLifetimeEnded";
                    }

            ResetScopedTown800();
        }

        void PollScopedTownLease800()
        {
            // Must precede the held-boot early return: published town still owns
            // active colliders even though normal streaming remains disabled.
            if (scopedTownSupport800 == null && !scopedTownPreparing800)
                return;
            bool valid = false;
            try
            {
                valid = townSupportCurrent800 != null && townSupportCurrent800();
            }
            catch
            {
                RevokeScopedTown800Support();
                throw;
            }

            if (!valid)
                RevokeScopedTown800Support();
            else if (townSemanticPublication800 != null)
                CheckTownPublication800(townSemanticPublication800, false);
        }

        void CaptureTownMeshes800(GameObject body)
        {
            foreach (var filter in body.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh && scopedTownMeshes800.Add(filter.sharedMesh))
                {
                    var mesh = filter.sharedMesh;
                    townVertexCount800 += mesh.vertexCount;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                        townTriangleCount800 += (long)mesh.GetIndexCount(i) / 3;
                    if (scopedTownMeshes800.Count > 8192 || townVertexCount800 > 2000000 || townTriangleCount800 > 4000000)
                        throw new InvalidDataException("Scoped town geometry budget exceeded; no partial publication");
                }
        }

        void ResetScopedTown800(bool normalReinitialize = false)
        {
            if (townResetInProgress800)
                return;
            townResetInProgress800 = true;
            try
            {
                RetireTownPublication800("TownReset");
                var cancel = townPreparationCancel800;
                townPreparationCancel800 = null;
                cancel?.Cancel();
                bool owned = scopedTownPreparing800 || scopedTownSupport800 != null || scopedTownRoot800;
                scopedTownPreparing800 = false;
                townSupportCurrent800 = null;
                scopedTownSupport800 = null;
                scopedTownRejectQueries800 = owned && !normalReinitialize;
                ++townLifetime800;
                if (owned)
                {
                    Region = null;
                    BuildingInputs = null;
                    buildingSites.Clear();
                    buildings.Clear();
                    generation++;
                }

                var root = scopedTownRoot800;
                scopedTownRoot800 = null;
                if (root)
                    Release(root);
                foreach (var mesh in scopedTownMeshes800)
                    if (mesh)
                    {
                        if (Application.isPlaying)
                            Destroy(mesh);
                        else
                            DestroyImmediate(mesh);
                    }

                scopedTownMeshes800.Clear();
                townVertexCount800 = townTriangleCount800 = 0;
            }
            finally
            {
                townResetInProgress800 = false;
            }
        }
    }
}
