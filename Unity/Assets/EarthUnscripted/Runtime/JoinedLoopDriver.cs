using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using EarthUnscripted.Core800;

namespace EarthUnscripted.RouteProof3
{
    /// <summary>
    /// Explicit QA driver for one already-placed production car on a source-connected
    /// loop. It writes ordinary NetworkDriven pedal/steering demand before the real
    /// VehicleController.FixedUpdate; it never integrates physics, changes a pose,
    /// edits geometry or sets velocity. A parent proof owns staging, camera, isolated
    /// files and lifecycle. Merely importing this component starts nothing.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class JoinedLoopDriver : MonoBehaviour
    {
        public const string RequiredFlag = "-earthUnscriptedCore800RouteProof3";
        private const float DesiredSpeedMetresPerSecond = 5;
        private const float MaximumLateralAccelerationMetresPerSecondSquared = 1.5f;
        private const float MaximumThrottle = 0.55f;
        // QA driver demand only, not engine/tyre tuning. Error is m/s; the P
        // gain converts it to dimensionless throttle, and I integrates error
        // (metres) with a bounded demand. The modest base counters rolling loss;
        // anti-windup and brake hysteresis avoid pedal chatter on real grades.
        private const float ProportionalDemandPerMetrePerSecond = 0.2f;
        private const float IntegralDemandPerMetre = 0.04f;
        private const float MaximumIntegralDemand = 0.35f;
        private const float BaseRollingDemand = 0.08f;
        private const float PedalHysteresisMetresPerSecond = 0.08f;
        private const float MaximumLateralErrorMetres = 0.9f;
        private const float MaximumDamage = 0.05f;
        private const double NamedHeightToleranceMetres = 0.0002;
        private const float ContactHalfSpanMetres = 3;
        private const float MinimumDrivingSeconds = 60;
        private const float MinimumPhysicalTravelMetres = 800;
        private const float MaximumTravelSeconds = 300;
        private const float MeasuredHoldSeconds = 10.03f;
        private const float MaximumHoldExcursionMetres = 0.05f;
        private const int MaximumProjectionSegments = 256;
        /// <summary>
        /// Actual fixed-step evidence. Units: seconds, metres and m/s. Inputs remain
        /// dimensionless [-1,1] steer and [0,1] pedals; no negative-throttle reverse.
        /// A parent writes every record unfiltered and binds its final byte digest.
        /// </summary>
        [Serializable]
        public sealed class Trace
        {
            public string vehicle, phase;
            public string[] roadIds, contactPackets;
            public double routeArcMetres;
            public float simulationSeconds, speedMetresPerSecond, travelMetres, lateralErrorMetres;
            public float throttle, brake, steer, damage;
            public bool parkingApplied, parkingHolding, reverse;
            public Vector3 position, velocity, forward;
            public bool[] grounded;
            public Vector3[] contactPoints, contactNormals, rayPoints, rayNormals;
            public int[] rayTriangles;
            public bool provedAdjacentCorner;
            public long projectionSegmentOrdinal = -1;
            public SweptSourceCorner32.Evidence adjacentCornerCrossing;
        }

        public string Phase { get; private set; } = "not-initialized";
        public string Failure { get; private set; }
        public bool Complete { get; private set; }
        public double ProgressArcMetres { get; private set; }
        public float ActualTravelMetres { get; private set; }
        public float DriveSeconds { get; private set; }
        public float MaximumHoldExcursion { get; private set; }

        public string[] CopyCrossedSourceNodes() => crossedNodes.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        private PreparedWorldMeshPlan800 plan;
        private JoinedLoopRoute route;
        private JoinedLoopRoute.Sample[] samples;
        private JoinedLoopRoute.Junction[] junctions;
        private VehicleController vehicle;
        private DriverInput input;
        private Action<Trace> retainEveryTrace;
        private CancellationToken cancellation;
        private string identity;
        private HashSet<string> knownRoadPackets;
        private readonly HashSet<string> crossedNodes = new HashSet<string>(StringComparer.Ordinal);
        private double startArc, endArc;
        private float driveBegan, phaseBegan, holdBegan, integralSpeedError;
        private int holdSamples;
        private Vector3 previousPosition, holdAnchor;
        private long acceptedProjectionSegment = -1;
        private SweptSourceCorner32.Evidence cornerCrossing;
        private SweptSourceCorner32.Join[] sourceCornerJoins;
        /// <summary>
        /// Main-thread initialization after actual factory/tuning/rest setup. The
        /// caller chooses an existing route point and places the car ONCE before
        /// observation. This method verifies that pose instead of moving it.
        /// Fails without touching physics if identities, stock role or heading differ.
        /// Parent owns this component and must dispose it on cancellation/supersession.
        /// </summary>
        public void Initialize(PreparedWorldMeshPlan800 finalizedPlan, JoinedLoopRoute candidate, VehicleController actualVehicle, DriverInput actualInput, string vehicleIdentity, double initialArcMetres, Action<Trace> retainTrace, CancellationToken token = default)
        {
            if (plan != null || Array.IndexOf(Environment.GetCommandLineArgs(), RequiredFlag) < 0 || Application.identifier != "com.earthunscripted.qa.core800route3")
                throw new InvalidOperationException("Explicit isolated route3 QA context required");
            if (finalizedPlan == null || candidate == null || !actualVehicle || !actualInput || retainTrace == null || actualVehicle.Metrics.Model != 0 || !actualVehicle.NetworkDriven || !actualVehicle.ParkingAutomaticEnabled || !actualVehicle.Body || actualVehicle.Body.isKinematic || !actualVehicle.Visual.ImportedAppearance || candidate.RawDocumentDigest != finalizedPlan.RawDocumentDigest || candidate.FinalBufferDigest != finalizedPlan.FinalBufferDigest)
                throw new InvalidOperationException("Actual stock controller and bound source/final route required");
            plan = finalizedPlan;
            route = candidate;
            vehicle = actualVehicle;
            input = actualInput;
            identity = vehicleIdentity;
            cancellation = token;
            retainEveryTrace = retainTrace;
            knownRoadPackets = new HashSet<string>(plan.CopyRootKeys().SelectMany(key => plan.CopyRootPacketIds(key)).Where(id => plan.PacketRole(id) == PreparedWorldMeshPlan800.Role.GroundRoad), StringComparer.Ordinal);
            samples = route.CopySamples();
            junctions = route.CopyJunctions();
            sourceCornerJoins = junctions.Select(join => new SweptSourceCorner32.Join(join.NodeId, join.IncomingRoad, join.OutgoingRoad)).ToArray();
            ProgressArcMetres = startArc = initialArcMetres;
            endArc = startArc + route.LengthMetres;
            previousPosition = vehicle.Body.position;
            var expected = Point(startArc);
            var heading = Heading(startArc);
            if (new Vector2(expected.x - previousPosition.x, expected.z - previousPosition.z).magnitude > 0.25f || Vector3.Dot(Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up).normalized, heading) < 0.95f)
                throw new InvalidOperationException("Car is not at its pre-staged route start/heading; no teleport fallback");
            Phase = "initial-supported-rest";
            phaseBegan = Time.time;
            Inputs(0, 0, 0);
        }

        private void FixedUpdate()
        {
            if (plan == null || Complete || Failure != null)
                return;
            try
            {
                cornerCrossing = null;
                cancellation.ThrowIfCancellationRequested();
                float elapsed = Time.time - phaseBegan;
                if (Phase == "initial-supported-rest")
                {
                    Inputs(0, 0, 0);
                    if (elapsed >= 2)
                    {
                        Need(vehicle.ParkingBrakeApplied && vehicle.ParkingBrakeHolding && vehicle.AbsoluteSpeed < 0.05f, "Actual initial rest did not stay automatically parked");
                        Phase = "driving-source-loop";
                        phaseBegan = driveBegan = Time.time;
                    }
                }
                else if (Phase == "driving-source-loop")
                {
                    double previousArc = ProgressArcMetres;
                    ProgressArcMetres = ProjectProgress();
                    RecordNodeCrossings(previousArc, ProgressArcMetres);
                    ActualTravelMetres += Vector3.Distance(previousPosition, vehicle.Body.position);
                    previousPosition = vehicle.Body.position;
                    DriveSeconds = Time.time - driveBegan;
                    Need(DriveSeconds < MaximumTravelSeconds, "Actual full loop exceeded its explicit time budget");
                    if (ProgressArcMetres >= endArc)
                    {
                        Need(DriveSeconds >= MinimumDrivingSeconds && ActualTravelMetres >= MinimumPhysicalTravelMetres && crossedNodes.Count == junctions.Length && !vehicle.Reverse, "Complete topology/physical travel/forward control proof missing");
                        Phase = "controlled-stop";
                        phaseBegan = Time.time;
                        Inputs(0, 1, 0, 1);
                    }
                    else
                        DriveWithOrdinaryInputs();
                }
                else if (Phase == "controlled-stop")
                {
                    Inputs(0, 1, 0, 1);
                    Need(elapsed < 10, "Actual loop stop timed out");
                    if (vehicle.AbsoluteSpeed < 0.08f)
                    {
                        Inputs(0, 0, 0);
                        Phase = "automatic-stopped-hold";
                        phaseBegan = Time.time;
                        holdBegan = -1;
                    }
                }
                else if (Phase == "automatic-stopped-hold")
                {
                    Inputs(0, 0, 0);
                    if (holdBegan < 0 && elapsed >= 2)
                    {
                        Need(vehicle.ParkingBrakeApplied && vehicle.ParkingBrakeHolding, "Automatic hold absent after genuine loop stop");
                        holdBegan = Time.time;
                        holdAnchor = vehicle.Body.position;
                    }

                    if (holdBegan >= 0)
                    {
                        holdSamples++;
                        MaximumHoldExcursion = Math.Max(MaximumHoldExcursion, Vector3.Distance(holdAnchor, vehicle.Body.position));
                        Need(vehicle.ParkingBrakeApplied && vehicle.ParkingBrakeHolding && MaximumHoldExcursion < MaximumHoldExcursionMetres, "Actual parking hold lost or exceeded5cm MAX excursion");
                        if (Time.time - holdBegan >= MeasuredHoldSeconds)
                        {
                            Need(holdSamples >= Mathf.CeilToInt(10 / Time.fixedDeltaTime) + 1, "Actual full100Hz hold samples missing");
                            Complete = true;
                            Phase = "complete-parked";
                        }
                    }
                }

                ObserveActualSupport();
            }
            catch (Exception error)
            {
                Failure = error.ToString();
                Inputs(0, 1, 0, 1);
            }
        }

        private void DriveWithOrdinaryInputs()
        {
            // A finite low-speed driver, not alternate physics. The look-ahead is a
            // steering target inside the already checked source-connected path.
            float lookAhead = Mathf.Clamp(3.5f + vehicle.AbsoluteSpeed * 0.35f, 4.5f, 6);
            Vector3 aim = Point(ProgressArcMetres + lookAhead);
            Vector3 local = vehicle.transform.InverseTransformPoint(aim);
            float steeringDegrees = Mathf.Atan2(2 * vehicle.Metrics.Wheelbase * local.x, local.x * local.x + local.z * local.z) * Mathf.Rad2Deg;
            float permittedSteer = Mathf.Lerp(30, 10, Mathf.Clamp01(vehicle.AbsoluteSpeed / 48));
            var before = Heading(ProgressArcMetres - 3);
            var after = Heading(ProgressArcMetres + 3);
            float curvatureEstimate = Vector3.Angle(before, after) * Mathf.Deg2Rad / 6;
            float desired = Math.Min(DesiredSpeedMetresPerSecond, Mathf.Sqrt(MaximumLateralAccelerationMetresPerSecondSquared / Math.Max(0.001f, curvatureEstimate)));
            float error = desired - vehicle.Speed;
            integralSpeedError = Mathf.Clamp(integralSpeedError + error * Time.fixedDeltaTime * IntegralDemandPerMetre, 0, MaximumIntegralDemand);
            float throttle = error >= -PedalHysteresisMetresPerSecond ? Mathf.Clamp(BaseRollingDemand + error * ProportionalDemandPerMetrePerSecond + integralSpeedError, 0, MaximumThrottle) : 0;
            float brake = error < -PedalHysteresisMetresPerSecond ? Mathf.Clamp(-error * ProportionalDemandPerMetrePerSecond, 0, 0.5f) : 0;
            if (brake > 0)
                integralSpeedError = Math.Max(0, integralSpeedError - Time.fixedDeltaTime * 0.2f);
            Inputs(throttle, brake, Mathf.Clamp(steeringDegrees / permittedSteer, -1, 1));
        }

        private Vector3 Point(double arc)
        {
            var target = route.AtArc(arc);
            foreach (string id in target.CopyRoadIds())
                if (plan.TryRoad(id, target.X, target.Z, out var hit))
                    return new Vector3((float)target.X, (float)hit.Height, (float)target.Z);
            throw new InvalidOperationException("Driver target lacks named final support; no nearest/analytic fallback");
        }

        private Vector3 Heading(double arc)
        {
            // Secant over one wheelbase avoids pretending a literal one-sided knot
            // is an instantaneous physical yaw command. It does not alter any row.
            Vector3 a = Point(arc - vehicle.Metrics.Wheelbase * 0.5);
            Vector3 b = Point(arc + vehicle.Metrics.Wheelbase * 0.5);
            Vector3 heading = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
            Need(heading.sqrMagnitude > 0.9f, "Finite route heading required");
            return heading;
        }

        private double ProjectProgress()
        {
            // Driver progress projection is restricted to this exact source path;
            // it never chooses or connects roads by their spatial proximity.
            double wrapped = ProgressArcMetres % route.LengthMetres;
            int low = 0, high = samples.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (samples[middle].ArcMetres < wrapped)
                    low = middle + 1;
                else
                    high = middle;
            }

            int centre = low;
            double period = Math.Floor(ProgressArcMetres / route.LengthMetres);
            double bestDistance = double.PositiveInfinity, selected = ProgressArcMetres;
            int selectedSegment = -1;
            long selectedOrdinal = -1;
            double selectedFrom = 0, selectedTo = 0;
            int segmentCount = samples.Length - 1;
            for (int offset = -MaximumProjectionSegments / 2; offset <= MaximumProjectionSegments / 2; offset++)
            {
                int unwrappedIndex = centre + offset;
                int cycle = (int)Math.Floor((double)unwrappedIndex / segmentCount);
                int index = unwrappedIndex - cycle * segmentCount;
                var a = samples[index];
                var b = samples[index + 1];
                double from = (period + cycle) * route.LengthMetres + a.ArcMetres;
                double to = (period + cycle) * route.LengthMetres + b.ArcMetres;
                if (to < ProgressArcMetres - 3 || from > ProgressArcMetres + 15)
                    continue;
                double dx = b.X - a.X, dz = b.Z - a.Z, squared = dx * dx + dz * dz;
                if (squared == 0)
                    continue;
                double fraction = Math.Max(0, Math.Min(1, ((vehicle.Body.position.x - a.X) * dx + (vehicle.Body.position.z - a.Z) * dz) / squared));
                double x = a.X + dx * fraction, z = a.Z + dz * fraction;
                double distance = (vehicle.Body.position.x - x) * (vehicle.Body.position.x - x) + (vehicle.Body.position.z - z) * (vehicle.Body.position.z - z);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    selected = from + (to - from) * fraction;
                    selectedOrdinal = checked(((long)period + cycle) * segmentCount + index);
                    selectedSegment = index;
                    selectedFrom = from;
                    selectedTo = to;
                }
            }

            // Diagnostic-only addendum. The original admission predicates remain
            // exactly unchanged. Capture the rejected physics pose BEFORE throwing:
            // the previous version retained only the preceding successful frame.
            if (!(Math.Sqrt(bestDistance) <= MaximumLateralErrorMetres))
                throw ProjectionFailure("Actual car left its finite named driver corridor", selected, bestDistance, selectedSegment, selectedFrom, selectedTo);
            // Physical movement is not projection-coordinate movement. Preserve
            // the independent0.15m raw/physics step bound before considering any
            // topological corner event; no body pose or progress value is clamped.
            if (Vector3.Distance(previousPosition, vehicle.Body.position) > SweptSourceCorner32.MaximumPhysicalStepMetres)
                throw ProjectionFailure("Actual physics position exceeded the unchanged fixed-step movement bound", selected, bestDistance, selectedSegment, selectedFrom, selectedTo);
            bool ordinaryArcStep = selected - ProgressArcMetres < 0.75 && selected >= ProgressArcMetres - 0.25;
            if (!ordinaryArcStep)
            {
                string refusal = "No prior accepted segment or forward adjacent crossing";
                bool proved = acceptedProjectionSegment >= 0 && selected > ProgressArcMetres && SweptSourceCorner32.TryProve(DescribeSegment(acceptedProjectionSegment), DescribeSegment(selectedOrdinal), sourceCornerJoins, PhysicsPoint(previousPosition), PhysicsPoint(vehicle.Body.position), vehicle.Speed, vehicle.Reverse, MaximumLateralErrorMetres, out cornerCrossing, out refusal);
                if (!proved)
                    throw ProjectionFailure("Progress cannot skip a source event or reverse across the source path; " + refusal, selected, bestDistance, selectedSegment, selectedFrom, selectedTo);
            }

            acceptedProjectionSegment = selectedOrdinal;
            return selected;
        }

        private static SweptSourceCorner32.Point PhysicsPoint(Vector3 point) => new SweptSourceCorner32.Point(point.x, point.y, point.z);
        private SweptSourceCorner32.Segment DescribeSegment(long ordinal)
        {
            if (ordinal < 0)
                throw new InvalidOperationException("Actual accepted source-segment ordinal required");
            int segmentCount = samples.Length - 1;
            int index = (int)(ordinal % segmentCount);
            var a = samples[index];
            var b = samples[index + 1];
            return new SweptSourceCorner32.Segment(ordinal, new SweptSourceCorner32.Point(a.X, a.Y, a.Z), new SweptSourceCorner32.Point(b.X, b.Y, b.Z), a.NodeId, a.CopyRoadIds());
        }

        [Serializable]
        private sealed class RejectedProjection
        {
            public string diagnosticRecipe = "RejectedSourceProjection/1";
            public double priorArcMetres, selectedArcMetres, selectedArcDeltaMetres;
            public double candidateDistanceMetres, segmentFromMetres, segmentToMetres;
            public int selectedSegment;
            public float fixedStepSeconds, simulationSeconds, speedMetresPerSecond;
            public Vector3 actualPosition, priorObservedPosition, actualVelocity, actualForward;
        }

        private Exception ProjectionFailure(string reason, double selected, double squaredDistance, int segment, double from, double to)
        {
            // Failure evidence only: no clamping, segment substitution, force/pose
            // change, physical gate relaxation or fabricated next-step observation.
            var evidence = new RejectedProjection
            {
                priorArcMetres = ProgressArcMetres,
                selectedArcMetres = selected,
                selectedArcDeltaMetres = selected - ProgressArcMetres,
                candidateDistanceMetres = Math.Sqrt(squaredDistance),
                selectedSegment = segment,
                segmentFromMetres = from,
                segmentToMetres = to,
                fixedStepSeconds = Time.fixedDeltaTime,
                simulationSeconds = Time.time,
                speedMetresPerSecond = vehicle.Speed,
                actualPosition = vehicle.Body.position,
                priorObservedPosition = previousPosition,
                actualVelocity = vehicle.Body.linearVelocity,
                actualForward = vehicle.transform.forward
            };
            return new InvalidOperationException(reason + " | actual rejected projection=" + JsonUtility.ToJson(evidence));
        }

        private void RecordNodeCrossings(double previous, double current)
        {
            foreach (var junction in junctions)
            {
                if (crossedNodes.Contains(junction.NodeId))
                    continue;
                var crossing = samples.First(s => s.NodeId == junction.NodeId);
                double station = crossing.ArcMetres;
                while (station < startArc)
                    station += route.LengthMetres;
                if (previous <= station && current >= station)
                    crossedNodes.Add(junction.NodeId);
            }
        }

        private void ObserveActualSupport()
        {
            var allowed = route.CopyContactRoadIds(ProgressArcMetres, ContactHalfSpanMetres);
            var trace = new Trace
            {
                provedAdjacentCorner = cornerCrossing != null,
                projectionSegmentOrdinal = acceptedProjectionSegment,
                adjacentCornerCrossing = cornerCrossing,
                vehicle = identity,
                phase = Phase,
                roadIds = allowed,
                routeArcMetres = ProgressArcMetres,
                simulationSeconds = Time.time,
                speedMetresPerSecond = vehicle.Speed,
                travelMetres = ActualTravelMetres,
                throttle = vehicle.NetworkThrottle,
                brake = vehicle.NetworkBrake,
                steer = vehicle.NetworkSteer,
                damage = vehicle.Damage,
                parkingApplied = vehicle.ParkingBrakeApplied,
                parkingHolding = vehicle.ParkingBrakeHolding,
                reverse = vehicle.Reverse,
                position = vehicle.Body.position,
                velocity = vehicle.Body.linearVelocity,
                forward = vehicle.transform.forward,
                grounded = (bool[])vehicle.WheelGrounded.Clone(),
                contactPoints = (Vector3[])vehicle.ContactPoints.Clone(),
                contactNormals = (Vector3[])vehicle.ContactNormals.Clone(),
                contactPackets = new string[4],
                rayTriangles = new int[4],
                rayPoints = new Vector3[4],
                rayNormals = new Vector3[4]
            };
            bool supported = true;
            for (int index = 0; index < 4; index++)
            {
                Vector3 origin = vehicle.transform.TransformPoint(VehicleGeometry.Anchor(vehicle.Metrics.Model, index));
                if (!Physics.Raycast(origin, -vehicle.transform.up, out var hit, vehicle.Metrics.SuspensionRayLength, ~(1 << 8), QueryTriggerInteraction.Ignore))
                {
                    trace.contactPackets[index] = "MISSING";
                    trace.rayTriangles[index] = -1;
                    supported = false;
                    continue;
                }

                trace.contactPackets[index] = hit.collider.name;
                trace.rayTriangles[index] = hit.triangleIndex;
                trace.rayPoints[index] = hit.point;
                trace.rayNormals[index] = hit.normal;
                bool named = hit.collider is MeshCollider && plan.PacketRole(hit.collider.name) == PreparedWorldMeshPlan800.Role.GroundRoad && allowed.Any(id => plan.TryRoad(id, hit.point.x, hit.point.z, out var road) && Math.Abs(road.Height - hit.point.y) <= NamedHeightToleranceMetres);
                supported &= named;
            }

            Vector3 expected = Point(ProgressArcMetres);
            trace.lateralErrorMetres = new Vector2(vehicle.Body.position.x - expected.x, vehicle.Body.position.z - expected.z).magnitude;
            retainEveryTrace(trace); // Retain the failed record BEFORE refusing the run.
            Need(supported && trace.grounded.All(value => value) && vehicle.Damage <= MaximumDamage, "Actual four-wheel final asphalt support/damage gate failed");
        }

        private void Inputs(float throttle, float brake, float steer, float handbrake = 0)
        {
            vehicle.NetworkThrottle = input.Throttle = throttle;
            vehicle.NetworkBrake = input.Brake = brake;
            vehicle.NetworkSteer = input.Steer = steer;
            vehicle.NetworkHandbrake = input.Handbrake = handbrake;
        }

        private static void Need(bool condition, string reason)
        {
            if (!condition)
                throw new InvalidOperationException(reason);
        }

        private void OnDestroy()
        {
            // Ordinary input request only. The owning proof destroys its own car;
            // this helper never changes Rigidbody constraints/kinematic state/pose.
            if (vehicle && input)
                Inputs(0, 1, 0, 1);
        }
    }
}
