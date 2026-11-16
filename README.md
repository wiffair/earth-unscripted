# Earth Unscripted

**Real places. Your adventure.**

Earth Unscripted is a work-in-progress exploration game built around real-world
geography. Drive roads generated from map and elevation data, choose destinations,
listen to fictional radio stations, and explore scenery that streams around you.
Walking, riding public transport, driving trains, boating and flying are future
directions; their appearance in the identity artwork does not claim they are all
playable today.

![Earth Unscripted](Brand/splash.png)

The single-player beta provides driving, navigation, imported vehicles, changing
weather and daylight, nearby simulated trains, and Standard/HD graphics modes.
Both graphics modes use the same gameplay source. Streamed geography and traffic
are approximations for a game, not a real navigation or safety system.

## Start here

- **Players:** [Playing a compiled game](Docs/PLAYING.md). You do **not** need
  Unity installed to run a compiled download. The current GitHub Beta 52 release
  contains developer source/content archives, not ready-to-play installers.
- **Developers:** [Build from source](Docs/BUILDING.md). This is the route that
  requires Unity Editor, an eligible Unity licence and build dependencies.
- [Documentation index](Docs/README.md).
- [Required/optional accounts and API setup](Docs/DATA_SERVICES.md): normal play
  needs no third-party API account. Optional UK live-feed registration is separate
  from the remaining collector/mapping integration work.
- [Upload privately first, then make public](Docs/GITHUB_RELEASE.md).
- [Recommended monitoring after publication](Docs/MONITORING.md).
- [Verified release status and remaining limitations](Docs/RELEASE_STATUS.md).

## Project layout

- `Unity/`: Unity project, C# gameplay, interfaces, render adapters and tests.
- `World/`: source of the packaged Python geography worker and provider adapters.
- `Native/`: original platform integration, including haptics and the car picker.
- `Launcher/`: Mac-only Standard/HD graphics chooser (Swift); a Windows equivalent
  is not implemented yet. Windows game build targets are in the shared Unity project.
- `Brand/`: icon, splash artwork, prompts and attribution.
- `Provenance/`: asset origins, data licences and third-party notices.
- `Tools/`: portable preparation, packaging and release checks.
- `Docs/`: build instructions, architecture, licensing, controls and release status.

Large visual resources and radio recordings are distributed as **versioned release
assets**, with checksums. They are outside ordinary Git history. See
[building](Docs/BUILDING.md) if you are compiling the source. These are developer
inputs. A compiled player package includes its runtime and worker; end users do
not install Unity or Python themselves.

## Licence

Original program code and original technical documentation: **MIT**. Original
Earth Unscripted branding and the owner's supplied radio recordings:
**CC BY-SA 4.0**, to the extent the owner holds the relevant rights. Third-party
assets, Unity components and geographic databases keep their existing licences.
See [licence scope](LICENSE_SCOPE.md) and [asset attribution](Provenance/README.md).
There is no blanket CC BY-SA or MIT grant over third-party material.

The game was previously developed as Road Rush. Existing save locations, internal
namespaces, pack formats and bundle identifiers are retained for compatibility.
See [compatibility](Docs/COMPATIBILITY.md).

Project source: [wiffair/earth-unscripted](https://github.com/wiffair/earth-unscripted).
The repository is private during initial review. The intended project website is
**earthunscripted.com**.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) and [the roadmap](Docs/ROADMAP.md). Report
the beta version, graphics mode, platform and a reproducible public location for
bugs. Never include saves, access tokens, private addresses or downloaded caches
in an issue or pull request.
