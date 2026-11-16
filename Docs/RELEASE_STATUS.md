# Beta 52 release status

Version **0.23.0-beta.52** is available for private testing at
[wiffair/earth-unscripted](https://github.com/wiffair/earth-unscripted/releases/tag/v0.23.0-beta.52).
The complete Mac and Windows ZIPs each include Standard and HD graphics, their
native chooser, the geography worker and runtime scenery/radio content.

The Mac download targets Apple silicon. The Windows download targets x64 Windows
10/11. These are beta builds: the Mac app is ad-hoc signed, not notarized, and
Windows executables have no publisher signing certificate.

## Checks completed before packaging

- Standard and HD build from the same gameplay source. Their gameplay assemblies
  match within each platform; each package carries a source identity manifest.
- Mac controller tests cover 24 menus, including actions, ticks, pursuit
  restrictions and haptics. Layout checks cover 21 captures at three resolutions.
- The production builder passes its road, rail, tunnel, traffic, radio, controller
  and presentation policy checks for both platform builds.
- A clean public-source Unity project builds after restoring the content packs.
- Content archives pass checksum, payload and CRC checks and restore through the
  hydration tool. The large Cardiff policy fixture is supplied separately.
- Provider tests pass 41 cases; one skips because its separate online fixture is
  absent. They do not establish continuous availability of external services.
- Twelve listening MP3 copies contain project/licence metadata, with decoded audio
  identical to the original recordings. Runtime radio audio is unchanged.

## Packaging checks

The Windows builder now places its worker where the runtime expects it, under
`Helpers/AutomaticWorld`. The worker includes embedded CPython, pinned x64 wheels
and time-zone data. Its static native-library check reports no missing DLLs.
A Windows Actions workflow executes the packaged worker and native chooser, then
starts both Unity players headlessly. Check that workflow's result alongside the
release; a headless test does not cover rendered driving or physical controllers.

The fresh Mac worker exposed a PROJ-library filename collision that a lightweight
provider-only check had missed. The worker builder now keeps pyproj's extensions
and native libraries together, then checks full worker startup before packaging.
Worker checksums are refreshed after signing so the game's integrity check reads
the actual distributed bytes.

Both packaged Mac launcher choices pass native generated-road driving checks, including shared car/credits/radio, renderer selection, pedestrian heads and shader checks. Standard passes 15 checks; HD passes 17. These use a disposable profile. The beta's original
careers, settings and live installation are kept separate. The presentation and
packaging changes leave the geometry cache identity unchanged.

## Before a wider release

- Test installation and rendered driving on independent Mac and Windows machines.
- Add publisher signing/notarization when distribution credentials are available.
- Review with private collaborators before changing repository visibility.
- Review additional branded vehicle packs before distributing them. The VW-branded
  pack remains in the owner's private installation and is excluded here.

Original code/docs use MIT; original artwork and supplied radio music use CC BY-SA
4.0. Third-party models, audio, geographic data and Unity retain their own terms.
The notices in `Provenance` and `LICENSES` accompany the player packages.
