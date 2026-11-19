# Content packs

Earth Unscripted uses versioned packs for vehicles, radio and world content.
Keep the pack's identity stable when updating it, and increase its version and
revision when its contents change. Publish new archive checksums with each release.

## Vehicles — .eucar

Open the vehicle-pack importer in the game menu and choose an `.eucar` file.
Importing adds the car to the catalogue. Its purchase price still applies;
Hekla Formula Offroad is free.

A vehicle pack is a ZIP with `manifest.json` and `mesh.json`, plus optional
`texture.png`, `README.txt`, `LICENSE.txt` and the validated engine recordings
described in [Vehicle-pack extensions](VEHICLE_PACK_EXTENSIONS.md). The manifest declares
`EarthUnscriptedVehiclePack`, `formatVersion: 1`, a stable ID, semantic version,
increasing revision, author, licence and mesh/texture SHA-256 hashes. Driving
specifications include dimensions, mass, wheel size, suspension, strength and cost.
Paintable materials use the mesh material's `paint` flag.

The importer checks the data before installation, limits archives to 32 MB and
rejects conflicting bytes under the same revision. See
[VehiclePackFormat.cs](../Unity/Assets/EarthUnscripted/Runtime/VehiclePackFormat.cs)
for the accepted fields and ranges. Hekla 2.0.0 and the reviewed [VW Splittie 2.0.0](../ContentSources/VW-Splittie/README.md)
provide working examples. Both are free.

## Radio — .euradio

The supplied packs are Iron & Ash 2.0.0 and Two Stations 3.0.0. Both use
`EarthUnscriptedRadioPack`, `format_version: 1`, and include the approved music,
intros and jingles. The radio's configured import folder accepts `.euradio` files
when its library is loaded. Restart after adding or updating an archive.

Each pack contains its station manifest, audio files, file-integrity records and
licence information. File paths, byte counts and hashes must match the payload.
Keep fictional station, artist and track names and each recording's attribution.
The project website and licence metadata are embedded in the music copies.
See the [radio source](../Unity/Assets/EarthUnscripted/Radio) and the release packs
for the complete schema.

## World — .euworld

The world-pack menu imports `.euworld` archives for scenery and authored circuits.
Their manifest format is `EarthUnscriptedWorldPack`. Revisions are inspected and
stored separately; previewing a pack does not certify its gameplay geometry.
The [world-pack source](../Unity/Assets/EarthUnscripted/Runtime/Packs) defines
payload limits, rights declarations, checksums and validation records.

## Build content archives

SceneryCore 2.0.0, RadioRuntime 3.0.0 and ValidationFixtures 2.0.0 are developer
content sets. Restore them with `Tools/hydrate_content.py` as described in
[Building](BUILDING.md). Their catalogue pins the exact archive and payload
checksums. The compiled game already contains its runtime content.

The Earth Unscripted baseline changes pack headers as well as extensions. Use the
converted releases rather than renaming older files. Third-party asset licences
and original credits accompany each conversion.
