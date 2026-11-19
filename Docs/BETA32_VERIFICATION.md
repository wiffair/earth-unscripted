# Beta 32: repeatable performance drives

Local Beta 32. Public Beta 29 and its update feed are unchanged. The complete high-speed drive is under validation; incomplete attempts are retained as failures.

## Running a benchmark

Open Help & About → Benchmark drives. Choose the dense New York city run, the A472 open-road run, the New York tunnel/bridge run, or the long M4/M32 run from Magor to Bristol. The short runs request 1 km. Dense city targets 45 mph; the other short cases target 70 mph, subject to mapped limits, bends and road readiness.

The M4/M32 plan starts on the verified eastbound M4 mainline after the J23A merge, crosses the Prince of Wales Bridge, leaves at J19 and follows the M32 to Newfoundland Way in Bristol. Its horizontal plan is 32.33 km, including about 24.56 km on the M4. This isolated stress test targets **100 mph on the M4 and 70 mph on the M32**, ignoring posted speed limits. Steering, cornering limits and the production road-readiness guard remain active. It does not modify normal driving speed limits. Actual speed exposure is exported: finishing slowly does not qualify as passing.

Long-run coverage requires at least 10 km physically driven above 95 mph on the M4, at least 2 km above 65 mph on the M32, 100 m on a mapped bridge, an actual bridge collider overhead, and published building visuals within 120 m. Reports distinguish source ways and spatial cells from individual structure counts. Deviation, pose jumps, collisions or lost support fail the drive. Only the geographic route is bundled; future road and scenery areas must stream normally. Horizontal routing coordinates never replace streamed road elevations.

Each selection saves the current career, starts a disposable player, runs cold, then starts a fresh player for warm using the same test cache. Source downloads, prepared area data and finished geometry are all isolated from the owner's caches. The cold cache starts empty. Operating-system, graphics-driver and upstream network caches are not cleared. Failed cold drives still attempt the warm phase, but the pair cannot pass or become a comparable performance result.

Keep the computer unlocked for display/FPS comparisons. On Mac the opt-in benchmark requests an idle-sleep assertion; it does not unlock the Mac or change security settings. Close the test window to abort. Normal career data is never restored from an old snapshot. The player's process-held file lease prevents two phases from owning the test cache together, including when a parent process has not yet been reaped.

## Capture and reports

Detailed diagnostics are optional in Display & Audio and on the benchmark page. Leave them off for baseline FPS; enable them when investigating a slow phase. Live traffic is a separate opt-in variable; the scenery baseline excludes it so a traffic queue cannot silently become a scenery throughput measurement.

Open benchmark reports from the same page. Each pair writes cold/warm JSON reports, a comparison and a ZIP in the product's Diagnostics/Benchmarks folder. Reports include whole-drive frame-time histogram percentiles, average FPS, worst frame, startup time, streaming brakes, soft readiness speed-cap time, readiness standstill time, source-stage samples, queue counts, current/worst build slices, finished-cache hits, and the exact source identity, route hash and capture settings. Detailed mode adds native CPU/render/GPU timing where available, startup spans and work scopes. Unavailable timing values are labelled with negative values rather than represented as a measured zero.

Machine information includes CPU, core count, API-reported frequency, RAM, GPU, graphics API, driver information where available, OS/Unity versions, resolution/quality and cache-volume capacity/free space/type/format. Apple GPU drivers are identified by macOS build. Windows uses the system graphics-driver query; it has not been runtime-verified on Windows for this candidate. Disk model, disk speed and hardware serial numbers are not collected. Review exports before sharing; benchmark reports are not uploaded automatically.

Soft cap time means a finite road-readiness speed cap was active; it is separate from the emergency StreamingBrake flag. Standstill time means the cap was below 0.5 mph and actual speed below 0.25 m/s. Samples include the cap, support distance and coverage reason, so intentional coverage waits can be distinguished from a physical obstruction.

Only paired drives that finish successfully, cover the requested distance and match route/settings/source are marked comparable. Diagnostics add overhead. A previous detailed run and a later more-warmed lightweight run are not a controlled measurement of that overhead.

## Geographic source recovery

Public map-server timeouts exposed a cross-border fallback problem during the M4 test. A 238-city geographic shortlist now lets the fallback work with custom/unnamed places as well as city-name hints. The shortlist is approximately 10 KB and is generated from official BBBike boundary metadata. Runtime selection still checks the current catalogue and verified exact polygon; the shortlist cannot assert coverage or supply a caller-controlled URL.

A full source halo can cross country boundaries and exceed a single small extract. The fallback can combine up to three exact country/city coverages while keeping the **combined PBF download within 256 MiB**. It retains original OSM objects and IDs, deduplicates identical copies and rejects conflicting copies rather than splicing geometry. All source receipts are retained. A failure cooldown applies to the requested geographic bounds, rather than disabling every subsequent area in that cache.

City-boundary data in World/Source/city_coverage.py and the route fixture remain ODbL-derived data, outside the original-code MIT grant. Provenance/WorldData/BBBikeCityCoverage.json records the official source URLs and hashes. Tools/refresh_city_coverage.py refreshes the metadata shortlist; it does not download PBFs or build game areas.

## Stable road and railway boundary planning

Profile samples now stay aligned to the complete OSM way, including when each area clips a different planning halo. Previously every clip restarted its two-metre grid. The independently sampled curvature constraints could then disagree with an already published neighbour by less than a millimetre, making the exact solve infeasible and causing repeated whole-area retries. The fix retains source vertices, clip endpoints, physical grade/curvature limits, clearance checks and the existing final seam tolerance.

Owned road profiles also contribute planning samples when a neighbouring core meets only diagonally. This avoids publishing incompatible heights on opposite sides of a short unbuilt span. Preparation farther along the actual driving corridor is no longer cancelled just because a nearer failed area's retry timer expires; off-corridor work can still yield.

The isolated real-data sequence from the M4 start through areas (5,-1) and (5,-2) prepared all eight cores successfully in 117.7 seconds, with downloaded source inputs already cached. The previously failing core (5,-1) took 16.75 seconds; the adjoining core took 20.93 seconds. These are numeric preparation timings, not cold-cache startup or complete rendered-drive measurements. Their maximum shared-edge differences were below 0.05 mm. Three sampling regressions cover overlapping clips, source vertices, reversed ways and the legacy uncropped call.

## Validation status

- Installed local source: 5b254b04538db1f4c162b726f503ec20526887480847703832c191e919e0e518, worker 4b3e14cc8b5ffdc53d0adb794b580a7b77dd44da894493aad7367161f0db51eb. Identical Standard/HD gameplay assembly: 3f5ae3946e9f3b0e7d1514181284c70c95bf9a5861929849f82ff60cae9d432d. Both builds passed their gates, including 17 cache/benchmark-policy checks. The local launcher verified both players, and atomic installation preserved the owner save and retained the previous app.
- All 190 worker tests passed (one skipped). Tests cover source admission, exact coverage gaps, geographic lookup, per-area cooldowns, duplicate/conflicting objects and the combined download cap.
- Both Mac and Windows workers rebuilt from the pinned dependencies. The Mac packaged worker passed authenticated health, anonymous/Origin/Host/traversal denial and rotated-session checks. Windows runtime/driver-query verification is still pending.
- The previously failing M4 source request succeeded using exact BBBike Bristol + Geofabrik Wales coverage: 193,900,096 bytes of source PBFs, 320 extracted objects including two motorway ways. The diagnostic took 21.87 seconds with Wales already cached. This was a source-recovery test, not a fresh-cache whole-drive timing comparison.
- The test cache lease was exercised: a held lock prevented initialization for 12 seconds; releasing it resumed initialization in about 0.11 seconds. The owner's save remained unchanged.
- Earlier traffic-free dense-city cold/warm runs completed at approximately 27 FPS using preceding source 9c7c6b94. They validate the harness, not final-candidate FPS.
- An earlier A472 cold attempt failed during source acquisition; its warm attempt completed. It remains a failed pair.
- The M4 mainline trial on preceding source 8b3c1f32 physically drove about 6.3 km, then stopped safely at a road-source boundary. It was ended after the source failure was diagnosed and the Mac was found locked. It did not complete or qualify as a high-speed/bridge/building coverage pass. The original J23A slip-road seed also exposed nearby geometry warnings; moving the benchmark start does not claim those interchange findings are fixed.
- The preceding installed candidate passed the source boundary at 6.3 km, then reached about 8 km before the numeric profile conflict stopped progress. That drive was aborted, the owner save was unchanged, and no high-speed completion claim is made.
- Final end-to-end M4/M32 coverage, a final unlocked desktop FPS comparison and visible launcher/menu checks remain pending. Source recovery is verified independently; no claim is made that every frame-pacing or geometry issue is fixed.
