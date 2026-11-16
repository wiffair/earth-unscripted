# Project identity and pack formats

**0.24.0-beta.1** is the Earth Unscripted baseline. Its source, native plugins,
shaders, launchers and pack formats use the new name. Both graphics modes share
a fresh Earth Unscripted profile. Earlier beta saves and caches stay in their
original folders; this release starts a new career.

## Application identity

| Component | Identifier |
| --- | --- |
| Standard player | `com.earthunscripted.beta.singleplayer` |
| HD player | `com.earthunscripted.beta.singleplayer.hd` |
| Mac graphics chooser | `com.earthunscripted.beta.launcher` |
| Unity company / product | `Earth Unscripted` / `Earth Unscripted Beta` |
| C# namespace and asset root | `EarthUnscripted` / `Assets/EarthUnscripted` |

Profile storage follows Unity's persistent-data directory for that company and
product. On Mac it is `~/Library/Application Support/Earth Unscripted/Earth Unscripted Beta`.
Standard and HD store their shared career under its `BetaData` folder.
`ProductIdentity` defines the product name, version and storage root.

## Packs

| Pack | Extension | Manifest format | Current pack versions |
| --- | --- | --- | --- |
| Vehicle | `.eucar` | `EarthUnscriptedVehiclePack` | Hekla Formula Offroad 2.0.0 |
| Radio | `.euradio` | `EarthUnscriptedRadioPack` | Iron & Ash 2.0.0; Two Stations 3.0.0 |
| World | `.euworld` | `EarthUnscriptedWorldPack` | Format available for new packs |

These are versioned ZIP containers. The importers validate their manifests and
payloads; renaming an old archive's extension is insufficient. The converted
vehicle keeps its mesh and driving specification, and the radio conversions keep
the approved recordings while updating project metadata and file checksums.
See [Packs](PACKS.md) for authoring and import details.

Original source archives, historical release tags and third-party credits retain
their original names. Real place names, including Blackwood, also remain intact.

## Git baseline

The `v0.24.0-beta.1` tag marks the rebrand in the existing repository. Development
continues from that commit. Keeping earlier commits preserves authorship and
allows changes to be reviewed or reverted; a new repository is unnecessary.
