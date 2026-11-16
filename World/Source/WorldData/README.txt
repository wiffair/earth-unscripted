WORLD DATA MODEL AND PROVIDER FRAMEWORK — schema1 / version1.0.0

Source adapters → geographic common envelopes → explicit admitted gameplay data.
The same catalogue.json ships in the player and its self-contained world worker.
The UI exposes discovery status at Settings > World Data Sources. Discovery does
not claim that an eligible feed is connected or that every country has LiDAR.

Catalogue selection uses country aliases, geographic bounds, capability, priority,
credentials, temporary availability and dataset dates. Geographic bounds are broad
discovery envelopes: survey pixels and actual service routes establish coverage.
Country rules are source-overridable defaults, not complete clearance standards.
Adding a compatible regional feed normally needs a catalogue entry and network/
track bindings. A new format needs an adapter; gameplay need not parse its raw data.

Common envelopes identify schema, family, provider, adapter/version, source edition,
source hash, attribution, receipt UTC, validity and quality. Geographic coordinates
are WGS84 longitude/latitude. Speeds are m/s; source train observation time is never
replaced by feed publication time. DTM ground and DSM surface remain separate.
Raster metadata retains native horizontal CRS, vertical datum, resolution and unit
conversion. It does not perform an unsupported vertical transformation. The existing
world pipeline still handles native raster reprojection/sampling; the new observer
adds bounded provenance records without altering prepared numeric geometry.

Families implemented: train.services, train.observations, map.features, terrain.raster.
Adapters: GTFS Schedule ZIP; GTFS Realtime protobuf or protobuf JSON; Network Rail
complete JSON Schedule plus stateful TRUST activation/movement/cancellation/
reinstatement; Finnish Digitraffic train GeoJSON; OSM nodes/ways and unassembled
relation members; georeferenced single-band elevation raster metadata.

GTFS calendars, calendar exceptions, agency timezone, clocks beyond24hours and
local-noon-minus12hours service-day timing follow the published specification.
Full/differential realtime state, deletes, cancelled services, stop delays and skipped
stops are preserved. Network Rail STP cancellation/overlay precedence is supported
for a COMPLETE extract. Merge daily/incremental schedule feeds before conversion.
TRUST without an activation remains an opaque, unmapped reporting point.

The game bridge needs explicit admitted pathId/sourceWays, stockId, direction,
stop stationing and optionally report-point or geographic track stationing. It
never draws straight rails between stations or guesses an unknown train allocation.
GPS is projected only onto that explicit mapped path within a bounded distance.
Different service dates do not share cancellations. Unbound services are reported
and omitted from physical playback. Existing C# train-snapshot validation remains
in charge of actual source geometry, speed, gauge, whole-consist and clearance.

Cache identity includes provider, source edition/hash, adapter version and conversion
policy. Writes are atomic and bounded. Positions expire after120seconds; cancellation
state may remain for the bound service run (maximum48hours). Static terrain receipts
can remain by immutable source hash. Published timetables retain explicit validity.
The world observer's separate16MiB metadata cache does not flush the existing world
or scenery caches. Normalised quality is never promoted from estimate to measurement.

CLI (run from the Car Game workspace):
  PYTHONPATH=AutomaticWorld python -m WorldData resolve -3.18 51.48 GB terrain
  PYTHONPATH=AutomaticWorld python -m WorldData convert gtfs-schedule INPUT.zip OUTPUT.json --provider PROVIDER_ID --country COUNTRY --date YYYY-MM-DD --attribution "Source attribution"
  PYTHONPATH=AutomaticWorld python -m WorldData convert gtfs-realtime INPUT.pb OUTPUT.json --provider PROVIDER_ID --country COUNTRY --attribution "Source attribution"
  PYTHONPATH=AutomaticWorld python -m WorldData export-game SCHEDULE.json BINDINGS.json GAME.json --observations OBSERVATIONS.json --stock-ids gb.197.2

The packaged RoadRushWorldWorker provides the same commands after --world-data;
no external Python installation is required by the beta. Exported GAME.json can
be published by an authorised collector or atomically placed at the existing
BetaData/TrainData/services.json input. The converter itself performs no automatic
network fetch or account registration. Long-lived realtime collectors must retain
RealtimeState/NetworkRailTrust instances; a one-shot differential file needs its
preceding complete state. Credentials belong to that collector, never a shipped app.

Current regional sources: existing Britain/Switzerland surveys and global coarse
terrain, OSM, published South Wales timetable, Network Rail discovery requiring
credentials, and public Finnish train-location discovery. The catalogue is deliberately
small and extensible, not an exhaustive global feed directory. The attempted public
Finnish request returned HTTP406 in this environment; live-source validation is
recorded as unavailable. Its adapter is tested against captured specification fixtures.
UK account integration, broker transports, actual reference tables/report-to-OSM
mapping and additional country providers remain to connect and verify. Physical
stock currently remains UK standard gauge; international data support does not
silently assert suitable vehicles/clearances for another network.

Specifications consulted15November2026:
https://gtfs.org/documentation/schedule/reference/
https://gtfs.org/documentation/realtime/reference/
https://www.networkrail.co.uk/who-we-are/transparency-and-ethics/transparency/open-data-feeds/
https://wiki.openraildata.com/index.php/Schedule_Records
https://wiki.openraildata.com/index.php/Train_Movement
https://www.digitraffic.fi/en/railway-traffic/
https://www.digitraffic.fi/en/terms-of-service/
