using RoadRush.FunnyAnimals.Admission;
using RoadRush.FunnyAnimals.Scheduling800;
using RoadRush.FunnyAnimals.VehicleCaptureProposal;
using GroundEnvelope = RoadRush.FunnyAnimals.Core800Proposal.Envelope;

namespace RoadRush.Scenery800
{
    /// <summary>
    /// Copied provisional first-stage diagnostic, not a world/actor/owner witness or
    /// permission. No native handles, fresh queries or cached exclusion verdicts.
    /// Produced within the existing main-thread call; only the original final owner
    /// commit can attest current source state. Null IDs/bounds are UNKNOWN, not empty.
    /// One bounded record per existing exclusion call; original query caps apply.
    /// </summary>
    public sealed class ExclusionObservation800
    {
        private const int MaximumReasonCharacters = 512; // Diagnostic text policy, not geometry tolerance.
        public readonly double FromX, FromZ, ToX, ToZ, RadiusMetres;
        public string Stage = "QueryBudget";
        public string Detail, WholeGroundLastStoredCandidateReason;
        public int Frame = -1;
        public Verdict ProvisionalVerdict = Verdict.Unknown;
        public Verdict FinalVerdict = Verdict.Unknown;
        // Actual original method out-value, potentially partial on Unknown.
        public GroundEnvelope? GroundEnvelope;
        public LeasedStaticGround800.PointObservation PointObservation;
        // Existing source/Town APIs do not expose individual hits. Do not manufacture
        // an ID or run another scan/owner call merely to fill a diagnostic field.
        public readonly string SourceOriginalId = null;
        public readonly string TownBodyOrColliderId = null;
        public int ActorIndex = -1, ActorModel = -1, SampleIndex = -1;
        public string ActorRuntimeHandle, ActorRole;
        public OccupiedBounds? ActorBounds, QueryBounds;
        public float? SampleX, SampleZ;

        internal ExclusionObservation800(P from, P to, double radius)
        {
            FromX = from.X; FromZ = from.Z; ToX = to.X; ToZ = to.Z;
            RadiusMetres = radius;
        }

        internal static string BoundReason(string copiedReason)
        {
            if (copiedReason == null || copiedReason.Length <= MaximumReasonCharacters) return copiedReason;
            return copiedReason.Substring(0, MaximumReasonCharacters) + "[truncated]";
        }
    }
}
