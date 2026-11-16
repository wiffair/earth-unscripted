using UnityEngine;
using RoadRush.Core;

namespace RoadRush
{
    public sealed partial class DrivingUI
    {
        GUIStyle homeLink, homePrimary, metric, metricCaption, hudRoad, speedDigits;
        void PresentationStyles()
        {
            if (homeLink != null) return;
            homeLink = new GUIStyle(button) { fontSize = 17, padding = new RectOffset(22, 15, 7, 7) };
            homePrimary = new GUIStyle(homeLink) { fontSize = 19, fontStyle = FontStyle.Bold };
            homePrimary.normal.background = InterfaceTheme.PrimaryTexture;
            homePrimary.normal.textColor = InterfaceTheme.Navy;
            metric = new GUIStyle(heading) { fontSize = 25, wordWrap = true };
            metricCaption = new GUIStyle(small) { fontSize = 12 };
            metricCaption.normal.textColor = InterfaceTheme.Gold;
            hudRoad = new GUIStyle(body) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            speedDigits = new GUIStyle(big) { fontSize = 68, alignment = TextAnchor.MiddleRight };
        }
        void DrawHome()
        {
            PresentationStyles();
            BrandScreen.Background(BrandScreen.Canvas, .08f);
            InterfaceTheme.Card(new Rect(40, 38, 380, 804), .96f);
            Text(68, 66, 320, 25, "EARTH UNSCRIPTED", metricCaption);
            Text(68, 105, 320, 55, "Your next journey", heading);
            Text(68, 163, 320, 56, "Choose a place. Take your time.\nSee where the road leads.", small);
            string[] items = { Session.Started ? "RESUME DRIVE" : "FREE DRIVE", "LOCATIONS & MAP", "CARS & GARAGE", "RACES & CHALLENGES", "WORLD & WEATHER", "SETTINGS", "SAVE & LOAD CAREER", "HELP & ABOUT", "QUIT" };
            for (int i = 0; i < items.Length; i++)
            {
                float y = 250 + i * 56;
                if (ControllerMenuGUI.Button(new Rect(64, y, 332, 46), items[i], i == 0 ? homePrimary : homeLink)) Activate(i);
            }
            Text(68, 790, 315, 35, GameGraphics.Name + " graphics · " + Application.version, small);
            Text(477, 84, 830, 24, "REAL PLACES. YOUR ADVENTURE.", metricCaption);
            BrandScreen.Wordmark(new Rect(470, 118, 895, 90), 61);
            Text(479, 214, 730, 50, "An open world, shaped by the world outside.", body);
            string place = Session.World.Route.Region?.PlaceNameAt(Session.Car.Body.position) ?? Session.World.Route.District(Session.Progress);
            HomeCard(new Rect(477, 569, 438, 168), "YOUR LOCATION", place, "Continue exploring, or find somewhere new.");
            int owned=Session.Profile.garage.cars.Count;
            HomeCard(new Rect(935, 569, 438, 168), "YOUR VEHICLE", ImportedVehicleCatalogue.DisplayName(Session.Profile.garage.Active), Session.Economy.Credits.ToString("N0") + " credits · " + owned + (owned==1?" owned vehicle":" owned vehicles"));
            InterfaceTheme.Card(new Rect(477, 757, 896, 85), .94f);
            Text(499, 775, 850, 48, "Driving is available now. Walking, boats and new ways to travel are part of the longer journey.", small);
            if (ControllerMenuGUI.Button(new Rect(477, 852, 896, 24), "© OpenStreetMap contributors   ·   DATA CREDITS & LICENCES", small)) tab = 13;
        }
        void HomeCard(Rect bounds, string label, string value, string detail)
        {
            InterfaceTheme.Card(bounds, .95f);
            Text(bounds.x + 22, bounds.y + 20, bounds.width - 44, 24, label, metricCaption);
            Text(bounds.x + 22, bounds.y + 53, bounds.width - 44, 65, value, metric);
            Text(bounds.x + 22, bounds.y + 124, bounds.width - 44, 32, detail, small);
        }
        void DrawMenuHeader()
        {
            PresentationStyles();
            // Garage and circuit pages render a live vehicle preview on the
            // right. Dim the world instead of covering that preview with art.
            Panel(BrandScreen.Canvas, new Color(.015f, .028f, .042f, .46f));
            InterfaceTheme.Card(new Rect(35, 35, 680, 180), .97f);
            Text(62, 54, 630, 25, "YOUR JOURNEY  /  PAUSED", metricCaption);
            BrandScreen.Wordmark(new Rect(60, 91, 640, 65),42);
            Text(65, 164, 620, 28, "Progress saved locally · " + GameGraphics.Name + " graphics", small);
        }
        void DrawSpeedInstrument()
        {
            PresentationStyles(); var car = Session.Car;
            InterfaceTheme.Card(new Rect(1202, 636, 208, 157), .9f);
            Text(1218, 648, 174, 24, car.Reverse ? "REVERSE" : "DRIVE", metricCaption);
            Text(1214, 668, 123, 79, Mathf.RoundToInt(car.AbsoluteSpeed * Core.DrivingMath.MphPerMetreSecond).ToString(), speedDigits);
            Text(1344, 717, 50, 24, "MPH", small);
            float damage = Mathf.Clamp01(car.Damage / 100);
            InterfaceTheme.Fill(new Rect(1218, 771, 174, 3), new Color(.25f, .32f, .37f));
            if (damage > .005f) InterfaceTheme.Fill(new Rect(1218, 771, 174 * damage, 3), damage > .65f ? new Color(.94f, .37f, .25f) : InterfaceTheme.Gold);
            Text(1218, 746, 174, 22, "Condition " + Mathf.RoundToInt(100 - car.Damage) + "%", small);
            if (!Session.World.IsCircuit)
            {
                // A familiar roadside-limit disc, kept separate from car speed.
                InterfaceTheme.Disc(new Rect(1133, 695, 56, 56), new Color(.83f, .24f, .20f));
                InterfaceTheme.Disc(new Rect(1138, 700, 46, 46), InterfaceTheme.Paper);
                var old = heading.normal.textColor; var alignment = heading.alignment;
                heading.normal.textColor = InterfaceTheme.Navy; heading.alignment = TextAnchor.MiddleCenter;
                Text(1138, 701, 46, 44, Session.Limit.ToString(), heading);
                heading.normal.textColor = old; heading.alignment = alignment;
            }
        }
        void DrawJourneyStrip()
        {
            PresentationStyles(); var s = Session;
            InterfaceTheme.Card(new Rect(28, 28, 380, 96), .87f);
            string road = s.CurrentRoad?.name;
            if (string.IsNullOrWhiteSpace(road) || road == "road") road = s.World.Route.District(s.Progress);
            Text(46, 40, 344, 28, road, hudRoad);
            string area = s.World.Route.Region?.PlaceNameAt(s.Car.Body.position) ?? "";
            Text(46, 71, 344, 24, area, small);
            Text(46, 97, 344, 20, Mathf.FloorToInt(s.Conditions.Hour).ToString("00") + ":" + Mathf.FloorToInt(s.Conditions.Hour % 1 * 60).ToString("00") + (s.Conditions.Rain > .2f ? "  ·  Rain" : ""), metricCaption);
        }
    }
}
