using RoadRush.Packs;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GameSession
    {
        public string PhysicalLayoutIdentity {get;private set;}
        public PackCircuitContext ImportedCircuit { get; private set; }
        public Core.RacingRuleSet RacingRules { get; private set; } = Core.RacingRuleSet.LegacyDefaults;
        public bool SetRacingRulesForNextStart(Core.RacingRuleSet rules)
        {
            if (!BootReady || !Menu || rules == null || !World.IsCircuit || Network.Active || PreparingStart || ImportedCircuit?.Event!=null) return false;
            // A running session retains its captured immutable rules until Begin is
            // explicitly called again; no mid-lap penalty/eligibility mutation.
            RacingRules = rules; return true;
        }
        /// <summary>Called only after the chosen source has been verified/decoded on a
        /// worker. Persist the exact token through the ordinary career-save transaction.</summary>
        public bool SelectImportedCircuit(PackCircuitContext verified, int mode, int laps, int difficulty)
        {
            if (BootReady && Profile.garage.Active.model >= Core.ImportedVehicleCatalogue.BuiltInCount) { Notify("Select an owned built-in car before entering a circuit with this beta."); return false; }
            if (!BootReady || verified == null || !WorldPackCircuitReference.IsToken(verified.Token) || RefuseScopedDriving800()) return false;
            if (Network.Active) { Notify("Leave LAN before selecting an imported circuit"); return false; }
            if (LocationLoading.Busy) return false;
            if(Racing&&!Racing.RetryPendingLap())return false;
            var preset=verified.Event;
            if(preset!=null)
            {
                string vehicleClass=Profile.garage.Active.model==3?"single-seat":"road";
                if(!System.Linq.Enumerable.Contains(preset.Rules.AllowedVehicleClasses,vehicleClass)){Notify("This event does not allow the active car class");return false;}
                mode=preset.Mode;laps=preset.Laps;difficulty=preset.Difficulty;
            }
            string previous = Profile.region; int oldMode = Profile.trackMode, oldLaps = Profile.raceLaps, oldDifficulty = Profile.raceDifficulty;
            Profile.region = verified.Token; Profile.trackMode = Mathf.Clamp(mode, 0, 3);
            Profile.raceLaps = Mathf.Clamp(laps, 1, 5); Profile.raceDifficulty = Mathf.Clamp(difficulty, 0, 2);
            if (!TrySave())
            {
                Profile.region = previous; Profile.trackMode = oldMode; Profile.raceLaps = oldLaps; Profile.raceDifficulty = oldDifficulty;
                Notify("Circuit not changed: the career could not be saved"); return false;
            }
            AutoStartCircuit = true; Time.timeScale = 1; LocationLoading.Open(verified.VenueName + " · " + verified.LayoutName); return true;
        }
        public bool CanReturnFromMissingImportedCircuit => BootFailed && Profile != null && !Profile.LoadBlocked
            && WorldPackCircuitReference.IsToken(Profile.region);
        public bool CanReturnFromFailedLocation => BootFailed && Profile != null && !Profile.LoadBlocked
            && !string.IsNullOrEmpty(Profile.region);
        /// <summary>Explicit error-screen recovery, never a silent missing-pack fallback.</summary>
        public bool ReturnToCountyKeepingCareer()
        {
            if (!CanReturnFromFailedLocation) return false;
            string previous = Profile.region; Profile.region = "";
            if (!Profile.Save()) { Profile.region = previous; return false; }
            AutoStartCircuit = false; LocationLoading.Retry(); return true;
        }
    }
}
