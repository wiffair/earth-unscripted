using System;
using System.IO;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Explicit isolated native QA driver. Uses normal menu/location handlers,
    /// survives scene loading, and observes the normal activation component in each scene.</summary>
    public sealed class RegionalPackSwitchCapture : MonoBehaviour
    {
        string prefix, archive, home, selectedHash;
        int phase, frames, switches;
        double deadline;
        GameSession current, previous;
        RegionalPackWindowController retiredController;
        WorldPackMenu menu;
        bool prepared, finished;
        readonly JArray observations = new JArray();

        internal void Initialize(GameSession session, string output, string source)
        {
            if (string.IsNullOrEmpty(PlayerProfile.StorageOverride) || Path.GetFullPath(PlayerProfile.StoragePath) != Path.GetFullPath(output + "-profile.json"))
                throw new InvalidOperationException("Switch capture requires its exact isolated profile");
            current = session; prefix = output; archive = source; home = session.World.Route.Region.id;
            DontDestroyOnLoad(gameObject); Application.runInBackground = true; deadline = Time.realtimeSinceStartupAsDouble + 180;
        }
        void Prepare()
        {
            if (prepared) return;
            if (current.Conditions.WeatherRequests != 0 || current.Updates && current.Updates.SourceRequests != 0)
                throw new InvalidOperationException("Switch capture attempted a provider request");
            if (current.Updates) { current.Updates.Enabled = false; current.Updates.Automatic = false; }
            current.Conditions.CancelLocalFetch(); current.Car.StartLocked = true; current.Input.enabled = false;
            current.Input.Throttle = current.Input.Steer = 0; current.Input.Brake = current.Input.Handbrake = 1;
            foreach (var police in FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None)) police.gameObject.SetActive(false);
            foreach (var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None)) traffic.gameObject.SetActive(false);
            current.SetMenu(true); current.GetComponent<DrivingUI>().ShowImportedPacks(); menu = current.GetComponent<DrivingUI>().ImportedPacks; prepared = true;
        }
        bool Ready(string id)
        {
            var next = FindAnyObjectByType<GameSession>();
            if (!next || next == previous || !next.BootReady || LocationLoading.Busy) return false;
            if ((next.World.Route.Region?.id ?? "") != id) throw new InvalidOperationException("Wrong switched region");
            if (FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length != 1) throw new InvalidOperationException("More than one session after switch");
            current = next; Prepare(); return true;
        }
        RegionalPackActivation Activation => current.GetComponent<RegionalPackActivation>();
        bool WindowReady(bool expectedPack)
        {
            var activation = Activation; var world = current.World.GeographicWorld;
            if (!activation || activation.PreferenceBusy || menu.Busy) return false;
            if(menu.RevisionCount!=1)throw new InvalidOperationException("Switched scene lost imported pack inventory");
            if (!activation.Selected.Enabled || !activation.Selected.AllowCandidatePreview || activation.Selected.ManifestSha256 != selectedHash)
                throw new InvalidOperationException("Scene switch changed saved selection");
            if (!world) throw new InvalidOperationException("Geographic world missing");
            var controller = activation.Controller;
            if (!controller || controller.Busy || controller.QueriesCompleted < 1) return false;
            if ((world.BuildingWindowIdentity != null) != expectedPack || (controller.LastRegionalParts > 0) != expectedPack)
                throw new InvalidOperationException("Incorrect source coverage after switch: " + activation.Status);
            return true;
        }
        void Observe(string stage)
        {
            var world = current.World.GeographicWorld; var activation = Activation;
            observations.Add(new JObject { ["stage"] = stage, ["region"] = current.World.Route.Region.id,
                ["selectedManifest"] = activation.Selected.ManifestSha256, ["activeWindow"] = world ? world.BuildingWindowIdentity : null,
                ["regionalParts"] = activation.Controller ? activation.Controller.LastRegionalParts : 0,
                ["damage"] = current.Car.Damage, ["providerRequests"] = current.Conditions.WeatherRequests + (current.Updates ? current.Updates.SourceRequests : 0) });
            ScreenCapture.CaptureScreenshot(prefix + "-switch-" + stage + ".png"); frames = 0;
        }
        void Go(string id)
        {
            previous = current; retiredController = Activation.Controller; prepared = false; switches++;
            if (!current.SelectRegion(id)) throw new InvalidOperationException("Normal location handler refused " + id);
            deadline = Time.realtimeSinceStartupAsDouble + 180;
        }
        void Update()
        {
            if (finished || string.IsNullOrEmpty(prefix)) return;
            try
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Pack switch phase " + phase);
                if (phase == 0)
                {
                    Prepare(); if (menu.Busy) return;
                    if (menu.RevisionCount == 0) { menu.ImportArchive(archive); phase = 1; return; }
                    phase = 1;
                }
                if (phase == 1)
                {
                    if (menu.Busy) return;
                    if (menu.RevisionCount != 1) throw new InvalidOperationException("Expected one isolated pack revision");
                    menu.PreviewRevision(0); phase = 2; return;
                }
                if (phase == 2)
                {
                    if (Activation.PreferenceBusy || !Activation.Selected.Enabled) return;
                    selectedHash = Activation.Selected.ManifestSha256;
                    if (!WindowReady(true)) return;
                    Observe("pantside-before"); phase = 3; return;
                }
                if (phase == 3) { if (++frames < 6) return; Go("dee-valley"); phase = 4; return; }
                if (phase == 4)
                {
                    if (!Ready("dee-valley") || !WindowReady(false)) return;
                    if (retiredController) throw new InvalidOperationException("Old pack controller survived scene retirement");
                    Observe("dee-base"); phase = 5; return;
                }
                if (phase == 5) { if (++frames < 6) return; Go(home); phase = 6; return; }
                if (phase == 6)
                {
                    if (!Ready(home) || !WindowReady(true)) return;
                    if (retiredController) throw new InvalidOperationException("Dee controller survived scene retirement");
                    Observe("pantside-return"); phase = 7; return;
                }
                if (phase == 7)
                {
                    if (++frames < 6) return;
                    var result = new JObject { ["status"] = "PASS_NATIVE_PACK_LOCATION_REBIND", ["actualSceneSwitches"] = switches,
                        ["observations"] = observations, ["oldControllersRetired"] = true, ["savedSelectionPreserved"] = true,
                        ["outsideCoverageUsesBaseScenery"] = true, ["isolatedQAProfile"] = true, ["circuitSwitchVerified"] = false };
                    File.WriteAllText(prefix + "-switch-result.json", result.ToString()); finished = true; Application.Quit();
                }
            }
            catch (Exception error)
            {
                finished = true; File.WriteAllText(prefix + "-switch-result.json", new JObject { ["status"] = "FAIL", ["phase"] = phase,
                    ["error"] = error.ToString(), ["observations"] = observations }.ToString()); Debug.LogError("Pack switch verification: " + error); Application.Quit(1);
            }
        }
    }
}
