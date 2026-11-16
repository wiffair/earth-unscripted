using System;
using System.Collections.Generic;

namespace EarthUnscripted.FunnyAnimals.Scheduling800
{
    public enum StaticProbeStatus { Unknown, Blocked, Clear }
    public delegate StaticProbeStatus StaticGroundProbe<T>(float x, float z, out T result) where T : struct;

    /// <summary>
    /// Bounded memo of immutable ground results at exact binary32 XZ positions.
    /// It never caches actor proximity, source hazards, Town walls or habitat.
    /// </summary>
    /// <remarks>
    /// Main-thread only. An opaque epoch identifies the actual caller-owned lease;
    /// it grants no authority. Every lookup, including a cache hit, must pass the
    /// supplied real-owner currentness check. Unknown work is retried next frame.
    /// Retaining static results across frames lets a route requiring more unique
    /// samples than one frame's sampler budget complete without raising that budget.
    /// </remarks>
    public sealed class StaticGroundMemo800<T> where T : struct
    {
        // Bounded value-only memory policy, not a change to the sampler's16 fresh
        // height calls.512 entries retain recent routes; eviction never admits a
        // missing result.64 lookups matches the candidate's logical query limit.
        public const int MaximumEntries = 512;
        public const int MaximumLookupsPerFrame = 64;
        readonly Dictionary<PointKey, Entry> resolved = new Dictionary<PointKey, Entry>();
        readonly HashSet<PointKey> unknownThisFrame = new HashSet<PointKey>();
        object epoch;
        int frame = -1;
        int remainingLookups;
        long useSequence;
        public int Count => resolved.Count;
        public int ProbeCallsThisFrame { get; private set; }
        public int CacheHitsThisFrame { get; private set; }

        readonly struct PointKey : IEquatable<PointKey>
        {
            readonly int xBits, zBits;
            public PointKey(float x, float z)
            {
                xBits = BitConverter.SingleToInt32Bits(x);
                zBits = BitConverter.SingleToInt32Bits(z);
            }
            public bool Equals(PointKey other) => xBits == other.xBits && zBits == other.zBits;
            public override bool Equals(object other) => other is PointKey key && Equals(key);
            // Integer hash mixing only; no coordinate quantization or source ID.
            public override int GetHashCode() => unchecked(xBits * 397 ^ zBits);
        }
        struct Entry
        {
            public StaticProbeStatus Status;
            public T Value;
            public long LastUse;
        }

        /// <summary>
        /// Advance once per actual frame. Repeated calls cannot refill budgets;
        /// changing epochs clears values while preserving same-frame work spent.
        /// A backwards/negative frame refuses and invalidates cached publication.
        /// </summary>
        public bool BeginFrame(int actualFrame, object actualEpoch)
        {
            if (actualFrame < 0 || actualFrame < frame || actualEpoch == null)
            {
                Invalidate();
                return false;
            }
            if (actualFrame != frame)
            {
                frame = actualFrame;
                remainingLookups = MaximumLookupsPerFrame;
                ProbeCallsThisFrame = CacheHitsThisFrame = 0;
                unknownThisFrame.Clear();
            }
            if (!ReferenceEquals(epoch, actualEpoch))
            {
                resolved.Clear();
                unknownThisFrame.Clear();
                useSequence = 0;
                epoch = actualEpoch;
            }
            return true;
        }

        public StaticProbeStatus Query(float x, float z, Func<bool> realOwnerCurrent,
            StaticGroundProbe<T> probe, out T result)
        {
            result = default;
            // Spend before owner/engine access even on repeated exhausted attempts.
            if (remainingLookups <= 0) return StaticProbeStatus.Unknown;
            remainingLookups--;
            if (epoch == null || realOwnerCurrent == null || probe == null
                || float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z))
                return StaticProbeStatus.Unknown;
            object capturedEpoch = epoch;
            try
            {
                if (!realOwnerCurrent() || !ReferenceEquals(epoch, capturedEpoch))
                {
                    Invalidate();
                    return StaticProbeStatus.Unknown;
                }
                var key = new PointKey(x, z);
                if (resolved.TryGetValue(key, out var cached))
                {
                    cached.LastUse = NextUse();
                    resolved[key] = cached;
                    CacheHitsThisFrame++;
                    result = cached.Value;
                    return cached.Status;
                }
                if (unknownThisFrame.Contains(key)) return StaticProbeStatus.Unknown;
                ProbeCallsThisFrame++;
                var status = probe(x, z, out result);
                // Reentrant revocation during a probe cannot publish its result.
                if (!realOwnerCurrent() || !ReferenceEquals(epoch, capturedEpoch))
                {
                    Invalidate();
                    result = default;
                    return StaticProbeStatus.Unknown;
                }
                if (status != StaticProbeStatus.Clear && status != StaticProbeStatus.Blocked)
                {
                    unknownThisFrame.Add(key);
                    result = default;
                    return StaticProbeStatus.Unknown;
                }
                if (resolved.Count == MaximumEntries) EvictOldest();
                resolved[key] = new Entry { Status = status, Value = result, LastUse = NextUse() };
                return status;
            }
            catch
            {
                Invalidate();
                result = default;
                return StaticProbeStatus.Unknown;
            }
        }
        long NextUse()
        {
            // Avoid a wrapped lifetime stamp becoming a false recent entry.
            if (useSequence == long.MaxValue) { resolved.Clear(); useSequence = 0; }
            return ++useSequence;
        }
        void EvictOldest()
        {
            // At most512 comparisons per insertion; no unbounded eviction queue.
            PointKey oldestKey = default;
            long oldestUse = long.MaxValue;
            foreach (var pair in resolved)
            {
                if (pair.Value.LastUse < oldestUse) { oldestUse = pair.Value.LastUse; oldestKey = pair.Key; }
            }
            resolved.Remove(oldestKey);
        }
        public void Invalidate()
        {
            epoch = null;
            resolved.Clear();
            unknownThisFrame.Clear();
            useSequence = 0;
            // Keep frame and spent budget: revocation must not create more work.
        }
    }
}
