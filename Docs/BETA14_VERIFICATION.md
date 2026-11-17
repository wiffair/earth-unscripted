# Beta 14 verified local candidate

This candidate adds working Mac graphics-state preloading, compatible HD surface fallbacks and lighter normal-play timing collection. The installed owner beta remains 13 while the remaining frame pauses are investigated; nothing from Beta 14 has been published.

Final candidate source: `72ac68c53b3253d7959d86a5636baa24c40bdeff00306573a8ad7335698a11a9`.
Standard/HD gameplay assembly SHA-256: `32de11ac4236b539833deb05b241fbeb03d797c32eb04692359e69d5ef41be52`.
The geometry identity remains the Beta 13 identity, so corrected finished meshes remain reusable.

## Accepted checks

The complete paired-player regression suite passes, including 42 graphics-loading checks, 61 shader-compatibility checks and six timing-policy checks, plus all prior terrain/traffic/radio/controller/camera checks.

Two isolated Development captures produced checked Metal/OSXPlayer collections: Standard 412,332 bytes and HD 535,016 bytes. The committed capture receipt records their hashes, source, engine version, quality and observed route. No owner or temporary filesystem paths were found in the capture data. The loader scopes by platform/API/mode, rejects incompatible or empty data, bounds file size and JSON depth, and falls back from damaged local data without native parse errors. Release players do not pretend to trace new states.

Normal-player native startup checks passed: Standard completed all 47 input states and HD all 49, with native `isWarmedUp=true` in each. Complete test-process durations were 15.15 s and 14.13 s. Full player logs contain no unsupported-shader errors. The two Standard-only surface shaders now have HDRP/Lit fallbacks; their original names/properties and existing HDSharedLit mode adaptation remain intact. Standard and HD driving images were compared with the previous build; the final scene appearance is preserved.

Ordinary beta play now skips native diagnostic counters, heap polling and periodic timing JSON writes. A bounded in-memory physical/frame history is flushed on quit. Deliberately requested detailed tests retain their diagnostics. On-screen statistics remain available.

Three normal-like physical drives passed, each covering approximately 1,118 m with no falls, damage or streaming-brake time. Standard recorded one in-motion frame above 100 ms (worst 131.7 ms), HD two (120.3 ms), and a Standard repeat two (118.4 ms). The final receipts confirm no native profiler counters, work-scope events or heap polling in these comparisons. Warmup completed in every case. No frame exceeded 333 ms.

## Remaining work

Brief pauses remain in issue #18. Earlier scope-only on/off/on comparisons did not establish diagnostic work as their sole cause. Graphics warmup is functional, but these runs do not establish that it eliminates the pauses. A separate bounded road-index memory change is under investigation. Owner installation/acceptance of Beta 14 is deferred while that work proceeds; Beta 13 already contains the verified global road/grass repair.
