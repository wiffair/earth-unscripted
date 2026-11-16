using System;
namespace RoadRush.FunnyAnimals.Scheduling800
{
    /// <summary>
    /// Gives one actor a planning turn per frame and carries elapsed simulation
    /// time for the others. This schedules consumers, not an authoritative actor set.
    /// </summary>
    public sealed class RoundRobinTurnClock800
    {
        public const int MaximumClients = 8;
        // Same0.05s integration cap as the unchanged FunnyAnimalPlanner. Two
        // actors at60fps receive about0.033s each; slower service drops excess
        // time instead of inventing large collision-unsafe catch-up movement.
        public const float MaximumStepSeconds = .05f;
        readonly float[] elapsedSeconds = new float[MaximumClients];
        int lastFrame = -1, nextClient, previousCount;

        /// <summary>
        /// Caller must use this when its ordered client set changes identity or
        /// order, even if the count is unchanged. It clears carried time without
        /// allowing a second planning turn in the current frame. This operation
        /// does not validate that the supplied actor set is complete or current.
        /// </summary>
        public void ResetClients()
        {
            Array.Clear(elapsedSeconds, 0, elapsedSeconds.Length);
            nextClient = 0;
            previousCount = 0;
        }

        public bool TryTakeTurn(int actualFrame, int clientCount, float frameDeltaSeconds,
            out int clientIndex, out float elapsedStepSeconds)
        {
            clientIndex = -1;
            elapsedStepSeconds = 0;
            if (actualFrame < 0 || actualFrame <= lastFrame || clientCount < 1 || clientCount > MaximumClients
                || float.IsNaN(frameDeltaSeconds) || float.IsInfinity(frameDeltaSeconds)
                || frameDeltaSeconds < 0) return false;
            lastFrame = actualFrame;
            if (clientCount != previousCount)
            {
                Array.Clear(elapsedSeconds, 0, elapsedSeconds.Length);
                nextClient = 0;
                previousCount = clientCount;
            }
            if (frameDeltaSeconds == 0) return false;
            for (int index = 0; index < clientCount; index++)
                elapsedSeconds[index] = Math.Min(MaximumStepSeconds, elapsedSeconds[index] + frameDeltaSeconds);
            clientIndex = nextClient;
            nextClient = (nextClient + 1) % clientCount;
            elapsedStepSeconds = elapsedSeconds[clientIndex];
            elapsedSeconds[clientIndex] = 0;
            return true;
        }
    }
}
