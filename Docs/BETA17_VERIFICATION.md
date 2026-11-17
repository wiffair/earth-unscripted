# Beta 17 verification

Local test build; public downloads remain Beta 11. The terrain, camper, traffic and streaming repairs from Beta 16 are included.

Beta 17 adds optional recorded engines, layered paint and authored wipers to
vehicle packs. Existing packs keep their current engine and appearance paths.
The generic importer and runtime support are separate from permission to
redistribute a particular vehicle or recording.

## Completed checks

- Full regression suite passed, including 95 focused extension assertions in the
  actual Unity Mono runtime.
- Development Standard and HD players each passed 50 checks in the ordinary game
  session, followed by a road drive: 1,127.79 m Standard and 1,127.94 m HD.
  Both runs reported no runtime errors.
- The native checks exercised the normal menu import worker, insufficient-credit
  refusal, purchase at the declared price, stable vehicle identity and saved
  ownership reload using isolated test profiles.
- Actual selected geometry retained its layered material and two authored wiper
  pivots. Both blades moved through `CarVisual` animation; no duplicate generated
  wiper mechanism was present. Front and driver views were captured in each mode.
- Both engine voices contained the admitted PCM samples and looped. Engine volume,
  menu pause, stationary paused cursors and same-clip resume passed. Radio controls
  and global DSP configuration remained unchanged during those checks.
- Malformed audio, extension declarations and mix budgets have focused validation
  coverage. The Standard paint path uses its clearcoat surface; the HD build setup
  retains a fifth opaque clearcoat variant alongside the existing four variants.

Development-player results establish integration; the normal-player evidence and
remaining performance limits are recorded separately below.

## Normal players and local installation

Runtime source: `2d88b5818687238a4d03507d9ed788004c7dd9a97eb9a401164ac8e8bd088619`.
Matching Standard/HD gameplay assembly SHA-256: `36792b1019ecd61eed318937e64fbf7ab65b41875d1c0e173c1fe6c6cdff4289`.
Geometry identity is unchanged from Beta 16: `earth-geometry3-530a7ec6c110a3db522ae3032cf71c7fa8d54821686347aba289981dc4e96e03`.

Both normal players passed the same 50 extension checks, then approximately
1.10 km of driving each. Two additional normal-car drives covered approximately
1.12 km each. All four drives had zero damage, airborne time, streaming braking
or driving frames over 100 ms. These are bounded route observations, not a
universal performance guarantee.

New graphics-state collections were captured in actual Development players and
read back with Unity's native API: Standard 55 variants/53 raster states; HD 39
variants/52 states; Metal, OSXPlayer, Ultra. The normal players loaded and
completed warm-up for all 53/52 states. The capture source differs from the final
source only by a screenshot-settling delay in the explicitly guarded QA driver;
shader inputs are pinned in the capture receipt. The fifth HD material retains
its clearcoat keyword; the first four variants gain only neutral finish defaults.
Private readback helpers were removed before the normal builds.

The ad hoc signed local app was atomically replaced after source, assembly, manifest
and chooser checks. Beta 16 remains recoverable. The owner save was byte-identical
through replacement and local pack installation; no credits were spent and no
active car was changed. Both installed startup checks passed with a preinstalled
pack and stable-ID owned car in fresh disposable profiles: 14.12 s Standard and
15.64 s HD (whole test-process durations, not player-only startup benchmarks).
The owner save remained byte-identical after both tests. Owner acceptance is pending.

Issue #18 remains open: the initial Beta 16 836 ms pause was not reproduced in
repeat, empty-finished-cache or these Beta 17 drives, so its cause is unproven.

## Content boundary

The Crimson Stepside fixture is for local testing. Public redistribution rights
for its engine recordings have not been documented; it must remain outside public
release assets and public content feeds until that review is complete. Private
reference photographs, videos, working paths and source recordings are not part
of the generic feature documentation or code release.

## Changes

- Vehicle packs can supply two validated PCM engine loops, with independent
  throttle blending and the existing engine-volume control.
- Pack materials can declare a layered colour coat, metallic flakes and clearcoat
  in Standard and HD graphics.
- Packs can supply their own paired wiper geometry, pivots and sweep angles.
- Import validation rejects unsupported capabilities, mismatched audio hashes,
  malformed PCM payloads, incomplete wipers and engine mixes above their gain
  budget. Existing VW and Hekla packs retain their existing paths.
