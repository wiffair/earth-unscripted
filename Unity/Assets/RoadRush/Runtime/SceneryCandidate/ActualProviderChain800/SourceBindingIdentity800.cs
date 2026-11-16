using System;

namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Compares the identities of an existing and requested source preparation.
    /// This is a pure equality guard: it neither validates nor issues any object,
    /// lease, source capability, world generation or gameplay authority.
    /// </summary>
    public static class SourceBindingIdentity800
    {
        /// <summary>
        /// Returns true only when all three non-null existing references match
        /// the requested references. Exact identity matters even when digests or
        /// numeric generations happen to be equal across different owners.
        /// </summary>
        /// <remarks>
        /// SourceHazards800 supplies its actual typed ground/source/input handles.
        /// Tests may exercise opaque reference equality without fabricating those
        /// capabilities. Safe to call before engine access, cancellation or work.
        /// </remarks>
        public static bool SameBinding(
            object existingGround,
            object existingCanonicalSource,
            object existingReviewedInput,
            object requestedGround,
            object requestedCanonicalSource,
            object requestedReviewedInput)
        {
            return existingGround != null
                && existingCanonicalSource != null
                && existingReviewedInput != null
                && ReferenceEquals(existingGround, requestedGround)
                && ReferenceEquals(existingCanonicalSource, requestedCanonicalSource)
                && ReferenceEquals(existingReviewedInput, requestedReviewedInput);
        }
    }
}
