# Data services and optional accounts

Checked against the Beta 52 source and provider documentation on **16 November
2026**. Provider terms and endpoints can change; the linked provider pages take
precedence. This page describes current connections, not planned capabilities.

## What do I need?

For a compiled game download, **players do not install Unity, Unity Hub or Python**.
Those are developer requirements when compiling from source. The current GitHub
release contains source/content, not compiled player installers. See [Playing](PLAYING.md).

| What you want to do | Account needed? | Start here |
| --- | --- | --- |
| Play an installed build with prepared areas and local simulation | No third-party API account | Choose a prepared location and simulated weather/trains. Offline coverage is limited to installed/prepared data. |
| Search places, prepare new roads, use online map/routing or fetch weather | No API key for the currently used public endpoints | Internet access and the services below are needed for the corresponding online feature. Availability and usage limits apply. |
| Get detailed terrain in supported countries | No key for the public survey endpoints currently implemented | Terrain is selected by location; actual survey coverage varies. |
| Use public international train feeds | No key for the selected public feeds below | Enable live train data in the train settings. This does not guarantee nearby usable trains. |
| Connect broader UK schedules/live movements | **Optional Network Rail registration** | Read the Network Rail section: account integration is not yet turnkey. |
| Build the Unity application | Unity account and an eligible Unity licence | [Building](BUILDING.md); download the pinned Editor through [Unity Hub](https://unity.com/download). Licence eligibility is separate from MIT. |
| Upload source or contribute on GitHub | GitHub account for uploading/contributing | [Private-first GitHub release](GITHUB_RELEASE.md). Players do not need a GitHub login to run downloaded builds. |

The game does not require an OpenAI/ChatGPT API account, Google Maps key, Mapbox
account or paid AI subscription to generate roads and scenery. Runtime geography
uses map/elevation algorithms, not an AI API. No provider credentials are supplied
by this repository, and no registration is performed automatically.

## Maps, places, routing and weather

These services have different jobs. An OpenStreetMap editing account is not an
API subscription and does not increase the limits of other hosts.

| Service | Why it is used | Account and setup | Current connection and limits |
| --- | --- | --- | --- |
| [OpenStreetMap data](https://www.openstreetmap.org/copyright), [Overpass](https://wiki.openstreetmap.org/wiki/Overpass_API) and [Geofabrik downloads](https://download.geofabrik.de/) | Road/rail geometry, buildings, water, land cover and country data | No account for the public extracts/queries used here. Choose a location or prepare a country in the game. | `map.osm`. Worker `map_transport.py` uses public Overpass and Geofabrik sources; `GlobalRegions.cs` also has Overpass fallbacks. Overpass hosts include atownsend.org.uk, overpass-api.de and overpass.private.coffee. These names identify independent map servers, not coffee purchases or accounts. They can throttle, time out or be unavailable. OSM attribution/ODbL obligations remain. |
| [OSM raster tiles](https://operations.osmfoundation.org/policies/tiles/) | Background of the large, labelled map | No account. Requested when displaying online map tiles. | `NavigationAtlas.cs` uses `tile.openstreetmap.org`. Visible attribution, an identifying User-Agent and HTTP-aware caching are required. The public server prohibits bulk tile downloads/prefetch. Road-data preparation is a separate operation and is not permission to pre-download map images. |
| [OSRM](https://project-osrm.org/) | Calculate a driving route between the car and a selected destination | No key for the public demonstration endpoint currently used | `NavigationAtlas.cs` calls `router.project-osrm.org`. This is a shared demonstration service, not a guaranteed production backend. A public game with substantial traffic needs a suitable hosted or self-hosted routing service. Buying another provider's key does not switch this code automatically. |
| [Open-Meteo geocoding](https://open-meteo.com/en/docs/geocoding-api) | Convert a typed place name into coordinates | No key for the current public endpoint | `GlobalRegions.cs` uses `geocoding-api.open-meteo.com`, not Nominatim. Place-name attribution includes GeoNames. Coordinate-based selection can avoid a place-name lookup. |
| [Open-Meteo weather and elevation](https://open-meteo.com/en/docs), [terms](https://open-meteo.com/en/terms) | Optional current weather; coarse elevation for legacy area preparation | No key for eligible non-commercial use of the free API | `EnvironmentController.cs` and `GlobalRegions.cs` use `api.open-meteo.com`. Fixed/simulated weather needs no live request. Current free-service terms limit use to non-commercial applications and impose call limits. MIT allowing commercial forks does **not** grant commercial access to this hosted API. Commercial/high-volume deployments need suitable service terms and implementation of the chosen endpoint/authentication; a purchased key is not currently a menu setting. |

The app contains public endpoint URLs in source, not a universal API-key settings
page. Deployers must change and validate the appropriate adapter when choosing a
different host. Do not assume every source URL can be replaced by an arbitrary
URL with the same result. Keep attribution and source-specific formats intact.

## Terrain and LiDAR

No account or subscription is required by the implemented public terrain
connections. The worker selects a candidate from `World/Source/WorldData/catalogue.json`
by country, bounds, capability and priority; it validates actual raster pixels.
Missing coverage or a temporary service failure can fall back to coarser terrain.
Registering an account does not create survey coverage where none is published.

| Provider ID / coverage | Why and where | How it is used / limitations |
| --- | --- | --- |
| `terrain.gb` — Britain | Welsh Government/NRW survey catalogues through [DataMapWales](https://datamap.gov.wales/); England surveys from the [Environment Agency](https://environment.data.gov.uk/survey) | Automatic British binding in `elevation_provider.py`. DTM ground and DSM surface stay distinct. This is not complete high-resolution coverage of every UK country; retain each dataset's terms, including OGL where applicable. |
| `terrain.ch` — Switzerland | [swisstopo open geodata](https://www.swisstopo.admin.ch/en/open-government-data) | Public STAC/survey download in `elevation_provider.py`; partial/absent survey coverage uses the coarse background. Retain swisstopo conditions. |
| `terrain.nl.ahn` — Netherlands | [AHN open data](https://www.ahn.nl/open-data), delivered by PDOK | Public WCS DTM/DSM crops; binding `nl-ahn-v1` in `WorldData/public_terrain.py`. Numeric height data is used, not a coloured hillshade image. Catalogue records CC0. |
| `terrain.ca.hrdem` — Canada | [NRCan elevation downloads](https://download-telecharger.services.geo.ca/pub/elevation/), including the [HRDEM WCS guide](https://download-telecharger.services.geo.ca/pub/elevation/dem_mne/HRDEMmosaic_mosaiqueMNEHR/HRDEM_Mosaic_WCS-WMS_instructions_EN.pdf) | Public DTM/DSM crops from `datacube.services.geo.ca`; binding `ca-hrdem-v1`. Coverage varies. Retain Open Government Licence–Canada notices. |
| `terrain.us.3dep` — United States | [USGS 3DEP](https://www.usgs.gov/3d-elevation-program) | Public ImageServer numeric heights; binding `us-3dep-v1`. A 1 m output grid does **not** prove the underlying survey is 1 m LiDAR. Retain source-specific notices. |
| `terrain.global` — coarse fallback | Public [Terrain Tiles](https://registry.opendata.aws/terrain-tiles/) / Mapzen Terrarium background | `coarse_provider.py` reads public S3 tiles. An AWS account/key is not needed for these anonymous public object URLs. This is approximate background elevation, not worldwide fine LiDAR. |

Horizontal CRS, native vertical datum and resolution are recorded separately.
The converter does not claim an unsupported vertical-datum transformation. See
[provenance](../Provenance/README.md) for data notices; the project's MIT/CC grants
do not replace the geographic datasets' licences.

## Trains without an account

Select the live-service option in the game's train settings to use public data.
With no custom feed URL or local `TrainData/services.json`, the game can query its
local geography worker, which chooses the eligible account-free regional feed.
Local simulation requests no live rail data. Timetable fallback is separately
controlled in train settings. See `TrainServiceFeed.cs` and `WorldData/public_rail.py`.

| Provider ID / region | Account and official information | Current support and fallback |
| --- | --- | --- |
| `rail.gb.published` — part of South Wales | No account; a bundled subset of published TfW/GWR timetable facts | Valid **14 September–12 December 2026**, within its route subset. It is not a national timetable, a continuously updated feed or live GPS. Outside the valid area/date, select local simulation or provide a valid feed. |
| `rail.fi.digitraffic` — Finland | No key for the selected [Fintraffic Digitraffic railway API](https://www.digitraffic.fi/en/railway-traffic/); review [terms](https://www.digitraffic.fi/en/terms-of-service/) | GeoJSON converter/public collector implemented. Earlier verification received HTTP 406; account-free does not mean currently reachable. Bounded cooldown/fallback applies. |
| `rail.no.entur` — Norway | Selected public [Entur APIs](https://developer.entur.org/pages-intro-authentication/) require an identifying `ET-Client-Name` header, not a secret key | Rail-position GraphQL collector implemented. Header is supplied by `public_rail.py`; an identifying client name is not an OAuth credential. Privileged Entur APIs have separate authentication and are not enabled here. |
| `rail.ie.irishrail` — Ireland | No key for the selected [Irish Rail realtime XML endpoint](https://api.irishrail.ie/realtime/) | Running-train reports converted as **approximate reported locations**, not continuous measured GPS. Physical stock/track compatibility remains validated; Ireland's gauge differs from standard-gauge stock. |
| `rail.us.mbta` — Boston area | Public static GTFS ZIP and GTFS-Realtime files listed in [MBTA's own feed documentation](https://github.com/mbta/gtfs-documentation/blob/master/reference/gtfs-realtime.md) | Collector uses `cdn.mbta.com`, keeps a mainline-rail subset and matches live trips to it. This is different from the optional [MBTA V3 developer API](https://api-v3.mbta.com/), which offers account/API-key access and is not this integration. It is not US-wide coverage. |

Providers are polled on bounded background tasks, with cached schedules and
independent cooldowns. Stale/unusable observations are omitted. Physical playback
also needs nearby, loaded and unambiguous track, suitable stock/gauge and adequate
clearance. A successful API response does not guarantee a train can safely spawn.
The current collector filters GTFS for rail; it does not enable bus simulation.

## Optional UK account: Network Rail

**Why:** broader UK schedule data plus reported train movements, activations and
cancellations. These are reporting-point observations, not universal GPS fixes.

**Where:** read [Network Rail's open-feed description](https://www.networkrail.co.uk/who-we-are/transparency-and-ethics/transparency/open-data-feeds/)
and register/request access at the [data-feeds portal](https://datafeeds.networkrail.co.uk/).
Access/approval and provider terms are controlled by Network Rail; no guaranteed
activation time is implied.

**How, and what still needs implementation:**

1. Register directly with the provider and obtain the necessary authorised feed
   access. Schedule JSON and TRUST movements are the formats supported by the
   existing converters. Select additional reference datasets as needed for binding.
2. Keep credentials in an external collector's local secret store/environment or
   server secret manager. Do not put them in Unity assets, the provider catalogue,
   a GitHub issue, a game-feed URL or a distributed executable.
3. Implement/operate the authenticated feed download/broker connection outside
   the player. **This repository does not yet contain a turnkey Network Rail
   authenticated collector, a login menu or a credential environment-variable
   contract.** Merely creating an account does not connect the game.
4. Use `WorldData/adapters.py`: `networkrail_schedule` converts a complete JSON
   schedule extract; `NetworkRailTrust` maintains activation/movement/cancellation
   state. Merge schedule increments into a complete input before conversion. Keep
   state between live messages; isolated differential messages are insufficient.
5. Supply reference mappings from reporting points/services to admitted OSM track
   paths and suitable stock. `WorldData/bridge.py` exports a validated game snapshot;
   it deliberately omits unbound services instead of drawing rails between stations.
6. Publish only a bounded, authorised normalized snapshot to the game's existing
   feed URL input, or atomically replace `BetaData/TrainData/services.json` with the
   exported snapshot. HTTPS is accepted; plain HTTP is restricted to loopback.
   The game's URL field is **not** a place to paste Network Rail's raw broker URL
   or login details. Polling/expiry/validation still apply.

Provider ID `rail.gb.networkrail` has `credentialRef: networkrail-feed`. This is a
discovery reference, not an API key or a switch that establishes a connection.
The current player reports that credentials are required; converters and tests
are ready, but account transport/reference mapping remain further work.

For local converter commands, run from the repository root after installing
[worker dependencies](BUILDING.md):

```sh
PYTHONPATH=World/Source python3 -m WorldData resolve -3.18 51.48 GB rail.live
PYTHONPATH=World/Source python3 -m WorldData convert networkrail-schedule INPUT.json SCHEDULE.json --date 2026-11-16
PYTHONPATH=World/Source python3 -m WorldData export-game SCHEDULE.json BINDINGS.json GAME.json --observations OBSERVATIONS.json --stock-ids gb.197.2
```

The input/output names above are files supplied by a collector, not bundled data.
Choose the actual service date and admitted stock/path bindings. `GAME.json` is
the exported payload; rename/publish it atomically only after validation.
The packaged worker exposes the same CLI after `--world-data`.

The retained legacy beta root is intentional: on Mac it is under
`~/Library/Application Support/Blackwood Games Beta/Road Rush Beta/BetaData`;
on Windows under `%USERPROFILE%/AppData/LocalLow/Blackwood Games Beta/Road Rush Beta/BetaData`.
Do not commit these runtime directories. See [compatibility](COMPATIBILITY.md).

## National Rail / Darwin and other future services

[National Rail's developer page](https://www.nationalrail.co.uk/developers/)
directs consumers to its feed request process and the Rail Data Marketplace.
Darwin provides predictions, platforms, delays and cancellations. It is a separate
service/account/product from Network Rail Schedule/TRUST. **A Darwin adapter and
authenticated connection are not implemented in this release.** No Darwin account
is required to build or play. Register only if you plan to implement/use that feed;
its credentials are not interchangeable with Network Rail credentials.

Other countries/operators can be added via catalogue entries, adapters and track
bindings. GTFS support is a format capability, not universal access to every
operator. Each provider may require its own key, agreement, attribution and limits.
Google/Mapbox, music streaming subscriptions and paid worldwide LiDAR subscriptions
are not prerequisites of the current implementation.

## Diagnostics, privacy and release scale

Use **Settings → World Data Sources** to inspect discovery and coverage. An eligible
catalogue row is not proof of a connected feed. A 401/403 may mean authentication
or policy; 406 can be a content/server rejection; 429 indicates throttling; a
timeout may be service/network availability. Do not create accounts at unrelated
providers to try to cure a public-endpoint timeout.

Online features send the relevant search text, geographic bounding box, map tile
coordinates, route endpoints or weather location to the corresponding host. A
local worker communicates over loopback but can make external provider requests.
Using a private GitHub repository does not make those runtime requests private.
Use prepared local areas and local weather/train simulation when avoiding online
features; this release does not claim a single master offline/privacy switch.

Before distributing to many players, review provider capacity/terms, attribution,
commercial use, caching and a suitable hosted/self-hosted backend. Open-source code
licensing does not pay for APIs or grant unlimited access. These are deployment
decisions, not requirements for uploading source to a private repository.
