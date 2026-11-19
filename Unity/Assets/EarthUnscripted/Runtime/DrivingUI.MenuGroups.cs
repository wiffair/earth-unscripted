using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    public sealed partial class DrivingUI
    {
        void MenuGroup(string name,string explanation)
        {
            Panel(new Rect(35,240,1370,620),ink);
            Text(65,265,1250,35,name,heading);Text(65,315,1240,65,explanation,body);
        }
        void GroupBack(int parent=0){if(Button(65,800,175,"BACK",37))tab=parent;}
        void DrawCarsHub()
        {
            MenuGroup("CARS & GARAGE","Manage owned cars, buy another model, import a car pack, or change its paint job.");
            if(Button(65,400,600,"MY GARAGE & UPGRADES"))ShowGarage();
            if(Button(65,460,600,"BUY A BUILT-IN CAR")){ShowGarage();garageMenu.ShowDealership();}
            if(Button(65,520,600,"CAR PACKS & IMPORTER"))ShowVehiclePacks();
            if(Button(65,580,600,"PAINT JOBS & FINISH")){paintLoaded=false;tab=25;}
            if(Button(65,640,600,"FREE ROADSIDE REPAIR"))Session.Repair();
            if(Button(65,700,600,"TOW TO GARAGE"))Session.TowToGarage();
            Text(735,400,590,90,"ACTIVE CAR\n"+ImportedVehicleCatalogue.DisplayName(Session.Profile.garage.Active),heading);
            Text(735,515,590,100,$"{Session.Economy.Credits:N0} credits · {Session.Profile.garage.cars.Count}/8 garage spaces\nImported packs are added to the local library first. Buying a car is a separate choice; its price is shown before purchase.",body);
            Text(735,655,590,100,string.IsNullOrEmpty(Session.VehicleSwapUnavailableReason)?Session.Notice:Session.VehicleSwapUnavailableReason,body);GroupBack();
        }
        void DrawLocationsHub()
        {
            MenuGroup("LOCATIONS & MAP","Find somewhere to drive, plan a route, or return to a saved location.");
            if(Button(65,405,600,"SEARCH FOR A PLACE"))ShowWorldMap();
            if(Button(65,470,600,"OPEN MAP & PLAN ROUTE"))navigation.OpenMap(Session);
            if(Button(65,535,600,"SAVED LOCATIONS"))tab=15;
            if(Button(65,600,600,"MAP & SCENERY PACKS"))tab=19;
            GroupBack();
        }
        void DrawDataHub()
        {
            MenuGroup("MAP & SCENERY PACKS","Inspect installed scenery and add optional map content.");
            if(Button(65,405,600,"INSTALLED SCENERY"))ShowSceneryPacks();
            if(Button(65,470,600,"IMPORT MAP OR CIRCUIT PACK"))ShowImportedPacks();
            if(Button(65,535,600,"DATA CREDITS & LICENCES"))tab=13;
            if(Button(735,405,600,"EXPORT DIAGNOSTIC REPORT"))BetaDiagnostics.Begin(Session);
            if(Button(735,470,600,"OPEN REPORTS FOLDER"))BetaDiagnostics.OpenFolder();
            Text(735,535,600,145,BetaDiagnostics.Status,small);
            GroupBack(18);
        }
        void DrawRacingHub()
        {
            MenuGroup("RACES & CHALLENGES","Choose a circuit session or the bundled county checkpoint challenge.");
            if(Button(65,405,600,"CIRCUITS & TIME TRIALS")){tab=6;trackMode=Session.Profile.trackMode;difficulty=Session.Profile.raceDifficulty;lapChoice=Session.Profile.raceLaps==1?0:Session.Profile.raceLaps==3?1:2;LoadCircuitPreview();}
            if(Button(65,470,600,"COUNTY CHECKPOINT CHALLENGE"))Session.StartRun();
            if(Session.World.IsCircuit&&Button(65,535,600,"CIRCUIT CAR HIRE & RETURN"))tab=12;
            Text(735,405,580,120,"Imported cars currently support free drive. Choose an owned built-in car for timed circuit sessions. Race progress and hire charges remain separate from the car-pack library.",body);GroupBack();
        }
        void DrawSettingsHub()
        {
            MenuGroup("SETTINGS","Display, audio, controls and storage.");
            if(Button(65,405,600,"DISPLAY & AUDIO"))tab=22;
            if(Button(65,470,600,"KEYBOARD & CONTROLLER"))tab=2;
            if(Button(65,535,600,"STEERING WHEEL CALIBRATION"))tab=23;
            if(Button(65,600,600,"WORLD CACHE & STORAGE"))tab=14;
            if(Button(65,665,600,"TRAINS & TYRE TRACKS"))tab=26;
            if(Button(735,405,600,"WORLD DATA SOURCES"))tab=27;
            if(SinglePlayerBeta.Enabled&&Button(735,470,600,"RADIO & MUSIC"))tab=28;
            if(SinglePlayerBeta.Enabled&&Button(735,535,600,"GAME & PACK UPDATES"))tab=31;
            GroupBack();
        }
        void DrawHelpHub()
        {
            MenuGroup("HELP & ABOUT","Driving controls, beta information and attribution.");
            if(Button(65,405,600,"DRIVING CONTROLS"))tab=2;
            if(Button(65,470,600,"ABOUT THIS BETA"))tab=4;
            if(Button(65,535,600,"DATA CREDITS & LICENCES"))tab=13;
            if(Button(735,405,600,"EXPORT DIAGNOSTIC REPORT"))BetaDiagnostics.Begin(Session);
            if(Button(735,470,600,"OPEN REPORTS FOLDER"))BetaDiagnostics.OpenFolder();
            Text(735,535,600,145,BetaDiagnostics.Status,small);
            GroupBack();
        }
        bool paintLoaded;int jobPaint,jobPattern,jobFinish;
        void DrawPaintJobs()
        {
            var active=Session.Profile.garage.Active;
            if(active.model>=ImportedVehicleCatalogue.BuiltInCount&&!ImportedVehicleCatalogue.Get(active.model).Settings.SupportsPaint)
            {
                MenuGroup("PAINT JOBS & FINISH",ImportedVehicleCatalogue.DisplayName(active));
                Text(65,405,1200,120,"This pack has a fixed livery. Its original colours and finish stay with the model; no paint changes are charged.",body);
                GroupBack(17);return;
            }
            if(!paintLoaded){jobPaint=active.paint;jobPattern=active.paintJob;jobFinish=active.finish;paintLoaded=true;}
            MenuGroup("PAINT JOBS & FINISH",ImportedVehicleCatalogue.DisplayName(active)+" · choose colour, pattern and finish, then apply the quoted change.");
            Text(65,390,600,30,"Base colour",body);
            jobPaint=ControllerMenuGUI.SelectionGrid(new Rect(65,430,1250,85),jobPaint,CarColours.Names,4,button);
            Text(65,535,600,30,"Paint job",body);
            jobPattern=ControllerMenuGUI.SelectionGrid(new Rect(65,575,1250,80),jobPattern,VehiclePaintJobs.Names,3,button);
            jobFinish=ControllerMenuGUI.Toolbar(new Rect(65,685,1250,38),jobFinish,new[]{"METALLIC","MATTE","GLOSS"},button);
            var quote=GarageRules.Style(Session.Profile.garage,10000000,jobPaint,active.wheels,active.bodyKit,jobFinish,jobPattern);
            int price=quote==null?0:10000000-quote.Credits;
            Text(65,742,700,44,quote==null?"Choose a different colour, pattern or finish.":$"Cost: {price:N0} credits · balance: {Session.Economy.Credits:N0}",body);
            bool enabled=GUI.enabled;GUI.enabled=enabled&&quote!=null&&Session.Economy.Credits>=price;
            if(Button(930,795,390,"APPLY · "+price.ToString("N0")+" CR",42))Session.PaintActiveCar(jobPaint,jobPattern,jobFinish);
            GUI.enabled=enabled;GroupBack(17);
        }
    }
}
