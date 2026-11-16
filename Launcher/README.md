# Graphics launchers

Both launchers offer **Standard** and **HD**. They open matching builds of the
same Unity game and share beta saves. This guide covers building the launchers.

## Mac

`GraphicsLauncher.swift` uses AppKit and GameController. Compile it on a Mac:

```sh
swiftc -O -target arm64-apple-macos15.0 -framework AppKit -framework Foundation -framework GameController \
  Launcher/GraphicsLauncher.swift -o ChooseGraphics
```

`LaunchManifest.json` contains the version, gameplay source hash and two choices.
Paths relative to the launcher's Resources folder make the packaged app portable;
absolute paths remain supported for existing local installations. Each choice is
checked against the player's bundle identifier, version and source manifest.

The launcher supports pointer, keyboard and controller input. If the other game
version is running, it offers to save and close it before switching.

## Windows

`Windows/GraphicsLauncher.c` is a native Win32 chooser. Build with a Windows x64
MinGW-w64 toolchain (LLVM-mingw was used for Beta 52), from `Launcher/Windows`:

```sh
x86_64-w64-mingw32-windres launcher.rc -O coff -o launcher-resource.o
x86_64-w64-mingw32-clang -std=c11 -O2 -Wall -Wextra -Werror -municode -mwindows \
  -static GraphicsLauncher.c launcher-resource.o -o 'Earth Unscripted.exe' \
  -luser32 -lgdi32 -lshell32
```

The chooser resolves `Players/Standard` and `Players/HD` beside its own executable,
checks the game version/source manifest and starts the selected player directly.
It supports keyboard, pointer and XInput controllers. A running beta must be
closed before another is opened.

`--check` validates the two packaged player paths and identities without starting
the game. `--ui-smoke` opens the chooser briefly and exits for automated checks.
See [packaging](../Docs/BUILDING.md) for the complete folder layout.
