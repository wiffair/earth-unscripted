# Beta 16 local verification

Beta 16 reduces the memory work involved in loading prepared areas and rebuilding road queries and building details. It is a local test build; public downloads remain Beta 11. The bridge-to-beach terrain repair and camper visibility corrections from Beta 15 are included.

Runtime source: `b909549432669654e6bbf736ad4cd073955bdeb550bcbdf225c5696f0b84fc23`.
Standard/HD gameplay assembly SHA-256: `e7eed6e3617840ee7a70e019900392bf8f40d1765e716eb15bc4901660e87fd3`.
Geometry identity: `earth-geometry3-530a7ec6c110a3db522ae3032cf71c7fa8d54821686347aba289981dc4e96e03`.

## Prepared-area transfer

A three-second Development-player capture identified a 19,007,095-byte main-thread array in DownloadHandler.GetData and a roughly 38 MB worker UTF8 string. Allocation stacks were present for 171,558 of 171,577 samples. These are observed allocating methods; the instrumented frame times are not a performance benchmark.

Numeric areas now download to owned temporary files. A worker verifies their size and SHA-256, counts UTF8 characters with fixed scratch buffers, and fills one exact string before using the existing JsonUtility parser. A second size/hash check protects the decode pass. The helper uses a 16 KiB byte buffer and 16 KiB character buffer rather than another whole-payload array or StringBuilder. The complete UTF16 string is still required by the parser.

Ownership transfers before worker scheduling. Queue failure rolls it back; coroutine disposal cannot unlink an active worker's input; worker completion/failure/cancellation performs cleanup. The focused Mono proof passed 179 checks, including Unicode boundaries, replacement/BOM behavior, integrity failures, native authorized download/abort, and exact text/serialized-map equality for a 19,023,087-byte real map. The full regression run also passed its mandatory smaller fixtures.

The second player capture still observed the roughly 38 MB worker string but no large main-thread download-getter allocation. Its 141,142 stack-bearing samples cover 141,153 allocations. Total metadata volumes in these short windows differ with the work captured; they are not a throughput or frame-rate comparison. The native temporary download directory contained no orphan files after quit.

## Query and window storage

GeoRegion now stores each segment occurrence once and retains ordered numeric cell memberships. Original query order, duplicate occurrences, profiled-width behavior and snapshot ownership remain intact. Existing sharing/view oracles passed 38 and 73 checks; new storage checks passed 304. Actual prepared-area checks passed 2,337. For eight cores, retained value/index payload fell from 26,586,816 to 6,026,288 bytes. A conservative allowance for new cell wrappers leaves a 20,451,664-byte saving. Managed segment stride is 48 bytes, verified with IL sizeof. These are structural capacities, not total heap or GC measurements.

Building-window details reuse only retired matrix lists, leaving the published generation untouched while rebuilding across frames. Inactive storage follows actual demand, with ceilings of 32 MiB and 2,048 groups. Oversized old buffers are pruned after shrinking. The focused proof passed 7,004 checks, including exact original matrices/order/bounds, 51 yield observations, 631 actual backing-array reuses, real mapped data, faults, cancellation, reentry and shutdown.

## Native acceptance and remaining hitch

All ten normal-player checks passed: bridge landing and startup in both renderers, then four Beta 16 physical drives and two installed Beta 15 baselines. Landing checks retained physical/visual pavement clearance and proper grass medians. Complete logs contain no unsupported-shader errors; native graphics warmup completed in both modes.

The first Beta 16 Standard drive had four moving frames over 100 ms, including one 836 ms frame; the first HD drive had two, worst 120 ms. Neither repeated Beta 16 drive nor either warm Beta 15 baseline had a moving frame over 100 ms. Each physical traversal covered approximately 1,118 m without airborne time, damage or streaming braking.

A separate comparison started both Standard versions with empty finished-geometry caches while retaining prepared source data. Both passed with zero moving frames over 100 ms and zero streaming braking. The original cache was parked and restored without deletion; the owner save was unchanged. Driver/Metal shader caches were not cleared. The initial large pause did not recur, so its cause is unproven. Issue #18 remains open; these results do not establish universal hitch elimination.

The full regression suite and bounded repository audit passed. Final players were built after refreshing their runtime/geometry identity and packaged with matching assemblies. The geometry identity changes conservatively because the streaming code changed, so affected finished meshes rebuild once; prepared source data remains reusable. Private diagnostic helpers, raw captures and owner-profile evidence are excluded from the repository and release.

Local replacement is complete. Both installed players passed their startup checks (13.64 s Standard and 13.61 s HD, complete test-process durations rather than player-only startup benchmarks). The owner save remains byte-for-byte unchanged after installation and tests. The previous Beta 15 app is retained as a recoverable backup. Intermediate app registrations were removed and the canonical launcher was registered again. Owner acceptance remains pending.
