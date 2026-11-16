using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class AutomaticWorldQaBuild
    {
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-automaticWorldApp");
            if (at < 0 || at + 1 == args.Length) throw new ArgumentException("Fresh QA app required");
            string app = Path.GetFullPath(args[at + 1]);
            if (!app.StartsWith("/private/tmp/RoadRushAutomaticQA-", StringComparison.Ordinal) ||
                !app.EndsWith(".app", StringComparison.Ordinal) || Directory.Exists(app)) throw new IOException("Fresh isolated app only");
            byte[] settings = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
            string company = PlayerSettings.companyName, product = PlayerSettings.productName;
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            var architecture = UnityEditor.OSXStandalone.UserBuildSettings.architecture;
            bool background = PlayerSettings.runInBackground;
            int exit = 1;
            try
            {
                PlayerSettings.companyName = "RoadRush Verification";
                PlayerSettings.productName = "RoadRush Automatic QA";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "games.blackwood.roadrush.qa.automaticworld");
                PlayerSettings.runInBackground = true;
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
                var result = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, app, BuildTarget.StandaloneOSX, BuildOptions.None);
                if (result.summary.result != BuildResult.Succeeded) throw new IOException("Main-game QA build failed");
                string source = Path.GetFullPath("AutomaticWorldWorker");
                // This package contains both signed Mach-O code and Python/GIS
                // data. Seal its exact files as resources; an unbundled mixed
                // directory under Helpers makes codesign treat licence/data
                // files as unsigned nested executables.
                string destination = Path.Combine(app, "Contents/Resources/AutomaticWorld");
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(destination, file.Substring(source.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, false);
                }
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                PlayerSettings.companyName = company; PlayerSettings.productName = product;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, id);
                PlayerSettings.runInBackground = background;
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = architecture;
                File.WriteAllBytes("ProjectSettings/ProjectSettings.asset", settings);
                EditorApplication.Exit(exit);
            }
        }
    }
}
