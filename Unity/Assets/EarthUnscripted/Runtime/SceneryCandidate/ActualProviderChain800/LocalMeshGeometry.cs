using System;
using System.Collections.Generic;

namespace EarthUnscripted.FunnyAnimals.Core800Proposal
{
    /// <summary>Unknown is refusal/incomplete evidence; Clear is local geometry evidence only.</summary>
    public enum Verdict { Unknown, Blocked, Clear }

    /// <summary>
    /// Legacy source API name retained: planar point in metres, x right/z forward.
    /// Query points are world coordinates until explicitly translated into a packet.
    /// No serialized schema or terrain authority is attached to this mutable value type.
    /// </summary>
    public struct P
    {
        public double X;
        public double Z;
        public P(double x, double z) { X = x; Z = z; }
    }

    /// <summary>
    /// Legacy source API name retained: packet-local point in metres, y up.
    /// Coordinates come from original uploaded local-f32 vertices, promoted to double.
    /// </summary>
    public struct D3
    {
        public double X;
        public double Y;
        public double Z;
        public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// Mutable, caller-owned shared work budget. The consumer resets once per real
    /// Unity frame. Counts are logical operations, not native physics rays or total
    /// allocations. Not thread-safe; sharing does not implement actor fairness.
    /// </summary>
    public sealed class QueryBudget
    {
        // Reviewed proposal policy, not measured world capacity or a per-actor allowance.
        public const int DefaultQueries = 64;
        public const int DefaultReferences = 32768;
        public const int DefaultFaces = 8192;
        public const int DefaultHeightCalls = 16;
        public const int DefaultFootprintCalls = 64;

        public int Queries = DefaultQueries;
        public int References = DefaultReferences;
        public int Faces = DefaultFaces;
        public int HeightCalls = DefaultHeightCalls;
        public int FootprintCalls = DefaultFootprintCalls;

        public void Reset()
        {
            Queries = DefaultQueries;
            References = DefaultReferences;
            Faces = DefaultFaces;
            HeightCalls = DefaultHeightCalls;
            FootprintCalls = DefaultFootprintCalls;
        }

        /// <summary>Charges one unit before work; exhausted/negative counters refuse.</summary>
        public bool Use(ref int remaining)
        {
            if (remaining <= 0) return false;
            remaining--;
            return true;
        }
    }

    /// <summary>
    /// Derived witness from intersecting faces, never a new ground-height authority.
    /// Heights are world metres, normal is dimensionless y/up dot, inverse error metres.
    /// Only a Clear return makes extrema meaningful; output may be partial on refusal.
    /// </summary>
    public struct Envelope
    {
        public double MinimumY;
        public double MaximumY;
        public double MinimumNormalY;
        public int IntersectingFaces;
        public bool Bank;
        public bool Road;
        public double MaximumInverseInputError;
    }

    /// <summary>
    /// Single-threaded, original local-mesh spatial index for animal walking witnesses.
    /// Stores copied LOCAL-f32 mesh buffers plus original exact double translation;
    /// never rounds vertices into world-f32, regrids, drops faces or changes topology.
    /// Complete-domain/active-upload authority MUST come from the actual typed owner.
    /// Clear here does not prove coverage, metadata safety, ownership or gameplay admission.
    /// Reused clipping arrays/stamps forbid concurrent queries or mutation during Inspect.
    /// </summary>
    public sealed class LocalMeshIndex
    {
        // Existing public policy names retained for source compatibility. Cell is metres;
        // WalkingNormal is the original approximation near cos(25 degrees), retained unchanged;
        // Margin is an outward metric geometry allowance, NOT native transform proof.
        public const double Cell = 8;
        public const double WalkingNormal = 0.9063001;
        public const double Margin = 0.000001;

        // Separate Core800 proposal limits. Actual pinned inventory is1063 packets,
        //317003 vertices/533219 triangles/689595 references/10201 cells. Not old32 caps.
        public const int MaximumPackets = 2048;
        public const int MaximumVertices = 500000;
        public const int MaximumTriangles = 1000000;
        public const int MaximumReferences = 1000000;
        public const int MaximumCells = 10201;
        public const int MaximumCellReferences = 4096;

        private const int MaximumPacketVertices = 24000;
        private const int MaximumPacketTriangles = 48000;
        private const double MaximumOriginXZMetres = 448;
        private const double DomainHalfSizeMetres = 400;
        private const double MaximumAbsoluteWorldHeightMetres = 20000;
        private const double MinimumProjectedDoubleAreaSquareMetres = 1e-12;
        private const double OrientationToleranceSquareMetres = 1e-10;
        // Ratio grade limits from the typed final source policy plus its existing1e-6 allowance.
        private const double RoadGradeLimit = 0.180001;
        private const double BankGradeLimit = 2.000001;
        private const double TerrainGradeLimit = 1.200001;
        private const double MaximumRadiusMetres = 1.5;
        private const double MaximumCorridorLengthSquaredMetres = 64;
        private const int MaximumQueryCells = 9;
        private const double MaximumInverseInputErrorMetres = 0.00005;
        private const int MaximumClipVertices = 12;

        private sealed class Packet
        {
            public string Id;
            public double OriginX, OriginY, OriginZ; // original exact translation in metres
            public float[] Vertices;
            public int[] Indices;
            public bool Road;
            public bool[] Banks;
            public int[] Stamps;
        }

        private struct FaceReference
        {
            public int PacketIndex;
            public int TriangleIndex;
            public FaceReference(int packetIndex, int triangleIndex) { PacketIndex = packetIndex; TriangleIndex = triangleIndex; }
        }

        private readonly List<Packet> packets = new List<Packet>();
        private readonly HashSet<string> packetIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<(int, int), List<FaceReference>> cells = new Dictionary<(int, int), List<FaceReference>>();
        private readonly D3[] clipA = new D3[MaximumClipVertices];
        private readonly D3[] clipB = new D3[MaximumClipVertices];
        private int stamp;
        private bool failed;
        private bool revoked;

        public int PacketCount => packets.Count;
        public int VertexCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int ReferenceCount { get; private set; }
        public int CellCount => cells.Count;
        /// <summary>Owned array payload only; excludes object/dictionary/list allocation.</summary>
        public long OwnedArrayPayloadBytes { get; private set; }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void Need(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
        private static int CellKey(double coordinate) => (int)Math.Floor(coordinate / Cell);

        /// <summary>
        /// Lazily copies and validates one actual packet. Caller owns original arrays;
        /// index owns clones. Each yielded0 is a staged work unit, not constant wall time.
        /// A suspended/invalid packet leaves failed=true, so no valid prefix can publish.
        /// Refuse is sticky even if this iterator is later drained (Street regression).
        /// Original projected area must be positive; no face repair/drop is permitted.
        /// Requires exclusive access; throws on invalid source/caps and never issues coverage.
        /// </summary>
        public IEnumerable<int> AddPacket(string id, double originX, double originY, double originZ,
            float[] vertices, int[] indices, bool road, bool[] banks)
        {
            Need(!failed && !revoked, "Index already refused");
            failed = true;
            Need(packets.Count < MaximumPackets && packetIds.Add(id) && !string.IsNullOrEmpty(id), "Packet count/id");
            Need(Math.Abs(originX) <= MaximumOriginXZMetres && Math.Abs(originZ) <= MaximumOriginXZMetres
                && Finite(originX) && Finite(originY) && Finite(originZ)
                && (double)(float)originX == originX && (double)(float)originY == originY
                && (double)(float)originZ == originZ, "Exact finite original origin");
            Need(vertices != null && vertices.Length >= 9 && vertices.Length % 3 == 0
                && vertices.Length / 3 <= MaximumPacketVertices && indices != null && indices.Length >= 3
                && indices.Length % 3 == 0 && indices.Length / 3 <= MaximumPacketTriangles
                && banks != null && banks.Length == indices.Length / 3, "Per-packet buffers");
            Need(VertexCount + vertices.Length / 3 <= MaximumVertices
                && TriangleCount + indices.Length / 3 <= MaximumTriangles, "Aggregate buffers");

            var packet = new Packet
            {
                Id = id, OriginX = originX, OriginY = originY, OriginZ = originZ,
                Vertices = (float[])vertices.Clone(), Indices = (int[])indices.Clone(),
                Road = road, Banks = (bool[])banks.Clone(), Stamps = new int[indices.Length / 3]
            };
            OwnedArrayPayloadBytes += (long)packet.Vertices.Length * 4 + (long)packet.Indices.Length * 4
                + packet.Banks.Length + (long)packet.Stamps.Length * 4;
            int packetIndex = packets.Count;
            packets.Add(packet);
            VertexCount += vertices.Length / 3;
            TriangleCount += indices.Length / 3;
            for (int vertexOffset = 0; vertexOffset < packet.Vertices.Length; vertexOffset += 3)
            {
                Need(Finite(packet.Vertices[vertexOffset]) && Finite(packet.Vertices[vertexOffset + 1])
                    && Finite(packet.Vertices[vertexOffset + 2]), "Finite local vertex");
                double worldX = originX + packet.Vertices[vertexOffset];
                double worldZ = originZ + packet.Vertices[vertexOffset + 2];
                Need(worldX >= -DomainHalfSizeMetres && worldX <= DomainHalfSizeMetres
                    && worldZ >= -DomainHalfSizeMetres && worldZ <= DomainHalfSizeMetres
                    && Math.Abs(originY + packet.Vertices[vertexOffset + 1]) < MaximumAbsoluteWorldHeightMetres,
                    "Actual800 domain");
                yield return 0;
            }
            for (int triangle = 0; triangle < packet.Indices.Length / 3; triangle++)
            {
                for (int corner = 0; corner < 3; corner++)
                    Need(packet.Indices[triangle * 3 + corner] >= 0
                        && packet.Indices[triangle * 3 + corner] < packet.Vertices.Length / 3, "Index range");
                D3 first = ReadLocalVertex(packet, packet.Indices[triangle * 3]);
                D3 second = ReadLocalVertex(packet, packet.Indices[triangle * 3 + 1]);
                D3 third = ReadLocalVertex(packet, packet.Indices[triangle * 3 + 2]);
                double projectedArea = (second.Z - first.Z) * (third.X - first.X)
                    - (second.X - first.X) * (third.Z - first.Z);
                Need(Finite(projectedArea) && projectedArea > MinimumProjectedDoubleAreaSquareMetres,
                    "Actual LOCAL face upward/nondegenerate; never drop");
                double edgeUX = second.X - first.X, edgeUY = second.Y - first.Y, edgeUZ = second.Z - first.Z;
                double edgeVX = third.X - first.X, edgeVY = third.Y - first.Y, edgeVZ = third.Z - first.Z;
                double normalX = edgeUY * edgeVZ - edgeUZ * edgeVY;
                double normalZ = edgeUX * edgeVY - edgeUY * edgeVX;
                double grade = Math.Sqrt(normalX * normalX + normalZ * normalZ) / projectedArea;
                Need(Finite(grade) && grade <= (road ? RoadGradeLimit : packet.Banks[triangle] ? BankGradeLimit : TerrainGradeLimit),
                    "Typed role grade policy; no flatten/drop");
                int minimumCellX = CellKey(originX + Math.Min(first.X, Math.Min(second.X, third.X)));
                int maximumCellX = CellKey(originX + Math.Max(first.X, Math.Max(second.X, third.X)));
                int minimumCellZ = CellKey(originZ + Math.Min(first.Z, Math.Min(second.Z, third.Z)));
                int maximumCellZ = CellKey(originZ + Math.Max(first.Z, Math.Max(second.Z, third.Z)));
                long referenceCount = ((long)maximumCellX - minimumCellX + 1) * ((long)maximumCellZ - minimumCellZ + 1);
                Need(referenceCount > 0 && referenceCount <= MaximumReferences - ReferenceCount, "Reviewed index reference budget");
                for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ++)
                for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
                {
                    if (!cells.TryGetValue((cellX, cellZ), out List<FaceReference> references))
                    {
                        Need(cells.Count < MaximumCells, "Index cell budget");
                        cells.Add((cellX, cellZ), references = new List<FaceReference>());
                    }
                    Need(references.Count < MaximumCellReferences, "Dense index cell budget");
                    references.Add(new FaceReference(packetIndex, triangle));
                    ReferenceCount++;
                    yield return 0;
                }
            }
            if (!revoked) failed = false;
        }

        /// <summary>Irreversibly refuses this index, including already suspended builders.</summary>
        public void Refuse() { revoked = true; failed = true; }

        private static D3 ReadLocalVertex(Packet packet, int vertex)
        {
            vertex *= 3;
            return new D3(packet.Vertices[vertex], packet.Vertices[vertex + 1], packet.Vertices[vertex + 2]);
        }

        private static double SignedCrossXZ(P first, P second, P point) =>
            (second.X - first.X) * (point.Z - first.Z) - (second.Z - first.Z) * (point.X - first.X);

        // Uploaded positive-y faces are clockwise when projected onto x/z. Boundary
        // uncertainty is included conservatively; do not reverse this test to generic CCW.
        private static bool PointInsideProjectedTriangle(P point, D3 first, D3 second, D3 third)
        {
            var firstXZ = new P(first.X, first.Z);
            var secondXZ = new P(second.X, second.Z);
            var thirdXZ = new P(third.X, third.Z);
            return SignedCrossXZ(firstXZ, secondXZ, point) <= OrientationToleranceSquareMetres
                && SignedCrossXZ(secondXZ, thirdXZ, point) <= OrientationToleranceSquareMetres
                && SignedCrossXZ(thirdXZ, firstXZ, point) <= OrientationToleranceSquareMetres;
        }

        private static double PointSegmentDistanceSquared(P point, P from, P to)
        {
            double deltaX = to.X - from.X, deltaZ = to.Z - from.Z;
            double lengthSquared = deltaX * deltaX + deltaZ * deltaZ;
            if (lengthSquared == 0)
                return (point.X - from.X) * (point.X - from.X) + (point.Z - from.Z) * (point.Z - from.Z);
            double fraction = Math.Max(0, Math.Min(1,
                ((point.X - from.X) * deltaX + (point.Z - from.Z) * deltaZ) / lengthSquared));
            deltaX = point.X - from.X - fraction * deltaX;
            deltaZ = point.Z - from.Z - fraction * deltaZ;
            return deltaX * deltaX + deltaZ * deltaZ;
        }

        private static bool PointOnSegment(P point, P from, P to) =>
            Math.Abs(SignedCrossXZ(from, to, point)) <= OrientationToleranceSquareMetres
            && point.X >= Math.Min(from.X, to.X) - Margin && point.X <= Math.Max(from.X, to.X) + Margin
            && point.Z >= Math.Min(from.Z, to.Z) - Margin && point.Z <= Math.Max(from.Z, to.Z) + Margin;

        private static double SegmentDistanceSquared(P firstFrom, P firstTo, P secondFrom, P secondTo)
        {
            double firstSide = SignedCrossXZ(firstFrom, firstTo, secondFrom);
            double secondSide = SignedCrossXZ(firstFrom, firstTo, secondTo);
            double thirdSide = SignedCrossXZ(secondFrom, secondTo, firstFrom);
            double fourthSide = SignedCrossXZ(secondFrom, secondTo, firstTo);
            if ((firstSide > 0 && secondSide < 0 || firstSide < 0 && secondSide > 0)
                && (thirdSide > 0 && fourthSide < 0 || thirdSide < 0 && fourthSide > 0)
                || PointOnSegment(secondFrom, firstFrom, firstTo) || PointOnSegment(secondTo, firstFrom, firstTo)
                || PointOnSegment(firstFrom, secondFrom, secondTo) || PointOnSegment(firstTo, secondFrom, secondTo)) return 0;
            return Math.Min(Math.Min(PointSegmentDistanceSquared(firstFrom, secondFrom, secondTo), PointSegmentDistanceSquared(firstTo, secondFrom, secondTo)),
                Math.Min(PointSegmentDistanceSquared(secondFrom, firstFrom, firstTo), PointSegmentDistanceSquared(secondTo, firstFrom, firstTo)));
        }

        /// <summary>
        /// Conservative projected triangle/capsule intersection; coordinates are in ONE
        /// packet-local metric frame. Includes containment in both directions and touch.
        /// Assumes original upward clockwise triangles; no full-world coverage is proved.
        /// Caller supplies inverse input uncertainty through radius; Margin is added here.
        /// </summary>
        public static bool Intersects(D3 first, D3 second, D3 third, P from, P to, double radius)
        {
            if (PointInsideProjectedTriangle(from, first, second, third) || PointInsideProjectedTriangle(to, first, second, third)) return true;
            double radiusSquared = (radius + Margin) * (radius + Margin);
            var firstXZ = new P(first.X, first.Z);
            var secondXZ = new P(second.X, second.Z);
            var thirdXZ = new P(third.X, third.Z);
            return SegmentDistanceSquared(from, to, firstXZ, secondXZ) <= radiusSquared
                || SegmentDistanceSquared(from, to, secondXZ, thirdXZ) <= radiusSquared
                || SegmentDistanceSquared(from, to, thirdXZ, firstXZ) <= radiusSquared;
        }

        // Sutherland-Hodgman clipping against one axis-aligned half-plane. Interpolate
        // local y along the original edge; this is an extremum witness, never a heightfield.
        private static int ClipAgainstAxisPlane(D3[] input, int count, D3[] output, int axis, double boundary, bool above)
        {
            if (count == 0) return 0;
            int outputCount = 0;
            D3 previous = input[count - 1];
            double previousSide = (axis == 0 ? previous.X : previous.Z) - boundary;
            bool previousInside = above ? previousSide >= 0 : previousSide <= 0;
            for (int index = 0; index < count; index++)
            {
                D3 next = input[index];
                double nextSide = (axis == 0 ? next.X : next.Z) - boundary;
                bool nextInside = above ? nextSide >= 0 : nextSide <= 0;
                if (previousInside != nextInside)
                {
                    double fraction = previousSide / (previousSide - nextSide);
                    Need(Finite(fraction) && fraction >= 0 && fraction <= 1 && outputCount < MaximumClipVertices,
                        "Clip arithmetic/cap");
                    output[outputCount++] = new D3(previous.X + (next.X - previous.X) * fraction,
                        previous.Y + (next.Y - previous.Y) * fraction, previous.Z + (next.Z - previous.Z) * fraction);
                }
                if (nextInside)
                {
                    Need(outputCount < MaximumClipVertices, "Clip cap");
                    output[outputCount++] = next;
                }
                previous = next;
                previousSide = nextSide;
                previousInside = nextInside;
            }
            return outputCount;
        }

        /// <summary>
        /// Inspect all original faces intersecting a finite world-XZ capsule, radius
        /// (0,1.5]m and centreline<=8m, inside the reviewed800m square. Budget is required,
        /// mutable and shared. Missing coverage is NOT inferred safe from this method:
        /// the actual typed lease must first certify the complete enclosing footprint.
        /// Returns Blocked on road/bank/steep faces, Unknown on caps/arithmetic/refusal,
        /// Clear only for complete intersecting-face geometry witnesses in this index.
        /// No allocation occurs on the measured warm pure path; no native-cost claim.
        /// </summary>
        public Verdict Inspect(P from, P to, double radius, QueryBudget budget,
            out Envelope envelope, out string reason)
        {
            envelope = new Envelope
            {
                MinimumY = double.PositiveInfinity,
                MaximumY = double.NegativeInfinity,
                MinimumNormalY = 1
            };
            reason = "Unknown";
            if (failed || revoked || !budget.Use(ref budget.Queries)
                || !Finite(from.X) || !Finite(from.Z) || !Finite(to.X) || !Finite(to.Z)
                || !Finite(radius) || radius <= 0 || radius > MaximumRadiusMetres) return Verdict.Unknown;
            double deltaX = to.X - from.X, deltaZ = to.Z - from.Z;
            if (deltaX * deltaX + deltaZ * deltaZ > MaximumCorridorLengthSquaredMetres
                || Math.Min(from.X, to.X) - radius < -DomainHalfSizeMetres
                || Math.Max(from.X, to.X) + radius > DomainHalfSizeMetres
                || Math.Min(from.Z, to.Z) - radius < -DomainHalfSizeMetres
                || Math.Max(from.Z, to.Z) + radius > DomainHalfSizeMetres) return Verdict.Unknown;
            if (stamp == int.MaxValue)
            {
                reason = "Stamp lifetime requires rebuild";
                return Verdict.Unknown;
            }
            stamp++;

            // Broaden the INDEX domain as well as face tests. Otherwise a thin raised
            // face in the uncertainty ring could disappear before extrema clipping.
            int minimumCellX = CellKey(Math.Min(from.X, to.X) - radius - MaximumInverseInputErrorMetres - Margin);
            int maximumCellX = CellKey(Math.Max(from.X, to.X) + radius + MaximumInverseInputErrorMetres + Margin);
            int minimumCellZ = CellKey(Math.Min(from.Z, to.Z) - radius - MaximumInverseInputErrorMetres - Margin);
            int maximumCellZ = CellKey(Math.Max(from.Z, to.Z) + radius + MaximumInverseInputErrorMetres + Margin);
            if ((maximumCellX - minimumCellX + 1) * (maximumCellZ - minimumCellZ + 1) > MaximumQueryCells)
                return Verdict.Unknown;
            try
            {
                for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ++)
                for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
                if (cells.TryGetValue((cellX, cellZ), out List<FaceReference> references))
                foreach (FaceReference reference in references)
                {
                    if (!budget.Use(ref budget.References)) return Verdict.Unknown;
                    Packet packet = packets[reference.PacketIndex];
                    if (packet.Stamps[reference.TriangleIndex] == stamp) continue;
                    packet.Stamps[reference.TriangleIndex] = stamp;
                    if (!budget.Use(ref budget.Faces)) return Verdict.Unknown;
                    D3 first = ReadLocalVertex(packet, packet.Indices[reference.TriangleIndex * 3]);
                    D3 second = ReadLocalVertex(packet, packet.Indices[reference.TriangleIndex * 3 + 1]);
                    D3 third = ReadLocalVertex(packet, packet.Indices[reference.TriangleIndex * 3 + 2]);
                    var localFrom = new P(from.X - packet.OriginX, from.Z - packet.OriginZ);
                    var localTo = new P(to.X - packet.OriginX, to.Z - packet.OriginZ);

                    // Compare exact-double subtraction with the float-input/float-origin
                    // inverse query path. This uncertainty is not a replacement for Root's
                    // actual native uploaded-buffer/transform agreement contract.
                    double fromError = Math.Sqrt(
                        Math.Pow((double)((float)from.X - (float)packet.OriginX) - localFrom.X, 2)
                        + Math.Pow((double)((float)from.Z - (float)packet.OriginZ) - localFrom.Z, 2));
                    double toError = Math.Sqrt(
                        Math.Pow((double)((float)to.X - (float)packet.OriginX) - localTo.X, 2)
                        + Math.Pow((double)((float)to.Z - (float)packet.OriginZ) - localTo.Z, 2));
                    double uncertainty = Math.Max(fromError, toError);
                    if (uncertainty > MaximumInverseInputErrorMetres)
                    {
                        reason = "Inverse query coordinate uncertainty";
                        return Verdict.Unknown;
                    }
                    if (!Intersects(first, second, third, localFrom, localTo, radius + uncertainty)) continue;
                    envelope.MaximumInverseInputError = Math.Max(envelope.MaximumInverseInputError, uncertainty);
                    envelope.IntersectingFaces++;
                    if (packet.Banks[reference.TriangleIndex] || packet.Road)
                    {
                        envelope.Bank |= packet.Banks[reference.TriangleIndex];
                        envelope.Road |= packet.Road;
                        reason = packet.Banks[reference.TriangleIndex] ? "Whole-footprint derived bank" : "Whole-footprint road";
                        return Verdict.Blocked;
                    }
                    double edgeUX = second.X - first.X, edgeUY = second.Y - first.Y, edgeUZ = second.Z - first.Z;
                    double edgeVX = third.X - first.X, edgeVY = third.Y - first.Y, edgeVZ = third.Z - first.Z;
                    double normalX = edgeUY * edgeVZ - edgeUZ * edgeVY;
                    double normalY = edgeUZ * edgeVX - edgeUX * edgeVZ;
                    double normalZ = edgeUX * edgeVY - edgeUY * edgeVX;
                    double normal = normalY / Math.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
                    if (!(normal > WalkingNormal))
                    {
                        reason = "Actual local face slope";
                        return Verdict.Blocked;
                    }
                    envelope.MinimumNormalY = Math.Min(envelope.MinimumNormalY, normal);

                    // ClipAgainstAxisPlane to the conservative capsule AABB, using EXACTLY the same
                    // uncertainty-expanded radius as intersection. Omitting uncertainty
                    // here missed Street's0.4m raised sliver (retained regression test).
                    double clipRadius = radius + uncertainty + Margin;
                    clipA[0] = first;
                    clipA[1] = second;
                    clipA[2] = third;
                    int clipCount = ClipAgainstAxisPlane(clipA, 3, clipB, 0, Math.Min(localFrom.X, localTo.X) - clipRadius, true);
                    clipCount = ClipAgainstAxisPlane(clipB, clipCount, clipA, 0, Math.Max(localFrom.X, localTo.X) + clipRadius, false);
                    clipCount = ClipAgainstAxisPlane(clipA, clipCount, clipB, 1, Math.Min(localFrom.Z, localTo.Z) - clipRadius, true);
                    clipCount = ClipAgainstAxisPlane(clipB, clipCount, clipA, 1, Math.Max(localFrom.Z, localTo.Z) + clipRadius, false);
                    for (int index = 0; index < clipCount; index++)
                    {
                        envelope.MinimumY = Math.Min(envelope.MinimumY, packet.OriginY + clipA[index].Y - Margin);
                        envelope.MaximumY = Math.Max(envelope.MaximumY, packet.OriginY + clipA[index].Y + Margin);
                    }
                }
                if (envelope.IntersectingFaces == 0 || !Finite(envelope.MinimumY) || !Finite(envelope.MaximumY))
                {
                    reason = "No support witness";
                    return Verdict.Unknown;
                }
                reason = "All intersecting local faces inspected; coverage needs typed owner";
                return Verdict.Clear;
            }
            catch (Exception exception)
            {
                reason = "Unknown geometry arithmetic: " + exception.Message;
                return Verdict.Unknown;
            }
        }
    }
}
