using System;
using RoadRush.VehiclePopulation;
using RoadRush.FunnyAnimals.ActualRegistryConsumer;

namespace RoadRush.Scenery800.Verification
{
    /// <summary>
    /// Plain copied row diagnostic, never an authenticated owner/currentness witness.
    /// Records one existing ordered sample; it does not create actors or omit unbound
    /// members. Physical conversion reads immutable numeric fields only. Main-thread
    /// caller must already possess the genuine AfterActorFrame snapshot.
    /// </summary>
    public sealed class ActorRefusalObservation800
    {
        private const int MaximumRows = NativeProviderProbe800.RequiredActualActors;
        private const int MaximumIdentityCharacters = 128;
        private const int MaximumReasonCharacters = 1536;
        public int Index, Model;
        public string Role, RuntimeHandle, OwnerUnavailableReason, PhysicalReason;
        public bool PresentationPresent, PhysicalConverted, IdentityTruncated, ReasonTruncated;

        /// <summary>
        /// Copies at most the existing 28-row fixture limit; over-limit input yields
        /// no misleading prefix and an explicit diagnostic refusal. No IsCurrent,
        /// Time, native getters, owner queries, callbacks or source mutation. A failed
        /// pure conversion remains a row, with no change to actual chain decisions.
        /// Bound: <=28 conversions (<=8 copied source boxes each) per attempted frame,
        /// <=152 attempts from the existing 120 warm +32 job caps. Allocations are
        /// diagnostic-only and are not a performance/admission claim.
        /// </summary>
        internal static ActorRefusalObservation800[] CopyOrderedRows(
            ActualVehiclePopulationSnapshot snapshot, out string diagnosticReason)
        {
            diagnosticReason = "Copied ordered sample observations; not currentness/admission";
            if (snapshot == null || snapshot.Vehicles == null || snapshot.Vehicles.Count > MaximumRows)
            {
                diagnosticReason = "Snapshot unavailable or diagnostic row limit exceeded; no prefix copied";
                return Array.Empty<ActorRefusalObservation800>();
            }
            var rows = new ActorRefusalObservation800[snapshot.Vehicles.Count];
            for (int index = 0; index < rows.Length; index++)
            {
                ActualVehicleSample source = snapshot.Vehicles[index];
                var row = new ActorRefusalObservation800 { Index = index, Model = source?.Model ?? -1 };
                rows[index] = row;
                row.Role = Limit(source?.Role, MaximumIdentityCharacters, out bool roleTruncated);
                row.RuntimeHandle = Limit(source?.RuntimeHandle, MaximumIdentityCharacters, out bool handleTruncated);
                row.IdentityTruncated = roleTruncated || handleTruncated;
                row.PresentationPresent = source?.Presentation != null;
                row.OwnerUnavailableReason = Limit(source?.AppearanceUnavailableReason,
                    MaximumReasonCharacters, out bool ownerReasonTruncated);
                try
                {
                    row.PhysicalConverted = ActualSamplePhysicalConverter.TryConvert(source, out _, out string reason);
                    row.PhysicalReason = Limit(reason, MaximumReasonCharacters, out bool physicalTruncated);
                    row.ReasonTruncated = ownerReasonTruncated || physicalTruncated;
                }
                catch (Exception error)
                {
                    // Observer errors remain visible; never manufacture a bound row.
                    row.PhysicalConverted = false;
                    row.PhysicalReason = "Pure diagnostic conversion exception: " + error.GetType().Name;
                    row.ReasonTruncated = ownerReasonTruncated;
                }
            }
            return rows;
        }

        private static string Limit(string value, int maximumCharacters, out bool truncated)
        {
            truncated = value != null && value.Length > maximumCharacters;
            return truncated ? value.Substring(0, maximumCharacters) + "[truncated]" : value;
        }
    }
}
