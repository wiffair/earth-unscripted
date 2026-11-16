# Contributing to Earth Unscripted

Use a separate branch and keep changes focused. Explain the player-visible problem,
the resulting behaviour, how it was verified and any unresolved limitations.

Original code contributions use the project's MIT licence. Original artwork or
music contributions must explicitly declare their licence and provenance; the
project's preferred creative licence is CC BY-SA 4.0. Do not relicense third-party
material. Only contribute material you have authority to share.

Keep gameplay shared between Standard and HD. Isolate renderer-specific materials
and graphics services behind the existing adapters. Preserve serialized IDs, pack
contracts and save compatibility. Prefer global geography fixes over exceptions
for a single town or junction.

Run relevant build/policy checks and verify visible UI changes in a native player.
Use disposable profiles for automated tests. Do not test migrations against a
player's only save. Controller actions must use the same callbacks, validation
and confirmation paths as pointer input.

Do not commit Unity `Library`, `Temp`, `UserSettings`, build outputs, world caches,
career files, private keys, provider credentials or reference-only media. Keep
large content in versioned release assets, accompanied by manifests and checksums.

New providers need capability/coverage, attribution, timestamps, coordinate and
vertical references, adapter version, account requirements and a clear fallback.
Live observations must not be labelled as exact physical positions unless their
track binding and freshness have been validated.
