using System.Diagnostics;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // Loading can spend a larger, bounded slice on geometry while no car is
        // driving. Share that allowance across chunk rows, publication and
        // collider attachment: yielding after every small attach made cached
        // starts wait thousands of frames. Once Ready, retain the existing
        // 2 ms driving policy and atomic collider-before-activation boundary.
        const double InitialGeometryFrameBudgetMilliseconds = 12;
        readonly Stopwatch initialGeometrySlice = new Stopwatch();
        int initialGeometryFrame = -1;

        void BeginInitialGeometrySlice()
        {
            if (ready || initialGeometryFrame == Time.frameCount) return;
            initialGeometryFrame = Time.frameCount;
            initialGeometrySlice.Restart();
        }

        bool GeometrySliceExpired(double currentWorkMilliseconds)
        {
            if (ready) return currentWorkMilliseconds > SliceMilliseconds;
            BeginInitialGeometrySlice();
            return initialGeometrySlice.Elapsed.TotalMilliseconds >= InitialGeometryFrameBudgetMilliseconds;
        }
    }
}
