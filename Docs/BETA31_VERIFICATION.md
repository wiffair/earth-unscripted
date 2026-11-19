# Beta 31 — local verification, 19 November 2026

Beta 31 extends source preparation ahead of driving and adds separate downloaded-source cache controls. It is installed locally for owner testing. The public release, README, website and update feed remain Beta 29.

## Changes

The source planning distance adapts to vehicle speed and measured preparation time, bounded between 3.072 and 7.68 km. Farther areas are prepared on disk and contribute a small road graph to subsequent forecasts without downloading their full models into Unity. They become resident as the driver approaches. The existing nearby geometry window remains unchanged. Immediate driving coverage continues to outrank route and optional preparation.

A cancelled optional numeric fit previously held the serial preparation worker inside HiGHS: the captured tunnel case wasted another 51 seconds before urgent preparation could begin. Numeric fits now run in a bounded child process using numeric arrays and fixed solver options. Cancellation terminates that child; closing or losing the parent also stops it. Existing physical gradient, tunnel cover, railway clearance and seam constraints are unchanged.

Downloaded sources now default to 5 GB, separately adjustable alongside prepared areas and finished scenery. The menu and driving stats show usage for all three caches. Inventories run in a background task at intervals, rather than scanning directories on the rendering thread. Retention preserves regional source/index groups and keeps 2 GB free where possible. Existing numeric caches remain compatible; the changed gameplay geometry identity can require a one-time mesh rebuild.

## Evidence

- 175 world-source tests passed, with one existing skip; editor gate passed, including six cache-usage checks and 21 source-priority checks.
- The final packaged Mac worker passed session authentication and a real large New York numeric fit in 76.46 seconds. Retained road-only models extended its frontier beyond client-resident areas.
- Cancelling a real fit and serving an urgent cached area took 2.46 seconds. Closing the parent pipe stopped a child solving a 349,804-row matrix in 0.005 seconds. These are measured reproductions, not universal timing guarantees.
- Signed Standard and HD players each completed an isolated 500 m tunnel-boundary drive with initially missing source coverage and conflicting navigation: 500.85 m in 61.27 seconds and 500.74 m in 58.92 seconds. Neither fell, took damage or logged a test failure. Both preserved the owner save hash.
- The storage menu was inspected on screen: three independent 5 GB limits, with used figures. The player was closed normally. The canonical app was atomically replaced and the old Beta 30 retained as a recoverable backup. Launcher registration points at the canonical app.
- Installed Standard startup from an isolated copy of the latest owner save and cache passed in 34.75 seconds; the owner save hash was unchanged and the player exited normally.
- Repository filename/text audit passed. This bounded audit does not prove binary provenance or absence of every possible secret.

## Identity and limits

Version: `0.24.0-beta.31`.
Shared C# source: `c3246448d9c0edae7ba184cf69c003b4fa3dbfd628ed9d390145d7236712e1ae`.
Worker: `0aac3acc035dd17c7d73442f2a379654146507c6231278c356bde9a1af03506a`.
Identical Standard/HD gameplay assembly: `57965088c1d03f7cca764abd09f254fa12ab7af7a1970cf43df60dffe08e62ec`.

Both pinned Mac and Windows source workers were rebuilt. Native acceptance was on macOS; Windows runtime acceptance was not performed here. General hitch and tunnel incidents remain open pending longer owner drives. Large cold fits can still exceed a minute; this change reduces avoidable priority waits and starts bounded work farther ahead. It does not guarantee uninterrupted driving in every region.

A moving vertical collision envelope was discussed as a future optimisation; no new height-band filtering is included in this build. No owner career was restored, credits re-granted, or private vehicle/audio assets published. No source push, public release or website deployment is part of Beta 31.
