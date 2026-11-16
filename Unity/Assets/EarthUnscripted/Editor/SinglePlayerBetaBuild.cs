using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    /// <summary>Builds the isolated Apple Silicon beta; never installs a live release.</summary>
    public static class SinglePlayerBetaBuild
    {
        public static void Build()
        {
            int resultCode = 1;
            try
            {
                ControllerFeedbackVerification.Run();
                Beta32PolicyVerification.Run();
                BuildingRoadClearanceVerification.Run();
                JunctionPavingVerification.Run();
                TransportControlVerification.Run();
                var arguments = Environment.GetCommandLineArgs();
                int outputArgument = Array.IndexOf(arguments, "-earthUnscriptedBetaApp");
                if (outputArgument < 0 || outputArgument + 1 >= arguments.Length) throw new ArgumentException("Fresh beta output required");
                string app = Path.GetFullPath(arguments[outputArgument + 1]);
                if (Path.GetFileName(app) != "Earth Unscripted Beta.app" || Directory.Exists(app)) throw new IOException("Fresh separate beta app required");
                PlayerSettings.companyName = "Earth Unscripted";
                PlayerSettings.productName = ProductIdentity.BetaName;
                PlayerSettings.bundleVersion = ProductIdentity.Version;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, SinglePlayerBeta.Identifier);
                PlayerSettings.runInBackground = true;
                var betaIcon=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/EarthUnscripted/Icon.png");
                if(!betaIcon)throw new IOException("Beta icon asset is missing");
                var iconSizes=PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone,IconKind.Any);
                var icons=new Texture2D[iconSizes.Length];
                for(int iconIndex=0;iconIndex<icons.Length;iconIndex++)icons[iconIndex]=betaIcon;
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone,icons,IconKind.Any);

                UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
                AssetDatabase.SaveAssets();
                var build = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, app, BuildTarget.StandaloneOSX, BuildOptions.None);
                if (build.summary.result != BuildResult.Succeeded) throw new IOException("Beta build failed");
                // macOS resolves competing app copies using the numeric build,
                // not the human-readable prerelease suffix. Unity otherwise
                // emits zero for prerelease version strings, so old test copies can
                // win launch resolution. This metadata is sealed afterwards.
                string infoPath=Path.Combine(app,"Contents/Info.plist");
                string infoText=File.ReadAllText(infoPath);
                var buildNumber=new Regex(@"(<key>CFBundleVersion</key>\s*<string>)[^<]*(</string>)");
                if(buildNumber.Matches(infoText).Count!=1)throw new IOException("Missing or ambiguous Mac build number");
                // Preserve Apple's original DTD. XDocument writes an empty []
                // internal subset that Apple's plist parser rejects at launch.
                File.WriteAllText(infoPath,buildNumber.Replace(infoText,"${1}52${2}"));
                string worker = Path.GetFullPath("AutomaticWorldWorker");
                string destination = Path.Combine(app, "Contents/Resources/AutomaticWorld");
                foreach (string source in Directory.GetFiles(worker, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(destination, source.Substring(worker.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, false);
                }
                resultCode = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally { EditorApplication.Exit(resultCode); }
        }
    }
}
