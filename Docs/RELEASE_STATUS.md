# Earth Unscripted beta 6

Version **0.24.0-beta.6** fixes UTC parsing under regional settings, including UK
English, with live engine checks in seven cultures. It adds signed game updates, background pack downloads and
radio updates that preserve the playing song. Settings offers update now,
install at close and automatic downloads. Both graphics players are updated as
one package. The VW Splittie pack is included in fresh downloads.

Native updater tests pass on Mac and Windows, including signature rejection,
archive guards, installation and rollback after a failed launcher probe. The
playlist-only delta proof reconstructs the exact pack while reusing 98,008,697
bytes of compressed audio. Release verification files record the packaged tests.

See [Updates](UPDATES.md) for operation, trust and recovery details. Windows
rendered driving and physical-controller checks still need Windows hardware.

## Beta 2 terrain and kerb fix

Version **0.24.0-beta.2** separates ground paths and elevated paths into their
own collision meshes. The original 8.7 m support failure measured a cycle bridge
against lower terrain. The generated ground pavement itself stays supported.

The original scene regression now passes 512 path samples across seven paths,
with a maximum terrain-support gap of 0.300011 m. The full native extended
vehicle/terrain suite passes all 164 checks.

The new production regression checks sample 216 emitted ground-path triangles
on uneven terrain and beside a road, with an independent bridge crossing above.
The maximum support gap is 0.30001 m. An additional audit of 34,641 triangles
from generated scene caches measures the same maximum within floating-point
precision. Kerbs retain solid sides and stand 10 cm above the adjacent asphalt.

Finished-geometry caches have a new identity and regenerate automatically. Careers
and prepared source areas stay compatible. The beta 1 details below describe
the original release and its investigation.

## Beta 1 baseline

Version **0.24.0-beta.1** completes the project and pack rebrand. The
[release page](https://github.com/wiffair/earth-unscripted/releases/tag/v0.24.0-beta.1)
holds complete Mac and Windows downloads, each with Standard and HD graphics.
This release starts a fresh Earth Unscripted profile.

The Mac download targets Apple silicon with macOS 15 or newer. Windows targets
x64 Windows 10/11. The Mac app is ad-hoc signed; publisher signing and notarization
remain release work for wider distribution.

## Source and content checks

- Both platform builds pass production policies covering graphics, controllers,
  radio, roads, railway clearance, tunnels, buildings and traffic.
- Standard and HD gameplay assemblies match within each platform. Source,
  geometry and geography-worker identities are recorded in `SOURCE_IDENTITY.json`.
- All 1,200 retained Unity asset metadata files keep their GUIDs after the moves.
  Old shader-state recordings were retired with the shader rename.
- The source audit passes. SceneryCore 2.0.0, RadioRuntime 3.0.0 and
  ValidationFixtures 2.0.0 restore successfully into an empty checkout.
- Radio policy validation passes 72 checks for two stations and twelve songs.
  The converted music uses the previously verified metadata copies; decoded
  recordings match their originals and approved speech bytes are unchanged.
- Provider unit tests pass 41 cases; one skips because its separate online
  fixture is absent. External provider availability varies.
- The Mac haptics library loads with the renamed native entry points. Windows
  haptics is rebuilt for the same ABI; its packaged check also loads that DLL.

## Mac player checks

Both graphics choices launch through the packaged chooser with a disposable new
profile. Standard passes 13 checks and HD passes 17, including generated-road
driving, renderer selection, shaders and pedestrian heads. HD also verifies the
same car, credits and radio choice saved by Standard.

The first cold Standard attempt reached the world but failed its five-second
movement assertion. The repeat, with the window foregrounded and area cached,
passes. This leaves cold-start timing and independent installation testing worth
checking on other machines.

The converted vehicles pass 41 import, purchase, save/load, livery, suspension,
impact and related feature checks. The extended suite then fails its separate
terrain-supported-kerb assertion near the starting area. That scenery check
remains outstanding; the vehicle importer and both converted packs load.

## Windows player checks

The Windows archive includes both players, the native graphics chooser and the
pinned embedded geography worker. The packaged Windows Actions workflow checks
worker imports, coordinate transforms, provider selection, file integrity, the
renamed native plugin ABI, chooser identities and both headless Unity startups.
Rendered driving and physical controllers still need a Windows test machine.
The workflow's result and verification files accompany the release.

## Distribution and licences

The repository and downloads are public. Original code/docs use
MIT; original artwork and supplied radio music use CC BY-SA 4.0. Third-party
models, audio, geographic data and Unity retain their own terms. Notices in
`Provenance` and `LICENSES` accompany the player packages.

The VW camper was excluded from the initial player archives while its provenance
was reviewed. That review is now complete; the [pack and editable source](../ContentSources/VW-Splittie/README.md)
are available separately, and future source builds include it. Existing beta 2
players can import the pack. The original player ZIPs remain unchanged.
