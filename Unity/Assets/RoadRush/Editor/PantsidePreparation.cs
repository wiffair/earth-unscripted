using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class PantsidePreparation
    {
        public static void Run()
        {
            string priorProfile=PlayerProfile.StorageOverride,priorFolder=RegionCache.OverrideFolder;
            try
            {
                string root=Path.GetFullPath("Builds/Verification/Pantside");Directory.CreateDirectory(root);PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
                var place=new WorldPlace{name="Pantside · NP11 5DB",latitude=51.671156,longitude=-3.131748,country_code="GB",timezone="Europe/London",drivingSide=-1,size=3000};
                var pack=new CountryPack(Path.GetFullPath("WorldPacks/UnitedKingdom"));if(!pack.Covers(place))throw new Exception("Installed country pack does not cover Pantside");
                string source=pack.Extract(place);var raw=GlobalRegions.Convert(place,source,pack.Grid(place,65),65,place.size);raw.id=RegionCache.PackId(place,pack);raw.datasetVersion=pack.Name+"/"+pack.Version;raw.datasetSource=pack.ReplicationSource;RegionCache.Save(raw,source);File.WriteAllText(Path.Combine(root,"region-id.txt"),raw.id);
                var region=GeoRegion.Load(raw.id);var road=region.Nearest(Vector3.zero,out var nearest,out float distance);var route=new RoutePath(region);float progress=route.Closest(Vector3.zero,out _,out float routeGap);
                var videoRoute=RoadDemoRoute.Build(region,Vector3.zero);float videoGap=new Vector2(videoRoute.Points[0].x,videoRoute.Points[0].z).magnitude;
                var names=region.roads.Where(r=>r.points.Any(p=>new Vector2(p.x,p.z).magnitude<350)).Select(r=>r.name).Distinct().ToArray();
                string details=$"Prepared Pantside/NP11 5DB from installed {pack.Name}/{pack.Version}\npostcodeCentroid=51.671156,-3.131748\nroads={region.roads.Length} buildings={region.buildings.Length}\nnearestRoad={road.name} distance={distance:F1}m\npreviewRouteGap={routeGap:F1}m startProgress={progress:F1}m\nvideoRouteStartGap={videoGap:F1}m videoRouteLength={videoRoute.Length:F1}m\nnearbyRoads="+string.Join(", ",names)+"\nRecord only after next playable release passes verification.\n";
                File.WriteAllText(Path.Combine(root,"preparation.txt"),details);File.WriteAllText(Path.Combine(root,"place.json"),JsonUtility.ToJson(place,true));Debug.Log(details);
                File.WriteAllText(Path.Combine(root,"POSTCODE_SOURCE.txt"),"Postcode centroid lookup: https://api.postcodes.io/postcodes/NP115DB\nDocumentation/licences: https://postcodes.io/docs/licences/\nGreat Britain postcode data: OS OpenData licence.\nContains Ordnance Survey data © Crown copyright and database right2025.\nContains Royal Mail data © Royal Mail copyright and database right2025.\nContains National Statistics data © Crown copyright and database right2025.\nContains NRS data © Crown copyright and database right2025.\nRoads/buildings: © OpenStreetMap contributors, ODbL. Terrain: Copernicus DEM GLO90. Fictional scenery.\n");
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=priorProfile;RegionCache.OverrideFolder=priorFolder;}
        }
    }
}
