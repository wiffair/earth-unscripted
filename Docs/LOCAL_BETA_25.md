# Local Beta 25: tree visibility CPU work

Dense scenery was spending about 6 ms of CPU time per frame deciding which trees to draw. The same visibility, distance, density, fade and LOD calculations now run as Burst-compiled CPU jobs. Rendering still uses the existing ordered batches and materials. Tree draw distances and detail levels are unchanged.

The jobs read numeric snapshots. Unity object queries, terrain support checks and rendering stay on the main thread. Ground eligibility is reused until terrain, coast, tree population or damage changes; broken trees invalidate it immediately. Jobs complete before scene publication or retirement can change their source records. Persistent arrays are reused and disposed with the scenery component.

## Measured result

At native 3456 × 2234 in the dense HD test scene, the final three samples took 4.16, 4.20 and 4.27 ms of tree-processing CPU time. The original path took approximately 5.8–6.2 ms. Both submitted 31,458 visible trees and 37 detailed trees. This saves roughly 1.8 ms of CPU work per frame in that scene.

Overall frame time remains affected by rendering and frame pacing. Final median frame intervals were 17.26, 17.34 and 16.97 ms; the warmed original also reached roughly 17–19 ms. These results establish reduced CPU cost, not a large overall FPS gain or elimination of intermittent hitches. Lighting and quality reductions tested during profiling were rejected for lack of a convincing benefit.

## Validation

The new verification compares the jobs with the original calculations across all three quality profiles, fade/LOD/range boundaries and new-tree ages. It also exercises real eligibility invalidation after tree breaks, replacement terrain and coast changes: 36,390 checks. The complete existing verification gate passed, and the paired player build now runs the tree verification as a required check.

Normal Standard and HD physical driving checks passed. A separate HD Fort William check sampled 512 submitted tree bases across four views: zero unsupported bases and zero base-to-terrain gap. Native driving, grounding and installed startup receipts are held with the local QA record. Automated tests isolate their profiles and verify the owner career remains byte-identical. Standard and HD share the same gameplay assembly. This update does not change the world-data worker or finished geometry identity, so existing prepared areas remain reusable.

This is a local beta only. Source, players, website and update feed have not been published.
