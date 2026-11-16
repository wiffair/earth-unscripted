using System;

namespace EarthUnscripted.Core800
{
    /// <summary>
    /// Actual uploader result for the trusted scene coordinator. Give Lease to
    /// consumers; retain Control and revoke BEFORE disabling/destroying the parent.
    /// This bundle is not source/provider or whole-world admission.
    /// </summary>
    public sealed class UploadedGroundBuild800
    {
        public GroundLease800 Lease { get; }
        public UploadedGroundControl800 Control { get; }

        internal UploadedGroundBuild800(UploadedGroundOwner800 owner, object key, GroundLease800 lease)
        {
            owner.AuthorizeCallerControl(key, lease);
            Lease = lease;
            Control = new UploadedGroundControl800(owner, key, lease);
        }
    }

    /// <summary>
    /// Cleanup-only authority returned to the creator, never inferred through an
    /// object-name lookup. Main-thread Revoke is idempotent after retirement.
    /// </summary>
    public sealed class UploadedGroundControl800
    {
        private readonly UploadedGroundOwner800 owner;
        public GroundLease800 Lease { get; }

        internal UploadedGroundControl800(UploadedGroundOwner800 owner, object key, GroundLease800 lease)
        {
            owner.AuthorizeCallerControl(key, lease);
            this.owner = owner;
            Lease = lease;
        }

        public void Revoke(string reason) => owner.Revoke(reason);
    }
}
