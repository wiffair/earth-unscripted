using System;
using UnityEngine;
using UnityEngine.InputSystem;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    /// <summary>
    /// Garage-owned immediate-mode race setup editor. Editing/recommendations modify a cloned
    /// in-memory draft only; explicit garage fitting or pit service owns the transaction/save boundary.
    /// Draw and input are Unity main-thread operations. The owner retains this menu across frames
    /// and calls ResetDraft when changing garage context; no Unity object or save is owned here.
    /// </summary>
    public sealed class RaceSetupMenu
    {
        // Layout is in the parent's 1440 x 900 reference-pixel GUI space (DrivingUI.Draw).
        // The parent owns GUI.matrix scaling/letterboxing. These retain the original calibrated
        // layout; they are not physical screen pixels or a new responsive-layout policy.
        // Initial content height is an estimate until the first content draw, not screen height.
        const int InitialContentHeightReferencePixels = 900;
        const int TuningControlCount = 11;
        const int GuideControlCount = 2;
        const int EngineerControlCount = 7;
        const int ViewCount = 3;
        const int TuningView = 0;
        const int GuideView = 1;
        const int EngineerView = 2;
        // View-local focus IDs are stable navigation policy, not serialized profile fields.
        const int ViewSelectorControl = 0;
        const int FirstTuningEditControl = 1;
        const int LastTuningEditControl = 7;
        const int FirstEngineerQuestionControl = 1;
        const int LastEngineerQuestionControl = 4;
        const int CompoundCount = 5;
        const int SymptomCount = 7;
        const int PhaseCount = 5;
        const int SurfaceCount = 3;
        // Dimensionless per-activation adjustment, shared by all six scalar keyboard/D-pad edits.
        // Engineer recommendation steps have different semantics and are intentionally not reused.
        const float KeyboardAdjustmentStep = .01f;
        // Mirrors RaceSetup.Validate's authoritative limits. Core currently exposes no range
        // constants; parity checks guard drift without changing Core or interpreting tuning values
        // as physical ratios beyond their documented model meaning. Brake bias is a front share;
        // spring is a model multiplier; differential is power-slip tuning, not a locking percent.
        const float MinimumFinalDriveMultiplier = .8f;
        const float MaximumFinalDriveMultiplier = 1.2f;
        const float MinimumFrontBrakeShare = .48f;
        const float MaximumFrontBrakeShare = .65f;
        const float MinimumSpringMultiplier = .7f;
        const float MaximumSpringMultiplier = 1.3f;
        const float MinimumDifferentialTuning = .1f;
        const float MaximumDifferentialTuning = .9f;
        // Scroll policies are reference pixels; controller velocity uses unscaled real seconds.
        // Deadzone is a normalized stick magnitude; no input repeat/sensitivity policy changes.
        const int FocusScrollInsetReferencePixels = 20;
        const int ScrollViewportHeightReferencePixels = 244;
        const int GuideScrollStepReferencePixels = 100;
        const int KeyboardPageStepReferencePixels = 210;
        const int ControllerScrollReferencePixelsPerSecond = 250;
        const float ControllerScrollDeadzone = .2f;
        const int ContentWidthReferencePixels = 575;
        const int MinimumParagraphHeightReferencePixels = 22;
        const int ParagraphGapReferencePixels = 7;
        const int PanelLeftReferencePixels = 65;
        const int ContentPanelWidthReferencePixels = 605;
        const int ScrollViewportWidthReferencePixels = 620;
        const int ScrollBodyWidthReferencePixels = 595;
        const int ViewTabsTopReferencePixels = 349;
        const int ViewTabsHeightReferencePixels = 32;
        const int StatusTopReferencePixels = 388;
        const int StatusHeightReferencePixels = 22;
        const int ScrollTopReferencePixels = 418;
        const int ActionTopReferencePixels = 670;
        const int ActionHeightReferencePixels = 38;
        const int NavigationTopReferencePixels = 711;
        const int NavigationHeightReferencePixels = 18;
        const int SliderLabelWidthReferencePixels = 340;
        const int SliderLabelHeightReferencePixels = 28;
        const int SliderTrackHeightReferencePixels = 70;
        const int SliderLeftReferencePixels = 360;
        const int SliderTopOffsetReferencePixels = 9;
        const int SliderWidthReferencePixels = 210;
        const int SliderHeightReferencePixels = 20;
        const int ExplanationTopOffsetReferencePixels = 30;
        const int MinimumExplanationHeightReferencePixels = 36;
        const int SettingRowLeadReferencePixels = 35;
        const int SettingRowGapReferencePixels = 12;
        const int ChoiceTitleHeightReferencePixels = 25;
        const int ChoiceTopOffsetReferencePixels = 29;
        const int ChoiceHeightReferencePixels = 35;
        const int CompoundRowAdvanceReferencePixels = 44;
        const int GuideItemGapReferencePixels = 12;
        const int SymptomQuestionAdvanceReferencePixels = 146;
        const int QuestionAdvanceReferencePixels = 76;
        const int HighSpeedToggleHeightReferencePixels = 30;
        const int HighSpeedToggleAdvanceReferencePixels = 40;
        const int AdviceChangeGapReferencePixels = 8;
        const int AdviceScrollTargetReferencePixels = 330;
        const int LabelFontReferencePixels = 17;
        const int DetailFontReferencePixels = 12;
        const int BodyAndControlFontReferencePixels = 14;
        const int ButtonPaddingReferencePixels = 4;
        const int TogglePaddingReferencePixels = 2;
        const int ResetButtonWidthReferencePixels = 185;
        const int FitButtonLeftReferencePixels = 260;
        const int FitButtonWidthReferencePixels = 195;
        const int PitButtonLeftReferencePixels = 465;
        const int PitButtonWidthReferencePixels = 205;
        const int RecommendButtonWidthReferencePixels = 260;
        const int ApplyButtonLeftReferencePixels = 340;
        const int ApplyButtonWidthReferencePixels = 330;
        const int UnavailableTopReferencePixels = 355;
        const int UnavailableWidthReferencePixels = 600;
        const int UnavailableHeightReferencePixels = 65;
        // Nullable until an eligible car is drawn. Draft identity follows the active owned car ID.
        // Clones isolate mutable edits from profile data and from engineer/pit transaction objects.
        RaceSetup draftSetup;
        string draftCarId;
        int selectedView, focusedControl;
        Vector2 scrollPosition;
        float contentHeightReferencePixels = InitialContentHeightReferencePixels;
        int lastControllerInputFrame = -1;
        // Managed GUIStyle copies are lazily created from the current skin and retained across
        // ResetDraft. They borrow skin templates at creation; no textures/materials are disposed here.
        GUIStyle labelStyle, detailStyle, bodyStyle, headingStyle, buttonStyle, toggleStyle;
        // Focus IDs are view-local: 0 is always the view selector. Tuning uses 1..7 edits,
        // 8..10 actions; engineer uses 1..4 answers and 5..6 actions; guide has two focus slots.
        // Recorded content-local Y positions persist across reset until redrawn, as in the base UI.
        readonly float[] focusYPositionsReferencePixels = new float[TuningControlCount];
        bool isDrawingScrollContent;
        RaceEngineerSymptom selectedSymptom = RaceEngineerSymptom.FrontWash;
        RaceEngineerPhase selectedPhase = RaceEngineerPhase.MidCorner;
        RaceEngineerSurface selectedSurface = RaceEngineerSurface.Dry;
        bool reportedHighSpeed;
        RaceEngineerAdvice engineerAdvice;
        // Label ordering is the UI/enum navigation contract; strings and order stay unchanged.
        static readonly string[] ViewLabels =
        {
            "TUNING",
            "SETTING GUIDE",
            "RACE ENGINEER"
        };
        static readonly string[] SymptomLabels =
        {
            "WON’T TURN",
            "REAR SLIDES",
            "WHEELSPIN",
            "REAR SLIDES BRAKING",
            "TOP SPEED",
            "SLOW PICKUP",
            "TYRE GRIP"
        };
        static readonly string[] PhaseLabels =
        {
            "BRAKING",
            "TURN IN",
            "MID CORNER",
            "EXIT",
            "STRAIGHT"
        };
        static readonly string[] SurfaceLabels =
        {
            "DRY",
            "DAMP",
            "WET"
        };
        static readonly string[] CompoundLabels =
        {
            "SOFT",
            "MEDIUM",
            "HARD",
            "INTER",
            "WET"
        };
        static int WrapSelection(int value, int delta, int count) => (value + delta + count) % count;
        /// <summary>
        /// Discard the draft/advice and restore initial view/question/input state. This neither fits
        /// nor saves. Styles, measured content height and focus-position cache deliberately survive.
        /// May be called by the garage's successful-fit callback while Draw is still executing.
        /// </summary>
        public void ResetDraft()
        {
            draftSetup = null;
            draftCarId = null;
            engineerAdvice = null;
            scrollPosition = Vector2.zero;
            selectedView = focusedControl = 0;
            lastControllerInputFrame = -1;
            selectedSymptom = RaceEngineerSymptom.FrontWash;
            selectedPhase = RaceEngineerPhase.MidCorner;
            selectedSurface = RaceEngineerSurface.Dry;
            reportedHighSpeed = false;
        }

        // Lazy main-thread IMGUI initialization: do not allocate styles per Layout/Repaint event.
        void EnsureStyles()
        {
            if (labelStyle != null)
                return;
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = LabelFontReferencePixels,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0)
            };
            detailStyle = new GUIStyle(labelStyle)
            {
                fontSize = DetailFontReferencePixels
            };
            bodyStyle = new GUIStyle(labelStyle)
            {
                fontSize = BodyAndControlFontReferencePixels,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            headingStyle = new GUIStyle(labelStyle)
            {
                fontStyle = FontStyle.Bold
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = BodyAndControlFontReferencePixels,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            buttonStyle.padding.left = buttonStyle.padding.right = ButtonPaddingReferencePixels;
            toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = BodyAndControlFontReferencePixels,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            toggleStyle.padding.top = toggleStyle.padding.bottom = TogglePaddingReferencePixels;
        }

        // Only content-local controls record scroll anchors. A mouse-down also moves focus;
        // the caller owns Event processing, so this helper does not consume the mouse event.
        void TrackControlFocus(Rect controlRect, int controlIndex)
        {
            if (isDrawingScrollContent)
                focusYPositionsReferencePixels[controlIndex] = controlRect.y;
            if (Event.current.type == EventType.MouseDown && controlRect.Contains(Event.current.mousePosition))
                focusedControl = controlIndex;
        }

        string WithFocusMarker(string textValue, int controlIndex) => focusedControl == controlIndex ? "› " + textValue : textValue;
        float DrawParagraph(float verticalPosition, string textValue, GUIStyle textStyle = null)
        {
            textStyle = textStyle ?? bodyStyle;
            float height = Mathf.Max(MinimumParagraphHeightReferencePixels, textStyle.CalcHeight(new GUIContent(textValue), ContentWidthReferencePixels));
            GUI.Label(new Rect(0, verticalPosition, ContentWidthReferencePixels, height), textValue, textStyle);
            return verticalPosition + height + ParagraphGapReferencePixels;
        }

        void InvalidateAdvice()
        {
            engineerAdvice = null;
        }

        void ResetToDefaultDraft()
        {
            draftSetup = new RaceSetup();
            InvalidateAdvice();
        }

        // Capture a clone for the transaction factory. GarageChange enforces garage/network/race
        // eligibility and persistence/rollback; invoke the owner callback only on its success.
        // The callback may discard/recreate this menu's draft, so callers must tolerate invalidation.
        void FitDraftAtGarage(GameSession gameSession, Action onFitted)
        {
            var requestedSetup = draftSetup.Clone();
            if (gameSession.GarageChange((garageState, credits) => RaceSetupRules.Fit(garageState, credits, requestedSetup)))
                onFitted?.Invoke();
        }

        // A request, not an immediate fit/save. PitService receives a private clone and owns
        // service eligibility, timing and eventual transaction; absence of a service remains a no-op.
        void RequestDraftAtPit(GameSession gameSession)
        {
            gameSession.Pits?.RequestSetup(draftSetup.Clone());
        }

        // Tyre state is a cloned observation. Recommendations neither mutate the fitted setup
        // nor commit a save; applying advice below creates another isolated draft clone.
        void RequestEngineerAdvice(GameSession gameSession)
        {
            engineerAdvice = RaceEngineerRules.Recommend(draftSetup,
                new RaceEngineerRequest
            {
                Symptom = selectedSymptom,
                Phase = selectedPhase,
                Surface = selectedSurface,
                HighSpeed = reportedHighSpeed,
                Tyres = gameSession.Car.RaceTyres.Clone()
            });
        }

        bool CanApplyAdvice => engineerAdvice != null && engineerAdvice.Suggested != null && engineerAdvice.Changes.Count > 0;

        // Changes editing state only; selecting a suggestion is not authorization to fit it.
        void ApplyAdviceToDraft()
        {
            if (!CanApplyAdvice)
                return;
            draftSetup = engineerAdvice.Suggested.Clone();
            engineerAdvice = null;
            selectedView = TuningView;
            focusedControl = 1;
            scrollPosition = Vector2.zero;
        }

        // Delta is a discrete +/-1 navigation step. Focus wraps within the current view;
        // only editable content controls scroll into view. Action buttons stay outside the viewport.
        void MoveControlFocus(int delta)
        {
            int count = selectedView == TuningView ? TuningControlCount : selectedView == GuideView ? GuideControlCount : EngineerControlCount;
            focusedControl = WrapSelection(focusedControl, delta, count);
            if ((selectedView == TuningView && 
                focusedControl >= FirstTuningEditControl && 
                focusedControl <= LastTuningEditControl) || 
                (selectedView == EngineerView && 
                focusedControl >= FirstEngineerQuestionControl && 
                focusedControl <= LastEngineerQuestionControl))
                scrollPosition.y = Mathf.Clamp(focusYPositionsReferencePixels[focusedControl] - FocusScrollInsetReferencePixels,
                    0,
                    Mathf.Max(0,
                    contentHeightReferencePixels - ScrollViewportHeightReferencePixels));
        }

        // Shared keyboard/controller action routing. Delta is -1/0/+1; enter confirms actions.
        // Existing policy is retained: confirm advances view/compound/question choices, while a
        // zero-delta scalar activation still invalidates old advice. High-speed answers toggle.
        void ActivateFocusedControl(GameSession gameSession, Action onFitted, int delta, bool enter)
        {
            if (focusedControl == ViewSelectorControl)
            {
                selectedView = WrapSelection(selectedView, delta == 0 ? 1 : delta, ViewCount);
                scrollPosition = Vector2.zero;
            }
            else if (selectedView == TuningView)
            {
                float adjustment = delta * KeyboardAdjustmentStep;
                if (focusedControl == 1)
                    draftSetup.finalDrive = Mathf.Clamp(draftSetup.finalDrive + adjustment, MinimumFinalDriveMultiplier, MaximumFinalDriveMultiplier);
                else if (focusedControl == 2)
                    draftSetup.brakeBias = Mathf.Clamp(draftSetup.brakeBias + adjustment, MinimumFrontBrakeShare, MaximumFrontBrakeShare);
                else if (focusedControl == 3)
                    draftSetup.spring = Mathf.Clamp(draftSetup.spring + adjustment, MinimumSpringMultiplier, MaximumSpringMultiplier);
                else if (focusedControl == 4)
                    draftSetup.differential = Mathf.Clamp(draftSetup.differential + adjustment, MinimumDifferentialTuning, MaximumDifferentialTuning);
                else if (focusedControl == 5)
                    draftSetup.frontWing = Mathf.Clamp01(draftSetup.frontWing + adjustment);
                else if (focusedControl == 6)
                    draftSetup.rearWing = Mathf.Clamp01(draftSetup.rearWing + adjustment);
                else if (focusedControl == 7)
                    draftSetup.compound = (RaceCompound)WrapSelection((int)draftSetup.compound, delta == 0 ? 1 : delta, CompoundCount);
                else if (enter && focusedControl == 8)
                    ResetToDefaultDraft();
                else if (enter && focusedControl == 9)
                    FitDraftAtGarage(gameSession, onFitted);
                else if (enter && focusedControl == 10)
                    RequestDraftAtPit(gameSession);
                InvalidateAdvice();
            }
            else if (selectedView == GuideView)
                scrollPosition.y = Mathf.Clamp(scrollPosition.y + (delta == 0 ? GuideScrollStepReferencePixels : delta * GuideScrollStepReferencePixels),
                    0,
                    Mathf.Max(0,
                    contentHeightReferencePixels - ScrollViewportHeightReferencePixels));
            else
            {
                int adjustment = delta == 0 ? 1 : delta;
                if (focusedControl == 1)
                {
                    selectedSymptom = (RaceEngineerSymptom)WrapSelection((int)selectedSymptom, adjustment, SymptomCount);
                    InvalidateAdvice();
                }
                else if (focusedControl == 2)
                {
                    selectedPhase = (RaceEngineerPhase)WrapSelection((int)selectedPhase, adjustment, PhaseCount);
                    InvalidateAdvice();
                }
                else if (focusedControl == 3)
                {
                    selectedSurface = (RaceEngineerSurface)WrapSelection((int)selectedSurface, adjustment, SurfaceCount);
                    InvalidateAdvice();
                }
                else if (focusedControl == 4)
                {
                    reportedHighSpeed = !reportedHighSpeed;
                    InvalidateAdvice();
                }
                else if (enter && focusedControl == 5)
                    RequestEngineerAdvice(gameSession);
                else if (enter && focusedControl == 6)
                    ApplyAdviceToDraft();
            }
        }

        // Process only KeyDown, including platform-generated repeats. Consume recognized keys
        // after applying exactly one action; unrelated events remain available to the parent GUI.
        void ProcessKeyboard(GameSession gameSession, Action onFitted)
        {
            if (ControllerMenuGUI.Enabled) return;
            var keyboardEvent = Event.current;
            if (keyboardEvent.type != EventType.KeyDown)
                return;
            if (keyboardEvent.keyCode == KeyCode.Tab || keyboardEvent.keyCode == KeyCode.DownArrow || keyboardEvent.keyCode == KeyCode.UpArrow)
            {
                MoveControlFocus(keyboardEvent.keyCode == KeyCode.UpArrow || (keyboardEvent.keyCode == KeyCode.Tab && keyboardEvent.shift) ? -1 : 1);
                keyboardEvent.Use();
                return;
            }

            if (keyboardEvent.keyCode == KeyCode.PageDown || keyboardEvent.keyCode == KeyCode.PageUp)
            {
                scrollPosition.y = Mathf.Clamp(scrollPosition.y + (keyboardEvent.keyCode == KeyCode.PageDown ? KeyboardPageStepReferencePixels : -KeyboardPageStepReferencePixels),
                    0,
                    Mathf.Max(0,
                    contentHeightReferencePixels - ScrollViewportHeightReferencePixels));
                keyboardEvent.Use();
                return;
            }

            int delta = keyboardEvent.keyCode == KeyCode.RightArrow ? 1 : keyboardEvent.keyCode == KeyCode.LeftArrow ? -1 : 0;
            bool enter = keyboardEvent.keyCode == KeyCode.Return || keyboardEvent.keyCode == KeyCode.KeypadEnter || keyboardEvent.keyCode == KeyCode.Space;
            if (delta == 0 && !enter)
                return;
            ActivateFocusedControl(gameSession, onFitted, delta, enter);
            keyboardEvent.Use();
        }

        // IMGUI may run Layout/Repaint repeatedly per frame. Poll pressed edges once during
        // Layout using Time.frameCount; the frame is marked even when no gamepad is connected.
        // Right-stick scrolling is reference pixels/second using unscaledDeltaTime, not physics dt.
        void ProcessController(GameSession gameSession, Action onFitted)
        {
            if (ControllerMenuGUI.Enabled || Event.current.type != EventType.Layout || lastControllerInputFrame == Time.frameCount)
                return;
            lastControllerInputFrame = Time.frameCount;
            var gamepad = Gamepad.current;
            if (gamepad == null)
                return;
            if (gamepad.leftShoulder.wasPressedThisFrame || gamepad.rightShoulder.wasPressedThisFrame)
            {
                selectedView = WrapSelection(selectedView, gamepad.rightShoulder.wasPressedThisFrame ? 1 : -1, ViewCount);
                focusedControl = 0;
                scrollPosition = Vector2.zero;
                return;
            }

            if (gamepad.buttonEast.wasPressedThisFrame && selectedView != TuningView)
            {
                selectedView = TuningView;
                focusedControl = 0;
                scrollPosition = Vector2.zero;
                return;
            }

            if (gamepad.dpad.up.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame)
            {
                MoveControlFocus(gamepad.dpad.up.wasPressedThisFrame ? -1 : 1);
                return;
            }

            int delta = gamepad.dpad.right.wasPressedThisFrame ? 1 : gamepad.dpad.left.wasPressedThisFrame ? -1 : 0;
            if (delta != 0 || gamepad.buttonSouth.wasPressedThisFrame)
                ActivateFocusedControl(gameSession, onFitted, delta, gamepad.buttonSouth.wasPressedThisFrame);
            float stickScrollAxis = gamepad.rightStick.y.ReadValue();
            if (Mathf.Abs(stickScrollAxis) > ControllerScrollDeadzone)
                scrollPosition.y = Mathf.Clamp(scrollPosition.y - stickScrollAxis * ControllerScrollReferencePixelsPerSecond * Time.unscaledDeltaTime,
                    0,
                    Mathf.Max(0,
                    contentHeightReferencePixels - ScrollViewportHeightReferencePixels));
        }

        string SettingExplanation(string key)
        {
            foreach (var item in RaceEngineerRules.Explanations)
                if (item.Key == key)
                    return item.Effect + " " + item.Tradeoff;
            return "See the setting guide for the implemented effect and tradeoff.";
        }

        // Value/range are dimensionless model settings. Returns the GUI slider result; caller
        // assigns only the draft. Row explanation height is content-measured in reference pixels.
        float DrawSettingSlider(float verticalPosition, int controlIndex, string title, string key, float value, float minimum, float maximum)
        {
            var controlRect = new Rect(0, verticalPosition, SliderLabelWidthReferencePixels, SliderLabelHeightReferencePixels);
            TrackControlFocus(new Rect(0, verticalPosition, ContentWidthReferencePixels, SliderTrackHeightReferencePixels), controlIndex);
            GUI.Label(controlRect, WithFocusMarker(title + "  " + value.ToString("F2"), controlIndex), labelStyle);
            float nextValue = ControllerMenuGUI.HorizontalSlider(new Rect(SliderLeftReferencePixels,
                verticalPosition + SliderTopOffsetReferencePixels,
                SliderWidthReferencePixels,
                SliderHeightReferencePixels),
                value,
                minimum,
                maximum);
            if (nextValue != value)
            {
                focusedControl = controlIndex;
                InvalidateAdvice();
            }

            float height = Mathf.Max(MinimumExplanationHeightReferencePixels, bodyStyle.CalcHeight(new GUIContent(SettingExplanation(key)), ContentWidthReferencePixels));
            GUI.Label(new Rect(0, verticalPosition + ExplanationTopOffsetReferencePixels, ContentWidthReferencePixels, height), SettingExplanation(key), bodyStyle);
            return nextValue;
        }

        float SettingRowHeight(string key) => SettingRowLeadReferencePixels + Mathf.Max(MinimumExplanationHeightReferencePixels,
            bodyStyle.CalcHeight(new GUIContent(SettingExplanation(key)),
            ContentWidthReferencePixels)) + SettingRowGapReferencePixels;
        float DrawTuning()
        {
            float verticalPosition = 0;
            draftSetup.finalDrive = DrawSettingSlider(verticalPosition,
                1,
                "Final drive",
                "finalDrive",
                draftSetup.finalDrive,
                MinimumFinalDriveMultiplier,
                MaximumFinalDriveMultiplier);
            verticalPosition += SettingRowHeight("finalDrive");
            draftSetup.brakeBias = DrawSettingSlider(verticalPosition, 2, "Front brake bias", "brakeBias", draftSetup.brakeBias, MinimumFrontBrakeShare, MaximumFrontBrakeShare);
            verticalPosition += SettingRowHeight("brakeBias");
            draftSetup.spring = DrawSettingSlider(verticalPosition, 3, "Spring rate", "spring", draftSetup.spring, MinimumSpringMultiplier, MaximumSpringMultiplier);
            verticalPosition += SettingRowHeight("spring");
            draftSetup.differential = DrawSettingSlider(verticalPosition,
                4,
                "Differential",
                "differential",
                draftSetup.differential,
                MinimumDifferentialTuning,
                MaximumDifferentialTuning);
            verticalPosition += SettingRowHeight("differential");
            draftSetup.frontWing = DrawSettingSlider(verticalPosition, 5, "Front wing", "frontWing", draftSetup.frontWing, 0, 1);
            verticalPosition += SettingRowHeight("frontWing");
            draftSetup.rearWing = DrawSettingSlider(verticalPosition, 6, "Rear wing", "rearWing", draftSetup.rearWing, 0, 1);
            verticalPosition += SettingRowHeight("rearWing");
            verticalPosition = DrawParagraph(verticalPosition, WithFocusMarker("TYRE COMPOUND", 7), headingStyle);
            TrackControlFocus(new Rect(0, verticalPosition, ContentWidthReferencePixels, ChoiceHeightReferencePixels), 7);
            int nextCompound = ControllerMenuGUI.Toolbar(new Rect(0,
                verticalPosition,
                ContentWidthReferencePixels,
                ChoiceHeightReferencePixels),
                (int)draftSetup.compound,
                CompoundLabels,
                buttonStyle);
            if (nextCompound != (int)draftSetup.compound)
            {
                focusedControl = 7;
                draftSetup.compound = (RaceCompound)nextCompound;
                InvalidateAdvice();
            }

            verticalPosition += CompoundRowAdvanceReferencePixels;
            verticalPosition = DrawParagraph(verticalPosition, SettingExplanation("compound"));
            return verticalPosition;
        }

        float DrawSettingGuide()
        {
            float verticalPosition = 0;
            foreach (var item in RaceEngineerRules.Explanations)
            {
                verticalPosition = DrawParagraph(verticalPosition, item.Title, headingStyle);
                verticalPosition = DrawParagraph(verticalPosition, item.Effect);
                verticalPosition = DrawParagraph(verticalPosition, "Tradeoff: " + item.Tradeoff);
                verticalPosition += GuideItemGapReferencePixels;
            }

            return verticalPosition;
        }

        // Columns==0 selects a single toolbar row; positive columns select a wrapping grid.
        // Values/selected indexes follow the unchanged enum-label order, with no persisted schema.
        int DrawChoice(float verticalPosition, int controlIndex, string title, int selected, string[] values, int columns = 0)
        {
            GUI.Label(new Rect(0, verticalPosition, ContentWidthReferencePixels, ChoiceTitleHeightReferencePixels), WithFocusMarker(title, controlIndex), labelStyle);
            float height = columns > 0 ? Mathf.Ceil(values.Length / (float)columns) * ChoiceHeightReferencePixels : ChoiceHeightReferencePixels;
            TrackControlFocus(new Rect(0, verticalPosition + ChoiceTopOffsetReferencePixels, ContentWidthReferencePixels, height), controlIndex);
            return columns > 0
                ? ControllerMenuGUI.SelectionGrid(new Rect(0,
                verticalPosition + ChoiceTopOffsetReferencePixels,
                ContentWidthReferencePixels,
                height),
                selected,
                values,
                columns,
                buttonStyle)
                : ControllerMenuGUI.Toolbar(new Rect(0,
                verticalPosition + ChoiceTopOffsetReferencePixels,
                ContentWidthReferencePixels,
                height),
                selected,
                values,
                buttonStyle);
        }

        static string SettingDisplayName(string key)
        {
            switch (key)
            {
                case "finalDrive":
                    return "Final drive";
                case "brakeBias":
                    return "Front brake bias";
                case "spring":
                    return "Spring rate";
                case "differential":
                    return "Differential";
                case "frontWing":
                    return "Front wing";
                case "rearWing":
                    return "Rear wing";
                case "compound":
                    return "Tyre compound";
                default:
                    return key;
            }
        }

        float DrawEngineer()
        {
            float verticalPosition = DrawParagraph(0, "Tell the engineer what the car does. Suggestions use this game's handling model; try one change at a time.");
            int nextSymptom = DrawChoice(verticalPosition, 1, "What is the main problem?", (int)selectedSymptom, SymptomLabels, 3);
            if (nextSymptom != (int)selectedSymptom)
            {
                focusedControl = 1;
                selectedSymptom = (RaceEngineerSymptom)nextSymptom;
                InvalidateAdvice();
            }

            verticalPosition += SymptomQuestionAdvanceReferencePixels;
            int nextPhase = DrawChoice(verticalPosition, 2, "When does it happen?", (int)selectedPhase, PhaseLabels);
            if (nextPhase != (int)selectedPhase)
            {
                focusedControl = 2;
                selectedPhase = (RaceEngineerPhase)nextPhase;
                InvalidateAdvice();
            }

            verticalPosition += QuestionAdvanceReferencePixels;
            int nextSurface = DrawChoice(verticalPosition, 3, "What is the surface like?", (int)selectedSurface, SurfaceLabels);
            if (nextSurface != (int)selectedSurface)
            {
                focusedControl = 3;
                selectedSurface = (RaceEngineerSurface)nextSurface;
                InvalidateAdvice();
            }

            verticalPosition += QuestionAdvanceReferencePixels;
            TrackControlFocus(new Rect(0, verticalPosition, ContentWidthReferencePixels, HighSpeedToggleHeightReferencePixels), 4);
            bool nextHighSpeedReport = InterfaceTheme.CheckBox(new Rect(0,
                verticalPosition,
                ContentWidthReferencePixels,
                HighSpeedToggleHeightReferencePixels),
                reportedHighSpeed,
                WithFocusMarker("At higher speed (aero has more effect)",
                4),
                toggleStyle);
            if (nextHighSpeedReport != reportedHighSpeed)
            {
                focusedControl = 4;
                reportedHighSpeed = nextHighSpeedReport;
                InvalidateAdvice();
            }

            verticalPosition += HighSpeedToggleAdvanceReferencePixels;
            if (engineerAdvice == null)
                return DrawParagraph(verticalPosition, "Choose RECOMMEND below. Nothing will be fitted or saved automatically.");
            string status = engineerAdvice.Status == RaceEngineerStatus.Recommended ? "SUGGESTED STARTING POINT"
                : engineerAdvice.Status == RaceEngineerStatus.NoChange ? "NO CHANGE SUGGESTED"
                : engineerAdvice.Status == RaceEngineerStatus.Unsupported ? "MORE DETAIL NEEDED"
                : "CHECK SETUP OR ANSWERS";
            verticalPosition = DrawParagraph(verticalPosition, status, headingStyle);
            verticalPosition = DrawParagraph(verticalPosition, engineerAdvice.Summary);
            foreach (var change in engineerAdvice.Changes)
            {
                verticalPosition = DrawParagraph(verticalPosition, SettingDisplayName(change.Setting) + ": " + change.Before + " → " + change.After, headingStyle);
                verticalPosition = DrawParagraph(verticalPosition, change.Benefit);
                verticalPosition = DrawParagraph(verticalPosition, "Tradeoff: " + change.Tradeoff);
                verticalPosition += AdviceChangeGapReferencePixels;
            }

            return verticalPosition;
        }

        /// <summary>
        /// Draw in the parent's current reference GUI transform; requires an initialized session
        /// and IMGUI event. Only model 3 is editable. Clones on first eligible draw/car-ID change,
        /// then handles input before drawing controls. A successful-fit callback may clear the draft;
        /// it is reacquired before subsequent control drawing. No direct profile/save write occurs.
        /// Public parameter names remain unchanged for named-argument caller compatibility.
        /// </summary>
        public void Draw(GameSession session, Action fitted = null)
        {
            EnsureStyles();
            var activeCar = session.DrivenCar;
            if (activeCar.model != 3)
            {
                GUI.Label(new Rect(PanelLeftReferencePixels,
                    UnavailableTopReferencePixels,
                    UnavailableWidthReferencePixels,
                    UnavailableHeightReferencePixels),
                    "Fine tuning and race compounds are available for the single-seat car.",
                    bodyStyle);
                return;
            }

            if (draftSetup == null || draftCarId != activeCar.id)
            {
                ResetDraft();
                draftSetup = (activeCar.raceSetup ?? new RaceSetup()).Clone();
                draftCarId = activeCar.id;
            }

            ProcessKeyboard(session, fitted);
            if (draftSetup != null)
                ProcessController(session, fitted);
            if (draftSetup == null)
            {
                activeCar = session.DrivenCar;
                draftSetup = (activeCar.raceSetup ?? new RaceSetup()).Clone();
                draftCarId = activeCar.id;
            }

            TrackControlFocus(new Rect(PanelLeftReferencePixels, ViewTabsTopReferencePixels, ContentPanelWidthReferencePixels, ViewTabsHeightReferencePixels), 0);
            int nextValue = ControllerMenuGUI.Toolbar(new Rect(PanelLeftReferencePixels,
                ViewTabsTopReferencePixels,
                ContentPanelWidthReferencePixels,
                ViewTabsHeightReferencePixels),
                selectedView,
                ViewLabels,
                buttonStyle);
            if (nextValue != selectedView)
            {
                selectedView = nextValue;
                focusedControl = 0;
                scrollPosition = Vector2.zero;
            }

            var fittedTyres = session.Car.RaceTyres;
            GUI.Label(new Rect(PanelLeftReferencePixels,
                StatusTopReferencePixels,
                ContentPanelWidthReferencePixels,
                StatusHeightReferencePixels),
                $"Fitted: {fittedTyres.compound} · {fittedTyres.temperatureC:F0}°C · {fittedTyres.wear * 100:F0}% wear. Changes below are a draft.",
                detailStyle);
            scrollPosition = ControllerMenuGUI.BeginScrollView(new Rect(PanelLeftReferencePixels,
                ScrollTopReferencePixels,
                ScrollViewportWidthReferencePixels,
                ScrollViewportHeightReferencePixels),
                scrollPosition,
                new Rect(0,
                0,
                ScrollBodyWidthReferencePixels,
                Mathf.Max(ScrollViewportHeightReferencePixels,
                contentHeightReferencePixels)));
            isDrawingScrollContent = true;
            contentHeightReferencePixels = selectedView == TuningView ? DrawTuning() : selectedView == GuideView ? DrawSettingGuide() : DrawEngineer();
            isDrawingScrollContent = false;
            ControllerMenuGUI.EndScrollView();
            if (selectedView == TuningView)
            {
                TrackControlFocus(new Rect(PanelLeftReferencePixels, ActionTopReferencePixels, ResetButtonWidthReferencePixels, ActionHeightReferencePixels), 8);
                if (ControllerMenuGUI.Button(new Rect(PanelLeftReferencePixels,
                    ActionTopReferencePixels,
                    ResetButtonWidthReferencePixels,
                    ActionHeightReferencePixels),
                    WithFocusMarker("RESET DRAFT",
                    8),
                    buttonStyle))
                    ResetToDefaultDraft();
                TrackControlFocus(new Rect(FitButtonLeftReferencePixels, ActionTopReferencePixels, FitButtonWidthReferencePixels, ActionHeightReferencePixels), 9);
                if (ControllerMenuGUI.Button(new Rect(FitButtonLeftReferencePixels,
                    ActionTopReferencePixels,
                    FitButtonWidthReferencePixels,
                    ActionHeightReferencePixels),
                    WithFocusMarker("FIT AT GARAGE",
                    9),
                    buttonStyle))
                    FitDraftAtGarage(session, fitted);
                TrackControlFocus(new Rect(PitButtonLeftReferencePixels, ActionTopReferencePixels, PitButtonWidthReferencePixels, ActionHeightReferencePixels), 10);
                if (ControllerMenuGUI.Button(new Rect(PitButtonLeftReferencePixels,
                    ActionTopReferencePixels,
                    PitButtonWidthReferencePixels,
                    ActionHeightReferencePixels),
                    WithFocusMarker("REQUEST PIT FIT",
                    10),
                    buttonStyle))
                    RequestDraftAtPit(session);
            }
            else if (selectedView == EngineerView)
            {
                TrackControlFocus(new Rect(PanelLeftReferencePixels, ActionTopReferencePixels, RecommendButtonWidthReferencePixels, ActionHeightReferencePixels), 5);
                if (ControllerMenuGUI.Button(new Rect(PanelLeftReferencePixels,
                    ActionTopReferencePixels,
                    RecommendButtonWidthReferencePixels,
                    ActionHeightReferencePixels),
                    WithFocusMarker("RECOMMEND",
                    5),
                    buttonStyle))
                {
                    RequestEngineerAdvice(session);
                    scrollPosition.y = AdviceScrollTargetReferencePixels;
                }

                TrackControlFocus(new Rect(ApplyButtonLeftReferencePixels, ActionTopReferencePixels, ApplyButtonWidthReferencePixels, ActionHeightReferencePixels), 6);
                bool wasGuiEnabled = GUI.enabled;
                GUI.enabled = wasGuiEnabled && CanApplyAdvice;
                if (ControllerMenuGUI.Button(new Rect(ApplyButtonLeftReferencePixels,
                    ActionTopReferencePixels,
                    ApplyButtonWidthReferencePixels,
                    ActionHeightReferencePixels),
                    WithFocusMarker("APPLY TO DRAFT",
                    6),
                    buttonStyle))
                    ApplyAdviceToDraft();
                GUI.enabled = wasGuiEnabled;
            }
            else
                GUI.Label(new Rect(PanelLeftReferencePixels,
                    ActionTopReferencePixels,
                    ContentPanelWidthReferencePixels,
                    ActionHeightReferencePixels),
                    "Use TUNING to edit, then fit at the garage or request pit service.",
                    bodyStyle);
            GUI.Label(new Rect(PanelLeftReferencePixels,
                NavigationTopReferencePixels,
                ScrollViewportWidthReferencePixels,
                NavigationHeightReferencePixels),
                "Tab/D-pad: focus · ←→: adjust · Enter/confirm: select · shoulders: view · stick: scroll",
                detailStyle);
        }
    }
}
