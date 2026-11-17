# Kessock bridge regression data

This Editor-only fixture reproduces issue #33. It contains the two A9 carriageways
and two parallel bridge cycleways, with the prepared approximate height profiles
that caused the collision. It is test data and is not included in player content.

Road/path geometry and tags derive from OpenStreetMap ways
[33911529](https://www.openstreetmap.org/way/33911529),
[4475944](https://www.openstreetmap.org/way/4475944),
[38129651](https://www.openstreetmap.org/way/38129651) and
[38129652](https://www.openstreetmap.org/way/38129652).
© OpenStreetMap contributors; database derivatives retain
[ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/) obligations.
See [OpenStreetMap attribution](https://www.openstreetmap.org/copyright).

The acquisition receipt identifies **Mapzen/Terrain Tiles Terrarium**, sampled at
approximately 32m, as the elevation fallback used for these derived profiles.
It does not identify the underlying DEM at each pixel. No fine LiDAR is claimed.
The failed Welsh coverage lookup in the full receipt supplied no raster for this
Scottish area. Terrain Tiles was accessed on 16 November 2026 from the
[AWS dataset](https://registry.opendata.aws/terrain-tiles/).

Constituent terrain terms and credits remain applicable:
[Tilezen source attribution](https://github.com/tilezen/joerd/blob/master/docs/attribution.md).
For European terrain: Produced using Copernicus data and information funded by
the European Union - EU-DEM layers. Global SRTM/GMTED2010 source data credit the
U.S. Geological Survey. The linked source notice also retains the applicable
Environment Agency/OGL terms where that source contributes to UK tiles.

Coordinates were resampled and heights fitted by the game worker. These values
are approximate game geometry, including the incorrect path profiles preserved
for the test, rather than surveyed deck heights. Buildings, trees and owner data
are omitted. The four zero grid values are an unused fixture placeholder.

Exact source URLs, hashes, generator identity and retrieval times are in
[KessockBridgeProfilesFixture.provenance.json](KessockBridgeProfilesFixture.provenance.json).
This data is excluded from the repository's blanket MIT code and CC BY-SA artwork
grants; its source data terms apply.
