using UnityEngine;
using UnityEngine.InputSystem;

namespace EarthUnscripted
{
    public sealed partial class DrivingUI : MonoBehaviour
    {
        public GameSession Session;
        public bool MinimapReady => movingMap.Ready;
        GUIStyle title,heading,body,small,button,big,toggle,compactSpeed;
        Texture2D white,geoMiniMap;
        readonly DrivingMinimap movingMap = new DrivingMinimap();
        readonly DrivingNavigation navigation = new DrivingNavigation();
        readonly ControllerMenuNavigation controllerMenus = new ControllerMenuNavigation();
        internal ControllerMenuNavigation ControllerMenus => controllerMenus;
        internal int MenuTab => tab;
        internal DrivingNavigation Navigation=>navigation;
        public void ShowNavigationMap(){if(Session && Session.BootReady && !Session.World.IsCircuit)navigation.OpenMap(Session);}
        int tab,selection,circuitChoice,trackMode=1,lapChoice,difficulty=1;
        GeoRegion circuitPreview;Texture2D circuitMap;
        readonly CircuitHireMenu circuitHire=new CircuitHireMenu();
        readonly DataCreditsMenu dataCredits=new DataCreditsMenu();
        readonly VehiclePackMenu vehiclePacks = new VehiclePackMenu();
        GarageMenu garageMenu;WorldMapMenu worldMap;SceneryPackMenu sceneryPacks;WorldPackMenu importedPacks;CircuitPackMenu circuitPacks;int importedReturnTab=3;
        public SceneryPackMenu SceneryPacks=>sceneryPacks;
        public WorldPackMenu ImportedPacks=>importedPacks;
        public CircuitPackMenu ImportedCircuits=>circuitPacks;
        public void ShowCircuitHire(){if(Session.World.IsCircuit)tab=12;}
        public void ShowDataCredits(){tab=13;}
        int careerSlot;string careerName="",careerPending="";PlayerProfile[] careerSlots;
        string bindingMessage="",hostAddress="127.0.0.1";
        readonly Color ink=InterfaceTheme.Navy,muted=InterfaceTheme.Muted,accent=InterfaceTheme.Gold;
        void Styles()
        {
            if(title!=null)return;white=Texture2D.whiteTexture;
            title=new GUIStyle(GUI.skin.label){font=InterfaceTheme.Font,fontSize=38,fontStyle=FontStyle.Bold};title.normal.textColor=Color.white;
            heading=new GUIStyle(title){fontSize=23};body=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true};body.normal.textColor=Color.white;
            small=new GUIStyle(body){fontSize=13};small.normal.textColor=muted;
            big=new GUIStyle(title){fontSize=68,alignment=TextAnchor.MiddleRight};compactSpeed=new GUIStyle(big){fontSize=56};
            button=new GUIStyle(GUI.skin.button){fontSize=18,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(20,10,7,7)};
            toggle=new GUIStyle(GUI.skin.toggle){fontSize=17};toggle.normal.textColor=Color.white;
        }
        // Text states remain explicit even when the platform checkbox marker is faint.
        static bool StateToggle(Rect bounds,bool selected,string label,GUIStyle style)
        {
            return InterfaceTheme.CheckBox(bounds,selected,label.TrimStart(),style);
        }
        void Update()
        {
            displayResolution.Update();if(tab!=32)displayResolution.Revert();
            using var timing=WorkTelemetry.Measure("frame.ui-update",null,8);
            if(!Session||!Session.BootReady)return;
            if(Session.Menu&&Session.AppUpdates&&Session.AppUpdates.PromptPending){Session.AppUpdates.DismissPrompt();tab=31;}
            navigation.Update(Session);
            if(Session.World.AutomaticWorld)Session.World.AutomaticWorld.Renderer.SetPlannedRoute(navigation.Current?.points);
            importedPacks?.Poll();circuitPacks?.Poll();vehiclePacks.Poll();
            
            
            if(Session&&tab==9)sceneryPacks?.Update(Session);
            if(Session&&Session.PreparingStart)
            {
                if(Keyboard.current?.escapeKey.wasPressedThisFrame==true||Gamepad.current?.buttonEast.wasPressedThisFrame==true)Session.CancelPreparingStart();return;
            }
            if(garageMenu!=null&&Session&&(!Session.Menu||tab!=5)){garageMenu.Close();if(!Session.Menu&&tab==5)tab=0;}
            controllerMenus.SetFeedback(Session.Feedback.MenuFeedback);
            controllerMenus.Update((Session.Menu || navigation.IsOpen) && !Session.PreparingStart && !Session.Input.Calibrating,
                () => { if(tab==32){displayResolution.Revert();tab=22;}else if (tab == 0) Session.ResumeDrive(); else if(tab==29||tab==30){Radio?.StopPreview();Radio?.Save();tab=28;}else if(tab==28){Radio?.Save();tab=21;}else tab = 0; });
        }
        void Activate(int item)
        {
            if(Session.PreparingStart)return;
            switch(item)
            {
                case 0:Session.ResumeDrive();break;
                case 1:tab=18;break;
                case 2:tab=17;break;
                case 3:tab=20;break;
                case 4:tab=3;break;
                case 5:tab=21;break;
                case 6:ShowCareers();break;
                case 7:tab=24;break;
                default:Session.Save();Application.Quit();break;
            }
        }
        public WorldMapMenu LocationMenu=>worldMap;
        public void ShowWorldMap(){tab=8;if(worldMap==null)worldMap=new WorldMapMenu();worldMap.Open(Session);}
        public void ShowSceneryPacks(){tab=9;if(sceneryPacks==null)sceneryPacks=new SceneryPackMenu();sceneryPacks.Open(Session);}
        public void ShowImportedPacks(){if(tab!=10&&tab!=11)importedReturnTab=tab==6?6:19;circuitPacks?.Dispose();circuitPacks=null;tab=10;if(importedPacks==null)importedPacks=new WorldPackMenu();importedPacks.Open(Session);}
        public void ShowImportedCircuit(Packs.WorldPackInventoryItem row){circuitPacks?.Dispose();circuitPacks=new CircuitPackMenu();circuitPacks.Open(Session,row);tab=11;}
        public void ShowCareers(){tab=7;careerSlots=new PlayerProfile[5];for(int i=0;i<5;i++)careerSlots[i]=CareerStorage.Read(i);}
        public void ShowVehiclePacks(){tab=16;}
        public void ShowSettings(){tab=21;}
        public void ShowAppUpdates(){tab=31;}
        public void ShowGarage(){if(Session && Session.IsScopedReaderSession800){Session.Notify("Scoped reader QA has no garage presentation");return;}tab=5;if(garageMenu==null)garageMenu=new GarageMenu();garageMenu.Open(Session);}
        void Panel(Rect r,Color colour){if(colour==ink)InterfaceTheme.Card(r);else InterfaceTheme.Fill(r,colour);}
        void Text(float x,float y,float w,float h,string text,GUIStyle style){GUI.Label(new Rect(x,y,w,h),text,style);}
        bool Button(float x,float y,float w,string text,float height=44)=>ControllerMenuGUI.Button(new Rect(x,y,w,height),text,button);
        void OnGUI()
        {
            using var frameWork=WorkTelemetry.Measure("frame.hud",null,8);
            if(!Session||!Session.BootReady||!Session.Car)return;
            using(WorkTelemetry.Measure("hud.styles",null,4)){InterfaceTheme.Apply();Styles();}
            if(Radio)EnsureRadioHudResources();
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1440*scale)*.5f,(Screen.height-900*scale)*.5f),Quaternion.identity,Vector3.one*scale);
            if(SinglePlayerBeta.Enabled && Session.Menu && !Session.World.IsCircuit && Session.World.Route.Region!=null){using var mapWarm=WorkTelemetry.Measure("hud.minimap.menu-warm",null,4);movingMap.Warm(Session);}
            bool previousEnabled = GUI.enabled;
            if (Session.PreparingStart) GUI.enabled = false;
            if (Session.Menu || navigation.IsOpen)
            {
                using (ControllerMenuGUI.Begin(controllerMenus, navigation.IsOpen ? "navigation-map" : "menu-" + tab + (tab == 16 ? "/" + vehiclePacks.ControllerPage : tab==10 ? "/"+importedPacks.ControllerPage : tab==11 ? "/"+circuitPacks.ControllerPage : "")))
                {
                    if (controllerMenus.Editing) controllerMenus.DrawKeyboard();
                    else if (navigation.IsOpen) navigation.DrawMap(Session);
                    else DrawMenu();
                }
                GUI.Label(new Rect(65, 880, 1310, 20), "D-pad / left stick: focus · A / Cross: select · B / Circle: back · Left/right: adjust · Right stick: scroll · Map sticks: pan, triggers: zoom", small);
            }
            else DrawHUD();
            GUI.enabled = previousEnabled;
            if(Session.PreparingStart)
            {
                BrandScreen.DrawLoading(Session.PreparingRoadFrontier?"Preparing starting roads":"Checking your starting area",Session.World.Route.Region?.name,Session.PreparingRoadFrontier?Session.PreparingStartStatus:Session.Downloads.Busy?Session.Downloads.Status:Session.Updates.Status,false);
                if(Button(98,690,360,"CANCEL AND STAY IN MENU"))Session.CancelPreparingStart();
            }
        }
        void DrawMenu()
        {
            if(tab==0){DrawHome();if(Session.Racing&&Session.Racing.Finished)DrawRaceResults();return;}
            DrawMenuHeader();
            if(tab==17)DrawCarsHub();else if(tab==18)DrawLocationsHub();else if(tab==19)DrawDataHub();else if(tab==20)DrawRacingHub();else if(tab==21)DrawSettingsHub();else if(tab==22)DrawSettings();else if(tab==23)DrawWheelSettings();else if(tab==24)DrawHelpHub();else if(tab==25)DrawPaintJobs();else if(tab==26)DrawTrainsAndTracks();
            else if(tab==32)displayResolution.Draw(Session.Profile,()=>tab=22);else if(tab==31)DrawAppUpdates();else if(tab==28)DrawRadio();else if(tab==29)DrawRadioPacks();else if(tab==30)DrawRadioReview();else if(tab==27)DrawDataSources();else if(tab==16)vehiclePacks.Draw(Session,()=>tab=17);else if(tab==15)DrawSavedLocations();else if(tab==14)DrawWorldStorage();else if(tab==1)DrawSettings();else if(tab==3)DrawWorld();else if(tab==4)DrawLan();else if(tab==5)garageMenu.Draw(()=>tab=17);else if(tab==6)DrawCircuits();else if(tab==7)DrawCareers();else if(tab==8)worldMap.Draw(Session,()=>tab=18);else if(tab==9)sceneryPacks.Draw(Session,()=>tab=19);else if(tab==10)importedPacks.Draw(()=>tab=importedReturnTab);else if(tab==11)circuitPacks.Draw(()=>ShowImportedPacks());else if(tab==12)circuitHire.Draw(Session,()=>tab=20);else if(tab==13)dataCredits.Draw(Session,()=>tab=24);else DrawControls();
            if(tab==0&&Session.Racing&&Session.Racing.Finished)DrawRaceResults();

        }
        void DrawCareers()
        {
            Panel(new Rect(35,240,1370,620),ink);Text(65,265,1000,35,"SAVE / LOAD CAREER",heading);
            Text(65,315,1220,50,"Five manual snapshots keep credits, cars, upgrades, damage, location and settings. Your current autosave stays separate; loading keeps a previous-save backup.",body);
            for(int i=0;i<5;i++){var c=careerSlots[i];string label=c==null?"EMPTY SLOT "+(i+1):c.LoadBlocked?"UNAVAILABLE SLOT "+(i+1)+" · compatible build or backup access required":$"{i+1}. {c.careerName} · {c.credits:N0} credits · {c.garage.cars.Count} cars · {c.region}";if(Button(65,390+i*55,760,(careerSlot==i?"> ":"")+label,42)){careerSlot=i;careerName=c?.careerName??"Career "+(i+1);careerPending="";}}
            Text(865,390,450,30,"Career name",body);careerName=ControllerMenuGUI.TextField(new Rect(865,430,450,40),careerName,32);
            if(Button(865,500,450,"SAVE CURRENT TO SLOT",46))careerPending="save";
            if(Button(865,565,450,"LOAD SELECTED CAREER",46))careerPending="load";
            if(careerPending!="")
            {
                Text(865,630,450,65,careerPending=="save"?"Save current career here? Any existing slot is replaced; a backup is retained.":"Load this career? Your current autosave is backed up before replacement.",body);
                if(Button(865,710,210,"CONFIRM",40)){if(careerPending=="save"){Session.SaveCareer(careerSlot,careerName);careerSlots[careerSlot]=CareerStorage.Read(careerSlot);}else Session.LoadCareer(careerSlot);careerPending="";}
                if(Button(1105,710,210,"CANCEL",40))careerPending="";
            }
            Text(65,715,750,45,Session.Notice,body);if(Button(65,790,260,"BACK"))tab=0;
        }
        float preparedGigabytes=5, finishedGigabytes=5;
        bool storageLoaded;
        string storageNotice="";
        string StoragePath=>System.IO.Path.Combine(EarthUnscripted.SinglePlayerBeta.ProductRoot,"AutomaticWorldCache","storage-settings.json");
        [System.Serializable] sealed class StorageSettings { public float preparedGigabytes=5, finishedGigabytes=5; }
        void DrawWorldStorage()
        {
            if(!storageLoaded)
            {
                storageLoaded=true;
                try
                {
                    if (System.IO.File.Exists(StoragePath))
                    {
                        string json = System.IO.File.ReadAllText(StoragePath);
                        var settings = JsonUtility.FromJson<StorageSettings>(json);
                        if (!float.IsNaN(settings.preparedGigabytes) && !float.IsInfinity(settings.preparedGigabytes)) preparedGigabytes = Mathf.Clamp(settings.preparedGigabytes, .5f, 50);
                        if (json.Contains("\"finishedGigabytes\"") && !float.IsNaN(settings.finishedGigabytes) && !float.IsInfinity(settings.finishedGigabytes)) finishedGigabytes = Mathf.Clamp(settings.finishedGigabytes, 0, 50);
                    }
                }
                catch(System.Exception){storageNotice="Could not read the saved storage limit. Choose a limit and save to retry.";}
            }
            Panel(new Rect(35,240,1370,620),ink);
            Text(65,270,1100,40,"WORLD STORAGE",heading);
            Text(65,330,1150,65,"Prepared data avoids repeat downloads. Finished geometry avoids rebuilding roads, terrain and buildings. Shared scenery assets and cached tree placements are reused.",body);
            Text(65,415,1000,40,$"Prepared-data cache: {preparedGigabytes:0.#} GB",heading);
            if(Button(65,465,180,"LESS",38))preparedGigabytes=Mathf.Max(.5f,preparedGigabytes-1);
            if(Button(270,465,180,"MORE",38))preparedGigabytes=Mathf.Min(50,preparedGigabytes+1);
            if(Button(475,465,260,"RESET TO 5 GB",38))preparedGigabytes=5;
            Text(65,530,1050,40,$"Finished roads & scenery: {finishedGigabytes:0.#} GB",heading);
            if(Button(65,580,180,"LESS",38))finishedGigabytes=Mathf.Max(0,finishedGigabytes-1);
            if(Button(270,580,180,"MORE",38))finishedGigabytes=Mathf.Min(50,finishedGigabytes+1);
            if(Button(475,580,260,"RESET TO 5 GB",38))finishedGigabytes=5;
            Text(65,640,1150,72,"Limits are separate and space is not reserved in advance. Older entries are removed first; keep 2 GB free. Set finished geometry to 0 GB to disable it. Graphics upload and collision setup still run when loading.",body);
            Text(65,725,1120,45,storageNotice,body);
            if(Button(65,800,175,"BACK",37))tab=21;
            if(Button(325,790,300,"SAVE STORAGE LIMIT"))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath));
                    string temporary=StoragePath+".partial";
                    System.IO.File.WriteAllText(temporary,JsonUtility.ToJson(new StorageSettings{preparedGigabytes=preparedGigabytes,finishedGigabytes=finishedGigabytes}));
                    if(System.IO.File.Exists(StoragePath))System.IO.File.Replace(temporary,StoragePath,null);else System.IO.File.Move(temporary,StoragePath);
                    _ = EarthUnscripted.NewbridgePrototype.FinishedGeometryStore.ApplyLimit(System.IO.Path.GetDirectoryName(StoragePath));
                    storageNotice="Saved. Cache limits apply automatically.";
                }
                catch(System.Exception){storageNotice="Could not save the storage limit. Check available storage and try again.";}
            }
        }
        readonly DisplayResolutionMenu displayResolution=new DisplayResolutionMenu();
        void DrawSettings()
        {
            Panel(new Rect(35,240,1370,620),ink);var p=Session.Profile;
            Text(65,266,600,36,"DISPLAY & AUDIO",heading);
            Text(65,315,580,25,"GRAPHICS & DISPLAY",small);
            Text(735,315,580,25,"AUDIO & CONTROLLER FEEL",small);
            int q=ControllerMenuGUI.Toolbar(new Rect(65,354,580,40),p.quality,GraphicsQualityPreset.Names);if(q!=p.quality)Session.ApplyQuality(q);
            if(GameGraphics.HighDefinition)Text(65,410,580,36,"Scene detail follows the quality preset",small);
            else
            {
                bool graphicsEnabled=GUI.enabled;GUI.enabled=graphicsEnabled&&p.quality>=2;
                int detail=ControllerMenuGUI.Toolbar(new Rect(65,410,580,36),p.clarity,new[]{"PRESET DETAIL","EXTRA 125%","EXTRA 150%"});if(detail!=p.clarity){p.clarity=detail;Session.ApplyQuality(p.quality);}
                GUI.enabled=graphicsEnabled;
            }
            p.volume=Slider(735,350,"Master audio",p.volume,0,1,600);
            p.engineVolume=Slider(735,430,"Engine volume",p.engineVolume,0,1,600);
            p.rumble=Slider(735,510,"Bumps, crashes & overall feedback",p.rumble,0,1,600);
            p.engineRumble=Slider(735,590,"Engine vibration",p.engineRumble,0,1,600);
            Text(65,454,600,28,GraphicsQualityPreset.Description(p.quality,GameGraphics.HighDefinition?0:p.clarity),small);
            p.livingScenery=StateToggle(new Rect(65,489,580,32),p.livingScenery,"People and animals in scenery",toggle);
            p.detailedHud=StateToggle(new Rect(65,546,580,32),p.detailedHud,"Show detailed driving information",toggle);
            p.performanceStats=StateToggle(new Rect(65,603,580,32),p.performanceStats,"Show performance stats",toggle);
            Text(735,663,580,45,"Engine volume adjusts the sound. Vibration settings adjust controller feedback from the engine, bumps and crashes.",body);
            Text(65,657,580,45,"Graphics mode: "+GameGraphics.Name+". Choose Standard or HD at launch.",small);
            if(Button(65,720,580,"DISPLAY RESOLUTION…",40)){displayResolution.Open(Session.Profile);tab=32;}
            if(Button(735,720,580,"RADIO & MUSIC",40))tab=28;
            if(Button(65,800,175,"BACK",37)){p.Save();tab=21;}
        }
        void DrawWheelSettings()
        {
            Panel(new Rect(35,240,1370,620),ink);var p=Session.Profile;
            Text(65,270,500,40,"STEERING WHEEL",heading);
            Text(65,340,490,200,"1. Enable wheel input.\n2. Bind each axis.\n3. Hold the requested position and click its calibration button.\n4. Enable force feedback only after calibration.",body);
            if(Button(65,800,175,"BACK",37)){Session.Input.CancelBinding();p.Save();tab=21;}
            Text(610,265,690,35,"LOGITECH G29 / WHEEL SETUP",heading);
            p.wheelEnabled=StateToggle(new Rect(615,315,340,30),p.wheelEnabled," Use calibrated wheel inputs",toggle);
            Text(615,350,720,30,Session.Input.Calibrating?"Move the requested axis • Escape cancels":bindingMessage,small);
            AxisRow(615,390,"STEERING",p.steerPath,"steer");
            if(Button(1000,390,105,"LEFT",32))p.steerLeft=Session.Input.ReadAxis(p.steerPath);
            if(Button(1115,390,105,"CENTRE",32))p.steerCentre=Session.Input.ReadAxis(p.steerPath);
            if(Button(1230,390,105,"RIGHT",32))p.steerRight=Session.Input.ReadAxis(p.steerPath);
            AxisRow(615,455,"THROTTLE",p.gasPath,"gas");
            if(Button(1000,455,160,"RELEASED",32))p.gasRest=Session.Input.ReadAxis(p.gasPath);
            if(Button(1175,455,160,"PRESSED",32))p.gasFull=Session.Input.ReadAxis(p.gasPath);
            AxisRow(615,520,"BRAKE",p.brakePath,"brake");
            if(Button(1000,520,160,"RELEASED",32))p.brakeRest=Session.Input.ReadAxis(p.brakePath);
            if(Button(1175,520,160,"PRESSED",32))p.brakeFull=Session.Input.ReadAxis(p.brakePath);
            Text(615,576,700,40,$"Live axes: steer {Session.Input.ReadAxis(p.steerPath):F2}  gas {Session.Input.ReadAxis(p.gasPath):F2}  brake {Session.Input.ReadAxis(p.brakePath):F2}",small);
            p.wheelForceEnabled=StateToggle(new Rect(615,615,370,30),p.wheelForceEnabled," Enable wheel force feedback",toggle);
            p.invertForce=StateToggle(new Rect(1000,615,330,30),p.invertForce," Invert force direction",toggle);
            p.wheelStrength=Slider(615,665,"Force strength",p.wheelStrength,0,.65f,710);
            Text(615,735,710,45,Session.Feedback.WheelStatus,small);
            Text(615,785,710,50,"Bind each axis first. Then hold each steering/pedal position while clicking its calibration button. Save with Back.",small);
        }
        float Slider(float x,float y,string label,float value,float min,float max,float width=480)
        {Text(x,y,width,27,label+"   "+Mathf.RoundToInt(value*100)+"%",body);return ControllerMenuGUI.HorizontalSlider(new Rect(x,y+35,width,22),value,min,max,label);}
        void AxisRow(float x,float y,string label,string path,string which)
        {
            if(Button(x,y,175,"BIND "+label,32))
            {bindingMessage="Move "+label.ToLower()+" now";Session.Input.Bind(which,()=>bindingMessage="Axis bound. Record its positions using the buttons.");}
            Text(x+185,y,195,40,string.IsNullOrEmpty(path)?"Not bound":path,small);
        }
        void LoadCircuitPreview()
        {if(circuitMap)Destroy(circuitMap);circuitPreview=GeoRegion.Load(circuitChoice==0?"silverstone":"spa");if(circuitPreview!=null)circuitMap=RegionMapTexture.Create(circuitPreview);}
        void DrawCircuits()
        {
            Panel(new Rect(35,240,1370,620),ink);Text(65,265,700,35,"CIRCUIT & DRIVING MODE",heading);
            if(Button(785,265,540,"IMPORTED CIRCUIT PACKS",38))ShowImportedPacks();
            int next=ControllerMenuGUI.Toolbar(new Rect(65,325,650,45),circuitChoice,new[]{"SILVERSTONE GP","SPA-FRANCORCHAMPS GP"});if(next!=circuitChoice){circuitChoice=next;LoadCircuitPreview();}
            trackMode=ControllerMenuGUI.Toolbar(new Rect(65,410,650,45),trackMode,new[]{"PRACTICE","TIME TRIAL","RACE","SPEED TRIAL"});
            string[] descriptions={"Explore the circuit without a competitive result.","Flying laps with sectors and saved personal bests for your car/setup and conditions.","A standing start against three matched-performance physical AI cars. Finish order includes track-limit penalties.","Highest ground speed during a complete valid flying lap. Shortcuts and resets invalidate the attempt."};
            Text(65,485,650,95,descriptions[trackMode],body);
            Text(65,600,160,30,"Race laps",body);lapChoice=ControllerMenuGUI.Toolbar(new Rect(235,595,480,38),lapChoice,new[]{"1","3","5"});
            Text(65,660,160,30,"AI difficulty",body);difficulty=ControllerMenuGUI.Toolbar(new Rect(235,655,480,38),difficulty,new[]{"EASY","NORMAL","HARD"});
            bool hireEnabled=GUI.enabled;GUI.enabled=hireEnabled&&Session.World.IsCircuit;
            if(Button(65,710,650,"HIRE / RETURN AT CURRENT CIRCUIT",38))tab=12;GUI.enabled=hireEnabled;
            if(Button(400,770,315,"START SESSION",48))Session.SelectCircuit(circuitChoice==0?"silverstone":"spa",trackMode,new[]{1,3,5}[lapChoice],difficulty);
            if(Button(65,800,175,"BACK",37))tab=20;
            if(circuitMap)GUI.DrawTexture(new Rect(875,325,390,315),circuitMap,ScaleMode.ScaleToFit);
            Text(785,670,560,75,$"{(circuitChoice==0?"5.891":"7.004")} km reference GP layout · offline data\nOwned car: {Core.GarageRules.Catalogue[Session.Profile.garage.Active.model].Name}",body);
            Text(785,765,560,65,"Unofficial layout/elevation approximations from OSM and Copernicus. Timing-line placement and scenery are approximate. LAN races are not included yet.",small);
        }
        void DrawRaceResults()
        {
            var race=Session.Racing;Panel(new Rect(760,270,610,420),ink);Text(790,295,550,85,race.Result,heading);
            for(int i=0;i<race.Classification.Length;i++)Text(795,405+i*48,530,40,race.Classification[i],body);
            Text(790,625,550,45,"Resume for free driving, restart the track session, or visit your garage.",small);
        }
        void DrawCircuitHUD()
        {
            var race=Session.Racing;if(!race||!race.Running)return;var lap=race.Lap;
            Panel(new Rect(505,24,440,62),new Color(.035f,.055f,.07f,.62f));
            string timing=race.Countdown>0?"START IN "+Mathf.CeilToInt(race.Countdown):lap.Armed?"LAP "+CircuitSession.Format(lap.Elapsed):"CROSS THE START LINE";
            Text(520,31,410,27,race.ModeName+"  ·  "+timing,body);
            Text(520,60,410,23,(race.Mode==2?"POS "+race.Position+"/"+race.Entrants+"  ·  ":"")+(lap.Valid?"SECTOR "+lap.Sector:"INVALID · "+lap.Reason)+(lap.LastTime>0?"  ·  LAST "+CircuitSession.Format(lap.LastTime):""),small);
            if(Session.IsRenting)Text(520,91,420,24,"HIRED · "+Core.GarageRules.Catalogue[Session.DrivenCar.model].Name+(Session.RentalAttemptPaid?" · fee paid":" · next fee due"),small);
            if(Session.Profile.detailedHud)Text(520,117,420,24,"SESSION "+CircuitSession.Format(race.Clock),small);
        }
        void DrawLan()
        {
            if(SinglePlayerBeta.Enabled)
            {
                Panel(new Rect(35,240,1370,620),ink);
                Text(65,265,1100,40,"SINGLE PLAYER BETA",heading);
                Text(65,330,1180,110,"Try automatically generated roads, terrain and photographic scenery. Your beta saves and settings are separate from the live game. LAN play is unavailable in this beta.",body);
                Text(65,465,1180,90,"Start near Bryn Meadows / Newbridge, or choose another place in World, Weather & Clock. Internet is needed to fetch new areas; prepared areas are cached for reuse.",body);
                if(Button(65,785,260,"BACK"))tab=0;
                return;
            }
            Panel(new Rect(35,240,1370,620),ink);Text(65,265,1100,40,"LAN FREE DRIVE · TWO-PLAYER PROTOTYPE",heading);
            Text(65,325,1130,70,"Host a session, then enter the host’s local IPv4 address on the other computer. Required map data is received from the host before joining. No account or internet service is needed.",body);
            Text(65,420,1000,40,Session.Network.Status,heading);
            if(Button(65,490,430,"HOST ON THIS COMPUTER"))Session.Network.Host();
            Text(600,485,650,30,"Host IPv4 (127.0.0.1 for this Mac)",body);
            hostAddress=ControllerMenuGUI.TextField(new Rect(600,525,450,40),hostAddress,64);
            if(Button(1070,525,230,"JOIN"))Session.Network.Join(hostAddress);
            if(Button(65,570,430,"DISCONNECT"))Session.Network.Stop();
            if(!string.IsNullOrEmpty(Session.Network.HostFingerprint))Text(65,615,1200,32,"Session fingerprint: "+Session.Network.HostFingerprint,small);
            if(!string.IsNullOrEmpty(Session.Network.DataUpdateStatus))Text(65,745,1200,32,Session.Network.DataUpdateStatus,small);
            Text(65,665,1200,70,"The host simulates both cars and collision outcomes. This first build has no LAN rewards, shared traffic/police or race mode. Same-Mac testing comes first; Windows interoperability and real network latency remain to be verified.",small);
            if(Button(65,785,260,"BACK"))tab=0;
        }
        void DrawWorld()
        {
            Panel(new Rect(35,240,1370,620),ink);var p=Session.Profile;
            Text(65,260,620,35,"DRIVING REGION",heading);
            Text(65,309,600,72,"Llangollen / Dee Valley: bundled real roads, hills, waterways and mapped building footprints. Buildings with missing heights use estimates.",body);
            if(Button(65,400,520,"DRIVE LLANGOLLEN / DEE VALLEY"))Session.SelectRegion("dee-valley");
            if(Button(65,456,520,"DRIVE ORIGINAL FICTIONAL COUNTY"))Session.SelectRegion("");
            if(Button(65,512,520,"FIND A PLACE / CHOOSE A ROAD START")){tab=8;if(worldMap==null)worldMap=new WorldMapMenu();worldMap.Open(Session);}
            Text(65,572,540,65,Session.World.Route.Region!=null?$"{(Session.World.IsCircuit?"GP circuit": (Session.World.Route.Region.roads.Length.ToString("N0")+" road segments"))}\n{Session.World.GeographicWorld.LoadedChunks} loaded chunks · offline geography":"Original fictional loop selected",body);
            Text(65,650,550,70,"Map data © OpenStreetMap contributors (ODbL)\nTerrain: Copernicus DEM / EU / ESA\nSource data and full notices: DATA_SOURCES.md",small);
            p.detailedScenery=StateToggle(new Rect(65,715,550,28),p.detailedScenery," Use installed high-detail buildings and woodland",toggle);
            if(Button(65,750,520,"SCENERY PACKS · FILES, SIZES & LICENCES",30))ShowSceneryPacks();
            Text(690,260,630,35,"WEATHER & TIME",heading);
            int normalWeather=ControllerMenuGUI.Toolbar(new Rect(690,310,650,40),Session.Weather<6?Session.Weather:-1,new[]{"CLEAR","RAIN","FOG","EVENING","DYNAMIC","LIVE"});
            if(normalWeather>=0)Session.Weather=normalWeather;
            int winter=ControllerMenuGUI.Toolbar(new Rect(690,355,650,28),Session.Weather>=6?Session.Weather-6:-1,new[]{"SNOW","FROST","ICE"});
            if(winter>=0)Session.Weather=winter+6;
            Text(690,389,650,28,Session.Conditions.WeatherStatus,body);
            Text(690,420,650,42,"Live: Weather data by Open-Meteo.com (CC BY 4.0). Modelled conditions at the selected map location; refresh every 15 minutes.",small);
            if(ControllerMenuGUI.Button(new Rect(690,470,650,28),"Open-Meteo weather data and attribution"))Application.OpenURL("https://open-meteo.com/");
            p.clockMode=ControllerMenuGUI.Toolbar(new Rect(690,520,650,40),p.clockMode,new[]{"FIXED","CYCLE","MAP REAL TIME","COMPUTER TIME"});
            Text(690,575,650,30,Session.Conditions.ClockStatus,heading);
            Text(690,620,650,24,"Fixed hour: "+p.fixedHour.ToString("F1")+"   •   cycle length: "+p.dayMinutes.ToString("F0")+" min",small);
            p.fixedHour=ControllerMenuGUI.HorizontalSlider(new Rect(690,653,310,20),p.fixedHour,0,23.99f);p.dayMinutes=ControllerMenuGUI.HorizontalSlider(new Rect(1030,653,310,20),p.dayMinutes,2,120);
            Text(690,690,650,24,"Clock offset: "+p.clockOffset.ToString("+0.0;-0.0;0")+" hours (weather remains current)",body);
            p.clockOffset=Mathf.Round(ControllerMenuGUI.HorizontalSlider(new Rect(690,730,650,20),p.clockOffset,-24,24)*2)/2;
            bool today=string.IsNullOrEmpty(p.worldDate);
            bool chooseToday=StateToggle(new Rect(690,760,190,28),today,"Today's date",toggle);
            if(chooseToday!=today)p.worldDate=chooseToday?"":System.DateTime.Now.ToString("yyyy-MM-dd");
            bool dateEnabled=GUI.enabled;GUI.enabled=dateEnabled&&!chooseToday;
            p.worldDate=chooseToday?"":ControllerMenuGUI.TextField(new Rect(895,758,215,32),p.worldDate,10);GUI.enabled=dateEnabled;
            Text(1125,758,210,35,"Sky date: yyyy-mm-dd",small);
            if(Button(65,800,175,"BACK",37)){Session.Save();tab=0;}
            if(Button(270,800,300,"MAP & SCENERY PACKS",37))tab=19;
            if(Button(1040,800,300,"ZERO TIME OFFSET",37))p.clockOffset=0;
        }
        void DrawControls()
        {
            Panel(new Rect(35,245,1370,600),ink);Text(65,280,600,40,"KEYBOARD",heading);Text(735,280,600,40,"XBOX / PLAYSTATION",heading);
            Text(65,340,580,350,"W / Up     Accelerate / return to forward\nS / Down     Brake, then reverse\nA D / Left Right     Steer\nSpace     Handbrake\nP     Toggle parking brake\nC     Cycle seven camera views\nB     Hold to look back\nH     Horn\nT     Start / restart road or track session\nG     Free roadside repair / pit guidance\nR     Recover to road\nEscape     Pause / settings",body);
            Text(735,340,580,350,"RT / R2     Accelerate / return to forward\nLT / L2     Brake, then reverse\nLeft stick     Steer\nRight stick     Look / set chase height · click to reset\nA / Cross     Handbrake\nD-pad down     Toggle parking brake\nRB / R1     Cycle camera\nY / Triangle     Hold to look back\nB / Circle     Horn\nX / Square     Start road / track session\nD-pad up     Free repair / pit guidance\nView / Create     Recover\nMenu / Options     Pause",body);
            Text(65,710,1230,45,"Hold brake after stopping to reverse. G / D-pad up repairs free in road free drive; circuits have a free pit service bay.",small);
            if(Button(65,800,175,"BACK",37))tab=21;
        }
        readonly FrameTiming[] statsTimings=new FrameTiming[1];double statsGpu;
        float statsNext,statsFrame,statsFps;int statsFrames;float statsElapsed;
        void DrawPerformanceStats()
        {
            if(Event.current.type==EventType.Repaint)
            {
                statsFrames++;statsElapsed+=Time.unscaledDeltaTime;
                if(Time.realtimeSinceStartup>=statsNext)
                {statsFps=statsFrames/Mathf.Max(.001f,statsElapsed);statsFrame=statsElapsed*1000/Mathf.Max(1,statsFrames);statsFrames=0;statsElapsed=0;statsNext=Time.realtimeSinceStartup+.5f;FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,statsTimings)>0)statsGpu=statsTimings[0].gpuFrameTime;}
            }
            var world=Session.World.AutomaticWorld?Session.World.AutomaticWorld.Renderer:null;
            var host=Session.World.AutomaticWorld;
            string slice=world?$"Main slice: {world.CurrentGenerationSliceMilliseconds:F1} ms · worst {world.MaximumGenerationSliceMilliseconds:F1} ms":"Main slice: idle";
            string queues=world?$"Queued: {world.PendingGeometryChunks} road chunks · {world.PendingSceneryChunks} building visuals · {world.PendingBuildingCollisions} building colliders":"Queued: —";
            string build=world?$"Chunk: {world.CurrentChunkSeconds:F1} s ({world.CurrentBuildStage}) · last queue-to-ready {world.LastChunkReadySeconds:F1} s · bakes {world.ActiveRoadColliderBakes}":"Chunk: —";
            string data=world?$"Data: {world.DataPreparationStage} {world.DataPreparationSeconds:F0} s · activity {world.DataWorkerIdleSeconds:F0} s ago · building worker worst {world.MaximumBuildingWorkerMilliseconds:F1} ms":"Data: —";
            string coverage=host?$"Ahead: {host.SupportedAheadMetres:F0} m · {host.CoverageReason}" : "Ahead: —";
            if(host&&!float.IsInfinity(host.PreparationSpeedLimitMph))coverage+=$" · safe speed {host.PreparationSpeedLimitMph:F0} mph";
            var canvas=BrandScreen.Canvas;float left=canvas.xMax-928,top=canvas.yMin+28;
            Panel(new Rect(left,top,900,126),new Color(.025f,.04f,.05f,.80f));
            Text(left+10,top+2,880,24,$"{statsFps:F0} FPS · frame {statsFrame:F1} ms · GPU {(statsGpu>0?statsGpu.ToString("F1")+" ms":"—")} · "+slice,small);
            Text(left+10,top+26,880,24,queues,small);Text(left+10,top+50,880,24,build,small);
            Text(left+10,top+74,880,24,data,small);Text(left+10,top+98,880,24,coverage,small);
        }
        void DrawHUD()
        {
            var s=Session;var car=s.Car;
            if(s.Profile.performanceStats){using var stats=WorkTelemetry.Measure("hud.stats",null,4);DrawPerformanceStats();}
            using(WorkTelemetry.Measure("hud.radio",null,4))DrawRadioHUD();
            using(WorkTelemetry.Measure("hud.journey",null,4))DrawJourneyStrip();
            using(WorkTelemetry.Measure("hud.speed",null,4))DrawSpeedInstrument();
            if(s.Profile.detailedHud)
            {
                InterfaceTheme.Card(new Rect(28,136,380,93),.85f);
                Text(46,150,344,25,$"{s.Economy.Credits:N0} CR  ·  SKILL {s.Economy.Skill+s.Drifts.Total:N0}",small);
                Text(46,180,344,38,s.Conditions.WeatherStatus+" · clean streak "+s.Economy.Streak,small);
            }
            if(car.ParkingBrakeApplied)Text(1080,600,330,26,"PARK · accelerate to release",small);
            if(s.World.IsCircuit)DrawCircuitHUD();
            float noticeOffset=s.Profile.performanceStats?136:0;
            if(s.Drifts.Combo>0)Text(505,132+noticeOffset,650,32,s.Drifts.Feedback+"  "+s.Drifts.Combo.ToString("F0"),body);
            if(s.Network.Active)Text(505,166+noticeOffset,650,25,s.Network.Status+" · "+s.Network.RoundTripMs.ToString("F0")+" ms",small);
            if(s.Wanted>0){Panel(new Rect(505,24+noticeOffset,430,44),new Color(.35f,.08f,.06f,.75f));Text(520,31+noticeOffset,400,30,"POLICE PURSUIT  ·  "+Mathf.CeilToInt(s.Wanted)+"s",body);}
            if(s.Run.Active)Text(505,78+noticeOffset,650,28,$"COUNTY RUN {s.Run.Elapsed:F2}s  ·  CHECKPOINT {s.Run.Next+1}/8  ·  +{s.Run.Penalties:F0}s",body);
            if(!string.IsNullOrEmpty(s.Notice))
            {Panel(new Rect(265,815,830,36),new Color(.035f,.055f,.07f,.72f));Text(279,820,802,29,s.Notice,small);}
            if(!s.World.IsCircuit&&car.Damage>8)Text(510,775,680,26,"G / D-PAD UP · FREE ROADSIDE REPAIR",small);
            if(s.Pits&&s.World.IsCircuit&&(s.Pits.Servicing||s.Pits.AtBay))Text(510,775,680,28,s.Pits.Servicing?$"PIT SERVICE · {s.Pits.Remaining:F1}s · FREE":"PIT BOX · STOP FOR FREE REPAIR",body);
            if(s.AtGarage&&!s.World.IsCircuit)Text(510,775,680,28,"HARBOR AUTO · STOP + G / D-PAD UP TO REPAIR",small);
            DrawMap();Text(28,866,450,24,"Esc / Options  Pause     ·     Select map to navigate",small);

        }
        void OnDestroy(){displayResolution.Revert();if(radioKnob)Destroy(radioKnob);vehiclePacks.Dispose();navigation.Dispose();movingMap.Dispose();worldMap?.Dispose();circuitPacks?.Dispose();importedPacks?.Dispose();sceneryPacks?.Dispose();garageMenu?.Close();if(circuitMap)Destroy(circuitMap);if(geoMiniMap)Destroy(geoMiniMap);}
        void DrawMap()
        {
            Rect r=new Rect(28,660,190,190);InterfaceTheme.Card(new Rect(20,652,206,206),.94f);Panel(r,ink);
            if(SinglePlayerBeta.Enabled && !Session.World.IsCircuit && Session.World.Route.Region!=null){if(ControllerMenuGUI.Button(r,GUIContent.none,GUIStyle.none)){ShowNavigationMap();return;}movingMap.Draw(r,Session);using(WorkTelemetry.Measure("hud.navigation-mini",null,4))navigation.DrawMini(r,Session);return;}
            var region=Session.World.Route.Region;
            if(region!=null)
            {
                if(!geoMiniMap)geoMiniMap=RegionMapTexture.Create(region);GUI.DrawTexture(r,geoMiniMap);
                var p=Session.Car.Body.position;float x=r.x+(p.x-region.minX)/region.width*r.width,y=r.y+r.height-(p.z-region.minZ)/region.depth*r.height;Panel(new Rect(x-3,y-3,6,6),accent);
                var garagePoint=Session.World.Garage.position;float gx=r.x+(garagePoint.x-region.minX)/region.width*r.width,gy=r.y+r.height-(garagePoint.z-region.minZ)/region.depth*r.height;Panel(new Rect(gx-2,gy-2,4,4),Color.yellow);Text(r.x+4,r.y+3,30,20,"N",small);return;
            }
            foreach(var p in Session.World.Route.Points){float mapScale=Session.World.Route.Region!=null?.026f:.085f;float x=r.x+r.width*.5f+p.x*mapScale,y=r.y+r.height*.5f-p.z*mapScale;Panel(new Rect(x,y,2,2),muted);}
            float factor=Session.World.Route.Region!=null?.026f:.085f;var car=Session.Car.transform.position;Panel(new Rect(r.center.x-3+car.x*factor,r.center.y-3-car.z*factor,7,7),accent);
            var garage=Session.World.Garage.position;Panel(new Rect(r.center.x-2+garage.x*factor,r.center.y-2-garage.z*factor,5,5),Color.yellow);
        }
    }
}
