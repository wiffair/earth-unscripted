# Local Beta 26: repeated road-height searches

A live Kessock Bridge drive fell to 3–7 FPS. The HUD showed only a 1.5 ms current geometry build slice, which did not account for all CPU work. A five-second native stack sample located most main-thread work in UpdateDrivingCoverage → ProjectRoadForVehicle → RoadSurfaceHeightForRoad. Resolving the sampled Mono addresses identified the functions directly.

Vehicle projection was recalculating the same complete authoritative road-height profile for every nearby segment of that road. The cell index preserves road insertion order, so adjacent segments can reuse that height within the query. The result remains tied to the exact road object and query XZ. The cache is local to a single synchronous call: no allocation, global/shared state, approximate heights, stale profile results or cross-road-ID reuse. Every candidate segment and its original ordering, width, source station, vertical plausibility and score are retained.

The coverage guard also has an isolated diagnostic timing scope. Normal play does not collect this telemetry. Missing asphalt, wrong road levels, lane direction, stopping distance, physical support checks and off-road ground-continuation rules are unchanged.

## Validation

An independent copy of the original query checks 306 assertions, including duplicate road IDs and occurrences, lower/upper decks, invalid coordinates, missing cells and a profile change between calls. Exact selected-road identity, source projection, widths, height, height gap and on-road decisions match. In the dense native-profile fixture, 1,236 candidates need only four height-profile runs. The original query took 180.05 ms and the candidate 0.64 ms in the Editor benchmark. These are query timings, not a promised game FPS or a universal speedup.

The complete existing regression gate passes, including 678 physical road-coverage checks and 36,390 tree-job checks. A matched fresh 1.2 km HD bridge drive at 3456 × 2234 passed in both versions, with the same 89,596 query segments. Median sampled frame time fell from 32.71 to 25.27 ms; p95 from 53.87 to 43.29 ms. Worst recorded frame fell from 118.53 to 55.56 ms. The candidate recorded no frames over 100 ms and no coverage scope over its 8 ms reporting threshold. These are fresh-session samples: the original long-running 3–7 FPS state was not reproduced after restarting, so extended owner testing remains important. Native comparison, paired driving and installed startup receipts are held with the local acceptance record. Both player modes share the gameplay fix. Rendering quality, road geometry, source workers, content packs and save formats are unchanged. Finished geometry remains reusable.

This update is local only. No source, player, site or update-feed publication is authorized.
