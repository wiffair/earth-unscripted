# Beta 19 verification

Beta 19 removes repeated traffic-control layout work when generated chunks
alternate between neighbouring source areas. It includes the Beta 18 coverage,
queue, public-map recovery and Cardiff diagnostic changes.

## Cached Cardiff startup

At the same fixed position, with all three source areas and 206 finished chunks
cached, Standard reached ready in **26.72 seconds**, compared with **167.79 seconds**
in Beta 18: an observed 84.1% reduction, or 6.28 times faster. Each version has
one retained measured run under these conditions.

Both runs restored all 206 chunks with zero mesh misses and no terrain generation.
Beta 19 built three sign layouts and reused their placements 203 times. The cache
held 11,947 placements across three resident maps. The former single-map cache
rebuilt the whole layout on repeated A → B → A ownership switches; those switches
accounted for 149.12 seconds in the earlier run.

The cache retains final placement arrays and releases the constructor's temporary
spatial index. Replaced or retired maps are removed; world destruction clears the
cache and abandoned task faults are observed. Sign placement, duplicate filtering,
visual batching and complete collision-before-activation behaviour are unchanged.

## Checks

- Full Unity regression passed, including 4,540 assertions for actual lookup,
  in-flight/completed reuse, placement parity, side changes, replacement,
  retirement, destruction and fault handling. Dense Cardiff data are included.
- A native cold-geometry drive covered 4,000.10 physical metres and physically
  visited four newly resident cores. Finished-cache hits were zero. Damage was
  zero, airborne time was 0.072 seconds and moving coverage braking totalled
  0.424 seconds. Its sole frame over 100 ms was at 0.179 m/s near initial rest.
- The first visit under the new geometry/index identity reached ready in 107.49
  seconds, with prepared sources already cached and finished meshes absent.
  The fully cached repeat above measures subsequent startup separately.

Normal Standard/HD players each passed about 999.5 m of physical driving with
zero damage, airborne time or streaming braking. Both installed launch checks
passed with a preinstalled imported-car profile in 12.12/12.61 seconds. The owner
save stayed byte-identical through installation and launch checks; Beta 18 is
recoverable. Beta 19 is installed locally for owner testing.

## Limits

These are bounded tests. The earlier intermittent 836 ms hitch was not reproduced;
a normal HD drive did record a 777 ms frame at 41.1 mph. A detailed repeat did not reproduce that moving hitch; its cause remains
unproven and issue #18 stays open. Brief cold-generation coverage
intervention is still recorded, so uninterrupted generation is not claimed.
An ordinary saved-location launch also exposed a fatal 25/27 initial-traffic
requirement, tracked in #46 and pending the next fix. The reported grass beneath
the bridge is tracked in #47; a span-level visual check is pending.
Windows rendered driving and successful live loading from a newly discovered
public Overpass host remain unverified. Public downloads and the update feed stay
at Beta 11.

Runtime source: `f21ca1b3817bd4e2e62b23398205baccb7d408ed85899526044f37fc3a6e510f`.
Matching Standard/HD assembly: `971f87978536bfb856c06155640f61b3352a719d714cf96b4f1a2e7ce42d2aee`.
Geometry: `earth-geometry3-e49c0b5f85e70793b787044aca85f9e8bdca587a4326eb1c1be39aef80f0f535`.
The worker remains `d85e2bcfad0c1a8c4110791a30633e905156321bd48462bad2a24d1832e39529`.
