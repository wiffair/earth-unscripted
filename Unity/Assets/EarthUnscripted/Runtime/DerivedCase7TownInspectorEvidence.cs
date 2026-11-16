using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class DerivedCase7TownInspectorProof
    {
        // Existing production foundation embeds at least0.20m; account only for
        // the same0.20mm native/query agreement used by ground evidence above.
        const double MinimumFoundationEmbedMetres = .20 - RayToleranceMetres;
        const float WallRayOffsetMetres = .4f, WallRayLengthMetres = .8f, WallRayToleranceMetres = .001f;
        const float GroundRayStartMetres = 2, GroundRayLengthMetres = 4;
        static string WallPartId(string name)
        {
            const string Prefix = "Building ";
            const string Mapped = " (mapped height)", Estimated = " (estimated height)";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
                return null;
            string suffix = name.EndsWith(Mapped, StringComparison.Ordinal) ? Mapped : name.EndsWith(Estimated, StringComparison.Ordinal) ? Estimated : null;
            if (suffix == null)
                throw new InvalidDataException("Unknown actual wall collider naming contract");
            return name.Substring(Prefix.Length, name.Length - Prefix.Length - suffix.Length);
        }

        bool NativeGround(float x, float z, double height)
        {
            // Query ONLY the exact physical owner's current1064 colliders; walls,
            // stairs and other overlays cannot masquerade as original ground.
            var hits = Physics.RaycastAll(new Vector3(x, (float)height + GroundRayStartMetres, z), Vector3.down, GroundRayLengthMetres, ~0, QueryTriggerInteraction.Ignore);
            return hits.Any(hit => physicalColliders.Contains(hit.collider) && Math.Abs(hit.point.y - height) <= RayToleranceMetres);
        }

        void ExportTownEvidence()
        {
            var ledger = JObject.Parse(town.ExportScopedTownLedger800());
            var rows = (JArray)ledger["buildings"];
            Need(town.Region.buildings.Length == ExpectedSources && rows.Count > 0 && rows.All(row => (string)row["status"] == "Emitted" || (string)row["status"] == "Suppressed"), "Every actual render entry has one terminal disposition");
            var originalIds = rows.SelectMany(row => ((JArray)row["originalIds"]).Values<string>()).ToArray();
            Need(originalIds.Length == ExpectedSources && originalIds.Distinct(StringComparer.Ordinal).Count() == ExpectedSources && originalIds.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(town.Region.buildings.Select(b => b.id).OrderBy(x => x, StringComparer.Ordinal)), "All1793 original owned sources accounted exactly once, no dropped/coalesced sentinel");
            Need((string)ledger["rawBefore"] == (string)ledger["rawAfter"] && (string)ledger["rawDocumentDigest"] == plan.RawDocumentDigest && !(bool)ledger["registryIssued"] && !(bool)ledger["closedBuildingVolumes"], "Source unchanged, no facade-registry or closed-volume claim");
            File.WriteAllText(Path.Combine(output, "town-ledger.json"), ledger.ToString(Formatting.Indented));
            File.WriteAllText(Path.Combine(output, "entry-audit.json"), town.EntryAudit.ExportJson());
            var wallRows = new JArray();
            var wallsByPart = new Dictionary<string, int>(StringComparer.Ordinal);
            int wallFaceRays = 0, wallBaseSamples = 0;
            var walls = town.GetComponentsInChildren<MeshCollider>(true).Where(c => WallPartId(c.name) != null).ToArray();
            Need(walls.Length > 0 && walls.Length == rows.Sum(row => (int)row["wallColliders"]), "Ledger equals actual emitted WALLS_ONLY collision census");
            foreach (var collider in walls)
            {
                string part = WallPartId(collider.name);
                Need(town.BuildingSites.ContainsKey(part), "Every actual wall belongs to a real prepared site part");
                wallsByPart[part] = wallsByPart.TryGetValue(part, out int count) ? count + 1 : 1;
                var mesh = collider.sharedMesh;
                var filter = collider.GetComponent<MeshFilter>();
                var renderer = collider.GetComponent<MeshRenderer>();
                Need(mesh && filter && filter.sharedMesh == mesh && renderer && renderer.enabled && collider.enabled && !collider.isTrigger && !collider.convex && collider.gameObject.activeInHierarchy, "Actual visible wall mesh and collider share final native buffer");
                var local = mesh.vertices;
                var indices = mesh.triangles;
                var world = local.Select(v => collider.transform.TransformPoint(v)).ToArray();
                var xyz = new JArray();
                foreach (var vertex in world)
                {
                    xyz.Add(vertex.x);
                    xyz.Add(vertex.y);
                    xyz.Add(vertex.z);
                }

                wallRows.Add(new JObject { { "partId", part }, { "collisionPolicy", "WALLS_ONLY" }, { "colliderNativeId", collider.GetEntityId().ToString() }, { "worldGeneration", town.SurfaceGeneration }, { "groundGeneration", ground.Lease.Generation }, { "worldXYZ", xyz }, { "indices", new JArray(indices) } });
                for (int i = 0; i < world.Length; i += 4)
                {
                    Need(i + 3 < world.Length, "Actual production wall quad structure");
                    foreach (int at in new[]
                    {
                        i,
                        i + 1
                    }

                    )
                    {
                        var p = world[at];
                        Need(plan.TryGround(p.x, p.z, out var support), "Every wall base has final plan support");
                        Need(p.y <= support.Height - MinimumFoundationEmbedMetres && NativeGround(p.x, p.z, support.Height), "Actual base below physical final support, no floating foundation");
                        wallBaseSamples++;
                    }

                    var mid = (world[i] + world[i + 1] + world[i + 2] + world[i + 3]) / 4;
                    var normal = Vector3.Cross(world[i + 1] - world[i], world[i + 2] - world[i]).normalized;
                    bool hit = collider.Raycast(new Ray(mid + normal * WallRayOffsetMetres, -normal), out var actual, WallRayLengthMetres) || collider.Raycast(new Ray(mid - normal * WallRayOffsetMetres, normal), out actual, WallRayLengthMetres);
                    Need(hit && Vector3.Distance(actual.point, mid) < WallRayToleranceMetres, "Actual emitted wall face ray, not closed house volume");
                    wallFaceRays++;
                }
            }

            File.WriteAllText(Path.Combine(output, "actual-wall-meshes.json"), wallRows.ToString(Formatting.None));
            Need(new FileInfo(Path.Combine(output, "actual-wall-meshes.json")).Length <= MaximumDiagnosticBytes, "Bounded wall readback export");
            var sites = new JArray();
            foreach (var pair in town.BuildingSites)
            {
                var site = pair.Value;
                foreach (var sample in site.Samples)
                    Need(plan.TryGround(sample.x, sample.z, out var support) && Math.Abs(sample.y - support.Height) <= RayToleranceMetres, "Stored real site sample uses exact derived final ground");
                sites.Add(new JObject { { "partId", pair.Key }, { "floorY", site.LevelFloorY }, { "foundationBottomY", site.FoundationBottomY }, { "boundaryMinY", site.BoundaryMinY }, { "boundaryMaxY", site.BoundaryMaxY }, { "samples", new JArray(site.Samples.Select(s => new JArray(s.x, s.y, s.z))) } });
            }

            File.WriteAllText(Path.Combine(output, "actual-sites.json"), sites.ToString(Formatting.None));
            var features = new JArray();
            foreach (var chunk in town.GetComponentsInChildren<BuildingFacadeChunk>(true))
                foreach (var feature in chunk.Features)
                    features.Add(new JObject { { "id", feature.Id }, { "door", feature.IsDoor }, { "position", new JArray(feature.Position.x, feature.Position.y, feature.Position.z) }, { "width", feature.Width }, { "height", feature.Height } });
            File.WriteAllText(Path.Combine(output, "actual-facade-features.json"), features.ToString(Formatting.None));
            var stairs = new JArray();
            foreach (var access in town.GetComponentsInChildren<BuildingAccessChunk>(true))
            {
                Need(access.OverflowCount == 0, "No lost access flights from capacity overflow");
                stairs.Add(new JObject { { "ownerName", access.name }, { "flights", access.FlightCount }, { "sealed", access.Sealed }, { "colliders", new JArray(access.GetComponentsInChildren<Collider>(true).Select(c => c.GetType().Name)) } });
            }

            File.WriteAllText(Path.Combine(output, "actual-stairs.json"), stairs.ToString(Formatting.Indented));
            var targets = new JArray();
            for (int i = 0; i < TargetIndices.Length; i++)
            {
                var source = town.Region.buildings[TargetIndices[i]];
                Need(source.id == TargetIds[i], "Target source index/identity exact, not ordinal guess");
                var row = rows.Single(r => ((JArray)r["originalIds"]).Values<string>().Contains(source.id));
                var parts = ((JArray)row["parts"]).Select(r => (string)r["partId"]).ToArray();
                int actualWalls = parts.Sum(part => wallsByPart.TryGetValue(part, out int count) ? count : 0);
                bool emitted = (string)row["status"] == "Emitted";
                Need(emitted ? actualWalls > 0 : actualWalls == 0 && !string.IsNullOrEmpty((string)row["reason"]), "Target disposition tied to actual walls or explicit suppression; never infer emission from road gap");
                targets.Add(new JObject { { "sourceArrayIndex", TargetIndices[i] }, { "sourceId", source.id }, { "actualWallColliders", actualWalls }, { "emitted", emitted }, { "ledger", row.DeepClone() }, { "clearanceAccepted", false }, { "note", "Native inspection evidence only; Regional must measure these actual wall buffers against actual final road packets." } });
            }

            File.WriteAllText(Path.Combine(output, "case7-target-dispositions.json"), targets.ToString(Formatting.Indented));
            receipt["target1697ActuallyEmitted"] = (bool)targets[0]["emitted"];
            receipt["target1005ActuallyEmitted"] = (bool)targets[1]["emitted"];
            receipt["originalSources"] = originalIds.Length;
            receipt["renderEntries"] = rows.Count;
            receipt["emittedEntries"] = rows.Count(r => (string)r["status"] == "Emitted");
            receipt["suppressedEntries"] = rows.Count(r => (string)r["status"] == "Suppressed");
            receipt["baseline235NotAnAcceptanceTarget"] = true;
            receipt["wallColliders"] = walls.Length;
            receipt["wallFaceRays"] = wallFaceRays;
            receipt["wallBaseSamples"] = wallBaseSamples;
            receipt["targetDispositionsExported"] = true;
        }
    }
}
