using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using EarthUnscripted.NewbridgePrototype;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Disposable coastal-world proof: actual bed colliders, mapped
    /// substrate, shallow splashes and delayed deep-water recovery.</summary>
    public static class Beta15WaterChecks
    {
        public static IEnumerator Exercise(GameSession session)
        {
            var world = session.World.AutomaticWorld.Renderer;
            double began = Time.realtimeSinceStartupAsDouble;
            while (!world.CoastalBedsReady)
            {
                if (Time.realtimeSinceStartupAsDouble - began > 180) throw new TimeoutException("Seabed collider publication");
                yield return null;
            }
            var report = new JObject();
            var terrain = world.GetComponentsInChildren<MeshCollider>().Where(c => c.name == "Terrain").ToArray();
            var origin = session.Car.Body.position;
            var patch=(NewbridgeWorld.CoastalPatch)typeof(NewbridgeWorld).GetField("coastalPatch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
            Vector3 sample = default, worst = default;
            int count = 0, edgeSamples = 0;
            float edgeProtrusion = float.NegativeInfinity, worstShoreDistance = 0;
            float greatestDepth = 0, maximumProtrusion = float.NegativeInfinity;
            for (int x = -360; x <= 360; x += 24)
                for (int z = -360; z <= 360; z += 24)
                {
                    var point = origin + new Vector3(x, 0, z);
                    if (!world.HasGeometryAt(point) || !world.TryWaterSurface(point, out float level, out bool sea) || !sea) continue;
                    var road = session.World.Route.Region.Nearest(point, out _, out float distance);
                    if (road != null && distance < road.width * .5f + 8) continue; // Causeways keep their road support.
                    var ray = new Ray(new Vector3(point.x, level + 100, point.z), Vector3.down);
                    float height = float.NegativeInfinity;
                    foreach (var collider in terrain) if (collider.Raycast(ray, out var hit, 250)) height = Mathf.Max(height, hit.point.y);
                    if (float.IsInfinity(height)) continue;
                    float shoreDistance=DistanceToShore(patch,point);
                    // A coastline can pass through a 2 m terrain triangle. Its
                    // land corner may legitimately rise above mean water level;
                    // open-water acceptance starts beyond one cell diagonal.
                    if(shoreDistance<3)
                    {edgeSamples++;edgeProtrusion=Mathf.Max(edgeProtrusion,height-level);continue;}
                    if(height-level>maximumProtrusion){maximumProtrusion=height-level;worst=point;worstShoreDistance=shoreDistance;}
                    float depth = level - height;
                    if (depth > greatestDepth) { greatestDepth = depth; sample = new Vector3(point.x, level, point.z); }
                    count++;
                }
            report["sampledSeaBedPoints"] = count;
            report["shorelineTriangleSamples"] = edgeSamples;
            report["maximumShoreEdgeRiseMetres"] = edgeProtrusion;
            report["worstOpenWaterPoint"]=new JArray(worst.x,worst.y,worst.z);
            report["worstOpenWaterShoreDistanceMetres"]=worstShoreDistance;
            report["maximumBedAboveWaterMetres"] = maximumProtrusion;
            report["deepestSampleMetres"] = greatestDepth;
            Save(report);
            if (count < 6 || greatestDepth < .8f || maximumProtrusion > .05f)
                throw new InvalidOperationException("Mapped open sea must have supported terrain below the water, not grass above it");
            bool substrate = world.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Terrain")
                .Any(r => r.sharedMaterial.HasProperty("_WaterMaskEnabled") && r.sharedMaterial.GetFloat("_WaterMaskEnabled") > .5f);
            if (!substrate) throw new InvalidOperationException("Mapped water substrate mask is not bound to actual terrain");
            report["mappedSubstrateBound"] = true;
            var car = session.Car;
            var originalPosition = car.Body.position;
            var originalRotation = car.Body.rotation;
            bool originalGravity = car.Body.useGravity;
            session.SetMenu(false); car.StartLocked = true;
            for (int frame = 0; frame < 3; frame++) yield return null; // Establish a real safe-road recovery anchor.
            car.enabled = false; car.Body.useGravity = false;
            var effects = car.GetComponent<TyreEffects>(); int beforeParticles = effects.EmittedParticles;
            car.Body.position = sample + Vector3.up * (car.Metrics.RestRootHeight(car.Body.mass,car.SpringFactor) - .18f);
            car.Body.linearVelocity = Vector3.forward * 5;
            Physics.SyncTransforms();
            double start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - start < 1.2) yield return null;
            report["shallowWaterDepth"] = car.WaterDepth;
            report["splashParticles"] = effects.EmittedParticles - beforeParticles;
            report["shallowRecoveryStarted"] = session.PreparingStart;
            Save(report);
            if (car.WaterDepth < .1f || car.WaterDepth > .3f || session.PreparingStart || effects.EmittedParticles <= beforeParticles)
                throw new InvalidOperationException("Shallow sea must splash without immediate recovery");
            car.Body.position = sample + Vector3.up * (car.Metrics.RestRootHeight(car.Body.mass,car.SpringFactor) - 1.5f);
            car.Body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
            double deepBegan=Time.timeAsDouble;
            yield return new WaitForSecondsRealtime(1);
            bool Recovered()=>session.PreparingStart||Vector3.Distance(car.Body.position,sample)>20;
            if (Recovered()) throw new InvalidOperationException("Deep-water recovery started before its grace period");
            start = Time.realtimeSinceStartupAsDouble;
            while (!Recovered() && Time.realtimeSinceStartupAsDouble - start < 6) yield return null;
            report["deepRecoveryAfterGrace"] = Recovered();
            report["deepRecoverySimulationSeconds"] = Time.timeAsDouble-deepBegan; Save(report);
            if (!Recovered()) throw new InvalidOperationException("Deep-water recovery did not start after the flooding grace period");
            while (session.PreparingStart)
            {
                if (Time.realtimeSinceStartupAsDouble - start > 120) throw new TimeoutException("Deep-water safe-road recovery");
                yield return null;
            }
            car.Body.position = originalPosition; car.Body.rotation = originalRotation;
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            car.Body.useGravity = originalGravity; car.enabled = true; car.WaterDepth = 0;
            car.StartLocked = true; session.SetMenu(true);
            report["passed"] = true; Save(report);
        }
        static float DistanceToShore(NewbridgeWorld.CoastalPatch patch,Vector3 point)
        {
            float nearest=float.PositiveInfinity;var sample=new Vector2(point.x,point.z);
            foreach(var ring in patch.shore??Array.Empty<NewbridgeWorld.CoastRing>())
                for(int index=1;index<(ring.points?.Length??0);index++)
                {
                    var a=new Vector2(ring.points[index-1].x,ring.points[index-1].z);var b=new Vector2(ring.points[index].x,ring.points[index].z);
                    var direction=b-a;float station=Mathf.Clamp01(Vector2.Dot(sample-a,direction)/Mathf.Max(.0001f,direction.sqrMagnitude));
                    nearest=Mathf.Min(nearest,Vector2.Distance(sample,a+direction*station));
                }
            return nearest;
        }
        static void Save(JObject report) => File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"beta15-water.json"),report.ToString());
    }
}
