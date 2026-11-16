using System;
using System.Linq;
using RoadRush.Core800;
using RoadRush.VehiclePopulation;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GameSession
    {
        SessionVehiclePopulation actualVehicles;
        int actualTrafficCohort;
        BootActorState actualVehicleBootStage;

        /// <summary>
        /// Persistent read-only population service. Null/unbound is unavailable,
        /// never an empty safe scene. It does not write career/network/physics state.
        /// </summary>
        public SessionVehiclePopulation ActualVehicles => actualVehicles;

        void BeginActualVehicleRoster()
        {
            RevokeActualVehicleRoster();
            actualVehicles = new SessionVehiclePopulation(this, World.IsCircuit);
            actualTrafficCohort = 0;
            actualVehicleBootStage = null;
            VehiclePopulationHooks.Attach(actualVehicles);
            var driver = gameObject.GetComponent<VehiclePopulationPhaseDriver>();
            if (!driver) driver = gameObject.AddComponent<VehiclePopulationPhaseDriver>();
            driver.Population = actualVehicles;
        }

        void BeginStagedActualVehicleRoster(BootActorState actualStage)
        {
            if (actualStage == null) throw new ArgumentNullException(nameof(actualStage));
            BeginActualVehicleRoster();
            actualVehicleBootStage = actualStage;
        }

        internal object CaptureActualVehicleStage(GameObject root)
        {
            if (actualVehicleBootStage == null) return null; // Actual post-boot factory, not caller readiness.
            if (!actualVehicleBootStage.OwnsSuspendedVehicle(root))
                throw new InvalidOperationException("Actual private boot stage does not own this suspended vehicle.");
            return actualVehicleBootStage;
        }

        internal void RequireActivatedActualVehicleStage(GameObject root, object capturedStage)
        {
            if (actualVehicleBootStage == null || !ReferenceEquals(capturedStage, actualVehicleBootStage) ||
                !actualVehicleBootStage.OwnsActivatedVehicle(root))
                throw new InvalidOperationException("Actual private boot activation receipt does not own this vehicle.");
        }

        internal VehiclePopulationHooks.FactoryScope BeginActualVehicleFactory(
            UnityEngine.Object actualProducer, string slot, ActualVehicleRosterDomain.Role role)
        {
            if (actualVehicles == null) BeginActualVehicleRoster();
            bool sessionFactory = ReferenceEquals(actualProducer, this) &&
                (role == ActualVehicleRosterDomain.Role.Player || role == ActualVehicleRosterDomain.Role.Traffic ||
                 role == ActualVehicleRosterDomain.Role.Police);
            bool raceFactory = ReferenceEquals(actualProducer, Racing) && Racing &&
                role == ActualVehicleRosterDomain.Role.RaceOpponent;
            bool lanFactory = ReferenceEquals(actualProducer, Network) && Network &&
                role == ActualVehicleRosterDomain.Role.LanPeer;
            if (!sessionFactory && !raceFactory && !lanFactory)
                actualVehicles.Domain.Poison("No registered actual vehicle producer for this role.");
            return VehiclePopulationHooks.Begin(actualVehicles, slot, role);
        }

        void SealActualVehicleBoot()
        {
            // Registry failure disables the semantic consumer, never alters an
            // otherwise functioning career/physics boot into an exception path.
            try
            {
                if (!Car || actualVehicles.Domain.Find(Car.gameObject)?.Slot != "player")
                    throw new InvalidOperationException("Actual session player is not the registered player root.");
                if (actualVehicleBootStage == null)
                    throw new InvalidOperationException("Actual private boot stage receipt missing.");
                actualVehicles.ValidateAndSealActualBoot(actualVehicleBootStage);
                actualVehicleBootStage = null;
            }
            catch (Exception exception) { actualVehicles.Domain.Poison(exception.Message); }
        }

        internal void RetireActualVehicle(UnityEngine.Object actualProducer, GameObject root)
        {
            if (actualVehicles == null || !root) return;
            var member = actualVehicles.Domain.Find(root);
            bool raceRemoval = ReferenceEquals(actualProducer, Racing) &&
                member?.VehicleRole == ActualVehicleRosterDomain.Role.RaceOpponent;
            bool lanRemoval = ReferenceEquals(actualProducer, Network) &&
                member?.VehicleRole == ActualVehicleRosterDomain.Role.LanPeer;
            if (!raceRemoval && !lanRemoval)
            {
                actualVehicles.Domain.Poison("No actual producer authority for this vehicle retirement.");
                return;
            }
            actualVehicles.BeginRetirement(root);
            // Deactivation can confirm immediately; Destroy is confirmed by watcher.
            actualVehicles.ObserveInactiveOrDestroyed(root);
        }

        int NextActualTrafficCohort() => actualTrafficCohort++;

        static string ActualTrafficSlot(int cohort, int index, bool police)
        {
            string role = police ? "police/" : "traffic/";
            return cohort == 0 ? role + index : role + "extra/" + cohort + "/" + index;
        }

        internal void BindActualVehicleScope(GroundLease800 ground)
        {
            if (actualVehicles == null || !World || !World.GeographicWorld ||
                !World.GeographicWorld.TryCaptureVehicleTownContext800(ground, out var context))
                throw new InvalidOperationException("Actual completed ground/town scope unavailable.");
            actualVehicles.BindActualWorld(context);
        }

        void RevokeActualVehicleRoster()
        {
            if (actualVehicles == null) return;
            actualVehicleBootStage = null;
            actualVehicles.Revoke();
            VehiclePopulationHooks.Detach(actualVehicles);
            actualVehicles = null;
        }
    }
}

namespace RoadRush.VehiclePopulation
{
    /// <summary>
    /// Passive notifications inserted into real TrafficCar/VehicleController Awake.
    /// The construction stack is controlled by authorized session factory scopes.
    /// It does not discover a population by scanning the scene.
    /// </summary>
    internal static partial class VehiclePopulationHooks
    {
        static readonly System.Collections.Generic.List<SessionVehiclePopulation> populations =
            new System.Collections.Generic.List<SessionVehiclePopulation>();
        // Awake observations precede registry creation in unsupported/custom scenes.
        // Retaining these actual roots prevents a later empty "complete" claim.
        static readonly System.Collections.Generic.List<GameObject> observedRoots =
            new System.Collections.Generic.List<GameObject>();
        const int MaximumObservedRoots = 512; // A bounded guard, never an empty-complete fallback.
        [ThreadStatic] static FactoryScope current;

        internal sealed class FactoryScope : IDisposable
        {
            readonly SessionVehiclePopulation population;
            readonly ActualVehicleRosterDomain.FactoryTicket ticket;
            readonly FactoryScope previous;

            internal FactoryScope(SessionVehiclePopulation population, string slot,
                ActualVehicleRosterDomain.Role role)
            {
                this.population = population;
                try { ticket = population.Domain.BeginFactory(slot, role); }
                catch (Exception exception)
                {
                    // A semantic refusal must not interrupt the original game factory.
                    population.Domain.Poison(exception.Message);
                }
                previous = current;
                current = this;
            }

            internal void Observe(GameObject root)
            {
                try { ticket?.Observe(root); }
                catch (Exception exception) { population.Domain.Poison(exception.Message); }
                var watcher = root.GetComponent<ActualVehiclePresence>();
                if (!watcher) watcher = root.AddComponent<ActualVehiclePresence>();
                watcher.Population = population;
            }

            internal void Commit()
            {
                if (ticket == null) return;
                try { population.RegisterActual(ticket); }
                catch (Exception exception) { population.Domain.Poison(exception.Message); }
            }

            public void Dispose()
            {
                current = previous;
                ticket?.Dispose();
            }
        }

        internal static FactoryScope Begin(SessionVehiclePopulation population, string slot,
            ActualVehicleRosterDomain.Role role) => new FactoryScope(population, slot, role);

        internal static void Attach(SessionVehiclePopulation population)
        {
            foreach (var stale in observedRoots.Where(root => !root).ToArray())
                observedRoots.Remove(stale);
            populations.Add(population);
            foreach (var root in observedRoots)
                if (root && root.activeInHierarchy && root.scene == population.Session.gameObject.scene)
                    population.Domain.ObserveUnownedCreation();
        }
        internal static void Detach(SessionVehiclePopulation population) => populations.Remove(population);

        internal static void ObserveAwake(Component component)
        {
            foreach (var stale in observedRoots.Where(root => !root).ToArray())
                observedRoots.Remove(stale);
            if (observedRoots.Count >= MaximumObservedRoots &&
                !observedRoots.Any(root => ReferenceEquals(root, component.gameObject)))
            {
                foreach (var population in populations)
                    population.Domain.Poison("Observed vehicle budget exceeded.");
                return;
            }
            if (!observedRoots.Any(root => ReferenceEquals(root, component.gameObject)))
                observedRoots.Add(component.gameObject);
            if (current != null)
            {
                current.Observe(component.gameObject);
                return;
            }
            if (!component.GetComponent<ActualVehiclePresence>())
                component.gameObject.AddComponent<ActualVehiclePresence>();
            foreach (var population in populations)
                if (population.Session && population.Session.gameObject.scene == component.gameObject.scene)
                    population.Domain.ObserveUnownedCreation();
        }

        internal static void ObserveReactivation(GameObject root)
        {
            // An inactive retired root may not silently rejoin a published roster.
            // Actual creation must pass through a new producer reservation.
            foreach (var population in populations)
                if (population.Session && population.Session.gameObject.scene == root.scene)
                    population.Domain.Poison("Vehicle root/watcher reactivated outside a fresh factory.");
        }

        internal static void ShapeWillChange(Component component)
        {
            var watcher = component.GetComponent<ActualVehiclePresence>();
            watcher?.Population?.MarkShape(component.gameObject);
        }

        internal static void PoseWillReset(Component component)
        {
            var watcher = component.GetComponent<ActualVehiclePresence>();
            watcher?.Population?.MarkPose(component.gameObject);
        }
    }

    /// <summary>Tracks actual root lifetime, including deferred Destroy retirement.</summary>
    internal sealed class ActualVehiclePresence : MonoBehaviour
    {
        internal SessionVehiclePopulation Population;
        bool hasBeenDisabled;
        void OnDisable()
        {
            hasBeenDisabled = true;
            Population?.ObservePresenceDisabled(gameObject);
        }
        void OnEnable()
        {
            // First AddComponent enables before its factory assigns Population.
            // Only subsequent reactivation is an unexpected lifecycle transition.
            if (hasBeenDisabled) VehiclePopulationHooks.ObserveReactivation(gameObject);
        }
        void OnDestroy()
        {
            Population?.ObserveInactiveOrDestroyed(gameObject);
            // Do not erase an actual root merely because its watcher component was
            // destroyed. The bounded observation table prunes genuine fake-null roots.
        }
    }

    /// <summary>
    /// Runs after normal vehicle controllers. Fixed samples describe pre-solve
    /// state; LateUpdate samples describe the frame after actor updates.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    internal sealed class VehiclePopulationPhaseDriver : MonoBehaviour
    {
        internal SessionVehiclePopulation Population;
        void FixedUpdate() => Population?.Sample(VehicleSamplePhase.BeforePhysicsSolve);
        void LateUpdate() => Population?.Sample(VehicleSamplePhase.AfterActorFrame);
    }
}
