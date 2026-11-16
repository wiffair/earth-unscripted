using System;
using System.Collections.Generic;
using System.Globalization;

namespace EarthUnscripted.Core
{
    // Enum order is the keyboard/controller question contract. Never reorder silently.
    public enum RaceEngineerSymptom
    {
        FrontWash,
        RearRotation,
        Wheelspin,
        BrakingInstability,
        TopSpeed,
        Acceleration,
        TyreGrip
    }

    public enum RaceEngineerPhase
    {
        Braking,
        Entry,
        MidCorner,
        Exit,
        Straight
    }

    public enum RaceEngineerSurface
    {
        Dry,
        Damp,
        Wet
    }

    public enum RaceEngineerStatus
    {
        Recommended,
        NoChange,
        Unsupported,
        InvalidInput
    }

    public enum RaceEngineerConfidence
    {
        None,
        Low,
        Moderate,
        High
    }

    /// <summary>
    /// UI question draft. Keep it stable during a synchronous call. Recommend captures
    /// its questions and clones the optional observed tyres; no telemetry is sampled.
    /// HighSpeed is the caller's qualitative choice, not a metres/second threshold.
    /// </summary>
    public sealed class RaceEngineerRequest
    {
        public RaceEngineerSymptom Symptom;
        public RaceEngineerPhase Phase;
        public RaceEngineerSurface Surface;
        public bool HighSpeed;
        // Optional observation, cloned immediately by Recommend; never changed or fitted.
        public RaceTyreState Tyres;
    }

    /// <summary>Immutable review text for one bounded draft edit; it never fits a car.</summary>
    public sealed class RaceEngineerChange
    {
        public string Setting { get; }
        public string Before { get; }
        public string After { get; }
        public string Benefit { get; }
        public string Tradeoff { get; }
        public string RuleId { get; }

        internal RaceEngineerChange(
            string setting,
            string beforeValue,
            string afterValue,
            string benefit,
            string tradeoff,
            string ruleId)
        {
            Setting = setting;
            Before = beforeValue;
            After = afterValue;
            Benefit = benefit;
            Tradeoff = tradeoff;
            RuleId = ruleId;
        }
    }

    /// <summary>Immutable help entry. Keys/text/order remain the existing UI contract.</summary>
    public sealed class RaceSettingExplanation
    {
        public string Key { get; }
        public string Title { get; }
        public string Effect { get; }
        public string Tradeoff { get; }

        internal RaceSettingExplanation(string settingKey, string title, string effect, string tradeoff)
        {
            Key = settingKey;
            Title = title;
            Effect = effect;
            Tradeoff = tradeoff;
        }
    }

    /// <summary>
    /// Detached recommendation snapshot. Current and Suggested return fresh clones on
    /// each read because the shared RaceSetup type is mutable. Changes is read-only;
    /// unsupported/invalid/boundary results contain no suggested setup or edits.
    /// </summary>
    public sealed class RaceEngineerAdvice
    {
        readonly RaceSetup currentSetup;
        readonly RaceSetup suggestedSetup;
        public RaceSetup Current => currentSetup == null ? null : currentSetup.Clone();
        public RaceSetup Suggested => suggestedSetup == null ? null : suggestedSetup.Clone();
        public IReadOnlyList<RaceEngineerChange> Changes { get; }
        public RaceEngineerStatus Status { get; }
        public RaceEngineerConfidence Confidence { get; }
        public string Summary { get; }

        internal RaceEngineerAdvice(
            RaceSetup beforeValue,
            RaceSetup afterValue,
            RaceEngineerChange[] changes,
            RaceEngineerStatus status,
            RaceEngineerConfidence confidence,
            string summary)
        {
            currentSetup = beforeValue == null ? null : beforeValue.Clone();
            suggestedSetup = afterValue == null ? null : afterValue.Clone();
            Changes = Array.AsReadOnly((RaceEngineerChange[])changes.Clone());
            Status = status;
            Confidence = confidence;
            Summary = summary;
        }
    }

    /// <summary>
    /// Deterministic local helper grounded in the implemented player race-car model.
    /// It suggests one small draft edit or refuses an unsupported/invalid diagnosis.
    /// It never fits, saves, spends, replaces tyres or accesses profiles/accounts.
    /// Keep input drafts stable during the call; results own their cloned snapshots.
    /// </summary>
    public static class RaceEngineerRules
    {
        // Conservative, dimensionless SINGLE-EDIT calibration, retained unchanged.
        // These step sizes are a helper policy, not measured lap-time improvements.
        // Legal limits come from RaceEngineering.RaceSetup's existing validation bounds;
        // reject invalid drafts rather than silently sanitizing the caller's settings.
        public const float FinalDriveStep = .02f;
        public const float BrakeBiasStep = .01f;
        public const float DifferentialStep = .05f;
        public const float WingStep = .05f;

        // Valid dimensionless tuning ranges match RaceEngineering.RaceSetup's existing
        // bounds. These constants name the old values; they do not recalibrate handling.
        const float MinimumFinalDrive = .8f;
        const float MaximumFinalDrive = 1.2f;
        const float MinimumFrontBrakeShare = .48f;
        const float MaximumFrontBrakeShare = .65f;
        const float MinimumSpringMultiplier = .7f;
        const float MaximumSpringMultiplier = 1.3f;
        const float MinimumDifferentialSetting = .1f;
        const float MaximumDifferentialSetting = .9f;
        const float MinimumWingSetting = 0f;
        const float MaximumWingSetting = 1f;

        // Tyre temperature is degrees Celsius; wear/wetness are fractions, not percent.
        // Fresh-set temperature matches VehicleController's replacement policy. The
        // selected wetness is a categorical comparison point, not a weather forecast.
        const float MinimumTyreTemperatureC = -50f;
        const float MaximumTyreTemperatureC = 200f;
        const float MinimumWearFraction = 0f;
        const float MaximumWearFraction = 1f;
        const float FreshTyreTemperatureC = 20f;
        const float FreshTyreWearFraction = 0f;
        const float DryWetnessFraction = 0f;
        const float DampWetnessFraction = .5f;
        const float WetWetnessFraction = 1f;

        const int FirstCompoundIndex = (int)RaceCompound.Soft;
        const int LastCompoundIndex = (int)RaceCompound.Wet;
        const int CompoundCount = 5;
        const int FirstWetWeatherCompoundIndex = (int)RaceCompound.Intermediate;
        const int FirstSymptomIndex = (int)RaceEngineerSymptom.FrontWash;
        const int LastSymptomIndex = (int)RaceEngineerSymptom.TyreGrip;
        const int FirstPhaseIndex = (int)RaceEngineerPhase.Braking;
        const int LastPhaseIndex = (int)RaceEngineerPhase.Straight;
        const int FirstSurfaceIndex = (int)RaceEngineerSurface.Dry;
        const int LastSurfaceIndex = (int)RaceEngineerSurface.Wet;
        // These user-facing explanations are frozen product text in this CODE-01
        // revision. Formula/source notes belong in comments and the reviewer mapping.
        static readonly IReadOnlyList<RaceSettingExplanation> explanations = Array.AsReadOnly(
            new[]
            {
                new RaceSettingExplanation(
                    "finalDrive",
                    "Final drive (.8–1.2)",
                    "More final drive improves low-speed pull and lowers the speed ceiling.",
                    "Stronger pull can cause more wheelspin. At high speed, this setting needs more information before an acceleration change is recommended."),
                new RaceSettingExplanation(
                    "brakeBias",
                    "Brake bias (.48–.65 front share)",
                    "Moves braking effort between the front and rear. More front share can help when the rear moves around under braking.",
                    "More front braking can leave less front grip for turning. It does not increase total brake strength."),
                new RaceSettingExplanation(
                    "spring",
                    "Spring (.7–1.3)",
                    "Changes suspension stiffness and damping together at all four wheels.",
                    "It does not adjust front-to-rear balance. A useful stiffness change needs more information about bumps and wheel contact."),
                new RaceSettingExplanation(
                    "differential",
                    "Differential (.1–.9): power-slip tuning",
                    "Higher values reduce modeled power slip and can help the rear keep more turning grip while accelerating.",
                    "It does not directly reduce engine force. Tyre grip and wheel contact still matter; treat it as power-slip tuning, not a locking percentage."),
                new RaceSettingExplanation(
                    "frontWing",
                    "Front wing (0–1)",
                    "Adds more front axle load as speed rises, helping front grip in faster corners when the wheels stay on the ground.",
                    "More drag can reduce straight-line speed. It has little effect on slow-corner front wash."),
                new RaceSettingExplanation(
                    "rearWing",
                    "Rear wing (0–1)",
                    "Adds more rear axle load as speed rises, helping rear grip in faster corners when the wheels stay on the ground.",
                    "More drag can reduce straight-line speed. It is not a reliable fix for low-speed wheelspin or wheels losing contact."),
                new RaceSettingExplanation(
                    "compound",
                    "Tyre compound and condition",
                    "Choose tyres for the selected dry, damp or wet surface. Temperature and wear also affect grip.",
                    "Fresh tyres start cold and can initially grip less than the warm set already fitted. This helper does not predict the weather or plan a full stint."),
                new RaceSettingExplanation(
                    "compound.Soft",
                    "Soft",
                    "The strongest dry grip and quickest warm-up of the slick tyres.",
                    "Wears fastest among the slicks and has poor wet grip. A cold replacement still needs time to build grip."),
                new RaceSettingExplanation(
                    "compound.Medium",
                    "Medium",
                    "A middle choice for dry grip, warm-up and durability.",
                    "Less dry grip than Soft, but slower wear. Speed and sliding affect how quickly it heats and wears."),
                new RaceSettingExplanation(
                    "compound.Hard",
                    "Hard",
                    "The longest-wearing slick, with the slowest warm-up.",
                    "Less dry grip than Soft or Medium. It may take longer to build grip after fitting."),
                new RaceSettingExplanation(
                    "compound.Intermediate",
                    "Intermediate",
                    "A grip-focused choice for damp conditions, with a relatively quick heat response.",
                    "Lower dry grip than slicks and much faster wear on dry ground. A weather change can make it the wrong choice."),
                new RaceSettingExplanation(
                    "compound.Wet",
                    "Wet",
                    "The strongest grip on the wettest surface represented by the game.",
                    "Poor dry grip and faster wear as the surface dries. Fresh cold tyres can still have less grip than a warm set.")
            });

        public static IReadOnlyList<RaceSettingExplanation> Explanations => explanations;

        static bool IsFiniteNumber(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool IsInRange(float value, float minimumValue, float maximumValue) =>
            IsFiniteNumber(value) && value >= minimumValue && value <= maximumValue;
        static bool IsValidInput(RaceSetup setup) =>
            setup != null
            && IsInRange(setup.finalDrive, MinimumFinalDrive, MaximumFinalDrive)
            && IsInRange(setup.brakeBias, MinimumFrontBrakeShare, MaximumFrontBrakeShare)
            && IsInRange(setup.spring, MinimumSpringMultiplier, MaximumSpringMultiplier)
            && IsInRange(setup.differential, MinimumDifferentialSetting, MaximumDifferentialSetting)
            && IsInRange(setup.frontWing, MinimumWingSetting, MaximumWingSetting)
            && IsInRange(setup.rearWing, MinimumWingSetting, MaximumWingSetting)
            && (int)setup.compound >= FirstCompoundIndex
            && (int)setup.compound <= LastCompoundIndex;
        static bool IsValidInput(RaceTyreState tyres) =>
            tyres == null
            || ((int)tyres.compound >= FirstCompoundIndex
                && (int)tyres.compound <= LastCompoundIndex
                && IsInRange(tyres.temperatureC, MinimumTyreTemperatureC, MaximumTyreTemperatureC)
                && IsInRange(tyres.wear, MinimumWearFraction, MaximumWearFraction));
        static string FormatSettingValue(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        static RaceEngineerAdvice CreateNoSuggestion(
            RaceSetup setup,
            RaceEngineerStatus status,
            string summary) =>
            new RaceEngineerAdvice(
                setup, null, new RaceEngineerChange[0], status, RaceEngineerConfidence.None, summary);
        static bool IsSupportedPhase(RaceEngineerSymptom symptom, RaceEngineerPhase phase)
        {
            switch (symptom)
            {
                case RaceEngineerSymptom.FrontWash:
                case RaceEngineerSymptom.RearRotation:
                    return phase == RaceEngineerPhase.Entry || phase == RaceEngineerPhase.MidCorner || phase == RaceEngineerPhase.Exit;
                case RaceEngineerSymptom.Wheelspin:
                case RaceEngineerSymptom.Acceleration:
                    return phase == RaceEngineerPhase.Exit || phase == RaceEngineerPhase.Straight;
                case RaceEngineerSymptom.BrakingInstability:
                    // VehicleController redistributes commanded brake share. Combined
                    // longitudinal/lateral force is bounded; forward bias is conditional
                    // on reported rear instability and therefore always Low confidence.
                    return phase == RaceEngineerPhase.Braking || phase == RaceEngineerPhase.Entry;
                case RaceEngineerSymptom.TopSpeed:
                    // Player drive force includes ratio * clamp(1-speed*ratio/limit).
                    // Lower ratio raises its taper threshold but reduces launch pull.
                    return phase == RaceEngineerPhase.Straight;
                case RaceEngineerSymptom.TyreGrip:
                    return true;
                default:
                    return false;
            }
        }

        // Limit one setting to its existing range. A saturated boundary never triggers
        // an unrelated edit. Round display values normally, but fall back to round-trip
        // text when a one-ULP change would otherwise display equal before/after values.
        static RaceEngineerAdvice RecommendBoundedStep(
            RaceSetup currentSetup,
            string settingKey,
            float requestedDelta,
            float minimumValue,
            float maximumValue,
            string benefit,
            string tradeoff,
            string ruleId,
            RaceEngineerConfidence confidence,
            string summary)
        {
            var suggestedSetup = currentSetup.Clone();
            float beforeValue;
            switch (settingKey)
            {
                case "finalDrive":
                    beforeValue = currentSetup.finalDrive;
                    break;
                case "brakeBias":
                    beforeValue = currentSetup.brakeBias;
                    break;
                case "differential":
                    beforeValue = currentSetup.differential;
                    break;
                case "frontWing":
                    beforeValue = currentSetup.frontWing;
                    break;
                default:
                    beforeValue = currentSetup.rearWing;
                    break;
            }

            float afterValue = Math.Max(minimumValue, Math.Min(maximumValue, beforeValue + requestedDelta));
            if (afterValue == beforeValue)
                return CreateNoSuggestion(
                    currentSetup,
                    RaceEngineerStatus.NoChange,
                    "Already at the boundary for this conservative step. No unrelated setting was changed.");
            switch (settingKey)
            {
                case "finalDrive":
                    suggestedSetup.finalDrive = afterValue;
                    break;
                case "brakeBias":
                    suggestedSetup.brakeBias = afterValue;
                    break;
                case "differential":
                    suggestedSetup.differential = afterValue;
                    break;
                case "frontWing":
                    suggestedSetup.frontWing = afterValue;
                    break;
                case "rearWing":
                    suggestedSetup.rearWing = afterValue;
                    break;
            }

            string beforeText = FormatSettingValue(beforeValue), afterText = FormatSettingValue(afterValue);
            if (beforeText == afterText)
            {
                beforeText = beforeValue.ToString("R", CultureInfo.InvariantCulture);
                afterText = afterValue.ToString("R", CultureInfo.InvariantCulture);
            }

            var change = new RaceEngineerChange(settingKey, beforeText, afterText, benefit, tradeoff, ruleId);
            return new RaceEngineerAdvice(
                currentSetup,
                suggestedSetup,
                new[] { change },
                RaceEngineerStatus.Recommended,
                confidence,
                summary + " One small draft change; test it before changing another setting.");
        }

        /// <summary>
        /// Snapshot and validate inputs, then return conditional advice without mutation.
        /// Confidence means support for the model's direction of effect, not probability
        /// of a faster lap. No automatic spring change is justified: the model changes
        /// all-wheel stiffness and damping together, not front/rear balance.
        /// </summary>
        public static RaceEngineerAdvice Recommend(RaceSetup draft, RaceEngineerRequest request)
        {
            var currentSetup = draft == null ? null : draft.Clone();
            if (!IsValidInput(currentSetup) || request == null)
                return CreateNoSuggestion(
                    currentSetup,
                    RaceEngineerStatus.InvalidInput,
                    "A finite, in-range draft and a question are required. Nothing was changed.");
            var symptom = request.Symptom;
            var phase = request.Phase;
            var surface = request.Surface;
            bool isHighSpeed = request.HighSpeed;
            var tyres = request.Tyres == null ? null : request.Tyres.Clone();
            if ((int)symptom < FirstSymptomIndex
                || (int)symptom > LastSymptomIndex
                || (int)phase < FirstPhaseIndex
                || (int)phase > LastPhaseIndex
                || (int)surface < FirstSurfaceIndex
                || (int)surface > LastSurfaceIndex
                || !IsValidInput(tyres))
                return CreateNoSuggestion(
                    currentSetup,
                    RaceEngineerStatus.InvalidInput,
                    "Question or tyre condition is outside the implemented ranges. Nothing was changed.");
            if (!IsSupportedPhase(symptom, phase))
                return CreateNoSuggestion(
                    currentSetup,
                    RaceEngineerStatus.Unsupported,
                    "That symptom and phase do not describe a supported case. Choose where the problem actually occurs.");
            float wetness = surface == RaceEngineerSurface.Dry
                ? DryWetnessFraction
                : surface == RaceEngineerSurface.Damp ? DampWetnessFraction : WetWetnessFraction;
            if (symptom == RaceEngineerSymptom.TyreGrip)
                return RecommendCompound(currentSetup, tyres, wetness);
            var confidence = surface != RaceEngineerSurface.Dry
                && (int)currentSetup.compound < FirstWetWeatherCompoundIndex
                    ? RaceEngineerConfidence.Low : RaceEngineerConfidence.Moderate;
            string context = confidence == RaceEngineerConfidence.Low
                ? "Slick tyres on a damp/wet surface may dominate this symptom; check TyreGrip too. "
                : "";
            switch (symptom)
            {
                case RaceEngineerSymptom.FrontWash:
                    // VehicleController applies grounded axle load proportional to speed²
                    // and wing setting, plus drag. No low-speed front-only diagnosis follows.
                    if (!isHighSpeed)
                        return CreateNoSuggestion(
                            currentSetup,
                            RaceEngineerStatus.Unsupported,
                            "Wings have little effect at low speed. The available controls do not give a reliable front-only fix here; check tyre grip and entry speed before guessing spring balance.");
                    return RecommendBoundedStep(
                        currentSetup,
                        "frontWing",
                        WingStep,
                        MinimumWingSetting,
                        MaximumWingSetting,
                        "Adds front grip support in faster corners while the wheels stay on the ground.",
                        "More drag can reduce straight-line speed. It may not help if the problem is tyre grip or wheel contact.",
                        "VC_FRONT_AERO",
                        confidence,
                        context + "For reported high-speed front wash, try a little more front wing.");
                case RaceEngineerSymptom.RearRotation:
                    if (isHighSpeed)
                        return RecommendBoundedStep(
                            currentSetup,
                            "rearWing",
                            WingStep,
                            MinimumWingSetting,
                            MaximumWingSetting,
                            "Adds rear grip support in faster corners while the wheels stay on the ground.",
                            "More drag can reduce straight-line speed. Wheelspin or loss of contact can still cause the slide.",
                            "VC_REAR_AERO",
                            confidence,
                            context + "For reported high-speed rear rotation, try a little more rear wing.");
                    if (phase != RaceEngineerPhase.Exit)
                        return CreateNoSuggestion(
                            currentSetup,
                            RaceEngineerStatus.Unsupported,
                            "Low-speed entry or mid-corner rotation needs more information. Use the braking-instability question if it happens under braking; an exit-wheelspin change may not help here.");
                    return RecommendSlipStep(currentSetup, confidence, context);
                case RaceEngineerSymptom.Wheelspin:
                    return RecommendSlipStep(currentSetup, confidence, context);
                case RaceEngineerSymptom.BrakingInstability:
                    // VehicleController redistributes commanded brake share. Combined
                    // longitudinal/lateral force is bounded; forward bias is conditional
                    // on reported rear instability and therefore always Low confidence.
                    return RecommendBoundedStep(
                        currentSetup,
                        "brakeBias",
                        BrakeBiasStep,
                        MinimumFrontBrakeShare,
                        MaximumFrontBrakeShare,
                        "Uses less rear braking, which may help if the rear is moving around under braking.",
                        "Uses more front braking and can reduce turning grip while braking. This is a cautious trial, not a diagnosis.",
                        "VC_BRAKE_SHARE",
                        RaceEngineerConfidence.Low,
                        context + "Try a small forward bias step only if the rear is unstable under braking.");
                case RaceEngineerSymptom.TopSpeed:
                    // Player drive force includes ratio * clamp(1-speed*ratio/limit).
                    // Lower ratio raises its taper threshold but reduces launch pull.
                    return RecommendBoundedStep(
                        currentSetup,
                        "finalDrive",
                        -FinalDriveStep,
                        MinimumFinalDrive,
                        MaximumFinalDrive,
                        "Raises the speed ceiling set by final drive.",
                        "Less low-speed pull. Drag or lack of grip may still limit the speed you actually reach.",
                        "VC_DRIVE_TAPER",
                        confidence,
                        context + "For the straight-line speed threshold, try a slightly lower final drive.");
                case RaceEngineerSymptom.Acceleration:
                    // The drive expression is not monotonic in ratio at high speed.
                    // Keep the existing refusal rather than infer missing speed/RPM data.
                    if (isHighSpeed)
                        return CreateNoSuggestion(
                            currentSetup,
                            RaceEngineerStatus.Unsupported,
                            "At higher speeds, a ratio change can help or hurt depending on the actual speed. More information is needed before recommending an acceleration change.");
                    return RecommendBoundedStep(
                        currentSetup,
                        "finalDrive",
                        FinalDriveStep,
                        MinimumFinalDrive,
                        MaximumFinalDrive,
                        "Adds low-speed pulling force at the rear wheels.",
                        "More wheelspin risk and a lower speed ceiling. Extra force may not help when tyre grip is the limit.",
                        "VC_LOW_SPEED_DRIVE",
                        confidence,
                        context + "For low-speed acceleration, try a little more final drive.");
                default:
                    return CreateNoSuggestion(
                        currentSetup,
                        RaceEngineerStatus.Unsupported,
                        "No supported recommendation for this case.");
            }
        }

        // VehicleController multiplies power-slip demand by 1.2 - .5*differential.
        // Increasing this setting lowers modeled slip and can retain rear lateral reserve;
        // it neither reduces engine force nor represents a physical locking percentage.
        static RaceEngineerAdvice RecommendSlipStep(RaceSetup currentSetup, RaceEngineerConfidence confidence, string context)
        {
            return RecommendBoundedStep(
                currentSetup,
                "differential",
                DifferentialStep,
                MinimumDifferentialSetting,
                MaximumDifferentialSetting,
                "Reduces modeled power slip, helping the rear retain more turning grip while accelerating.",
                "Does not reduce engine force. Tyre grip and wheel contact still need attention.",
                "VC_POWER_SLIP_MULTIPLIER",
                confidence,
                context + "For wheelspin or powered exit rotation, try a small increase in the differential setting.");
        }

        // Compare actual RaceTyreState.Grip for all five compounds as fresh 20°C sets.
        // This is not a warm-up/wear forecast. Do not transfer an observation to another
        // compound, or recommend a cold replacement with less immediate grip than the
        // current observation. Missing observation keeps confidence Low. A strict-greater
        // comparison preserves the original first-enum tie order.
        static RaceEngineerAdvice RecommendCompound(RaceSetup currentSetup, RaceTyreState observedTyres, float wetness)
        {
            if (observedTyres != null && observedTyres.compound != currentSetup.compound)
                return CreateNoSuggestion(
                    currentSetup,
                    RaceEngineerStatus.Unsupported,
                    "The fitted tyre observation differs from the draft compound. Its temperature/wear cannot be transferred to this draft; compare the compound guide or reset the draft first.");
            RaceCompound bestCompound = currentSetup.compound;
            float bestGrip = float.MinValue;
            for (int compoundIndex = 0; compoundIndex < CompoundCount; compoundIndex++)
            {
                var candidateTyres = new RaceTyreState
                {
                    compound = (RaceCompound)compoundIndex,
                    temperatureC = FreshTyreTemperatureC,
                    wear = FreshTyreWearFraction
                };
                float candidateGrip = candidateTyres.Grip(wetness);
                if (candidateGrip > bestGrip)
                {
                    bestGrip = candidateGrip;
                    bestCompound = (RaceCompound)compoundIndex;
                }
            }

            if (bestCompound == currentSetup.compound)
                return CreateNoSuggestion(
                    currentSetup,
                    RaceEngineerStatus.NoChange,
                    "This draft already uses the grip-first compound for the selected surface. Temperature and wear can still limit grip; these tyres have not been replaced.");
            if (observedTyres != null && bestGrip < observedTyres.Grip(wetness))
                return CreateNoSuggestion(
                    currentSetup,
                    RaceEngineerStatus.Unsupported,
                    "The grip-first alternative is " + bestCompound + ", but a fresh 20°C set currently has less model grip than the observed warm/worn tyres. Without stint context, no change is recommended; fitting would reset temperature and wear.");
            var suggestedSetup = currentSetup.Clone();
            suggestedSetup.compound = bestCompound;
            string benefit = "A fresh set of this compound gives more grip for the selected "
                + (wetness == DryWetnessFraction ? "dry" : wetness == DampWetnessFraction ? "damp" : "wet")
                + " surface in this comparison.";
            string tradeoff = bestCompound == RaceCompound.Soft
                ? "Soft wears fastest among the slicks. Fitting starts a cold set; this does not predict your lap time or how long the set will last."
                : "Wet-weather tyres wear faster as the surface dries. Fitting starts a cold set; this is not a forecast or full-stint plan.";
            var change = new RaceEngineerChange(
                "compound",
                currentSetup.compound.ToString(),
                bestCompound.ToString(),
                benefit,
                tradeoff,
                "TYRE_GRIP_FRESH_COMPARISON");
            return new RaceEngineerAdvice(
                currentSetup,
                suggestedSetup,
                new[] { change },
                RaceEngineerStatus.Recommended,
                observedTyres == null ? RaceEngineerConfidence.Low : RaceEngineerConfidence.Moderate,
                "This is a grip-first compound choice for the surface you selected. Review warm-up and wear before trying it; nothing has been fitted.");
        }
    }
}
