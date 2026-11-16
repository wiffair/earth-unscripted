using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RoadRush.Core
{
    /// <summary>Resolved behaviour only. This immutable value grants no session or reward authority.</summary>
    public sealed class RacingRuleSet
    {
        public readonly string Id, Name, ParentId, BehaviourSha256;
        public readonly long Revision;
        public readonly int Gates, RaceOpponents;
        public readonly float OffTrackGrace, TrackLimitPenalty, RecoveryPenalty, RacePitLaneSpeed,
            RaceCountdown, TrialCountdown, RaceFinishWait;
        public readonly bool RecoveryInvalidates, CollisionInvalidates, RacePitLaneAllowed;
        readonly ReadOnlyCollection<string> classes;
        public IReadOnlyList<string> AllowedVehicleClasses => classes;
        public string[] CopyAllowedVehicleClasses() => classes.ToArray();

        public static RacingRuleSet LegacyDefaults => Create("builtin-rules2", "Current built-in racing", 2, null,
            24, .25f, 5, 8, true, true, true, 16, 3, 1.5f, 30, 3, new[] { "road", "single-seat" });
        static void Need(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
        static float Bound(float value, float max, string field)
        { Need(!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= max, "Racing rules " + field + " range"); return value == 0 ? 0 : value; }
        static bool SafeId(string value) => value != null && Regex.IsMatch(value, "\\A[a-z][a-z0-9-]{0,63}\\z");
        public static RacingRuleSet Create(string id, string name, long revision, string parentId,
            int gates, float offTrackGrace, float trackLimitPenalty, float recoveryPenalty,
            bool recoveryInvalidates, bool collisionInvalidates, bool racePitLaneAllowed,
            float racePitLaneSpeed, float raceCountdown, float trialCountdown, float raceFinishWait,
            int raceOpponents, IEnumerable<string> allowedVehicleClasses)
        {
            Need(SafeId(id) && (parentId == null || SafeId(parentId) && parentId != id), "Racing rules identity/parent");
            Need(!string.IsNullOrWhiteSpace(name) && name.Length <= 160 && !name.Any(char.IsControl), "Racing rules name");
            Need(revision >= 1, "Racing rules revision");
            Need(gates >= 6 && gates <= 384 && gates % 3 == 0, "Racing rules gates must be6..384 divisible by3");
            Need(raceOpponents >= 0 && raceOpponents <= 32, "Racing rules opponents range");
            Need(allowedVehicleClasses != null, "Racing rules classes required");
            // Bounded enumeration: a supplied arbitrary IEnumerable cannot allocate an unbounded list.
            var values = allowedVehicleClasses.Take(3).ToArray();
            Need(values.Length >= 1 && values.Length <= 2 && values.All(v => v == "road" || v == "single-seat")
                && values.Distinct(StringComparer.Ordinal).Count() == values.Length, "Racing rules class set");
            Array.Sort(values, StringComparer.Ordinal);
            return new RacingRuleSet(id, name, revision, parentId, gates,
                Bound(offTrackGrace, 30, "offTrackGrace"), Bound(trackLimitPenalty, 600, "trackLimitPenalty"),
                Bound(recoveryPenalty, 600, "recoveryPenalty"), recoveryInvalidates, collisionInvalidates,
                racePitLaneAllowed, Bound(racePitLaneSpeed, 100, "racePitLaneSpeed"),
                Bound(raceCountdown, 60, "raceCountdown"), Bound(trialCountdown, 60, "trialCountdown"),
                Bound(raceFinishWait, 600, "raceFinishWait"), raceOpponents, values);
        }
        RacingRuleSet(string id, string name, long revision, string parentId, int gates, float grace,
            float trackPenalty, float recoveryPenalty, bool recoveryInvalidates, bool collisionInvalidates,
            bool pitAllowed, float pitSpeed, float countdown, float trialCountdown, float finishWait,
            int opponents, string[] values)
        {
            Id=id; Name=name; Revision=revision; ParentId=parentId; Gates=gates; OffTrackGrace=grace;
            TrackLimitPenalty=trackPenalty; RecoveryPenalty=recoveryPenalty; RecoveryInvalidates=recoveryInvalidates;
            CollisionInvalidates=collisionInvalidates; RacePitLaneAllowed=pitAllowed; RacePitLaneSpeed=pitSpeed;
            RaceCountdown=countdown; TrialCountdown=trialCountdown; RaceFinishWait=finishWait; RaceOpponents=opponents;
            classes=Array.AsReadOnly((string[])values.Clone());
            // Versioned binary32 resolved settings: no JSON number formatting, metadata, host culture or class-order dependency.
            using (var bytes=new MemoryStream())
            {
                using (var writer=new BinaryWriter(bytes, new UTF8Encoding(false, true), true))
                {
                    writer.Write("RoadRushRacingBehaviour1"); writer.Write(Gates); writer.Write(OffTrackGrace);
                    writer.Write(TrackLimitPenalty); writer.Write(RecoveryPenalty); writer.Write(RecoveryInvalidates);
                    writer.Write(CollisionInvalidates); writer.Write(RacePitLaneAllowed); writer.Write(RacePitLaneSpeed);
                    writer.Write(RaceCountdown); writer.Write(TrialCountdown); writer.Write(RaceFinishWait); writer.Write(RaceOpponents);
                    writer.Write(classes.Count); foreach (var value in classes) writer.Write(value);
                }
                using (var sha=SHA256.Create()) BehaviourSha256=BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
