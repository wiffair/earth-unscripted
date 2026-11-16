using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit disposable-profile native proof for patrol upgrades and speed.
    /// Measures the actual spawned patrol body on an isolated level platform;
    /// neither the installed beta career nor the live game is opened or saved.
    /// </summary>
    public sealed class BetaPolicePerformanceVerification : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        readonly List<string> checks = new List<string>();
        readonly JObject results = new JObject();
        string failure;

        void Awake()
        {
            if (SinglePlayerBeta.VerificationOutput == null)
                throw new InvalidOperationException("Explicit disposable verification required");
            Application.logMessageReceived += CaptureError;
        }

        void CaptureError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                errors.Add(message + "\n" + stack);
        }

        void OnDestroy() { Application.logMessageReceived -= CaptureError; }

        IEnumerator Start()
        {
            // Flatten nested iterators so a native test exception always writes
            // its failure receipt rather than leaving a player running forever.
            var iterators = new Stack<IEnumerator>();
            iterators.Push(Exercise());
            while (iterators.Count > 0)
            {
                object value = null;
                bool more = false;
                try
                {
                    more = iterators.Peek().MoveNext();
                    if (more) value = iterators.Peek().Current;
                }
                catch (Exception error) { failure = error.ToString(); }
                if (failure != null) break;
                if (!more) { iterators.Pop(); continue; }
                if (value is IEnumerator nested) { iterators.Push(nested); continue; }
                yield return value;
            }
            bool passed = failure == null && errors.Count == 0;
            results["passed"] = passed;
            results["failure"] = failure;
            results["runtimeErrors"] = new JArray(errors);
            results["checks"] = new JArray(checks);
            results["version"] = Application.version;
            results["dataRoot"] = SinglePlayerBeta.DataRoot;
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "result.json"), results.ToString());
            Application.Quit(passed ? 0 : 1);
        }

        void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks.Add(description);
        }

        IEnumerator Exercise()
        {
            GameSession session = null;
            double began = Time.realtimeSinceStartupAsDouble;
            while (!(session = FindFirstObjectByType<GameSession>()) || !session.BootReady)
            {
                if (SinglePlayerBeta.StartupError != null) throw new IOException(SinglePlayerBeta.StartupError);
                if (session && session.BootFailed) throw new IOException(session.BootStatus);
                if (Time.realtimeSinceStartupAsDouble - began > 600) throw new TimeoutException("World startup");
                yield return null;
            }
            var patrols = FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None);
            Require(patrols.Length > 0, "Real world boot spawned physical patrols");
            foreach (var driver in patrols)
            {
                var maximum = MaximumBuild(driver.Car.Visual.Model);
                var car = driver.Car;
                Require(Mathf.Abs(car.PowerFactor - GarageRules.Power(maximum)) < .001f
                    && Mathf.Abs(car.SpeedLimit - GarageRules.Speed(maximum)) < .001f
                    && Mathf.Abs(car.TyreFactor - GarageRules.Grip(maximum)) < .001f
                    && Mathf.Abs(car.BrakeFactor - GarageRules.Braking(maximum)) < .001f
                    && Mathf.Abs(car.SpringFactor - 1.24f) < .001f
                    && Mathf.Abs(car.DamperFactor - 1.3f) < .001f
                    && Mathf.Abs(car.DifferentialFactor - .7f) < .001f
                    && Mathf.Abs(car.Body.mass - GarageRules.Catalogue[maximum.model].Mass) < .01f,
                    "Spawned patrol has all six maximum upgrades and its own model mass");
            }
            var patrol = patrols[0];
            var testedCar = patrol.Car;
            int model = testedCar.Visual.Model;
            results["testedModel"] = GarageRules.Catalogue[model].Name;
            results["patrolCount"] = patrols.Length;
            results["gearingLimitMph"] = testedCar.SpeedLimit * DrivingMath.MphPerMetreSecond;
            Require(PoliceDriver.DesiredPursuitSpeed(45, 120, false, testedCar.SpeedLimit) > 45,
                "Catch-up target exceeds the former 45 m/s AI cap");
            Require(PoliceDriver.DesiredPursuitSpeed(0, 7, false, testedCar.SpeedLimit) == 0,
                "Stationary player retains the braking stand-off");
            Require(PoliceDriver.DesiredPursuitSpeed(60, 1000, false, 30) == 30,
                "Catch-up target respects the selected patrol model limit");

            session.SetMenu(false);
            session.enabled = false;
            session.Input.enabled = false;
            session.View.enabled = false;
            var ui = FindFirstObjectByType<DrivingUI>();
            if (ui) ui.enabled = false;
            foreach (var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
                if (traffic != patrol.Marker) traffic.gameObject.SetActive(false);
            session.Car.gameObject.SetActive(false);
            patrol.enabled = false;
            patrol.Marker.enabled = false;
            testedCar.enabled = true;
            testedCar.StartLocked = false;
            testedCar.StreamingBrake = false;
            testedCar.ParkingAutomaticEnabled = false;
            testedCar.WaterDepth = 0;
            testedCar.SurfaceWetness = 0;

            // Above all map surfaces, with sufficient stopping distance even
            // after a 90-second run. Fixed physics step remains unchanged.
            Geometry.Box("Owned patrol speed platform", null, new Vector3(0, 9999.5f, 6000),
                new Vector3(100, 1, 14000), Geometry.Material("Patrol QA ground", Color.gray), true);
            Time.timeScale = 4;
            yield return Measure(testedCar, new OwnedCar { model = model }, true, "previousPatrol");
            yield return Measure(testedCar, MaximumBuild(model), false, "fullyUpgradedPatrol");
            var before = (JObject)results["previousPatrol"];
            var after = (JObject)results["fullyUpgradedPatrol"];
            Require((float)after["peakMph"] > (float)before["peakMph"] + 10,
                "Upgraded patrol is materially faster than the previous capped 65% throttle configuration");
            Require((float)after["zeroTo70Seconds"] > 0 && (float)after["zeroTo70Seconds"] < 15,
                "Actual upgraded patrol accelerates to 70 mph promptly");
            Require((float)after["damage"] < .1f, "Flat-road speed measurement remains undamaged");
        }

        static OwnedCar MaximumBuild(int model)
        {
            var owned = new OwnedCar { model = model };
            for (int part = 0; part < GarageRules.Parts.Length; part++) owned.SetLevel(part, 3);
            return owned;
        }

        IEnumerator Measure(VehicleController car, OwnedCar build, bool previous, string label)
        {
            car.ApplyTuning(build);
            // Before this fix Configure never applied tuning: the controller's
            // defaults included 1350 kg and 65 m/s regardless of the visual model.
            if (previous)
            {
                car.PowerFactor = car.TyreFactor = car.BrakeFactor = car.SpringFactor = car.DamperFactor = 1;
                car.SpeedLimit = 65;
                car.Body.mass = 1350;
            }
            car.Grip = previous ? 1 : car.TyreFactor;
            car.Body.isKinematic = false;
            car.Body.position = new Vector3(0, 10000 + car.Metrics.RestRootHeight(car.Body.mass, car.SpringFactor), 0);
            car.Body.rotation = Quaternion.identity;
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            car.transform.SetPositionAndRotation(car.Body.position, Quaternion.identity);
            car.NetworkThrottle = car.NetworkBrake = car.NetworkHandbrake = car.NetworkSteer = 0;
            Physics.SyncTransforms();
            for (int step = 0; step < 150; step++) yield return new WaitForFixedUpdate();
            var samples = new JArray();
            float peakMph = 0, zeroTo60 = -1, zeroTo70 = -1;
            double began = Time.timeAsDouble;
            int count = Mathf.CeilToInt(90 / Time.fixedDeltaTime);
            for (int step = 0; step < count; step++)
            {
                float seconds = (float)(Time.timeAsDouble - began);
                float mph = car.AbsoluteSpeed * DrivingMath.MphPerMetreSecond;
                peakMph = Mathf.Max(peakMph, mph);
                if (zeroTo60 < 0 && mph >= 60) zeroTo60 = seconds;
                if (zeroTo70 < 0 && mph >= 70) zeroTo70 = seconds;
                car.NetworkThrottle = previous ? (car.Speed < 45 ? .65f : 0) : 1;
                car.NetworkBrake = car.NetworkHandbrake = car.NetworkSteer = 0;
                if (step % 500 == 0)
                {
                    samples.Add(new JObject { ["seconds"] = seconds, ["mph"] = mph, ["height"] = car.Body.position.y });
                    File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "progress.json"),
                        new JObject { ["stage"] = label, ["seconds"] = seconds, ["peakMph"] = peakMph }.ToString());
                }
                yield return new WaitForFixedUpdate();
            }
            car.NetworkThrottle = 0;
            results[label] = new JObject
            {
                ["peakMph"] = peakMph, ["finalMph"] = car.AbsoluteSpeed * DrivingMath.MphPerMetreSecond,
                ["zeroTo60Seconds"] = zeroTo60, ["zeroTo70Seconds"] = zeroTo70,
                ["damage"] = car.Damage, ["samples"] = samples
            };
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "measurements.json"), results.ToString());
        }
    }
}
