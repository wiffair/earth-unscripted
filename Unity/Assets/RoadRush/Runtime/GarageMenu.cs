using System;
using UnityEngine;
using UnityEngine.InputSystem;
using RoadRush.Core;

namespace RoadRush
{
    /// <summary>
    /// Owned-garage IMGUI controller: previews edits and queues existing Core transactions, while
    /// GameSession remains the commit/save authority. No hire, wallet or rental behavior is added.
    /// All drawing, input, scene-object lifetime and callbacks are Unity-main-thread only.
    /// Public parameter names Open(s) and Draw(back) are retained for named-argument compatibility.
    /// </summary>
    public sealed class GarageMenu
    {
        // Parent DrivingUI owns 1440 x 900 reference-pixel scaling/letterboxing. Layout and
        // font constants retain the original calibration; these are not raw display pixels.
        const int GaragePanelLeftReferencePixels = 35;
        const int GaragePanelTopReferencePixels = 240;
        const int GaragePanelWidthReferencePixels = 680;
        const int GaragePanelHeightReferencePixels = 650;
        const int PanelContentLeftReferencePixels = 65;
        const int PageTabsTopReferencePixels = 253;
        const int WideContentWidthReferencePixels = 620;
        const int PageTabsHeightReferencePixels = 36;
        const int SpecificationPanelLeftReferencePixels = 760;
        const int SpecificationPanelTopReferencePixels = 55;
        const int SpecificationPanelWidthReferencePixels = 625;
        const int SpecificationPanelHeightReferencePixels = 165;
        const int OwnedListTopReferencePixels = 350;
        const int OwnedViewportHeightReferencePixels = 315;
        const int ContentOriginXReferencePixels = 0;
        const int ContentOriginYReferencePixels = 0;
        const int OwnedScrollBodyWidthReferencePixels = 595;
        const int OwnedRowLeftReferencePixels = 0;
        const int OwnedRowStepReferencePixels = 55;
        const int OwnedRowWidthReferencePixels = 590;
        const int OwnedRowHeightReferencePixels = 44;
        const int FirstPaintPaletteTopReferencePixels = 379;
        const int StyleContentWidthReferencePixels = 605;
        const int StyleToolbarHeightReferencePixels = 32;
        const int SecondPaintPaletteTopReferencePixels = 415;
        const int WheelToolbarTopReferencePixels = 487;
        const int BodyToolbarTopReferencePixels = 556;
        const int FinishToolbarTopReferencePixels = 626;
        const int PageHintTopReferencePixels = 290;
        const int PageHintHeightReferencePixels = 14;
        const int CreditsTopReferencePixels = 304;
        const int CreditsHeightReferencePixels = 35;
        const int NoticeTopReferencePixels = 733;
        const int NoticeHeightReferencePixels = 49;
        const int SpecificationTextLeftReferencePixels = 785;
        const int SpecificationNameTopReferencePixels = 72;
        const int SpecificationTextWidthReferencePixels = 575;
        const int SpecificationNameHeightReferencePixels = 35;
        const int SpecificationDetailsTopReferencePixels = 115;
        const int SpecificationDetailsHeightReferencePixels = 78;
        const int PaintLabelTopReferencePixels = 348;
        const int StyleLabelWidthReferencePixels = 600;
        const int StyleLabelHeightReferencePixels = 25;
        const int WheelLabelTopReferencePixels = 458;
        const int BodyLabelTopReferencePixels = 528;
        const int FinishLabelTopReferencePixels = 598;
        const int DealerHintTopReferencePixels = 707;
        const int DealerHintHeightReferencePixels = 24;
        const int ConfirmationTopReferencePixels = 788;
        const int TwoColumnButtonWidthReferencePixels = 295;
        const int SecondColumnButtonLeftReferencePixels = 375;
        const int FooterActionTopReferencePixels = 845;
        const int BackButtonWidthReferencePixels = 180;
        const int TrackActionLeftReferencePixels = 370;
        const int TrackActionWidthReferencePixels = 300;
        const int RotateLeftButtonLeftReferencePixels = 860;
        const int PreviewRotationTopReferencePixels = 752;
        const int RotationButtonWidthReferencePixels = 175;
        const int RotateRightButtonLeftReferencePixels = 1050;
        const int OwnedActionsTopReferencePixels = 677;
        const int StyleApplyTopReferencePixels = 681;
        const int DealerActionsTopReferencePixels = 660;
        const int UpgradeRowsTopReferencePixels = 350;
        const int UpgradeRowStepReferencePixels = 59;
        const int DealerRowsTopReferencePixels = 350;
        const int DealerRowStepReferencePixels = 76;
        const int UpgradeLabelWidthReferencePixels = 390;
        const int UpgradeTitleHeightReferencePixels = 27;
        const int UpgradeEffectOffsetReferencePixels = 28;
        const int UpgradeEffectHeightReferencePixels = 25;
        const int UpgradeFitLeftReferencePixels = 475;
        const int UpgradeFitWidthReferencePixels = 195;
        const int DealerDescriptionLeftReferencePixels = 75;
        const int DealerDescriptionOffsetReferencePixels = 45;
        const int DealerDescriptionWidthReferencePixels = 590;
        const int DealerDescriptionHeightReferencePixels = 40;
        const int DefaultButtonHeightReferencePixels = 38;
        const int CompactButtonHeightReferencePixels = 35;
        const int LabelFontReferencePixels = 17;
        const int HeadingFontReferencePixels = 24;
        const int DetailFontReferencePixels = 13;
        const int ButtonFontReferencePixels = 16;
        const int TabFontReferencePixels = 14;
        const int NavigationFontReferencePixels = 11;
        const int TabHorizontalPaddingReferencePixels = 4;
        const float DetailRedFraction = .7f;
        const float DetailGreenFraction = .77f;
        const float DetailBlueFraction = .8f;
        // Page order is the keyboard/left-stick-button contract; no persisted enum is changed.
        const int OwnedCarsPage = 0;
        const int UpgradesPage = 1;
        const int StylingPage = 2;
        const int DealershipPage = 3;
        const int PageCount = 5;
        const int UnprocessedInputFrame = -1;
        const int UpgradePartCount = 6;
        const int ColoursPerPaletteRow = 4;
        const int SingleSeatModelIndex = 3;
        // Garage-local positions in metres. Rest height comes from shared VehicleDimensions
        // and catalogue mass; the existing 5mm display offset is not surveyed ground clearance.
        const int ShowroomLocalXMetres = 8;
        const float ShowroomRestOffsetMetres = .005f;
        const int SoftboxCount = 2;
        const int KeyLightLocalXMetres = 3;
        const int KeyLightLocalYMetres = 4;
        const int KeyLightLocalZMetres = 3;
        const int FillLightLocalXMetres = -2;
        const int FillLightLocalYMetres = 2;
        const int FillLightLocalZMetres = -3;
        const int SoftboxRangeMetres = 12;
        // Existing Light.intensity pipeline calibration, not a claimed measured luminance.
        const int KeyLightIntensity = 35;
        const int FillLightIntensity = 18;
        const float SoftboxRedFraction = .9f;
        const float SoftboxGreenFraction = .94f;
        const int SoftboxBlueFraction = 1;
        const int PreviewRotationStepDegrees = 30;
        // Existing approximate specification readout only; this is not a new drive-force rule.
        const float DisplayKilonewtonsPerPowerUnit = 8.6f;

        // One draft editor per menu. Reset on context/active-car/fit changes so an old engineer
        // suggestion or setup edit cannot silently become the next owned car's draft.
        readonly RaceSetupMenu raceSetupMenu = new RaceSetupMenu();
        int selectedPage, previewModel, previewPaint, previewWheels, previewBodyKit, previewFinish;
        string selectedOwnedCarId = "", pendingTransactionLabel = "";
        Vector2 ownedScrollPosition;
        // A queued factory, not an already-applied transaction. Confirmation supplies CURRENT
        // garage/credits to GameSession; cancel/reset discards it without spending or saving.
        Func<GarageState, int, GarageTransaction> pendingTransactionFactory;
        // Menu-owned preview root and child lights; replaced/closed with deferred Object.Destroy.
        // shownCarSnapshot is a deep clone, so preview dents/style never alias the garage object.
        GameObject previewRoot;
        OwnedCar shownCarSnapshot;
        // Private name retained: RimStyleNativeVerification injects this through reflection.
        GameSession session;
        // Lazy managed style copies borrow GUI.skin templates at creation and survive Close/Open.
        // The private toolbar name is also reflected by RimStyleNativeVerification; do not rename
        // it without a coordinated test migration. No font/texture assets are disposed here.
        GUIStyle labelStyle, headingStyle, detailStyle, buttonStyle, toolbar, navigationStyle;
        bool isOpen;
        int lastNavigationInputFrame = UnprocessedInputFrame;
        // Private name preserved for the existing native rim-label verifier. Allocate once,
        // not during every Layout/Repaint, retaining the original skin-lifetime behavior.
        void Styles()
        {
            if (labelStyle != null)
                return;
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = LabelFontReferencePixels,
                wordWrap = true
            };
            labelStyle.normal.textColor = Color.white;
            headingStyle = new GUIStyle(labelStyle)
            {
                fontSize = HeadingFontReferencePixels,
                fontStyle = FontStyle.Bold
            };
            detailStyle = new GUIStyle(labelStyle)
            {
                fontSize = DetailFontReferencePixels
            };
            detailStyle.normal.textColor = new Color(DetailRedFraction, DetailGreenFraction, DetailBlueFraction);
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = ButtonFontReferencePixels
            };
            toolbar = new GUIStyle(GUI.skin.button)
            {
                fontSize = TabFontReferencePixels,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            toolbar.padding.left = toolbar.padding.right = TabHorizontalPaddingReferencePixels;
            navigationStyle = new GUIStyle(detailStyle)
            {
                fontSize = NavigationFontReferencePixels,
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
        }

        void DrawLabel(float leftReferencePixels,
            float topReferencePixels,
            float widthReferencePixels,
            float heightReferencePixels,
            string value,
            GUIStyle textStyle) => GUI.Label(new Rect(leftReferencePixels,
            topReferencePixels,
            widthReferencePixels,
            heightReferencePixels),
            value,
            textStyle);
        bool DrawButton(float leftReferencePixels,
            float topReferencePixels,
            float widthReferencePixels,
            string value,
            float heightReferencePixels = DefaultButtonHeightReferencePixels) => ControllerMenuGUI.Button(new Rect(leftReferencePixels,
            topReferencePixels,
            widthReferencePixels,
            heightReferencePixels),
            value,
            buttonStyle);
        /// <summary>
        /// Bind the initialized session, hide the driven visual, reset yaw/page/input frame and
        /// rebuild from the active owned car. Caller owns the camera/menu pause policy; this method
        /// neither applies the preview nor changes garage ownership. Native presentation behavior
        /// depends on CarVisual.SetPresentationHidden, which is outside this readability revision.
        /// </summary>
        public void Open(GameSession s)
        {
            session = s;
            isOpen = true;
            session.View.PreviewYaw = 0;
            session.Car.Visual.SetPresentationHidden(true);
            selectedPage = OwnedCarsPage;
            lastNavigationInputFrame = UnprocessedInputFrame;
            ResetFromActiveOwnedCar();
        }

        // Used after page/context changes and successful selects/fits/repairs/purchases. Re-read
        // the authoritative owned car, discard pending confirmation, and invalidate setup/advice.
        // A successful RaceSetupMenu fit can invoke this callback while its Draw is still running.
        void ResetFromActiveOwnedCar()
        {
            var activeOwnedCar = session.Profile.garage.Active;
            selectedOwnedCarId = activeOwnedCar.id;
            previewPaint = activeOwnedCar.paint;
            previewWheels = activeOwnedCar.wheels;
            previewBodyKit = activeOwnedCar.bodyKit;
            previewFinish = activeOwnedCar.finish;
            previewModel = activeOwnedCar.model;
            RebuildPreview(activeOwnedCar);
            pendingTransactionFactory = null;
            pendingTransactionLabel = "";
            raceSetupMenu.ResetDraft();
        }

        // sourceBuild may be an owned clone, a styling draft or an unowned dealership build DTO.
        // Deep-clone before visualization. The preview is not an ownership or purchase operation.
        // Retain deferred destruction/order and bind camera/streaming observation to the new root;
        // do not replace this with DestroyImmediate or alter terrain/cache behavior here.
        void RebuildPreview(OwnedCar sourceBuild)
        {
            if (previewRoot)
            {
                var previousPreviewRoot = previewRoot;
                previewRoot = null;
                UnityEngine.Object.Destroy(previousPreviewRoot);
            }

            shownCarSnapshot = sourceBuild.Clone();
            previewRoot = new GameObject("Garage showroom preview");
            previewRoot.transform.SetPositionAndRotation(session.World.Garage.TransformPoint(new Vector3(ShowroomLocalXMetres,
                Core.VehicleDimensions.ForModel(sourceBuild.model).RestRootHeight(Core.GarageRules.Catalogue[sourceBuild.model].Mass) + ShowroomRestOffsetMetres,
                0)),
                session.World.Garage.rotation);
            var previewVisual = previewRoot.AddComponent<CarVisual>();
            previewVisual.Build(CarColours.Paint[shownCarSnapshot.paint], true, shownCarSnapshot.model, shownCarSnapshot.wheels, shownCarSnapshot.bodyKit, shownCarSnapshot.finish);previewVisual.ApplyPaintJob(shownCarSnapshot);
            previewVisual.RestoreDents(shownCarSnapshot.dents);
            // A single-seat preview uses the cloned owned tyre compound; do not substitute the
            // default compound or mutate the physically driven car while presenting this clone.
            if (shownCarSnapshot.model == SingleSeatModelIndex)
                previewVisual.SetRaceCompound(shownCarSnapshot.raceTyres.compound);
            previewVisual.SetAutomaticLights(false);
            for (int lightIndex = 0; lightIndex < SoftboxCount; lightIndex++)
            {
                var softboxObject = new GameObject("Showroom softbox");
                softboxObject.transform.SetParent(previewRoot.transform, false);
                softboxObject.transform.localPosition = lightIndex == 0 ? new Vector3(KeyLightLocalXMetres,
                    KeyLightLocalYMetres,
                    KeyLightLocalZMetres) : new Vector3(FillLightLocalXMetres,
                    FillLightLocalYMetres,
                    FillLightLocalZMetres);
                var previewLight = softboxObject.AddComponent<Light>();
                previewLight.type = LightType.Point;
                previewLight.range = SoftboxRangeMetres;
                previewLight.intensity = lightIndex == 0 ? KeyLightIntensity : FillLightIntensity;
                previewLight.color = new Color(SoftboxRedFraction, SoftboxGreenFraction, SoftboxBlueFraction);
            }

            session.View.Preview = previewRoot.transform;
            if (session.World.GeographicWorld)
            {
                session.World.GeographicWorld.Observer = previewRoot.transform;
                session.World.GeographicWorld.Refresh(previewRoot.transform.position, true);
            }
        }

        /// <summary>
        /// Idempotently request preview destruction, restore the driven visual/camera observer
        /// when the Unity session survives, and clear the preview handle. Pending/style/draft caches
        /// otherwise remain until the next Open/reset, preserving existing lifetime semantics.
        /// Actual deferred destruction/visibility dispatch still requires engine verification.
        /// </summary>
        public void Close()
        {
            if (!isOpen)
                return;
            isOpen = false;
            if (previewRoot)
                UnityEngine.Object.Destroy(previewRoot);
            previewRoot = null;
            if (session)
            {
                if (session.Car && session.Car.Visual)
                    session.Car.Visual.SetPresentationHidden(false);
                if (session.View)
                    session.View.Preview = null;
                if (session.World && session.World.GeographicWorld && session.Car)
                    session.World.GeographicWorld.Observer = session.Car.transform;
            }
        }

        // Alpha1..Alpha5 select pages on KeyDown and consume recognized events. Left-stick-click
        // advances one page at most once per Layout frame (Time.frameCount), then resets drafts.
        // This is discrete edge navigation, not held-axis repeat or a per-second input scale.
        void ProcessPageNavigation()
        {
            var guiEvent = Event.current;
            if (guiEvent.type == EventType.KeyDown)
            {
                int targetPage = (int)guiEvent.keyCode - (int)KeyCode.Alpha1;
                if (targetPage >= 0 && targetPage < PageCount)
                {
                    if (targetPage != selectedPage)
                    {
                        selectedPage = targetPage;
                        ResetFromActiveOwnedCar();
                    }

                    guiEvent.Use();
                }
            }

            if (guiEvent.type == EventType.Layout && lastNavigationInputFrame != Time.frameCount)
            {
                lastNavigationInputFrame = Time.frameCount;
                if (!ControllerMenuGUI.Enabled && Gamepad.current?.leftStickButton.wasPressedThisFrame == true)
                {
                    selectedPage = (selectedPage + 1) % PageCount;
                    ResetFromActiveOwnedCar();
                }
            }
        }

        /// <summary>
        /// Draw within the parent's current reference GUI transform. Preview/quote actions stay
        /// local; CONFIRM clears the queued factory before GameSession.GarageChange and resets only
        /// on success. back remains the unchanged owner callback, invoked after Close on BACK.
        /// No new money, rental, capability or profile-write pathway is introduced.
        /// </summary>
        public void ShowDealership(){selectedPage=DealershipPage;ResetFromActiveOwnedCar();}
        public void Draw(Action back)
        {
            Styles();
            ProcessPageNavigation();
            GUI.Box(new Rect(GaragePanelLeftReferencePixels, GaragePanelTopReferencePixels, GaragePanelWidthReferencePixels, GaragePanelHeightReferencePixels), GUIContent.none);
            int nextPage = ControllerMenuGUI.Toolbar(new Rect(PanelContentLeftReferencePixels,
                PageTabsTopReferencePixels,
                WideContentWidthReferencePixels,
                PageTabsHeightReferencePixels),
                selectedPage,
                new[] { "1 OWNED CARS",
                "2 UPGRADES",
                "3 STYLING",
                "4 DEALERSHIP",
                "5 RACE SETUP" },
                toolbar);
            if (nextPage != selectedPage)
            {
                selectedPage = nextPage;
                ResetFromActiveOwnedCar();
            }

            DrawLabel(PanelContentLeftReferencePixels,
                PageHintTopReferencePixels,
                WideContentWidthReferencePixels,
                PageHintHeightReferencePixels,
                "1–5: garage pages · controller: click left stick to change page",
                navigationStyle);
            DrawLabel(PanelContentLeftReferencePixels,
                CreditsTopReferencePixels,
                WideContentWidthReferencePixels,
                CreditsHeightReferencePixels,
                $"{session.Economy.Credits:N0} CREDITS  ·  {session.Profile.garage.cars.Count}/8 CARS",
                headingStyle);
            if (selectedPage == OwnedCarsPage)
                DrawOwnedCars();
            else if (selectedPage == UpgradesPage)
                DrawUpgrades();
            else if (selectedPage == StylingPage)
                Styling();
            else if (selectedPage == DealershipPage)
                DrawDealership();
            else
                raceSetupMenu.Draw(session, ResetFromActiveOwnedCar);
            DrawLabel(PanelContentLeftReferencePixels,
                NoticeTopReferencePixels,
                WideContentWidthReferencePixels,
                NoticeHeightReferencePixels,
                SinglePlayerBeta.Enabled && selectedPage==OwnedCarsPage && !string.IsNullOrEmpty(session.VehicleSwapUnavailableReason) ? session.VehicleSwapUnavailableReason : pendingTransactionFactory != null ? pendingTransactionLabel : string.IsNullOrEmpty(session.Notice) ? (SinglePlayerBeta.Enabled&&!session.World.IsCircuit
                    ?"Swap owned cars while stopped in free drive. Stop at HARBOR AUTO to fit parts or buy cars."
                    :"Changes use earned game credits. Stop at HARBOR AUTO to fit or buy.") : session.Notice,
                detailStyle);
            if (pendingTransactionFactory != null)
            {
                if (DrawButton(PanelContentLeftReferencePixels, ConfirmationTopReferencePixels, TwoColumnButtonWidthReferencePixels, "CONFIRM"))
                {
                    var transactionFactoryToCommit = pendingTransactionFactory;
                    pendingTransactionFactory = null;
                    if (session.GarageChange(transactionFactoryToCommit))
                        ResetFromActiveOwnedCar();
                }

                if (DrawButton(SecondColumnButtonLeftReferencePixels, ConfirmationTopReferencePixels, TwoColumnButtonWidthReferencePixels, "CANCEL"))
                {
                    pendingTransactionFactory = null;
                    pendingTransactionLabel = "";
                }
            }

            if (DrawButton(PanelContentLeftReferencePixels, FooterActionTopReferencePixels, BackButtonWidthReferencePixels, "BACK", CompactButtonHeightReferencePixels))
            {
                Close();
                back();
            }

            if (session.Racing && session.Racing.Competitive)
            {
                if (DrawButton(TrackActionLeftReferencePixels,
                    FooterActionTopReferencePixels,
                    TrackActionWidthReferencePixels,
                    "END TRACK SESSION",
                    CompactButtonHeightReferencePixels))
                {
                    session.Racing.Cancel();
                    session.Notify("Track session ended · unfinished lap discarded");
                }
            }
            else if (!session.AtGarage && 
                DrawButton(TrackActionLeftReferencePixels,
                FooterActionTopReferencePixels,
                TrackActionWidthReferencePixels,
                "TOW TO HARBOR AUTO",
                CompactButtonHeightReferencePixels))
                session.TowToGarage();
            if (shownCarSnapshot != null)
            {
                if (DrawButton(RotateLeftButtonLeftReferencePixels,
                    PreviewRotationTopReferencePixels,
                    RotationButtonWidthReferencePixels,
                    "ROTATE LEFT",
                    CompactButtonHeightReferencePixels))
                    session.View.PreviewYaw -= PreviewRotationStepDegrees;
                if (DrawButton(RotateRightButtonLeftReferencePixels,
                    PreviewRotationTopReferencePixels,
                    RotationButtonWidthReferencePixels,
                    "ROTATE RIGHT",
                    CompactButtonHeightReferencePixels))
                    session.View.PreviewYaw += PreviewRotationStepDegrees;
                GUI.Box(new Rect(SpecificationPanelLeftReferencePixels,
                    SpecificationPanelTopReferencePixels,
                    SpecificationPanelWidthReferencePixels,
                    SpecificationPanelHeightReferencePixels),
                    GUIContent.none);
                var shownSpecification = GarageRules.Catalogue[shownCarSnapshot.model];
                DrawLabel(SpecificationTextLeftReferencePixels,
                    SpecificationNameTopReferencePixels,
                    SpecificationTextWidthReferencePixels,
                    SpecificationNameHeightReferencePixels,
                    shownSpecification.Name,
                    headingStyle);
                DrawLabel(SpecificationTextLeftReferencePixels,
                    SpecificationDetailsTopReferencePixels,
                    SpecificationTextWidthReferencePixels,
                    SpecificationDetailsHeightReferencePixels,
                    $"{(shownCarSnapshot.model >= ImportedVehicleCatalogue.BuiltInCount ? "IMPORTED CAR" : "REAR-WHEEL DRIVE")} · {shownSpecification.Mass:F0} kg\nDrive force {GarageRules.Power(shownCarSnapshot) * DisplayKilonewtonsPerPowerUnit:F1} kN · grip ×{GarageRules.Grip(shownCarSnapshot):F2}\nGearing limit {GarageRules.Speed(shownCarSnapshot) * Core.DrivingMath.MphPerMetreSecond:F0} mph · condition {100 - shownCarSnapshot.damage:F0}%",
                    detailStyle);
            }
        }

        // Beta free-drive rows switch directly through the existing owned-ID save transaction.
        // Purchases and upgrades retain their separate confirmation flow.
        void DrawOwnedCars()
        {
            ownedScrollPosition = ControllerMenuGUI.BeginScrollView(new Rect(PanelContentLeftReferencePixels,
                OwnedListTopReferencePixels,
                WideContentWidthReferencePixels,
                OwnedViewportHeightReferencePixels),
                ownedScrollPosition,
                new Rect(ContentOriginXReferencePixels,
                ContentOriginYReferencePixels,
                OwnedScrollBodyWidthReferencePixels,
                session.Profile.garage.cars.Count * OwnedRowStepReferencePixels));
            int rowIndex = 0;
            foreach (var ownedCar in session.Profile.garage.cars)
            {
                string displayName = ImportedVehicleCatalogue.DisplayName(ownedCar) + (ownedCar.id == session.Profile.garage.activeId ? "  · ACTIVE" : "") + $"  · {100 - ownedCar.damage:F0}%";
                if (ControllerMenuGUI.Button(new Rect(OwnedRowLeftReferencePixels,
                    rowIndex++ * OwnedRowStepReferencePixels,
                    OwnedRowWidthReferencePixels,
                    OwnedRowHeightReferencePixels),
                    displayName,
                    buttonStyle))
                {
                    selectedOwnedCarId = ownedCar.id;
                    RebuildPreview(ownedCar);
                    pendingTransactionFactory = null;
                    if (SinglePlayerBeta.Enabled && !session.World.IsCircuit && session.SelectOwnedVehicle(ownedCar.id))
                        ResetFromActiveOwnedCar();
                }
            }

            ControllerMenuGUI.EndScrollView();
            bool betaRoadSwap=SinglePlayerBeta.Enabled&&!session.World.IsCircuit;
            if (DrawButton(PanelContentLeftReferencePixels, OwnedActionsTopReferencePixels, TwoColumnButtonWidthReferencePixels,
                betaRoadSwap?(session.Wanted>0?"LOCKED · POLICE PURSUIT":"SWAP TO THIS CAR · FREE"):"SELECT PREVIEWED CAR"))
            {
                string selectedOwnedId = selectedOwnedCarId;
                if(betaRoadSwap)
                {
                    pendingTransactionFactory=null;
                    if(session.SelectOwnedVehicle(selectedOwnedId))ResetFromActiveOwnedCar();
                }
                else
                {
                    pendingTransactionFactory = (garageState, credits) => GarageRules.Select(garageState, credits, selectedOwnedId);
                    pendingTransactionLabel = "Select this owned car · no charge";
                }
            }

            if (DrawButton(SecondColumnButtonLeftReferencePixels, OwnedActionsTopReferencePixels, TwoColumnButtonWidthReferencePixels, "FREE REPAIR / PIT SERVICE"))
            {
                session.Repair();
                ResetFromActiveOwnedCar();
            }
        }

        // Display the existing six-part catalogue effects/prices. GUI.enabled is restored exactly
        // as before; capture the part index, not the loop variable, for the queued Core operation.
        void DrawUpgrades()
        {
            var activeOwnedCar = session.Profile.garage.Active;
            string[] effects =
            {
                "+13% engine force per level",
                "+6% gearing limit and improved delivery",
                "+12% tyre grip per level",
                "+17% braking force per level",
                "Firmer springs and controlled damping",
                "Better rear traction reserve under power"
            };
            for (int partIndex = 0; partIndex < UpgradePartCount; partIndex++)
            {
                float rowTopReferencePixels = UpgradeRowsTopReferencePixels + partIndex * UpgradeRowStepReferencePixels;
                int selectedPart = partIndex;
                int upgradePriceCredits = GarageRules.UpgradePrice(activeOwnedCar, partIndex);
                DrawLabel(PanelContentLeftReferencePixels,
                    rowTopReferencePixels,
                    UpgradeLabelWidthReferencePixels,
                    UpgradeTitleHeightReferencePixels,
                    GarageRules.Parts[partIndex] + $"   {activeOwnedCar.Level(partIndex)}/3",
                    labelStyle);
                DrawLabel(PanelContentLeftReferencePixels,
                    rowTopReferencePixels + UpgradeEffectOffsetReferencePixels,
                    UpgradeLabelWidthReferencePixels,
                    UpgradeEffectHeightReferencePixels,
                    effects[partIndex],
                    detailStyle);
                bool previousGuiEnabled = GUI.enabled;
                GUI.enabled = upgradePriceCredits > 0;
                if (DrawButton(UpgradeFitLeftReferencePixels,
                    rowTopReferencePixels,
                    UpgradeFitWidthReferencePixels,
                    upgradePriceCredits > 0 ? "FIT · " + upgradePriceCredits : "MAX LEVEL"))
                {
                    pendingTransactionFactory = (garageState, credits) => GarageRules.Upgrade(garageState, credits, selectedPart);
                    pendingTransactionLabel = $"{GarageRules.Parts[selectedPart]} level {activeOwnedCar.Level(selectedPart) + 1} · {upgradePriceCredits} credits";
                }

                GUI.enabled = previousGuiEnabled;
            }
        }

        // Private name preserved for RimStyleNativeVerification. Changes rebuild a cloned visual
        // only. Quote/captured selections remain drafts until confirmation; no style price changes.
        void Styling()
        {
            var active=session.Profile.garage.Active;
            if(active.model>=ImportedVehicleCatalogue.BuiltInCount&&!ImportedVehicleCatalogue.Get(active.model).Settings.SupportsPaint)
            {
                DrawLabel(PanelContentLeftReferencePixels,PaintLabelTopReferencePixels,StyleContentWidthReferencePixels,120,
                    "This pack has a fixed livery. Its original colours and finish cannot be changed; no styling charge applies.",labelStyle);
                return;
            }
            bool appearanceChanged = false;
            DrawLabel(PanelContentLeftReferencePixels, PaintLabelTopReferencePixels, StyleLabelWidthReferencePixels, StyleLabelHeightReferencePixels, "PAINT", labelStyle);
            int firstPaletteSelection = previewPaint < ColoursPerPaletteRow ? previewPaint : -1;
            int firstPaletteResult = ControllerMenuGUI.Toolbar(new Rect(PanelContentLeftReferencePixels,
                FirstPaintPaletteTopReferencePixels,
                StyleContentWidthReferencePixels,
                StyleToolbarHeightReferencePixels),
                firstPaletteSelection,
                new[] { "TEAL",
                "RED",
                "BLUE",
                "YELLOW" });
            if (firstPaletteResult >= 0 && firstPaletteResult != firstPaletteSelection)
            {
                previewPaint = firstPaletteResult;
                appearanceChanged = true;
            }

            int secondPaletteSelection = previewPaint >= ColoursPerPaletteRow ? previewPaint - ColoursPerPaletteRow : -1;
            int secondPaletteResult = ControllerMenuGUI.Toolbar(new Rect(PanelContentLeftReferencePixels,
                SecondPaintPaletteTopReferencePixels,
                StyleContentWidthReferencePixels,
                StyleToolbarHeightReferencePixels),
                secondPaletteSelection,
                new[] { "SILVER",
                "BLACK",
                "GREEN",
                "WHITE" });
            if (secondPaletteResult >= 0 && secondPaletteResult != secondPaletteSelection)
            {
                previewPaint = secondPaletteResult + ColoursPerPaletteRow;
                appearanceChanged = true;
            }

            bool styleEnabled = GUI.enabled;
            GUI.enabled = styleEnabled && session.Profile.garage.Active.model < ImportedVehicleCatalogue.BuiltInCount;
            DrawLabel(PanelContentLeftReferencePixels, WheelLabelTopReferencePixels, StyleLabelWidthReferencePixels, StyleLabelHeightReferencePixels, "WHEELS", labelStyle);
            int nextWheelStyle = ControllerMenuGUI.Toolbar(new Rect(PanelContentLeftReferencePixels,
                WheelToolbarTopReferencePixels,
                StyleContentWidthReferencePixels,
                StyleToolbarHeightReferencePixels),
                previewWheels,
                session.Profile.garage.Active.model == SingleSeatModelIndex ? new[] { "SILVER RIMS",
                "DARK RIMS",
                "GOLD RIMS" } : new[] { "CLASSIC",
                "FIVE SPOKE",
                "MULTI SPOKE" });
            if (nextWheelStyle != previewWheels)
            {
                previewWheels = nextWheelStyle;
                appearanceChanged = true;
            }

            DrawLabel(PanelContentLeftReferencePixels, BodyLabelTopReferencePixels, StyleLabelWidthReferencePixels, StyleLabelHeightReferencePixels, "BODY STYLE", labelStyle);
            int nextBodyStyle = ControllerMenuGUI.Toolbar(new Rect(PanelContentLeftReferencePixels,
                BodyToolbarTopReferencePixels,
                StyleContentWidthReferencePixels,
                StyleToolbarHeightReferencePixels),
                previewBodyKit,
                session.Profile.garage.Active.model == SingleSeatModelIndex ? new[] { "STANDARD",
                "TALL ENDPLATES",
                "LOW ENDPLATES" } : new[] { "FACTORY",
                "REAR WING",
                "SPORT SILLS" });
            if (nextBodyStyle != previewBodyKit)
            {
                previewBodyKit = nextBodyStyle;
                appearanceChanged = true;
            }

            GUI.enabled = styleEnabled;
            DrawLabel(PanelContentLeftReferencePixels, FinishLabelTopReferencePixels, StyleLabelWidthReferencePixels, StyleLabelHeightReferencePixels, "FINISH", labelStyle);
            int nextFinishStyle = ControllerMenuGUI.Toolbar(new Rect(PanelContentLeftReferencePixels,
                FinishToolbarTopReferencePixels,
                StyleContentWidthReferencePixels,
                StyleToolbarHeightReferencePixels),
                previewFinish,
                new[] { "METALLIC",
                "MATTE",
                "GLOSS" });
            if (nextFinishStyle != previewFinish)
            {
                previewFinish = nextFinishStyle;
                appearanceChanged = true;
            }

            if (appearanceChanged)
            {
                var previewedOwnedBuild = session.Profile.garage.Active.Clone();
                previewedOwnedBuild.paint = previewPaint;
                previewedOwnedBuild.wheels = previewWheels;
                previewedOwnedBuild.bodyKit = previewBodyKit;
                previewedOwnedBuild.finish = previewFinish;
                RebuildPreview(previewedOwnedBuild);
                pendingTransactionFactory = null;
            }

            if (DrawButton(PanelContentLeftReferencePixels, StyleApplyTopReferencePixels, StyleContentWidthReferencePixels, "APPLY PREVIEWED APPEARANCE"))
            {
                int selectedPaint = previewPaint, selectedWheels = previewWheels, selectedBodyKit = previewBodyKit, selectedFinish = previewFinish;
                var appearanceQuote = GarageRules.Style(session.Profile.garage, session.Economy.Credits, selectedPaint, selectedWheels, selectedBodyKit, selectedFinish);
                pendingTransactionFactory = (garageState, credits) => GarageRules.Style(garageState, credits, selectedPaint, selectedWheels, selectedBodyKit, selectedFinish);
                pendingTransactionLabel = appearanceQuote == null ? "Check credits or choose a changed appearance" : $"{CarColours.Names[selectedPaint]} · wheel style {selectedWheels + 1} · body style {selectedBodyKit + 1} · {session.Economy.Credits - appearanceQuote.Credits} credits";
            }
        }

        // Catalogue previews are temporary build DTOs. BUY/TRADE retain separate existing Core
        // factories, ownership/capacity/pricing rules and captured model indexes; no hire insertion.
        void DrawDealership()
        {
            for (int catalogueIndex = 0; catalogueIndex < ImportedVehicleCatalogue.BuiltInCount; catalogueIndex++)
            {
                var catalogueSpecification = GarageRules.Catalogue[catalogueIndex];
                int selectedModelIndex = catalogueIndex;
                float rowTopReferencePixels = DealerRowsTopReferencePixels + catalogueIndex * DealerRowStepReferencePixels;
                if (DrawButton(PanelContentLeftReferencePixels,
                    rowTopReferencePixels,
                    StyleContentWidthReferencePixels,
                    catalogueSpecification.Name + "  ·  " + catalogueSpecification.Price.ToString("N0") + " credits"))
                {
                    previewModel = selectedModelIndex;
                    RebuildPreview(new OwnedCar { model = selectedModelIndex, paint = selectedModelIndex % 8 });
                    pendingTransactionFactory = null;
                }

                DrawLabel(DealerDescriptionLeftReferencePixels,
                    rowTopReferencePixels + DealerDescriptionOffsetReferencePixels,
                    DealerDescriptionWidthReferencePixels,
                    DealerDescriptionHeightReferencePixels,
                    $"{catalogueSpecification.Mass:F0} kg · rear-wheel drive · {catalogueSpecification.Power * DisplayKilonewtonsPerPowerUnit:F1} kN drive force · base grip ×{catalogueSpecification.Grip:F2}",
                    detailStyle);
            }

            if (DrawButton(PanelContentLeftReferencePixels, DealerActionsTopReferencePixels, TwoColumnButtonWidthReferencePixels, "BUY · KEEP CURRENT CAR"))
            {
                int purchaseModelIndex = previewModel;
                pendingTransactionFactory = (garageState, credits) => GarageRules.Buy(garageState, credits, purchaseModelIndex, false);
                pendingTransactionLabel = $"Buy {GarageRules.Catalogue[purchaseModelIndex].Name} · {GarageRules.Catalogue[purchaseModelIndex].Price:N0} credits · keep your collection";
            }

            if (DrawButton(SecondColumnButtonLeftReferencePixels, DealerActionsTopReferencePixels, TwoColumnButtonWidthReferencePixels, "TRADE ACTIVE CAR"))
            {
                int purchaseModelIndex = previewModel;
                int tradeCredit = GarageRules.TradeValue(session.Profile.garage.Active);
                pendingTransactionFactory = (garageState, credits) => GarageRules.Buy(garageState, credits, purchaseModelIndex, true);
                pendingTransactionLabel = $"Exchange {GarageRules.Catalogue[session.Profile.garage.Active.model].Name}: trade credit {tradeCredit:N0}; net cost {GarageRules.Catalogue[purchaseModelIndex].Price - tradeCredit:N0}";
            }

            DrawLabel(PanelContentLeftReferencePixels,
                DealerHintTopReferencePixels,
                StyleContentWidthReferencePixels,
                DealerHintHeightReferencePixels,
                "Click a model to preview it before buying or trading.",
                detailStyle);
        }
    }
}
