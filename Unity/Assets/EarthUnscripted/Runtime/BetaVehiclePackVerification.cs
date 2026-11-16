using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Explicit disposable-profile integration proof. Exercises the real
    /// importer, save/load ID binding, actual free-drive selection, four-wheel
    /// suspension/drive, deformation and a physical collision. Never runs normally.</summary>
    public sealed class BetaVehiclePackVerification : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        readonly List<string> checks = new List<string>();
        GameSession session;
        float distance, peakSpeed;
        string failure;
        readonly JArray motionSamples = new JArray();
        void Awake() { Application.logMessageReceived += CaptureError; }
        void CaptureError(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) errors.Add(message + "\n" + stack); }
        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(Exercise());
            while (stack.Count > 0)
            {
                bool more = false; object value = null;
                try { more = stack.Peek().MoveNext(); if (more) value = stack.Peek().Current; }
                catch (Exception error) { failure = error.ToString(); }
                if (failure != null) { Finish(false); yield break; }
                if (!more) { stack.Pop(); continue; }
                if (value is IEnumerator nested) { stack.Push(nested); continue; }
                yield return value;
            }
            Finish(errors.Count == 0);
        }
        IEnumerator Exercise()
        {
            double start = Time.realtimeSinceStartupAsDouble;
            while (!(session = FindFirstObjectByType<GameSession>()) || !session.BootReady)
            {
                if (session && session.BootFailed) throw new IOException(session.BootStatus);
                if (SinglePlayerBeta.StartupError != null) throw new IOException(SinglePlayerBeta.StartupError);
                if(Time.realtimeSinceStartupAsDouble - start >= 600)throw new TimeoutException("World startup timed out");
                yield return null;
            }
            var entry = ImportedVehicleCatalogue.Find("earthunscripted.hekla-formula-offroad");
            Require(entry != null && entry.Settings.Version == "2.0.0", "Versioned example discovered before profile load");
            Require(GarageRules.Catalogue[0].Name == "Brook Coupe" && GarageRules.Catalogue[3].Name == "Apex Single Seat", "Built-in catalogue indexes retained");
            string source = Path.Combine(VehiclePackLibrary.IncludedFolder, "Hekla-Formula-Offroad-2.0.0.eucar");
            // Use the same asynchronous path and main-thread activation as the menu.
            using (var menu = new VehiclePackMenu())
            {
                menu.ImportArchive(source);
                for (int frame = 0; frame < 120; frame++) { menu.Poll(); yield return null; }
            }
            Require(VehiclePackLibrary.ForModel(entry.Model) != null, "Pack-menu import retains the active definition");
            string fixture = Path.Combine(SinglePlayerBeta.VerificationOutput, "fixtures"); Directory.CreateDirectory(fixture);
            CheckRefused(source, fixture, "bad-hash", m => m["meshSha256"] = new string('0', 64));
            CheckRefused(source, fixture, "bad-version", m => m["version"] = "tomorrow");
            CheckRefused(source, fixture, "bad-mass", m => m["physics"]["massKg"] = -1);
            string old = Rewrite(source, fixture, "old", m => { m["revision"] = 1; m["version"] = "0.9.0"; });
            bool refused = false;
            try { VehiclePackLibrary.Import(old, VehiclePackLibrary.Folder); } catch (InvalidDataException) { refused = true; }
            Require(refused, "Same-revision different-content pack refused");
            string successor = Rewrite(source, fixture, "successor", m => { m["revision"] = 5; m["version"] = "2.1.0"; });
            var updated = VehiclePackLibrary.Import(successor, VehiclePackLibrary.Folder);
            Require(updated.Settings.Version == "2.1.0" && entry.Settings.Version == "2.0.0" && VehiclePackLibrary.ForModel(entry.Model).Settings.Version == "2.0.0", "Update installed atomically; in-use version retained until restart");
            refused = false; try { VehiclePackLibrary.Import(source, VehiclePackLibrary.Folder); } catch (InvalidDataException) { refused = true; }
            Require(refused, "Downgrade refused");
            string second = Rewrite(source, fixture, "another-car", m => { m["id"] = "example.second-car"; m["name"] = "Second imported car"; m["priceCredits"] = 5000; });
            var another = VehiclePackLibrary.Import(second, VehiclePackLibrary.Folder); VehiclePackLibrary.Activate(another);
            Require(ImportedVehicleCatalogue.Find("example.second-car").Model != entry.Model, "A second independent pack loads without code changes");
            session.Car.Body.linearVelocity = Vector3.zero; session.Car.Body.angularVelocity = Vector3.zero;
            session.SetMenu(true);
            int previousWallet = session.Economy.Credits;
            session.Economy.Spend(previousWallet);
            int beforePurchaseCount = session.Profile.garage.cars.Count;
            Require(!session.SelectPackVehicle(ImportedVehicleCatalogue.Find(another.Settings.Id).Model) && session.Profile.garage.cars.Count == beforePurchaseCount, "Paid imported car refuses insufficient credits without granting ownership");
            session.Economy.AddCredits(6000);
            Require(session.SelectPackVehicle(ImportedVehicleCatalogue.Find(another.Settings.Id).Model) && session.Economy.Credits == 1000, "Paid pack purchase deducts its declared 5000-credit price");
            Require(session.SelectPackVehicle(0), "Return to original built-in after paid-pack fixture");
            session.Economy.AddCredits(previousWallet);
            var legacy = session.Profile.garage.Clone();
            int credits = session.Economy.Credits;
            session.Car.Body.linearVelocity = Vector3.zero; session.Car.Body.angularVelocity = Vector3.zero;
            session.SetMenu(true);
            Require(session.SelectPackVehicle(entry.Model), "Real paused free-drive pack selection");
            Require(session.Profile.garage.cars.Count == legacy.cars.Count + 1 && session.Economy.Credits == credits, "Original car and credits retained");
            var owned = session.Profile.garage.Active;
            owned.engine = 1; owned.damage = 17; owned.dents.Add(new SavedDent { x = .5f, y = .8f, z = .6f, nx = -1, severity = 2 });
            Require(session.Profile.Save(), "Imported car saved");
            var loaded = PlayerProfile.Load();
            Require(!loaded.LoadBlocked && loaded.garage.Active.packVehicleId == entry.Settings.Id && loaded.garage.Active.model == entry.Model && loaded.garage.Active.engine == 1 && loaded.garage.Active.damage == 17 && loaded.garage.Active.dents.Count == 1, "Save/reload restores stable ID, upgrades and condition");
            var reordered = loaded.garage.Clone(); reordered.Active.model = ImportedVehicleCatalogue.Find("example.second-car").Model; GarageRules.Validate(reordered);
            Require(reordered.Active.model == entry.Model, "Saved stable ID overrides a changed numeric model index");
            var absent = loaded.garage.Clone(); var missing = absent.Active; missing.packVehicleId = "missing.publisher-car"; GarageRules.Validate(absent);
            Require(absent.cars.Contains(missing) && missing.engine == 1 && missing.damage == 17 && absent.Active != missing && !ImportedVehicleCatalogue.Available(missing), "Missing pack keeps the owned car and selects an available fallback");
            // Actual gameplay car is checked before moving a separate QA car onto
            // an isolated platform, so the visible route and world remain intact.
            Require(session.Car.Metrics.Radius == entry.Settings.WheelRadius && session.Car.GetComponent<BoxCollider>().size.x == entry.Settings.ColliderWidth, "Imported wheel and chassis dimensions reach gameplay physics");
            Require(session.SelectPackVehicle(legacy.Active.model), "Return to the original owned car without a garage teleport");
            Require(session.Car.GetComponent<BoxCollider>().size.y == VehicleMetric.ColliderHeight, "Built-in chassis dimensions restored after imported car");
            var ground = Geometry.Box("Car-pack QA platform", null, new Vector3(0, 9999.5f, 0), new Vector3(240, 1, 300), Geometry.Material("Car-pack QA ground", new Color(.12f,.16f,.12f)), true);
            var root = new GameObject("Physical imported test car"); root.layer = 8;
            root.transform.position = new Vector3(0, 10000 + entry.Metric.RestRootHeight(entry.Settings.Mass), 0);
            root.AddComponent<Rigidbody>(); var box = root.AddComponent<BoxCollider>(); box.center = new Vector3(0,.86f,0); box.size = new Vector3(1.8f,.85f,4);
            var visual = root.AddComponent<CarVisual>(); visual.Build(new Color(1,.22f,.035f), true, entry.Model);
            var car = root.AddComponent<VehicleController>(); car.Visual = visual; car.NetworkDriven = true; car.ApplyTuning(new OwnedCar { model = entry.Model, packVehicleId = entry.Settings.Id }); car.NetworkBrake = 0;
            car.ParkingAutomaticEnabled = true;
            var camera = session.View.GetComponent<Camera>(); session.View.enabled = false;
            var sun = new GameObject("Car-pack QA daylight").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 2; sun.transform.rotation = Quaternion.Euler(45,-30,0);
            session.SetMenu(false);session.enabled=false;
            Time.timeScale = 1;
            for (int frame = 0; frame < 100; frame++) yield return new WaitForFixedUpdate();
            Require(car.WheelGrounded.All(v => v) && Math.Abs(car.Body.position.y - 10000 - entry.Metric.RestRootHeight(entry.Settings.Mass)) < .15f, "All four suspension contacts support the car at calibrated rest height");
            camera.transform.position = root.transform.position + new Vector3(5,2.8f,5); camera.transform.LookAt(root.transform.position + Vector3.up * .8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput, "hekla-front.png")); yield return new WaitForEndOfFrame(); yield return null;
            camera.transform.position = root.transform.position + new Vector3(5,2.6f,-5); camera.transform.LookAt(root.transform.position + Vector3.up * .8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput, "hekla-rear.png")); yield return new WaitForEndOfFrame(); yield return null;
            visual.Dent(root.transform.TransformPoint(new Vector3(.65f,.85f,-.75f)), 18, root.transform.right * -1);
            Require(visual.RecordedDents.Count == 1 && visual.DeformationExtent > 0, "Imported painted panels deform on impact"); visual.Repair(); Require(visual.DeformationExtent == 0, "Imported panel repair restores mesh");
            car.NetworkBrake = 0; car.NetworkThrottle = 1; car.Grip = entry.Settings.Grip * entry.Settings.OffroadGrip;
            var began = car.Body.position;
            for (int frame = 0; frame < 200; frame++)
            {
                peakSpeed = Math.Max(peakSpeed, car.AbsoluteSpeed);
                if(frame%10==0)motionSamples.Add(new JObject{["frame"]=frame,["x"]=car.Body.position.x,["y"]=car.Body.position.y,["z"]=car.Body.position.z,["speed"]=car.AbsoluteSpeed,["up"]=Vector3.Dot(root.transform.up,Vector3.up),["contacts"]=car.WheelGrounded.Count(v=>v)});
                yield return new WaitForFixedUpdate();
            }
            distance = Vector3.Distance(began, car.Body.position);
            Require(distance > 10 && peakSpeed > 5 && car.WheelGrounded.Count(v => v) >= 2 && Vector3.Dot(root.transform.up,Vector3.up)>.9f, "Imported 4WD accelerates with stable physical ground contact");
            // A stationary collision test avoids car orientation/steering ambiguity.
            car.NetworkThrottle = 0; car.NetworkBrake = 1;
            var obstacle = Geometry.Box("Car-pack QA obstacle", null, car.Body.position + root.transform.forward * 4, new Vector3(4,4,1), Geometry.Material("Car-pack QA wall", Color.gray), true);
            car.Body.linearVelocity = root.transform.forward * 8;
            for (int frame = 0; frame < 70; frame++) yield return new WaitForFixedUpdate();
            Require(car.Damage > 0, "Imported chassis collides and receives damage");
            UnityEngine.Object.Destroy(ground); UnityEngine.Object.Destroy(obstacle);
            yield return null;
            yield return Beta15FeatureChecks.Exercise(session,car,camera,checks);
            UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(ground); UnityEngine.Object.Destroy(obstacle);
            Require(errors.Count == 0, "No runtime errors");
        }
        void CheckRefused(string source, string folder, string name, Action<JObject> change)
        {
            bool refused = false; try { VehiclePackFormat.Read(Rewrite(source, folder, name, change)); } catch (InvalidDataException) { refused = true; }
            Require(refused, "Rejected malformed pack: " + name);
        }
        static string Rewrite(string source, string folder, string name, Action<JObject> change)
        {
            string path = Path.Combine(folder, name + ".eucar");
            using (var input = ZipFile.OpenRead(source))
            using (var output = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var entry in input.Entries)
                {
                    var created = output.CreateEntry(entry.FullName);
                    using (var reader = entry.Open()) using (var writer = created.Open())
                    {
                        if (entry.FullName != "manifest.json") reader.CopyTo(writer);
                        else { using (var text = new StreamReader(reader)) { var json = JObject.Parse(text.ReadToEnd()); change(json); using (var body = new StreamWriter(writer)) body.Write(json.ToString()); } }
                    }
                }
            return path;
        }
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        void Finish(bool passed)
        {
            var result = new JObject { ["passed"] = passed, ["failure"] = failure ?? "", ["checks"] = new JArray(checks), ["errors"] = new JArray(errors), ["distanceMetres"] = distance, ["peakMetresPerSecond"] = peakSpeed, ["motionSamples"] = motionSamples };
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "result.json"), result.ToString());
            Application.Quit(passed ? 0 : 1);
        }
        void OnDestroy() { Application.logMessageReceived -= CaptureError; }
    }
}
