# Beta 13 local verification

Beta 13 repairs terrain intruding through roads using emitted asphalt footprints. It also reduces copied road-query data. This is a local test build; public downloads remain Beta 11.

Source: `e91bfaae9d03323f4016a69234b98a239c10d50f31f215580f47becb9f1a74d6`.
Standard and HD gameplay assembly SHA-256: `057d6a06bcf964ab9d05434e6823a1142d03c3663a90fdd38b68b2294979f2f9`.

## Terrain repair

Only terrain pieces overlapping emitted, non-tunnel asphalt and entering the top 2 cm are recessed to 12 cm below its fitted plane. Outside pieces remain unchanged. Actual upper ground-road footprints protect supporting soil; bridge spans do not protect erroneous grass under a lower road. Visuals and collision use the same completed mesh, published together after the numeric worker completes. The changed geometry identity prevents older finished meshes from bypassing the repair; prepared source areas remain reusable.

The full paired-player suite passed, including 110 focused terrain checks and all existing terrain, bridge, junction, traffic, camera, radio and controller regressions. Terrain cases cover the actual emitted landing triangle, unchanged verges and islands, safe recessed soil, high intrusion, affine gradients, lower crossings and tunnel cover. A representative 64 m fixture changed three triangles, adding 54 vertices; worker duration varied approximately 0.75–2.75 ms across gates. The allocation byte counter was unavailable.

Both native landing proofs passed. Each sampled 3,426 points over actual emitted asphalt near the Kessock bridge landing: no missing terrain samples or terrain above asphalt. The highest terrain excess was −5.9 cm. At the original intrusion, asphalt was 28.47554 m and terrain 28.35554 m. All 80 Standard / 83 HD terrain visual meshes matched their collision meshes. The cold Standard proof cleared 70 triangles, with a maximum background clearance job of 17.2 ms; HD reused 104 finished-cache entries and retained the correction. Close-up road-edge and overview images were inspected; no exposed opening required additional cut-edge walls. Genuine median grass remains.

## Road-query data and driving

Per-road groups now hold ordered numeric views of their snapshot-owned source cells instead of copying segment structs. All four query consumers match an independent original copied-group baseline across 500 positions, including main-grade and bridge branches, duplicate ordering, rebuilds and eight concurrent independent maps: 73 checks passed. A four-core structural calculation estimates 18.34 MB less retained backing-slot payload; this is not measured GC saving.

Three normal native physical drives passed, each covering approximately 1,118 m, with no runtime errors, airborne time, vehicle damage or streaming-brake time. Standard recorded one in-motion frame above 100 ms (worst 102.5 ms); HD four (143.2 ms); the warm Standard repeat three (116.9 ms). No frame exceeded 333 ms. These are bounded run observations, not evidence that every hitch is fixed. Issue #18 remains open; the graphics warmup capture path is being investigated separately.

Installation and owner acceptance are pending. The coordinate-specific proof is nonce-isolated test code; the terrain repair contains no geographic exceptions.
