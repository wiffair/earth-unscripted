using UnityEngine;

namespace RoadRush
{
    public sealed partial class RoutePath
    {
        readonly PreparedScopedSession800 scopedSession800;

        /// <summary>
        /// Reader-only route adapter for the explicitly reviewed corridor. It is
        /// not a replacement canonical route or a full road-network admission.
        /// Constructor is internal and accepts only the private-issued preparation.
        /// </summary>
        internal RoutePath(PreparedScopedSession800 prepared)
        {
            scopedSession800 = prepared ?? throw new System.ArgumentNullException(nameof(prepared));
            const int Samples = 257; // Bounded progress hints; final heights never interpolate this array.
            Points = new Vector3[Samples];
            distances = new float[Samples];
            for (int index = 0; index < Samples; index++)
            {
                distances[index] = prepared.CorridorLength * index / (Samples - 1);
                Points[index] = prepared.Point(distances[index]);
            }
            Region = null;
            IsClosed = false;
        }
    }
}
