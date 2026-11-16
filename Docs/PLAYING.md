# Getting started

Download the complete [Beta 52 game](https://github.com/wiffair/earth-unscripted/releases/tag/v0.23.0-beta.52),
then extract the ZIP before opening it. The game includes its engine runtime,
scenery, radio and mapping worker. Unity, Python and developer tools are only
needed if you want to build the source yourself.

## Mac

Choose **EarthUnscripted-0.23.0-beta.52-macOS-arm64.zip** for an Apple silicon Mac
(M1 or newer), running **macOS 15 or newer**. Open **Earth Unscripted Beta.app**, or drag the whole app into
Applications first. Its two graphics versions are inside the app, so moving the
app keeps everything together. This download does not support Intel Macs.

The beta is ad-hoc signed and has not been notarized by Apple. macOS may ask you
to approve the downloaded app in **System Settings → Privacy & Security**. Check
that your download came from this project's release before approving it.

## Windows

Choose **EarthUnscripted-0.23.0-beta.52-Windows-x64.zip** for 64-bit Windows 10 or 11.
Use **Extract All**, then open **Earth Unscripted.exe** in the extracted folder.
Keep the launcher and both `Players` folders together. Opening an executable
inside the ZIP, or copying just the launcher elsewhere, will not work.

The launcher is a native Windows application. It does not use Swift or require
.NET, Python or an administrator account. This beta has no publisher signing
certificate, so Windows may show a reputation warning.

## Standard or HD?

| Choice | Best for |
| --- | --- |
| **Standard** | Better performance and lighter graphics workloads |
| **HD** | Richer lighting and reflections on more capable hardware |

Both choices run the same game and share beta progress. Start with Standard if
you are unsure. Save and close the current drive before switching versions.
The Windows chooser asks you to close a running game; the Mac chooser can offer
to save and close it for you.

## Your first drive

Choose a location, prepare the area, then select a road to start driving. New
areas need internet access and may take longer to prepare on the first visit.
The game keeps downloaded and prepared areas in its adjustable caches.

Click the small map to pause, browse the larger map and choose a destination.
The route then appears on the driving map. Save locations you want to return to;
the game also remembers where you finished your last drive. Settings include
controller feedback, audio, graphics and cache sizes.

Beta progress is separate from the original live game. Standard and HD share
the beta's saves; switching graphics does not start a new career. Existing beta
saves still use the older Road Rush storage names.

## Which other downloads do I need?

**None for normal play.** The complete Mac and Windows downloads already contain
their runtime content. SceneryCore, RadioRuntime and ValidationFixtures are inputs
for [building from source](BUILDING.md). The Radio-Music ZIP is a separate MP3
collection for listening outside the game.

Ordinary play needs no third-party API account. Online data services still have
coverage and usage limits, and some optional live train feeds need registration.
See [Data services](DATA_SERVICES.md) for details and [Release status](RELEASE_STATUS.md)
for the checks performed on this beta.
