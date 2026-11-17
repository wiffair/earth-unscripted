# Vehicle-pack extensions

These optional features extend the existing `.eucar` format in Beta 17. Keep
`format: "EarthUnscriptedVehiclePack"` and `formatVersion: 1`; include the normal
identity, revision, price, physics, mesh hash and licensing fields described in
[Content packs](PACKS.md). This document describes additions, not a complete pack.

## Declare the capabilities used

Add a `requirements` object to `manifest.json`, listing only the features your
pack uses:

```json
{
  "requirements": {
    "features": [
      "engine-audio-pcm-v1",
      "layered-candy-paint-v1",
      "authored-wipers-v1"
    ]
  }
}
```

Each used extension requires its corresponding declaration. Feature names must
be unique strings, start with a lowercase letter, contain only lowercase letters,
digits and hyphens, and be at most 64 characters. A present feature list must
contain 1–8 entries. Beta 17 rejects unknown features. Older packs with none of
these extensions can omit `requirements`. Builds released before capability
validation may ignore that object; target an extension-capable game release.

## Recorded engines

Include both `engine-exhaust.wav` and `engine-mechanical.wav` at the ZIP root and
add `engineAudio` to `manifest.json`:

```json
{
  "engineAudio": {
    "formatVersion": 1,
    "minimumPitch": 0.85,
    "maximumPitch": 1.8,
    "blendStartThrottle": 0.12,
    "blendEndThrottle": 0.85,
    "exhaust": {
      "file": "engine-exhaust.wav",
      "sha256": "REPLACE_WITH_64_LOWERCASE_HEX_DIGITS",
      "referenceRpm": 900,
      "referenceRpmEstimated": true,
      "gain": 0.72
    },
    "mechanical": {
      "file": "engine-mechanical.wav",
      "sha256": "REPLACE_WITH_64_LOWERCASE_HEX_DIGITS",
      "referenceRpm": 900,
      "referenceRpmEstimated": true,
      "gain": 0.48
    }
  }
}
```

Replace each placeholder with the SHA-256 of the exact WAV bytes. Both loops and
both metadata objects are required together. Audio files without `engineAudio`
are rejected. The importer decodes PCM in managed code; it accepts:

- RIFF/WAVE, uncompressed signed little-endian PCM16, mono.
- 22,050, 44,100 or 48,000 Hz; 0.25–30 seconds; at most 4 MiB per WAV.
- Exactly one 16-byte `fmt ` chunk and one nonempty, even-length `data` chunk.
  Optional `JUNK` and `LIST` chunks are accepted. Other chunk types, incorrect RIFF
  sizes, byte rates, block alignment, duplicate chunks and missing padding fail.

Author seamless loops with safe peaks and document rights to the recordings.
The importer validates the format; it does not repair loop seams or normalise
recordings. `referenceRpm` is the engine speed represented by the source loop;
use `referenceRpmEstimated: true` when estimated.

| Field | Accepted range |
| --- | --- |
| `referenceRpm` | 300–10,000 |
| Per-loop `gain` | 0–1 |
| `minimumPitch` | 0.5–1 |
| `maximumPitch` | 1–2.5 |
| `blendStartThrottle` | 0–0.9 |
| `blendEndThrottle` | 0.1–1; greater than start |

For exhaust gain `e`, mechanical gain `m` and throttle blend `b` from 0 to 1,
the target weights are `e × (1 − 0.5b)` and `m × (0.1 + 0.9b)`. The importer
requires `max(e + 0.1m, 0.5e + m) ≤ 1`. Leave additional headroom for other game
audio. Master volume and engine volume multiply the mix. Pitch follows engine
RPM divided by the loop's reference RPM, clamped to the supplied pitch range.
Menus pause the owned voices; resuming continues those clips. Packs without this
extension retain their existing synthesized or VW-recorded engine path.

## Layered paint

In a material entry in `mesh.json`, add an optional `finish` object alongside the
existing `colour`, `metallic`, `smoothness` and `paint` fields:

```json
{
  "finish": {
    "kind": "layered-candy-v1",
    "transmission": [0.8, 0.3, 0.2],
    "opticalThickness": 1.0,
    "flakeStrength": 0.3,
    "flakeScalePerMetre": 850,
    "clearcoat": 1.0
  }
}
```

| Field | Accepted range |
| --- | --- |
| `transmission` | Exactly three linear RGB values, each 0.001–1 |
| `opticalThickness` | 0.1–3 |
| `flakeStrength` | 0–1 |
| `flakeScalePerMetre` | 100–2,000 |
| `clearcoat` | 0–1 |

`kind` must be `layered-candy-v1`. Transmission describes a colour coat over an
opaque substrate, not glass transparency. `paint: true` uses the player's paint
colour and participates in paint patterns and dirt; otherwise the material uses
its authored colour. Metallic and smoothness still come from the material.
Standard and HD share the coat model while using their respective lighting paths.
Materials without `finish` keep their existing shader path.

## Authored wipers

Add paired pivots and sweep angles to `manifest.json`:

```json
{
  "wipers": {
    "formatVersion": 1,
    "pivots": [[-0.4, 1.295, 0.835], [0.2, 1.295, 0.835]],
    "sweepDegrees": [70, 70]
  }
}
```

Supply mesh parts attached to both `wiper0` and `wiper1` in `mesh.json`. Wiper
vertices are local to their corresponding pivot, with the authored parked pose
at zero rotation. Coordinates are metres: +X right, +Y up, +Z forward. Pivot
positions are relative to the vehicle root. Animation rotates each pivot around
its local Z axis from 0 to its signed sweep angle.

There must be exactly two pivots and two sweeps. Each pivot has three finite
coordinates: X −2–2, Y 0–3 and Z −3–3. Each sweep must lie between −100° and
100°, with magnitude at least 10°. Metadata requires both attachments, and wiper
attachments require metadata. Multiple mesh parts can share a wiper attachment.

Author the geometry and sweeps to fit the actual glass; the numeric bounds do not
certify coverage or prevent a blade hitting the frame. These pivots replace the
automatically fitted mechanism. Packs without authored wipers keep the existing
fitting behaviour. Check both parked and full-sweep poses from inside and outside.

## Archive and release checks

The archive remains a data-only ZIP: no executable plug-ins or external audio
paths. Its only accepted root filenames are `manifest.json`, `mesh.json`,
`texture.png`, `README.txt`, `LICENSE.txt` and the two engine WAV names above.
Names are case-sensitive; duplicates and additional entries are rejected.

The archive limit is 32 MiB, with 48 MiB total expanded data. Manifest JSON is
limited to 32 KiB, each text file to 64 KiB and each WAV to 4 MiB. Existing mesh,
texture and geometry limits still apply: at most 80,000 vertices and 360,000
triangle indices in total, 16 materials and 128 parts. Optional textures fit
within 2,048 × 2,048 pixels.

Increase the pack version and revision when publishing changed content, and
publish the new archive hash. Test the normal import, purchase, car swap, menu
pause/resume, cockpit and wiper sweep in both Standard and HD. Include licensing
and attribution for the mesh, texture and every recording; successful import
is not a redistribution licence.
