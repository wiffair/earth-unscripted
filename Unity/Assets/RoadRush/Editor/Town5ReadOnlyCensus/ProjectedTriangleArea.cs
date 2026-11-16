using System;

namespace RoadRush.VisualDiagnosticsProposal
{
    /// <summary>Copied2D pixel point, solely for diagnostic projected-area ranking.</summary>
    internal readonly struct PixelPoint
    {
        internal readonly double X, Y;
        internal PixelPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// Clips a projected triangle to viewport bounds and returns unsigned pixel² area.
    /// Counts occluded/back-facing overlaps too: NEVER visible coverage or authority.
    /// Caller owns two8point buffers, reused without per-triangle allocations. Triangle
    /// must already be wholly in front of the near plane; partial near clipping is
    /// explicitly excluded/counted by the caller, not silently treated as complete.
    /// </summary>
    internal static class ProjectedTriangleArea
    {
        internal const int ClipBufferPoints = 8;
        private const double MaximumProjectedCoordinatePixels = 1e12; // diagnostic numeric refusal, never a clamp

        internal static double Clipped(PixelPoint first, PixelPoint second, PixelPoint third,
            double widthPixels, double heightPixels, PixelPoint[] bufferA, PixelPoint[] bufferB)
        {
            if (bufferA.Length < ClipBufferPoints || bufferB.Length < ClipBufferPoints
                || ReferenceEquals(bufferA, bufferB) || !Valid(first) || !Valid(second) || !Valid(third)
                || !Finite(widthPixels) || !Finite(heightPixels) || widthPixels <= 0 || heightPixels <= 0)
                throw new ArgumentException("Positive viewport and8point buffers required");
            bufferA[0] = first; bufferA[1] = second; bufferA[2] = third;
            int count = 3;
            PixelPoint[] input = bufferA, output = bufferB;
            for (int plane = 0; plane < 4 && count > 0; plane++)
            {
                int written = 0;
                PixelPoint previous = input[count - 1];
                double previousDistance = Distance(previous, plane, widthPixels, heightPixels);
                for (int index = 0; index < count; index++)
                {
                    PixelPoint current = input[index];
                    double currentDistance = Distance(current, plane, widthPixels, heightPixels);
                    if ((previousDistance < 0 && currentDistance > 0) || (previousDistance > 0 && currentDistance < 0))
                    {
                        double fraction = previousDistance / (previousDistance - currentDistance);
                        output[written++] = new PixelPoint(previous.X + fraction * (current.X - previous.X),
                            previous.Y + fraction * (current.Y - previous.Y));
                    }
                    if (currentDistance >= 0) output[written++] = current;
                    previous = current; previousDistance = currentDistance;
                }
                PixelPoint[] swap = input; input = output; output = swap;
                count = written;
            }
            double doubledArea = 0;
            for (int index = 0; index < count; index++)
            {
                PixelPoint current = input[index], next = input[(index + 1) % count];
                doubledArea += current.X * next.Y - current.Y * next.X;
            }
            return Math.Abs(doubledArea) * 0.5;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Valid(PixelPoint point) => Finite(point.X) && Finite(point.Y)
            && Math.Abs(point.X) <= MaximumProjectedCoordinatePixels && Math.Abs(point.Y) <= MaximumProjectedCoordinatePixels;

        private static double Distance(PixelPoint point, int plane, double width, double height) =>
            plane == 0 ? point.X : plane == 1 ? width - point.X : plane == 2 ? point.Y : height - point.Y;
    }
}
