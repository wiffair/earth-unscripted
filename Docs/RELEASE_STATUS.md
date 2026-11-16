# Beta 52 release preparation

Earth Unscripted 0.23.0-beta.52 is prepared for the private repository
[wiffair/earth-unscripted](https://github.com/wiffair/earth-unscripted), created on
16 November 2026. No public visibility change, domain configuration or
signed/notarized public player release has been performed.
The owner confirmed MIT for original code and CC BY-SA 4.0 for original music/artwork
on 16 November 2026. Source preparation now includes [account/provider setup](DATA_SERVICES.md)
and a [private-first GitHub guide](GITHUB_RELEASE.md), checked against all 13 current
provider catalogue entries. No credentials or provider registration were added by that documentation work.
The later owner-authorized private upload includes source and versioned content.

## Verified on macOS

- Standard and HD players build from matching gameplay source and assemblies.
- The native graphics chooser launches both renderers with disposable profiles.
  The two renderers share the selected car, credits and radio station.
- Controller menu verification covers 24 menus, actual actions, selection ticks,
  pursuit restrictions and haptics. Presentation checks cover 21 captures across
  three resolutions, including the garage, settings, map, loading and driving HUD.
- Policy checks run by the production builder pass. Provider tests run 42 tests:
  41 pass and one skips because its separately captured online fixture is absent.
- The content archives pass CRC, declared-payload and SHA-256 verification and
  restore successfully through the public hydration tool.
- A fresh Unity project, with no pre-existing Library, builds the Standard player
  from the public tree and hydrated content.
- The geography worker builds from the exported, pinned Python source. Its local
  provider-resolution command returns the British terrain adapter and global fallback.
- Twelve separately distributed MP3 copies carry project/CC BY-SA metadata. Their
  decoded PCM matches the original recordings. Runtime radio recordings are unchanged.

The final loading-text refinement is covered by final Standard/HD launcher checks;
the broader controller and resolution checks precede that text-only refinement.
The first final Standard drive assertion failed during concurrent fresh-project
compilation; the unchanged test and player passed when repeated alone. This is
retained as a timing-sensitive test result, not treated as proof of its cause.
Original beta careers, settings and the live installation are protected during tests.
The geometry cache identity is unchanged by this presentation update.

## Distribution boundaries

Use one repository for shared game, worker, launcher and importer source. Publish
the versioned scenery/radio archives as release assets; their URLs point to the matching
versioned release in the private repository. Music metadata copies form a separate
listening/download archive, not a runtime importer pack.
The unchanged 46 MB Cardiff policy fixture is restored from the versioned
ValidationFixtures archive rather than ordinary Git, avoiding GitHub's API upload
size restriction. Hydrate it with the other build inputs before invoking Unity.

The public tree excludes old branding, private development history, careers, caches,
credentials and the separately reviewed VW-branded vehicle pack. That pack remains
in the owner's private beta. Included third-party models, audio and datasets retain
their source-specific licences and attribution. Exact notices are under Provenance/.
Do not publish the entire parent development workspace.

## Remaining release work

- Complete private collaborator review before deliberately making the repository public.
- Build and execute this version on Windows with a Windows geography worker. The
  new release is not claimed to have native Windows or physical wheel validation.
- Perform publisher signing/notarization and independent-device installation tests.
- Review any additional branded vehicle packs before distributing them publicly.

The bounded hygiene scan checks known filename/text patterns, not every possible
secret encoding, binary payload or legal ownership claim. Unity is a proprietary
dependency and requires an eligible licence; open project source does not relicense it.
