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
braking or loss of support, but brief frame hitches remained. Continue with an
unlocked desktop release-player run and an audio listening check; track results
in [issue 18](https://github.com/wiffair/earth-unscripted/issues/18).

Keep raw captures and owner-profile evidence out of the repository. The captures
can be large and can contain names, paths and precise game locations.
