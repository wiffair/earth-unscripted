using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using RoadRush.Scenery800.Diagnostics;

namespace RoadRush.Editor
{
    /// <summary>
    /// Explicit Root-run PlayMode coordinator. Suppress/isolate BEFORE entering
    /// PlayMode, restore across domain reload, and delegate geometry to real Runtime
    /// issuers. The external reviewed launcher owns source/settings/production guards.
    /// </summary>
    [InitializeOnLoad]
    public static class Case7FacadePixelVerification800
    {
        const string Key = "RoadRushCase7FacadePixelDiagnostic1";
        const double EditorTimeoutSeconds = 660;
        static double deadline;
        static bool runnerCreated, finishing;
        static int exitCode = 1;
        static string output;
        static Case7FacadePixelVerification800()
        {
            if (!SessionState.GetBool(Key, false))
                return;
            output = SessionState.GetString(Key + "Output", "");
            deadline = SessionState.GetFloat(Key + "Deadline", 0);
            exitCode = SessionState.GetInt(Key + "ExitCode", 1);
            finishing = SessionState.GetBool(Key + "Finishing", false);
            Isolate();
            Hook();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void SuppressBeforeSceneReady()
        {
            if (SessionState.GetBool(Key, false))
                Isolate();
        }

        static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Isolate()
        {
            GameSession.SuppressAutomaticLaunch = true;
            PlayerProfile.StorageOverride = Path.Combine(output, "isolated", "unused-profile.json");
            RegionCache.OverrideFolder = Path.Combine(output, "isolated", "unused-regions");
            CountryUpdateService.OverrideFolder = Path.Combine(output, "isolated", "unused-updates");
            CountryUpdateService.AllowVerification = false;
        }

        static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            if (index < 0 || index != Array.LastIndexOf(args, key) || index + 1 >= args.Length)
                throw new ArgumentException(key);
            return Path.GetFullPath(args[index + 1]);
        }

        static void SaveOverride(string suffix, string value)
        {
            SessionState.SetBool(Key + suffix + "Null", value == null);
            SessionState.SetString(Key + suffix, value ?? "");
        }

        static string Previous(string suffix)
        {
            return SessionState.GetBool(Key + suffix + "Null", false) ? null : SessionState.GetString(Key + suffix, "");
        }

        public static void Run()
        {
            try
            {
                BeginRun();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (!SessionState.GetBool(Key, false))
                {
                    EditorApplication.Exit(1);
                    return;
                }

                exitCode = 1;
                finishing = true;
                SessionState.SetInt(Key + "ExitCode", 1);
                SessionState.SetBool(Key + "Finishing", true);
                Hook();
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    FinishEditor();
            }
        }

        static void BeginRun()
        {
            if (SessionState.GetBool(Key, false) || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.sceneCount != 1 || UnityEngine.SceneManagement.SceneManager.GetSceneAt(0).isDirty || UnityEngine.SceneManagement.SceneManager.GetSceneAt(0).rootCount != 0)
                throw new InvalidOperationException("Fresh process and clean empty scene required; never replace user scene");
            output = Argument("-roadRushFacadePixelOutput");
            if (!output.StartsWith("/private/tmp/rr019-case7-facade-pixels-", StringComparison.Ordinal) || File.Exists(output) || Directory.Exists(output))
                throw new IOException("Fresh private output required");
            string context = Argument("-roadRushFacadePixelSourceContext");
            string regional = Argument("-roadRushFacadePixelRegionalRoot");
            var args = Environment.GetCommandLineArgs();
            int modeIndex = Array.IndexOf(args, "-roadRushFacadeObservation800");
            if (modeIndex < 0 || modeIndex != Array.LastIndexOf(args, "-roadRushFacadeObservation800") || modeIndex + 1 >= args.Length ||
                (args[modeIndex + 1] != "full" && args[modeIndex + 1] != "none"))
                throw new ArgumentException("Exactly one explicit full|none facade observation mode required");
            string observationMode = args[modeIndex + 1];
            Directory.CreateDirectory(output);
            SaveOverride("Storage", PlayerProfile.StorageOverride);
            SaveOverride("Regions", RegionCache.OverrideFolder);
            SaveOverride("Updates", CountryUpdateService.OverrideFolder);
            SessionState.SetBool(Key + "Allow", CountryUpdateService.AllowVerification);
            SessionState.SetBool(Key + "Suppress", GameSession.SuppressAutomaticLaunch);
            SessionState.SetString(Key + "Output", output);
            SessionState.SetString(Key + "Context", context);
            SessionState.SetString(Key + "Regional", regional);
            SessionState.SetString(Key + "ObservationMode", observationMode);
            deadline = EditorApplication.timeSinceStartup + EditorTimeoutSeconds;
            SessionState.SetFloat(Key + "Deadline", (float)deadline);
            SessionState.SetInt(Key + "ExitCode", 1);
            SessionState.SetBool(Key + "Finishing", false);
            SessionState.SetBool(Key, true);
            Isolate();
            Hook();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var early = new GameObject(Case7FacadePixelProof800.ClearCameraName).AddComponent<Camera>();
            early.clearFlags = CameraClearFlags.SolidColor;
            early.backgroundColor = new Color(.055f, .075f, .10f);
            early.cullingMask = 0;
            early.gameObject.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Base;
            // No AudioListener/MainCamera tag or GameSession creation.
            EditorApplication.EnterPlaymode();
        }

        static Case7FacadeCollector800.Mode ReadObservationMode()
        {
            string value = SessionState.GetString(Key + "ObservationMode", "");
            if (value == "full") return Case7FacadeCollector800.Mode.Full;
            if (value == "none") return Case7FacadeCollector800.Mode.None;
            throw new InvalidOperationException("Missing or invalid persisted facade observation mode");
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("660-second facade Editor watchdog");
                if (finishing)
                {
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.ExitPlaymode();
                        return;
                    }

                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                        return;
                    FinishEditor();
                    return;
                }

                if (!EditorApplication.isPlaying || runnerCreated)
                    return;
                if (UnityEngine.Object.FindAnyObjectByType<GameSession>())
                    throw new InvalidOperationException("Unexpected normal GameSession");
                runnerCreated = true;
                new GameObject("Root explicit derived Case7 inspector").AddComponent<Case7FacadePixelProof800>().Begin(output, SessionState.GetString(Key + "Context", ""), SessionState.GetString(Key + "Regional", ""),
                    ReadObservationMode(), (passed, error) =>
                {
                    exitCode = passed ? 0 : 1;
                    finishing = true;
                    SessionState.SetInt(Key + "ExitCode", exitCode);
                    SessionState.SetBool(Key + "Finishing", true);
                    EditorApplication.ExitPlaymode();
                });
            }
            catch (Exception error)
            {
                exitCode = 1;
                finishing = true;
                SessionState.SetInt(Key + "ExitCode", 1);
                SessionState.SetBool(Key + "Finishing", true);
                try { File.WriteAllText(Path.Combine(output, "editor-failure.json"), new JObject { { "error", error.ToString() } }.ToString()); }
                catch { /* Keep exiting even if diagnostic I/O fails. External guard retains process/log evidence. */ }
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    FinishEditor();
            }
        }

        static void FinishEditor()
        {
            bool failed = false;
            void Attempt(Action work) { try { work(); } catch { failed = true; } }
            try
            {
                Attempt(() => EditorApplication.update -= Tick);
                Attempt(() => PlayerProfile.StorageOverride = Previous("Storage"));
                Attempt(() => RegionCache.OverrideFolder = Previous("Regions"));
                Attempt(() => CountryUpdateService.OverrideFolder = Previous("Updates"));
                Attempt(() => CountryUpdateService.AllowVerification = SessionState.GetBool(Key + "Allow", false));
                Attempt(() => GameSession.SuppressAutomaticLaunch = SessionState.GetBool(Key + "Suppress", false));
                Attempt(() => SessionState.SetBool(Key, false));
            }
            finally { EditorApplication.Exit(failed ? 1 : exitCode); }
        }
    }
}
