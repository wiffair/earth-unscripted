using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed class VehiclePackPart
    {
        public string Name, Attachment;
        public int Material;
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public int[] Triangles;
        public Vector2[] UV;
    }
    public sealed class VehiclePackMaterial
    {
        public Color Colour;
        public float Metal, Smoothness;
        public bool Paint;
        public VehiclePackPaintFinish Finish;
    }
    public sealed class VehiclePackWipers
    {
        public Vector3[] Pivots;
        public float[] SweepDegrees;
    }
    public sealed class VehiclePack
    {
        public ImportedVehicleSettings Settings;
        public string Author, Description, Licence, ArchiveHash;
        public VehiclePackMaterial[] Materials;
        public VehiclePackPart[] Parts;
        public byte[] TexturePng;
        public VehiclePackEngineAudio EngineAudio;
        public VehiclePackWipers Wipers;
    }

    /// <summary>Data-only .eucar ZIP reader. Parsing runs off the Unity thread;
    /// no archive content is extracted, evaluated or loaded as native code.
    /// Coordinates: metres, +Y up, +Z forward, +X right. Body vertices are relative
    /// to the physical root; wheel vertices are relative to each wheel centre.
    /// Budgets are checked on decompressed bytes and aggregate geometry before admission.</summary>
    public static class VehiclePackFormat
    {
        public const int MaximumArchiveBytes = 32 * 1024 * 1024;
        const int MaximumPayloadBytes = 48 * 1024 * 1024;
        const int MaximumVertices = 80000;
        const int MaximumIndices = 360000;
        static readonly HashSet<string> AllowedFiles = new HashSet<string>(StringComparer.Ordinal)
            { "manifest.json", "mesh.json", "texture.png", "README.txt", "LICENSE.txt", "engine-exhaust.wav", "engine-mechanical.wav" };

        public static VehiclePack Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                Require(stream.Length > 0 && stream.Length <= MaximumArchiveBytes, "Pack must be at most 32 MB.");
                using (var sha = SHA256.Create())
                {
                    string hash = Hex(sha.ComputeHash(stream));
                    stream.Position = 0;
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) return Read(archive, hash);
                }
            }
        }
        static VehiclePack Read(ZipArchive archive, string hash)
        {
            Require(archive.Entries.Count >= 2 && archive.Entries.Count <= AllowedFiles.Count, "Unexpected pack file count.");
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                Require(AllowedFiles.Contains(entry.FullName) && !files.ContainsKey(entry.FullName), "Unexpected or duplicate file: " + entry.FullName);
                int budget = entry.FullName.EndsWith(".wav", StringComparison.Ordinal) ? VehiclePackPcm.MaximumBytes : entry.FullName == "manifest.json" ? 32768 : entry.FullName.EndsWith(".txt", StringComparison.Ordinal) ? 65536 : 24 * 1024 * 1024;
                Require(entry.Length > 0 && entry.Length <= budget, "File exceeds its byte limit: " + entry.FullName);
                total += entry.Length;
                Require(total <= MaximumPayloadBytes, "Pack is too large when expanded.");
                using (var source = entry.Open())
                using (var target = new MemoryStream())
                {
                    var buffer = new byte[16384];
                    int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        Require(target.Length + count <= entry.Length, "Expanded file size differs.");
                        target.Write(buffer, 0, count);
                    }
                    Require(target.Length == entry.Length, "Truncated pack file.");
                    files.Add(entry.FullName, target.ToArray());
                }
            }
            Require(files.ContainsKey("manifest.json") && files.ContainsKey("mesh.json"), "manifest.json and mesh.json are required.");
            var manifest = Json(files["manifest.json"]);
            Require(Text(manifest, "format", 64) == "EarthUnscriptedVehiclePack" && Integer(manifest, "formatVersion", 1, 1) == 1, "Unsupported vehicle pack format.");
            ValidateRequirements(manifest);
            string id = Text(manifest, "id", 100);
            Require(Regex.IsMatch(id, @"\A[a-z][a-z0-9]*(?:[.-][a-z0-9]+)+\z"), "Use a stable publisher-qualified ID, e.g. studio.car-name.");
            Require(Text(manifest, "meshSha256", 64) == Digest(files["mesh.json"]), "Mesh checksum differs.");
            var pack = new VehiclePack
            {
                ArchiveHash = hash, Author = Text(manifest, "author", 100), Description = Text(manifest, "description", 400),
                Licence = Text(manifest, "licence", 200), Settings = Settings(manifest, id)
            };
            if (files.TryGetValue("texture.png", out var texture))
            {
                Require(Text(manifest, "textureSha256", 64) == Digest(texture), "Texture checksum differs.");
                Require(texture.Length >= 24 && texture[0] == 137 && Encoding.ASCII.GetString(texture, 1, 3) == "PNG", "Texture must be PNG.");
                int width = BigEndian(texture, 16), height = BigEndian(texture, 20);
                Require(width > 0 && height > 0 && width <= 2048 && height <= 2048, "Texture must fit within 2048 × 2048 pixels.");
                pack.TexturePng = texture;
            }
            pack.Wipers = ReadWipers(manifest);
            ReadGeometry(pack, Json(files["mesh.json"]));
            if(pack.Materials.Any(material=>material.Finish!=null))RequireFeature(manifest,"layered-candy-paint-v1");
            bool firstWiper=pack.Parts.Any(part=>part.Attachment=="wiper0"),secondWiper=pack.Parts.Any(part=>part.Attachment=="wiper1");
            Require(pack.Wipers == null ? !firstWiper&&!secondWiper : firstWiper&&secondWiper,"Wiper metadata and both mesh attachments must agree.");
            pack.EngineAudio = ReadEngineAudio(manifest, files);
            pack.Settings.SupportsPaint=pack.Parts.Any(part=>pack.Materials[part.Material].Paint);
            return pack;
        }
        static void RequireFeature(JObject manifest,string name)
        {
            var features=manifest["requirements"]?["features"] as JArray;
            Require(features!=null&&features.Any(t=>t.Type==JTokenType.String&&(string)t==name),
                "Pack uses an extension without declaring required feature: "+name);
        }
        static VehiclePackWipers ReadWipers(JObject manifest)
        {
            if(manifest["wipers"]==null)return null;
            var data=manifest["wipers"] as JObject;
            Require(data!=null&&Integer(data,"formatVersion",1,1)==1,"Unsupported authored wipers.");
            RequireFeature(manifest,"authored-wipers-v1");
            var pivots=data["pivots"] as JArray;var sweeps=data["sweepDegrees"] as JArray;
            Require(pivots!=null&&pivots.Count==2&&sweeps!=null&&sweeps.Count==2,"Two wiper pivots and sweeps required.");
            var result=new VehiclePackWipers{Pivots=new Vector3[2],SweepDegrees=new float[2]};
            for(int i=0;i<2;i++)
            {
                var v=pivots[i] as JArray;Require(v!=null&&v.Count==3,"Invalid wiper pivot.");
                result.Pivots[i]=new Vector3(Value(v[0],-2,2),Value(v[1],0,3),Value(v[2],-3,3));
                result.SweepDegrees[i]=Value(sweeps[i],-100,100);
                Require(Math.Abs(result.SweepDegrees[i])>=10,"Wiper sweep must be at least 10 degrees.");
            }
            return result;
        }
        // Optional extension: old packs have no engineAudio and retain their
        // existing synthesis/VW behaviour. Audio entries without metadata fail closed.
        static VehiclePackEngineAudio ReadEngineAudio(JObject manifest, Dictionary<string, byte[]> files)
        {
            bool exhaustPresent = files.ContainsKey("engine-exhaust.wav");
            bool mechanicalPresent = files.ContainsKey("engine-mechanical.wav");
            if (manifest["engineAudio"] == null)
            {
                Require(!exhaustPresent && !mechanicalPresent, "Orphaned engine audio files.");
                return null;
            }
            RequireFeature(manifest,"engine-audio-pcm-v1");
            var audio = manifest["engineAudio"] as JObject;
            Require(audio != null && exhaustPresent && mechanicalPresent, "Engine audio requires metadata and both WAV files.");
            Require(Integer(audio, "formatVersion", 1, 1) == 1, "Unsupported engine audio format.");
            var result = new VehiclePackEngineAudio
            {
                Exhaust = ReadLoop(audio, "exhaust", "engine-exhaust.wav", files),
                Mechanical = ReadLoop(audio, "mechanical", "engine-mechanical.wav", files),
                MinimumPitch = Number(audio, "minimumPitch", .5f, 1),
                MaximumPitch = Number(audio, "maximumPitch", 1, 2.5f),
                BlendStartThrottle = Number(audio, "blendStartThrottle", 0, .9f),
                BlendEndThrottle = Number(audio, "blendEndThrottle", .1f, 1),
                PitchResponseExponent = OptionalNumber(audio, "pitchResponseExponent", 1, .25f, 1),
                ExhaustLoadRetention = OptionalNumber(audio, "exhaustLoadRetention", .5f, 0, 1)
            };
            Require(result.BlendEndThrottle > result.BlendStartThrottle, "Engine blend interval must increase.");
            if(audio["pitchResponseExponent"]!=null||audio["exhaustLoadRetention"]!=null)RequireFeature(manifest,"engine-audio-response-v1");
            // Each target voice gain is affine in the throttle blend, so its
            // combined maximum occurs at one of these two endpoints.
            float exhaust=result.Exhaust.Gain, mechanical=result.Mechanical.Gain;
            Require(Math.Max(exhaust + .1f * mechanical, result.ExhaustLoadRetention * exhaust + mechanical) <= 1f,
                "Combined engine target gain exceeds master/engine budget.");
            return result;
        }
        static VehiclePackPcmData ReadLoop(JObject audio, string role, string filename, Dictionary<string, byte[]> files)
        {
            var loop = audio[role] as JObject;
            Require(loop != null && Text(loop, "file", 32) == filename, "Unexpected engine loop file.");
            Require(Text(loop, "sha256", 64) == Digest(files[filename]), "Engine loop checksum differs.");
            Require(loop["referenceRpmEstimated"]?.Type == JTokenType.Boolean, "Declare whether loop reference RPM is estimated.");
            float rpm = Number(loop, "referenceRpm", 300, 10000);
            float gain = Number(loop, "gain", 0, 1);
            var pcm = VehiclePackPcm.Decode(files[filename]);
            pcm.ReferenceRpm = rpm; pcm.Gain = gain;
            pcm.ReferenceRpmEstimated = (bool)loop["referenceRpmEstimated"];
            return pcm;
        }
        static void ValidateRequirements(JObject manifest)
        {
            if (manifest["requirements"] == null) return;
            var requirements = manifest["requirements"] as JObject;
            var features = requirements?["features"] as JArray;
            Require(features != null && features.Count > 0 && features.Count <= 8, "Invalid pack requirements.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var feature in features)
            {
                Require(feature.Type == JTokenType.String, "Pack feature requirement must be text.");
                string name = (string)feature;
                Require(Regex.IsMatch(name, @"\A[a-z][a-z0-9-]{0,63}\z") && seen.Add(name), "Invalid or duplicate feature requirement.");
                Require(name == "engine-audio-pcm-v1" || name == "engine-audio-response-v1" || name == "layered-candy-paint-v1" || name == "authored-wipers-v1",
                    "This vehicle pack requires a newer game build with feature: " + name);
            }
        }
        static VehiclePackPaintFinish ReadPaintFinish(JObject material)
        {
            if (material["finish"] == null) return null;
            var finish = material["finish"] as JObject;
            Require(finish != null && Text(finish, "kind", 32) == "layered-candy-v1", "Unsupported material finish; update the game.");
            var transmission = finish["transmission"] as JArray;
            Require(transmission != null && transmission.Count == 3, "Expected RGB coat transmission.");
            return new VehiclePackPaintFinish
            {
                Transmission = new Vector3(Value(transmission[0], .001f, 1), Value(transmission[1], .001f, 1), Value(transmission[2], .001f, 1)),
                Thickness = Number(finish, "opticalThickness", .1f, 3),
                FlakeStrength = Number(finish, "flakeStrength", 0, 1),
                FlakeScale = Number(finish, "flakeScalePerMetre", 100, 2000),
                Clearcoat = Number(finish, "clearcoat", 0, 1)
            };
        }
        static int BigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        static ImportedVehicleSettings Settings(JObject json, string id)
        {
            var p = json["physics"] as JObject;
            Require(p != null, "Missing physics settings.");
            var s = new ImportedVehicleSettings
            {
                Id = id, Name = Text(json, "name", 80), Version = Text(json, "version", 32), Revision = Integer(json, "revision", 1, 1000000), Price = Integer(json, "priceCredits", 0, 1000000),
                Mass = Number(p, "massKg", 500, 4000), Power = Number(p, "powerFactor", .25f, 4), Speed = Number(p, "speedMetresPerSecond", 10, 100),
                Grip = Number(p, "gripFactor", .5f, 2), OffroadGrip = Number(p, "offroadGripFactor", .3f, 2), FrontDriveFraction = Number(p, "frontDriveFraction", 0, 1),
                BodyWidth = Number(p, "bodyWidth", .8f, 2.8f), Track = Number(p, "track", 1, 2.8f), Wheelbase = Number(p, "wheelbase", 1.6f, 4),
                WheelRadius = Number(p, "wheelRadius", .22f, .8f), TyreWidth = Number(p, "tyreWidth", .15f, .8f),
                AnchorY = Number(p, "suspensionAnchorY", .2f, 1.4f), RayLength = Number(p, "suspensionRayLength", .3f, 1.8f), Travel = Number(p, "suspensionTravel", .1f, .8f),
                SpringRate = Number(p, "springNewtonsPerMetre", 20000, 100000), DamperRate = Number(p, "damperNewtonSecondsPerMetre", 1000, 12000),
                ColliderWidth = Number(p, "colliderWidth", .6f, 2.6f), ColliderLength = Number(p, "colliderLength", 1.8f, 5),
                ColliderY = Number(p, "colliderCentreY", .2f, 1.3f), ColliderHeight = Number(p, "colliderHeight", .3f, 1.5f), RoofHeight = Number(p, "roofHeight", .8f, 2.5f),
                CentreOfMassY = Number(p, "centreOfMassY", 0, .8f), CockpitX = Number(p, "cockpitX", -.8f, .8f),
                CockpitY = Number(p, "cockpitY", .5f, 2.3f), CockpitZ = Number(p, "cockpitZ", -1.5f, 1.5f)
            };
            Require(Regex.IsMatch(s.Version, @"\A(?:0|[1-9][0-9]{0,3})\.(?:0|[1-9][0-9]{0,3})\.(?:0|[1-9][0-9]{0,3})\z"), "Pack version must use major.minor.patch, e.g. 1.0.0.");
            s.StructuralStrength = OptionalNumber(p,"structuralStrength",1,.5f,3);
            s.SafeLandingSpeed = OptionalNumber(p,"safeLandingMetresPerSecond",5.5f,4,14);
            s.WadingDepth = OptionalNumber(p,"wadingDepthMetres",.35f,.2f,1.5f);
            s.WheelObstacleContact = (bool?)p["wheelObstacleContact"] == true;
            s.EngineLayout = (string)p["engineLayout"] ?? "standard";
            Require(s.EngineLayout == "standard" || s.EngineLayout == "v8-crossplane" || s.EngineLayout == "flat4-aircooled", "Unsupported engine layout.");
            float staticCompression = s.Mass * 9.81f / (4 * s.SpringRate);
            Require(staticCompression < s.Travel * .8f && s.Travel < s.RayLength, "Suspension cannot support the stated mass.");
            float restRoot = s.RayLength - s.AnchorY - staticCompression;
            Require(restRoot >= -.1f && restRoot <= 1.2f && s.ColliderY - s.ColliderHeight / 2 + restRoot >= .12f, "Chassis would intersect the ground at rest.");
            Require(s.ColliderWidth <= s.BodyWidth && s.ColliderLength >= s.Wheelbase * .7f, "Chassis dimensions are inconsistent.");
            return s;
        }
        static void ReadGeometry(VehiclePack pack, JObject json)
        {
            var materials = json["materials"] as JArray;
            var parts = json["parts"] as JArray;
            Require(materials != null && materials.Count > 0 && materials.Count <= 16 && parts != null && parts.Count > 0 && parts.Count <= 128, "Invalid material or part count.");
            pack.Materials = new VehiclePackMaterial[materials.Count];
            for (int index = 0; index < materials.Count; index++)
            {
                var material = materials[index] as JObject;
                Require(material != null, "Invalid material.");
                var colour = material["colour"] as JArray;
                Require(colour != null && colour.Count == 3, "Expected RGB material colour.");
                pack.Materials[index] = new VehiclePackMaterial
                {
                    Colour = new Color(Value(colour[0], 0, 1), Value(colour[1], 0, 1), Value(colour[2], 0, 1)),
                    Metal = Number(material, "metallic", 0, 1), Smoothness = Number(material, "smoothness", 0, 1), Paint = (bool?)material["paint"] == true, Finish = ReadPaintFinish(material)
                };
            }
            pack.Parts = new VehiclePackPart[parts.Count];
            int vertices = 0, indices = 0;
            var attachments = new HashSet<string>();
            bool body = false;
            for (int index = 0; index < parts.Count; index++)
            {
                var row = parts[index] as JObject;
                Require(row != null, "Invalid mesh part.");
                string attachment = Text(row, "attachment", 20);
                Require(attachment == "body" || attachment == "steering" || attachment == "wheel0" || attachment == "wheel1" || attachment == "wheel2" || attachment == "wheel3" || attachment == "wiper0" || attachment == "wiper1", "Unknown attachment.");
                attachments.Add(attachment);
                var positions = row["vertices"] as JArray;
                var triangles = row["triangles"] as JArray;
                Require(positions != null && positions.Count >= 9 && positions.Count % 3 == 0 && triangles != null && triangles.Count >= 3 && triangles.Count % 3 == 0, "Invalid triangle mesh.");
                vertices += positions.Count / 3; indices += triangles.Count;
                Require(vertices <= MaximumVertices && indices <= MaximumIndices, "Geometry exceeds 80,000 vertices or 120,000 triangles.");
                var part = new VehiclePackPart
                {
                    Name = Text(row, "name", 80), Attachment = attachment, Material = Integer(row, "material", 0, materials.Count - 1),
                    Vertices = new Vector3[positions.Count / 3], Triangles = new int[triangles.Count], UV = new Vector2[positions.Count / 3]
                };
                for (int v = 0; v < part.Vertices.Length; v++) part.Vertices[v] = new Vector3(Value(positions[v * 3], -5, 5), Value(positions[v * 3 + 1], -3, 4), Value(positions[v * 3 + 2], -5, 5));
                if (row["normals"] is JArray normals)
                {
                    Require(normals.Count == positions.Count, "Normal count differs from vertices.");
                    part.Normals = new Vector3[part.Vertices.Length];
                    for (int v = 0; v < part.Normals.Length; v++)
                    {
                        var normal = new Vector3(Value(normals[v * 3], -1, 1), Value(normals[v * 3 + 1], -1, 1), Value(normals[v * 3 + 2], -1, 1));
                        Require(normal.sqrMagnitude > .8f && normal.sqrMagnitude < 1.2f, "Expected unit normal.");
                        part.Normals[v] = normal.normalized;
                    }
                }
                for (int t = 0; t < part.Triangles.Length; t++)
                {
                    Require(triangles[t].Type == JTokenType.Integer, "Triangle indexes must be integers.");
                    int vertex = (int)triangles[t]; Require(vertex >= 0 && vertex < part.Vertices.Length, "Triangle index is outside its part."); part.Triangles[t] = vertex;
                }
                if (row["uv"] is JArray uv)
                {
                    Require(uv.Count == part.Vertices.Length * 2, "UV count differs from vertices.");
                    for (int v = 0; v < part.Vertices.Length; v++) part.UV[v] = new Vector2(Value(uv[v * 2], -32, 32), Value(uv[v * 2 + 1], -32, 32));
                }
                body |= attachment == "body";
                pack.Parts[index] = part;
            }
            Require(body && Enumerable.Range(0, 4).All(i => attachments.Contains("wheel" + i)), "A body and all four wheel attachments are required.");
        }
        static JObject Json(byte[] bytes)
        {
            using (var input = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes))) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
            {
                var result = JObject.Load(input, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                Require(!input.Read(), "Trailing JSON content."); return result;
            }
        }
        static float Value(JToken token, float minimum, float maximum)
        {
            Require(token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float), "Expected numeric value.");
            double value = (double)token;
            Require(!double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum, "Numeric value outside supported range.");
            return (float)value;
        }
        static float OptionalNumber(JObject json,string name,float fallback,float minimum,float maximum) => json[name]==null?fallback:Value(json[name],minimum,maximum);
        static float Number(JObject json, string name, float minimum, float maximum) => Value(json[name], minimum, maximum);
        static int Integer(JObject json, string name, int minimum, int maximum)
        {
            Require(json[name]?.Type == JTokenType.Integer, "Expected integer: " + name);
            return (int)Value(json[name], minimum, maximum);
        }
        static string Text(JObject json, string name, int maximum)
        {
            Require(json[name]?.Type == JTokenType.String, "Missing text: " + name);
            string value = (string)json[name];
            Require(!string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(c => char.IsControl(c) || c == '<' || c == '>'), "Invalid text: " + name);
            return value;
        }
        public static string Digest(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
