using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core800;

namespace EarthUnscripted.RouteProof3
{
    /// <summary>
    /// Read-only driver-path candidate on an already admitted scoped final mesh.
    /// Exact source endpoint IDs establish connectivity; distance never creates an
    /// edge. Literal incoming/outgoing row sides remain in the control ledger.
    /// A driver path is not new road geometry or proof that a car can traverse it.
    /// All coordinates/lengths are metres in the bound source frame, Y up.
    /// </summary>
    public sealed class JoinedLoopRoute
    {
        public const string Recipe = "SourceConnectedLoop/FinalNamedRows/1";
        private const int MaximumLegs = 32;
        private const int MaximumControls = 20_000;
        private const int MaximumSamples = 40_000;
        private const double SamplingStepMetres = 0.5;
        private const double NamedHeightAgreementMetres = 0.0002;
        private const double MinimumWidthMetres = 2;
        private const double MaximumWidthMetres = 30;
        /// <summary>A source road and travel sign: +1 follows its literal source S.</summary>
        public readonly struct Leg
        {
            public readonly string RoadId;
            public readonly int Direction;
            public Leg(string roadId, int direction)
            {
                if (string.IsNullOrEmpty(roadId) || (direction != 1 && direction != -1))
                    throw new ArgumentException("Named road and explicit +/-1 direction required");
                RoadId = roadId;
                Direction = direction;
            }
        }

        /// <summary>
        /// An unchanged one-sided source row mapped to its UK-left lane centre.
        /// Equal source station pairs are retained; they are not de-duplicated.
        /// These are driver controls, not a replacement for original source points.
        /// </summary>
        public readonly struct Control
        {
            public readonly string RoadId;
            public readonly double SourceS, X, Y, Z, LaneOffset;
            public readonly bool PreviousSide;
            public readonly int Direction;
            internal Control(string id, double station, bool previous, int direction, double lane, double x, double y, double z)
            {
                RoadId = id;
                SourceS = station;
                PreviousSide = previous;
                Direction = direction;
                LaneOffset = lane;
                X = x;
                Y = y;
                Z = z;
            }
        }

        /// <summary>
        /// One sampled driver target. Membership contains only its exact source leg,
        /// or the two legs meeting at an explicitly checked shared source node.
        /// Between duplicate side rows, both controls are retained in the ledger.
        /// </summary>
        public sealed class Sample
        {
            public double ArcMetres { get; internal set; }
            public double X { get; internal set; }
            public double Y { get; internal set; }
            public double Z { get; internal set; }
            public double ForwardX { get; internal set; }
            public double ForwardZ { get; internal set; }
            public string NodeId { get; internal set; }

            public string[] CopyRoadIds() => (string[])roads.Clone();
            internal string[] roads;
            internal Sample Clone() => new Sample
            {
                ArcMetres = ArcMetres,
                X = X,
                Y = Y,
                Z = Z,
                ForwardX = ForwardX,
                ForwardZ = ForwardZ,
                NodeId = NodeId,
                roads = (string[])roads.Clone()
            };
        }

        /// <summary>
        /// Source-topology join receipt. Angles are measured from outward final
        /// frames, in degrees; classification is geometric, not traffic priority.
        /// </summary>
        public sealed class Junction
        {
            public string NodeId { get; internal set; }
            public string IncomingRoad { get; internal set; }
            public string OutgoingRoad { get; internal set; }
            public string GeometryDescription { get; internal set; }

            public double[] CopyArmGapsDegrees() => (double[])gaps.Clone();
            public string[] CopyArmRoadIds() => (string[])arms.Clone();
            internal double[] gaps;
            internal string[] arms;
            internal Junction Clone() => new Junction
            {
                NodeId = NodeId,
                IncomingRoad = IncomingRoad,
                OutgoingRoad = OutgoingRoad,
                GeometryDescription = GeometryDescription,
                gaps = (double[])gaps.Clone(),
                arms = (string[])arms.Clone()
            };
        }

        private readonly Control[] controls;
        private readonly Sample[] samples;
        private readonly Junction[] junctions;
        public string RawDocumentDigest { get; }
        public string FinalBufferDigest { get; }
        public double LengthMetres => samples[samples.Length - 1].ArcMetres;
        public bool DrivingAdmitted => false;

        public Control[] CopyControls() => (Control[])controls.Clone();
        public Sample[] CopySamples() => samples.Select(s => s.Clone()).ToArray();
        public Junction[] CopyJunctions() => junctions.Select(j => j.Clone()).ToArray();
        private JoinedLoopRoute(PreparedWorldMeshPlan800 plan, Control[] sourceControls, Sample[] targets, Junction[] joins)
        {
            RawDocumentDigest = plan.RawDocumentDigest;
            FinalBufferDigest = plan.FinalBufferDigest;
            controls = sourceControls;
            samples = targets;
            junctions = joins;
        }

        /// <summary>
        /// Build a closed driver candidate using a verified plan and independently
        /// captured canonical raw source. Throws on source mismatch, one-way conflict,
        /// unconnected endpoints, absent named support, height conflict or work cap.
        /// Does not mutate inputs, upload meshes, place cars or touch Unity APIs.
        /// Cancellation and work execute on the calling worker thread.
        /// </summary>
        public static JoinedLoopRoute Build(PreparedWorldMeshPlan800 plan, byte[] independentlyCapturedRaw, IReadOnlyList<Leg> routeLegs, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (plan == null || independentlyCapturedRaw == null || independentlyCapturedRaw.Length > World13DocumentCodec.MaximumBytes || World13DocumentCodec.Hash(independentlyCapturedRaw) != plan.RawDocumentDigest)
                throw new InvalidDataException("Actual plan/raw source identity required");
            if (routeLegs == null || routeLegs.Count < 3 || routeLegs.Count > MaximumLegs)
                throw new InvalidDataException("Bounded closed source loop required");
            var raw = JObject.Parse(new UTF8Encoding(false, true).GetString(independentlyCapturedRaw));
            var roads = raw["geography"]?["roads"]?.Children<JObject>().ToDictionary(r => (string)r["id"], StringComparer.Ordinal) ?? throw new InvalidDataException("Canonical source road graph absent");
            var controlList = new List<Control>();
            var joins = new List<Junction>();
            var joinByPair = new Dictionary<(string, string), string>();
            var seenLegs = new HashSet<string>(StringComparer.Ordinal);
            for (int legIndex = 0; legIndex < routeLegs.Count; legIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                var leg = routeLegs[legIndex];
                if (!seenLegs.Add(leg.RoadId) || !roads.TryGetValue(leg.RoadId, out var road))
                    throw new InvalidDataException("Missing/repeated source leg");
                if ((bool? )road["bridge"] == true || (bool? )road["tunnel"] == true || (int? )road["layer"] != 0)
                    throw new InvalidDataException("Ground-only loop cannot alias a structure");
                int oneWay = (int? )road["oneWay"] ?? 0;
                if (oneWay != 0 && oneWay != leg.Direction)
                    throw new InvalidDataException("Route opposes actual source one-way direction");
                double width = (double? )road["width"] ?? double.NaN;
                if (!Finite(width) || width < MinimumWidthMetres || width > MaximumWidthMetres)
                    throw new InvalidDataException("Source lane width unavailable/outside driver policy");
                var rows = plan.CopySourceRows(leg.RoadId);
                if (rows.Length < 2 || controlList.Count + rows.Length > MaximumControls)
                    throw new InvalidDataException("Complete named final rows required, never truncate");
                // Reverse the literal row sequence, including both duplicate sides.
                // UK left is negative travel-right; reverse travel flips its sign.
                var ordered = leg.Direction == 1 ? rows : rows.Reverse().ToArray();
                double lane = -leg.Direction * width * 0.25;
                foreach (var row in ordered)
                {
                    cancellation.ThrowIfCancellationRequested();
                    double x = row.CenterX + lane * row.RightX;
                    double z = row.CenterZ + lane * row.RightZ;
                    double y = Height(plan, new[] { leg.RoadId }, x, z);
                    controlList.Add(new Control(leg.RoadId, row.SourceS, row.PreviousSide, leg.Direction, lane, x, y, z));
                }

                var next = routeLegs[(legIndex + 1) % routeLegs.Count];
                if (!roads.TryGetValue(next.RoadId, out var nextRoad))
                    throw new InvalidDataException("Missing outgoing source leg");
                string node = Endpoint(road, leg.Direction, false);
                if (node != Endpoint(nextRoad, next.Direction, true))
                    throw new InvalidDataException("No shared source-node identity; proximity cannot connect roads");
                joinByPair.Add((leg.RoadId, next.RoadId), node);
                joins.Add(DescribeJunction(plan, roads.Values, node, leg.RoadId, next.RoadId));
            }

            var targets = new List<Sample>();
            double arc = 0;
            for (int index = 0; index < controlList.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var start = controlList[index];
                var end = controlList[(index + 1) % controlList.Count];
                string[] members = start.RoadId == end.RoadId ? new[]
                {
                    start.RoadId
                }

                : new[]
                {
                    start.RoadId,
                    end.RoadId
                };
                string node = members.Length == 1 ? "" : joinByPair[(start.RoadId, end.RoadId)];
                double dx = end.X - start.X, dz = end.Z - start.Z;
                double horizontal = Math.Sqrt(dx * dx + dz * dz);
                // A zero-length driver segment retains its source controls above.
                // It creates no fake spatial travel or deletion of source identity.
                if (horizontal == 0)
                {
                    if (start.Y != end.Y)
                        throw new InvalidDataException("Zero-XZ driver control cannot hide a vertical source conflict");
                    continue;
                }

                int intervals = Math.Max(1, (int)Math.Ceiling(horizontal / SamplingStepMetres));
                if (targets.Count + intervals + 1 > MaximumSamples)
                    throw new InvalidDataException("Driver sampling cap exceeded; no shortened loop");
                for (int sampleIndex = 0; sampleIndex < intervals; sampleIndex++)
                {
                    double fraction = (double)sampleIndex / intervals;
                    double x = start.X + dx * fraction, z = start.Z + dz * fraction;
                    double y = Height(plan, members, x, z);
                    if (targets.Count > 0)
                    {
                        var previous = targets[targets.Count - 1];
                        arc += Distance(previous.X, previous.Y, previous.Z, x, y, z);
                    }

                    targets.Add(new Sample { ArcMetres = arc, X = x, Y = y, Z = z, ForwardX = dx / horizontal, ForwardZ = dz / horizontal, NodeId = node, roads = members });
                }
            }

            if (targets.Count < 3)
                throw new InvalidDataException("No physical driver loop");
            var first = targets[0];
            var last = targets[targets.Count - 1];
            arc += Distance(last.X, last.Y, last.Z, first.X, first.Y, first.Z);
            var closing = first.Clone();
            closing.ArcMetres = arc;
            targets.Add(closing);
            return new JoinedLoopRoute(plan, controlList.ToArray(), targets.ToArray(), joins.ToArray());
        }

        /// <summary>
        /// Interpolate a cyclic DRIVER target, not source geometry. Arc may extend
        /// beyond one loop for progress bookkeeping; membership stays local to the
        /// sampled source interval. The actual controller must re-query final Y.
        /// </summary>
        public Sample AtArc(double unwrappedArcMetres)
        {
            if (!Finite(unwrappedArcMetres))
                throw new ArgumentOutOfRangeException(nameof(unwrappedArcMetres));
            double arc = unwrappedArcMetres % LengthMetres;
            if (arc < 0)
                arc += LengthMetres;
            int right = FirstAtOrAfter(arc);
            if (right == 0)
            {
                var first = samples[0].Clone();
                first.ArcMetres = unwrappedArcMetres;
                return first;
            }

            var a = samples[right - 1];
            var b = samples[right];
            double fraction = (arc - a.ArcMetres) / (b.ArcMetres - a.ArcMetres);
            double dx = b.X - a.X, dz = b.Z - a.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            return new Sample
            {
                ArcMetres = unwrappedArcMetres,
                X = a.X + dx * fraction,
                Y = a.Y + (b.Y - a.Y) * fraction,
                Z = a.Z + dz * fraction,
                ForwardX = length > 0 ? dx / length : a.ForwardX,
                ForwardZ = length > 0 ? dz / length : a.ForwardZ,
                NodeId = a.NodeId,
                roads = a.roads.Concat(b.roads).Distinct(StringComparer.Ordinal).ToArray()
            };
        }

        /// <summary>
        /// Actual source IDs within a bounded arc neighbourhood, used for a car's
        /// front/rear contact span through a checked joint. No spatial-nearest-road
        /// lookup or packet-wide alias of unrelated road IDs is introduced.
        /// </summary>
        public string[] CopyContactRoadIds(double arc, double halfSpanMetres)
        {
            if (!Finite(arc) || !Finite(halfSpanMetres) || halfSpanMetres < 0 || halfSpanMetres > 10)
                throw new ArgumentOutOfRangeException(nameof(halfSpanMetres));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (double offset = -halfSpanMetres; offset <= halfSpanMetres; offset += SamplingStepMetres)
                foreach (string id in AtArc(arc + offset).roads)
                    ids.Add(id);
            foreach (string id in AtArc(arc + halfSpanMetres).roads)
                ids.Add(id);
            return ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        // Binary search is bounded by the finite authored sample cap. No full
        // resident-mesh scan or geometry rebuild occurs in a per-frame target query.
        private int FirstAtOrAfter(double arc)
        {
            int left = 0, right = samples.Length - 1;
            while (left < right)
            {
                int middle = (left + right) / 2;
                if (samples[middle].ArcMetres < arc)
                    left = middle + 1;
                else
                    right = middle;
            }

            return left;
        }

        /// <summary>Reviewed six-road caller; reverse uses the actual opposite source directions.</summary>
        public static Leg[] ReviewedPantsideLegs(bool oppositeDirection)
        {
            var result = new[]
            {
                new Leg("59973982-span-4", 1),
                new Leg("59659256-span-15", 1),
                new Leg("59973878-span-0", -1),
                new Leg("59973876-span-4", 1),
                new Leg("59659256-span-19", 1),
                new Leg("59973973-span-0", -1)
            };
            return oppositeDirection ? result.Reverse().Select(l => new Leg(l.RoadId, -l.Direction)).ToArray() : result;
        }

        private static Junction DescribeJunction(PreparedWorldMeshPlan800 plan, IEnumerable<JObject> allRoads, string node, string incoming, string outgoing)
        {
            var arms = allRoads.Where(r => ((string)r["start"] == node || (string)r["end"] == node) && (bool? )r["bridge"] != true && (bool? )r["tunnel"] != true && (int? )r["layer"] == 0).OrderBy(r => (string)r["id"], StringComparer.Ordinal).ToArray();
            var angles = new List<double>();
            foreach (var arm in arms)
            {
                string id = (string)arm["id"];
                var rows = plan.CopySourceRows(id);
                if (rows.Length < 2)
                    throw new InvalidDataException("Mapped junction arm lacks final named frame; cannot invent an arm");
                bool starts = (string)arm["start"] == node;
                double station = starts ? rows[0].SourceS : rows[rows.Length - 1].SourceS;
                var side = starts ? RoadRowSide.Next : RoadRowSide.Previous;
                if (!plan.TrySourceFrame(id, station, side, out var frame) || !frame.HasLongitudinal)
                    throw new InvalidDataException("Actual outward junction tangent unavailable");
                double sign = starts ? 1 : -1;
                double angle = Math.Atan2(sign * frame.DCenterZPerH, sign * frame.DCenterXPerH);
                angles.Add(angle < 0 ? angle + Math.PI * 2 : angle);
            }

            angles.Sort();
            var gaps = new double[angles.Count];
            for (int index = 0; index < angles.Count; index++)
                gaps[index] = ((index + 1 < angles.Count ? angles[index + 1] : angles[0] + Math.PI * 2) - angles[index]) * 180 / Math.PI;
            string description = arms.Length == 3 ? (gaps.Max() >= 150 ? "three-arm, T-like geometry" : "three-arm, Y-like geometry") : arms.Length + " actual source arms";
            return new Junction
            {
                NodeId = node,
                IncomingRoad = incoming,
                OutgoingRoad = outgoing,
                arms = arms.Select(r => (string)r["id"]).ToArray(),
                gaps = gaps,
                GeometryDescription = description
            };
        }

        private static string Endpoint(JObject road, int direction, bool entry)
        {
            string value = (string)road[(direction == 1) == entry ? "start" : "end"];
            if (string.IsNullOrEmpty(value))
                throw new InvalidDataException("Source endpoint ID absent");
            return value;
        }

        private static double Height(PreparedWorldMeshPlan800 plan, string[] members, double x, double z)
        {
            if (!plan.Contains(x, z))
                throw new InvalidDataException("Driver target outside proved scope; no old-world fallback");
            bool found = false;
            double height = 0;
            foreach (string id in members)
            {
                if (!plan.TryRoad(id, x, z, out var hit))
                    continue;
                if (found && Math.Abs(height - hit.Height) > NamedHeightAgreementMetres)
                    throw new InvalidDataException("Named joint surfaces disagree in actual final Y");
                found = true;
                height = hit.Height;
            }

            if (!found)
                throw new InvalidDataException("Driver target has no exact named final pavement");
            return height;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Distance(double x, double y, double z, double xx, double yy, double zz) => Math.Sqrt((xx - x) * (xx - x) + (yy - y) * (yy - y) + (zz - z) * (zz - z));
    }
}
