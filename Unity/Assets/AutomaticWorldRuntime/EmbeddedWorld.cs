using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // Automatic launch, muted audio and proof-player controls belong only
        // to the separate executable. A normal GameSession owns its real car,
        // camera, lighting, audio, physics settings and profile lifecycle.
        static bool IsPrototypePlayer => Application.identifier == "com.earthunscripted.prototype.liveworld";
        bool embedded;
        bool embeddedDistantStarted;
        bool embeddedGeometryDiagnostics;
        Vector3 embeddedHeading = Vector3.forward;

        Vector3[] plannedRouteSource;RoutePreparation plannedRoute;int plannedRouteRevision;
        public void SetPlannedRoute(Vector3[] points)
        {if(ReferenceEquals(points,plannedRouteSource))return;plannedRouteSource=points;plannedRoute=points==null?null:new RoutePreparation(points);plannedRouteRevision++;lastReconcilePosition=new Vector3(float.MaxValue,0,0);}
        public bool Ready => ready;
        public int ReusedGeometryChunks => report.reusedGeometryChunks;
        public int PendingGeometryChunks => report.pendingChunks;
        public int WarmGeometryChunks => warmChunks.Count;
        public long WarmGeometryEstimatedBytes => warmBytes;
        public string LoadingStatus => dataStatus;
        public Material RoadMaterial => asphalt;
        public IEnumerable<Map> ResidentAreas => areas.Values;
        public DriveRoute InitialRoute => map?.routes != null && map.routes.Length > 0 ? map.routes[0] : null;
        public event Action AreasChanged;
        public void EnableGeometryDiagnostics(bool enabled) { embeddedGeometryDiagnostics=enabled; }

        /// <summary>
        /// Prepare the same native geometry used by the proof player inside a
        /// normal world owner. Coordinates are metres in the worker's frame.
        /// The caller starts/stops the owned loopback worker, drives loading UI
        /// and destroys this object's root on cancellation or world retirement.
        /// No vehicle, camera, career, light or audio listener is created here.
        /// </summary>
        public IEnumerator PrepareEmbedded(string loopbackEndpoint, Vector3 start,
            Vector3 forward, string title, string attribution, Func<bool> cancelled = null)
        {
            if (IsPrototypePlayer || embedded || map != null)
                throw new InvalidOperationException("World renderer already owns a startup mode");
            if (!Uri.TryCreate(loopbackEndpoint, UriKind.Absolute, out var address) ||
                address.Scheme != "http" || address.Host != "127.0.0.1" ||
                address.Port <= 0 || address.UserInfo.Length != 0 ||
                address.Query.Length != 0 || address.Fragment.Length != 0 || address.AbsolutePath != "/")
                throw new ArgumentException("Owned loopback worker endpoint required");
            embedded = true;
            finishedStore = new FinishedGeometryStore(System.IO.Path.Combine(EarthUnscripted.SinglePlayerBeta.ProductRoot, "AutomaticWorldCache"));
            endpoint = loopbackEndpoint.TrimEnd('/');
            embeddedHeading = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (embeddedHeading.sqrMagnitude < .5f) embeddedHeading = Vector3.forward;
            observer = start;
            var bootstrap = new Bootstrap { start = start, headingX = embeddedHeading.x,
                headingZ = embeddedHeading.z, name = title, attribution = attribution };
            if (cancelled?.Invoke() == true) throw new OperationCanceledException();
            yield return AcquireInitialMap(bootstrap);
            if (cancelled?.Invoke() == true) throw new OperationCanceledException();
            MakeMaterials();
            // A cached coastline normally returns immediately. A missing/offline
            // optional source adds at most four seconds, then the usual later
            // coastal recovery remains available while driving.
            yield return FetchCoastalPatch(4,cancelled);
            if (cancelled?.Invoke() == true) throw new OperationCanceledException();
            bool initialCoastKnown=coastalPatch!=null;
            if(initialCoastKnown)coastalVisualSuppressionPending=false;
            yield return PrepareForwardCoverage(bootstrap);
            if (cancelled?.Invoke() == true) throw new OperationCanceledException();
            if(initialCoastKnown)
            {
                // Every initial terrain mesh was built against this exact coast;
                // no second full bed/physics rebuild is needed after drive entry.
                CoastalBedsReady=true;CoastalVisualPreviewPhase="water-publication";
                // Animated sea must have its fitted-road cutoff from its FIRST
                // visible frame, including low causeways and bridge landings.
                yield return RefreshRoadWaterMask();
                if (cancelled?.Invoke() == true) throw new OperationCanceledException();
                yield return BuildCoastalSurface(coastalPatch);
                if (cancelled?.Invoke() == true) throw new OperationCanceledException();
                CoastalVisualPreviewPhase="ready";
            }
            ready = true;
            report.initialReadySeconds = life.Elapsed.TotalSeconds;
            pumpRunning = true;
            StartCoroutine(DataPump());
            StartCoroutine(DistantBuildingLoop());
            StartCoroutine(MappedWaterLoop());
            StartCoroutine(RoadWaterClearanceLoop());
        }

        /// <summary>
        /// Supply the normal game's current player/view without transferring
        /// ownership. Call each frame, including after camera changes. This
        /// renderer never changes the supplied car's controls or transform.
        /// </summary>
        public void SetEmbeddedObserver(Vector3 position, Vector3 forward, Camera borrowedView)
        {
            if (!embedded) throw new InvalidOperationException("Embedded world required");
            observer = position;
            var heading = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (heading.sqrMagnitude > .01f) embeddedHeading = heading.normalized;
            view = borrowedView;
            if (ready && view && !embeddedDistantStarted)
            {
                embeddedDistantStarted = true;
                StartCoroutine(DistantTerrainLoop());
                StartCoroutine(CoastalLoop());
            }
        }

        public bool HasGeometryAt(Vector3 position)
        {
            return ready && chunks.TryGetValue(ChunkKey(position), out var root) && root && root.activeInHierarchy;
        }

        public bool TryGroundHeight(float x, float z, out float height)
        {
            height = 0;
            if (AreaAt(x, z) == null) return false;
            height = Ground(x, z);
            return true;
        }

        public bool TryNaturalHeight(float x, float z, out float height)
        {
            height = 0;
            if (AreaAt(x, z) == null) return false;
            height = RawHeight(x, z);
            return true;
        }
    }
}
