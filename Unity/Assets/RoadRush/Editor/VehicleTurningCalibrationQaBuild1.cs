using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoadRush.Editor
{
    /// <summary>Editor-only fresh-output build transaction for actual native turning calibration.</summary>
    public static class VehicleTurningCalibrationQaBuild1
    {
        /// <summary>Exact settings-restoration receipt for the external launch guard.</summary>
        [Serializable]
        public sealed class Restoration
        {
            public string status, beforeSHA256, afterSHA256, error;
        }

        private static string RequiredArg(string[] arguments, string flag)
        {
            int index = Array.IndexOf(arguments, flag);
            if (index < 0 || index + 1 >= arguments.Length)
                throw new ArgumentException("Missing explicit QA argument: " + flag);
            return arguments[index + 1];
        }

        /// <summary>
        /// Build an isolated opt-in QA product, pin compiled source/app and private flat-surface recipe,
        /// then restore exact ProjectSettings bytes in finally before Editor exit.
        /// No road/world source data is read or generated here and no production app is launched.
        /// </summary>
        public static void Build()
        {
            int exit = 1;
            string app = null;
            string settings = "ProjectSettings/ProjectSettings.asset";
            byte[] saved = File.ReadAllBytes(settings);
            string before = World13DocumentCodec.Hash(saved);
            string company = PlayerSettings.companyName, product = PlayerSettings.productName, id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            bool background = PlayerSettings.runInBackground;
            var arch = UnityEditor.OSXStandalone.UserBuildSettings.architecture;
            string restoreError = null;
            try
            {
                if (Application.isPlaying || UnityEngine.Object.FindAnyObjectByType<GameSession>())
                    throw new InvalidOperationException("Editor-only isolated QA build");
                var a = Environment.GetCommandLineArgs();
                int k = Array.IndexOf(a, "-roadRushTurningApp1");
                if (k < 0 || k + 1 >= a.Length)
                    throw new ArgumentException("Fresh private app path required");
                app = Path.GetFullPath(a[k + 1]);
                if (!app.StartsWith("/private/tmp/RoadRushTurningCalibrationQA1-", StringComparison.Ordinal) || !app.EndsWith(".app", StringComparison.Ordinal) || Directory.Exists(app) || File.Exists(app + "-binding.json") || File.Exists(app + "-build-restore.json"))
                    throw new IOException("Fresh isolated QA output only");
                PlayerSettings.companyName = "RoadRush Verification";
                PlayerSettings.productName = "TurningCalibrationQA1";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "games.blackwood.roadrush.qa.turning1");
                PlayerSettings.runInBackground = true;
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
                var build = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, app, BuildTarget.StandaloneOSX, BuildOptions.None);
                if (build.summary.result != BuildResult.Succeeded)
                    throw new Exception("QA build failed");
                string exe = Directory.GetFiles(Path.Combine(app, "Contents/MacOS")).Single(p => !Path.GetFileName(p).StartsWith("."));
                var binding = new VehicleTurningCalibrationNative1.Binding
                {
                    recipe = VehicleTurningCalibrationNative1.Recipe,
                    buildGUID = build.summary.guid.ToString(),
                    sourceSHA256 = Core.DataPackageRules.HashFile("Assets/RoadRush/Runtime/VehicleTurningCalibrationNative1.cs"),
                    gameSessionSHA256 = Core.DataPackageRules.HashFile("Assets/RoadRush/Runtime/GameSession.cs"),
                    timeManagerSHA256 = Core.DataPackageRules.HashFile("ProjectSettings/TimeManager.asset"),
                    appExecutableSHA256 = Core.DataPackageRules.HashFile(exe),
                    surfaceRecipeSHA256 = VehicleTurningCalibrationNative1.SurfaceDigest
                };
                binding.sourceFiles = Directory.GetFiles("Assets/RoadRush", "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Select(p => new VehicleTurningCalibrationNative1.FilePin { path = p.Replace('\\', '/'), bytes = new FileInfo(p).Length, sha256 = Core.DataPackageRules.HashFile(p) }).ToArray();
                binding.appFiles = Directory.GetFiles(app, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Select(p => new VehicleTurningCalibrationNative1.FilePin { path = p.Substring(app.Length + 1).Replace('\\', '/'), bytes = new FileInfo(p).Length, sha256 = Core.DataPackageRules.HashFile(p) }).ToArray();
                if (binding.sourceFiles.Length > 4096 || binding.appFiles.Length > 4096)
                    throw new IOException("Bounded QA build manifest limit");
                File.WriteAllText(app + "-binding.json", JsonUtility.ToJson(binding, true));
                exit = 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                // Restore exact disk bytes even if an Editor API restoration itself throws.
                try
                {
                    PlayerSettings.companyName = company;
                    PlayerSettings.productName = product;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, id);
                    PlayerSettings.runInBackground = background;
                    UnityEditor.OSXStandalone.UserBuildSettings.architecture = arch;
                }
                catch (Exception e)
                {
                    restoreError = e.ToString();
                    exit = 1;
                }
                finally
                {
                    try
                    {
                        File.WriteAllBytes(settings, saved);
                    }
                    catch (Exception e)
                    {
                        restoreError = (restoreError ?? "") + "\nDisk restoration: " + e;
                        exit = 1;
                    }
                }

                string after = Core.DataPackageRules.HashFile(settings);
                if (after != before)
                {
                    restoreError = (restoreError ?? "") + "\nProjectSettings.asset bytes changed";
                    exit = 1;
                }

                if (app != null)
                    File.WriteAllText(app + "-build-restore.json", JsonUtility.ToJson(new Restoration { status = restoreError == null && after == before ? "PASS" : "FAIL", beforeSHA256 = before, afterSHA256 = after, error = restoreError }, true));
            }

            Debug.Log("CORE800 QA BUILD " + (exit == 0 ? "PASS" : "FAIL") + " " + app);
            EditorApplication.Exit(exit);
        }
    }
}
