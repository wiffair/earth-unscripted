using System;
using System.IO;
using System.Threading;
using UnityEngine;
using RoadRush.Core800;
using RoadRush.TerrainDraft.Core800;

namespace RoadRush
{
    /// <summary>
    /// Worker-created capability for one reviewed 800 m diagnostic session. This
    /// recaptures original source and validates the complete cooked producer;
    /// filenames and receipt flags never mint ground/Town or gameplay authority.
    /// Immutable final arrays remain owned by Plan. Scene upload stays main-thread.
    /// </summary>
    internal sealed class PreparedScopedSession800
    {
        internal const string ExactRoadId = "59973878-span-0";
        internal const int ActorCount = 28; // Actual default factory cohort: 1 + 24 + 3.
        internal const double EndMarginMetres = 20;
        internal const double ActorSpacingMetres = 7.5;
        internal const float DiagnosticWidthMetres = 2.4f; // Corridor policy, NOT source road width.
        const double FootprintHalfWidthMetres = 1.05;
        const double FootprintHalfLengthMetres = 2.5;
        // Existing construction limits: 18% longitudinal + 2.5% crossfall.
        // A five-metre body can therefore span 0.9535 m vertically; 0.8 m
        // would incorrectly reject valid near-limit roads in worker preflight.
        const double MaximumSupportSpreadMetres =
            2 * FootprintHalfLengthMetres * .18 + 2 * FootprintHalfWidthMetres * .025 + .001;
        const string FinalDigest = "04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf";

        internal CanonicalRaw13Document Source { get; }
        internal PreparedWorldMeshPlan800 Plan { get; }
        internal double FirstSourceS { get; }
        internal double LastSourceS { get; }
        internal float CorridorLength => (float)(LastSourceS - FirstSourceS);

        PreparedScopedSession800(CanonicalRaw13Document source, PreparedWorldMeshPlan800 plan)
        {
            Source = source;
            Plan = plan;
            var rows = plan.CopySourceRows(ExactRoadId);
            if (rows.Length < 2) throw new InvalidDataException("Reviewed named road rows missing.");
            FirstSourceS = rows[0].SourceS + EndMarginMetres;
            LastSourceS = rows[rows.Length - 1].SourceS - EndMarginMetres;
            if (LastSourceS - FirstSourceS < (ActorCount - 1) * ActorSpacingMetres)
                throw new InvalidDataException("Named reviewed corridor cannot fit the actual 28-role cohort.");
        }

        internal static PreparedScopedSession800 PrepareReviewed(string cookedPath,
            string sourceContext, CancellationToken cancellation)
        {
            var captured = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(sourceContext, cancellation);
            byte[] raw = captured.CopyDocumentBytes(cancellation);
            var proof = VerifiedCoreTerrain.LoadReviewedCooked(cookedPath, raw, cancellation);
            if (proof.FinalBufferDigest != FinalDigest)
                throw new InvalidDataException("Scoped session requires the reviewed final physical buffers.");
            var source = CanonicalRaw13Codec.Read(captured, raw);
            var plan = PreparedWorldMeshPlan800.FromVerifiedCore(source, proof, cancellation);
            var result = new PreparedScopedSession800(source, plan);
            for (int actor = 0; actor < ActorCount; actor++)
            {
                cancellation.ThrowIfCancellationRequested();
                result.ValidateActorFootprint(actor);
            }
            return result;
        }

        /// <summary>Exact named uploaded road sheet. No nearest road/DEM fallback.</summary>
        internal Vector3 Point(float corridorDistance, float lateral = 0)
        {
            if (!float.IsFinite(corridorDistance) || !float.IsFinite(lateral) ||
                corridorDistance < 0 || corridorDistance > CorridorLength ||
                Math.Abs(lateral) > DiagnosticWidthMetres * .5)
                throw new InvalidOperationException("Scoped corridor request outside diagnostic support.");
            double station = FirstSourceS + corridorDistance;
            if (!Plan.TrySourceFrame(ExactRoadId, station, RoadRowSide.Next, out var frame))
                throw new InvalidOperationException("Named source frame unavailable.");
            double x = frame.CenterX + lateral * frame.RightX;
            double z = frame.CenterZ + lateral * frame.RightZ;
            if (!Plan.TryRoad(ExactRoadId, x, z, out var surface))
                throw new InvalidOperationException("Named final road sheet does not cover corridor point.");
            return new Vector3((float)x, (float)surface.Height, (float)z);
        }

        internal Quaternion Rotation(float distance)
        {
            float before = Mathf.Max(0, distance - .1f);
            float after = Mathf.Min(CorridorLength, distance + .1f);
            return Quaternion.LookRotation(Point(after) - Point(before), Vector3.up);
        }

        internal float ActorDistance(int actorIndex)
        {
            if (actorIndex < 0 || actorIndex >= ActorCount) throw new ArgumentOutOfRangeException(nameof(actorIndex));
            return (float)(actorIndex * ActorSpacingMetres);
        }

        void ValidateActorFootprint(int actorIndex)
        {
            float distance = ActorDistance(actorIndex);
            Vector3 centre = Point(distance);
            if (!Plan.TrySourceFrame(ExactRoadId, FirstSourceS + distance, RoadRowSide.Next, out var frame) || !frame.HasLongitudinal)
                throw new InvalidDataException("Scoped spawn has no exact longitudinal frame.");
            double tangentLength = Math.Sqrt(frame.DCenterXPerH * frame.DCenterXPerH + frame.DCenterZPerH * frame.DCenterZPerH);
            if (!(tangentLength > 0)) throw new InvalidDataException("Scoped spawn frame tangent degenerate.");
            // Pure XZ rotation here: Quaternion.LookRotation is an engine API and
            // must never run on the preparation worker.
            double forwardX = frame.DCenterXPerH / tangentLength;
            double forwardZ = frame.DCenterZPerH / tangentLength;
            double rightX = forwardZ, rightZ = -forwardX;
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            // Nine samples are a bounded spawn preflight only. Actual 28 physical
            // conversions, support rays and building collision remain native gates.
            for (int longitudinal = -1; longitudinal <= 1; longitudinal++)
            for (int side = -1; side <= 1; side++)
            {
                double x = centre.x + forwardX * longitudinal * FootprintHalfLengthMetres + rightX * side * FootprintHalfWidthMetres;
                double z = centre.z + forwardZ * longitudinal * FootprintHalfLengthMetres + rightZ * side * FootprintHalfWidthMetres;
                if (!Plan.TryRoad(ExactRoadId, x, z, out var road) || !Plan.TryGround(x, z, out var ground) ||
                    Math.Abs(road.Height - ground.Height) > .001)
                    throw new InvalidDataException("Actual cohort spawn footprint lacks named final road support at slot " + actorIndex);
                low = Math.Min(low, road.Height);
                high = Math.Max(high, road.Height);
            }
            if (high - low > MaximumSupportSpreadMetres)
                throw new InvalidDataException("Scoped spawn footprint spread " + (high - low).ToString("R") + " exceeds derived body-envelope bound at slot " + actorIndex);
        }
    }
}
