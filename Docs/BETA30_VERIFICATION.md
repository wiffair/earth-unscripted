# Beta 30 — local verification

Beta30 fixes source loading at tunnel boundaries and conflicts between independently prepared railway profiles. This candidate is installed locally; the public release remains Beta29.

The loader requests missing source data along the car's immediate corridor before speculative navigation areas. This priority also applies when replacing a pending request. Coverage status distinguishes missing source data from unfinished geometry.

The preparation worker preserves boundaries for every area delivered during its session, including replies still downloading. Previously cached areas are checked against those boundaries before reuse. If unused cached profiles impose incompatible heights, the worker refits against delivered areas, validates the complete result, and retires only incompatible unused admission manifests. Their numeric files and raw sources remain available; the areas regenerate on demand. Unknown ownership remains conservative.

Railway planning also samples actual neighbouring cores through the context halo, including diagonal connections. An old halo does not become a fixed boundary. Physical gradient, vertical-curve, crossing-clearance and tunnel-cover limits remain enforced.

## Checks

- 159 world-source tests passed, with one existing skip. Nine new tests cover cache reuse/rebuild, preserved delivered boundaries, infeasible committed boundaries, diagonal planning and strict physical limits.
- The recorded New York conflict completed full preparation in 30.90s: 328 tunnel-cover checks, eight seam samples, maximum boundary mismatch 0.001002m. The previously delivered core's hash stayed unchanged.
- The retired neighbouring proposal regenerated on revisit in 120.18s: 127 seam samples, maximum mismatch 0.001002m. These are source preparation timings, not startup improvement claims.
- Packaged Mac worker session checks passed: authenticated access, rejected anonymous/cross-origin/invalid-Host/path-traversal requests, and rotated session credentials. Both Mac and Windows workers were rebuilt from pinned source and dependencies.
- Standard and HD each drove 500m across the previously blocked Brooklyn–Battery Tunnel boundary, with the next source core initially absent and a competing navigation prefetch. Standard: 500.66m in 57.87s; HD: 500.69m in 57.70s. Both reported no damage, airborne time or runtime errors, and verified the missing source area became resident. The real career stayed unchanged.
- Both installed players share gameplay source `ba5a38875d51ec692041993933f9fc5a1c910e11ab7583ee111c29b7794e8070` and assembly `da4737d9ea6edc5e22376ad6901c7d4830a438effd78ee4c8258a4f44952568f`. Worker identity: `e3d43b3d7c80d77951113a2b41329abcd7145eccea73f0ca7210537632967648`.

Two validated numeric tunnel areas were reused in the owner's cache. The installed app was replaced atomically, retaining the previous installation and preserving the latest save. No release upload, website change or public update-feed promotion was made for Beta 30. The private truck pack and recordings remain excluded from publication.

This verifies the reported boundary failure and the global reconciliation policy. It does not establish that every international source area will prepare without delay; genuinely incompatible already-delivered boundaries and non-feasibility solver failures still fail safely rather than removing coverage checks.
