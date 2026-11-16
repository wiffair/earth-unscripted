# Building Earth Unscripted from source — developers

This guide covers compiling the game, preparing the geography worker and packaging
a release. For game downloads and setup, see [Getting started](PLAYING.md).

The source targets **Unity 6000.6.8f1**, with the package manifest/lock under `Unity/`.
Install the appropriate Mac/Windows build-support module and use an eligible Unity
licence. See [Data services](DATA_SERVICES.md) for provider setup and optional
live feeds.

## Restore release content

Download the matching content archives accompanying the source release, or use
the locally prepared copies. From the repository root, run:

```sh
python3 Tools/hydrate_content.py /path/to/EarthUnscripted-SceneryCore-2.0.0.zip
python3 Tools/hydrate_content.py /path/to/EarthUnscripted-RadioRuntime-3.0.0.zip
python3 Tools/hydrate_content.py /path/to/EarthUnscripted-ValidationFixtures-2.0.0.zip
python3 Tools/audit_repository.py
```

The tool checks the archive checksum against `Content/catalogue.json`, then checks
each file's bytes and hash. It refuses traversal, undeclared entries and mismatched
existing files. The archives restore only declared paths under `Unity/Assets`.
The reviewed VW Splittie and Hekla packs are included under `VehiclePacks`;
their source, attribution and trademark notes are in `Provenance`.
The validation archive restores the large Cardiff policy fixture unchanged; it is
needed by the production builder's checks, not by installed player downloads.

## Build the geography worker

Use Python 3.13 in a virtual environment on the target platform:

```sh
python3.13 -m venv .venv
.venv/bin/python -m pip install -r World/requirements.txt
.venv/bin/python Tools/build_worker.py
```

For Windows, build an embedded CPython worker instead. This can be staged on a
Mac or Windows build host. Download the wheels named in `World/windows-wheels.json`
from [PyPI](https://pypi.org/), then run:

```sh
python3.13 Tools/build_windows_worker.py --wheels /path/to/windows-wheels
```

The tool checks every wheel against its pinned SHA-256 and downloads the pinned
CPython embedded archive from python.org. An offline copy can be supplied with
`--python-archive`. It writes `Unity/AutomaticWorldWindowsWorker`, including Python,
GIS dependencies and time-zone data.
The Mac and Windows workers use the same pinned Python source and provider model.
Keep dependency notices in the bundle.

## Build players

Open `Unity/` in the pinned Editor, let packages/assets import, and select the
required target. The production command-line builder is:

```sh
Unity -batchmode -nographics -projectPath "$PWD/Unity" \
  -executeMethod EarthUnscripted.Editor.SharedBetaBuild.BuildPair \
  -earthUnscriptedSharedOutput /absolute/path/to/a-new-output-directory \
  -logFile /absolute/path/to/build.log
```

Use `BuildStandard` or `BuildHD` for one Mac renderer, or `BuildWindowsPair` for
Windows with that platform's worker in `Unity/AutomaticWorldWindowsWorker`.
Build the matching native chooser using [Launcher/README.md](../Launcher/README.md).
Swift is used on Mac; the Windows chooser is a separate Win32 executable.
The output directory must not already exist. Runtime package paths and graphics
adapters are shared. The builder runs relevant policy checks before building.

The builder adds the geography worker and build metadata after Unity builds the
player. On macOS, finalize the local signature after those additions:

```sh
python3 Tools/finalize_macos.py '/path/to/output/Earth Unscripted Beta.app'
python3 Tools/finalize_macos.py '/path/to/output/Earth Unscripted Beta HD.app'
```

Ad-hoc signing supports local testing; it does not provide publisher identity or
notarization for public downloads. Do not modify a signed app afterwards.

The production-policy fixtures are included. Older diagnostic commands may need
additional captured data or environment configuration.

Native haptics sources use CMake and the pinned SDL release. The optional Mac
car-picker source is in `Native/CarPicker.m`; its existing redistributable bundle
and full dependency notices are included. macOS release signing/notarization and
Windows signing require the publisher's own credentials, which are never shipped.

## Validation

`Tools/audit_repository.py` checks source hygiene and the large-content boundary.
Provider unit tests run with `PYTHONPATH=World/Source python3 -m unittest discover -s World/Tests` after
installing worker dependencies. Native UI/controller tests use the game's explicit
isolated-profile switches. See [Release status](RELEASE_STATUS.md) for completed
checks and platform testing.

## Package a download

Build the launcher, then stage both players with the packaging tool. Supply a new
output directory and keep the versioned runtime source identity unchanged:

```sh
python3 Tools/package_players.py macOS-arm64 --build /path/to/mac-build \
  --launcher /path/to/ChooseGraphics --icon /path/to/AppIcon.icns \
  --installer /path/to/EarthUnscriptedUpdateInstaller \
  --output /path/to/new-mac-stage
python3 Tools/package_players.py Windows-x64 --build /path/to/windows-build \
  --launcher '/path/to/Earth Unscripted.exe' \
  --installer /path/to/EarthUnscriptedUpdateInstaller.exe --output /path/to/new-windows-stage
```

The tool adds source manifests, runtime checks, player instructions and licence
notices. It places both Mac players inside the chooser app; Windows players sit
in `Players/Standard` and `Players/HD` beside the chooser. On Mac it signs nested
executables, refreshes worker checksums, then seals the app. Publisher signing
and notarization are separate release steps.

ZIP the complete staged `Earth Unscripted` folder, preserving Mac executable bits
and symlinks. Generate SHA-256 checksums, extract into a different path containing
spaces, and test the extracted package before uploading. Exclude Unity's generated
`BackUpThisFolder_ButDontShipItWithYourGame` folders and debug symbols.
The Windows packaged-runtime workflow checks the worker and launcher on Windows;
it also starts both Unity players headlessly. Rendered driving still needs a
Windows PC with suitable graphics hardware.

Build the native helper using [Updater/README.md](../Updater/README.md).

Create ZIPs with `Tools/archive_players.py STAGE OUTPUT.zip`. It preserves file
modes and internal symbolic links and omits Mac AppleDouble metadata files.
Do not use a ZIP writer that turns extended attributes into ordinary `._` files
inside the signed app. Verify the extracted app signature as well as the source.
