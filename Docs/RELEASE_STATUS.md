# Earth Unscripted Beta 32

Version **0.24.0-beta.32** improves road streaming, tunnel approaches and scenery preparation. [Download for Mac or Windows](https://earthunscripted.com/download), with Standard and HD graphics.

Roads that overlap the edge of a source area now retain their asphalt and physical support. Ground beside tunnel entrances stays beneath adjoining ground-level roads. Roadside signs keep the height of their own road at stacked crossings. Malformed optional building boundaries no longer prevent usable roads from preparing.

Preparation can work farther along a planned route and keeps useful work when nearby cached areas become ready. Dense junction paving does less unrelated building work. Building facades and tunnel weather reuse unchanged buffers; finished-mesh caching removes a temporary allocation for every coordinate.

On the tested Mac, the full 32.3 km M4/M32 stress drive completed both source-warm/generation-cold and cached runs with zero readiness stops, damage or loss of road support. Startup was 16.11/7.69 seconds and average frame rate 70.83/68.41 FPS. The first run retained downloaded source files; these figures are not first-download timings. The corrected Brooklyn–Battery tunnel test also completes its full kilometre in both runs. See [verification](BETA32_VERIFICATION.md) and [measurement details](PERFORMANCE_PLAN_PROGRESS.md).

**Still under investigation:** dense city height fitting can take several minutes, including the Holland Tunnel area; off-road re-entry can be blocked even after loading finishes; recovery can place a car on other traffic; repeated stops at secondary signal heads and abrupt lane-count changes remain. Short frame hitches also remain. These findings are tracked in [the issue ledger](BETA32_ISSUE_LEDGER.md). The passing Brooklyn tunnel test does not cover every tunnel.

The VW Splittie remains included, with its improved cockpit. Its pack is still version 2.0.0. The private truck and recordings are excluded. Windows packaging checks cover the native worker, launcher and headless engine startup; rendered driving and physical-controller acceptance still require a Windows PC.

[Release notes and files](https://github.com/wiffair/earth-unscripted/releases/tag/v0.24.0-beta.32).

---

# Earth Unscripted Beta 29

Version **0.24.0-beta.29** brings the locally tested Beta 12–29 changes to the [Mac and Windows downloads](https://earthunscripted.com/download), with Standard and HD graphics.

The update includes the split-screen camper interior, floor joins and steering correction; cockpit rain and wipers; graphics presets; sparse patrols; location recovery, recent searches and favourites; diagnostic export; railway furniture; background audio preparation; and road/scenery caching and preparation improvements. The existing VW Splittie 2.0.0 pack gains its improved cockpit through this game update. Its pack archive is unchanged. The private truck pack and recordings are excluded.

Stats now appear at the top right. Worded signs retain their lettering when the font texture changes. Original and prepared data caches remain usable; changed generated geometry rebuilds under its new identity.

**Known issues:** a driver can remain blocked in the Brooklyn–Battery Tunnel despite zero queued road jobs ([#55](https://github.com/wiffair/earth-unscripted/issues/55)). Intermittent frame hitches ([#18](https://github.com/wiffair/earth-unscripted/issues/18)) and broader high-speed streaming acceptance ([#21](https://github.com/wiffair/earth-unscripted/issues/21)) remain open. Rendered Windows driving and physical-controller acceptance require Windows hardware. This release does not claim to eliminate every streaming delay.

See [Beta 27 verification](BETA27_VERIFICATION.md), [Beta 28–29 verification](BETA29_VERIFICATION.md), and the [release](https://github.com/wiffair/earth-unscripted/releases/tag/v0.24.0-beta.29).

---

# Earth Unscripted Beta 11

Version **0.24.0-beta.11** is available for [Mac and Windows](https://earthunscripted.com/download), with Standard and HD graphics. It includes the Beta 8–11 location-loading, traffic, road, scenery, radio, menu and controller fixes.

Mac acceptance includes seven final native scenarios and installed-player checks. Windows packaged checks pass for both headless players, worker authentication and recovery, GIS/provider resolution, native haptics and the graphics chooser. Rendered Windows driving and physical-controller checks still need Windows hardware.

Occasional brief frame hitches and some narrow-junction traffic stand-offs remain under investigation. See [Beta 10 verification](BETA10_VERIFICATION.md), [Beta 11 verification](BETA11_VERIFICATION.md) and the [GitHub release](https://github.com/wiffair/earth-unscripted/releases/tag/v0.24.0-beta.11).

## Beta 8

Version **0.24.0-beta.8** fixes traffic resuming from an obsolete route position after a collision, which could launch the camper off the fallback county oval. Nearby traffic now remains physical until safely separated, and resumes from its actual road position.

Location selection updates the map immediately. Overseas downloads select services with suitable geographic coverage and retry backups. Enter or Return submits the place search. The geographic route planner handles the procedural county safely, and coastal sign-rule boundary intersections no longer abort world loading. Speed-sign digits are larger and use a separate, immutable lettering atlas.

Prepared road geometry remains reusable. Both graphics options contain the same gameplay changes. See [the verification record](BETA8_VERIFICATION.md) for test scope and remaining checks.

## Earlier releases

### Beta 6

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
