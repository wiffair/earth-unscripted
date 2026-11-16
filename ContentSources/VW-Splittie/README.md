# VW Splittie — Peace & Seafoam 2.0.0

Free camper pack for Earth Unscripted beta 2 and later. Download the `.eucar`,
open **Cars & Garage → Car Packs**, import it and select the camper while stopped.
Its two-tone livery is fixed. The current game includes the recorded engine loops
and tailpipe effect used by this pack.

## Source files

- `mesh.json`, `manifest.json`, `README.txt`, `LICENSE.txt`: exact reviewed pack payloads.
- `build_model.py`: original editable Blender modelling source, updated for this project.
- `pack_reviewed.py`: builds an archive from the frozen reviewed payloads.
- `AudioSource/`: credited original MP3 preview inputs.
- `RecordedAudio/`: processed engine loops and attribution.
- `prepare_audio.py`, `audio-processing.json`, `audio-sources.json`: processing and integrity records.
- `PROVENANCE.md`, `LICENCE.md`, `review.json`: review conclusions and rights scope.

Repack the reviewed car with Python:

```sh
python3 pack_reviewed.py
```

Edit/regenerate the model in a fresh Blender process (reviewed with Blender 4.3.2):

```sh
blender --background --factory-startup --python build_model.py
```

The script clears its own scene and writes `Generated/candidate-mesh.json`.
Export ordering and computed normals can vary between Blender runs. Keep the reviewed payload for
2.0.0; edited models need a new pack version, revision and mesh hash. The importer
rejects conflicting content under the same revision.

`prepare_audio.py` uses FFmpeg and NumPy to regenerate the loops from the credited
inputs. Codec/library changes can affect exact sample bytes; compare against the
reviewed processing hashes before publishing a new revision.

The source checkout includes the vehicle in StreamingAssets for future builds.
Existing beta 2 game downloads can add it through the importer. This upload does
not alter those published player archives.
