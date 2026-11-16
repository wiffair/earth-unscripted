using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EarthUnscripted.Packs;

namespace EarthUnscripted
{
    /// <summary>Exact imported layout provenance, independent of mutable career/racing state.
    /// Imported layouts stay offline until geometry/rules/subset negotiation is implemented.</summary>
    public sealed class PackCircuitContext
    {
        public readonly PackCircuitEventSettings Event;
        public readonly string Token, ManifestSha256, VenueId, VenueName, LayoutId, LayoutName, Attribution;
        internal PackCircuitContext(WorldPackCircuitReference reference, WorldPackCircuitReader reader, WorldPackManifest manifest,PackCircuitEventSettings eventSettings=null)
        {
            var layout = reader.Layout(reference.LayoutId); Event=eventSettings;
            Token = reference.Token; ManifestSha256 = reader.ManifestSha256; VenueId = reader.VenueId;
            VenueName = reader.Name; LayoutId = layout.Id; LayoutName = layout.Name; Attribution = manifest.Attribution;
        }
    }

    /// <summary>
    /// Main-thread capture contains strings only. Verification/reading runs on the normal
    /// boot worker; the decoded owned region then follows the existing finalizer/renderer.
    /// Original payload bytes and bundled region IDs are never rewritten on disk.
    /// </summary>
    internal static class PackCircuitSources
    {
        internal static PackEngineSupport Readers(string version) => new PackEngineSupport(version,
            new Dictionary<string, int> { { "circuit-catalogue", 1 }, { "circuit-layout", 1 }, { "provenance", 1 }, { "circuit-event", 2 }, { "circuit-pit-complexes", 1 }, { "racing-rules", 1 }, { "circuit-scenery", 1 }, { "circuit-road-surfaces", 1 } });
        internal sealed class Captured
        {
            readonly string root, version, token;
            PackCircuitContext loaded;
            string originalWorldId;
            Packs.CircuitSceneryPlan scenery;
            CircuitRoadSurfacePlan roadSurfaces;
            internal PackCircuitContext Context { get; private set; }
            internal Captured(string libraryRoot, string engineVersion, string launchToken)
            { root = libraryRoot; version = engineVersion; token = launchToken; }
            internal string Read(CancellationToken cancellation)
            {
                try
                {
                    var reference = WorldPackCircuitReference.Load(root, token, cancellation);
                    var store = new WorldPackStore(root); var installed = store.Locate(reference.Pack);
                    var reader = reference.Resolve(store, Readers(version), cancellation);
                    var layout = reader.Layout(reference.LayoutId);
                    string raw = reader.ReadRawLayoutJson(layout.Id, cancellation);
                    var eventSettings=reference.EventId==null?null:reader.ReadEvent(reference.EventId,cancellation);
                    loaded = new PackCircuitContext(reference, reader, installed.Manifest,eventSettings); originalWorldId = layout.WorldId;
                    var rawRegion=UnityEngine.JsonUtility.FromJson<GeoRegion>(raw);
                    scenery=reader.ReadScenery(layout.Id,rawRegion,cancellation);
                    roadSurfaces=reader.ReadRoadSurfaces(layout.Id,rawRegion,cancellation);
                    scenery=reader.ResolvePitComplex(layout.Id,rawRegion,scenery,roadSurfaces,eventSettings?.PitComplexId,cancellation);
                    if(roadSurfaces!=null&&scenery==null)throw new InvalidDataException("Authored circuit requires a declared mapped pit/service anchor");
                    if((long)(scenery?.AllocatedMeshVertices??0)+(roadSurfaces?.AllocatedMeshVertices??0)>CircuitSceneryPlan.MaximumAllocatedMeshVertices)
                        throw new InvalidDataException("Circuit pavement and scenery exceed their shared mesh allocation budget");
                    return raw;
                }
                catch (IOException error) when (error is FileNotFoundException || error is DirectoryNotFoundException)
                {
                    throw new InvalidDataException("The saved circuit pack or layout reference is missing. Reinstall its exact revision, retry, or open the county to keep your career.", error);
                }
            }
            internal void BindOwned(GeoRegion region, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (loaded == null || region == null || !region.circuit || region.id != originalWorldId)
                    throw new InvalidDataException("Imported layout does not contain its declared circuit world");
                if((region.roadWidthSchema != 0 && (region.roadWidthSchema != 1 || roadSurfaces == null)) || region.id.StartsWith("download-peer-", StringComparison.Ordinal))
                    throw new InvalidDataException("This circuit layout reader requires raw source geometry; finalized surface snapshots need a supported import feature");
                // These are actual existing route/timing contracts, not invented road
                // gradient limits: the legacy lap tracker clamps lengths below100m.
                if (!region.roads.Any(r => r.kind == "raceway" && !r.tunnel))
                    throw new InvalidDataException("Imported layout has no supported driving raceway");
                var route = new RoutePath(region);
                if (!route.IsClosed || route.Length < 100)
                    throw new InvalidDataException("Imported layout needs a closed lap of at least100m for the current timing system");
                for (int i = 1; i < region.route.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if ((region.route[i] - region.route[i - 1]).sqrMagnitude <= .000001f)
                        throw new InvalidDataException("Imported circuit route contains a zero-length leg");
                }
                region.CircuitScenery=scenery;
                region.CircuitRoadSurfaces=roadSurfaces;
                // Rename only the fresh owned DTO. Independent imported layouts/revisions
                // cannot collide with bundled circuit PB keys or borrow special-case art.
                region.id = region.networkMapId = loaded.Token;
                region.name = loaded.VenueName.EndsWith(" " + loaded.LayoutName, StringComparison.OrdinalIgnoreCase)
                    || loaded.VenueName.Equals(loaded.LayoutName, StringComparison.OrdinalIgnoreCase)
                    ? loaded.VenueName : loaded.VenueName + " · " + loaded.LayoutName;
                Context = loaded;
            }
        }
        internal static Captured Capture(string token)
        {
            if (!WorldPackCircuitReference.IsToken(token)) throw new InvalidDataException("Invalid imported circuit launch token");
            return new Captured(RegionalPackActivation.LibraryRoot(), UnityEngine.Application.version, token);
        }
    }
}
