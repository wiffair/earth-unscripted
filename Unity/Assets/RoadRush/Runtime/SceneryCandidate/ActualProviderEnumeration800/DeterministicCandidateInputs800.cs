using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using RoadRush.FunnyAnimals.Admission;
using RoadRush.FunnyAnimals.Semantics800Proposal;

namespace RoadRush.Scenery800.CandidateEnumeration
{
    /// <summary>Versioned DATA-only enumeration. It never reads terrain heights,
    /// Unity objects, owners, actors or native support. Source filter results only
    /// select an UNBOUND shortlist; every live predicate and commit must run again.</summary>
    public static class DeterministicCandidateInputs800
    {
        public const string Algorithm = "DETERMINISTIC_GRID8_ORIGIN0_V1";
        public const string ExpectedRawDigest = "fd3b6201584eac1e3303b1392c21896a2a0c76774149f389cc3ace17022803cb";
        public const string ExpectedRoadDigest = "f16323b89e4818e6ee57e6847a1798e742b47195a311fb3facd3a8682c8ed62d";
        public const string ExpectedScopeDigest = "9955e93cf991f88ebc3079064de2bc7db85fe82724b746f821b6c01e66b5ef55";
        public const string ExpectedCookedFinalDigest = "04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf";
        public const int GridStepMetres = 8;
        public const int FirstGridIndex = -49;
        public const int LastGridIndex = 49;
        public const int GridAxisCount = 99;
        public const int GridAnchorCount = GridAxisCount * GridAxisCount;
        public const int MaximumProfiles = GridAnchorCount * 2;
        public const int MaximumRetainedPerSpecies = 256;
        public const int MaximumRetainedBundles = MaximumRetainedPerSpecies * 2;
        public const int ForwardDistanceMetres = 2;
        public const double RadiusMetres = (double)0.85f;

        public readonly struct Request
        {
            public readonly string Label;
            public readonly float FromX, FromZ, ToX, ToZ;
            internal Request(string label, float ax, float az, float bx, float bz)
            { Label = label; FromX = ax; FromZ = az; ToX = bx; ToZ = bz; }
        }
        public sealed class Candidate
        {
            public string Id { get; }
            public int AnchorOrdinal { get; }
            public int Kind { get; }
            public AreaKind800 Area { get; }
            public int XMetres { get; }
            public int ZMetres { get; }
            public ReadOnlyCollection<Request> Requests { get; }
            internal Candidate(int ordinal, int kind, int x, int z)
            {
                AnchorOrdinal = ordinal; Kind = kind; XMetres = x; ZMetres = z;
                Area = kind == 1 ? AreaKind800.GrassAgriculture : AreaKind800.Woodland;
                Id = Algorithm + "/kind" + kind + "/anchor" + ordinal;
                Requests = Array.AsReadOnly(new[] {
                    new Request("home-disc",x,z,x,z),
                    new Request("forward2m",x,z,x+ForwardDistanceMetres,z),
                    new Request("reverse2m",x+ForwardDistanceMetres,z,x,z) });
            }
        }
        public readonly struct SourceFacts
        {
            public readonly Verdict Raster, Hazards;
            public readonly bool AreaKnown, WholeAreaInside;
            public SourceFacts(Verdict raster, bool areaKnown, bool wholeInside, Verdict hazards)
            { Raster = raster; AreaKnown = areaKnown; WholeAreaInside = wholeInside; Hazards = hazards; }
        }
        /// <summary>Must use only a private managed actual-source clone. Test filters
        /// may exercise ordering, but never become a native/owner acceptance witness.</summary>
        public delegate SourceFacts ManagedSourceFilter(Request request, int kind, AreaKind800 area);

        public sealed class Inputs
        {
            public string AlgorithmVersion => Algorithm;
            public bool GameplayAdmission => false;
            public string Availability => "UNBOUND_DATA_ONLY";
            public bool FullGridScanCompleted { get; internal set; }
            public int ProfilesScanned { get; internal set; }
            public int SourceRequestsEvaluated { get; internal set; }
            public int SheepSourceEligible { get; internal set; }
            public int DeerSourceEligible { get; internal set; }
            public int SheepOmittedByRetentionCap { get; internal set; }
            public int DeerOmittedByRetentionCap { get; internal set; }
            public ReadOnlyCollection<Candidate> Candidates { get; internal set; }
            public IReadOnlyDictionary<string,int> SourceSkipCounts => skips;
            internal readonly Dictionary<string,int> skips = new Dictionary<string,int>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Entire fixed z-major/x-minor/kind1then2 scan; no heights/outcomes choose
        /// the grid or offset. Retain each species' FIRST256 source-eligible bundles,
        /// then rank-merge Sheep,Deer fairly. Caps/omissions are explicit; an exhausted
        /// retained prefix is never described as exhaustion of continuous worldspace.
        /// Cancellation throws without publishing a partial Inputs result.
        /// </summary>
        public static Inputs Build(ManagedSourceFilter filter, CancellationToken cancellation = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            var inputs = new Inputs();
            var sheep = new List<Candidate>();
            var deer = new List<Candidate>();
            for (int zi = FirstGridIndex; zi <= LastGridIndex; zi++)
                for (int xi = FirstGridIndex; xi <= LastGridIndex; xi++)
                    for (int kind = 1; kind <= 2; kind++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int ordinal = (zi-FirstGridIndex)*GridAxisCount + xi-FirstGridIndex;
                        var candidate = new Candidate(ordinal,kind,xi*GridStepMetres,zi*GridStepMetres);
                        inputs.ProfilesScanned++;
                        string failure = null;
                        foreach (Request request in candidate.Requests)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            SourceFacts facts = filter(request,kind,candidate.Area);
                            inputs.SourceRequestsEvaluated++;
                            failure = Refusal(facts);
                            if (failure != null) { failure += "@" + request.Label; break; }
                        }
                        if (failure != null)
                        {
                            inputs.skips.TryGetValue(failure,out int count);
                            inputs.skips[failure] = count+1;
                            continue;
                        }
                        if (kind == 1) inputs.SheepSourceEligible++; else inputs.DeerSourceEligible++;
                        List<Candidate> list = kind == 1 ? sheep : deer;
                        if (list.Count < MaximumRetainedPerSpecies) list.Add(candidate);
                        else if (kind == 1) inputs.SheepOmittedByRetentionCap++; else inputs.DeerOmittedByRetentionCap++;
                    }
            cancellation.ThrowIfCancellationRequested();
            var merged = new List<Candidate>();
            for (int rank=0;rank<Math.Max(sheep.Count,deer.Count);rank++)
            {
                if (rank<sheep.Count) merged.Add(sheep[rank]);
                if (rank<deer.Count) merged.Add(deer[rank]);
            }
            inputs.Candidates = merged.AsReadOnly();
            inputs.FullGridScanCompleted = true;
            return inputs;
        }
        /// <summary>Reconstruct ONLY canonical data points from a hash-pinned input
        /// file's ordered per-species ordinal arrays. This does not authenticate its
        /// source filters or an owner; the runtime rechecks all live predicates.</summary>
        public static ReadOnlyCollection<Candidate> ReconstructUnboundShortlist(
            IReadOnlyList<int> sheepOrdinals, IReadOnlyList<int> deerOrdinals)
        {
            var sheep = Reconstruct(sheepOrdinals,1);
            var deer = Reconstruct(deerOrdinals,2);
            var merged = new List<Candidate>();
            for (int rank=0;rank<Math.Max(sheep.Count,deer.Count);rank++)
            {
                if (rank<sheep.Count) merged.Add(sheep[rank]);
                if (rank<deer.Count) merged.Add(deer[rank]);
            }
            return merged.AsReadOnly();
        }
        private static List<Candidate> Reconstruct(IReadOnlyList<int> ordinals, int kind)
        {
            if (ordinals == null || ordinals.Count > MaximumRetainedPerSpecies)
                throw new ArgumentException("Bounded ordered canonical shortlist required");
            var list = new List<Candidate>();
            int previous = -1;
            foreach (int ordinal in ordinals)
            {
                if (ordinal <= previous || ordinal < 0 || ordinal >= GridAnchorCount)
                    throw new ArgumentException("Canonical ordinals must be distinct and increasing");
                previous = ordinal;
                int xi = ordinal%GridAxisCount + FirstGridIndex;
                int zi = ordinal/GridAxisCount + FirstGridIndex;
                list.Add(new Candidate(ordinal,kind,xi*GridStepMetres,zi*GridStepMetres));
            }
            return list;
        }

        private static string Refusal(SourceFacts facts)
        {
            if (facts.Raster != Verdict.Clear) return "RASTER_" + facts.Raster;
            if (!facts.AreaKnown) return "WHOLE_AREA_UNKNOWN";
            if (!facts.WholeAreaInside) return "NOT_WHOLE_REQUIRED_AREA";
            if (facts.Hazards != Verdict.Clear) return "SOURCE_HAZARDS_" + facts.Hazards;
            return null;
        }
    }
}
