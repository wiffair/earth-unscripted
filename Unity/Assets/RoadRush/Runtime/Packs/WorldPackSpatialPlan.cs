using System;
using System.Collections.Generic;
using System.Linq;

namespace RoadRush.Packs
{
    public sealed class WorldPackSpatialPlan
    {
        public readonly string ManifestSha256, SubsetIdentity, Problem;
        public readonly bool FitsPreparationBudget;
        public readonly long SelectedPayloadBytes;
        public readonly IReadOnlyList<string> SelectedElements, Files, Load, Retain, Unload;
        internal WorldPackSpatialPlan(string manifest, string subset, string problem, long bytes, List<string> selected, List<string> files,
            List<string> load, List<string> retain, List<string> unload)
        {
            ManifestSha256 = manifest; SubsetIdentity = subset; Problem = problem; FitsPreparationBudget = problem == null;
            SelectedPayloadBytes = bytes; SelectedElements = selected.AsReadOnly(); Files = files.AsReadOnly();
            Load = load.AsReadOnly(); Retain = retain.AsReadOnly(); Unload = unload.AsReadOnly();
        }
    }

    /// <summary>
    /// Metadata-only nearby-content selection. Payload bytes bound I/O/preparation, not GPU/physics memory;
    /// each feature reader must separately admit decoded meshes, actors and render budgets.
    /// A successful plan is a proposal: the scene owner must finish preparation before committing unloads.
    /// </summary>
    public static class WorldPackSpatialPlanner
    {
        public static WorldPackSpatialPlan Plan(WorldPackManifest pack, PackCompatibility compatibility, PackBounds visible,
            PackBounds preload, IEnumerable<string> activeElements, int maximumElements = 256, long maximumPayloadBytes = 64L * 1024 * 1024)
        {
            if (pack == null || compatibility == null || activeElements == null) throw new ArgumentNullException();
            WorldPackPolicy.Require(compatibility.ManifestSha256 == pack.ManifestSha256 && compatibility.CanActivate, "compatible exact pack required for spatial planning");
            WorldPackPolicy.Require(preload.Contains(visible), "preload bounds must include visible bounds");
            WorldPackPolicy.Require(maximumElements >= 1 && maximumElements <= WorldPackPolicy.MaximumElements
                && maximumPayloadBytes >= 1 && maximumPayloadBytes <= WorldPackPolicy.MaximumInstalledBytes, "spatial preparation budget range");
            var decisions = compatibility.Decisions.ToDictionary(d => d.Element.Id, StringComparer.Ordinal);
            var active = new HashSet<string>(activeElements, StringComparer.Ordinal);
            WorldPackPolicy.Require(active.All(decisions.ContainsKey), "active elements must belong to this exact pack generation");
            var selected = new HashSet<string>(decisions.Values.Where(d => d.Loaded && d.Element.Bounds.Intersects(preload)).Select(d => d.Element.Id), StringComparer.Ordinal);
            var pending = new Queue<string>(selected);
            while (pending.Count > 0)
                foreach (string dependency in decisions[pending.Dequeue()].Element.Dependencies)
                {
                    WorldPackPolicy.Require(decisions[dependency].Loaded, "unsupported dependency reached spatial preparation");
                    if (selected.Add(dependency)) pending.Enqueue(dependency);
                }
            var selectedFiles = new HashSet<string>(selected.SelectMany(id => decisions[id].Variant.Files), StringComparer.Ordinal);
            long bytes = pack.Files.Where(f => selectedFiles.Contains(f.Path)).Sum(f => f.Bytes);
            var problems = new List<string>();
            if (selected.Count > maximumElements) problems.Add("Complete nearby selection needs " + selected.Count + " elements; budget " + maximumElements);
            if (bytes > maximumPayloadBytes) problems.Add("Complete nearby selection needs " + bytes + " payload bytes; budget " + maximumPayloadBytes);
            bool fits = problems.Count == 0;
            // Do not discard an arbitrary prefix or retire the existing area when the next area cannot be admitted.
            var load = fits ? selected.Except(active).OrderBy(id => id, StringComparer.Ordinal).ToList() : new List<string>();
            var retain = (fits ? active.Intersect(selected) : active).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var unload = fits ? active.Except(selected).OrderBy(id => id, StringComparer.Ordinal).ToList() : new List<string>();
            return new WorldPackSpatialPlan(pack.ManifestSha256, compatibility.SubsetIdentity, fits ? null : string.Join("; ", problems), bytes,
                selected.OrderBy(id => id, StringComparer.Ordinal).ToList(), selectedFiles.OrderBy(id => id, StringComparer.Ordinal).ToList(), load, retain, unload);
        }
    }
}
