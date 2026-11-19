# Beta 15 local verification

Beta 15 combines the global terrain/asphalt repair and camper visibility work with shared road-index storage, functioning Mac graphics warmup, compatible HD surface fallbacks and lighter normal-play diagnostics. It is a local owner-test build. Public downloads remain Beta 11.

Source: `98c3855d5e262c71465fceb9fe644b47853527582adad1cf34f9c892627494e0`.
Standard/HD gameplay assembly SHA-256: `2346bf0b22d219c717816d5fcc9674176dc40d5ef3b25144442e9b2e28b9c920`.

## Shared road index

Each map publishes one immutable segment array. Grid cells retain ordered numeric positions into it; exceptional halo cells keep independent value lists. Source grouping, null/empty behavior, ordering and query results are preserved. Capacity totals are computed during construction, so reporting them scans maps rather than every cell.

The focused independent original-query comparison passed 61,707 checks, covering actual road/ground/bridge/path/tunnel/building-clearance queries, controls, bounds, offsets, duplicates, fallback behavior and concurrent independent snapshots. An additional four-real-core comparison passed 82,486 checks. Actual Unity Mono managed segment size is 72 bytes, measured using IL sizeof rather than marshaling.

Across those four prepared cores, segment-array payload fell from 89,451,072 to 12,934,648 bytes. The native route retained 170,152 unique records for 1,337,699 memberships, with 1,878,508 numeric capacity slots: 19,764,976 bytes of shared values/indices versus the equivalent prior value-array payload of 135,252,576 bytes. These calculations exclude object headers and small view objects; they are not total managed-heap measurements. The legacy QueryIndexSegmentCopies field remains a membership count; separate unique-record and capacity counters describe the new representation.

The complete regression suite passed, including the legacy bounded-index fixture updated to its read-only cell contract, then both players built successfully. Existing physics, traffic, terrain, radio, controller, streaming and camera checks remain intact. Source audit passed.

## Native acceptance

Seven checks passed: landing and startup in both renderers, then three normal-like physical drives. Both landing proofs inspected 3,426 actual pavement samples with no missing terrain or grass above asphalt; the original defect remains 12 cm below the road. Visual and collision meshes match, and the genuine median/verges remain. Road-edge/overview images were inspected in Standard and HD.

Both players completed their graphics warmup (47 Standard / 49 HD states, native isWarmedUp true). Complete early player logs contain no unsupported-shader errors. Startup test-process durations were 27.71 s on the first new-geometry preparation and 13.62 s with reuse; these include proof overhead and are not player-only startup timings. The changed index source conservatively changes the finished-geometry identity, so this update rebuilds affected finished meshes once. Prepared source data remains reusable.

Each physical drive covered approximately 1,118 m with no airborne time, damage or streaming-brake time. In-motion frames over 100 ms were Standard 2 (worst118.8 ms), HD1 (148.5 ms), repeat Standard2 (209.3 ms). No frame exceeded333 ms. The comparisons confirm profiler counters, work-scope events, heap polling and periodic diagnostic writes were disabled. Memory savings did not eliminate every short pause; issue #18 remains open. These results do not claim hitch elimination or a measured frame-rate improvement.

The canonical local app is installed. Both installed Standard/HD startup checks passed (14.14 s and 13.62 s complete test-process durations). The owner save remains byte-for-byte unchanged after installation and tests. Intermediate players and old backups were unregistered from the launch menu; the current canonical app was registered again. Owner acceptance remains pending. Private proof profiles and truck-reference media are not part of this release.


## Follow-up diagnostics

An offline all-thread analysis of a historical Beta 10 Development recording
identified allocation-heavy worker work and building-detail regeneration, with no
captured allocation call stacks. Its collection markers do not establish the
cause of current Beta 15 hitches. A separate batch capture returned no frames and
was rejected as evidence. No runtime or installed-player changes followed these
checks. See PERFORMANCE_CAPTURE.md for the scope and next probe.
