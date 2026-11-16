# Beta 9 local verification

Candidate for issues #7 and #15–#19. Public releases and the website feed remain unchanged.

- Speed numerals use shared opaque vector meshes. Standard and HD close-up
  renders stay readable after UI font rebuilds; clone unload preserves the master.
- The paired build passes the existing transport, controller, radio, ground-path,
  tunnel, building-clearance and junction checks, plus 21 feedback regression checks.
- Native Standard and HD tests pass resolution preview, explicit revert,
  15-second rollback and saved-setting reload. Older profiles receive an engine
  volume of 35%; an explicit zero remains zero. Other audio keeps its master level.
- Actual Standard and HD water-shader renders pass three states: baseline sea,
  water excluded across the whole wave-height envelope at a road, and retained
  water beneath a raised bridge. Road-mask refresh also follows area changes.
- Junction tests cover extra corner pavement, constrained radius, building
  exclusion, split one-way islands, roundabout centres, separate levels and
  reversed asymmetric road geometry. Island-hole ownership is assigned once. Cached integer containment agrees with the original millimetre-precision checks and allocates no memory over 10,000 warmed lookups.
- Coastal parsing/hashing, water-substrate rasterization and periodic save writes
  run off-thread. Distant terrain now yields between individual edge samples.
  Recorded-engine gain and pitch changes are bounded after a delayed frame.

The first native HD traversal covered 180 m with no damage or streaming brakes.
It still recorded a 100.6 ms distant-boundary burst, which prompted the additional
edge-sampling fix. Final traversal results and local installation are recorded below.

Raw logs, player coordinates and career files remain local. A short automated drive
cannot establish that every device or route is free of hitches or audible clicks.

The final packaged Standard player completed 180.1 m with no damage, no runtime
errors and zero streaming-brake seconds. The final HD player reached the saved
bridge-area start in 10.2 seconds with its cached geometry. Both graphics options
contain the same gameplay assembly.

**Issue #18 remains open:** the final Standard traversal still recorded eight
frames over 100 ms, with a 213.5 ms worst frame. Generation slices are smaller,
but these results do not establish hitch-free driving or confirm an audible-click
fix. Native sampling also showed managed allocator contention; further profiling
is needed before treating the intermittent-stutter report as resolved.

Local Mac installation passed archive-hash, deep signature and graphics-launcher
checks. The app was exchanged atomically and the old beta retained outside the
launcher index. The owner career hash is unchanged. Public-update promotion is
not prepared: the release protocol requires both Mac and Windows artifacts, and
no Windows Beta 9 package or public catalogue is claimed by this local update.
