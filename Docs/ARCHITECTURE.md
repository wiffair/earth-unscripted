# Architecture

The repository contains the game, geography worker, native integrations and build
tools. Standard and HD share gameplay and save formats, with separate rendering
builds.

| Component | Purpose |
| --- | --- |
| `Unity/` | Gameplay, menus, graphics, world presentation and tests |
| `World/` | Geography worker, provider catalogue and data converters |
| `Native/` | Platform integrations, including wheel feedback and file selection |
| `Launcher/` | Mac and Windows Standard/HD choosers |
| `Content/` | Versioned content catalogue and restoration metadata |
| `Provenance/` | Asset origins, licences and attribution |

## Geography and providers

Provider adapters convert maps, terrain, timetables and reported train movements
into a common geographic model. Each source records its coverage, age, quality,
account requirements and attribution. The worker selects an eligible provider,
validates the returned data and uses a fallback where needed.

The game checks geometry and clearance before placing roads, tracks and traffic.
Caches store downloaded data, prepared areas and finished scene geometry in the
player's local storage.

## Content and releases

Large scenery resources, radio recordings and optional packs are versioned
separately from the code. Their manifests include attribution and SHA-256 hashes.
GitHub Releases holds the archives; the hydration tool checks each archive before
restoring it into a source checkout.

Player downloads contain the compiled game and runtime content. Vehicle, radio
and world packs have their own versions and import formats.
