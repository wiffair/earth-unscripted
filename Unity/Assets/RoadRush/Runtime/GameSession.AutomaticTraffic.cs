using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GameSession
    {
        readonly struct AutomaticTrafficSite
        {
            public readonly Vector3 Point, Forward;
            public AutomaticTrafficSite(Vector3 point, Vector3 forward) { Point = point; Forward = forward; }
        }

        List<AutomaticTrafficSite> AutomaticTrafficSites(VehicleController player, int count)
        {
            if (!World.AutomaticWorld) return null;
            var region = World.Route.Region;
            var candidates = new List<AutomaticTrafficSite>();
            foreach (var road in region.roads.OrderBy(road => road.id))
            {
                if (road.width < 3 || road.kind == "track") continue;
                for (float distance = 12; distance < road.length - 12; distance += 24)
                {
                    var centre = region.RoadPoint(road, distance, out var sourceForward);
                    foreach (int direction in road.oneWay == 0 ? new[] { 1, -1 } : new[] { road.oneWay })
                    {
                        var point = region.DrivingLanePoint(road, centre, sourceForward * direction, out var forward);
                        float gap = Vector3.Distance(player.Body.position, point);
                        if (gap < 90 || gap > 850) continue;
                        if (!World.AutomaticWorld.Renderer.HasGeometryAt(point) ||
                            !World.AutomaticWorld.Renderer.HasGeometryAt(point + forward * 3) ||
                            !World.AutomaticWorld.Renderer.HasGeometryAt(point - forward * 3)) continue;
                        if(Physics.CheckBox(point+Vector3.up*1.3f,new Vector3(1.15f,.35f,2.7f),Quaternion.LookRotation(forward,Vector3.up),~(1<<8),QueryTriggerInteraction.Ignore))continue;
                        candidates.Add(new AutomaticTrafficSite(point, forward));
                    }
                }
            }
            var selected = new List<AutomaticTrafficSite>();
            var random = new System.Random(128);
            foreach (var site in candidates.OrderBy(site => random.Next()))
            {
                if (selected.Any(previous => Vector3.Distance(previous.Point, site.Point) < 22)) continue;
                selected.Add(site);
                if (selected.Count == count) return selected;
            }
            throw new System.InvalidOperationException("Starting traffic needs more prepared road space (" + selected.Count + "/" + count + ")");
        }
    }
}
