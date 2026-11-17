# Investigating driving hitches

Use a disposable beta profile and a fresh output directory ending in `.noindex`.
Keep diagnostic players separate from the installed game. Development profiling
changes frame pacing and allocation behavior, so compare any proposed fix in a
normal release player before accepting it.

`SharedBetaBuild.BuildStandard` accepts `-earthUnscriptedGraphicsTraceBuild` to
build a development Standard player. Start that player with Unity's
`-profiler-enable -profiler-log-file /tmp/EarthUnscripted-drive.raw` options, along
with the existing isolated beta proof arguments. See Unity's
[profiler command-line reference](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/profiler/command-line-arguments).

After the player exits, run the editor batch method
`EarthUnscripted.Editor.ProfilerCaptureReadback.Batch`, supplying
`-earthProfilerInput /tmp/EarthUnscripted-drive.raw` and
`-earthProfilerOutput /tmp/EarthUnscripted-drive-summary.json`.
It loads up to 2,000 recorded frames, skips the first 18 seconds of that loaded
window, and reports the 24 slowest remaining frames with CPU samples over 2 ms.
The elapsed time is relative to the first retained frame, not process launch.
Read the frame hierarchy before attributing time: parent/child values overlap,
and frame-limiter waits, screenshots and shutdown must be distinguished from
ordinary driving work. The report is a diagnostic summary, not an acceptance test.

The Beta 10 investigation read two real captures successfully. Several long
development frames spent 50–74 ms in `WaitForTargetFPS`; other individual callbacks
varied between runs. A chunk-coroutine spike did not recur in the separately
timed cache stages. No single production cause was established from those
captures. The installed release completed its physical route with no streaming
braking or loss of support, but brief frame hitches remained. Track subsequent release-player and listening results
in [issue 18](https://github.com/wiffair/earth-unscripted/issues/18).

Keep raw captures and owner-profile evidence out of the repository. The captures
can be large and can contain names, paths and precise game locations.

## Graphics-state captures

Unity 6000.6 records graphics states in Development players. Release players can preload a saved collection. A release verification argument cannot enable tracing.

The checked Mac captures live under `Unity/Assets/StreamingAssets/GraphicsWarmup/OSXPlayer/Metal`. Their `capture.json` records the capture source, engine version, quality, hashes and observed scope. Standard contains 47 states; HD contains 49. Both completed native warmup in the normal release candidate. Other platforms and graphics APIs need their own captures.

To refresh them, build an isolated Development pair with `-earthUnscriptedGraphicsTraceBuild`, exercise the relevant scenes, and quit cleanly. Check the resulting collection API, runtime platform, quality, state counts and file hash before replacing its matching bundled file and receipt. Rebuild normal release players and record `completedWarmupCount` and `isWarmedUp` after the warmup job completes. Loaded input counts alone do not establish completion. Review the full native player log, including messages before application error callbacks register. Captures cover observed raster states; compute and ray-tracing work need separate investigation.

See Unity's [tracing requirements](https://docs.unity.com/en-us/engine/6000.6/manual/materials-and-shaders/shaders/shader-troubleshooting/shader-reduce-stalling/shader-prevent-stutter/shader-pso-trace-warming/shader-pso-trace) and [completion counter](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rendering/graphicsstatecollection/completedwarmupcount).

## Timing overhead

Ordinary beta play keeps a bounded in-memory frame/physical sample history and writes its summary on quit. It skips native profiler counters, heap polling and periodic JSON reports. The on-screen statistics remain available.

An isolated proof keeps detailed timing by default. `-earthUnscriptedDetailedTiming` also requests periodic reports and heap statistics in ordinary beta play. Use `-earthUnscriptedLightweightTiming` for a normal-like isolated comparison: it disables work scopes, native profiler counters, heap-stat calls and periodic writes while retaining wall/simulation frame timing, physical support and final evidence. Explicit lightweight mode takes precedence over detailed mode.

`-earthUnscriptedDisableWorkTelemetry` disables only nested work scopes; other detailed timing remains. Compare the same normal release and prepared-area state with diagnostics on, off and on again before attributing a pause to the enclosing callback. Differences from one pass can reflect worker scheduling, traffic or driver caches.
