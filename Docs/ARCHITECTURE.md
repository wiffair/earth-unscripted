# Architecture and repository boundaries

Start with **one main repository**. Keep Unity gameplay, the geography worker,
provider models, native adapters, launcher and build documentation together so a
single version identifies a compatible game/worker pair. Standard and HD share
gameplay and save formats; they are two rendering builds, not two projects to
maintain independently.

Keep large **content releases** outside ordinary Git history: scenery resources,
fictional radio recordings and optional vehicle/world packs have versioned
manifests, attribution and SHA-256 checksums. GitHub Releases is a suitable delivery
mechanism. The hydration tool takes a local release ZIP and verifies it before
restoring its exact files into the Unity project. This avoids requiring Git LFS
for the 128 MiB generated tree-view asset.

A separate `earth-unscripted-content` repository can later hold pack metadata,
authoring sources and its own contribution process if contributors need that
boundary. A separate `earth-unscripted-world` library is useful only when another
application consumes the provider/geography engine independently. Splitting now
would create version coordination without adding a current consumer.

Provider-specific converters normalize map, terrain, timetable and live movement
data into the existing common geographic model. Capability/coverage, source age,
quality, account requirements and attribution travel with that data. Runtime
placement still validates actual geometry and safe coverage; discovery is not a
promise that a service has live coverage at every point.

Caches contain downloaded data, prepared geographic products and finished scene
geometry. They are per-user runtime state, never source-repository content.
Optional radio/car/world packs remain independently versioned.
