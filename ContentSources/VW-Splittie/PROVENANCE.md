# VW Splittie — provenance review

Reviewed 16 November 2026 for Earth Unscripted. The reviewed vehicle is
**VW-Splittie-2.0.0.eucar**, a free, fixed-livery split-window camper.

## Model and livery

The mesh is original project work generated from Blender primitives, curves and
boolean operations. `build_model.py` contains the modelling source: coachwork,
window openings, interior, wheels, curtains, daisies, peace motifs, roof luggage
and the modelled VW monogram. Its twelve material definitions use numeric colours
and surface parameters. The pack contains no external model, raster texture,
photograph, font or downloaded logo image.

The exact `mesh.json` bytes and physics settings match the archived 1.1.0 model
from the original local generator. Version 2.0.0 changes the project identity and
metadata while preserving that model and tuning. `review.json` records the source
and payload hashes. The native beta 2 extended suite loaded this exact camper
archive and passed 164 vehicle/terrain checks.

Blender exports can vary in object/vertex ordering and computed normal data.
A fresh Blender 4.3.2 run reproduces the oriented triangle positions and materials. `build_model.py` writes an
editable candidate and reports its hash; it does not overwrite the reviewed
payload or publish changed geometry under the existing version. The frozen
`mesh.json` plus `pack_reviewed.py` reproduce the reviewed release payload.

## Recorded engine sound

The vehicle archive contains geometry and configuration. Earth Unscripted's
runtime supplies these separately credited audio loops:

| Runtime loop | Recording and author | Licence | Processing |
| --- | --- | --- | --- |
| `vw-idle.wav` | [Volkswagen Beetle 1961](https://freesound.org/people/klankbeeld/sounds/544344/) — klankbeeld | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | 1.5–7.5 s excerpt; mono 44.1 kHz; filtering, DC removal, crossfade, smoothing and normalization |
| `vw-load.wav` | [1974 VW Bug recording](https://freesound.org/people/be_a_hero_not_a_patriot/sounds/331707/) — be_a_hero_not_a_patriot | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) | 91–97 s excerpt; the same loop processing |

Both source pages' audio licence links were checked on the review date. The
archived MP3 inputs match the current public high-quality previews byte-for-byte.
The processed WAV hashes match the runtime files and the existing SceneryCore
2.0.0 content archive. `audio-sources.json` and `audio-processing.json` identify
the exact inputs, transformations and outputs; `RecordedAudio/ATTRIBUTION.txt`
retains the full credits. Reprocessing both source previews reproduces the exact
runtime WAV bytes. Repacking the frozen payload also reproduces the exact released
archive bytes. The recordings are of Beetles, used as an approximate
air-cooled flat-four sound for this camper.

The photograph displayed on the first Freesound page has separate CC BY-SA 3.0
credits. That photograph is absent from the model and distributed assets. The
user's YouTube sound reference is also absent from the downloads.

## Licence scope and brand identifiers

Original modelling/build scripts use MIT. Original model and livery artwork are
also offered under CC BY-SA 4.0, while the existing MIT permission in the unchanged
vehicle archive remains available. Recipients may use either grant for original
project artwork; see `LICENCE.md`. The audio keeps its separate CC BY / CC0 terms.

Volkswagen names, the depicted emblem and vehicle design identifiers remain
subject to their owners' rights. This source-and-copyright-licence review does not
supply a Volkswagen trademark or design licence, and no affiliation or endorsement
is claimed. Project copyright grants cover only rights the contributors hold.
