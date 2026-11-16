using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GeoWorld
    {
        AutomaticWorldHost automaticWorld;
        public bool IsAutomaticWorld => automaticWorld != null;
        public void AttachAutomaticWorld(AutomaticWorldHost host)
        {
            if (!host || !host.Ready || Region != null) throw new System.InvalidOperationException("Fresh native geography owner required");
            automaticWorld = host; Region = host.Region; Asphalt = host.Renderer.RoadMaterial;
            MarkingDiagnostic = null;
        }
        void UpdateAutomaticWorld()
        {
            if (!automaticWorld || !Observer) return;
            automaticWorld.Observer = Observer;
            if (!automaticWorld.Player) automaticWorld.Player = Observer.GetComponent<VehicleController>();
            if (!automaticWorld.View)
            {
                var drive = FindAnyObjectByType<DriveCamera>();
                if (drive) automaticWorld.View = drive.GetComponent<Camera>();
            }
        }
        System.Collections.IEnumerator PrepareAutomaticFrontier(Vector3 position, System.Action<int,int> progress, System.Func<bool> cancelled)
        {
            DriveFrontierReady = false;
            automaticWorld.Renderer.SetEmbeddedObserver(position, Observer ? Observer.forward : Vector3.forward, automaticWorld.View);
            double started = Time.realtimeSinceStartupAsDouble;
            while (!automaticWorld.Renderer.HasGeometryAt(position))
            {
                if (cancelled?.Invoke() == true) yield break;
                if (Time.realtimeSinceStartupAsDouble - started > 300) throw new System.TimeoutException("Nearby automatic terrain is still loading");
                DriveFrontierStatus = automaticWorld.Status; progress?.Invoke(0, 1); yield return null;
            }
            DriveFrontierReady = true; DriveFrontierStatus = "Starting roads ready"; progress?.Invoke(1, 1);
        }
    }
}
