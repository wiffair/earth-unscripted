using System;
using System.Collections.Generic;
using System.Linq;

namespace RoadRush.RouteProof3
{
    /// <summary>
    /// Pure diagnostic progress admission for a physical FORWARD crossing between
    /// immediately consecutive source-path segments. Closest-point arc coordinates
    /// can jump at a polyline corner even when a real body moves only centimetres.
    /// This proves that specific topological event; it does not raise the ordinary
    /// arc-step limit, repair a pose, choose a nearby road or grant world authority.
    /// XYZ and physical distances are metres; plane calculations use horizontal X/Z.
    /// </summary>
    public static class SweptSourceCorner32
    {
        public const string Recipe = "AdjacentSourceCorner/SweptBisector/1";
        public const double MaximumPhysicalStepMetres = 0.15; // Existing independent raw-physics bound.
        private const double MinimumDirectionLength = 1e-12; // Reject undefined zero/180degree bisectors.
        public readonly struct Point
        {
            public readonly double X, Y, Z;
            public Point(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            internal bool Finite => IsFinite(X) && IsFinite(Y) && IsFinite(Z);
        }

        /// <summary>Immutable descriptor from the exact owned route sample table.</summary>
        public sealed class Segment
        {
            public long Ordinal { get; }
            public Point A { get; }
            public Point B { get; }
            public string NodeId { get; }

            private readonly string[] roads;
            public string[] CopyRoadIds() => (string[])roads.Clone();
            public Segment(long ordinal, Point a, Point b, string nodeId, string[] sourceRoadIds)
            {
                Ordinal = ordinal;
                A = a;
                B = b;
                NodeId = nodeId ?? "";
                roads = sourceRoadIds == null ? Array.Empty<string>() : (string[])sourceRoadIds.Clone();
            }
        }

        /// <summary>Exact source endpoint-graph join, not a proximity connection.</summary>
        public readonly struct Join
        {
            public readonly string NodeId, IncomingRoad, OutgoingRoad;
            public Join(string nodeId, string incomingRoad, string outgoingRoad)
            {
                NodeId = nodeId;
                IncomingRoad = incomingRoad;
                OutgoingRoad = outgoingRoad;
            }
        }

        [Serializable]
        public sealed class Evidence
        {
            public string recipe = Recipe, sourceNode;
            public long fromSegment, toSegment;
            public double physicalStepMetres, previousGateMetres, currentGateMetres;
            public double crossingRadiusMetres, maximumCrossingRadiusMetres, turnRadians;
            public double gateNormalX, gateNormalZ, crossingX, crossingZ;
        }

        /// <summary>
        /// Admit only one adjacent forward corner event, using the actual previous
        /// and current physics positions. Both selected finite segments must share
        /// the SAME source vertex exactly. All local road identities must be one
        /// source road or its actual directed source-node join. The body step must
        /// cross that vertex's forward bisector, within the unchanged corridor's
        /// geometrically derived radius. Caller still checks real four-wheel support,
        /// source-node/whole-loop travel, ordinary arc limits and every idle record.
        /// Failure returns a reason and never substitutes/clamps a progress value.
        /// </summary>
        public static bool TryProve(Segment previousSegment, Segment selectedSegment, IReadOnlyList<Join> actualSourceJoins, Point previousBody, Point currentBody, double signedForwardSpeed, bool reverseGear, double corridorMetres, out Evidence evidence, out string refusal)
        {
            evidence = null;
            refusal = null;
            if (actualSourceJoins != null && actualSourceJoins.Count > 32)
                return Refuse("Bounded actual source joins required", out refusal);
            if (previousSegment == null || selectedSegment == null || actualSourceJoins == null || !previousBody.Finite || !currentBody.Finite || !IsFinite(corridorMetres) || corridorMetres <= 0 || !IsFinite(signedForwardSpeed))
                return Refuse("Finite actual source/physics inputs required", out refusal);
            if (previousSegment.Ordinal < 0 || selectedSegment.Ordinal != previousSegment.Ordinal + 1)
                return Refuse("Cannot skip a source segment/node or use a nonadjacent closest branch", out refusal);
            if (!previousSegment.A.Finite || !previousSegment.B.Finite || !selectedSegment.A.Finite || !selectedSegment.B.Finite || !Equal(previousSegment.B, selectedSegment.A))
                return Refuse("Exact shared source-path vertex required; proximity is insufficient", out refusal);
            if (reverseGear || signedForwardSpeed <= 0)
                return Refuse("This admission proves forward traversal, never reverse/backing", out refusal);
            double step = Distance(previousBody, currentBody);
            if (!(step > 0 && step <= MaximumPhysicalStepMetres))
                return Refuse("Existing physical fixed-step movement bound exceeded", out refusal);
            if (HorizontalDistance(previousBody, previousSegment.A, previousSegment.B) > corridorMetres || HorizontalDistance(currentBody, selectedSegment.A, selectedSegment.B) > corridorMetres)
                return Refuse("Both actual endpoint poses must remain in the unchanged horizontal corridor", out refusal);
            var oldRoads = previousSegment.CopyRoadIds();
            var newRoads = selectedSegment.CopyRoadIds();
            if (oldRoads.Length == 0 || newRoads.Length == 0 || oldRoads.Length > 2 || newRoads.Length > 2 || oldRoads.Concat(newRoads).Any(string.IsNullOrEmpty))
                return Refuse("Bound local source membership required", out refusal);
            string[] union = oldRoads.Concat(newRoads).Distinct(StringComparer.Ordinal).ToArray();
            string node = "";
            if (union.Length != 1)
            {
                if (union.Length != 2)
                    return Refuse("Cannot alias unrelated source roads", out refusal);
                var matches = actualSourceJoins.Where(join => !string.IsNullOrEmpty(join.NodeId) && (join.NodeId == previousSegment.NodeId || join.NodeId == selectedSegment.NodeId) && union.Contains(join.IncomingRoad) && union.Contains(join.OutgoingRoad) && join.IncomingRoad != join.OutgoingRoad).ToArray();
                if (matches.Length != 1)
                    return Refuse("Exact directed source-node witness required for a road transition", out refusal);
                node = matches[0].NodeId;
                // On a leg transition, an outgoing-only segment cannot precede an
                // incoming-only segment. Literal cap intervals may carry both IDs.
                if (!oldRoads.Contains(matches[0].IncomingRoad) || !newRoads.Contains(matches[0].OutgoingRoad))
                    return Refuse("Source-road transition opposes the directed route join", out refusal);
            }

            Point vertex = previousSegment.B;
            double ux = vertex.X - previousSegment.A.X, uz = vertex.Z - previousSegment.A.Z;
            double vx = selectedSegment.B.X - vertex.X, vz = selectedSegment.B.Z - vertex.Z;
            double uLength = Math.Sqrt(ux * ux + uz * uz), vLength = Math.Sqrt(vx * vx + vz * vz);
            if (!(uLength > MinimumDirectionLength && vLength > MinimumDirectionLength))
                return Refuse("Finite nonzero source-segment directions required", out refusal);
            ux /= uLength;
            uz /= uLength;
            vx /= vLength;
            vz /= vLength;
            double nx = ux + vx, nz = uz + vz, normalLength = Math.Sqrt(nx * nx + nz * nz);
            if (!(normalLength > MinimumDirectionLength))
                return Refuse("Opposed/undefined source corner cannot mint a crossing", out refusal);
            nx /= normalLength;
            nz /= normalLength;
            double before = (previousBody.X - vertex.X) * nx + (previousBody.Z - vertex.Z) * nz;
            double after = (currentBody.X - vertex.X) * nx + (currentBody.Z - vertex.Z) * nz;
            if (!(before <= 0 && after >= 0 && after > before))
                return Refuse("Actual swept body did not cross the adjacent forward corner gate", out refusal);
            double fraction = -before / (after - before);
            double crossX = previousBody.X + (currentBody.X - previousBody.X) * fraction;
            double crossZ = previousBody.Z + (currentBody.Z - previousBody.Z) * fraction;
            double radius = Math.Sqrt((crossX - vertex.X) * (crossX - vertex.X) + (crossZ - vertex.Z) * (crossZ - vertex.Z));
            double turn = Math.Acos(Math.Max(-1, Math.Min(1, ux * vx + uz * vz)));
            // On the bisector, distance to either incident infinite line equals
            // radial distance*cos(turn/2). This derives a bound from the SAME0.9m
            // corridor, rather than introducing a broader corner tolerance.
            double cosine = Math.Cos(turn * 0.5);
            if (!(cosine > MinimumDirectionLength && radius <= corridorMetres / cosine))
                return Refuse("Swept crossing lies outside the unchanged corridor's corner envelope", out refusal);
            evidence = new Evidence
            {
                sourceNode = node,
                fromSegment = previousSegment.Ordinal,
                toSegment = selectedSegment.Ordinal,
                physicalStepMetres = step,
                previousGateMetres = before,
                currentGateMetres = after,
                crossingRadiusMetres = radius,
                maximumCrossingRadiusMetres = corridorMetres / cosine,
                turnRadians = turn,
                gateNormalX = nx,
                gateNormalZ = nz,
                crossingX = crossX,
                crossingZ = crossZ
            };
            return true;
        }

        private static double HorizontalDistance(Point point, Point a, Point b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, squared = dx * dx + dz * dz;
            if (!(squared > 0))
                return double.PositiveInfinity;
            double fraction = Math.Max(0, Math.Min(1, ((point.X - a.X) * dx + (point.Z - a.Z) * dz) / squared));
            double x = a.X + dx * fraction, z = a.Z + dz * fraction;
            return Math.Sqrt((point.X - x) * (point.X - x) + (point.Z - z) * (point.Z - z));
        }

        private static bool Refuse(string reason, out string refusal)
        {
            refusal = reason;
            return false;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Equal(Point a, Point b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}
