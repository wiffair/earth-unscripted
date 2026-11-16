using System;
using RoadRush.VehiclePopulation;
using UnityEngine;

namespace RoadRush.Scenery800
{
    /// <summary>
    /// Opt-in, ordered read point for an actual registered vehicle population and
    /// actual typed Town publication. It forwards physical snapshots only; it does
    /// not issue safety, presentation, habitat or ISafeWorld authority.
    /// </summary>
    /// <remarks>
    /// The real producer samples in LateUpdate at order30000. An ordinary Update
    /// reader observes a previous-frame sample, and a pre-solve Fixed sample may
    /// already be stale after PhysX. This component therefore reads at31000 and
    /// requires the actual AfterActorFrame phase in the SAME frame/fixed stamp.
    /// Nothing is installed automatically or changes the legacy Living.Update.
    /// Native positive ordering/lifecycle tests remain required before activation.
    /// </remarks>
    [DefaultExecutionOrder(ActorFramePhasePolicy800.ConsumerLateUpdateOrder)]
    public sealed class ActorFrameReadPump800 : MonoBehaviour
    {
        GameSession session;
        SessionVehiclePopulation population;
        TownSemanticPublication800 town;
        IDisposable townRetirement;
        Action<ActualVehiclePopulationSnapshot, TownSemanticPublication800> consumer;
        int bindingRevision;
        int lastAttemptFrame = -1;
        bool bound;

        public string Reason { get; private set; } = "Actual actor frame reader unbound";
        public int SuccessfulReadFrames
        {
            get; private set;
        }
        public bool GameplaySemanticsBound => false;

        /// <summary>
        /// Bind one explicitly supplied real session/publication and one consumer.
        /// The actual owner predicate compares world/ground/source references and
        /// generations. No GUID, diagnostic ledger or caller readiness flag is used.
        /// The callback must finish synchronously and validate currency at its own
        /// immediate action commit; it must not retain a snapshot across a yield.
        /// </summary>
        public bool Bind(GameSession actualSession, TownSemanticPublication800 actualTown,
            Action<ActualVehiclePopulationSnapshot, TownSemanticPublication800> actualConsumer)
        {
            Retire("Replacing actor frame binding");
            int bindAttempt = bindingRevision;
            if (!actualSession || actualTown == null || actualConsumer == null) return false;
            SessionVehiclePopulation actualPopulation = null;
            IDisposable pendingSubscription = null;
            int capturedRevision = bindAttempt;
            bool fieldsAssigned = false;
            try
            {
                actualPopulation = actualSession.ActualVehicles;
                if (actualPopulation == null || !actualTown.IsCurrent
                    || !actualPopulation.MatchesTownPublication800(actualTown))
                {
                    if (bindingRevision == bindAttempt)
                        Reason = "Actual same-session Town/population association unavailable";
                    return false;
                }
                if (bindingRevision != bindAttempt) return false;
                session = actualSession;
                population = actualPopulation;
                town = actualTown;
                consumer = actualConsumer;
                fieldsAssigned = true;
                capturedRevision = ++bindingRevision;
                pendingSubscription = actualTown.BeforeRevoke((expired, reason) =>
                {
                    if (capturedRevision == bindingRevision && ReferenceEquals(expired, town))
                        Retire("Town retired before actor frame commit: " + reason);
                });
                if (!SameBinding(capturedRevision, actualSession, actualPopulation, actualTown))
                {
                    pendingSubscription.Dispose();
                    return false;
                }
                townRetirement = pendingSubscription;
                pendingSubscription = null;
                bound = true;
                Reason = "Waiting for actual same-frame AfterActorFrame sample";
                return true;
            }
            catch (Exception exception)
            {
                pendingSubscription?.Dispose();
                bool stillThisAttempt = fieldsAssigned
                    ? SameBinding(capturedRevision, actualSession, actualPopulation, actualTown)
                    : bindingRevision == bindAttempt;
                if (stillThisAttempt) Retire("Actual actor frame binding refused: " + exception.Message);
                return false;
            }
        }

        void LateUpdate()
        {
            if (lastAttemptFrame == Time.frameCount)
                return;
            lastAttemptFrame = Time.frameCount;
            if (!bound)
                return;
            int capturedRevision = bindingRevision;
            GameSession actualSession = session;
            SessionVehiclePopulation actualPopulation = population;
            TownSemanticPublication800 actualTown = town;
            Action<ActualVehiclePopulationSnapshot, TownSemanticPublication800> callback = consumer;
            try
            {
                if (!actualSession || !ReferenceEquals(actualSession.ActualVehicles, actualPopulation)
                    || !actualTown.IsCurrent || !actualPopulation.MatchesTownPublication800(actualTown))
                {
                    RetireOldBinding("Actual actor session/Town generation changed");
                    return;
                }
                if (!actualPopulation.TryCapture(out var snapshot, out string reason))
                {
                    if (SameBinding(capturedRevision, actualSession, actualPopulation, actualTown))
                        Reason = "Actual population unavailable: " + reason;
                    return;
                }
                if (!ActorFramePhasePolicy800.MatchesAfterActorFrame(snapshot.Frame,
                    snapshot.FixedTimeSeconds, snapshot.Phase, Time.frameCount, Time.fixedTime)
                    || !snapshot.IsCurrent || capturedRevision != bindingRevision
                    || !actualTown.IsCurrent || !actualPopulation.MatchesTownPublication800(actualTown)
                    || !SameBinding(capturedRevision, actualSession, actualPopulation, actualTown))
                {
                    if (SameBinding(capturedRevision, actualSession, actualPopulation, actualTown))
                        Reason = "Actual actor sample is stale or not AfterActorFrame";
                    return;
                }
                // The receiver still owns final safety/appearance validation;
                // receiving this actual physical sample is not permission to move.
                callback(snapshot, actualTown);
                if (!SameBinding(capturedRevision, actualSession, actualPopulation, actualTown))
                    return;
                SuccessfulReadFrames++;
                Reason = "Actual same-frame physical actor snapshot forwarded";
            }
            catch (Exception exception)
            {
                RetireOldBinding("Actor frame read/consumer refused: " + exception.Message);
            }

            void RetireOldBinding(string reason)
            {
                // Callback code may bind B and then throw. Cleanup belongs only
                // to the captured A attempt, never to that replacement binding.
                if (SameBinding(capturedRevision, actualSession, actualPopulation, actualTown)) Retire(reason);
            }
        }

        bool SameBinding(int revision, GameSession expectedSession,
            SessionVehiclePopulation expectedPopulation, TownSemanticPublication800 expectedTown)
        {
            return revision == bindingRevision && ReferenceEquals(session, expectedSession)
                && ReferenceEquals(population, expectedPopulation) && ReferenceEquals(town, expectedTown);
        }

        public void Retire(string reason)
        {
            bound = false;
            bindingRevision++;
            townRetirement?.Dispose();
            townRetirement = null;
            session = null;
            population = null;
            town = null;
            consumer = null;
            Reason = reason;
            // Keep lastAttemptFrame: retirement/rebind cannot take a second turn
            // in this actual frame. No producer or native object is destroyed.
        }

        void OnDisable() => Retire("Actor frame reader disabled");
        void OnDestroy() => Retire("Actor frame reader destroyed");
    }
}
