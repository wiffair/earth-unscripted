using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class LapEconomyVerification
    {
        public static void RunAndBuild()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushBuildingLifecycleResult");
                if(at<0||at+1>=args.Length)throw new ArgumentException("Explicit QA result root required");
                string output=Path.Combine(Path.GetDirectoryName(args[at+1]),"ProfileMigration7");
                if(ProfileMigrationSafetyVerification.Verify(output)!=0)throw new InvalidDataException("Schema7 profile migration fixtures failed");
                if(RentalReleaseVerification.Verify("/private/tmp/rr020-rental-release-checks-"+Path.GetFileName(Path.GetDirectoryName(args[at+1])))!=0)throw new InvalidDataException("Populated migration/physical identity fixtures failed");
                BuildingWindowLifecycleVerification.RunAndBuild();
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
