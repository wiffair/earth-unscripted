# Vegetation asset provenance

Road Rush's broadleaf branch/twig skeleton, LOD meshes, mesh preparation code and procedural fissured-bark texture are original project work. No third-party tree-generator or tree-model code was copied. The shapes are oak-like artistic approximations suitable for Welsh scenery; they do not identify tree species from LiDAR.

Leaf colour and opacity originate from **Leaf Set 016**, ambientCG / Lennart Demes, released under **Creative Commons CC0 1.0 Universal**. Commercial modification and redistribution, including raw asset files in a game, are permitted by the provider's licence declaration.

- Original source: https://ambientcg.com/view?id=LeafSet016
- Provider licence: https://docs.ambientcg.com/license/
- Legal code: https://creativecommons.org/publicdomain/zero/1.0/legalcode
- Download: https://ambientcg.com/get?file=LeafSet016_1K-JPG.zip
- Retrieved: 5 November 2026
- ZIP size: 4,151,038 bytes
- ZIP SHA256: c624926d28eeabc266ee942a1a0dc4aa559b10d575f3a37ecaa71915cde02220

The ZIP contained no standalone licence file; the provider's explicit primary licence declaration was checked before use. Retain this notice and the source/hash record even though attribution is not required by CC0.

Changes: combined actual scanned opacity with colour; removed tiny scanner specks from the opacity mask; padded colour into transparent texels for mip filtering; subdued spring colour; mapped six scanned leaves to original cards. Runtime shaders and animation are separate project work. No provider logos or website preview render are part of the derived texture.

`broadleaf-assets.json` records exact output sizes, hashes, triangle counts and bounds. Runtime meshes are root-normalised with horizontal radius <=0.94 and height0..1; the remaining radial margin is reserved for bounded shader wind. Root scaling is independently `(crown radius, height, crown radius)`. Vertex colours encode wind weight, phase and shade variation. No raw LiDAR or absolute elevation is needed by these appearance assets.

RRBM2 meshes also store the per-leaf-card centre in UV channel1 / TEXCOORD1 as float3 XYZ, in the same normalised space as positions. All vertices in one card share that centre; bark values are zero. Leaf-card offsets were normalised isotropically while their centres follow the independently scaled tree skeleton. The runtime leaf shader must compensate relative Y offset by objectScaleX/objectScaleY around this pivot before transforming the vertex, and use rotation-only normals for leaves. Bark uses the ordinary inverse-transpose normal transformation. This avoids stretching photographed leaves when tree height and crown radius differ. The shader's bounds, wind and LOD policies are separately owned runtime concerns.

Actual Unity 6000.6 importer verification on 5 November2026 passed120 checks: hashes, geometry counts, finite bounds/normals/channels, UV1 per-card pivots, resource paths, decreasing LOD counts, leaf aspect at height/radius ratios5 and0.8, source alpha and mip coverage settings. Native appearance and performance remain unverified at this asset handoff.

Leaf vein/surface detail additionally uses the same reviewed CC0 archive's `LeafSet016_1K-JPG_NormalGL.jpg` and `LeafSet016_1K-JPG_Roughness.jpg`. No new asset download was needed. The compiled maps preserve the source atlas grid and scanned visible-leaf pixels, replace the transparent normal background with RGB(128,128,255), and replace background roughness with1. `LeafNormal` is an OpenGL +Y tangent-space normal map, with no green inversion; `LeafRoughness` stores the original scalar roughness in RGB, not smoothness or an opaque alpha-channel packing. Sample both linearly; use Unity's normal-map importer and normal unpacking for the normal. The shader converts smoothness=1-roughness. Exact hashes, roles and source derivation are in `broadleaf-assets.json`.

These additional maps do not change the 18 compiled mesh files, UV0 or UV1, topology, density, leaf colour/opacity atlas or bark atlas. Use `tools/prepare_broadleaf_assets.py --leaf-detail-only` to regenerate them without rebuilding geometry. Actual Unity6000.6 Mac verification passed14 detail-map source/hash/import/packing/roughness/readback/preservation checks; material setup also completed with no shader compilation errors. Resources map GUIDs were checked against the saved leaf material. Native PBR appearance/performance is separately verified by the main development workflow.
