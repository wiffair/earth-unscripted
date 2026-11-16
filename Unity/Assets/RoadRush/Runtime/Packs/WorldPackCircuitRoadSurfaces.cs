using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RoadRush.Packs
{
    public sealed partial class WorldPackCircuitReader
    {
        readonly Dictionary<string, PackFile> roadSurfaceFiles = new Dictionary<string, PackFile>();

        /// <summary>Recheck the immutable file and its layout binding on every
        /// boot. Reading this data does not publish meshes or activate actors.</summary>
        public CircuitRoadSurfacePlan ReadRoadSurfaces(string layoutId, GeoRegion region, CancellationToken token)
        {
            var layout = Layout(layoutId);
            if (!roadSurfaceFiles.TryGetValue(layout.ElementId, out var file)) return null;
            return CircuitRoadSurfacePlan.Parse(ReadChecked(file, CircuitRoadSurfacePlan.MaximumBytes, token),
                region, layout.Payload.Sha256, token);
        }

        void ReadRoadSurfaceCatalogue(WorldPackManifest manifest, PackCompatibility compatible)
        {
            foreach (var decision in compatible.Decisions.Where(d => d.Loaded &&
                d.Variant.Feature == "circuit-road-surfaces" && d.Variant.Schema == 1))
            {
                WorldPackPolicy.Require(decision.Element.AffectsGameplay && decision.Variant.Files.Count == 1,
                    "authored pavement must declare gameplay effects and one payload");
                var owners = Layouts.Where(l => decision.Element.Dependencies.Contains(l.ElementId)).ToArray();
                WorldPackPolicy.Require(owners.Length == 1 && owners[0].Readable &&
                    !roadSurfaceFiles.ContainsKey(owners[0].ElementId), "authored pavement needs one readable layout owner");
                var file = manifest.Files.Single(f => f.Path == decision.Variant.Files[0]);
                WorldPackPolicy.Require(file.Bytes > 0 && file.Bytes <= CircuitRoadSurfacePlan.MaximumBytes,
                    "authored pavement byte budget");
                roadSurfaceFiles.Add(owners[0].ElementId, file);
            }
        }
    }
}
