using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Disposable native acceptance at the reported coastal street.
    /// Validates real road colliders against mapped sea level after corrected
    /// source preparation; captures the scene without changing a real career.</summary>
    public sealed class BetaShorelineAlignmentVerification : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        readonly List<string> checks = new List<string>();
        readonly JArray samples = new JArray();
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) errors.Add(text + "\n" + stack); }
        void Check(bool passed, string message)
        { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
        IEnumerator Start()
        {
            var work = Exercise();
            string failure = null;
            while (true)
            {
                object current = null; bool more = false;
                try { more = work.MoveNext(); if (more) current = work.Current; }
                catch (Exception error) { failure = error.ToString(); }
                if (failure != null || !more) break;
                yield return current;
            }
            var startup = JObject.Parse(File.ReadAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "startup.json")));
            startup["alignmentChecks"] = new JArray(checks);
            startup["roadSamples"] = samples;
            startup["alignmentFailure"] = failure;
            startup["alignmentErrors"] = new JArray(errors);
            startup["passed"] = (bool)startup["passed"] && failure == null && errors.Count == 0;
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "result.json"), startup.ToString());
            Application.Quit((bool)startup["passed"] ? 0 : 1);
        }
        IEnumerator Exercise()
        {
            var session = FindFirstObjectByType<GameSession>();
            var world = session.World.AutomaticWorld.Renderer;
            Check(session.BootReady && !session.BootFailed, "Corrected cached world reaches normal readiness");
            double began = Time.realtimeSinceStartupAsDouble;
            while (!world.CoastalSurfaceReady || !world.CoastalBedsReady)
            {
                if (Time.realtimeSinceStartupAsDouble - began > 240) throw new TimeoutException("Coastal surface and bed readiness");
                yield return null;
            }
            session.SetMenu(true);
            session.Car.Body.linearVelocity = session.Car.Body.angularVelocity = Vector3.zero;
            var camper = Core.ImportedVehicleCatalogue.Find("earthunscripted.vw-splittie");
            Check(session.SelectPackVehicle(camper.Model), "Camper fits supported corrected road without moving to another area");
            session.Input.enabled = false;
            var position = session.Car.Body.position;
            float sea = world.MappedSeaLevel;
            Check(!world.IsMappedSeaAt(position), "Reported street remains land in the unchanged coastline mask");
            var roads = world.ResidentAreas.SelectMany(area => area.features)
                .Where(feature => feature.category == "road" && !feature.tunnel && feature.name == "Cove Row");
            foreach (var road in roads)
            foreach (var point in road.points)
            {
                if (Vector2.Distance(new Vector2(point.x, point.z), new Vector2(position.x, position.z)) > 65) continue;
                if (samples.Count > 100) break;
                var hits = Physics.RaycastAll(new Ray(point + Vector3.up * 4, Vector3.down), 8, ~(1 << 8), QueryTriggerInteraction.Ignore);
                var surfaces = hits.Where(hit => hit.collider.name == "Road asphalt" && hit.collider.transform.IsChildOf(world.transform)).ToArray();
                Check(surfaces.Length > 0, "Nearby Cove Row station owns a published road collider");
                float surface = surfaces.Max(hit => hit.point.y);
                Check(surface > sea + .5f, "Nearby Cove Row road surface stays above mapped sea");
                Check(Mathf.Abs(surface - point.y) < .25f, "Rendered road collision follows corrected prepared profile");
                samples.Add(new JObject { ["x"] = point.x, ["z"] = point.z, ["profileY"] = point.y, ["colliderY"] = surface, ["seaY"] = sea });
            }
            Check(samples.Count >= 10, "Multiple coastal road stations verified rather than one raised spawn point");
            Check(position.y > sea + .5f, "Parked vehicle is supported above the water surface");
            session.SetMenu(false); session.enabled = false;
            session.Car.Body.linearVelocity = session.Car.Body.angularVelocity = Vector3.zero;
            session.Car.enabled = false; session.Car.Body.useGravity = false;
            Time.timeScale = 0;
            session.View.Preview = null; session.View.Mode = 0; session.View.enabled = true;
            for (int frame = 0; frame < 90; frame++) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput, "cove-row-corrected.png"));
            yield return new WaitForEndOfFrame(); yield return null;
            session.View.enabled = false;
            var camera = session.View.GetComponent<Camera>();
            camera.transform.position = position + new Vector3(-14, 8, -10);
            camera.transform.LookAt(position + Vector3.up);
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput, "cove-row-water-level.png"));
            yield return new WaitForEndOfFrame(); yield return null;
        }
    }
}
