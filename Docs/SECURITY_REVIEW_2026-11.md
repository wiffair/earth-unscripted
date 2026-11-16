# Security review — 16 November 2026

This review covers the current beta source, its world-data worker, pack importers,
automatic updater, native launchers, haptics plugin, website and pinned dependencies.
It is a focused source and dependency review before publisher code signing, not an
independent penetration test or a guarantee that the game has no vulnerabilities.

The work is tracked in [issue #1](https://github.com/wiffair/earth-unscripted/issues/1).
Confirmed vulnerabilities are held in private draft GitHub security advisories until
an updated release is ready. Public hardening records do not include private locations,
credentials or unpatched reproduction instructions.

## Findings and changes

- **Local world service:** session-bound authorization, browser request isolation,
  bounded connections and job retention, private atomic endpoint publication and
  constrained prepared-file paths. Every game caller and automatic worker recovery
  uses the current session. Details remain in the private advisory pending release.
- **Bundled runtime:** the Mac worker used CPython 3.13.1 and the Windows worker
  used 3.13.16. Both builds now target the official 3.14.10 security release, with
  pinned runtime archives and compatible wheels. Mac builds refuse another interpreter
  version and reinstall hash-verified local wheels. See [issue #2](https://github.com/wiffair/earth-unscripted/issues/2).
- **Updater extraction:** enforce declared lengths while streaming regular files
  and symbolic-link payloads. Existing signature, URL, archive hash, path and total
  size checks remain. This is defence in depth for publisher-approved malformed
  archives, not an unauthenticated execution finding. See [issue #3](https://github.com/wiffair/earth-unscripted/issues/3).
- **Repository controls:** enabled private vulnerability reporting, dependency alerts,
  automatic dependency security-fix pull requests, secret scanning and push protection.
  Contributor changes still require the owner's code review; owner maintenance bypass
  remains as previously configured.

## Dependency checks

The 16 November checks returned no known OSV matches for the 24 pinned Python
packages or the checked Newtonsoft.Json versions. npm audit reported no known
vulnerabilities in the website dependency tree. NuGet's vulnerability query returned
no advisories for the native installer project, including transitive packages.
These are advisory database results, not proof that every dependency is safe.

Unity 6000.6.8f1 is in the vendor's fixed-version lists for the recent
[eMudhra trust issue](https://activation.unity3d.com/security/unity-emudhra-compromise)
and [Brotli decoder issue](https://activation.unity3d.com/security/unity-brotli-decoder-vulnerability).
The native updater uses .NET 10.0.15, matching the current vendor runtime metadata.
The haptics build uses a pinned SDL source archive with audio, rendering, GPU and
camera subsystems disabled. SDL's repository had no published security advisories
at review time; that is not a complete audit of native SDL code.

The Python update follows the vendor's [3.13.18 security release](https://www.python.org/downloads/release/python-31318/)
and uses [3.14.10](https://www.python.org/downloads/release/python-31410/), which has official binaries for both targets.

## Validation and release status

- World-data tests under the patched interpreter: **48 tests, one optional live test skipped**.
- Worker HTTP boundary tests are included in that suite: **six passed**.
- Unity client capability and recovery tests: **six passed**; project compilation passed.
- Native Mac updater tests: **33 passed**, including successful installation and rollback.
- Packaged Mac Standard rendered startup passed in **30.5 seconds**, with no reported errors, using a disposable profile.
- The packaged Mac worker passed authorization, path, Host/Origin and restart checks.
  It also resolved providers with the temporary build framework unavailable.
- Current source hygiene scan passed. GitHub reported zero open secret-scanning and
  dependency alerts when checked; newly enabled scans can produce later findings.

The [native Windows worker checks](https://github.com/wiffair/earth-unscripted/actions/runs/43938643191)
and [33 Windows installer/rollback checks](https://github.com/wiffair/earth-unscripted/actions/runs/43938640798)
passed. Final player packaging is checked before the next beta is published.
A source patch or a green scan does not update older installed binaries.
The issue tracker remains open until the fixed release and its verification are recorded.

## Limits

The review does not exhaustively fuzz Unity's native media decoders, inspect every
native library embedded in GIS wheels, audit proprietary engine internals, validate
all external data providers, or prove absence of unknown/encoded secrets throughout
history. The current single-player beta does not enable the legacy LAN host/join
listener. If LAN multiplayer is enabled for a future release, its trust model needs
another review. Signing credentials remain outside source and release assets.
