using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class RailwayPreparation
    {
        public static void Pantside()
        {
            string previous=RegionCache.OverrideFolder;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-earthUnscriptedRailCountry");if(at<0||at+1>=args.Length)throw new Exception("Supply the explicit installed country folder");
                var pack=new CountryPack(Path.GetFullPath(args[at+1]));if(!pack.RailwaysComplete)throw new Exception("A complete migrated rail source is required");
                string output=Path.GetFullPath("Builds/Verification/RailwayStandard");Directory.CreateDirectory(output);RegionCache.OverrideFolder=Path.Combine(output,"Regions");Directory.CreateDirectory(RegionCache.Folder);
                var place=new WorldPlace{name="Pantside · NP11 5DB",latitude=51.671156,longitude=-3.131748,country_code="GB",timezone="Europe/London",size=3000,drivingSide=-1};
                string source=pack.Extract(place);var region=GlobalRegions.Convert(place,source,pack.Grid(place,65),65,3000);region.id="download-pantside-rail-proof";region.datasetVersion=pack.Name+"/"+pack.Version;region.datasetSource=pack.ReplicationSource;
                File.WriteAllText(RegionCache.PathFor(region.id),JsonUtility.ToJson(region));region=GeoRegion.Load(region.id);
                File.WriteAllText(Path.Combine(output,"pantside-world.json"),JsonUtility.ToJson(region));File.WriteAllText(Path.Combine(output,"source.json"),source);
                string receipt=$"PASS standard-world extraction; no optional detail packs applied\nCountry={pack.Name}/{pack.Version}\nRailSource={pack.RailwayIdentity}\nRoads={region.roads.Length}\nRailways={region.railways.Length}\nRailPoints={region.railways.Sum(r=>r.points.Length)}\nBridgeStreet={region.roads.Count(r=>r.id.StartsWith("1109998597-",StringComparison.Ordinal))}\nBridgeA={region.railways.Count(r=>r.id.StartsWith("railway-256150001-",StringComparison.Ordinal))}\nBridgeB={region.railways.Count(r=>r.id.StartsWith("railway-1108948870-",StringComparison.Ordinal))}\n";
                if(!region.railways.Any(r=>r.id.StartsWith("railway-256150001-",StringComparison.Ordinal))||!region.railways.Any(r=>r.id.StartsWith("railway-1108948870-",StringComparison.Ordinal)))throw new Exception("Expected both Bridge Street mapped bridge ways");
                File.WriteAllText(Path.Combine(output,"preparation.txt"),receipt);Debug.Log(receipt);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{RegionCache.OverrideFolder=previous;}
        }
    }
}
