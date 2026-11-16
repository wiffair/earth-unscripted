# Changelog

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
