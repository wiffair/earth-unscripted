using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted
{
    // Optional observer of the NORMAL game. No generated/replacement geometry,
    // readiness assignment, road-height adjustment or collider modification.
    public sealed class LegacyJunctionVisualQa : MonoBehaviour
    {
        const string Flag = "-earthUnscriptedLegacyJunctionQa";
        const string PrivatePrefix = "/private/tmp/rr019-legacy-junction-";
        static bool requested, ownsOutput;
        static string output, caseName, phase, baseline, setupError, focusMethod, regionFile, regionFileSHA, regionCacheFile;
        static string setupStatus = "ISOLATION_REFUSED"; static int setupExit = 1;
        static Vector3 seedFocus;
        static bool seedFound;
        static readonly string[] CaseNames = { "oldpant", "hazelwood", "tir", "celynen", "a472-photo", "a472-west" };
        // Display groups only: geometry/focus is resolved from the actual loaded
        // source segments. These IDs never grant a road/collision/height result.
        static readonly string[][] BridgeWays = { new[] { "66367445", "66367442" }, new[] { "66367448" }, new[] { "90846106" } };
        static readonly int[] ExpectedCrossings = { 6, 2, 2 }, FirstMapPoint = { 1, 7, 9 };
        const float LowerRoadCameraHeightMetres = 1.3f;
        static readonly string[][] NodeIds = { new[] { "406353620" }, new[] { "745721225" }, new[] { "745715368", "745715323" } };

        [Serializable] public sealed class Pose { public string caption; public string name; public Vector3 eye, target; public float fieldOfView = 55; }
        [Serializable] public sealed class Poses { public string region, caseName, focusMethod; public Vector3 focus; public Pose[] views; }
        [Serializable] public sealed class Hit
        {
            public string name, category, entityId, colliderType, meshEntityId; public int layer, triangle; public Vector3 worldPoint, localPoint, worldNormal;
            public float distance, deltaToGroundEvaluator; public bool underActualGeoWorld, groundAsphaltComparisonApplicable;
        }
        [Serializable] public sealed class Sample
        {
            public Vector3 localPoint; public bool chunkPresent; public string focusNode, nearestRoad, nearestName, error; public int meshHitCount, omittedMeshHits;
            public float sharedEvaluator, layerEvaluator, rawHeight, terrainHeight, meshTerrainHeight;
            public Hit vehicleMaskTopHit; public Hit[] actualGeoMeshHits;
        }
        [Serializable] public sealed class Shot
        {
            public string name, file, sha256; public int frame, width, height, actualViewRenders;
            public Vector3 cameraWorldPosition; public Quaternion cameraWorldRotation; public float fieldOfView;
        }
        [Serializable] public sealed class Report
        {
            public string status, error, caseName, phase, buildGUID, regionId, regionName, source, attribution, focusMethod;
            public string scope = "Normal GameSession/GeoWorld observation only; missing views/samples are reports, not release gates. No driving or whole-world acceptance.";
            public string profilePath, regionCachePath, updateCachePath, bootStatus, regionInputFile, regionInputSHA256, privateRegionFile;
            public double latitude, longitude; public float datum; public float[] geoLocalToWorld;
            public bool bootReady, driveCameraRendered, sourceNodeMatched, sourceCrossingMatched, baselinePosesUsed;
            public int loadedChunks, activeMeshColliders; public Vector3 focus;
            public Shot[] shots; public Sample[] samples; public string[] notes; public Crossing[] sourceCrossings; public UnderpassGroup[] underpassGroups; public string[] viaductOverviewSourceIds; public string viaductOverviewScope;
        }
        readonly Report report = new Report();
        readonly List<Shot> shots = new List<Shot>();
        readonly List<string> notes = new List<string>();
        GameSession session; GeoWorld world; Camera camera; int viewRenders; bool finished;

        static string Arg(string[] args, string name, string fallback = null)
        { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        static string Hash(string path)
        { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, Flag) < 0) return;
            requested = true;
            // Refusal must also remain isolated, before ANY normal profile load.
            PlayerProfile.StorageOverride = Path.Combine(Application.temporaryCachePath, "refused-legacy-junction-" + Guid.NewGuid().ToString("N") + ".json");
            RegionCache.OverrideFolder = PlayerProfile.StorageOverride + ".regions";
            CountryUpdateService.OverrideFolder = PlayerProfile.StorageOverride + ".updates";
            CountryUpdateService.AllowVerification = false;
            try
            {
                if (Application.isEditor || Application.companyName != "EarthUnscripted Verification" || Application.productName != "LoadingCameraQA" || Application.identifier != "com.earthunscripted.qa.loadingcamera")
                    throw new InvalidOperationException("Use the existing separate LoadingCameraQA native product");
                if (new[] { "-earthUnscriptedCapture", "-earthUnscriptedLoadingCameraProof", "-earthUnscriptedLive", "-earthUnscriptedTown", "-earthUnscriptedGarage", "-earthUnscriptedRoadGeometryProof", "-earthUnscriptedSingleSeatPhysicalProof", "-earthUnscriptedDee", "-earthUnscriptedCircuit" }.Any(flag => Array.IndexOf(args, flag) >= 0))
                    throw new InvalidOperationException("Do not combine competing capture, live-provider or alternate-world flags");
                output = Path.GetFullPath(Arg(args, Flag) ?? "");
                if (!output.StartsWith(PrivatePrefix, StringComparison.Ordinal) || Directory.Exists(output)) throw new IOException("Fresh private junction QA directory required");
                caseName = Arg(args, "-earthUnscriptedJunctionCase", "oldpant"); phase = Arg(args, "-earthUnscriptedJunctionPhase", "inspection");
                int index = Array.IndexOf(CaseNames, caseName); if (index < 0) throw new ArgumentException("Case: oldpant, hazelwood, tir, celynen, a472-photo or a472-west");
                baseline = Arg(args, "-earthUnscriptedJunctionViews");
                if(index>=NodeIds.Length&&baseline!=null)throw new ArgumentException("Bridge views must use actual lower-road surface +1.3m; no external pose override");
                if (baseline != null && (!Path.GetFullPath(baseline).StartsWith(PrivatePrefix, StringComparison.Ordinal) || new FileInfo(baseline).Length > 65536)) throw new IOException("Private small baseline pose file required");
                Directory.CreateDirectory(output); ownsOutput = true;
                PlayerProfile.StorageOverride = Path.Combine(output, "profile.json"); RegionCache.OverrideFolder = Path.Combine(output, "regions"); CountryUpdateService.OverrideFolder = Path.Combine(output, "updates");
                // Copy the supplied existing normal-game region byte-for-byte.
                // No source coordinates/heights, road widths or bridge data are rewritten.
                regionFile = Path.GetFullPath(Arg(args, "-earthUnscriptedJunctionRegion") ?? "");
                if (!File.Exists(regionFile) || new FileInfo(regionFile).Length > 32 * 1024 * 1024) throw new IOException("Explicit existing GeoRegion JSON required");
                var supplied = JsonUtility.FromJson<GeoRegion>(File.ReadAllText(regionFile));
                if (supplied == null || supplied.circuit || !RegionCache.Safe(supplied.id)) throw new IOException("Normal saved road region with safe download identifier required");
                regionFileSHA = Hash(regionFile); regionCacheFile = RegionCache.PathFor(supplied.id);
                Directory.CreateDirectory(Path.GetDirectoryName(regionCacheFile)); File.Copy(regionFile, regionCacheFile, false);
                if (Hash(regionCacheFile) != regionFileSHA) throw new IOException("Private region copy mismatch");
                var preview = GeoRegion.Load(supplied.id);
                if (preview == null) { setupError = "Supplied normal region unavailable"; setupStatus = "REGION_NOT_AVAILABLE"; setupExit = 0; GameSession.SuppressAutomaticLaunch = true; return; }
                seedFound = TryFocus(preview, index, out seedFocus, out focusMethod);
                var profile = new PlayerProfile { region = supplied.id, startRegion = supplied.id, customStart = seedFound, startX = seedFocus.x, startZ = seedFocus.z, regionTimeZone = supplied.timeZone, detailedScenery = true, quality = 2, volume = 0, rumble = 0, wheelEnabled = false, wheelForceEnabled = false, weatherMode = 0, clockMode = 0, fixedHour = 14 };
                File.WriteAllText(PlayerProfile.StorageOverride, JsonUtility.ToJson(profile, true));
                Application.runInBackground = true;
                // Leave normal GameSession.SceneReady -> Launch -> Awake intact.
            }
            catch (Exception e) { setupError = e.ToString(); GameSession.SuppressAutomaticLaunch = true; }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (!requested || FindAnyObjectByType<LegacyJunctionVisualQa>()) return;
            new GameObject("Private normal-game junction observer").AddComponent<LegacyJunctionVisualQa>();
        }
        static bool TryFocus(GeoRegion region, int index, out Vector3 point, out string method)
        {
            if(index < NodeIds.Length)return TryNodeFocus(region, NodeIds[index][0], out point, out method);
            var crossings=FindCrossings(region,index-NodeIds.Length);
            point=crossings.Count==0?Vector3.zero:crossings[0].sourceCrossing;
            method=crossings.Count==0?"requested-source-segment-intersection-missing":
                "actual-loaded-source-segment-intersections/"+caseName+"/count="+crossings.Count;
            return crossings.Count>0;
        }
        static bool TryNodeFocus(GeoRegion region, string node, out Vector3 point, out string method)
        {
            var matches = new List<Vector3>();
            foreach (var road in region.roads ?? Array.Empty<GeoRoad>())
            {
                if (road.bridge || road.tunnel || road.layer != 0 || road.points == null || road.points.Length == 0) continue;
                if (road.sourceNodeIds != null && road.sourceNodeIds.Length == road.points.Length)
                { for (int i = 0; i < road.sourceNodeIds.Length; i++) if (road.sourceNodeIds[i] == node) matches.Add(road.points[i]); }
                else { if (road.start == node) matches.Add(road.points[0]); if (road.end == node) matches.Add(road.points[road.points.Length - 1]); }
            }
            point = matches.Count == 0 ? Vector3.zero : matches[0];
            method = matches.Count == 0 ? "source-node-missing" : "actual-loaded-source-node/" + node + "/occurrences=" + matches.Count;
            var chosen = point;
            if (matches.Count > 1) method += "/maximum-XZ-spread-metres=" + matches.Max(p => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(chosen.x, chosen.z))).ToString("R", CultureInfo.InvariantCulture);
            return matches.Count > 0;
        }
        [Serializable] public sealed class Crossing
        {
            public int mapPoint, bridgeSegment, lowerSegment;
            public string caption, bridgeWay, bridgeRoadId, lowerRoadId;
            public double sourceX, sourceZ, bridgeT, lowerT;
            public float rawUpperY, rawLowerY, actualUpperRoadY, actualLowerRoadY;
            public Vector3 sourceCrossing, lowerDirection, plannedLowerEye;
            public bool lowerEyeReferenceBelowSlabAtCrossing;
            [NonSerialized] public GeoRoad Upper, Lower;
        }
        static string LocalNode(GeoRoad road,int index)
        {
            if(road.sourceNodeIds!=null&&road.sourceNodeIds.Length==road.points.Length)return road.sourceNodeIds[index];
            return index==0?road.start:index==road.points.Length-1?road.end:null;
        }
        static List<Crossing> FindCrossings(GeoRegion region,int group)
        {
            var rows=new List<Crossing>();
            foreach(string way in BridgeWays[group])
            foreach(var upper in region.roads.Where(r=>r.bridge&&!r.tunnel&&r.id!=null&&r.id.StartsWith(way+"-",StringComparison.Ordinal)).OrderBy(r=>r.id,StringComparer.Ordinal))
            for(int i=1;i<upper.points.Length;i++)
            foreach(var lower in region.roads.Where(r=>!r.bridge&&!r.tunnel&&r.layer==0).OrderBy(r=>r.id,StringComparer.Ordinal))
            for(int j=1;j<lower.points.Length;j++)
            {
                // Shared source-node continuations are joins, not independent
                // overpasses. No proximity-node fallback is used for these cases.
                var upperNodes=new[]{LocalNode(upper,i-1),LocalNode(upper,i)};
                var lowerNodes=new[]{LocalNode(lower,j-1),LocalNode(lower,j)};
                if(upperNodes.Any(n=>!string.IsNullOrEmpty(n)&&lowerNodes.Contains(n)))continue;
                var a=upper.points[i-1];var b=upper.points[i];var c=lower.points[j-1];var d=lower.points[j];
                double ux=(double)b.x-a.x,uz=(double)b.z-a.z,vx=(double)d.x-c.x,vz=(double)d.z-c.z;
                double dx=(double)c.x-a.x,dz=(double)c.z-a.z,denominator=ux*vz-uz*vx;
                if(denominator==0)continue;
                double t=(dx*vz-dz*vx)/denominator,u=(dx*uz-dz*ux)/denominator;
                if(t<0||t>1||u<0||u>1)continue;
                double x=a.x+t*ux,z=a.z+t*uz;
                var direction=new Vector3((float)vx,0,(float)vz).normalized;
                rows.Add(new Crossing{bridgeWay=way,bridgeRoadId=upper.id,lowerRoadId=lower.id,
                    bridgeSegment=i-1,lowerSegment=j-1,sourceX=x,sourceZ=z,bridgeT=t,lowerT=u,
                    rawUpperY=(float)(a.y+t*((double)b.y-a.y)),rawLowerY=(float)(c.y+u*((double)d.y-c.y)),
                    sourceCrossing=new Vector3((float)x,(float)(c.y+u*((double)d.y-c.y)),(float)z),
                    lowerDirection=direction,Upper=upper,Lower=lower});
            }
            bool mapCountMatches=rows.Count==ExpectedCrossings[group];
            for(int i=0;i<rows.Count;i++)
            {
                var row=rows[i];row.mapPoint=mapCountMatches?FirstMapPoint[group]+i:0;
                row.actualUpperRoadY=region.RoadSurfaceHeightForRoad(row.Upper,row.sourceCrossing.x,row.sourceCrossing.z);
                row.actualLowerRoadY=region.RoadSurfaceHeightForRoad(row.Lower,row.sourceCrossing.x,row.sourceCrossing.z);
                row.lowerEyeReferenceBelowSlabAtCrossing=row.actualLowerRoadY+LowerRoadCameraHeightMetres<row.actualUpperRoadY-.75f;
                row.caption="Point "+(row.mapPoint==0?"unmapped":row.mapPoint.ToString(CultureInfo.InvariantCulture))+": upper "+row.bridgeRoadId+" over lower "+row.lowerRoadId+
                    "; actual source XZ "+row.sourceX.ToString("R",CultureInfo.InvariantCulture)+", "+row.sourceZ.ToString("R",CultureInfo.InvariantCulture);
            }
            return rows;
        }
        Poses MakeBridgePoses(GeoRegion region,int group,List<Crossing> rows)
        {
            var views=new List<Pose>();
            float x0=rows.Min(r=>r.sourceCrossing.x),x1=rows.Max(r=>r.sourceCrossing.x),z0=rows.Min(r=>r.sourceCrossing.z),z1=rows.Max(r=>r.sourceCrossing.z);
            float span=Mathf.Max(35,Mathf.Max(x1-x0,z1-z0));
            var centre=new Vector3((x0+x1)*.5f,rows.Max(r=>Mathf.Max(r.actualUpperRoadY,r.actualLowerRoadY)),(z0+z1)*.5f);
            views.Add(new Pose{name="overview",caption=caseName+"; actual source crossings "+string.Join(",",rows.Select(r=>r.mapPoint.ToString(CultureInfo.InvariantCulture))),
                eye=centre+new Vector3(0,Mathf.Max(28,span*.85f),-span*.65f),target=centre,fieldOfView=65});
            foreach(var row in rows.Take(7))
            {
                var a=row.Lower.points[row.lowerSegment];var b=row.Lower.points[row.lowerSegment+1];
                float length=new Vector2(b.x-a.x,b.z-a.z).magnitude;
                // Stay ON the declared lower segment. Choose its longer approach
                // side; neither a nearest road nor a height-raised rescue view.
                bool fromStart=row.lowerT>=.5;
                float available=(float)(fromStart?row.lowerT:1-row.lowerT)*length;
                float setback=Mathf.Min(8,available*.8f);
                Vector3 eye=row.sourceCrossing+row.lowerDirection*(fromStart?-setback:setback);
                eye.y=region.RoadSurfaceHeightForRoad(row.Lower,eye.x,eye.z)+LowerRoadCameraHeightMetres;
                row.plannedLowerEye=eye;
                var target=row.sourceCrossing;target.y=row.actualLowerRoadY+LowerRoadCameraHeightMetres;
                views.Add(new Pose{name="under-road-"+(row.mapPoint==0?views.Count:row.mapPoint),caption=row.caption+"; lower surface +1.3m (may be obstructed by incorrect deck)",eye=eye,target=target,fieldOfView=65});
            }
            if (group == 0)
            {
                var wide = MakeViaductSidePose(region);
                if (wide != null) views.Add(wide);
                else notes.Add("Whole-viaduct source bounds unavailable; no replacement view invented.");
            }
            if(rows.Count!=ExpectedCrossings[group])notes.Add("Source group expected "+ExpectedCrossings[group]+" crossings, found "+rows.Count+"; map-point numbers withheld, no substitute intersection used.");
            if(rows.Count>7)notes.Add("More crossings than the existing eight-pose budget: remaining native views explicitly omitted.");
            notes.Add("Under-road labels describe the declared lower source road. Eye is its actual named surface +1.3m even when deck is too low; no clearance rescue/geometry/profile change is made.");
            return new Poses{region=region.id,caseName=caseName,focus=rows[0].sourceCrossing,focusMethod=report.focusMethod,views=views.ToArray()};
        }
        Pose MakeViaductSidePose(GeoRegion region)
        {
            // Source-derived framing only, not a reconstructed user-photo pose.
            // Include both complete bridgeways and up to58m of genuine connected
            // approach paths. No nearest-road/proximity stitching is permitted.
            const float approachMetres = 58;
            const int maximumRoadVisits = 512;
            var chains = region.roads.Where(road => road.bridge && road.id != null &&
                BridgeWays[0].Any(way => road.id.StartsWith(way + "-", StringComparison.Ordinal))).ToArray();
            var points = new List<Vector3>(); var sourceIds = new HashSet<string>();
            var pending = new Queue<(string node, float remaining)>();
            var bestRemaining = new Dictionary<string, float>();
            foreach (var road in chains)
            {
                points.AddRange(region.CopyEffectiveBridgePoints(road)); sourceIds.Add(road.id);
                foreach (string node in new[] { road.start, road.end })
                    if (!string.IsNullOrEmpty(node)) pending.Enqueue((node, approachMetres));
            }
            int visits = 0;
            while (pending.Count > 0 && visits < maximumRoadVisits)
            {
                var state = pending.Dequeue();
                if (bestRemaining.TryGetValue(state.node, out float previous) && previous >= state.remaining) continue;
                bestRemaining[state.node] = state.remaining;
                foreach (var road in region.roads.Where(road => !road.tunnel && road.points != null && road.points.Length >= 2 &&
                    (road.start == state.node || road.end == state.node)).OrderBy(road => road.id, StringComparer.Ordinal))
                {
                    if (++visits > maximumRoadVisits) break;
                    if (chains.Contains(road)) continue;
                    var path = road.bridge ? region.CopyEffectiveBridgePoints(road) : road.points;
                    bool reverse = road.end == state.node;
                    float remaining = state.remaining;
                    Vector3 prior = path[reverse ? path.Length - 1 : 0]; points.Add(prior); sourceIds.Add(road.id);
                    for (int i = 1; i < path.Length; i++)
                    {
                        Vector3 next = path[reverse ? path.Length - 1 - i : i];
                        float span = Vector2.Distance(new Vector2(prior.x, prior.z), new Vector2(next.x, next.z));
                        if (span > remaining)
                        {
                            points.Add(Vector3.Lerp(prior, next, remaining / span)); remaining = 0; break;
                        }
                        points.Add(next); remaining -= span; prior = next;
                    }
                    string farNode = reverse ? road.start : road.end;
                    if (remaining > 0 && !string.IsNullOrEmpty(farNode)) pending.Enqueue((farNode, remaining));
                }
            }
            if (points.Count < 2) return null;
            report.viaductOverviewSourceIds = sourceIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            report.viaductOverviewScope = "Both complete source bridgeways + endpoint-connected approaches up to58m; derived framing, not measured photo location. Road visit cap512; remaining queue=" + pending.Count;
            var min = points[0]; var max = min;
            foreach (var point in points) { min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
            Vector3 axis = Vector3.forward; float longest = 0;
            foreach (var a in points) foreach (var b in points)
            {
                var delta = b - a; delta.y = 0;
                if (delta.sqrMagnitude > longest) { longest = delta.sqrMagnitude; axis = delta.normalized; }
            }
            float extent = Mathf.Max(35, Mathf.Sqrt(longest));
            var centre = (min + max) * .5f; var side = Vector3.Cross(Vector3.up, axis);
            return new Pose { name = "viaduct-side-wide", caption = report.viaductOverviewScope,
                eye = centre + side * (extent * 1.1f) + Vector3.up * Mathf.Max(20, extent * .45f),
                target = centre, fieldOfView = 65 };
        }

        void Rendered(ScriptableRenderContext context, Camera actual) { if (actual == camera) viewRenders++; }
        IEnumerator Start() { RenderPipelineManager.endCameraRendering += Rendered; yield return Guard(Run()); }
        IEnumerator Guard(IEnumerator work)
        {
            try
            {
                while (true)
                {
                    bool more = false; object next = null; Exception error = null;
                    try { more = work.MoveNext(); if (more) next = work.Current; } catch (Exception e) { error = e; }
                    if (error != null) { Finish("OBSERVER_ERROR", error.ToString(), 1); yield break; }
                    if (!more) yield break;
                    if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
                    if (finished) yield break;
                }
            }
            finally { (work as IDisposable)?.Dispose(); }
        }
        static bool ValidPoses(Poses poses)
        {
            bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
            bool Point(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && Mathf.Abs(p.x) <= 20000 && Mathf.Abs(p.y) <= 20000 && Mathf.Abs(p.z) <= 20000;
            return poses != null && poses.views != null && poses.views.Length > 0 && poses.views.Length <= 8 &&
                poses.views.All(p => p != null && !string.IsNullOrWhiteSpace(p.name) && p.name.Length <= 64 && p.name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_') && Point(p.eye) && Point(p.target) && (p.eye - p.target).sqrMagnitude > .001f && Finite(p.fieldOfView) && p.fieldOfView >= 30 && p.fieldOfView <= 100) &&
                poses.views.Select(p => p.name).Distinct(StringComparer.Ordinal).Count() == poses.views.Length;
        }
        IEnumerator Run()
        {
            if (setupError != null) { Finish(setupStatus, setupError, setupExit); yield break; }
            report.caseName = caseName; report.phase = phase; report.buildGUID = Application.buildGUID;
            report.profilePath = PlayerProfile.StoragePath; report.regionCachePath = RegionCache.OverrideFolder; report.updateCachePath = CountryUpdateService.OverrideFolder; report.regionInputFile = regionFile; report.regionInputSHA256 = regionFileSHA; report.privateRegionFile = regionCacheFile;
            double deadline = Time.realtimeSinceStartupAsDouble + 300;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                session = FindAnyObjectByType<GameSession>();
                if (session && session.BootFailed) { Finish("NORMAL_BOOT_FAILED", session.BootStatus, 1); yield break; }
                if (session && session.BootReady && session.World && session.World.GeographicWorld && session.View) break;
                yield return null;
            }
            if (!session || !session.BootReady || !session.World.GeographicWorld) { Finish("NORMAL_WORLD_NOT_OBSERVED", "Normal boot/world unavailable in observer window", 0); yield break; }
            world = session.World.GeographicWorld; var region = world.Region; camera = session.View.GetComponent<Camera>();
            if (!camera) { Finish("NORMAL_CAMERA_NOT_OBSERVED", "Actual session View camera missing", 0); yield break; }
            Screen.SetResolution(1440, 900, FullScreenMode.Windowed);
            for (int i = 0; i < 12; i++) yield return new WaitForEndOfFrame();
            deadline = Time.realtimeSinceStartupAsDouble + 10;
            while ((!session.BootDriveCameraRenderConfirmed || viewRenders < 2) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            report.bootReady = session.BootReady; report.driveCameraRendered = session.BootDriveCameraRenderConfirmed && viewRenders >= 2; report.bootStatus = session.BootStatus;
            report.regionId = region.id; report.regionName = region.name; report.source = region.datasetSource; report.attribution = region.attribution; report.latitude = region.latitude; report.longitude = region.longitude; report.datum = region.datum;
            report.geoLocalToWorld = Enumerable.Range(0, 16).Select(i => world.transform.localToWorldMatrix[i]).ToArray();
            int caseIndex = Array.IndexOf(CaseNames, caseName); Vector3 focus; string resolution;
            bool crossingCase=caseIndex>=NodeIds.Length;
            bool focusFound=TryFocus(region, caseIndex, out focus, out resolution);
            report.sourceNodeMatched = !crossingCase&&focusFound;report.sourceCrossingMatched=crossingCase&&focusFound;
            if (!focusFound) { notes.Add("Requested source node/intersection absent from actual loaded region; no geographic hint or substitute junction used."); yield return Capture("normal-menu"); Finish("REQUESTED_JUNCTION_NOT_FOUND", resolution, 0); yield break; }
            var crossingRows=crossingCase?FindCrossings(region,caseIndex-NodeIds.Length):null;
            focus.y = crossingCase?crossingRows[0].actualLowerRoadY:region.RoadSurfaceHeight(focus.x, focus.z); report.focus = focus; report.focusMethod = resolution;
            yield return Capture("normal-menu");
            var poses = crossingCase?MakeBridgePoses(region,caseIndex-NodeIds.Length,crossingRows):MakePoses(region, caseName, focus, resolution);
            if(crossingCase)report.sourceCrossings=crossingRows.ToArray();
            if (baseline != null)
            {
                var prior = JsonUtility.FromJson<Poses>(File.ReadAllText(baseline));
                if (ValidPoses(prior) && prior.region == region.id && prior.caseName == caseName) { poses = prior; report.baselinePosesUsed = true; }
                else notes.Add("Baseline pose region/case mismatch; freshly resolved views are not exact before/after comparison.");
            }
            File.WriteAllText(Path.Combine(output, "views.json"), JsonUtility.ToJson(poses, true));
            bool viewEnabled = session.View.enabled; var ui = session.GetComponent<DrivingUI>(); bool uiEnabled = ui && ui.enabled; float oldScale = Time.timeScale;
            Vector3 oldPosition = camera.transform.position; Quaternion oldRotation = camera.transform.rotation; float oldFov = camera.fieldOfView;
            try
            {
                Time.timeScale = 0; session.View.enabled = false; if (ui) ui.enabled = false;
                foreach (var pose in poses.views)
                {
                    camera.transform.position = world.transform.TransformPoint(pose.eye);
                    Vector3 target = world.transform.TransformPoint(pose.target); camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, world.transform.up); camera.fieldOfView = pose.fieldOfView;
                    for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
                    yield return Capture(pose.name);
                }
                Physics.SyncTransforms(); report.loadedChunks = world.LoadedChunks;
                var colliders = world.GetComponentsInChildren<MeshCollider>(false).Where(c => c.enabled && !c.isTrigger && c.sharedMesh).ToArray(); report.activeMeshColliders = colliders.Length;
                var samples = new List<Sample>();
                for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++) samples.Add(SampleAt(focus + new Vector3(x * 3, 0, z * 3), colliders, crossingCase?"source-segment-intersection":NodeIds[caseIndex][0]));
                if (!crossingCase&&NodeIds[caseIndex].Length > 1)
                {
                    if (TryNodeFocus(region, NodeIds[caseIndex][1], out var second, out var secondMethod))
                        for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++) samples.Add(SampleAt(second + new Vector3(x * 3, 0, z * 3), colliders, NodeIds[caseIndex][1]));
                    else notes.Add("Second connector endpoint not found; its sample group is missing, not inferred.");
                }
                report.samples = samples.ToArray();
                if (crossingCase)
                {
                    var groups = new List<UnderpassGroup>();
                    foreach (var crossing in crossingRows)
                    {
                        var group = new UnderpassGroup();
                        foreach (var step in ObserveUnderpass(crossing, group)) yield return null;
                        groups.Add(group);
                    }
                    report.underpassGroups = groups.ToArray();
                }
            }
            finally { camera.transform.SetPositionAndRotation(oldPosition, oldRotation); camera.fieldOfView = oldFov; session.View.enabled = viewEnabled; if (ui) ui.enabled = uiEnabled; Time.timeScale = oldScale; }
            if (!report.sourceNodeMatched&&!report.sourceCrossingMatched) notes.Add("Missing/ambiguous source binding is reported, not a release gate.");
            notes.Add("Collider names classify batches only, not source ownership. Deltas are ground-reference measurements; bridge/terrain/verge deltas are not asphalt failure checks. Bridge geometry is unchanged.");
            Finish(report.driveCameraRendered ? "OBSERVED" : "OBSERVED_WITH_MISSING_DRIVE_RENDER", null, 0);
        }
        Poses MakePoses(GeoRegion region, string name, Vector3 focus, string method)
        {
            var directions = new List<Vector3>();
            foreach (var road in (region.roads ?? Array.Empty<GeoRoad>()).Where(r => !r.bridge && !r.tunnel && r.layer == 0 && r.points != null).OrderBy(r => r.id, StringComparer.Ordinal))
                for (int i = 0; i < road.points.Length; i++)
                {
                    Vector3 p = road.points[i]; if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(focus.x, focus.z)) > 1) continue;
                    foreach (int j in new[] { i - 1, i + 1 }) if (j >= 0 && j < road.points.Length)
                    { Vector3 d = road.points[j] - p; d.y = 0; if (d.sqrMagnitude > .01f) { d.Normalize(); if (!directions.Any(old => Vector3.Dot(old, d) > .98f)) directions.Add(d); } }
                }
            var views = new List<Pose> { new Pose { name = "overview", eye = focus + new Vector3(0, 22, -20), target = focus + Vector3.up } };
            for (int i = 0; i < Math.Min(3, directions.Count); i++) views.Add(new Pose { name = "approach-" + i, eye = focus + directions[i] * 18 + Vector3.up * 3.2f, target = focus + Vector3.up * .7f });
            if (directions.Count == 0) { notes.Add("Approach tangent missing; overview only."); }
            return new Poses { region = region.id, caseName = name, focusMethod = method, focus = focus, views = views.ToArray() };
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try { string path = Path.Combine(output, name + ".png"); File.WriteAllBytes(path, texture.EncodeToPNG()); shots.Add(new Shot { name = name, file = path, sha256 = Hash(path), frame = Time.frameCount, width = texture.width, height = texture.height, actualViewRenders = viewRenders, cameraWorldPosition = camera.transform.position, cameraWorldRotation = camera.transform.rotation, fieldOfView = camera.fieldOfView }); }
            finally { Destroy(texture); }
        }
        const int UnderpassVehicleMask = ~(1 << 8); // Same actor exclusion as existing road diagnostics.
        const int MaximumUnderpassSamples = 10000, MaximumUnderpassHits = 8;
        const float UnderpassCadenceMetres = 1, UnderpassUpRangeMetres = 200;
        const float PavementRayAboveMetres = 2, PavementRayRangeMetres = 10;
        const float UpOriginOffsetMetres = .02f, PierObservationHeightMetres = 5.03f;

        [Serializable] public sealed class PierObservation
        {
            public string entityId, name; public int layer;
            public Vector3 centreWorld, sizeLocal, worldBoundsMin, worldBoundsMax;
            public float[] localToWorld;
        }
        [Serializable] public sealed class UnderpassSample
        {
            public Vector3 localQuery;
            public float alongMetres, acrossMetres, namedLowerEvaluator, rawHeight;
            public bool exactPavementCandidate, ambiguousPavementHeights, upperSourceFootprintProxy;
            public string pavementAssociation = "Nearest actual road/junction batch candidate to named evaluator; not per-triangle source ownership";
            public string error;
            public int downHitCount, omittedDownHits, upHitCount, omittedUpHits;
            public Hit[] downHits, upHits;
            public Hit selectedPavement, nearestActualOverhead, nearestActualBridgeUnderside;
            public bool pierVolumeUsesActualPavement;
            public bool actualClearanceObserved;
            public float actualOverheadClearanceMetres, actualBridgeUndersideClearanceMetres;
            public PierObservation[] actualPierBoxes;
        }
        [Serializable] public sealed class UnderpassGroup
        {
            public int mapPoint, layerMask = UnderpassVehicleMask, sampleCount, pavementMisses, ambiguousSamples, overheadMisses, pierTileIntersections;
            public string upperRoad, lowerRoad;
            public bool truncated, anyActualOverhead;
            public float requestedLowerWidthMetres, requestedAlongStartMetres, requestedAlongEndMetres;
            public float minimumActualOverheadMetres;
            public string qualification = "Finite approximately1m source-footprint grid, actual GeoWorld collider observations; group minimum uses only unambiguous pavement candidates, not exhaustive clearance or source-sheet ownership proof. Pier overlap envelope5.03m is an observation volume, not an acceptance gate.";
            public UnderpassSample[] samples;
        }

        IEnumerable<int> ObserveUnderpass(Crossing crossing, UnderpassGroup group)
        {
            var region = world.Region;
            group.mapPoint = crossing.mapPoint; group.upperRoad = crossing.bridgeRoadId; group.lowerRoad = crossing.lowerRoadId;
            float lowerWidth = region.MaximumRoadWidth(crossing.Lower), upperRadius = region.MaximumRoadWidth(crossing.Upper) / 1.2f;
            group.requestedLowerWidthMetres = lowerWidth;
            var direction = crossing.lowerDirection; var right = Vector3.Cross(Vector3.up, direction);
            var upperA = crossing.Upper.points[crossing.bridgeSegment]; var upperB = crossing.Upper.points[crossing.bridgeSegment + 1];
            var lowerA = crossing.Lower.points[crossing.lowerSegment]; var lowerB = crossing.Lower.points[crossing.lowerSegment + 1];
            float lowerStart = Vector3.Dot(lowerA - crossing.sourceCrossing, direction), lowerEnd = Vector3.Dot(lowerB - crossing.sourceCrossing, direction);
            float upperStart = Vector3.Dot(upperA - crossing.sourceCrossing, direction), upperEnd = Vector3.Dot(upperB - crossing.sourceCrossing, direction);
            float padding = upperRadius + lowerWidth * .5f;
            float start = Mathf.Max(Mathf.Min(lowerStart, lowerEnd), Mathf.Min(upperStart, upperEnd) - padding);
            float end = Mathf.Min(Mathf.Max(lowerStart, lowerEnd), Mathf.Max(upperStart, upperEnd) + padding);
            group.requestedAlongStartMetres = start; group.requestedAlongEndMetres = end;
            var samples = new List<UnderpassSample>();
            if (!float.IsNaN(start) && !float.IsNaN(end) && !float.IsInfinity(start) && !float.IsInfinity(end) &&
                lowerWidth > 0 && lowerWidth <= 100 && end >= start)
            {
                int acrossSteps = Mathf.Max(1, Mathf.CeilToInt(lowerWidth / UnderpassCadenceMetres));
                int alongSteps = Mathf.Max(1, Mathf.CeilToInt((end - start) / UnderpassCadenceMetres));
                for (int along = 0; along <= alongSteps; along++)
                {
                    for (int across = 0; across <= acrossSteps; across++)
                    {
                        float s = Mathf.Lerp(start, end, along / (float)alongSteps);
                        float lateral = Mathf.Lerp(-lowerWidth * .5f, lowerWidth * .5f, across / (float)acrossSteps);
                        var query = crossing.sourceCrossing + direction * s + right * lateral;
                        var delta = new Vector2(upperB.x - upperA.x, upperB.z - upperA.z);
                        var fromA = new Vector2(query.x - upperA.x, query.z - upperA.z);
                        float t = Mathf.Clamp01(Vector2.Dot(fromA, delta) / Mathf.Max(.000001f, delta.sqrMagnitude));
                        if ((fromA - delta * t).magnitude > upperRadius) continue;
                        if (samples.Count >= MaximumUnderpassSamples) { group.truncated = true; break; }
                        var sample = UnderpassAt(query, s, lateral, crossing.Lower, world.transform.rotation * Quaternion.LookRotation(direction, Vector3.up));
                        samples.Add(sample);
                        if (!sample.exactPavementCandidate) group.pavementMisses++;
                        if (sample.ambiguousPavementHeights) group.ambiguousSamples++;
                        if (sample.actualPierBoxes != null && sample.actualPierBoxes.Length > 0) group.pierTileIntersections++;
                        if (sample.actualClearanceObserved && !sample.ambiguousPavementHeights)
                        {
                            if (!group.anyActualOverhead || sample.actualOverheadClearanceMetres < group.minimumActualOverheadMetres)
                                group.minimumActualOverheadMetres = sample.actualOverheadClearanceMetres;
                            group.anyActualOverhead = true;
                        }
                        if (!sample.actualClearanceObserved) group.overheadMisses++;
                        if (samples.Count % 32 == 0) yield return 0;
                    }
                    if (group.truncated) break;
                }
            }
            else group.qualification += " Invalid or unsupported finite source sampling bounds; no substitute grid.";
            group.samples = samples.ToArray(); group.sampleCount = samples.Count;
        }

        UnderpassSample UnderpassAt(Vector3 query, float along, float across, GeoRoad lower, Quaternion tileRotation)
        {
            var sample = new UnderpassSample { localQuery = query, alongMetres = along, acrossMetres = across, upperSourceFootprintProxy = true };
            try
            {
                sample.namedLowerEvaluator = world.Region.RoadSurfaceHeightForRoad(lower, query.x, query.z);
                sample.rawHeight = world.Region.RawHeight(query.x, query.z);
                query.y = sample.namedLowerEvaluator;
                var up = world.transform.up;
                var downOrigin = world.transform.TransformPoint(query + Vector3.up * PavementRayAboveMetres);
                var down = Physics.RaycastAll(downOrigin, -up, PavementRayRangeMetres, UnderpassVehicleMask, QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.collider && hit.collider.transform.IsChildOf(world.transform)).OrderBy(hit => hit.distance).ToArray();
                sample.downHitCount = down.Length; sample.omittedDownHits = Math.Max(0, down.Length - MaximumUnderpassHits);
                sample.downHits = down.Take(MaximumUnderpassHits).Select(hit => HitRow(hit, sample.namedLowerEvaluator)).ToArray();
                // Promoted lower approaches render as actual Bridge deck batches.
                // Admit that category only from the region's existing effective-
                // deck decision; nearest named-height selection below is unchanged.
                bool effectiveLowerDeck = world.Region.IsEffectiveBridgeDeck(lower);
                var pavement = down.Where(hit => hit.collider.name == "Mapped road" || hit.collider.name == "Junction apron" ||
                    (effectiveLowerDeck && hit.collider.name == "Bridge deck"))
                    .OrderBy(hit => Mathf.Abs(world.transform.InverseTransformPoint(hit.point).y - sample.namedLowerEvaluator)).ToArray();
                sample.exactPavementCandidate = pavement.Length > 0;
                if (pavement.Length == 0)
                {
                    // Still report a pier against the cached named reference,
                    // explicitly distinguished from actual-pavement anchoring.
                    sample.actualPierBoxes = ObservePierBoxes(world.transform.TransformPoint(query), tileRotation);
                    return sample;
                }
                var selected = pavement[0]; sample.selectedPavement = HitRow(selected, sample.namedLowerEvaluator);
                float pavementY = sample.selectedPavement.localPoint.y;
                sample.ambiguousPavementHeights = pavement.Any(hit => Mathf.Abs(world.transform.InverseTransformPoint(hit.point).y - pavementY) > .005f);
                var overhead = Physics.RaycastAll(selected.point + up * UpOriginOffsetMetres, up, UnderpassUpRangeMetres,
                    UnderpassVehicleMask, QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.collider && hit.collider.transform.IsChildOf(world.transform)).OrderBy(hit => hit.distance).ToArray();
                sample.upHitCount = overhead.Length; sample.omittedUpHits = Math.Max(0, overhead.Length - MaximumUnderpassHits);
                sample.upHits = overhead.Take(MaximumUnderpassHits).Select(hit => HitRow(hit, sample.namedLowerEvaluator)).ToArray();
                if (overhead.Length > 0)
                {
                    sample.nearestActualOverhead = HitRow(overhead[0], sample.namedLowerEvaluator);
                    sample.actualClearanceObserved = true;
                    sample.actualOverheadClearanceMetres = overhead[0].distance + UpOriginOffsetMetres;
                }
                var bridge = overhead.FirstOrDefault(hit => hit.collider.name == "Bridge deck" || hit.collider.name == "Bridge concrete structure");
                if (bridge.collider)
                {
                    sample.nearestActualBridgeUnderside = HitRow(bridge, sample.namedLowerEvaluator);
                    sample.actualBridgeUndersideClearanceMetres = bridge.distance + UpOriginOffsetMetres;
                }
                sample.actualPierBoxes = ObservePierBoxes(selected.point, tileRotation);
                sample.pierVolumeUsesActualPavement = true;
            }
            catch (Exception error) { sample.error = error.ToString(); }
            return sample;
        }

        PierObservation[] ObservePierBoxes(Vector3 pavementWorld, Quaternion tileRotation)
        {
                // Whole approximately1m lower-road-oriented tile, not centre ray.
                var half = new Vector3(.5f, PierObservationHeightMetres * .5f, .5f);
                var scale = world.transform.lossyScale;
                half = Vector3.Scale(half, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                var tileCentre = pavementWorld + world.transform.up * (UpOriginOffsetMetres + half.y);
                return Physics.OverlapBox(tileCentre, half, tileRotation, UnderpassVehicleMask, QueryTriggerInteraction.Ignore)
                    .OfType<BoxCollider>().Where(box => box && box.name == "Bridge pier" && box.transform.IsChildOf(world.transform))
                    .OrderBy(box => box.GetEntityId().ToString(), StringComparer.Ordinal).Select(box => new PierObservation {
                        entityId = box.GetEntityId().ToString(), name = box.name, layer = box.gameObject.layer,
                        centreWorld = box.transform.TransformPoint(box.center), sizeLocal = box.size,
                        worldBoundsMin = box.bounds.min, worldBoundsMax = box.bounds.max,
                        localToWorld = Enumerable.Range(0, 16).Select(i => box.transform.localToWorldMatrix[i]).ToArray()
                    }).ToArray();
        }

        Sample SampleAt(Vector3 p, MeshCollider[] colliders, string node)
        {
            var s = new Sample { localPoint = p, focusNode = node, chunkPresent = world.HasSurface(p) }; var region = world.Region;
            try
            {
                s.sharedEvaluator = region.RoadSurfaceHeight(p.x, p.z); s.layerEvaluator = region.RoadSurfaceHeightOnLayer(p.x, p.z, 0); s.rawHeight = region.RawHeight(p.x, p.z); s.terrainHeight = region.TerrainHeight(p.x, p.z); s.meshTerrainHeight = world.MeshSurfaceHeight(p.x, p.z);
                var nearest = region.ProjectRoad(p, false, false); s.nearestRoad = nearest.Road?.id; s.nearestName = nearest.Road?.name;
                Vector3 origin = world.transform.TransformPoint(new Vector3(p.x, Mathf.Max(s.sharedEvaluator, s.terrainHeight) + 20, p.z)); var ray = new Ray(origin, -world.transform.up);
                if (Physics.Raycast(ray, out var top, 80, ~(1 << 8), QueryTriggerInteraction.Ignore)) s.vehicleMaskTopHit = HitRow(top, s.sharedEvaluator);
                var hits = new List<Hit>(); foreach (var collider in colliders)
                { if (collider.bounds.IntersectRay(ray) && collider.Raycast(ray, out var hit, 80)) hits.Add(HitRow(hit, s.sharedEvaluator)); }
                s.meshHitCount = hits.Count; s.omittedMeshHits = Math.Max(0, hits.Count - 32); s.actualGeoMeshHits = hits.OrderBy(h => h.distance).Take(32).ToArray();
            }
            catch (Exception e) { s.error = e.Message; }
            return s;
        }
        Hit HitRow(RaycastHit hit, float evaluator)
        {
            string name = hit.collider.name; string category = name.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0 ? "bridge-or-bridge-structure" : name == "Terrain" ? "terrain" : name.IndexOf("embankment", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("verge", StringComparison.OrdinalIgnoreCase) >= 0 ? "embankment-or-verge" : name.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("asphalt", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Junction", StringComparison.OrdinalIgnoreCase) >= 0 ? "road-or-junction-batch" : "other-geometry";
            Vector3 local = world.transform.InverseTransformPoint(hit.point);
            return new Hit { name = name, category = category, entityId = hit.collider.GetEntityId().ToString(), colliderType = hit.collider.GetType().Name, meshEntityId = hit.collider is MeshCollider meshCollider && meshCollider.sharedMesh ? meshCollider.sharedMesh.GetEntityId().ToString() : null, layer = hit.collider.gameObject.layer, triangle = hit.triangleIndex, worldPoint = hit.point, localPoint = local, worldNormal = hit.normal, distance = hit.distance, deltaToGroundEvaluator = local.y - evaluator, groundAsphaltComparisonApplicable = category == "road-or-junction-batch", underActualGeoWorld = hit.collider.transform.IsChildOf(world.transform) };
        }
        void Finish(string status, string error, int exit)
        {
            if (finished) return; finished = true; report.status = status; report.error = error; report.shots = shots.ToArray(); report.notes = notes.ToArray();
            if (ownsOutput && output != null && Directory.Exists(output)) File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, false));
            Debug.Log("Legacy junction QA " + status + " " + output); Application.Quit(exit);
        }
        void OnDestroy() { RenderPipelineManager.endCameraRendering -= Rendered; }
    }
}
