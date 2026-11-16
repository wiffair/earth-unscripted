# Mac graphics chooser

`GraphicsLauncher.swift` builds the **macOS** Standard/HD chooser. It imports
AppKit, Foundation, GameController and Darwin and is not a Windows launcher.
Players run its compiled `.app`; Swift/Xcode are developer build tools only.

The chooser opens separately compiled Standard or HD Unity players described by
its packaged `LaunchManifest.json`, with version/source checks and shared beta
storage. The game itself is shared Unity/C# source, not Swift.

The Windows build entry points produce standalone Standard/HD executables, but a
matching Windows chooser is not implemented in this repository. A Windows release
needs a compatible chooser or clearly labelled shortcuts to those executables.
See [playing versus building](../Docs/PLAYING.md) and [build instructions](../Docs/BUILDING.md).
