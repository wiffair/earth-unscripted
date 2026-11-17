# Beta 11 verification

Beta 11 (`0.24.0-beta.11`) includes the Beta 10 terrain, sea, tree grounding,
streaming, radio and menu fixes. After local owner testing, the verified Mac and
Windows downloads were published on GitHub. The website and signed update feed
now point to this release.

## Additional changes

Opposing cars use their actual body width and projected corners when deciding
whether another vehicle obstructs their lane. The previous fixed 2.6 m corridor
made safely separated cars wait for each other. Nearby player cars in a separate
lane also stop triggering an early handover to unpowered collision dynamics.
Same-direction following and genuine collision avoidance remain active.

Ordinary unprofiled roads up to 60 m wide now share their identical source/render
query index. Wider roads and prepared width profiles retain separate indexes.
Rebuilds detach the shared dictionary before changing those rules. This removes
one complete set of duplicate segment buckets; candidate values and insertion
order remain exact. Distant-building candidate scanning and retirement are
budgeted, and numeric candidate sorting runs off the driving thread.

## Evidence

- Combined regression suite passed, including all existing Beta 10 checks.
- Eight actual multi-vehicle PhysX cases passed 19 assertions: both traffic sides,
  queued followers, a stopped player, red lights, same-lane collision hazards and
  a road too narrow to pass. Cars remain physically separated; no teleport or
  overlap bypass was introduced.
- Query sharing passed 38 checks and 1,440 exact render/source/surface/layer/terrain
  comparisons against independently constructed original indexes. Narrow, repeated,
  wide, narrow-again, prepared-width and profile-removal rebuilds all passed.
  The fixture avoids 4,928 additional segment backing slots per eligible snapshot.
- All seven final native scenarios passed: Standard/HD physical drives,
  Standard/HD Fort William terrain, Kessock water, HD radio/menus and controller.
- Both drives travelled approximately 1,120 m along the route / 1,118 m physically,
  with zero damage, airborne time or streaming safety braking. They reused 252
  and 247 finished-cache entries respectively.
- Four Fort William views per renderer checked 1,024 submitted tree bases total:
  none unsupported, maximum gap 0.010 m. Each renderer checked 670 published sea
  samples, with no false terrain walls. Final screenshots were reviewed.
- The owner confirmed the installed radio sounds clean in normal play. The
  Beta 11 radio code is unchanged from that confirmed Beta 10 build.

Tests use disposable profiles. Warm driving profiles already contain the radio
and car packs, avoiding first-import work during the comparison. Standard and HD
have matching gameplay assembly hashes. The local installer checks source/version,
validates the complete bundle and preserves the owner save byte-for-byte.

## Remaining performance work

Brief hitches remain recorded in
[issue 18](https://github.com/wiffair/earth-unscripted/issues/18). Final warm runs
recorded four frames over 100 ms in each mode, with worst intervals of 173 ms and
145 ms; none exceeded 333 ms. Native samples show allocation-related collector
contention across different callbacks. These measurements do not establish a
single remaining cause. The duplicate-index change has independently verified
allocation savings; a broad reduction in observed hitches has not been proven.

Fresh disposable profiles also import radio packs, so their startup allocation
is a separate workload from an existing owner's warm profile. Development CPU
captures add profiling overhead and are kept separate from release acceptance.

New findings are tracked in
[issue 36](https://github.com/wiffair/earth-unscripted/issues/36) and
[issue 37](https://github.com/wiffair/earth-unscripted/issues/37).
The owner reported substantially better traffic; a further narrow-junction
stand-off remains recorded in issue 37. Linux and VR remain in the roadmap.
Windows packaged verification passed for both headless players, the worker,
GIS/provider checks, native haptics and the chooser; rendered driving remains
a separate Windows hardware check.
