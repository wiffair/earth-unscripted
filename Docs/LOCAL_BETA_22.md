# Beta 22 — local testing

This update is for local acceptance. The public release and website remain on Beta 11.

## Changes

- Road coverage now considers the requested forward/reverse direction at rest. A genuine mapped road end can continue onto prepared ground; clipped data boundaries and bridge/tunnel edges retain coverage protection.
- Failed world starts offer **Choose another place**. A missing saved area is reported instead of silently putting the player on a fallback circuit.
- Hamburg's multi-value OSM layer tags are handled. Initial place loading can use a bounded, verified BBBike city extract after public map services fail; Sydney has been checked with real source data.
- Place searches retain recent selections and favourites separately from disposable map caches.
- Pack browsing uses stable controller targets. **Export diagnostic report** is available in Help/About and on failed-loading screens. Reports are local ZIP files containing the current location and bounded diagnostic data, without career files or pack assets.
- Railway furniture includes buffers at real track ends and platforms/canopies where they are mapped. Split ways and chunk edges do not count as track ends.
- Finished-geometry caching supports the new railway batches. A reader limit mismatch found during candidate testing is corrected.
- MP3 decoding and normalization run on a cancellable worker with bounded memory and a verified prepared-audio cache. The main thread uploads short PCM blocks. Pause/resume and random initial tuning retain their existing behavior.
- Recorded-engine packs can optionally declare a bounded pitch-response curve and exhaust load retention. Existing packs retain their previous behavior.

## Performance evidence and limits

An own-process Time Profiler capture identified synchronous MPEG decoding on the main thread during a roughly 200 ms driving hitch. After the change, the two short owner-clone movement/radio checks had no frames over 100 ms. These are short checks, not a guarantee against every hitch during extended streaming.

The scoped macOS QoS experiment remains disabled by default: the native road benchmark improved about 0.4%, and the actual Python derivation did not improve. Both differences are noise-sized. The sampled game main thread already ran predominantly on performance cores. This does not characterize all other processes or background workers.

WAV/OGG retain Unity's decoder; their normalization is sliced, but decoding those formats has not been moved off-thread. The new MP3 decoder has exact normalization-oracle checks and independent decoder comparisons; listening acceptance still belongs to the user.

## Verification records

Local evidence lives alongside this note. It includes the combined Editor gate, Python tests, authenticated worker checks, failed-location recovery, real forward/reverse movement, radio cold/warm preparation and final Standard/HD acceptance receipts. Failed intermediate receipts are retained and are not represented as passes.

The exact historical stuck point was not forced through the normal safe-start guard. The current owner clone starts on a verified unobstructed road about 35 m away; focused physical coverage fixtures exercise the actual terminal policy separately.

The owner career is protected by before/after hashes during isolated tests and installation. Old installations are kept recoverable. No public publication is authorized for this update.

Installed Standard repeat startup at the current test area reached ready in24.5seconds. First use of the revised data required118.1seconds to fetch/prepare it. Longer Standard/HD acceptance drives passed but retained occasional104–110ms frames and brief coverage braking. Further frame-pacing work and extended acceptance remain; these changes do not claim to eliminate every hitch.
