# NLayer audio decoder

Earth Unscripted uses the managed NLayer MPEG decoder under its upstream MIT licence. Its notice is included in this folder and retained with the source.

Upstream: https://github.com/naudio/NLayer
Pinned commit: `82f7a92e2c0b994157720c71b7d3cb41095adc70` (3.0.1 runtime source).

`UPSTREAM.json` records the original archive, original file hashes and locally retained source hashes. `LOCAL-CHANGES.patch` records conservative game-import size limits; decoder arithmetic is unchanged. No NAudio integration, upstream audio/test media, AssemblyInfo attributes, native binary or external decoding program is included. The decoder runs as C# on the game's existing runtime.
