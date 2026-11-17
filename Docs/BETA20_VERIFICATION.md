# Beta 20 — coastal publication and saved-location startup

Public downloads remain Beta 11. This is local owner testing only.

## Saved-location startup

The owner profile previously failed at boot because only25 of27 safe traffic sites were available. A real supported player start must not be rejected because the surrounding fleet cannot all fit. Initial traffic now uses only genuinely supported separated sites, retaining required source slots in a bounded deferred factory queue. Normal driving updates create missing actors in supported off-camera gaps. World/player retirement clears the owned queue.

The actual vehicle registry retains all required source slots and stays unsealed while factories are missing. Normal activation provenance and final physical layout validation still apply; it seals only after all real factories and layouts exist. World-bound snapshot freshness remains a separate requirement.

Focused actual Mono checks already passed:71 scarcity/ownership and35 deferred-roster checks. First private latest-owner-copy run started with22traffic+3patrol and two deferred slots. Normal updates filled both slots. Its helper incorrectly demanded full fresh semantic snapshot completeness despite an absent actual Town/world binding; that baseline failed receipt is retained. The corrected helper observes genuine boot sealing/layout completion separately and does not grant freshness or a world binding. Corrected native repeat passed in39.36s overall: real22traffic+3patrol/two deferred →24+3/queue0,28unique roots/slots, genuine activation/bootseal and zero pending layouts/retirements. Fresh world snapshot remains explicitly unavailable without the actual Town binding.

## Coastal startup

The baseline private HD capture is now of the actual Kessock bridge span, not after its landing. Six probes are correctly mapped sea at8.18m. No active sea triangles were present on first drive entry; water appeared25.05seconds later (42.69seconds since helper startup), after fine-bed refresh. At settlement all six points were covered and their published terrain was below water.

The global loading sequence now optionally fetches/adopts coastal data before any initial terrain generation, so first-pass meshes and finished-cache identities already reflect sea beds. The optional fetch has a four-second deadline and later recovery remains available when offline. Initial road-water masking and sea/beach/substrate publication occur before renderer readiness. The first driving loop reuses the published surface. Late new-coast adoption retains the existing atomic bed/collider refresh path.

Stronger HD repeat passed in33.79s overall: sea and beds were ready on the first driving frame, all six mapped sea points covered and no protruding terrain. No post-drive wait. The full actual Mono regression gate passed, including18 coastal startup,71 scarcity,35 deferred-roster and4,540 traffic-control-cache checks. Normal packaged Standard and HD each drove about999physical metres with zero damage, airborne time, streaming-brake time and unpublished bridge samples. Maximum moving-frame time was64.9ms Standard/84.5ms HD. The HD timing recorder also saw100.7ms while stationary; no moving frame exceeded100ms in these two runs. No claim that this proves the intermittent HD moving-frame hitch resolved.

## Scope

Real owner save is externally hashed before/after each private run. Diagnostics use fresh nonce-specific outputs/private careers and are removed before normal builds. No owner car purchase/selection changes. Private truck media stays outside Git and distributable builds. The normal Standard/HD pair is installed locally. Both installed imported-car startup checks passed (14.12s/14.13s full process); the real owner save remained byte-identical through installation and both checks. Beta19 remains recoverable. Its shared source is `bb5ae28deecf19c7c22d59a3d4c2e18cab976433e9f6fcb80ce428f6fc2a3d18`; both gameplay assemblies have SHA256 `14d3d97d0f757b2f55aabe79af0c0456d60d03b5908c734d8d0f4751733f4296`. Private diagnostic types are absent in both normal assemblies. The bounded repository scan passed1,326text files; this is not a complete binary/legal provenance guarantee.


## Remaining checks

The occasional HD moving hitch remains tracked in issue18. A shorter clean run cannot prove it removed. Windows runtime and newly discovered external map providers have not received a new live acceptance test in this batch. Sparse moving police are a separate issue48 under development; Beta20 retains its existing patrol source count. Public GitHub/site/feed remain Beta11.
