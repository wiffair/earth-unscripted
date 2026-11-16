# Playing a compiled game versus building the source

**Players do not need to install Unity.** A compiled Earth Unscripted download can
include the Unity player runtime, game code, required content and the self-contained
geography worker. Players do not need Unity Hub, Unity Editor, a Unity account,
Python, Swift or a developer toolchain to run a correctly packaged game.

Unity Editor and an eligible Unity licence are needed by developers compiling or
modifying the project. The Unity runtime included in a compiled game remains under
Unity's terms; the project's MIT licence does not relicense the engine.

## Which download should I choose?

| Download | Audience | What to do |
| --- | --- | --- |
| Compiled Mac game package | Players on a supported Mac | Install/extract the complete package and open its `.app` launcher. No Unity installation is needed. |
| Compiled Windows game package | Players on a supported Windows PC | Extract/install the complete package and run its launcher or game `.exe`. Keep the adjacent data, DLLs, plugins and worker files together. No Unity installation is needed. |
| Source ZIP or Git clone | Developers/contributors | Follow [Building](BUILDING.md): install the pinned Unity Editor and build dependencies, restore content, then compile. |
| SceneryCore, RadioRuntime and ValidationFixtures archives | Source builders | Restore with the hydration tool as documented in Building. Installed players should receive required runtime content in their compiled package; they do not need development fixtures. |
| Radio-Music archive | Listeners | Optional tagged MP3 collection; not an executable game or a radio importer pack. |

**Current GitHub download status:** the Beta 52 release contains source and content
archives, not a ready-to-play Mac or Windows installer. The installed Mac beta
tested during development is separate from those GitHub downloads. Compiled game
packages can be added as release assets after packaging and target-platform checks.
New Windows native validation and publisher signing/notarization remain release work.

## Does the Swift launcher work on Windows?

**No. `Launcher/GraphicsLauncher.swift` is Mac-only.** It uses Apple's AppKit,
GameController and Darwin libraries. It is the small Standard/HD graphics chooser,
not the game engine. A player receives a compiled launcher `.app`; they do not
compile the Swift source themselves.

The shared Unity/C# game source has separate Mac and Windows build targets.
`SharedBetaBuild.BuildWindowsPair` produces separate Standard and HD Windows game
folders/executables. The repository does not currently provide an equivalent
Windows chooser. Initially, a Windows package could expose two labelled shortcuts;
a Windows-compatible chooser can later present the same Standard/HD choice.
That work does not require a separate implementation of the game logic.

## Online data is separate from installing the game

Playing prepared areas with local simulation can work within installed/cached
coverage. Exploring new areas and using online map/routing, live weather or train
feeds needs the corresponding network service. Normal public data endpoints do
not require players to create API accounts; optional services and usage restrictions
are explained in [Data services](DATA_SERVICES.md).
