using System;
using System.Collections.Generic;
using UnityEngine;
using RoadRush.Core800;

namespace RoadRush.FunnyAnimals.Core800Proposal
{
    /// <summary>
    /// Main-thread-only, ground-witness consumer of Root's actual private-issued800 lease.
    /// Borrows the lease; owns subscription, staged iterator, copied index and work budget.
    /// No issuer/Build, scene discovery, ISafeWorld, gameplay registration or heightfield.
    /// Ready means staging completed, never fresh authority: every query gates currentness.
    /// Source hazards/actor registry remain UNBOUND; no normal-world/gameplay admission.
    /// </summary>
    public sealed class Grounded800Candidate : IDisposable
    {
        // Exact reviewed source identities, not user-supplied bools or sufficient authority.
        // Native active-buffer/owner validation still belongs to the actual typed lease.
        private const string ReviewedFinal = "04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf";
        private const string ReviewedScope = "9955e93cf991f88ebc3079064de2bc7db85fe82724b746f821b6c01e66b5ef55";
        private const int ReviewedPackets = 1063;
        private const int ReviewedVertices = 317003;
        private const int ReviewedTriangles = 533219;
        private const int ReviewedReferences = 689595;
        private const int ReviewedCells = 10201;
        private const int ReviewedBankTriangles = 3586;
        private const int DigestHexCharacters = 64;

        // Per real Time.frameCount, shared by this consumer (not per actor).
        private const int MaximumNewBindingsPerFrame = 1;
        private const int MaximumPacketExportsPerFrame = 16;
        private const int MaximumStagedUnitsPerFrame = 4096;
        private const int ExportTargetBytesPerFrame = 262144;
        private const int ExportHardLimitBytesPerFrame = 524288;
        // Declared broad source limits versus OBSERVED reviewed packet ceilings.
        private const int DeclaredPacketVertices = 24000;
        private const int DeclaredPacketTriangles = 48000;
        private const int ReviewedMaximumPacketVertices = 6462;
        private const int ReviewedMaximumPacketTriangles = 12286;
        private const int ReviewedMaximumPacketExportBytes = 224976;
        private const int ScalarBytes = 4; // floatXYZ and int32 indices; array headers excluded

        // Actual legacy planner binary32 radius promoted exactly, not rounded down
        // to nominal decimal0.85. This numerical probe profile is not a measured
        // actor mesh envelope or native support/contact guarantee.
        public const double SupportedRadiusMetres = (double)0.85f;
        private const double AnimalRadiusMetres = SupportedRadiusMetres;
        private const double EnclosingRectangleRadiusMetres = SupportedRadiusMetres + 0.0001;
        private const double FootprintClearanceMetres = 0.00005;
        private const double MaximumCorridorSquaredMetres = 64;
        private const double MaximumFootprintHeightRangeMetres = 0.349999;
        private const double MaximumGroundAgreementMetres = 0.0002;
        private const double MaximumDerivedBankGrade = 2.0; // ratio, not angle
        // Existing transport uncertainty formula: source grade envelope plus1e-6,
        // binary32 half-relative-spacing approximation, then1micrometre allowance.
        private const double HeightTransportGradeFactor = 2.000001;
        private const int FloatRelativePrecisionExponent = -24;
        private const double HeightTransportMarginMetres = 0.000001;

        // Null before Bind/after Dispose. Caller must use an actual Root-issued lease;
        // cached association never replaces owner-generation/physical-layout validation.
        private GroundLease800 lease;
        private IDisposable revokeSubscription;
        private IEnumerator<int> pending;
        private LocalMeshIndex geometry;
        private readonly QueryBudget budget = new QueryBudget();
        private readonly Vector2[] footprint = new Vector2[5]; // four closed rectangle corners
        private int frame = -1;
        private int bindFrame = -1;
        private int newBindingsThisFrame;
        private int copiesRemaining;
        private int copiedBytes;
        private long localPayloadBaseline;
        private bool refused;
        private bool stepping;

        public bool Ready { get; private set; }
        public bool GameplaySemanticsBound => false;
        public string Reason { get; private set; } = "Actual imported/native-accepted owner bridge required";
        public int NewBindingValidationsThisFrame => bindFrame == Time.frameCount ? newBindingsThisFrame : 0;
        /// <summary>Exported XYZ/index payload only; excludes clone/list/native workload.</summary>
        public long ExportedArrayPayloadBytesThisFrame => copiedBytes;
        public long AdditionalLocalOwnedArrayPayloadBytesThisFrame => geometry == null
            ? 0 : Math.Max(0, geometry.OwnedArrayPayloadBytes - localPayloadBaseline);
        public int TemporaryBankMaskPayloadBytesThisFrame { get; private set; }
        public int PacketCopiesThisFrame { get; private set; }
        public int CopiedVerticesThisFrame { get; private set; }
        public int CopiedIndicesThisFrame { get; private set; }
        public int OwnerCurrentCallsThisFrame { get; private set; }
        public string RuntimeOwnerHandle => lease?.RuntimeOwnerHandle;
        public int Generation => lease?.Generation ?? -1;

        /// <summary>
        /// Derived placement witness: height comes ONLY from actual lease.TryGround.
        /// Height/error are world metres; normal y is dimensionless. No source metadata,
        /// obstacle clearance, actor safety or lasting support authority is provided.
        /// </summary>
        public struct Ground
        {
            public float Height;
            public float MinimumNormalY;
            public string PacketId;
            public int Triangle;
            public double NativeCoordinateErrorBound;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Need(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }

        // Actual Root owner caches its expensive full inventory check per real frame;
        // each read still has basic host/root work. Counter is NOT a native cost bound.
        private bool Current()
        {
            OwnerCurrentCallsThisFrame++;
            try
            {
                bool valid = lease != null && lease.IsCurrent && lease.Frame.Equals(Matrix4x4.identity);
                if (!valid)
                {
                    Ready = false;
                    refused = true;
                    Reason = "Actual owner lease/frame is no longer current";
                }
                return valid;
            }
            catch (Exception exception)
            {
                Ready = false;
                refused = true;
                Reason = "Unknown owner-current query: " + exception.Message;
                return false;
            }
        }

        // BeforeRevoke can run inside MoveNext via an owner read. Defer iterator Dispose
        // in that case, while immediately making Ready/index refusal sticky.
        private void Invalidate(string reason)
        {
            Ready = false;
            refused = true;
            Reason = reason;
            if (!stepping)
            {
                pending?.Dispose();
                pending = null;
            }
            geometry?.Refuse();
        }

        /// <summary>
        /// Associates one actual private-issued lease with exact reviewed inventory/hash
        /// policy and identity frame. Repeated same-reference success is CACHED association,
        /// never fresh authority. New bindings are capped1/realframe; Dispose preserves that
        /// cap. No Unity object is created or activated. Null/unsupported/expired refuses.
        /// A budget-refused DIFFERENT lease leaves the prior association intact; callers
        /// must honor false and must not interpret old Ready as the requested new binding.
        /// </summary>
        public bool Bind(GroundLease800 actualLease)
        {
            if (actualLease == null)
            {
                Dispose();
                Invalidate("No actual issued lease");
                return false;
            }
            if (ReferenceEquals(lease, actualLease) && !refused) return true;
            int currentFrame = Time.frameCount;
            if (bindFrame != currentFrame)
            {
                bindFrame = currentFrame;
                newBindingsThisFrame = 0;
            }
            if (newBindingsThisFrame >= MaximumNewBindingsPerFrame)
            {
                Reason = "New owner binding validation budget exhausted";
                return false;
            }
            newBindingsThisFrame++;
            Dispose();
            lease = actualLease;
            if (!Current())
            {
                Invalidate("No current actual private-issued800 lease");
                return false;
            }
            if (!lease.SourceSubsetOnly || lease.FullWorldAdmitted
                || lease.FinalBufferDigest != ReviewedFinal || lease.ScopeCoverageDigest != ReviewedScope)
            {
                Invalidate("Unreviewed typed800 buffer/scope profile");
                return false;
            }
            try
            {
                revokeSubscription = lease.BeforeRevoke((expired, reason) =>
                {
                    if (ReferenceEquals(expired, lease)) Invalidate("Owner revoked before physical teardown: " + reason);
                });
                geometry = new LocalMeshIndex();
                refused = false;
                Ready = false;
                pending = Prepare().GetEnumerator();
                Reason = "Staging reviewed exact LOCAL-f32 mesh copies";
                return true;
            }
            catch (Exception exception)
            {
                Invalidate("Lease binding refused: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Advances bounded preparation ONCE per actual Unity frame. Requires real
        /// advancing frames, not an Editor loop drained synchronously or a fake clock.
        /// Resets shared query budgets/counters; exports/clones and iterator work can
        /// allocate. Refusal clears staging; completed staging does not grant authority.
        /// </summary>
        public void BeginFrame()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            budget.Reset();
            copiesRemaining = MaximumPacketExportsPerFrame;
            copiedBytes = 0;
            localPayloadBaseline = geometry?.OwnedArrayPayloadBytes ?? 0;
            TemporaryBankMaskPayloadBytesThisFrame = 0;
            PacketCopiesThisFrame = CopiedVerticesThisFrame = CopiedIndicesThisFrame = OwnerCurrentCallsThisFrame = 0;
            if (refused)
            {
                pending?.Dispose();
                pending = null;
                return;
            }
            if (!Current()) { Invalidate(Reason); return; }
            try
            {
                if (pending != null)
                for (int work = 0; work < MaximumStagedUnitsPerFrame; work++)
                {
                    bool more;
                    stepping = true;
                    try { more = pending.MoveNext(); }
                    finally { stepping = false; }
                    if (refused)
                    {
                        pending?.Dispose();
                        pending = null;
                        break;
                    }
                    if (!more)
                    {
                        pending.Dispose();
                        pending = null;
                        Ready = true;
                        Reason = "All reviewed local packets retained; gameplay semantics still absent";
                        break;
                    }
                    if (pending.Current < 0) break;
                }
            }
            catch (Exception exception)
            {
                Invalidate("Local packet preparation refused: " + exception.Message);
            }
        }

        // Copy each actual physical packet only once per association, excluding overlays
        // through the real issuer's API. Bank descriptors bind EXACT triangle indices to
        // FinalBufferDigest. Avoid533219 owner reads by exporting3586 bank refs once.
        private IEnumerable<int> Prepare()
        {
            string[] ids = lease.CopyPhysicalPacketIds();
            Need(ids.Length == ReviewedPackets, "Exact1063 physical packets; overlays excluded by issuer");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var bankMap = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            int bankCount = 0;
            foreach (var bank in lease.CopyDerivedRoadBanks())
            {
                Need(bank.FinalBufferDigest == ReviewedFinal && bank.BlocksPedestrianAnimalPlacement
                    && bank.MaximumGrade <= MaximumDerivedBankGrade
                    && bank.SourceEdgeDigest != null && bank.SourceEdgeDigest.Length == DigestHexCharacters,
                    "Typed derived-bank binding");
                foreach (var reference in bank.CopyTriangleReferences())
                {
                    if (!bankMap.TryGetValue(reference.PacketId, out HashSet<int> selected))
                        bankMap[reference.PacketId] = selected = new HashSet<int>();
                    Need(selected.Add(reference.TriangleIndex), "Unique bank face");
                    bankCount++;
                    yield return 0;
                }
            }
            Need(bankCount == ReviewedBankTriangles, "Reviewed complete bank face set");
            foreach (string id in ids)
            {
                Need(unique.Add(id), "Duplicate physical packet id");
                if (copiesRemaining == 0 || copiedBytes >= ExportTargetBytesPerFrame) yield return -1;
                copiesRemaining--;
                Need(Current(), "Current owner before copy");
                var packet = lease.CopyPhysicalPacket(id);
                PacketCopiesThisFrame++;
                Need(packet.Id == id && packet.XYZ != null && packet.XYZ.Length / 3 <= DeclaredPacketVertices
                    && packet.Indices != null && packet.Indices.Length / 3 <= DeclaredPacketTriangles,
                    "Declared bounded original packet copy");
                int bytes = checked((packet.XYZ.Length + packet.Indices.Length) * ScalarBytes);
                Need(packet.XYZ.Length / 3 <= ReviewedMaximumPacketVertices
                    && packet.Indices.Length / 3 <= ReviewedMaximumPacketTriangles
                    && bytes <= ReviewedMaximumPacketExportBytes, "Measured reviewed per-packet copy ceiling");
                copiedBytes += bytes;
                Need(copiedBytes <= ExportHardLimitBytesPerFrame, "Bounded reviewed copy overshoot");
                CopiedVerticesThisFrame += packet.XYZ.Length / 3;
                CopiedIndicesThisFrame += packet.Indices.Length;
                var banks = new bool[packet.Indices.Length / 3];
                TemporaryBankMaskPayloadBytesThisFrame += banks.Length;
                if (bankMap.TryGetValue(id, out HashSet<int> selected))
                foreach (int triangle in selected)
                {
                    Need(triangle >= 0 && triangle < banks.Length, "Bank exact triangle index");
                    banks[triangle] = true;
                }
                foreach (int step in geometry.AddPacket(id, packet.OriginX, packet.OriginY, packet.OriginZ,
                    packet.XYZ, packet.Indices, lease.PacketRole(id) == PreparedWorldMeshPlan800.Role.GroundRoad, banks))
                    yield return step;
            }
            Need(geometry.PacketCount == ReviewedPackets && geometry.VertexCount == ReviewedVertices
                && geometry.TriangleCount == ReviewedTriangles && geometry.ReferenceCount == ReviewedReferences
                && geometry.CellCount == ReviewedCells && Current(),
                "Complete reviewed actual local inventory; no dropped/regridded surrogate faces");
        }

        /// <summary>
        /// Checks the entire swept0.85m footprint over a finite world-XZ segment<=8m.
        /// First spends the real-frame shared footprint budget BEFORE any owner check.
        /// A mandatory typed-owner CLOSED enclosing rectangle proves full support and
        /// derived-bank exclusion; no point/corner-only substitute is permitted.
        /// Local index then witnesses all intersecting road roles/normals/extrema.
        /// Unknown includes expiry, incomplete staging, unsupported shape and exhausted
        /// budgets. Clear is ground-only; semantic hazards/actors remain unbound.
        /// </summary>
        public Verdict InspectCorridor(float fromX, float fromZ, float toX, float toZ, out Envelope envelope)
        {
            envelope = default;
            if (frame != Time.frameCount || !Ready || refused
                || !Finite(fromX) || !Finite(fromZ) || !Finite(toX) || !Finite(toZ)
                || !budget.Use(ref budget.FootprintCalls)) return Verdict.Unknown;
            if (!Current()) return Verdict.Unknown;
            double deltaX = (double)toX - fromX, deltaZ = (double)toZ - fromZ;
            if (deltaX * deltaX + deltaZ * deltaZ > MaximumCorridorSquaredMetres) return Verdict.Unknown;

            // This conservative rectangle can falsely refuse useful bank-adjacent
            // paths; it intentionally includes the entire capsule, not only endpoints.
            float minimumX = (float)(Math.Min(fromX, toX) - EnclosingRectangleRadiusMetres);
            float maximumX = (float)(Math.Max(fromX, toX) + EnclosingRectangleRadiusMetres);
            float minimumZ = (float)(Math.Min(fromZ, toZ) - EnclosingRectangleRadiusMetres);
            float maximumZ = (float)(Math.Max(fromZ, toZ) + EnclosingRectangleRadiusMetres);
            footprint[0] = new Vector2(minimumX, minimumZ);
            footprint[1] = new Vector2(maximumX, minimumZ);
            footprint[2] = new Vector2(maximumX, maximumZ);
            footprint[3] = new Vector2(minimumX, maximumZ);
            footprint[4] = footprint[0];
            try
            {
                if (!lease.IsFootprintSupportedOutsideDerivedBanks(footprint, FootprintClearanceMetres, out string reason))
                {
                    Reason = "Full footprint unsupported/blocked/unknown: " + reason;
                    return Verdict.Unknown;
                }
                Verdict status = geometry.Inspect(new P(fromX, fromZ), new P(toX, toZ), AnimalRadiusMetres,
                    budget, out envelope, out string geometryReason);
                Reason = geometryReason;
                if (!Current()) return Verdict.Unknown;
                return status;
            }
            catch (Exception exception)
            {
                Reason = "Unknown issued footprint query: " + exception.Message;
                return Verdict.Unknown;
            }
        }

        /// <summary>
        /// Returns typed ACTUAL final-ground point height only after whole-footprint
        /// support/normals/range witnesses. Height metres are never interpolated from
        /// this index. Requires agreement<=0.2mm and derived transport error<=0.2mm.
        /// The0.349999m whole-disc range policy can conservatively refuse slopes/roughness.
        /// Shared16heightcalls/frame are not enough for arbitrary multi-actor routes;
        /// useful fair scheduling remains a separate pre-activation requirement.
        /// </summary>
        public Verdict TryGround(float x, float z, out Ground result)
        {
            return TryGround(x, z, out result, out _);
        }

        /// <summary>Diagnostic overload returning the SAME local disc envelope,
        /// without a second query or new authority. Unknown may return partial data.</summary>
        public Verdict TryGround(float x, float z, out Ground result, out Envelope observedEnvelope)
        {
            observedEnvelope = default;
            result = default;
            Verdict status = InspectCorridor(x, z, x, z, out Envelope envelope);
            observedEnvelope = envelope;
            if (status != Verdict.Clear) return status;
            if (envelope.MaximumY - envelope.MinimumY > MaximumFootprintHeightRangeMetres)
            {
                Reason = "Conservative whole-footprint height range";
                return Verdict.Blocked;
            }
            if (!budget.Use(ref budget.HeightCalls)) return Verdict.Unknown;
            try
            {
                if (!lease.TryGround(x, z, out var hit) || hit.MaximumAgreementGap > MaximumGroundAgreementMetres
                    || hit.Height < envelope.MinimumY - MaximumGroundAgreementMetres
                    || hit.Height > envelope.MaximumY + MaximumGroundAgreementMetres) return Verdict.Unknown;
                double uncertainty = HeightTransportGradeFactor * envelope.MaximumInverseInputError
                    + Math.Abs(hit.Height) * Math.Pow(2, FloatRelativePrecisionExponent) + HeightTransportMarginMetres;
                if (uncertainty > MaximumGroundAgreementMetres || !Current()) return Verdict.Unknown;
                result = new Ground
                {
                    Height = (float)hit.Height,
                    MinimumNormalY = (float)envelope.MinimumNormalY,
                    PacketId = hit.PacketId,
                    Triangle = hit.Triangle,
                    NativeCoordinateErrorBound = uncertainty
                };
                return Verdict.Clear;
            }
            catch (Exception exception)
            {
                Reason = "Typed final ground query refused: " + exception.Message;
                return Verdict.Unknown;
            }
        }

        /// <summary>
        /// Releases owned iterator/subscription/index association, never actual owner
        /// objects/meshes. Revokes local geometry evidence. New-bind per-frame counters
        /// intentionally survive to prevent Dispose-based binding-budget bypass.
        /// Must not be called concurrently or reentrantly from outside this consumer.
        /// </summary>
        public void Dispose()
        {
            pending?.Dispose();
            pending = null;
            revokeSubscription?.Dispose();
            revokeSubscription = null;
            lease = null;
            Ready = false;
            geometry?.Refuse();
            geometry = null;
            frame = -1;
        }
    }
}
