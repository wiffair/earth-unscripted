using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoadRush.Editor
{
    /// <summary>
    /// Build only the isolated actual-provider observer. Reuses genuine scoped
    /// session identity and eight reviewed source/cook inputs; additionally binds
    /// the exact twenty-job query file. No source owner or world is constructed by
    /// a manifest. Settings are restored byte-for-byte before Editor exit.
    /// </summary>
    public static class ActualProviderQaBuild800
    {
        [Serializable] public sealed class Restoration
        {
            public string status, beforeSHA256, afterSHA256, error;
        }
        static ActualProviderNativeProof800.FilePin[] CaptureSources() => Directory
            .GetFiles("Assets/RoadRush", "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new ActualProviderNativeProof800.FilePin
            { path = path.Replace('\\', '/'), bytes = new FileInfo(path).Length, sha256 = Core.DataPackageRules.HashFile(path) })
            .ToArray();
        static bool SameSources(ActualProviderNativeProof800.FilePin[] first,
            ActualProviderNativeProof800.FilePin[] second) => first.Length == second.Length && first.Zip(second,
                (a, b) => a.path == b.path && a.bytes == b.bytes && a.sha256 == b.sha256).All(equal => equal);

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
                int index = Array.IndexOf(arguments, "-roadRushProviderNativeApp800");
                if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException("Explicit fresh QA app");
                app = Path.GetFullPath(arguments[index + 1]);
                if (!app.StartsWith("/private/tmp/RoadRushScopedSessionQA-", StringComparison.Ordinal) ||
                    !app.EndsWith(".app", StringComparison.Ordinal) || Directory.Exists(app) ||
                    File.Exists(app + "-binding.json") || File.Exists(app + "-build-restore.json") || File.Exists(app + "-profile-template.json"))
                    throw new IOException("Fresh private isolated app only");
                var sources = CaptureSources();
                if (sources.Length == 0 || sources.Length > 4096) throw new IOException("Source budget");
                string Required(string flag)
                {
                    int position = Array.IndexOf(arguments, flag);
                    if (position < 0 || position != Array.LastIndexOf(arguments, flag) || position + 1 >= arguments.Length)
                        throw new ArgumentException("Exactly one explicit build argument " + flag);
                    return Path.GetFullPath(arguments[position + 1]);
                }
                string cooked = Required("-roadRushScopedCooked800");
                string context = Required("-roadRushScopedSourceContext800");
                string inputs = Required("-roadRushScopedInputs800");
                string placement = Required("-roadRushProviderPlacementJobs800");
                if (new FileInfo(placement).Length > 1024 * 1024 || Core.DataPackageRules.HashFile(placement) != "511046597b742f901d0bb9f5a444b77d299f0e1686e254b282310bf073a325f4")
                    throw new IOException("Exact original placement query data required");
                // Fixed order is shared with runtime/external guard; all eight
                // payloads are immutable inputs, never a manifest capability.
                string[] paths = { cooked, cooked + ".rrmeta", Path.Combine(inputs, "original-envelope.json"),
                    Path.Combine(context, "pantside-3000-source.json"),
                    Path.Combine(context, "pantside-3000-grid-f32.bin"),
                    Path.Combine(context, "pantside-3000-full-regional-raw-review.json"),
                    Path.Combine(context, "pantside-3000-source-provenance-review.json"),
                    Path.Combine(context, "readonly-source-capture.json") };
                var prepared = paths.Select(path => new ActualProviderNativeProof800.FilePin {
                    path = path, bytes = new FileInfo(path).Length, sha256 = Core.DataPackageRules.HashFile(path) }).ToArray();
                if (prepared[0].sha256 != TerrainDraft.Core800.VerifiedCoreTerrain.ReviewedCookedArtifactSHA256 ||
                    prepared[1].sha256 != TerrainDraft.Core800.VerifiedCoreTerrain.ReviewedCookedPackageSHA256 ||
                    prepared[2].sha256 != ReviewedCanonicalRaw13Input.ExpectedBodySHA256)
                    throw new IOException("Reviewed exact cooked/raw source pins required");
                PlayerSettings.companyName = ActualProviderNativeProof800.Company;
                PlayerSettings.productName = ActualProviderNativeProof800.Product;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,
                    ActualProviderNativeProof800.Identifier);
                PlayerSettings.runInBackground = true;
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
                var build = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, app,
                    BuildTarget.StandaloneOSX, BuildOptions.None);
                if (build.summary.result != BuildResult.Succeeded || !SameSources(sources, CaptureSources()))
                    throw new IOException("Build failed or source set changed during build");
                string executable = Directory.GetFiles(Path.Combine(app, "Contents/MacOS"))
                    .Single(path => !Path.GetFileName(path).StartsWith("."));
                // Legitimate isolated saved input, authored from real defaults.
                // Never copy or synthesize fields on the actual live GameSession.
                string seedPath = app + "-profile-template.json";
                var seed = new PlayerProfile { region = "", customStart = false, livingScenery = false,
                    detailedScenery = false, weatherMode = 0, clockMode = 0, quality = 2 };
                File.WriteAllText(seedPath, JsonUtility.ToJson(seed, true));
                var binding = new ActualProviderNativeProof800.Binding
                {
                    sourceContext = context, preparedInputs = prepared,
                    placementJobs = new ActualProviderNativeProof800.FilePin { path = placement, bytes = new FileInfo(placement).Length, sha256 = Core.DataPackageRules.HashFile(placement) },
                    profileTemplateSHA256 = Core.DataPackageRules.HashFile(seedPath),
                    recipe = ActualProviderNativeProof800.Recipe, buildGUID = build.summary.guid.ToString(),
                    executableSHA256 = Core.DataPackageRules.HashFile(executable), sourceFiles = sources,
                    timeManagerSHA256 = Core.DataPackageRules.HashFile("ProjectSettings/TimeManager.asset"),
                    dynamicsSHA256 = Core.DataPackageRules.HashFile("ProjectSettings/DynamicsManager.asset"),
                    appFiles = Directory.GetFiles(app, "*", SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.Ordinal).Select(path => new ActualProviderNativeProof800.FilePin
                        { path = path.Substring(app.Length + 1).Replace('\\', '/'), bytes = new FileInfo(path).Length,
                            sha256 = Core.DataPackageRules.HashFile(path) }).ToArray()
                };
                if (binding.appFiles.Length > 4096) throw new IOException("App manifest budget");
                File.WriteAllText(app + "-binding.json", JsonUtility.ToJson(binding, true));
                exit = 0;
            }
            catch (Exception exception) { error = exception.ToString(); Debug.LogException(exception); }
            finally
            {
                try
                {
                    PlayerSettings.companyName = company; PlayerSettings.productName = product;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, identifier);
                    PlayerSettings.runInBackground = background;
                    UnityEditor.OSXStandalone.UserBuildSettings.architecture = architecture;
                }
                catch (Exception exception) { error = (error ?? "") + "\n" + exception; exit = 1; }
                finally
                {
                    try { File.WriteAllBytes(Settings, saved); }
                    catch (Exception exception) { error = (error ?? "") + "\n" + exception; exit = 1; }
                }
                string after = Core.DataPackageRules.HashFile(Settings);
                if (after != before) { error = (error ?? "") + "\nSettings restoration mismatch"; exit = 1; }
                if (app != null) File.WriteAllText(app + "-build-restore.json", JsonUtility.ToJson(new Restoration
                { status = exit == 0 && error == null ? "PASS" : "FAIL", beforeSHA256 = before,
                    afterSHA256 = after, error = error }, true));
            }
            EditorApplication.Exit(exit);
        }
    }
}
