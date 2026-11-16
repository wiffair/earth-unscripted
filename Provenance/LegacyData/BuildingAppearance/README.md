# CC0 shared building surfaces

Prepared 5 November2026 from Poly Haven CC0 assets. See manifest.json and source-review.json for exact asset URLs, author names, licence evidence, source checksums, local archive hash and modification notice. The local ZIP is a deterministic review bundle, not a provider-issued archive. Source JPEG total7,935,053bytes(7.57MiB); four1K colour/OpenGL+Y normal/roughness sets.

Physical repeats: Brick1.4×1.4m; Render1×1m; Stone2.57×2.57m; Slate3×3m. These are provider dimensions, not a uniform2m assumption. Apply metric meshUVs or a material texture-scale multiplier. Use UV.v upward on walls and up the roof slope; do not rotate one material map independently. No baked windows/doors; root adds metric façade features.

Resources.Load<Texture2D>("BuildingAppearance/{Brick|Render|Stone|Slate}{Colour|Normal|MetalSmoothness}") resolves the intended resource names. Colour JPGs and normal JPGs unchanged. MetalSmoothness PNGs contain RGB0 and alpha255-sourceRoughness; read linear. Importers select sRGB colour, normal map+Y(no green flip), linear masks, Repeat, mipmaps, trilinear, anisotropy4, max1024, streaming mips and normal compression. Reuse these maps/materials across buildings; do not duplicate them per building.

Run offline preparation with PYTHONPATH=/private/tmp/roadrush-gis-tools python3 tools/prepare_building_appearance.py. It verifies pinned sourceSHA256, regenerates only the owned assets and manifest, and does not launch Unity or download anything.

Reviewed the generated colour-review-strip.png for material content: no doors/windows/signage are baked into the selected wall surfaces. This is a texture review, not a claim of in-game appearance; Unity import, lighting, scale and normal tangents still require parent integration/capture. The render source is a worn neutral grey suitable for tint variants, rather than a prepainted cream façade. Slate source has natural weathering/moss variation.

Primary licensing: https://polyhaven.com/license and https://creativecommons.org/publicdomain/zero/1.0/. Poly Haven permits commercial use and redistribution of downloadedCC0 assets; attribution is appreciated but not required. This is a static asset inclusion, not a liveAPI dependency. PublicAPI terms checked at https://github.com/Poly-Haven/Public-API/blob/master/ToS.md. No paid service or account enabled; no agreement click-through performed.

## Actual Unity GPU verification

BuildingAppearanceAssetVerification passed168checks in Unity6000.6.8f1 on this Mac, log `/private/tmp/rr015-building-appearance-assets3.log`; result `Builds/Verification/building-appearance-assets.txt`. Actual imported metallic/smoothness format is DXT5: source roughness versus GPU-read smoothness alpha mean absolute error0–0.00811(max0.05098). Normal maps decoded by Unity shader retain GL+Y orientation, mean source/readback dot0.99490–0.99979. No base readability setting was changed; test verifies baseasset/metafile hashes stay unchanged. This establishes imported asset/material correctness, not final gameplay appearance.

The test found and corrected an initial alpha-import bug: packed maps require FromInput alpha (`alphaUsage:1`). Without it Unity selected DXT1 and discarded smoothness alpha. Offline preparation now preserves that setting; verification asserts it and tests actual GPU samples to catch regressions.
