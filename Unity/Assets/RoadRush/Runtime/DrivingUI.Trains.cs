using UnityEngine;
namespace RoadRush
{
    public sealed partial class DrivingUI
    {
        void DrawTrainsAndTracks()
        {
            MenuGroup("TRAINS & TYRE TRACKS","Train traffic and visible tracks left by tyres.");var p=Session.Profile;
            p.simulatedTrains=StateToggle(new Rect(65,350,600,35),p.simulatedTrains,"Enable nearby trains",toggle);
            p.trainServiceMode=ControllerMenuGUI.SelectionGrid(new Rect(65,405,1260,45),p.trainServiceMode,new[]{"LOCAL SIMULATION","AUTOMATIC DATA","REPORTED POSITIONS"},3);
            string explanation=p.trainServiceMode==0?"Local trains follow available track. Their journeys and times are simulated.":p.trainServiceMode==1?"Automatically selects public rail sources for this area. Uses timetable estimates when available; generic types are labelled.":"Uses received train reports when available. Movement between reports is estimated; stale reports fall back to timetable estimates.";
            Text(65,475,600,110,explanation,body);
            var trains=Session.GetComponent<RailTrainSystem>();
            Text(735,475,580,110,trains?(p.trainServiceMode==0?trains.ActiveFleetCount+" active local trains":trains.FeedStatus):"Train traffic uses supported gauges and loaded railway geometry.",body);
            Text(735,600,580,130,"INCLUDED TRAIN TYPES\nClass 158 regional diesel\nClass 197 Civity · two or three cars\nClass 800 · five-car intercity\nClass 66 · locomotive and container wagons",small);
            p.trainFleetLimit=Mathf.RoundToInt(Slider(65,600,"Maximum nearby trains",p.trainFleetLimit,1,8,600));
            p.trainTimetableFallback=StateToggle(new Rect(65,735,640,35),p.trainTimetableFallback,"Use published timetables when connected data is unavailable",toggle);
            p.surfaceTracks=StateToggle(new Rect(65,690,600,35),p.surfaceTracks,"Snow and wet tyre tracks",toggle);
            Text(65,777,600,35,"Water prints fade quickly; compressed snow tracks last longer.",small);
            Text(735,745,580,65,"Published South Wales times work without an account. Live feeds await registration. Train types and between-stop positions are estimates.",small);
            if(Button(65,820,175,"BACK",37)){p.Save();tab=21;}
        }
    }
}
