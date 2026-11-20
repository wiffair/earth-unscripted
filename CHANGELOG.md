# Changelog

## 0.24.0-beta.32 — Streamed road support and preparation

- Preserve road width and colliders across source-core edges and support adjoining ground roads beside tunnel portals.
- Keep roadside signs on their mapped road level and omit malformed optional area relations without losing usable roads.
- Retain useful preparation jobs, prepare farther along planned routes, and reduce paving, facade, weather and mesh-cache work.
- Add repeatable performance drives and a full-distance Brooklyn–Battery tunnel fixture.
- Refresh Mac/Windows downloads and document remaining dense-city, off-road recovery, traffic and frame-pacing issues.

See [release status](Docs/RELEASE_STATUS.md) and [verification](Docs/BETA32_VERIFICATION.md) for measured results and limitations.

## 0.24.0-beta.9 — Local test candidate

- Give speed-limit numerals shared vector meshes, independent of UI font atlases.
- Add a separate engine-volume slider with a quieter default.
- Round the inside of mapped junctions where buildings and traffic islands allow.
- Add display presets, custom dimensions and a timed Keep/Revert confirmation.
- Exclude animated water where it would reach a mapped road surface, while
  retaining water beneath bridge decks and waves outside the road footprint.
- Move coastal parsing/hashing and periodic save writes off the driving thread,
  yield more often while placing building details, and smooth recorded engine
  gain/pitch recovery after a delayed frame. Increase the initial audio buffer.
- Assign junction-island holes once during preparation, and yield between
  individual distant-terrain boundary samples.

This candidate is for local testing. Public downloads remain unchanged.

## 0.24.0-beta.2 — Ground paths and kerbs

- Separate elevated footbridge/raised-path collision meshes from ordinary
  terrain-supported paths and traffic islands across every generated area.
- Correct the support audit so an upper bridge cannot be measured as a floating
  pavement over a lower path. Keep independent bridge heights and clearances.
- Add emitted-mesh regression checks covering uneven terrain, supported kerbs
  and stacked paths, and invalidate older finished meshes automatically.
- Keep the existing Earth Unscripted career and downloaded/prepared map data.

## 0.24.0-beta.1 — Earth Unscripted baseline

- Complete the project rename across gameplay source, shaders, resource paths,
  native plugins, product identifiers and graphics launchers.
- Start a fresh Earth Unscripted profile shared by Standard and HD.
- Introduce `.eucar`, `.euradio` and `.euworld` pack branding, with matching
  importer checks and versioned content archives.
- Convert Hekla Formula Offroad to 2.0.0, Iron & Ash to 2.0.0 and Two Stations
  to 3.0.0. Keep their vehicle specification and approved recordings.
- Publish SceneryCore 2.0.0, RadioRuntime 3.0.0 and ValidationFixtures 2.0.0
  with the new asset paths, manifests, checksums and original asset credits.
- Bundle the junction-paving policy fixture so production checks work from a
  clean source checkout.

Earlier work remains in Git history and the previous private beta releases.
