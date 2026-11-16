using UnityEngine;

namespace RoadRush
{
    public sealed partial class WorldBuilder
    {
        public AutomaticWorldHost AutomaticWorld { get; private set; }
        public void BuildAutomaticWorld(AutomaticWorldHost host)
        {
            if (!host || !host.Ready || !host.transform.IsChildOf(transform))
                throw new System.InvalidOperationException("Ready owned automatic world required");
            AutomaticWorld = host;
            Route = new RoutePath(host.Region);
            BuildEnvironment();
            var scenery = new GameObject("Automatic photographic scenery");
            scenery.transform.SetParent(transform,false);
            scenery.AddComponent<AutomaticScenery>().Initialize(host);
            scenery.AddComponent<AutomaticBuildingDetails>().Initialize(host);
            RenderSettings.fogDensity = .00012f;
            Asphalt = host.Renderer.RoadMaterial;
            GeographicWorld = new GameObject("Automatic geography queries").AddComponent<GeoWorld>();
            GeographicWorld.transform.SetParent(transform, false);
            GeographicWorld.AttachAutomaticWorld(host);
            // This location's real generated road is the service/recovery
            // anchor. Do not flatten surveyed terrain for a fictional garage.
            Garage = new GameObject("Roadside service anchor").transform;
            Garage.SetParent(transform, false);
            Garage.position = Route.At(35, Route.LaneOffset(35));
            Garage.rotation = Route.Rotation(35);
            BuildCheckpoints();
            DynamicGI.UpdateEnvironment();
        }
    }
}
