using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoadRush.Editor
{
    /// <summary>Fresh isolated native QA build; exact settings/source set restored/checked.</summary>
    public static class EngineerHelpQaBuild
    {
        [Serializable]
        public sealed class Restoration
        {
            public string status, beforeSHA256, afterSHA256, error;
        }

        static EngineerHelpNativeProof.FilePin[] CaptureSources() => Directory.GetFiles("Assets/RoadRush", "*.cs", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => new EngineerHelpNativeProof.FilePin { path = path.Replace('\\', '/'), bytes = new FileInfo(path).Length, sha256 = Core.DataPackageRules.HashFile(path) }).ToArray();
        static bool SameSources(EngineerHelpNativeProof.FilePin[] first, EngineerHelpNativeProof.FilePin[] second) => first.Length == second.Length && first.Zip(second, (a, b) => a.path == b.path && a.bytes == b.bytes && a.sha256 == b.sha256).All(equal => equal);
        public static void Build()
        {
            const string Settings = "ProjectSettings/ProjectSettings.asset";
            byte[] saved = File.ReadAllBytes(Settings);
            string before = Core.DataPackageRules.HashFile(Settings);
            string company = PlayerSettings.companyName, product = PlayerSettings.productName;
            string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            bool background = PlayerSettings.runInBackground;
            var architecture = UnityEditor.OSXStandalone.UserBuildSettings.architecture;
            string app = null, error = null;
            int exit = 1;
            try
            {
                if (Application.isPlaying || UnityEngine.Object.FindAnyObjectByType<GameSession>())
                    throw new InvalidOperationException("Editor-only isolated build");
                var arguments = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(arguments, "-roadRushEngineerHelpApp");
                if (index < 0 || index + 1 >= arguments.Length)
                    throw new ArgumentException("Explicit fresh QA app");
                app = Path.GetFullPath(arguments[index + 1]);
                if (!app.StartsWith("/private/tmp/RoadRushEngineerHelpQA-", StringComparison.Ordinal) || !app.EndsWith(".app", StringComparison.Ordinal) || Directory.Exists(app) || File.Exists(app + "-binding.json") || File.Exists(app + "-build-restore.json"))
                    throw new IOException("Fresh private isolated app only");
                var sources = CaptureSources();
                if (sources.Length == 0 || sources.Length > 4096)
                    throw new IOException("Source budget");
                PlayerSettings.companyName = "RoadRush Verification";
                PlayerSettings.productName = "EngineerHelpQA";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "games.blackwood.roadrush.qa.engineerhelp");
                PlayerSettings.runInBackground = true;
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
                var build = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, app, BuildTarget.StandaloneOSX, BuildOptions.None);
                if (build.summary.result != BuildResult.Succeeded || !SameSources(sources, CaptureSources()))
                    throw new IOException("Build failed or source set changed during build");
                string executable = Directory.GetFiles(Path.Combine(app, "Contents/MacOS")).Single(path => !Path.GetFileName(path).StartsWith("."));
                var binding = new EngineerHelpNativeProof.Binding
                {
                    recipe = EngineerHelpNativeProof.Recipe,
                    buildGUID = build.summary.guid.ToString(),
                    executableSHA256 = Core.DataPackageRules.HashFile(executable),
                    sourceFiles = sources,
                    timeManagerSHA256 = Core.DataPackageRules.HashFile("ProjectSettings/TimeManager.asset"),
                    dynamicsSHA256 = Core.DataPackageRules.HashFile("ProjectSettings/DynamicsManager.asset"),
                    appFiles = Directory.GetFiles(app, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => new EngineerHelpNativeProof.FilePin { path = path.Substring(app.Length + 1).Replace('\\', '/'), bytes = new FileInfo(path).Length, sha256 = Core.DataPackageRules.HashFile(path) }).ToArray()
                };
                if (binding.appFiles.Length > 4096)
                    throw new IOException("App manifest budget");
                File.WriteAllText(app + "-binding.json", JsonUtility.ToJson(binding, true));
                exit = 0;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                Debug.LogException(exception);
            }
            finally
            {
                try
                {
                    PlayerSettings.companyName = company;
                    PlayerSettings.productName = product;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, identifier);
                    PlayerSettings.runInBackground = background;
                    UnityEditor.OSXStandalone.UserBuildSettings.architecture = architecture;
                }
                catch (Exception exception)
                {
                    error = (error ?? "") + "\n" + exception;
                    exit = 1;
                }
                finally
                {
                    try
                    {
                        File.WriteAllBytes(Settings, saved);
                    }
                    catch (Exception exception)
                    {
                        error = (error ?? "") + "\n" + exception;
                        exit = 1;
                    }
                }

                string after = Core.DataPackageRules.HashFile(Settings);
                if (after != before)
                {
                    error = (error ?? "") + "\nSettings restoration mismatch";
                    exit = 1;
                }

                if (app != null)
                    File.WriteAllText(app + "-build-restore.json", JsonUtility.ToJson(new Restoration { status = exit == 0 && error == null ? "PASS" : "FAIL", beforeSHA256 = before, afterSHA256 = after, error = error }, true));
            }

            EditorApplication.Exit(exit);
        }
    }
}
