# Local Beta 23

Deferred scenery continues when the actionable geometry queue is empty, even if a future support check is unsatisfied. Building work resumes in batches of up to four buildings, yielding after a building once the 50 ms budget is reached. This gives road and data planning regular turns; a single building or final mesh publication can still take longer. Vehicle support checks and collider-before-publication rules remain strict.

The stats panel shows the latest measured build slice and the worst recorded slice, with stage names. The current value returns to zero when idle. These measurements describe assembly slices, rather than whole-frame CPU time or data-worker duration.

Tyre sound uses the four wheels’ actual contact surfaces, tyre size and profile, and local wetness, snow, ice and water depth. Ordinary, clean, rough and race asphalt, concrete, gravel, grass, mud, snow and ice have different acoustic profiles. Rolling volume levels off at higher speed. Mapped `surface` and `smoothness` tags are carried through the worker; missing tags use a normal-road fallback. Road age is not inferred.

Recorded engine packs can opt into separate RPM banks and a road-V8 sound profile. Existing packs retain their previous behavior. Pack authors can also specify a tyre sound profile. See [Vehicle pack extensions](VEHICLE_PACK_EXTENSIONS.md).

Validation includes 43 scenery scheduling/current-and-worst timing checks, 515 tyre audio checks, 9,453 pack-extension checks, and 139 world-source tests with one skip. Standard and HD native driving checks pass. Native receipts and local installation records are retained outside this repository. Listening acceptance remains a player check.

This update is local. Public releases and the website update feeds are unchanged.

Unity supports worker mesh calculations through [MeshData](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/mesh/allocatewritablemeshdata), while most scene APIs require the main thread ([threading guidance](https://docs.unity.com/en-us/engine/6000.0/manual/scripting/programming-distribute-work-threads/async-await-support/async-awaitable-continuations)). This generator still serializes assembly around its shared working map. Further parallel assembly needs separate per-job contexts.
