using System;
using EarthUnscripted.Core800;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GeoWorld
    {
        /// <summary>
        /// Private-lived physical town identity used only to bind vehicle samples.
        /// This is NOT closed-building/facade/door ownership or a scenery-safe grant.
        /// Only an actual completed town root and actual ground lease can issue it.
        /// </summary>
        internal sealed class VehicleTownContext800
        {
            readonly GeoWorld world;
            readonly GroundLease800 ground;
            readonly GameObject townRoot;
            readonly ScopedTownSupport800 support;
            readonly GeoRegion region;
            readonly int townGeneration;
            readonly int worldGeneration;
            readonly Matrix4x4 worldFrame;
            readonly Matrix4x4 townFrame;

            internal readonly string RuntimeHandle = Guid.NewGuid().ToString("N");
            internal string GroundRuntimeHandle => ground.RuntimeOwnerHandle;
            internal string SourceDigest => ground.SourceDigest;
            internal string PhysicalDigest => ground.FinalBufferDigest;
            internal GeoWorld World => world;

            internal VehicleTownContext800(GeoWorld world, GroundLease800 ground)
            {
                this.world = world;
                this.ground = ground;
                townRoot = world.scopedTownRoot800;
                support = world.scopedTownSupport800;
                region = world.Region;
                townGeneration = world.townLifetime800;
                worldGeneration = world.generation;
                worldFrame = world.transform.localToWorldMatrix;
                townFrame = townRoot.transform.localToWorldMatrix;
            }

            /// <summary>
            /// Main-thread exact lifetime association only. Both tokens are checked
            /// live; opaque runtime handles and equal content hashes cannot stand
            /// in for the same actual world, lease, region and generation objects.
            /// The semantic consumer still performs publication.ValidateSourceNow
            /// and a fresh vehicle snapshot check before committing its readmodel.
            /// </summary>
            internal bool MatchesTownPublication800(TownSemanticPublication800 publication)
            {
                return publication != null && ReferenceEquals(publication.World, world) &&
                    ReferenceEquals(publication.Ground, ground) &&
                    ReferenceEquals(publication.SourceRegion, region) &&
                    publication.WorldGeneration == worldGeneration &&
                    publication.TownGeneration == townGeneration &&
                    publication.Frame.Equals(worldFrame) && IsCurrent && publication.IsCurrent;
            }

            internal bool IsCurrent => world && townRoot && townRoot.activeInHierarchy &&
                ground.IsCurrent && !world.scopedTownPreparing800 && !world.scopedTownRejectQueries800 &&
                ReferenceEquals(world.scopedTownRoot800, townRoot) &&
                ReferenceEquals(world.scopedTownSupport800, support) &&
                ReferenceEquals(world.Region, region) && world.townLifetime800 == townGeneration &&
                world.generation == worldGeneration && world.townSupportCurrent800 != null &&
                world.townSupportCurrent800() && world.transform.localToWorldMatrix.Equals(worldFrame) &&
                townRoot.transform.localToWorldMatrix.Equals(townFrame) && support.MatchesVehicleGround800(ground);
        }

        /// <summary>
        /// Called only by the owning session. It authenticates actual private town
        /// state rather than trusting an exported diagnostic ledger/status field.
        /// </summary>
        internal bool TryCaptureVehicleTownContext800(GroundLease800 ground,
            out VehicleTownContext800 context)
        {
            context = null;
            if (ground == null || !ground.IsCurrent || !scopedTownRoot800 ||
                !scopedTownRoot800.activeInHierarchy || scopedTownPreparing800 ||
                scopedTownRejectQueries800 || scopedTownSupport800 == null ||
                !scopedTownSupport800.MatchesVehicleGround800(ground))
                return false;
            var candidate = new VehicleTownContext800(this, ground);
            if (!candidate.IsCurrent) return false;
            context = candidate;
            return true;
        }
    }

    // Guarded source proposal changes the original declaration to partial only.
    public sealed partial class ScopedTownSupport800
    {
        internal bool MatchesVehicleGround800(GroundLease800 ground) => ground.IsCurrent &&
            ground.SourceDigest == plan.SourceDigest && ground.RawDocumentDigest == plan.RawDocumentDigest &&
            ground.FinalBufferDigest == plan.FinalBufferDigest && ground.GeometryDigest == plan.GeometryDigest;
    }
}
