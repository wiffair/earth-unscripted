using System;

namespace EarthUnscripted.VehiclePopulation
{
    /// <summary>
    /// Exact reference provenance only, not a collider/world capability. The real
    /// new-allocation branch introduces a reference; an existing-name lookup can
    /// only verify a previous reference. A mismatch permanently denies this book.
    /// Pure tests use ordinary objects and make no native geometry claim.
    /// </summary>
    internal sealed class AllocatedWheelReferenceBook
    {
        internal const int WheelCount = 4;
        readonly object[] references = new object[WheelCount];
        internal bool Healthy { get; private set; } = true;

        internal void RecordNewAllocation(int index, object actualReference)
        {
            if (!Healthy || index < 0 || index >= WheelCount || actualReference == null ||
                references[index] != null)
            { Healthy = false; return; }
            references[index] = actualReference;
        }

        internal void VerifyRetained(int index, object actualReference)
        {
            if (!Healthy || index < 0 || index >= WheelCount || actualReference == null ||
                !ReferenceEquals(references[index], actualReference)) Healthy = false;
        }

        internal void ForgetDetached(int index, object previousReference)
        {
            if (!Healthy || index < 0 || index >= WheelCount ||
                !ReferenceEquals(references[index], previousReference))
            { Healthy = false; return; }
            references[index] = null;
        }
    }
}
