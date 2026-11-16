using System;
using System.Threading;
using UnityEngine;
using EarthUnscripted.TerrainDraft;
using EarthUnscripted.TerrainDraft.Core800;

namespace EarthUnscripted.Core800
{
    // Runtime physical-subtree lease, NOT provider/building/source ownership or whole-world admission.
    public sealed class GroundLease800
    {
        readonly UploadedGroundOwner800 owner;
        readonly PreparedWorldMeshPlan800 plan;
        readonly Matrix4x4 frame;
        public int Generation { get; }
        public string RuntimeOwnerHandle { get; }
        public Matrix4x4 Frame => frame;
        public string RawDocumentDigest => plan.RawDocumentDigest;
        public string SourceDigest => plan.SourceDigest;
        public string ControlDigest => plan.ControlDigest;
        public string OwnerLedgerDigest => plan.OwnerLedgerDigest;
        public string StructureLedgerDigest => plan.StructureLedgerDigest;
        public string ScopeCoverageDigest => plan.ScopeCoverageDigest;
        public string FinalBufferDigest => plan.FinalBufferDigest;
        public string GeometryDigest => plan.GeometryDigest;
        public bool SourceSubsetOnly => true;
        public bool FullWorldAdmitted => false;

        internal GroundLease800(UploadedGroundOwner800 owner, object key, int generation, Matrix4x4 frame)
        {
            owner.AuthorizeLeaseIssuance(key, generation);
            this.owner = owner;
            plan = owner.Plan;
            Generation = generation;
            RuntimeOwnerHandle = owner.RuntimeHandle;
            this.frame = frame;
        }

        // Reference identity is necessary: equal payload digests cannot substitute a different prepared object.
        internal bool OwnsPlan(PreparedWorldMeshPlan800 exactPlan) => ReferenceEquals(plan, exactPlan);
        public bool IsCurrent => owner.CheckLeaseCurrent(Generation);

        void Require()
        {
            if (!IsCurrent)
                throw new InvalidOperationException("Expired uploaded800 ground lease; no fallback");
        }

        public string[] CopyPhysicalPacketIds()
        {
            Require();
            return owner.CopyPhysicalPacketIds();
        }

        public LocalTerrainMesh CopyPhysicalPacket(string exactPacketId)
        {
            Require();
            return plan.CopyForUpload(exactPacketId);
        }

        public PreparedWorldMeshPlan800.Role PacketRole(string exactPacketId)
        {
            Require();
            return plan.PacketRole(exactPacketId);
        }

        public DerivedRoadBankDescriptor[] CopyDerivedRoadBanks()
        {
            Require();
            return plan.CopyDerivedRoadBanks();
        }

        public bool IsDerivedRoadBank(string exactPacketId, int triangleIndex)
        {
            Require();
            return plan.IsDerivedRoadBank(exactPacketId, triangleIndex);
        }

        public bool TryGround(double x, double z, out SharedFinalMeshIndex800.Hit hit)
        {
            hit = default;
            return IsCurrent && plan.TryGround(x, z, out hit);
        }

        public bool TryRoad(string exactRoadId, double x, double z, out SharedFinalMeshIndex800.Hit hit)
        {
            hit = default;
            return IsCurrent && plan.TryRoad(exactRoadId, x, z, out hit);
        }

        public bool IsFootprintSupportedOutsideDerivedBanks(Vector2[] closedWorldXZ, double clearanceMargin, out string reason, CancellationToken cancellation = default)
        {
            if (!IsCurrent)
            {
                reason = "ExpiredLease";
                return false;
            }

            return owner.FootprintSupported(closedWorldXZ, clearanceMargin, out reason, cancellation);
        }

        public IDisposable BeforeRevoke(Action<GroundLease800, string> callback)
        {
            Require();
            return owner.RegisterBeforeRevoke(this, callback);
        }
    }
}
