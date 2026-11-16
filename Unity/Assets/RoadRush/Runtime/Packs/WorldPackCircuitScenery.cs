using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadRush.Packs
{
    /// <summary>Data-only, source-bound circuit art. Coordinates are metres in the
    /// layout's east/up/north frame. No URLs, scripts, shaders or plugins are loaded.
    /// Optional unsupported art is skipped by the common capability resolver.</summary>
    public sealed class CircuitSceneryPlan
    {
        public const int MaximumBytes = 32 * 1024 * 1024;
        const int MaximumParts = 60000, MaximumVertices = 1500000;
        // Render sheets deliberately expand triangle corners to keep opposite
        // normals separate. Budget those actual allocations, including both
        // visual LODs and independent collision, rather than only source points.
        public const int MaximumAllocatedMeshVertices = 3000000;
        const int MaximumJsonTokens = 3000000;
        const string CapacityErrorKey = "RoadRush.CircuitScenery.Capacity";
        public int AllocatedMeshVertices { get; private set; }
        public static bool IsCapacityFailure(Exception error) => error?.Data[CapacityErrorKey] is bool value && value;
        static void Budget(bool ok, string reason)
        {
            if (ok) return;
            var error = new InvalidDataException("World pack: circuit scenery " + reason);
            error.Data[CapacityErrorKey] = true;
            throw error;
        }
        public sealed class Part
        {
            public string Name, Material;
            public int Lod;
            public bool Collision;
            public Vector3 Position, Size;
            public float Yaw;
            public Vector3[] Vertices;
            public int[] Triangles;
        }
        public string LayoutSha256 { get; private set; }
        public string PitRoadId { get; private set; }
        public string PitComplexId { get; private set; }
        public string PitComplexName { get; private set; }
        /// <summary>Rebind only an owned boot plan, never mutate cached/shared art
        /// or move an active service transaction to another lane.</summary>
        internal CircuitSceneryPlan WithServiceAnchor(CircuitPitComplex selected)
        {
            var next=new CircuitSceneryPlan {LayoutSha256=LayoutSha256,PitRoadId=selected.PitRoadId,
                Bay=selected.Bay,BayForward=selected.Forward,PitComplexId=selected.Id,PitComplexName=selected.Name,
                Parts=Parts,AllocatedMeshVertices=AllocatedMeshVertices};
            next.IndexVegetationExclusions();return next;
        }
        public Vector3 Bay { get; private set; }
        public Vector3 BayForward { get; private set; }
        public IReadOnlyList<Part> Parts { get; private set; }
        readonly Dictionary<Vector2Int, List<Rect>> vegetationExclusions = new Dictionary<Vector2Int, List<Rect>>();
        const float ExclusionCellMetres = 128;
        /// <summary>Keep procedural tree crowns clear of physical pack structures.
        /// Index once at import rather than scanning thousands of seat parts per tree.</summary>
        public bool BlocksVegetation(float x, float z)
        {
            var key = new Vector2Int(Mathf.FloorToInt(x / ExclusionCellMetres), Mathf.FloorToInt(z / ExclusionCellMetres));
            return vegetationExclusions.TryGetValue(key, out var cells) && cells.Any(r => r.Contains(new Vector2(x,z)));
        }
        void IndexVegetationExclusions()
        {
            const float CrownClearanceMetres = 8;
            foreach (var part in Parts.Where(p => p.Collision))
            {
                var bounds = new Bounds(); bool first = true;
                void Include(Vector3 p) { if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p); }
                if (part.Vertices != null) foreach (var p in part.Vertices) Include(p);
                else
                {
                    var rotation = Quaternion.Euler(0, part.Yaw, 0);
                    for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                        Include(part.Position + rotation * new Vector3(x * part.Size.x * .5f, 0, z * part.Size.z * .5f));
                }
                var rect = Rect.MinMaxRect(bounds.min.x-CrownClearanceMetres, bounds.min.z-CrownClearanceMetres,
                    bounds.max.x+CrownClearanceMetres, bounds.max.z+CrownClearanceMetres);
                for (int x = Mathf.FloorToInt(rect.xMin/ExclusionCellMetres); x <= Mathf.FloorToInt(rect.xMax/ExclusionCellMetres); x++)
                    for (int z = Mathf.FloorToInt(rect.yMin/ExclusionCellMetres); z <= Mathf.FloorToInt(rect.yMax/ExclusionCellMetres); z++)
                    {
                        var key = new Vector2Int(x,z);
                        if (!vegetationExclusions.TryGetValue(key, out var cells)) vegetationExclusions.Add(key, cells = new List<Rect>());
                        cells.Add(rect);
                    }
            }
        }
        public static readonly string[] Palette = { "concrete", "steel", "roof", "glass", "blue", "red", "white", "dark", "asphalt", "gravel", "green", "yellow" };

        static void Check(bool ok, string reason) => WorldPackPolicy.Require(ok, "circuit scenery " + reason);
        static float Number(JToken t)
        {
            Check(t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer), "numeric value");
            float v = (float)t; Check(!float.IsNaN(v) && !float.IsInfinity(v), "finite value"); return v;
        }
        static Vector3 Vector(JToken t)
        {
            Check(t is JArray a && a.Count == 3, "vector arity");
            return new Vector3(Number(t[0]), Number(t[1]), Number(t[2]));
        }
        static bool Bounded(Vector3 v) => Mathf.Abs(v.x) <= 6000 && Mathf.Abs(v.z) <= 6000 && v.y >= -100 && v.y <= 1000;
        public static CircuitSceneryPlan Parse(string text, GeoRegion region, string layoutHash)
        {
            Budget(text != null && Encoding.UTF8.GetByteCount(text) <= MaximumBytes, "byte budget");
            // A byte limit alone permits millions of tiny JSON values. Count
            // tokens before constructing the JObject tree to bound its peak size.
            using (var scan = new JsonTextReader(new StringReader(text)) { MaxDepth = 12, DateParseHandling = DateParseHandling.None })
            {
                int tokens = 0;
                while (scan.Read()) Budget(++tokens <= MaximumJsonTokens, "JSON token budget");
            }
            JObject d;
            using (var r = new JsonTextReader(new StringReader(text)) { MaxDepth = 12, DateParseHandling = DateParseHandling.None })
            { d = JObject.Load(r, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); Check(!r.Read(), "trailing data"); }
            Check((string)d["format"] == "RoadRushCircuitScenery1" && (string)d["layoutSha256"] == layoutHash, "format/layout binding");
            Check(region != null && region.circuit && (string)d["worldId"] == region.id, "world binding");
            Check(Math.Abs((double)d["latitude"] - region.latitude) < 1e-9 && Math.Abs((double)d["longitude"] - region.longitude) < 1e-9, "coordinate origin");
            string pitId = (string)d["pitRoadId"];
            var pit = region.roads.SingleOrDefault(x => x.id == pitId);
            Check(pit != null && pit.kind == "service" && pit.points.Length >= 3 && pit.width >= 5 && pit.width <= 20, "mapped pit lane");
            Vector3 bay = Vector(d["bay"]), forward = Vector(d["bayForward"]);
            Check(Bounded(bay) && Mathf.Abs(forward.y) < .001f && forward.magnitude > .99f && forward.magnitude < 1.01f, "service anchor");
            // A service bay must be on the declared lane; it cannot move repairs
            // into a grandstand/remote area through untrusted pack metadata.
            float separation = float.MaxValue;
            for (int i = 1; i < pit.points.Length; i++)
            {
                var a = pit.points[i - 1]; var v = pit.points[i] - a;
                float t = Mathf.Clamp01(Vector3.Dot(bay - a, v) / Mathf.Max(.0001f, v.sqrMagnitude));
                separation = Mathf.Min(separation, Vector3.Distance(bay, a + v * t));
            }
            Check(separation <= pit.width * .5f, "bay outside pit");
            var rows = d["parts"] as JArray; Check(rows != null && rows.Count > 0 && rows.Count <= MaximumParts, "part budget");
            var parts = new List<Part>(); int vertices = 0, collisions = 0, allocatedVertices = 0;
            foreach (JObject row in rows)
            {
                string name = (string)row["name"], material = (string)row["material"];
                Check(!string.IsNullOrWhiteSpace(name) && name.Length <= 120 && !name.Any(char.IsControl) && Palette.Contains(material), "identity/material");
                Check(row["lod"]?.Type == JTokenType.Integer && row["collision"]?.Type == JTokenType.Boolean, "typed flags");
                var p = new Part { Name = name, Material = material, Lod = (int)row["lod"], Collision = (bool)row["collision"] };
                Check(p.Lod >= -1 && p.Lod <= 1 && (!p.Collision || p.Lod == -1), "LOD/collision lifetime");
                if (p.Collision) Check(++collisions <= 512, "collider budget");
                if (row["vertices"] is JArray vv)
                {
                    var tt = row["triangles"] as JArray;
                    Check(vv.Count >= 3 && vv.Count <= 60000 && tt != null && tt.Count >= 3 && tt.Count <= 180000 && tt.Count % 3 == 0, "mesh budget");
                    allocatedVertices += tt.Count * ((p.Lod < 0 ? 2 : 1) + (p.Collision ? 1 : 0));
                    Budget(allocatedVertices <= MaximumAllocatedMeshVertices, "allocated mesh vertex budget");
                    p.Vertices = vv.Select(Vector).ToArray(); Check(p.Vertices.All(Bounded), "vertex bounds");
                    p.Triangles = tt.Select(t => { Check(t.Type == JTokenType.Integer, "integer index"); int n = (int)t; Check(n >= 0 && n < vv.Count, "index range"); return n; }).ToArray();
                    for (int i = 0; i < p.Triangles.Length; i += 3)
                        Check(Vector3.Cross(p.Vertices[p.Triangles[i + 1]] - p.Vertices[p.Triangles[i]], p.Vertices[p.Triangles[i + 2]] - p.Vertices[p.Triangles[i]]).sqrMagnitude > 1e-12f, "degenerate face");
                    vertices += vv.Count;
                }
                else
                {
                    allocatedVertices += 24 * ((p.Lod < 0 ? 2 : 1) + (p.Collision ? 1 : 0));
                    Budget(allocatedVertices <= MaximumAllocatedMeshVertices, "allocated mesh vertex budget");
                    p.Position = Vector(row["position"]); p.Size = Vector(row["size"]); p.Yaw = Number(row["yaw"]);
                    Check(Bounded(p.Position) && p.Size.x > 0 && p.Size.y > 0 && p.Size.z > 0 && p.Size.x <= 1000 && p.Size.y <= 100 && p.Size.z <= 1000 && Mathf.Abs(p.Yaw) <= 360, "box bounds");
                    vertices += 24;
                }
                Check(vertices <= MaximumVertices, "aggregate vertex budget"); parts.Add(p);
            }
            var plan = new CircuitSceneryPlan { LayoutSha256 = layoutHash, PitRoadId = pitId, Bay = bay, BayForward = forward, Parts = parts.AsReadOnly(), AllocatedMeshVertices = allocatedVertices };
            plan.IndexVegetationExclusions(); return plan;
        }
    }

    public sealed partial class WorldPackCircuitReader
    {
        /// <summary>Checks hashed immutable payload again on every boot. Scenery
        /// cannot supply missing driving geometry or modify a sibling layout.</summary>
        public CircuitSceneryPlan ReadScenery(string layoutId, GeoRegion region, System.Threading.CancellationToken token)
        {
            var layout = Layout(layoutId);
            if (!sceneryFiles.TryGetValue(layout.ElementId, out var file)) return null;
            return CircuitSceneryPlan.Parse(ReadChecked(file, CircuitSceneryPlan.MaximumBytes, token), region, layout.Payload.Sha256);
        }
        readonly Dictionary<string, PackFile> sceneryFiles = new Dictionary<string, PackFile>();
        void ReadSceneryCatalogue(WorldPackManifest manifest, PackCompatibility compatible)
        {
            foreach (var d in compatible.Decisions.Where(x => x.Loaded && x.Variant.Feature == "circuit-scenery" && x.Variant.Schema == 1))
            {
                WorldPackPolicy.Require(d.Element.AffectsGameplay && d.Variant.Files.Count == 1, "physical scenery must declare gameplay effects");
                var owner = Layouts.SingleOrDefault(l => d.Element.Dependencies.Contains(l.ElementId));
                WorldPackPolicy.Require(owner != null && !sceneryFiles.ContainsKey(owner.ElementId), "scenery needs one unambiguous layout owner");
                var file = manifest.Files.Single(f => f.Path == d.Variant.Files[0]);
                WorldPackPolicy.Require(file.Bytes <= CircuitSceneryPlan.MaximumBytes, "scenery file byte budget");
                sceneryFiles.Add(owner.ElementId, file);
            }
        }
    }
}
