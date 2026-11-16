#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// Editor-QA observation around ONE actual Town producer. No world/readiness,
    /// geometry or performance authority. Run/resume/dispose on the Unity main
    /// thread; same creator-thread enforcement is not a main-thread certificate.
    /// This never flattens nested iterators, interprets waits, or substitutes null.
    /// It owns/disposes the supplied producer exactly once after construction.
    /// </summary>
    public static class TownConstructionTiming800
    {
        public const int MaximumRawSamples = 65536;
        public const int MaximumYieldTypeNames = 128;
        public const int MaximumTypeNameCharacters = 160;
        public const int MaximumOutputBytes = 24 * 1024 * 1024;
        public const string UnspecifiedVariant = "UNSPECIFIED_NO_COMPARATIVE_CLAIM";

        /// <summary>
        /// outputPath must be a fresh absolute JSON file in the proof's existing
        /// isolated output directory. No overwrite or parent directory creation.
        /// Cancellation is checked when Unity resumes this wrapper; an opaque wait is
        /// not replaced/polled. Owner Dispose can retire it while waiting.
        /// Use the labelled overload for matched A/B. Timing starts at first
        /// MoveNext, not at factory/cook/app launch. Creation failure leaves producer
        /// ownership with caller; successful return transfers it to this wrapper.
        /// </summary>
        public static IEnumerator Run(IEnumerator actualProducer, string outputPath)
        {
            return Run(actualProducer, outputPath, UnspecifiedVariant, default);
        }

        public static IEnumerator Run(IEnumerator actualProducer, string outputPath,
            string sourceVariant, CancellationToken cancellation = default)
        {
            return new MeasuredIterator(actualProducer, outputPath, sourceVariant,
                cancellation, Stopwatch.GetTimestamp, Stopwatch.Frequency, () => Time.frameCount);
        }

        // Tests exercise the SAME wrapper with managed clocks, never fake Unity
        // objects/worlds. This overload is internal and grants no runtime capability.
        internal static IEnumerator RunWithClocks(IEnumerator actualProducer, string outputPath,
            string sourceVariant, CancellationToken cancellation, Func<long> timestamp,
            long frequency, Func<int> frameNumber)
        {
            return new MeasuredIterator(actualProducer, outputPath, sourceVariant,
                cancellation, timestamp, frequency, frameNumber);
        }

        private sealed class Sample
        {
            internal long Step;
            internal int Frame;
            internal bool ReturnedYield;
            internal string YieldType;
            internal double WorkWallSeconds;
            internal double BetweenCallsWallSeconds;
            internal string FailureType;
        }

        private sealed class MeasuredIterator : IEnumerator, IDisposable
        {
            private readonly IEnumerator producer;
            private readonly string outputPath, variant;
            private readonly CancellationToken cancellation;
            private readonly Func<long> timestamp;
            private readonly Func<int> frameNumber;
            private readonly long frequency;
            private readonly int ownerThread;
            private readonly List<Sample> samples = new List<Sample>();
            private readonly Dictionary<string, long> yieldTypes = new Dictionary<string, long>(StringComparer.Ordinal);
            private object current;
            private bool currentValid, started, finished, disposedProducer, completed;
            private long began, lastReturn, calls, currentReads, yields, rawOmitted, observedFrameChanges;
            private int firstFrame, lastFrame;
            private double activeWorkWallSeconds, maximumWorkWallSeconds, betweenCallsWallSeconds;
            private string status, failureType, disposalFailureType;

            internal MeasuredIterator(IEnumerator actualProducer, string path, string sourceVariant,
                CancellationToken token, Func<long> clock, long clockFrequency, Func<int> frames)
            {
                if (actualProducer == null || clock == null || frames == null || clockFrequency <= 0)
                    throw new ArgumentException("Actual producer and positive-frequency clocks required");
                if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || Path.GetFullPath(path) != path
                    || Path.GetExtension(path) != ".json" || !Directory.Exists(Path.GetDirectoryName(path))
                    || File.Exists(path) || Directory.Exists(path))
                    throw new IOException("Fresh absolute JSON in the existing isolated proof directory required");
                if (string.IsNullOrWhiteSpace(sourceVariant) || sourceVariant.Length > MaximumTypeNameCharacters)
                    throw new ArgumentException("Bounded explicit descriptive source-variant label required");
                producer = actualProducer; outputPath = path; variant = sourceVariant;
                cancellation = token; timestamp = clock; frequency = clockFrequency; frameNumber = frames;
                ownerThread = Thread.CurrentThread.ManagedThreadId;
            }

            public object Current
            {
                get
                {
                    RequireThread();
                    if (!currentValid) throw new InvalidOperationException("No current yielded item");
                    return current; // Never re-read actualProducer.Current.
                }
            }

            public bool MoveNext()
            {
                RequireThread();
                if (finished) return false;
                currentValid = false;
                long workStarted = 0;
                bool measuringWork = false;
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    long callStarted = timestamp();
                    int frame = frameNumber(); // Outside measured producer work.
                    if (!started)
                    {
                        started = true; began = callStarted; firstFrame = lastFrame = frame;
                    }
                    else
                    {
                        if (frame < lastFrame) throw new InvalidOperationException("Observed frame counter moved backwards");
                        if (frame != lastFrame) observedFrameChanges++;
                        lastFrame = frame;
                    }
                    double waiting = calls == 0 ? 0 : Seconds(callStarted - lastReturn);
                    betweenCallsWallSeconds += waiting;
                    calls++;
                    workStarted = timestamp(); measuringWork = true;
                    bool more = producer.MoveNext();
                    object yielded = null;
                    if (more)
                    {
                        currentReads++;
                        yielded = producer.Current; // EXACTLY one read, only after true.
                    }
                    long workEnded = timestamp(); measuringWork = false;
                    double work = Seconds(workEnded - workStarted);
                    AddWork(work);
                    string type = more ? YieldType(yielded) : "END_CURRENT_NOT_READ";
                    AddSample(new Sample { Step = calls, Frame = frame, ReturnedYield = more,
                        YieldType = type, WorkWallSeconds = work, BetweenCallsWallSeconds = waiting });
                    if (!more)
                    {
                        completed = true;
                        Finish("COMPLETED", null);
                        return false;
                    }
                    yields++;
                    yieldTypes[type] = yieldTypes.TryGetValue(type, out long count) ? count + 1 : 1;
                    current = yielded; currentValid = true;
                    lastReturn = timestamp();
                    return true; // The caller receives the EXACT original object.
                }
                catch (Exception failure)
                {
                    if (measuringWork)
                    {
                        double work = Seconds(timestamp() - workStarted);
                        AddWork(work);
                        AddSample(new Sample { Step = calls, Frame = lastFrame,
                            WorkWallSeconds = work, FailureType = failure.GetType().FullName });
                    }
                    Finish(failure is OperationCanceledException ? "CANCELLED" : "FAILED", failure);
                    ExceptionDispatchInfo.Capture(failure).Throw();
                    throw; // Unreachable; keeps compiler flow explicit.
                }
            }

            public void Reset() => throw new NotSupportedException("Never reset or replay the actual Town producer");

            public void Dispose()
            {
                RequireThread();
                if (!finished) Finish(started ? "DISPOSED_EARLY" : "DISPOSED_BEFORE_FIRST_MOVE", null);
            }

            private void Finish(string outcome, Exception originalFailure)
            {
                if (finished) return;
                finished = true; current = null; currentValid = false;
                status = outcome; failureType = originalFailure?.GetType().FullName;
                Exception cleanupFailure = null, writeFailure = null;
                if (!disposedProducer)
                {
                    disposedProducer = true;
                    try { (producer as IDisposable)?.Dispose(); }
                    catch (Exception error)
                    {
                        cleanupFailure = error; disposalFailureType = error.GetType().FullName;
                        status = "PRODUCER_DISPOSE_FAILED";
                    }
                }
                try { WriteSummary(); }
                catch (Exception error) { writeFailure = error; }
                // Preserve a sole original failure by identity. If cleanup or
                // diagnostic output ALSO fails, propagate an aggregate retaining
                // every real exception rather than hiding a failed disposal.
                var failures = new List<Exception>();
                if (originalFailure != null) failures.Add(originalFailure);
                if (cleanupFailure != null) failures.Add(cleanupFailure);
                if (writeFailure != null) failures.Add(writeFailure);
                if (failures.Count > 1)
                    throw new AggregateException("Actual Town timing/producer/disposal failures", failures);
                if (originalFailure != null) return; // MoveNext rethrows exact source exception.
                if (cleanupFailure != null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                if (writeFailure != null) ExceptionDispatchInfo.Capture(writeFailure).Throw();
            }

            private void AddWork(double work)
            {
                activeWorkWallSeconds += work;
                maximumWorkWallSeconds = Math.Max(maximumWorkWallSeconds, work);
            }

            private void AddSample(Sample sample)
            {
                if (samples.Count < MaximumRawSamples) samples.Add(sample);
                else rawOmitted++; // Totals still include EVERY actual call.
            }

            private string YieldType(object value)
            {
                if (value == null) return "null";
                string name = value.GetType().FullName ?? value.GetType().Name;
                if (name.Length > MaximumTypeNameCharacters) name = name.Substring(0, MaximumTypeNameCharacters);
                if (!yieldTypes.ContainsKey(name) && yieldTypes.Count >= MaximumYieldTypeNames)
                    return "OTHER_TYPES_AFTER_DECLARED_CAP";
                return name;
            }

            private double Seconds(long ticks)
            {
                if (ticks < 0) throw new InvalidOperationException("Monotonic timing clock moved backwards");
                return ticks / (double)frequency;
            }

            private void RequireThread()
            {
                if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                    throw new InvalidOperationException("Resume/dispose on the same creator Unity main thread");
            }

            private void WriteSummary()
            {
                // Completion wall excludes output encoding/file IO, but includes
                // measurement overhead, Unity waiting/rendering and producer dispose.
                double elapsed = started ? Seconds(timestamp() - began) : 0;
                using (var buffer = new MemoryStream())
                {
                    using (var text = new StreamWriter(buffer, new System.Text.UTF8Encoding(false), 4096, true))
                    using (var json = new JsonTextWriter(text) { Formatting = Formatting.None, CloseOutput = false })
                    {
                        void Field(string key, object value) { json.WritePropertyName(key); json.WriteValue(value); }
                        json.WriteStartObject();
                        Field("schema", "EarthUnscriptedTownConstructionTiming1"); Field("sourceVariantLabel", variant);
                        Field("status", status); Field("producerCompleted", completed);
                        Field("producerDisposeAttempted", disposedProducer);
                        Field("producerImplementsIDisposable", producer is IDisposable);
                        Field("producerDisposeSucceeded", disposedProducer && producer is IDisposable && disposalFailureType == null);
                        Field("creatorManagedThreadId", ownerThread); Field("timestampFrequencyTicksPerSecond", frequency);
                        Field("failureType", failureType);
                        Field("disposalFailureType", disposalFailureType); Field("timingUnit", "seconds");
                        Field("workClock", "Monotonic timestamp provider (production default Stopwatch), elapsed WALL; NOT thread/process CPU");
                        Field("workScope", "Only actual producer.MoveNext plus exactly one Current after true; nested yielded iterators are NOT flattened or separately timed");
                        Field("elapsedScope", "First wrapper MoveNext through producer disposal; includes Unity/other work and observer overhead, excludes summary encode/write");
                        Field("cpuMeasured", false); Field("memoryMeasured", false); Field("gpuMeasured", false);
                        Field("fpsOrStartupPerformanceAccepted", false);
                        Field("moveNextCalls", calls); Field("actualCurrentReads", currentReads); Field("returnedYieldItems", yields);
                        Field("elapsedIteratorWallSeconds", elapsed); Field("producerWorkWallSeconds", activeWorkWallSeconds);
                        Field("maximumProducerCallWallSeconds", maximumWorkWallSeconds);
                        Field("betweenWrapperCallsWallSeconds", betweenCallsWallSeconds);
                        Field("betweenCallsMeaning", "Unity/real waits/rendering/background scheduling; NOT pure wait-object CPU or GPU time");
                        Field("firstObservedFrame", started ? (object)firstFrame : null);
                        Field("lastObservedFrame", started ? (object)lastFrame : null);
                        Field("observedFrameChanges", observedFrameChanges);
                        Field("observedFrameNumberAdvance", started ? (long)lastFrame - firstFrame : 0);
                        Field("frameMeaning", "Unity frameCount sampled at wrapper entry, not rendered-frame count/FPS");
                        Field("maximumRawSamples", MaximumRawSamples); Field("rawSamplesOmitted", rawOmitted);
                        Field("allRawSamplesRetained", rawOmitted == 0);
                        json.WritePropertyName("yieldTypeCounts"); json.WriteStartObject();
                        foreach (var pair in yieldTypes) { json.WritePropertyName(pair.Key); json.WriteValue(pair.Value); }
                        json.WriteEndObject();
                        json.WritePropertyName("samples"); json.WriteStartArray();
                        foreach (Sample sample in samples)
                        {
                            json.WriteStartObject(); Field("step", sample.Step); Field("frame", sample.Frame);
                            Field("returnedYield", sample.ReturnedYield); Field("yieldType", sample.YieldType);
                            Field("producerWorkWallSeconds", sample.WorkWallSeconds);
                            Field("betweenCallsWallSeconds", sample.BetweenCallsWallSeconds);
                            Field("failureType", sample.FailureType); json.WriteEndObject();
                            json.Flush();
                            if (buffer.Length > MaximumOutputBytes) throw new IOException("Timing output exceeds declared24MiB; no partial success");
                        }
                        json.WriteEndArray(); json.WriteEndObject(); json.Flush();
                    }
                    if (buffer.Length > MaximumOutputBytes) throw new IOException("Timing output exceeds24MiB");
                    using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        buffer.Position = 0; buffer.CopyTo(output);
                    }
                }
            }
        }
    }
}
#endif

