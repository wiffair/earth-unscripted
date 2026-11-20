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
This reader examines the main thread only. It loads up to 2,000 recorded frames, skips the first 18 seconds of that loaded
window, and reports the 24 slowest remaining frames with CPU samples over 2 ms.
The elapsed time is relative to the first retained frame, not process launch.
For a deliberately short diagnostic whose retained tail already contains driving,
`-earthProfilerSkipSeconds 0` includes that whole loaded window. The default remains
18 seconds. Readback records the chosen skip and loaded window duration; neither
mode turns startup, frame-limiter waits or shutdown into evidence of a driving hitch.
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

## Loading and streaming measurements

The performance overlay separates frame time and GPU time (when the graphics
API supplies it) from world preparation. “Main slice” is the latest measured
main-thread operation, with its worst recorded value. It excludes time spent
waiting for providers, numeric workers and collider bakes. Whole-chunk elapsed
and queue-to-ready times expose those waits; they are seconds, not milliseconds.
The data row reports the current worker stage, elapsed time and recent activity.
The coverage row explains preparation speed limiting and supported distance ahead.

The map-index row measures the latest and worst background gameplay-query rebuild, with the number of resident areas and whether another rebuild is pending. Its elapsed time can overlap rendering and geometry work; it is not a main-thread slice or proof of a frame stall. Exported samples include the latest completed rebuild and pending state. Detailed capture also requests per-thread allocation bytes; a negative value means the runtime did not supply them. Ordinary play skips allocation polling for these jobs.

The area-install row separates prepared-file read/parse, background road/junction
indexing and the wait for safe publication between atomic chunk jobs. The index
value is elapsed worker time, not a main-thread frame duration. Ahead-job counts
show how many of the bounded one-at-a-time preparation tickets were reused.
The numeric worker still serializes source and seam changes; it can now prepare
one next needed core while Unity installs the previous ready file. Initial loading
keeps its existing window, and geometry is never declared ready by a source ticket.

Road-chunk, building-visual and building-collider queues represent separate work.
Distant walls and roofs can be visible before their colliders are cooked.
Colliders are scheduled on approach, using a speed-dependent stopping horizon
with a 256 m minimum. Far colliders can remain queued while the car is parked.
Window details wait for the corresponding building shell. Numeric building
preparation can overlap road scheduling; Unity scene and mesh publication stays
on the main thread.

Loading displays counts and a percentage for stages with a measured total.
Other stages show their name, elapsed time and activity instead of an invented
overall percentage. Numerical solves have a time limit; CPU-active notifications
keep them distinct from an idle worker. Identical active requests reuse one job.

Loading draws at roughly 30 frames per second while preparation updates can run
at 120. The clear-only loading camera disables world culling, shadows and HD
screen effects. Failure/cancellation uses a 30 fps update/render budget; driving
restores rendering every update. This policy applies to location changes too.
It does not change the physics timestep.

Finished meshes retain their own generator identity. Changes to mesh generation
invalidate those derived entries once; raw source downloads and player careers
are retained. A completed deferred-building cache entry includes both the
initial nearby buildings and later shells without replacing transport batches.
A distinct transport-only cache entry preserves roads when distant scenery has
not finished. Reloading it schedules the missing buildings again; it never
claims to be a complete scene. Boot prepares nearby buildings within192 m and
continues distant shells after the supported road window is ready.

## Graphics-state captures

Unity 6000.6 records graphics states in Development players. Release players can preload a saved collection. A release verification argument cannot enable tracing.

The checked Mac captures live under `Unity/Assets/StreamingAssets/GraphicsWarmup/OSXPlayer/Metal`. Their `capture.json` records the capture source, engine version, quality, hashes and observed scope. Standard contains 47 states; HD contains 49. Both completed native warmup in the normal release candidate. Other platforms and graphics APIs need their own captures.

To refresh them, build an isolated Development pair with `-earthUnscriptedGraphicsTraceBuild`, exercise the relevant scenes, and quit cleanly. Check the resulting collection API, runtime platform, quality, state counts and file hash before replacing its matching bundled file and receipt. Rebuild normal release players and record `completedWarmupCount` and `isWarmedUp` after the warmup job completes. Loaded input counts alone do not establish completion. Review the full native player log, including messages before application error callbacks register. Captures cover observed raster states; compute and ray-tracing work need separate investigation.

See Unity's [tracing requirements](https://docs.unity.com/en-us/engine/6000.6/manual/materials-and-shaders/shaders/shader-troubleshooting/shader-reduce-stalling/shader-prevent-stutter/shader-pso-trace-warming/shader-pso-trace) and [completion counter](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rendering/graphicsstatecollection/completedwarmupcount).

## Timing overhead

Ordinary beta play keeps a bounded in-memory frame/physical sample history and writes its summary on quit. It skips native profiler counters, heap polling and periodic JSON reports. The on-screen statistics remain available.

An isolated proof keeps detailed timing by default. `-earthUnscriptedDetailedTiming` also requests periodic reports and heap statistics in ordinary beta play. Use `-earthUnscriptedLightweightTiming` for a normal-like isolated comparison: it disables work scopes, native profiler counters, heap-stat calls and periodic writes while retaining wall/simulation frame timing, physical support and final evidence. Explicit lightweight mode takes precedence over detailed mode.

`-earthUnscriptedDisableWorkTelemetry` disables only nested work scopes; other detailed timing remains. Compare the same normal release and prepared-area state with diagnostics on, off and on again before attributing a pause to the enclosing callback. Differences from one pass can reflect worker scheduling, traffic or driver caches.


## Allocation investigation after Beta 15

An offline all-thread readback of the historical Beta 10 Development recording
examined 2,000 frames and 129,878 valid thread-frames. It found 12,489,403
GC.Alloc samples with 4,382,809,516 bytes of allocation metadata. No allocation
call stacks were recorded. The largest group, about 3.35 GB, belongs to unnamed
thread-pool work; it cannot identify parsing, indexing or query snapshots as the
allocating method. A named main-thread coroutine ancestor,
AutomaticBuildingDetails.BuildIndex, accounts for about 293 MB in this recording.
These are historical allocated volumes, not retained heap or current Beta 15 totals.

The largest individual recorded collection marker was 9.37 ms. Collection markers
may overlap or nest across threads; their sum is not a global pause. A slow frame
with a large worker allocation is a correlation, not proof that collection caused
that frame. Current normal-like Beta 15 drives still have occasional 100–209 ms
frames, with no streaming-brake time or loss of physical support.

A separate bounded batch Editor allocation capture returned no recorded frames.
That attempt supplies no allocation evidence. Both investigations left runtime
source unchanged, restored temporary Editor settings and kept raw evidence private.

The next narrow probe is current-player allocation call stacks around worker
preparation and building-detail regeneration. A possible building-detail change
is to reuse inactive matrix-list storage in two generations, without altering
height/clearance queries or published geometry during a coroutine yield. Any such
change needs exact output comparisons, a bound on spare retained capacity, and
normal-player verification. It has not been implemented or accepted as a hitch fix.

## Finished-mesh source dependencies

A finished chunk includes the exact numeric source hashes and present/absent states
for its conservative geometry-query envelope, alongside generator, engine and coast
identities. The envelope covers the terrain cell, mitered road/path/shoulder reach
and every assigned building footprint, including deferred shells. Unusually wide
mapped ways expand it. An extreme footprint declines disk caching rather than
creating an unbounded lookup.

The previous key included all eight adjacent areas for every chunk, even deep
inside a core. Different background-prefetch order could therefore invalidate
otherwise unchanged interior meshes. Boundary and large-building dependencies
remain explicit; this change does not ignore changed source hashes or reuse a mesh
under a different geometry recipe. First use of this revised key rebuilds old
finished entries once; downloaded sources remain available.


## Targeted Development hitch window

For an isolated Development benchmark, `-earthProfilerDriveWindow DELAY_SECONDS MAX_SECONDS` records after the requested driving delay and closes after the window or the first >100 ms driving frame (following five captured seconds). Maximum duration is 120 seconds. It writes `cpu-window.raw` and a small completion record in that benchmark's own report folder. Existing evidence is never overwritten. Normal compiled players and ordinary careers have no capture component.

Offline readback accepts `-earthProfilerSkipSeconds 0` for short captures and optional `-earthProfilerMaxFrames 12000` to include a complete bounded driving window. The output reports actual loaded duration and aggregates coastal refresh steps, so a trailing history limit cannot be mistaken for proof that an earlier stage did no work. Profiled driving and any later overlapping offline analysis remain separate from the normal lightweight FPS baseline. Capture-closing, startup and shutdown frames are not treated as ordinary driving causes.

The coastal refresh now shares its main-thread scan allowance across terrain meshes, including unchanged ones, and caches each renderer's distance once before sorting. Current diagnostics export its phase, processed/total chunks and readiness; the stats bar includes progress while that background refresh is active.
