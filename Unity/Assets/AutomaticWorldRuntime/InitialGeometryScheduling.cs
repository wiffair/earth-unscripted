using System.Diagnostics;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // Loading can spend a larger, bounded slice on geometry while no car is
        // driving. Share that allowance across chunk rows, publication and
        // collider attachment: yielding after every small attach wastes frames.
        // Driving shares a 2 ms frame allowance too, rather than waiting a
        // whole frame for each cheap attachment. Publication remains atomic.
        const double InitialGeometryFrameBudgetMilliseconds = 12;
        readonly Stopwatch initialGeometrySlice = new Stopwatch();
        int initialGeometryFrame = -1;

        void BeginInitialGeometrySlice()
        {
            if (initialGeometryFrame == Time.frameCount) return;
            initialGeometryFrame = Time.frameCount;
            initialGeometrySlice.Restart();
        }

        bool GeometrySliceExpired(double currentWorkMilliseconds)
        {
            BeginInitialGeometrySlice();
            double budget=ready?SliceMilliseconds:InitialGeometryFrameBudgetMilliseconds;
            return currentWorkMilliseconds>budget || initialGeometrySlice.Elapsed.TotalMilliseconds>=budget;
        }
    }
}
