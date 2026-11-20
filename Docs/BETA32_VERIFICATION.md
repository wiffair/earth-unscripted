# Beta 32: repeatable performance drives

Local Beta 32. Public Beta 29 and its update feed are unchanged. The final source-warm/generation-cold and warm high-speed pair passed; earlier incomplete or unqualified attempts remain recorded below.

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

When an exact coverage union is already cached, it is now admitted from fresh hash-verified metadata and source files before an online recovery attempt or transient failure cooldown. Cached-only city lookup cannot download. A transient socket timeout retries after 15 seconds; invalid data retains the 180-second backoff, including when its files are cached. Existing source byte caps and exact coverage checks remain unchanged. A real offline lookup for the blocked core found the Bristol, Gloucestershire and Wales union (235,605,167 bytes) in 0.25 seconds. That is coverage admission time; index querying and numeric preparation are additional work.

City-boundary data in World/Source/city_coverage.py and the route fixture remain ODbL-derived data, outside the original-code MIT grant. Provenance/WorldData/BBBikeCityCoverage.json records the official source URLs and hashes. Tools/refresh_city_coverage.py refreshes the metadata shortlist; it does not download PBFs or build game areas.

## Stable road and railway boundary planning

Profile samples now stay aligned to the complete OSM way, including when each area clips a different planning halo. Previously every clip restarted its two-metre grid. The independently sampled curvature constraints could then disagree with an already published neighbour by less than a millimetre, making the exact solve infeasible and causing repeated whole-area retries. The fix retains source vertices, clip endpoints, physical grade/curvature limits, clearance checks and the existing final seam tolerance.

Owned road profiles also contribute planning samples when a neighbouring core meets only diagonally. This avoids publishing incompatible heights on opposite sides of a short unbuilt span. Preparation farther along the actual driving corridor is no longer cancelled just because a nearer failed area's retry timer expires; off-corridor work can still yield.

The isolated real-data sequence from the M4 start through areas (5,-1) and (5,-2) prepared all eight cores successfully in 117.7 seconds, with downloaded source inputs already cached. The previously failing core (5,-1) took 16.75 seconds; the adjoining core took 20.93 seconds. These are numeric preparation timings, not cold-cache startup or complete rendered-drive measurements. Their maximum shared-edge differences were below 0.05 mm. Three sampling regressions cover overlapping clips, source vertices, reversed ways and the legacy uncropped call.

## Geometry throughput and roadside signs

Driving geometry work now shares one cumulative 2 ms frame allowance; loading retains its 12 ms allowance. Cheap collider attachments can share a frame instead of each forcing another whole frame. A chunk still activates only after every required collider bake is complete.

The long-drive controller now uses the existing local road spatial index for its horizontal route projection. It preserves source road heights, legal direction and road-class ranking. This removes repeated full-region scans from the test harness; it is not, by itself, evidence of an improvement to every normal driving callback.

A sign belonging to a bridge or underpass retains its associated road floor when terrain is on a different level. Previously a final terrain snap could lift a valid lower-road sign onto the motorway above. Actual sign creation and collider bounds are covered by the Editor checks.

## Optional scenery and connected tunnel entrances

Malformed optional building, woodland and water multipolygons are checked with the same area parser as the builder. A broken relation is omitted with its identity and source hashes recorded; valid road and railway ways are retained. This also sanitises older cached regional responses without redownloading them. A real university relation containing an inner courtyard and no outer ring had blocked the M32 area at about 25.8 km. The corrected installed worker passed that point during the subsequent native drive.

The same drive reached 31.9 km before a different Bristol profile conflict. A covered street and its directly connected underground garage entrance were incorrectly treated as stacked tunnels just beyond their shared OSM node. The candidate recognises the connected throat only when original node evidence exists, bounds the exception by travel along the complete source ways and the vertical-transition envelope, and preserves clearance at disconnected tunnels and remote crossings in loops. The real three-core reproduction completed with shared-edge differences below 0.05 mm. It was a cached-source preparation check, not a rendered high-speed drive; final-candidate native coverage remains under test.

## Source indexing and retries

The PBF importer now uses the existing native library to filter node callbacks. All node locations are still cached before filtering, so untagged coordinates referenced by roads and relation members remain available. Only node tags used by place, railway-furniture and traffic-control importers reach Python. Way and relation admission is unchanged.

On the same already available 151,365,572-byte Wales PBF, fresh spatial indexing took 63.62 seconds with the prior callbacks and 39.85 seconds with native filtering. Extracted JSON and all semantic rows across all six index tables had identical SHA-256 digests. This is about 37% less indexing time. OS caches were not cleared, and the result is not a whole-game startup or FPS comparison.

A fresh, verified complete country extract can now supply a new local index before any online map-host probe. A failed alternative coverage lookup preserves the original full-source timeout instead of converting it into a coverage error with a long backoff. Network-only backoffs do not prevent valid cached sources from being used; invalid data retains its longer rejection period. Startup retries honour their existing source-failure deadline rather than exhausting retries while the provider is cooling down.

## Regional source cache and LAN reuse

The automatic world worker now prefers complete supported regional OSM extracts before public query servers. It keeps these downloads and their local indexes in `OpenStreetMap`, separate from terrain/LiDAR in `Raw`, with an adjustable **5 GB default** for each. Existing map-source cache files move to the new directory; terrain and prepared areas stay in place. A complete extract can supply new supported geographic features without another PBF download. Extracts larger than the existing 256 MiB admission cap, unsupported coverage and failed sources still use bounded query fallbacks.

After acquiring the starting region, one low-priority worker considers nearby regional leaves and exact city extract polygons within about 50 km. It downloads source files, rather than building every road in those regions. Foreground source IO preempts it; cache quotas and device free-space reserves can defer it. Download progress shows bytes/percentage when supplied by the server. Strong-ETag checkpoints retain verified partial downloads across transient failures. A real official Wales Range/If-Range check resumed the last 65,536 bytes of a 151,365,572-byte source and reproduced the full expected SHA-256; it was not a fresh-download timing test.

Sharing remains tied to an existing connected LAN game. The single-player beta does not enable independent discovery or LAN play. Authenticated peers may offer up to three fresh public OSM PBFs, each capped at 256 MiB. The signed offer binds its official source URL, size, hash, licence and timestamp. A received source needs a complete matching receipt before the worker admits it; it is then indexed locally. SQLite indexes, executable workers and prepared physics models are not exchanged through this source channel. Legacy world/terrain transfer limits remain 32 MiB. Prepared-world compatibility includes both geometry and worker recipe identities.

The final runtime proof transferred/resumed a 35,587,246-byte valid PBF through authenticated loopback TCP, then the production worker indexed it and found its expected road with zero internet requests. Seventeen policy checks cover source limits, URL/signature rejection, incomplete/stale receipts and recipe compatibility. This verifies the protocol and consumer on one machine; two-machine throughput has not been measured.

## Validation status

- Local candidate source: `ab197f8b746c3828d9c9872614c45bc8ca4d66b463768f0e5a1d4e6eb22c6235`; worker `f6a125e3fa214e6fd1d57097cc4b7cdb02c7f5446a3741b6b9bf2d8386a9f91b`. Installed with matching Standard/HD gameplay assembly `b48897213edb8c9a51c5644ebb3f578be3c3379561c7eebda550a8ffdaa6b527`. Both builds passed their required gates, including 205 cache/benchmark/sign-floor/source-retry checks and 17 peer-source policy checks. Signed app and launcher checks passed; atomic installation preserved the owner save. The earlier unfocused warm phase remains an invalid performance comparison. Final native verification of this candidate is in progress.
- All 240 worker tests passed (one skipped). Tests cover source admission, exact coverage gaps, geographic lookup, per-area cooldowns, duplicate/conflicting objects and the combined download cap.
- Both Mac and Windows workers rebuilt from the pinned dependencies. The Mac packaged worker passed authenticated health, anonymous/Origin/Host/traversal denial and rotated-session checks. Windows runtime/driver-query verification is still pending.
- The previously failing M4 source request succeeded using exact BBBike Bristol + Geofabrik Wales coverage: 193,900,096 bytes of source PBFs, 320 extracted objects including two motorway ways. The diagnostic took 21.87 seconds with Wales already cached. This was a source-recovery test, not a fresh-cache whole-drive timing comparison.
- The test cache lease was exercised: a held lock prevented initialization for 12 seconds; releasing it resumed initialization in about 0.11 seconds. The owner's save remained unchanged.
- Earlier traffic-free dense-city cold/warm runs completed at approximately 27 FPS using preceding source 9c7c6b94. They validate the harness, not final-candidate FPS.
- An earlier A472 cold attempt failed during source acquisition; its warm attempt completed. It remains a failed pair.
- The M4 mainline trial on preceding source 8b3c1f32 physically drove about 6.3 km, then stopped safely at a road-source boundary. It was ended after the source failure was diagnosed and the Mac was found locked. It did not complete or qualify as a high-speed/bridge/building coverage pass. The original J23A slip-road seed also exposed nearby geometry warnings; moving the benchmark start does not claim those interchange findings are fixed.
- The preceding installed candidate passed the source boundary at 6.3 km, then reached about 8 km before the numeric profile conflict stopped progress. That drive was aborted, the owner save was unchanged, and no high-speed completion claim is made.
- The unlocked diagnostic drive on the first installed sampling fix passed both earlier failure points and reached at least 9.6 km. It exposed a transient source timeout/cooldown delay and was stopped to install cache-first recovery. It remains an incomplete diagnostic, not a completed cold/warm pair; the owner save was unchanged.
- Final end-to-end M4/M32 coverage, a final unlocked desktop FPS comparison and visible launcher/menu checks remain pending. Source recovery is verified independently; no claim is made that every frame-pacing or geometry issue is fixed.

The preceding cold/warm pair is incomplete: cold failed during initial source acquisition; the warm retry drove 31.9 km but stopped at the Bristol constraint conflict described above. It was closed for correction. This is neither a qualified pass nor a comparable cold/warm performance result. The owner save remained unchanged.

## Cache promotion and benchmark focus

Near a bend or junction, distance along a road can exceed the distance to a core boundary. Scheduling and promotion into resident memory now use the same core-distance rule. Previously they could repeatedly request an already prepared core as cache-only, postponing its admission for geometry. Forecast requests also use the selected integer tile's centre, avoiding a floating-point/JSON edge coordinate selecting the adjacent core; the actual car start and road elevations remain unchanged.

Regional indexing now replaces completed download progress with the indexing phase and processed-object count. It does not invent a total or percentage for that phase.

Benchmark captures count focused and unfocused driving frames/time and include actual render interval, display dimensions, vSync and frame cap in comparison settings. Captures with substantial background time are not marked comparable. On Mac the explicitly launched benchmark requests activation once and cooperatively hands activation to the next phase; it does not reclaim focus after a user switches away.

The updated A472 short cold/warm pair passed with matching route/settings and fully focused phases, without manual window selection. Cold startup took 263.61 seconds, including a slow fresh regional download, indexing and preparation of the four cores around the start. Warm startup took 24.16 seconds. Both drove approximately 1 km in 112 seconds with mapped limits/bends active, no streaming brake and no road-readiness cap or stop. Average FPS was 85.56 cold and 82.71 warm; p95 frame bins were 15/21 ms and worst frames 51.9/107.8 ms. The warm drive used 321 finished-geometry cache hits. These are one-device results, not a general loading-time guarantee.

The preceding full M4/M32 cold drive completed and qualified, including 11.83 km physically at least 95 mph on the M4, 3.94 km at least 65 mph on the M32, bridges overhead and below, and roadside buildings. It still stopped for readiness for 324.4 seconds. Its warm repeat completed the distance but failed the required speed exposure, so the pair is not a passing/comparable result. Window focus was not recorded in that preceding build; the 70.18 versus 31.87 FPS difference is not attributed to caching or game code. A new full pair is running with the focus and source-promotion fixes.

A real cached-source regeneration was also checked for a proposed unchanged-fit shortcut. The initial and neighbour-fit targets still violated their exact constraint sets; the shortcut was not added. Existing gradient, clearance and seam checks remain active.

## First-visit, generation-cold and fully warm results

These are different cache states. The menu's original cold test clears downloaded sources as well as generated data, so it measures a first visit and includes internet variability. A generation-cold comparison retains downloaded OSM, terrain/LiDAR and fixed source indexes, while starting with empty prepared-area and finished-mesh caches. A fully warm repeat retains both. The development comparison records this policy and the actual starting byte counts in its exported `cacheBefore` data; it does not label a source-filled cache as wholly empty.

On the source-promotion candidate b6e20bef, the complete 32.3 km first-visit/warm pair passed its strict physical, high-speed, route/settings and focus checks. Both phases stayed focused without manual window selection. First-visit startup took 166.78 seconds and driving took 1,290.71 seconds; average FPS was 75.55, p95/p99 frame bins 22/26 ms, and the worst frame 173.90 ms. It still included 315.68 seconds fully stopped for road readiness. The warm repeat started in 10.57 seconds and drove in 839.24 seconds; average FPS was 65.86, p95/p99 24/29 ms, and the worst frame 318.09 ms. It had zero full readiness stops, 4.23 seconds of streaming braking and 1,000 finished-geometry cache hits. Eight warm frames exceeded 100 ms and none exceeded 333 ms. This validates the route and cache/promotion fixes; it does not claim all frame hitches or first-visit waits are eliminated.

Near the first-visit 20 km wait, the required Gloucestershire OSM data was already cached. Four Environment Agency DTM/DSM archives totalled about 175 MB and took about 13 seconds to download. The nearby-region OSM prefetch paused before those transfers and resumed afterwards; it was not competing for bandwidth during the LiDAR transfers. Reading rasters, extracting features and fitting/joining profiles accounted for further preparation time.

## Reusing decoded elevation within a job

One preparation job previously decoded the same DTM/DSM inputs again for its final post-anchor waterway check. The storage worker now retains its sampler within that job, restores the original factory and releases arrays on success or failure, and creates a fresh sampler for the next area's inputs. This changes neither the numeric generator identity nor its constraints, so existing compatible numeric areas remain reusable.

A real UK core was regenerated in original/reuse/reuse/original order with sources cached and no game/rendering load. Median preparation fell from 18.73 to 16.85 seconds, about 10%. Raster decoding happened twice in each original run and once in each reuse run. All four complete 16,133,877-byte prepared outputs had identical SHA-256 d0acb20a3c646b479e8299c1e3d6eef9e063530480a40f60cba4544adb62e0f9, with 49 seam checks and a maximum difference below 0.05 mm. This is a numeric preparation measurement for one core, not a whole-game startup or FPS claim. Lifecycle tests cover fresh inputs in the next job, cache hits that allocate no raster, and failure cleanup.


## Cached-source generation run and cancellation diagnosis

Installed sampler candidate `93e9a8f2` completed the 32.3 km generation-cold drive with strict high-speed/bridge/building qualification. Downloaded source folders and fixed source indexes started with 4,850,309,987 bytes; prepared areas and finished geometry started empty. Startup took 15.71 seconds and driving 1,110.22 seconds. Average FPS was 74.77, p95/p99 bins 22/26 ms, worst frame 233.46 ms, full readiness stops 189.63 seconds, streaming braking 106.60 seconds, and finished-cache hits zero. All measured driving time was focused. The matching warm repeat completed physically, with zero full readiness stops and no runtime errors, but only 7.80 km at least 95 mph on the M4. It was unfocused for 999.34 of 1,058.79 driving seconds and failed both focus and speed qualification; its 33.18 FPS is not a comparable display measurement. This is a different build and cache policy from the first-visit result above, so the difference is not a controlled measurement of the sampler change alone.

Source retention did not cover every optional surrounding input: seven new source receipts totalled 76,075,060 bytes, mostly one LiDAR DSM raster. The worker completed 29 new prepared areas, totalling 637.70 seconds of numeric work and 26.87 seconds of acquisition. The dense final Bristol core took 79.41 seconds, of which 2.59 seconds was acquisition. These totals can overlap driving and do not add directly to time stopped.

The same recording contains 44 superseded source jobs. Near the diagonal transition from core `(12,-7)` through `(11,-7)` to `(11,-8)`, the earlier needed core was repeatedly cancelled and restarted for about a minute before its 38.95-second preparation finished. The physical halo forecast temporarily skipped the short intervening core while route preparation kept selecting it again. The first route-order correction was insufficient: both selection and ordering inherited a 64-metre sampling gap. The real route crosses `(11,-7)` for only 44.79 metres, so a moving sample phase could omit it. The current candidate traverses every crossed source-grid core and computes padded route/cell intersections analytically. It preserves the earlier pending core when both choices lie ahead on the nearby active route. Leaving the route, choosing an off-route core, or finding a genuinely earlier missing core can still supersede work. Regression coverage includes 64 starting phases of the real crossing, both selection and preemption, lane-width overlap, off-route choices, negative coordinates and repeated route vertices. Tracked in [issue 62](https://github.com/wiffair/earth-unscripted/issues/62).


## Dense building height reuse

A current real-data cProfile reproduction of Bristol core `(10,-10)` made 185,061 elevation sampling calls. Building extraction repeatedly sampled the same centre for each footprint and courtyard vertex. The change now computes that building's rounded ground height once and reuses it for those vertices. Road profiles, building footprints and heights, physical constraints and neighbour fitting are unchanged.

In a controlled original/changed/changed/original run with sources cached and HTTP forbidden, median preparation fell from 67.52 to 62.84 seconds (6.93%). Each run produced the identical complete 43,538,319-byte output, SHA-256 `96ded8b99f97b7949fa4efa8dfa669db9af96fb249d2f4e304efa73a21e46d23`, with 18,344 buildings and 43 seam checks (maximum difference 0.038 mm). The production AST matches that tested transformation. This is a one-core preparation result, not an FPS or whole-route speedup. Numeric generator identity advances conservatively for the source change; downloaded inputs remain reusable.

The installed candidate adds map-index rebuild elapsed timing and detailed per-thread allocation reporting. This worker time was absent from the main geometry-slice figure. Timing can overlap other work and is diagnostic evidence rather than a claim about the cause of a hitch.


## Exact diagonal-core diagnostic

An isolated 1.5 km physical drive began at station 26.8 km, with downloaded sources and other prepared cores retained but `(11,-7)` absent from prepared admission. That core was requested once and became ready after 37.54 seconds; it was not cancelled or restarted. The car completed 1,500.01 metres without damage, airborne time or runtime errors. It still spent 9.23 seconds stopped for readiness, so this narrow result does not establish interruption-free generation.

This diagnostic intentionally cannot qualify as the full endurance test: its report retains the failed full-route speed/bridge coverage gate. It ran with detailed instrumentation, averaged 84.87 FPS and recorded a 49.53 ms worst frame. These values are not a controlled comparison against a different route or lightweight capture. A complete source-warm/generation-cold M4/M32 pair is being checked separately.

## Shallow tunnel cover investigation

A diagnostic start near Eastville Roundabout found missing soil below published
ground-road asphalt. The same failure reproduced in the unchanged installed
candidate. A pedestrian subway was low enough to trigger the portal excavation
allowance even though its required headroom was already clear. The candidate now
keeps terrain above the required ceiling plus a 10 cm skin, while still excavating
terrain that enters that envelope. It does not reduce tunnel headroom. Actual
terrain/collider regressions cover walking, road and rail tunnels, overlying road
support and genuinely low portals. The real-data 2 km native check is recorded below.


## Resumed loading and terrain investigation

The preceding fully focused 32.3 km pair on source `4a369e52` passed strict cold-generation coverage but exposed a real warm-phase performance regression. Cold startup was 15.98 seconds, the drive took 1,000.93 seconds at 67.99 FPS, and readiness standstill totalled 92.79 seconds. Warm startup was 7.70 seconds, but the drive took 1,324.36 seconds at 26.60 FPS with 414.29 seconds of streaming braking. It failed the required high-speed exposure despite completing physically and remaining focused throughout. The pair is not comparable as a passing endurance result. This slowdown is not attributed to window focus.

The current local candidate starts one bounded next-core preparation after the current numeric result is ready, while Unity reads and indexes that result. It retains the worker's single serial numeric executor and the atomic road/collider publication guard. A real turn can supersede the optional request. Cache dependency identity now includes the conservative footprint actually queried by a chunk and its buildings, retaining exact relevant present/absent source hashes. Unrelated adjacent-core residency no longer invalidates interior meshes. Cache checks include actual height ownership, edge dependencies, large footprints and source revisions; all 205 world-cache and 90 finished-cache checks pass.

A separate real-data check at Eastville found that terrain excavation could remove road support above a shallow pedestrian subway. A distant higher segment on the same sloping entrance also incorrectly widened that excavation. The correction preserves already clear tunnel cover and uses the nearest segment of the source corridor. Three shallow-cover and four sloping-entrance collider checks pass, while genuine portals still excavate and required road, path and rail headroom remains enforced. The five originally failing real road points now have supporting terrain about 0.20 metres below asphalt, rather than over 3 metres below it.

The Development player physically drove 2,001 metres through this area with zero damage, airborne time or runtime errors. It passed the initial junction support gate and reused its one ahead-preparation ticket. This deliberately short diagnostic does not qualify as a full endurance pass. Its profiler-enabled startup (48.78 seconds), driving time (78.65 seconds) and FPS (61.15) include tracing overhead and are not normal-build performance measurements. The retained trace's last 2,000 frames cover 32.34 seconds; its largest frame is shutdown/report generation and is excluded from driving-cause conclusions.

The regular Standard/HD candidate is installed locally with matching gameplay assemblies and unchanged owner save. A fresh source-warm/generation-cold and warm 32.3 km pair is in progress. Public Beta 29, GitHub releases and the website feed remain unchanged.


### Completed overlap/cache-footprint comparison

On installed source `1d2976f1`, the fresh 32.3 km generation-cold and fully warm pair both passed strict physical/high-speed coverage and the unchanged route, settings and focus comparability gates. Both had zero damage, airborne time, runtime errors or pose jumps. Cold startup was 16.19 seconds, driving 913.68 seconds, average 64.06 FPS, p95/p99 bins 24/29 ms and worst 355.02 ms. Readiness standstill was 37.56 seconds and streaming braking 51.40 seconds. Twelve optional ahead requests were started and all twelve reused, with none discarded.

Warm startup was 7.84 seconds, driving 837.56 seconds, average 55.12 FPS, p95/p99 bins 29/55 ms and worst 271.61 ms. There were zero readiness standstill seconds, 0.43 seconds of streaming braking, and 1,405 finished-cache hits. Focus was qualified by the existing threshold: 831.66 focused and 5.90 unfocused seconds. It physically covered 16.95 km at least 95 mph on the M4 and 7.18 km at least 65 mph on the M32. Warm had seven frames over 100 ms and none over 333 ms; cold had fourteen over 100 ms, including one over 333 ms. Occasional hitches therefore remain an explicit investigation item.

Relative to the preceding `4a369e52` run, observed cold standstill fell from 92.79 to 37.56 seconds and streaming braking from 91.67 to 51.40 seconds; warm speed qualification recovered, with average FPS 55.12 instead of 26.60. These multi-change, one-device runs also retained slightly different optional source inputs, so they are not a controlled attribution to one individual change. The new pair itself is comparable and passed. Its first phase retained 5,477,075,350 bytes of downloaded sources and fixed indexes, with no prepared areas or finished geometry.


### Driving hitch traced to cumulative coastal scans

A bounded Development capture retained an actual 134.23 ms driving frame. Its main-thread hierarchy attributed 111.96 ms to `RefreshCoastalBeds`; another coastal step took 53.03 ms. This was separate from the completed normal-build comparison. The routine reset its stopwatch per terrain mesh, allowing many individually cheap unchanged meshes to run together. Its priority comparator also repeatedly called native hierarchy getters.

The correction shares a 1.5 ms sampling/scan budget across meshes, includes unchanged and retired meshes, and bounds cheap work by count. Renderer distances are read once before numeric sorting. Terrain calculations, nearest-first priority, old collision support during cooking, preview ownership and final collider publication remain intact. A 64-mesh emitted-object regression verifies real scheduling yields, nearest-first restoration, unchanged mesh/collider identities and completion; the previous retirement and coastal land/island tests still pass.

The repeated 7 km diagnostic captured the complete 120-second window (8,283 frames). It contained 151 coastal-refresh steps of at least 0.01 ms; the maximum was 1.54 ms and their summed CPU work was 218.14 ms. The work was spread across frames, not removed. No captured frame exceeded 100 ms; the whole diagnostic's worst driving frame was 67.87 ms. It completed 7 km with zero readiness standstill. These profiler-enabled timings are diagnostic evidence for the identified operation, not a replacement normal-build FPS baseline. Both disposable runs preserved the owner save. Offline trace analysis began only after each recording closed; later diagnostic driving was not used for baseline comparison.

The offline reader can explicitly request up to 12,000 frames so the full bounded capture is retained; it reports requested/loaded frame counts, loaded duration and the coastal aggregate. Its default remains 2,000 frames. The recorder is Development-only, requires an explicit isolated-benchmark flag, caps a window at 120 seconds, and creates no component in normal careers or release players.


### Final dense-city check and planned-route overlap

On the installed coastal-fix build `8df6b493`, the New York 1 km cold-generation/warm pair passed its physical, matching-route/settings and focus gates. Cold startup was 182.84 seconds and driving 78.34 seconds at 83.18 FPS (p95/p99 bins20/22 ms; worst44.84 ms). Warm startup was24.10 seconds and driving78.31 seconds at89.08 FPS (p95/p99 bins13/15 ms; worst39.83 ms), with170 finished-cache hits. Neither phase had a road-readiness cap, streaming brake or readiness standstill. Initial city source preparation remains much slower than a cached start; this result does not claim instant first-visit loading.

The current `ab197f8b` candidate also allows its one optional overlap ticket to start the next already forecast planned-route cache core beyond residency, within the existing speed-based planning horizon. Previously Unity could spend several seconds indexing a ready core while the numeric worker waited, even though that farther route work would be requested next. The change adds no numeric executor, speculative queue, wider initial window or new forecast area. A real turn still preempts the ticket. Without an active nearby route, overlap retains its original nearby-only bound. Regression checks reject off-route side cores, inactive routes and beyond-horizon work. The final full M4/M32 comparison on this candidate passed; its complete results follow.


### Final installed motorway pair

Installed source `ab197f8b` and matching Standard/HD assembly `b4889721` passed the full M4/M32 generation-cold and warm pair. Both runs passed the existing route, settings, focus and physical high-speed gates. Both were focused throughout, with zero damage, airborne time, runtime errors or pose jumps. The test retained 5,477,455,505 bytes of downloaded source data and fixed indexes before the first phase; prepared areas and finished geometry started empty. These are source-warm generation results, not first-time network download timings.

| Measurement | Generation cold | Warm repeat |
| --- | ---: | ---: |
| Startup | 15.98 s | 7.61 s |
| Driving time | 901.46 s | 837.66 s |
| Average FPS | 64.60 | 58.01 |
| p95 / p99 frame bins | 24 / 29 ms | 25 / 32 ms |
| Worst driving frame | 158.79 ms | 115.32 ms |
| Frames above 100 ms / 333 ms | 8 / 0 | 3 / 0 |
| Readiness standstill | 29.26 s | 0.00 s |
| Streaming braking | 43.54 s | 0.54 s |
| Finished geometry hits | 0 | 1,424 |

Cold physically covered 17.05 km at least 95 mph on the M4 and 5.71 km at least 65 mph on the M32. Warm covered 16.92 km and 7.17 km respectively. The cold pass started 24 optional next-core tickets, reused 22 and superseded two; warm reused all six tickets it started. No additional numeric executor was introduced. Relative to the preceding `4a369e52` run, cold standstill fell from 92.79 to 29.26 seconds, while the warm speed-qualified pass recovered from the prior 26.60 FPS/failed-speed result. These are multi-change observations on one machine, not a controlled attribution to one individual change.

The improved cold pass still waited in dense Bristol, and occasional driving hitches remain. Dense New York first preparation also remains around three minutes in the separate city check; a cached start was 24.10 seconds. This candidate improves loading and frame pacing without claiming uninterrupted first visits everywhere. Full private receipts are retained under benchmark case `60c23cbd17964958a2c156cf5e84c0e5`; they are not uploaded automatically.


### Final short tunnel diagnostic limitation

On final source `ab197f8b`, the New York tunnel/bridge selection completed only the available 299.88-metre guide (about 275.38 metres physically driven), although the benchmark requested 1 km. Both native phases completed that short path with no runtime errors, damage, airborne time or readiness standstill. Cold/warm startup was 182.52/19.59 seconds, driving 21.00/21.00 seconds, and worst frames 49.49/58.18 ms. These are short diagnostic observations, not a passing 1 km benchmark. `requestedDistanceCompleted` is false in both receipts and the comparison correctly remains `comparable: false`, despite each underlying short physical check completing. No acceptance threshold was lowered. Extending the tunnel fixture to a verified complete 1 km route remains a follow-up. Case `581d7c432cbb4767bae4d66d85a64ba8` retains the complete failed-distance receipts.


### Final installed HD diagnostic and handover

The installed HD player on source `ab197f8b` physically drove the requested 1,500.06-metre route segment from M4 station 1 km. It had zero runtime errors, damage, airborne time, readiness caps, readiness standstill or streaming braking; 269 finished chunks were reused. Startup was 13.16 seconds and driving 43.79 seconds at 67.30 FPS, with p95/p99 bins 23/27 ms and a worst driving frame of 60.20 ms. This was a short detailed diagnostic, not a Standard/HD performance comparison. The full-endurance speed/building/over-and-under-bridge gate remains deliberately failed for this short segment; the nonzero player exit is retained rather than represented as a full-route acceptance pass. Case `1f822d50d1c54136941e76fe31c9a12b` retains the report.

All 670 runtime source pins match the installed source identity. Standard and HD have identical gameplay assembly hashes. The installed chooser's `--check` passed and its branded Standard/HD selection screen was inspected on the unlocked desktop, then closed. Disposable checks preserved the owner's save byte-for-byte. Repository hygiene passed its bounded 1,574-file text/filename scan; this does not assert a complete security or provenance audit. Redundant development builds were removed after the timing runs, retaining source, diagnostics, unique profiler traces, saves, caches, the current installed/raw build and two rollback copies. Public Beta 29 and all remote releases, website files and update feeds remain unchanged.
