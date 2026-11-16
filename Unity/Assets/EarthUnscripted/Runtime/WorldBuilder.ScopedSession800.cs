using System;
using System.Collections;
using System.Threading;
using EarthUnscripted.Core800;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class WorldBuilder
    {
        internal PreparedScopedSession800 ScopedSession800 { get; private set; }
        internal GroundLease800 ScopedGround800 => scopedGroundBuild800?.Lease;
        UploadedGroundBuild800 scopedGroundBuild800;
        Material scopedTerrain800;

        /// <summary>
        /// Actual session-owned world factory. Creates the genuine physical owner
        /// and Town on this builder before normal actor factories run. No legacy
        /// roads, terrain, garage pad or roadside layby colliders are overlaid.
        /// Cancellation revokes consumers before destroying owned scene content.
        /// </summary>
        internal IEnumerator BuildScopedSession800(PreparedScopedSession800 prepared,
            CancellationToken cancellation, Func<bool> current)
        {
            if (prepared == null || current == null || !current() || GeographicWorld || Route != null ||
                string.IsNullOrEmpty(PlayerProfile.StorageOverride))
                throw new InvalidOperationException("Fresh isolated actual WorldBuilder required.");
            ScopedSession800 = prepared;
            Route = new RoutePath(prepared);
            Checkpoints = new Vector3[0];
            BuildEnvironment();
            Asphalt = Geometry.Surface("Scoped final road", new Color(.13f, .145f, .16f), .22f, 1);
            scopedTerrain800 = Geometry.Surface("Scoped final terrain", new Color(.25f, .32f, .18f), .3f, 1);
            bool completed = false;
            try
            {
                yield return UploadedGroundOwner800.BuildOwned(transform, prepared.Plan, Asphalt,
                    scopedTerrain800, cancellation, current, value => scopedGroundBuild800 = value);
                if (!current() || scopedGroundBuild800 == null || !scopedGroundBuild800.Lease.IsCurrent)
                    throw new OperationCanceledException("Scoped physical owner not current.");
                var town = new GameObject("Actual session scoped Town800");
                town.transform.SetParent(transform, false);
                GeographicWorld = town.AddComponent<GeoWorld>();
                yield return GeographicWorld.BuildScopedTown800(prepared.Source, prepared.Plan,
                    scopedGroundBuild800.Lease, cancellation);
                if (!current() || GeographicWorld.CurrentTownPublication800 == null ||
                    !GeographicWorld.CurrentTownPublication800.IsCurrent)
                    throw new OperationCanceledException("Scoped actual Town publication not current.");
                completed = true;
            }
            finally
            {
                if (!completed) RetireScopedSession800("scoped session build cancelled or failed");
            }
        }

        internal void RetireScopedSession800(string reason)
        {
            scopedGroundBuild800?.Control.Revoke(reason);
            scopedGroundBuild800 = null;
        }

        void OnDestroy()
        {
            RetireScopedSession800("actual WorldBuilder destroyed");
            if (ScopedSession800 != null)
            {
                if (Asphalt) Destroy(Asphalt);
                if (scopedTerrain800) Destroy(scopedTerrain800);
            }
        }
    }
}
