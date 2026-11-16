using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NativeWorld = RoadRush.NewbridgePrototype.NewbridgeWorld;

namespace RoadRush
{
    // Gameplay queries use the same owned-core profiles that produced the
    // colliders. Halo profiles are context, never a competing height authority.
    static class AutomaticWorldFacade
    {
        public static GeoRegion Create(GeoRegion source, NativeWorld renderer)
        {
            var guide = renderer.InitialRoute;
            if (guide?.points == null || guide.points.Length < 2) throw new InvalidOperationException("Starting road guide is missing");
            var region = new GeoRegion { id = source.id, name = source.name,
                latitude = source.latitude, longitude = source.longitude, datum = source.datum,
                countryCode = source.countryCode, timeZone = source.timeZone,
                drivingSide = source.drivingSide, preparedWidths = true,
                railwayProfilesPrepared = true, route = (Vector3[])guide.points.Clone(),
                NativeWorld = renderer, waters = Array.Empty<GeoWater>(),
                areas = Array.Empty<GeoArea>(), buildings = Array.Empty<GeoBuilding>() };
            Refresh(region, renderer);
            return region;
        }

        public static void Refresh(GeoRegion region, NativeWorld renderer)
        {
            var maps = renderer.ResidentAreas.OrderBy(map => map.tileX).ThenBy(map => map.tileZ).ToArray();
            Populate(region,maps);
        }

        // Numeric maps are immutable after adoption. Capture their array on the
        // main thread, then build queries without reading any renderer object.
        public static GeoRegion QuerySnapshot(GeoRegion source,NativeWorld.Map[] maps)
        {
            var result=new GeoRegion {NativeWorld=source.NativeWorld};
            Populate(result,maps);
            return result;
        }

        static void Populate(GeoRegion region,NativeWorld.Map[] maps)
        {
            if (maps.Length == 0) throw new InvalidOperationException("Automatic world has no resident areas");
            var nodeWays = new Dictionary<string, HashSet<string>>();
            foreach(var feature in maps.SelectMany(map => map.features).Where(feature => feature.category == "road"))
                foreach(var node in feature.sourceNodeIds ?? Array.Empty<string>())
                {
                    if(!node.StartsWith("osm:", StringComparison.Ordinal))continue;
                    if(!nodeWays.TryGetValue(node,out var ways))nodeWays[node]=ways=new HashSet<string>();
                    ways.Add(feature.sourceId);
                }
            var sharedNodes = new HashSet<string>(nodeWays.Where(row => row.Value.Count > 1).Select(row => row.Key));
            var roads = new List<GeoRoad>();
            var rails = new List<GeoRailway>();
            var waters = new List<GeoWater>();
            var buildings = new List<GeoBuilding>();
            foreach (var map in maps)
            {
                foreach (var feature in map.features)
                {
                    if (feature.category != "road" && feature.category != "rail" && feature.category != "water") continue;
                    // Halo-only features contribute to shared-node topology above, but
                    // cannot contribute any owned-core render/query section.
                    if (!MayTouchCore(feature.points, map)) continue;
                    int part = 0;
                    var identities = new Dictionary<Vector2Int, string>();
                    for(int index=0;index<feature.points.Length;index++)
                        if(feature.sourceNodeIds != null && index<feature.sourceNodeIds.Length)
                            identities[MillimetreKey(feature.points[index])]=feature.sourceNodeIds[index];
                    foreach (var section in SourceSections(feature,sharedNodes))
                    foreach (var points in Clip(section, map))
                    {
                        string Node(Vector3 point)
                        {
                            bool known=identities.TryGetValue(MillimetreKey(point),out var node);
                            if(known && node.StartsWith("osm:",StringComparison.Ordinal))return node;
                            bool boundary=Mathf.Abs(point.x-map.coreMinX)<.002f || Mathf.Abs(point.x-map.coreMaxX)<.002f ||
                                Mathf.Abs(point.z-map.coreMinZ)<.002f || Mathf.Abs(point.z-map.coreMaxZ)<.002f;
                            if(known&&!boundary)return node;
                            return "boundary:"+feature.sourceId+":"+Mathf.RoundToInt(point.x*100)+":"+Mathf.RoundToInt(point.z*100);
                        }
                        var nodes=points.Select(Node).ToArray();
                        string id = "native:" + map.tileX + ":" + map.tileZ + ":" + feature.id + ":" + part++;
                        if (feature.category == "road")
                            roads.Add(new GeoRoad { id = id, NativeTravelWidth = feature.travelWidth,
                                NativeSourceWay = feature.sourceId, NativeForwardLimitMps=feature.speedLimitForwardMps, NativeBackwardLimitMps=feature.speedLimitBackwardMps, NativeControls = feature.controls, NativeTurnLanes=feature.turnLanes, NativeTurnLanesBackward=feature.turnLanesBackward, start = nodes[0], end = nodes[nodes.Length-1], sourceNodeIds = nodes,
                                name = feature.name, kind = feature.kind, width = feature.width,
                                limit = Mathf.RoundToInt(feature.speedLimitMps * 2.2369363f),
                                oneWay = feature.oneway ? 1 : 0,
                                layer = feature.layer, bridge = feature.bridge, tunnel = feature.tunnel, roundabout = feature.roundabout, points = points });
                        else if (feature.category == "rail")
                            rails.Add(new GeoRailway { id = id, name = feature.name, kind = feature.kind,
                                start = id + ":start", end = id + ":end", width = feature.width,
                                layer = feature.layer, bridge = feature.bridge, tunnel = feature.tunnel, estimatedProfile = false, points = points });
                        else waters.Add(new GeoWater { name = feature.name, width = feature.width, points = points });
                    }
                }
                foreach (var building in (map.buildings ?? Array.Empty<NativeWorld.Building>()).Where(b=>!b.underground))
                {
                    if (building.points == null || building.points.Length < 3) continue;
                    Vector3 centre = Vector3.zero;
                    foreach (var point in building.points) centre += point;
                    centre /= building.points.Length;
                    if (centre.x < map.coreMinX || centre.x >= map.coreMaxX || centre.z < map.coreMinZ || centre.z >= map.coreMaxZ) continue;
                    buildings.Add(new GeoBuilding { id = building.id, kind = "building", height = building.height,
                        points = (Vector3[])building.points.Clone() });
                }
            }
            region.roads = roads.ToArray(); region.railways = rails.ToArray();
            region.nativePlaces=maps.SelectMany(area=>area.places??Array.Empty<NativeWorld.Place>()).GroupBy(place=>place.id).Select(group=>group.First()).ToArray();
            region.waters = waters.ToArray(); region.buildings = buildings.ToArray();
            region.minX = maps.Min(map => map.coreMinX); region.minZ = maps.Min(map => map.coreMinZ);
            region.width = maps.Max(map => map.coreMaxX) - region.minX;
            region.depth = maps.Max(map => map.coreMaxZ) - region.minZ;
            var seed = maps[0]; region.nx = seed.nx; region.nz = seed.nz;
            region.heights = seed.heights;
            region.attribution = string.Join(" · ", maps.Select(map => map.attribution).Where(value => !string.IsNullOrEmpty(value)).Distinct());
            region.RebuildRenderQueryIndexesOnly(true);
        }

        static bool MayTouchCore(Vector3[] points, NativeWorld.Map map)
        {
            if (points.Length < 2) return false;
            float minX=float.PositiveInfinity,minZ=float.PositiveInfinity,maxX=float.NegativeInfinity,maxZ=float.NegativeInfinity;
            foreach(var point in points)
            { minX=Mathf.Min(minX,point.x);minZ=Mathf.Min(minZ,point.z);maxX=Mathf.Max(maxX,point.x);maxZ=Mathf.Max(maxZ,point.z); }
            // Include the boundary and rounding margin; clipping remains the authority.
            return !(maxX<map.coreMinX-.01f || minX>map.coreMaxX+.01f || maxZ<map.coreMinZ-.01f || minZ>map.coreMaxZ+.01f);
        }

        static Vector2Int MillimetreKey(Vector3 point) => new Vector2Int(Mathf.RoundToInt(point.x*1000),Mathf.RoundToInt(point.z*1000));

        static IEnumerable<Vector3[]> SourceSections(NativeWorld.Feature feature,HashSet<string> sharedNodes)
        {
            int first=0;
            for(int index=1;index<feature.points.Length-1;index++)
            {
                if(feature.category!="road" || feature.sourceNodeIds==null || !sharedNodes.Contains(feature.sourceNodeIds[index]))continue;
                var part=new Vector3[index-first+1];Array.Copy(feature.points,first,part,0,part.Length);yield return part;first=index;
            }
            var last=new Vector3[feature.points.Length-first];Array.Copy(feature.points,first,last,0,last.Length);yield return last;
        }

        static IEnumerable<Vector3[]> Clip(Vector3[] source, NativeWorld.Map map)
        {
            var part = new List<Vector3>();
            for (int i = 1; i < source.Length; i++)
            {
                var a = source[i - 1]; var b = source[i]; var delta = b - a;
                float lo = 0, hi = 1;
                bool Axis(float start, float direction, float min, float max)
                {
                    if (Mathf.Abs(direction) < .000001f) return start >= min && start <= max;
                    float first = (min - start) / direction, last = (max - start) / direction;
                    if (first > last) { float swap = first; first = last; last = swap; }
                    lo = Mathf.Max(lo, first); hi = Mathf.Min(hi, last); return lo <= hi;
                }
                if (!Axis(a.x, delta.x, map.coreMinX, map.coreMaxX) || !Axis(a.z, delta.z, map.coreMinZ, map.coreMaxZ) || hi - lo < .000001f)
                {
                    if (part.Count > 1) yield return part.ToArray();
                    part.Clear(); continue;
                }
                var begin = a + delta * lo; var end = a + delta * hi;
                var middle=(begin+end)*.5f;
                if(middle.x<map.coreMinX || middle.x>=map.coreMaxX || middle.z<map.coreMinZ || middle.z>=map.coreMaxZ)
                {if(part.Count>1)yield return part.ToArray();part.Clear();continue;}
                if (part.Count > 0 && (part[part.Count - 1] - begin).sqrMagnitude > .0001f)
                { if (part.Count > 1) yield return part.ToArray(); part.Clear(); }
                if (part.Count == 0) part.Add(begin);
                part.Add(end);
            }
            if (part.Count > 1) yield return part.ToArray();
        }
    }
}
