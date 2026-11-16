using System;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class DrivingUI
    {
        void DrawDataSources()
        {
            MenuGroup("WORLD DATA SOURCES","Sources available for your location, with clear fallbacks.");
            var region=Session.World.Route.Region;var position=Session.Car.Body.position;
            double latitude=region.latitude+position.z/111319.4908,longitude=region.longitude+position.x/(111319.4908*Math.Cos(region.latitude*Math.PI/180));
            Text(65,350,1260,40,region.name+" · "+WorldProviderRegistry.Country(region.countryCode)+" · catalogue "+WorldProviderRegistry.Version,body);
            var terrain=WorldProviderRegistry.Resolve(longitude,latitude,region.countryCode,"terrain");
            Text(65,410,600,140,"TERRAIN\n"+string.Join("\n",terrain.Select(s=>s.Label+" · "+(s.Available?"available where covered":s.Reason)))+"\nMeasured coverage is checked tile by tile. Coarse ground height is an estimate.",body);
            var trains=Session.GetComponent<RailTrainSystem>();
            var live=WorldProviderRegistry.Resolve(longitude,latitude,region.countryCode,"rail.live");var timetable=WorldProviderRegistry.Resolve(longitude,latitude,region.countryCode,"rail.timetable");
            Text(735,410,600,180,"TRAIN DATA\n"+string.Join("\n",live.Concat(timetable).GroupBy(s=>s.Id).Select(g=>g.First()).Select(s=>s.Label+" · "+(s.Available?"available where covered":s.Reason)))+"\n"+(trains?trains.FeedStatus:"No railway session"),body);
            Text(65,630,600,145,"ROAD AND SCENERY DATA\nOpenStreetMap geometry. Data is converted to one internal format with source identity, age, quality and coordinate references.",body);
            Text(735,630,600,145,"Public sources are selected automatically in timetable/reported mode. Train types outside the UK use generic estimates on supported gauges. Unavailable or ambiguous tracks are skipped. Accounts stay optional.",body);
            if(Button(65,820,175,"BACK",37))tab=21;
        }
    }
}
