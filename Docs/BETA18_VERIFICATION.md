# Beta 18 verification

Beta 18 is installed locally for owner testing. Public downloads and the update
feed remain Beta 11. The owner save was unchanged through replacement and both
installed launch checks; Beta 17 remains recoverable.

## Changes

- Road-ahead coverage uses tyre-support height to select the correct deck at
  stacked roads. Planned samples retain their own height.
- Coverage continues beyond a planned route's tail and follows the future route
  direction when selecting a lane.
- The geometry queue promotes each 8 m forecast sample's 4 m driving footprint
  ahead of broad scenery margins. Existing surrounding, branch and planned-route
  work remains queued. The 2 ms geometry slice and collider publication rules
  are unchanged.
- After suitable map providers fail, the location loader and continuous worker
  can consult the official global Overpass directory. Eligible public services
  use bounded public-IP-pinned TLS, original-host certificate checks, cache TTL,
  backoff and cooldown limits. Discovered URLs use the restricted transport.
- The optional Cardiff surface diagnostic can follow legally connected short
  roads, retaining each leg's lane and height authority.

## Verification

The full Unity regression gate passed, including 678 road-coverage assertions,
7,009 assertions against the production queue planner and 24 connected-road
fixture assertions. Recovery passed 37 Python checks and nine C# process-runner
checks. The final Mac worker passed its authenticated session/access checks.

A genuine before/after comparison drove the human-car controller over the same
4 km source-road sequence with prepared data retained and finished geometry
initially absent. Both runs physically visited four newly resident cores,
recorded zero damage and restored the original cache and owner save.

| Observation | Before queue change | After queue change |
| --- | ---: | ---: |
| Physical distance | 4,000.41 m | 4,000.34 m |
| Moving coverage braking | 24.402 s | 0.348 s |
| Airborne time | 0.024 s | 0.075 s |

The observed reduction in coverage braking was 98.6%. This is one comparison;
event snapshots add observer work. Occasional frame hitches remain, including
a 127 ms frame at about 10 m/s in the later run. Issue #18 remains open.

Normal Standard and HD players each passed approximately 999 m of physical
driving with zero streaming braking or airborne time. Their only recorded frames
over 100 ms occurred at rest. Both loaded and completed native graphics warm-up.
Installed Standard/HD checks with a preinstalled imported-car profile passed in
12.12/12.11 s respectively (whole test-process durations).

Cardiff passed live search, preview, preparation, selected-world reload, keyboard
input and a 184.80 m connected-road drive with zero damage. The complete cold
scenario took 285.74 s; its cached repeat took 123.00 s. These include menu and
physical checks. Further startup timing capture is in progress; the remaining
Cardiff delay is not resolved by these results.

## Remaining limits

The official directory returned HTTP 200. Separate live queries to discovered
Overpass services returned errors/timeouts; successful discovered-service map
loading is still unverified. Controlled transport tests cover new-host dispatch.
The attributed OSM wiki fixture retains its CC BY-SA 2.0 notice.

Windows helpers were rebuilt; Windows rendered driving remains unverified.
Private diagnostic helpers and private vehicle recordings are outside this
installed package. The conservative geometry identity changed, so older finished
mesh entries can require a rebuild; prepared source data retains its own identity.

Runtime source: `f0baa1f663317c9c300c8baa127d580929cd95ddc57c160e7a612eca92e03e35`.
Matching Standard/HD assembly: `15d7476e50a06bea85264ba9cec23b15e4ac40d0c1de006b323767dc026cb4ef`.
Worker: `d85e2bcfad0c1a8c4110791a30633e905156321bd48462bad2a24d1832e39529`.
Geometry: `earth-geometry3-42365c685cad97afb8918c6ed98a775ecb3f77d007485504cdc146466224f8b6`.
