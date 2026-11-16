using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit standalone acceptance using a virtual Input System controller,
    /// real menu drawing/callbacks and a disposable beta profile. No real controller
    /// sensation is claimed; motor values and complete game flows are observed.
    /// </summary>
    public sealed class BetaControllerVerification : MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        GameSession session;DrivingUI ui;Gamepad pad;float pressPeak;int menusChecked;
        string Output=>SinglePlayerBeta.VerificationOutput;
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
        void Check(bool condition,string message)
        {
            File.WriteAllText(Path.Combine(Output,"controller-progress.json"),new JObject{["passed"]=condition,["check"]=message}.ToString());
            if(!condition)throw new InvalidOperationException(message);checks.Add(message);
        }
        IEnumerator Start()
        {
            string failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0)
            {
                bool more=false;object current=null;
                try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception error){failure=error.ToString();}
                if(failure!=null)break;if(!more){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(Output,"result.json"),new JObject{["passed"]=passed,["failure"]=failure,["checks"]=new JArray(checks),["errors"]=new JArray(errors),["menusChecked"]=menusChecked,["version"]=Application.version,["scope"]="Virtual gamepad through actual native IMGUI menus, save/owned-car switching and motor-output values. Physical controller sensation still needs user testing."}.ToString());
            Application.Quit(passed?0:1);
        }
        IEnumerator Press(GamepadButton button)
        {
            pressPeak=0;pad.MakeCurrent();InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(button));
            yield return null;yield return new WaitForEndOfFrame();
            pressPeak=Mathf.Max(pressPeak,session.Feedback.LastMotors.High);
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;yield return new WaitForEndOfFrame();
        }
        static readonly Vector2[] Directions={Vector2.down,Vector2.up,Vector2.left,Vector2.right};
        static readonly GamepadButton[] Buttons={GamepadButton.DpadUp,GamepadButton.DpadDown,GamepadButton.DpadLeft,GamepadButton.DpadRight};
        int FirstStep(ControllerMenuNavigation.Control target)
        {
            var nav=ui.ControllerMenus;var start=nav.Focused;
            if(start==null)return -1;
            var visited=new HashSet<int>{start.Id};var queue=new Queue<Tuple<ControllerMenuNavigation.Control,int>>();queue.Enqueue(Tuple.Create(start,-1));
            while(queue.Count>0)
            {
                var node=queue.Dequeue();
                for(int direction=0;direction<4;direction++)
                {
                    if(direction>=2&&(node.Item1.Kind==ControllerMenuNavigation.ControlKind.Choice||node.Item1.Kind==ControllerMenuNavigation.ControlKind.Slider))continue;
                    var next=ControllerMenuNavigation.Neighbour(nav.Controls,node.Item1,Directions[direction]);
                    if(next==null||!visited.Add(next.Id))continue;int first=node.Item2<0?direction:node.Item2;
                    if(next.Id==target.Id)return first;queue.Enqueue(Tuple.Create(next,first));
                }
            }
            return -1;
        }
        IEnumerator Focus(Func<ControllerMenuNavigation.Control,bool> predicate)
        {
            for(int step=0;step<80;step++)
            {
                var target=ui.ControllerMenus.Controls.FirstOrDefault(predicate);
                Check(target!=null,"Visible enabled menu action exists");
                if(ui.ControllerMenus.Focused?.Id==target.Id)yield break;
                int direction=FirstStep(target);
                if(direction<0)throw new InvalidOperationException("Controller cannot reach "+target.Label+" from "+ui.ControllerMenus.Focused?.Label+" on "+ui.ControllerMenus.Context);
                yield return Press(Buttons[direction]);
            }
            throw new TimeoutException("Controller focus did not settle");
        }
        IEnumerator Select(string label)
        {
            yield return Focus(c=>c.Label==label);yield return Press(GamepadButton.South);yield return null;yield return new WaitForEndOfFrame();
        }
        void CheckMenu()
        {
            var nav=ui.ControllerMenus;
            foreach(var control in nav.Controls)
            {
                // Scroll containers intentionally are not directional targets from
                // their children. Right-stick scrolling is available through any
                // focused descendant; test that actual supported access path.
                bool reachable=control.Id==nav.Focused?.Id||FirstStep(control)>=0;
                if(control.Kind==ControllerMenuNavigation.ControlKind.Scroll)
                    reachable|=nav.Controls.Any(child=>child.ScrollId==control.Id&&(child.Id==nav.Focused?.Id||FirstStep(child)>=0));
                Check(reachable,"Controller can reach "+control.Label+" on "+nav.Context);
            }
            menusChecked++;
        }
        IEnumerator Back(){yield return Press(GamepadButton.East);yield return null;yield return new WaitForEndOfFrame();}
        IEnumerator Exercise()
        {
            if(SinglePlayerBeta.VerificationOutput==null)throw new InvalidOperationException("Controller proof requires isolated beta data");
            // The native acceptance player is launched directly and may not own
            // the foreground window. Virtual input must not be reset by macOS
            // focus changes or compete with a physical pad/keyboard in this test.
            // These settings affect this disposable player process only.
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach(var device in InputSystem.devices.ToArray())
                if(device is Gamepad||device is Keyboard||device is Mouse)InputSystem.DisableDevice(device);
            double deadline=Time.realtimeSinceStartupAsDouble+600;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
            {if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Controller proof startup");yield return null;}
            ui=FindFirstObjectByType<DrivingUI>();session.SetMenu(true);session.Input.enabled=false;
            pad=InputSystem.AddDevice<Gamepad>("EarthUnscriptedControllerProof");pad.MakeCurrent();session.Profile.rumble=1;
            yield return null;yield return new WaitForEndOfFrame();CheckMenu();
            yield return Select("SETTINGS");Check(ui.MenuTab==21,"A opens settings hub");CheckMenu();
            yield return Select("DISPLAY & AUDIO");CheckMenu();
            var sliders=ui.ControllerMenus.Controls.Where(c=>c.Kind==ControllerMenuNavigation.ControlKind.Slider).ToArray();
            Check(sliders.Length>=2,"Audio and rumble sliders are navigable");
            int rumbleId=sliders[1].Id;yield return Focus(c=>c.Id==rumbleId);
            yield return Press(GamepadButton.DpadLeft);Check(session.Profile.rumble<1,"D-pad adjusts controller vibration slider");
            Check(pressPeak>0,"Menu navigation reaches haptic mixer while game is paused");
            Check(sliders.Length>=3,"Separate engine vibration slider is navigable");
            int engineId=sliders[2].Id;yield return Focus(c=>c.Id==engineId);
            session.Profile.engineRumble=0;float overall=session.Profile.rumble;yield return Press(GamepadButton.DpadRight);
            Check(session.Profile.engineRumble>0&&session.Profile.rumble==overall,"D-pad adjusts engine independently of overall vibration");
            yield return Press(GamepadButton.DpadLeft);Check(session.Profile.engineRumble==0,"Engine can be turned completely off with controller");

            session.Profile.rumble=1;yield return Focus(c=>c.Label.Contains("People and animals"));bool animals=session.Profile.livingScenery;
            yield return Press(GamepadButton.South);Check(session.Profile.livingScenery!=animals,"A toggles a real checkbox");
            session.Profile.detailedHud=true;
            ScreenCapture.CaptureScreenshot(Path.Combine(Output,"controller-settings.png"));yield return new WaitForEndOfFrame();
            yield return Back();Check(ui.MenuTab==21,"B returns through settings callback");
            yield return Select("STEERING WHEEL CALIBRATION");CheckMenu();yield return Back();
            yield return Select("TRAINS & TYRE TRACKS");Check(ui.MenuTab==26,"Controller opens train and tyre-track settings");CheckMenu();
            session.Profile.trainServiceMode=0;yield return null;yield return new WaitForEndOfFrame();
            bool tracks=session.Profile.surfaceTracks;yield return Focus(c=>c.Label.Contains("Snow and wet tyre tracks"));yield return Press(GamepadButton.South);Check(session.Profile.surfaceTracks!=tracks,"Controller toggles weather tyre tracks independently");
            yield return Focus(c=>c.Kind==ControllerMenuNavigation.ControlKind.Choice&&c.Label.Contains("LOCAL SIMULATION"));yield return Press(GamepadButton.DpadRight);Check(session.Profile.trainServiceMode==1,"Controller selects timetable operation");
            yield return Press(GamepadButton.DpadRight);Check(session.Profile.trainServiceMode==2,"Controller selects reported-position operation");
            yield return Press(GamepadButton.DpadRight);Check(session.Profile.trainServiceMode==0,"Controller restores clearly labelled local simulation");
            session.Profile.surfaceTracks=tracks;yield return Back();Check(ui.MenuTab==21,"Train settings Back returns to Settings");
            var radio=session.GetComponent<Radio.CarRadio>();while(radio&&!radio.Ready)yield return null;
            yield return Select("RADIO & MUSIC");Check(ui.MenuTab==28,"Controller opens car radio");CheckMenu();
            if(radio){string label=radio.StationLabel(radio.Catalog.ActiveStations.First());radio.ChangeStation("");yield return Select(label);Check(!string.IsNullOrEmpty(radio.Settings.station),"Controller selects a dynamically discovered radio station");}
            yield return Select("IMPORT / REFRESH PACKS");Check(ui.MenuTab==29,"Controller opens radio import settings");CheckMenu();yield return Back();Check(ui.MenuTab==28,"B returns from radio importer");
            yield return Select("REVIEW AUDIO CLIPS");Check(ui.MenuTab==30,"Controller opens radio clip review");CheckMenu();yield return Back();yield return Back();Check(ui.MenuTab==21,"B returns from radio to Settings");
            yield return Select("WORLD DATA SOURCES");Check(ui.MenuTab==27,"Controller opens data source status");CheckMenu();yield return Back();Check(ui.MenuTab==21,"Data source status Back returns to Settings");
            yield return Select("WORLD CACHE & STORAGE");CheckMenu();yield return Back();yield return Back();
            yield return Select("LOCATIONS & MAP");CheckMenu();yield return Select("SEARCH FOR A PLACE");CheckMenu();
            yield return Focus(c=>c.Kind==ControllerMenuNavigation.ControlKind.Text);yield return Press(GamepadButton.South);
            Check(ui.ControllerMenus.Editing,"A opens controller text keyboard");yield return null;yield return new WaitForEndOfFrame();CheckMenu();
            yield return Select("CLEAR");yield return Select("a");yield return Select("DONE");
            Check(!ui.ControllerMenus.Editing,"Controller keyboard closes on Done");
            string search=(string)typeof(WorldMapMenu).GetField("query",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(ui.LocationMenu);
            Check(search=="a","Onscreen keyboard applies edited location search text");
            yield return Back();Check(ui.MenuTab==18,"B returns from location search");
            yield return Select("OPEN MAP & PLAN ROUTE");CheckMenu();
            var centre=ui.Navigation.ViewCentre;yield return Focus(c=>c.Kind==ControllerMenuNavigation.ControlKind.Map);
            InputSystem.QueueStateEvent(pad,new GamepadState{leftStick=new Vector2(1,0)});
            for(int frame=0;frame<8;frame++)yield return null;
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;
            Check(ui.Navigation.ViewCentre.x>centre.x,"Controller stick pans the large map");
            yield return Back();Check(!ui.Navigation.IsOpen,"B closes navigation map");session.SetMenu(true);yield return null;yield return new WaitForEndOfFrame();
            yield return Back();yield return Select("CARS & GARAGE");CheckMenu();
            yield return Select("PAINT JOBS & FINISH");CheckMenu();yield return Back();
            yield return Select("CAR PACKS & IMPORTER");CheckMenu();
            yield return Select("CHOOSE CAR PACK…");CheckMenu();
            Check(ui.ControllerMenus.Controls.Any(c=>c.Label=="PARENT FOLDER"),"Controller file chooser stays inside the game");
            yield return Back();yield return Back();
            // Own a second real car in this disposable profile. Selection must not depend on Harbor Auto or paused velocity.
            var second=new OwnedCar{id="controller-proof-second-car",model=1,paint=2};session.Profile.garage.cars.Add(second);
            session.World.Garage.position+=Vector3.right*10000;Check(!session.AtGarage,"Car swap fixture is away from Harbor Auto");
            session.Car.Body.linearVelocity=session.Car.transform.forward*20;
            int credits=session.Economy.Credits;
            var wanted=typeof(GameSession).GetProperty("Wanted");wanted.SetValue(session,22f);
            Check(session.VehicleSwapUnavailableReason.Contains("police pursuit is active")&&session.VehicleSwapUnavailableReason.Contains("pauses in menus"),"Pursuit restriction explains why swaps are locked and countdown is paused");
            string originalId=session.Profile.garage.activeId;
            Check(!session.SelectOwnedVehicle(second.id)&&session.Profile.garage.activeId==originalId&&session.Economy.Credits==credits,"Blocked pursuit swap preserves active car and credits");
            yield return Select("MY GARAGE & UPGRADES");CheckMenu();
            Check(ui.ControllerMenus.Controls.Any(c=>c.Label=="LOCKED · POLICE PURSUIT"),"Owned-car menu visibly labels pursuit lock");
            ScreenCapture.CaptureScreenshot(Path.Combine(Output,"pursuit-swap-locked.png"));yield return new WaitForEndOfFrame();
            wanted.SetValue(session,0f);yield return null;yield return new WaitForEndOfFrame();
            Check(session.VehicleSwapUnavailableReason=="","Swapping unlocks after pursuit ends");
            yield return Focus(c=>c.Label.StartsWith(ImportedVehicleCatalogue.DisplayName(second),StringComparison.Ordinal));
            yield return Press(GamepadButton.South);
            Check(session.Profile.garage.activeId==second.id,"One A selection swaps to the exact owned car despite paused travel velocity");
            Check(session.Car.AbsoluteSpeed<.1f&&session.Economy.Credits==credits,"Direct swap parks safely and does not charge credits");
            Check(PlayerProfile.Load().garage.activeId==second.id,"Direct car selection persists through real beta save/reload");
            yield return Back();yield return Back();
            yield return Select("HELP & ABOUT");CheckMenu();yield return Select("DATA CREDITS & LICENCES");CheckMenu();yield return Back();yield return Back();
            yield return Select("SAVE & LOAD CAREER");CheckMenu();yield return Back();
            yield return Select("RACES & CHALLENGES");CheckMenu();yield return Select("CIRCUITS & TIME TRIALS");CheckMenu();yield return Back();
            ScreenCapture.CaptureScreenshot(Path.Combine(Output,"controller-finished.png"));yield return new WaitForEndOfFrame();
            Check(errors.Count==0,"No native menu/runtime exceptions");
        }
    }
}
