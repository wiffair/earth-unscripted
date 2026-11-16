using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RoadRush.Core800;
using RoadRush.TerrainDraft;

namespace RoadRush
{
    public sealed partial class DerivedCase7TownInspectorProof
    {
        // A query may see many foreign colliders; overflow REFUSES rather than
        // dropping an unseen owned hit. Actual scope has no moving actors.
        const int MaximumRayIntersections = 64;
        const long MaximumRawCoverageBytes = 128L * 1024 * 1024;
        readonly RaycastHit[] rayIntersections = new RaycastHit[MaximumRayIntersections];
        StreamWriter registrationLog;
        int registrationRows;
        readonly Dictionary<Collider, int> physicalOrdinals = new Dictionary<Collider, int>();
        int legacyOwnMisses, legacyOwnHeightDisagreements, changedCentroidCoordinates;
        int sourceWorldF32ZeroArea, sourceWorldF32ReversedArea;
        double minimumFaceArea = double.PositiveInfinity, minimumFaceEdge = double.PositiveInfinity;
        sealed class FaceWitness
        {
            internal int Triangle;
            internal Vector3 LegacyPoint, CoveragePoint;
            internal double ExactX, ExactY, ExactZ, AreaXZ, MinimumEdge;
            internal bool FloatWorldZeroArea, FloatWorldReversed;
        }

        /// <summary>
        /// Current native mesh readback must agree with its expected immutable
        /// packet BEFORE registration is inferred. Does not mint any capability.
        /// Disabled/missing/foreign mesh bindings refuse without native ray fallback.
        /// </summary>
        static bool NativePacketMatches(MeshFilter filter, LocalTerrainMesh expected, out Vector3[] vertices, out int[] indices)
        {
            vertices = null;
            indices = null;
            if (!filter || expected == null || !filter.gameObject.activeInHierarchy)
                return false;
            var collider = filter.GetComponent<MeshCollider>();
            var renderer = filter.GetComponent<MeshRenderer>();
            var owner = filter.GetComponent<GeneratedMeshOwner>();
            var mesh = filter.sharedMesh;
            if (!mesh || !collider || !renderer || !owner || !collider.enabled || collider.isTrigger || collider.convex || !renderer.enabled || collider.sharedMesh != mesh || owner.Mesh != mesh)
                return false;
            var matrix = filter.transform.localToWorldMatrix;
            var wanted = Matrix4x4.Translate(new Vector3((float)expected.OriginX, (float)expected.OriginY, (float)expected.OriginZ));
            for (int component = 0; component < 16; component++)
                if (matrix[component] != wanted[component])
                    return false;
            vertices = mesh.vertices;
            indices = mesh.triangles;
            if (vertices.Length * 3 != expected.XYZ.Length || !indices.SequenceEqual(expected.Indices))
                return false;
            for (int vertex = 0; vertex < vertices.Length; vertex++)
                if (!Bits(vertices[vertex].x, expected.XYZ[vertex * 3]) || !Bits(vertices[vertex].y, expected.XYZ[vertex * 3 + 1]) || !Bits(vertices[vertex].z, expected.XYZ[vertex * 3 + 2]))
                    return false;
            return true;
        }

        static double Orientation(double ax, double az, double bx, double bz, double cx, double cz)
        {
            return (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
        }

        static double Edge(double ax, double az, double bx, double bz)
        {
            return Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        }

        static FaceWitness FaceAt(MeshFilter filter, LocalTerrainMesh packet, Vector3[] vertices, int[] indices, int triangle)
        {
            var a = vertices[indices[triangle * 3]];
            var b = vertices[indices[triangle * 3 + 1]];
            var c = vertices[indices[triangle * 3 + 2]];
            double ax = packet.OriginX + a.x, az = packet.OriginZ + a.z;
            double bx = packet.OriginX + b.x, bz = packet.OriginZ + b.z;
            double cx = packet.OriginX + c.x, cz = packet.OriginZ + c.z;
            double x = (ax + bx + cx) / 3, y = (packet.OriginY + a.y + packet.OriginY + b.y + packet.OriginY + c.y) / 3;
            double z = (az + bz + cz) / 3;
            var wa = filter.transform.TransformPoint(a);
            var wb = filter.transform.TransformPoint(b);
            var wc = filter.transform.TransformPoint(c);
            double sourceSigned = Orientation(ax, az, bx, bz, cx, cz);
            double floatSigned = Orientation(wa.x, wa.z, wb.x, wb.z, wc.x, wc.z);
            return new FaceWitness
            {
                Triangle = triangle,
                ExactX = x,
                ExactY = y,
                ExactZ = z,
                AreaXZ = -sourceSigned * .5,
                MinimumEdge = Math.Min(Edge(ax, az, bx, bz), Math.Min(Edge(bx, bz, cx, cz), Edge(cx, cz, ax, az))),
                LegacyPoint = filter.transform.TransformPoint((a + b + c) / 3),
                CoveragePoint = new Vector3((float)x, (float)y, (float)z),
                FloatWorldZeroArea = floatSigned == 0,
                FloatWorldReversed = sourceSigned * floatSigned < 0
            };
        }

        static FaceWitness LargestRegistrationFace(MeshFilter filter, LocalTerrainMesh packet, Vector3[] vertices, int[] indices)
        {
            int selected = -1;
            double largestArea = 0;
            for (int triangle = 0; triangle < indices.Length / 3; triangle++)
            {
                var a = vertices[indices[triangle * 3]];
                var b = vertices[indices[triangle * 3 + 1]];
                var c = vertices[indices[triangle * 3 + 2]];
                double area = -Orientation(a.x, a.z, b.x, b.z, c.x, c.z) * .5;
                if (!(area > 0) || double.IsNaN(area) || double.IsInfinity(area))
                    throw new InvalidDataException("Native packet has invalid/downward source triangle");
                // Strict greater preserves the earliest original triangle on ties.
                if (area > largestArea)
                {
                    selected = triangle;
                    largestArea = area;
                }
            }

            if (selected < 0)
                throw new InvalidDataException("No existing registration triangle");
            return FaceAt(filter, packet, vertices, indices, selected);
        }

        static Ray CoverageRay(FaceWitness face, double height)
        {
            return new Ray(new Vector3(face.CoveragePoint.x, (float)(height + FaceRayStartMetres), face.CoveragePoint.z), Vector3.down);
        }

        void VerifyImmediateRegistration(int ordinal, MeshFilter filter)
        {
            var expected = plan.CopyForUpload(filter.name);
            Need(NativePacketMatches(filter, expected, out var vertices, out var indices), "Immediate exact native packet/renderer/collider binding");
            var selected = LargestRegistrationFace(filter, expected, vertices, indices);
            var ray = CoverageRay(selected, selected.ExactY);
            bool hit = filter.GetComponent<MeshCollider>().Raycast(ray, out var actual, FaceRayLengthMetres);
            bool passed = hit && !selected.FloatWorldZeroArea && !selected.FloatWorldReversed
                && Math.Abs(actual.point.y - selected.ExactY) <= RayToleranceMetres;
            var first = FaceAt(filter, expected, vertices, indices, 0);
            var firstRay = new Ray(first.LegacyPoint + Vector3.up * FaceRayStartMetres, Vector3.down);
            bool firstHit = filter.GetComponent<MeshCollider>().Raycast(firstRay, out var firstActual, FaceRayLengthMetres);
            var matrix = filter.transform.localToWorldMatrix;
            var record = new JObject
            {
                {
                    "ordinal",
                    ordinal
                },
                {
                    "packet",
                    expected.Id
                },
                {
                    "role",
                    plan.PacketRole(expected.Id).ToString()
                },
                {
                    "root",
                    filter.transform.parent.name
                },
                {
                    "triangle",
                    selected.Triangle
                },
                {
                    "projectedArea",
                    selected.AreaXZ
                },
                {
                    "minimumEdge",
                    selected.MinimumEdge
                },
                {
                    "localXYZ",
                    new JArray(new[] { indices[selected.Triangle * 3], indices[selected.Triangle * 3 + 1], indices[selected.Triangle * 3 + 2] }.SelectMany(i => new[] { vertices[i].x, vertices[i].y, vertices[i].z }))
                },
                {
                    "localFloatBits",
                    new JArray(new[] { indices[selected.Triangle * 3], indices[selected.Triangle * 3 + 1], indices[selected.Triangle * 3 + 2] }.SelectMany(i => new[] { BitConverter.SingleToInt32Bits(vertices[i].x), BitConverter.SingleToInt32Bits(vertices[i].y), BitConverter.SingleToInt32Bits(vertices[i].z) }))
                },
                {
                    "matrixFloatBits",
                    new JArray(Enumerable.Range(0, 16).Select(i => BitConverter.SingleToInt32Bits(matrix[i])))
                },
                {
                    "rayOrigin",
                    new JArray(ray.origin.x, ray.origin.y, ray.origin.z)
                },
                {
                    "rayDirection",
                    new JArray(0, -1, 0)
                },
                {
                    "hit",
                    hit
                },
                {
                    "hitPoint",
                    hit ? (JToken)new JArray(actual.point.x, actual.point.y, actual.point.z) : JValue.CreateNull()
                },
                {
                    "firstTriangleLegacyPoint",
                    new JArray(first.LegacyPoint.x, first.LegacyPoint.y, first.LegacyPoint.z)
                },
                {
                    "firstTriangleOwnHit",
                    firstHit
                },
                {
                    "firstTriangleOwnY",
                    firstHit ? (JToken)firstActual.point.y : JValue.CreateNull()
                },
                {
                    "firstTriangleArea",
                    first.AreaXZ
                },
                {
                    "firstIndices",
                    new JArray(indices.Take(3))
                },
                {
                    "firstLocalFloatBits",
                    new JArray(indices.Take(3).SelectMany(i => new[] { BitConverter.SingleToInt32Bits(vertices[i].x), BitConverter.SingleToInt32Bits(vertices[i].y), BitConverter.SingleToInt32Bits(vertices[i].z) }))
                },
                {
                    "nativeColliderId",
                    filter.GetComponent<MeshCollider>().GetEntityId().ToString()
                },
                {
                    "meshCookingOptions",
                    filter.GetComponent<MeshCollider>().cookingOptions.ToString()
                },
                {
                    "frame",
                    Time.frameCount
                },
                {
                    "fixedTime",
                    Time.fixedTime
                },
                {
                    "autoSyncTransforms",
                    Physics.autoSyncTransforms
                },
                {
                    "sameCallbackNoExtraSyncOrYield",
                    true
                }
            };
            record["floatWorldZeroArea"] = selected.FloatWorldZeroArea;
            record["floatWorldReversed"] = selected.FloatWorldReversed;
            // Persist before refusal, including actual native bits/probes. No alternate
            // point/foreign mesh can turn an unsuccessful registration into a pass.
            if (registrationLog == null)
                registrationLog = new StreamWriter(Path.Combine(output, "immediate-registration-witnesses.jsonl"), false, new System.Text.UTF8Encoding(false));
            registrationLog.WriteLine(record.ToString(Formatting.None));
            registrationLog.Flush();
            registrationRows++;
            Need(passed, "Largest existing ground-face native registration ray");
            Need(plan.TryGround(ray.origin.x, ray.origin.z, out var groundHit) && Math.Abs(groundHit.Height - actual.point.y) <= RayToleranceMetres, "Registration ground role/query support");
            if (plan.PacketRole(expected.Id) == PreparedWorldMeshPlan800.Role.GroundRoad)
                Need(plan.TryRoad(packetRoads[expected.Id][0], ray.origin.x, ray.origin.z, out var named) && Math.Abs(named.Height - actual.point.y) <= RayToleranceMetres, "Registration named pavement support");
        }

        /// <summary>
        /// Global PhysX query with an EXCLUSIVE by-reference collider set. Foreign
        /// ground is never accepted, even if closer. Nearest OWNED hit is compared
        /// with the plan at the same already-cast float X/Z. Overflow refuses.
        /// </summary>
        bool TryOnlyOwnedRay(Ray ray, HashSet<Collider> allowed, out RaycastHit result, out int rawCount, out bool overflow)
        {
            rawCount = Physics.RaycastNonAlloc(ray, rayIntersections, FaceRayLengthMetres, ~0, QueryTriggerInteraction.Ignore);
            overflow = rawCount >= rayIntersections.Length;
            result = default;
            if (overflow)
                return false;
            bool found = false;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < rawCount; i++)
                if (allowed.Contains(rayIntersections[i].collider) && rayIntersections[i].collider && rayIntersections[i].collider.enabled && rayIntersections[i].collider.gameObject.activeInHierarchy && rayIntersections[i].distance < distance)
                {
                    result = rayIntersections[i];
                    distance = result.distance;
                    found = true;
                }

            return found;
        }

        void NativeWitnessSandboxNegatives()
        {
            // These are isolated native helper tests, NOT a source/plan/lease issuer.
            // Never mutate the genuine1064 meshes to run negative cases.
            var sandbox = Own("Native witness helper sandbox, no lease");
            var v = new[]
            {
                new Vector3(-2, 0, -2),
                new Vector3(0, 0, 2),
                new Vector3(2, 0, -2)
            };
            var indices = new[]
            {
                0,
                1,
                2
            };
            var data = new LocalTerrainMesh("qa-witness-sandbox", 10000, 0, 10000, v.SelectMany(p => new[] { p.x, p.y, p.z }).ToArray(), indices);
            var meshObject = Geometry.Mesh(data.Id, sandbox.transform, v, indices, new Vector2[3], roadMaterial, true);
            meshObject.transform.localPosition = new Vector3(10000, 0, 10000);
            var filter = meshObject.GetComponent<MeshFilter>();
            var collider = meshObject.GetComponent<MeshCollider>();
            ownedMeshes.Add(filter.sharedMesh);
            Need(NativePacketMatches(filter, data, out _, out _), "Native sandbox valid binding positive");
            collider.enabled = false;
            Need(!NativePacketMatches(filter, data, out _, out _), "Disabled collider cannot satisfy registration");
            collider.enabled = true;
            var original = collider.sharedMesh;
            collider.sharedMesh = null;
            Need(!NativePacketMatches(filter, data, out _, out _), "Missing collider mesh cannot satisfy registration");
            var wrong = new Mesh
            {
                name = "Owned wrong sandbox mesh",
                vertices = v,
                triangles = new[]
                {
                    0,
                    2,
                    1
                }
            };
            ownedMeshes.Add(wrong);
            collider.sharedMesh = wrong;
            Need(!NativePacketMatches(filter, data, out _, out _), "Wrong collider mesh cannot satisfy registration");
            collider.sharedMesh = original;
            Destroy(wrong);
            var foreign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foreign.name = "Foreign sandbox floor, NEVER owned ground";
            foreign.transform.SetParent(sandbox.transform, false);
            foreign.transform.position = new Vector3(10000, -.25f, 10000);
            foreign.transform.localScale = new Vector3(10, .5f, 10);
            collider.enabled = false;
            Physics.SyncTransforms(); // Separate helper negative stage AFTER all true immediate/coverage tests.
            var ray = new Ray(new Vector3(10000, 1, 10000), Vector3.down);
            Need(Physics.Raycast(ray, out var any, FaceRayLengthMetres) && any.collider == foreign.GetComponent<Collider>(), "Foreign floor actually present for exclusion test");
            Need(!TryOnlyOwnedRay(ray, new HashSet<Collider> { collider }, out _, out _, out _), "Foreign-only ground cannot satisfy owned query");
            collider.enabled = true;
            Physics.SyncTransforms();
            Need(TryOnlyOwnedRay(ray, new HashSet<Collider> { collider }, out var restored, out _, out _) && restored.collider == collider, "Real enabled owned helper surface positive");
            Destroy(sandbox);
            receipt["nativeWitnessNegativeSandboxPassed"] = true;
        }
    }
}
