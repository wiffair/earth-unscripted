using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RoadRush.RegionalDraft;

namespace RoadRush.Packs
{
    /// <summary>
    /// Worker-side rendering interpretation of a bounded regional selection. The returned
    /// building array is detached; canonical geography, source records and activation gates
    /// remain unchanged. Absolute LiDAR datum is not inferred from relative building heights.
    /// </summary>
    public sealed class WorldPackBuildingWindow
    {
        readonly JArray renderBuildings;
        public int RenderBuildingCount => renderBuildings.Count;
        public JArray CopyRenderBuildings() => (JArray)renderBuildings.DeepClone();
        public readonly RegionalRenderAdmission.Result Admission;
        public readonly bool SourceAuthoringBlocked;
        public readonly string Identity, BaselineBuildingsSha256, ManifestSha256, SelectionIdentity, RenderedBuildingsSha256;
        public readonly int BaselineParts, RegionalParts;
        const double MetresPerDegree = 111319.4908;
        const int MaximumRenderableParts = 2500;

        WorldPackBuildingWindow(JArray buildings, RegionalRenderAdmission.Result admission,
            RegionalCollection.Selection selection, string manifestHash, string baselineHash, int baseline)
        {
            renderBuildings = (JArray)buildings.DeepClone(); Admission = admission; SourceAuthoringBlocked = selection.RuntimeAuthoringGate;
            BaselineParts = baseline; RegionalParts = selection.Buildings.Count;
            ManifestSha256 = manifestHash; BaselineBuildingsSha256 = baselineHash; SelectionIdentity = selection.SelectionIdentity;
            RenderedBuildingsSha256 = WorldPackPolicy.Hash(Encoding.UTF8.GetBytes(buildings.ToString(Formatting.None)));
            Identity = WorldPackPolicy.Hash(Encoding.UTF8.GetBytes("RoadRushBuildingWindow1|" + manifestHash + "|" + baselineHash + "|" + SelectionIdentity + "|" + Admission.Identity + "|" + RenderedBuildingsSha256));
        }

        public static WorldPackBuildingWindow Prepare(JObject baselineWorld, RegionalCollection.Selection selection,
            string manifestHash, CancellationToken cancellation = default)
        {
            if (baselineWorld == null || selection == null) throw new ArgumentNullException();
            WorldPackPolicy.Require(selection.FitsDeclaredBuildingBudget, "regional window budget exceeded");
            WorldPackPolicy.Require(manifestHash != null && manifestHash.Length == 64 && manifestHash.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'), "pack identity required");
            var baseline = RegionalMergeWorldCandidate.CountryFeatures(baselineWorld);
            var sourceArray = (JArray)baselineWorld["buildings"];
            string baselineHash = WorldPackPolicy.Hash(Encoding.UTF8.GetBytes(sourceArray.ToString(Formatting.None)));
            double latitude = (double)baselineWorld["latitude"], longitude = (double)baselineWorld["longitude"];
            double cosine = Math.Cos(latitude * Math.PI / 180);
            WorldPackPolicy.Require(Math.Abs(cosine) > .001, "unsupported polar projection");
            int nx = (int)baselineWorld["nx"], nz = (int)baselineWorld["nz"];
            double minX = (double)baselineWorld["minX"], minZ = (double)baselineWorld["minZ"], width = (double)baselineWorld["width"], depth = (double)baselineWorld["depth"];
            var heights = ((JArray)baselineWorld["heights"]).Select(h => (double)h).ToArray();
            WorldPackPolicy.Require(nx >= 2 && nz >= 2 && (long)nx * nz == heights.Length && width > 0 && depth > 0, "baseline terrain grid");
            double Ground(double x, double z)
            {
                double gx = Math.Max(0, Math.Min(nx - 1, (x - minX) / width * (nx - 1)));
                double gz = Math.Max(0, Math.Min(nz - 1, (z - minZ) / depth * (nz - 1)));
                int ix = Math.Min((int)gx, nx - 2), iz = Math.Min((int)gz, nz - 2);
                double u = gx - ix, v = gz - iz;
                return (heights[iz * nx + ix] * (1 - u) + heights[iz * nx + ix + 1] * u) * (1 - v)
                    + (heights[(iz + 1) * nx + ix] * (1 - u) + heights[(iz + 1) * nx + ix + 1] * u) * v;
            }
            var detail = selection.Buildings.Select(building =>
            {
                cancellation.ThrowIfCancellationRequested();
                var points = building.Points.Select(point => {
                    double x = (point.Longitude - longitude) * MetresPerDegree * cosine;
                    double z = (point.Latitude - latitude) * MetresPerDegree;
                    return new RegionalBuildingMerge.Point(x, Ground(x, z), z);
                }).ToArray();
                WorldPackPolicy.Require(points.Max(p => p.X) >= minX && points.Min(p => p.X) <= minX + width
                    && points.Max(p => p.Z) >= minZ && points.Min(p => p.Z) <= minZ + depth, "selected footprint outside current world");
                return new RegionalBuildingMerge.Feature(building.Id, building.SourceKey, building.Kind, building.Roof,
                    building.Height, false, RegionalBuildingMerge.Origin.Regional, points);
            }).ToArray();
            var strict = RegionalBuildingMerge.Prepare(baseline, detail, new RegionalBuildingMerge.Policy(), cancellation);
            string sourceSet = WorldPackPolicy.Hash(Encoding.UTF8.GetBytes(baselineHash + "|" + selection.SelectionIdentity));
            var admission = RegionalRenderAdmission.Prepare(strict, baseline.Concat(detail), sourceSet);
            WorldPackPolicy.Require(admission.Admitted.Count <= MaximumRenderableParts
                && admission.Admitted.Sum(f => (long)f.Points.Count) <= 120000
                && admission.Admitted.Sum(f => (long)f.Points.Count * f.Points.Count) <= 20000000, "combined building render budget exceeded; previous scenery must remain active");
            return new WorldPackBuildingWindow(new JArray(admission.Admitted.Select(RegionalMergeWorldCandidate.ToJson)),
                admission, selection, manifestHash, baselineHash, baseline.Length);
        }
    }
}
