# Beta 10 verification

Beta 10 (`0.24.0-beta.10`) is installed locally for owner testing. The public
release, website and update feed remain unchanged. Both Standard and HD use the
same gameplay source. The existing career, settings and saved position were
preserved byte-for-byte during installation.

## Changes

Road construction follows the planned route and the connected roads ahead of the
car. Its horizon grows with speed and recent preparation time. Nearer missing
road support takes priority over distant aligned cells and optional buildings.
Coverage requires physical asphalt; the safety control reduces speed gradually.
Roads and colliders publish before deferred scenery. Incomplete scenery is not
saved as a complete finished-cache entry.

Terrain uses continuous distant coverage, accepts valid deep bathymetry and
retries failed distant requests independently of road generation. Coastal beds
replace stale fine terrain before water is published. Approximate shoreline
heights are constrained between a nearby ground-level road and the mapped shore;
surveyed terrain and high cliffs are preserved. Trees follow the actual published
fine or distant mesh. Trees with stale support are withheld until their placement
is refreshed, rather than drawn in the air.

Road-water masking follows the fitted bridge deck and refreshes independently of
coastline requests. Parallel bridge paths use a consistent deck height, with the
inner railing suppressed where it would obstruct a traffic lane.

The radio balances decoded music, intros and jingles, chooses station links
randomly, and preserves playback through menu pauses. Recovery handles stale
loads and interrupted audio sources. Its menu retains power, station and volume.
Inactive camper-engine loops pause, and engine audio no longer takes the highest
voice priority. Minimap textures are prepared offscreen before being swapped.

Other changes address Hekla grass-to-road contact, traffic following, populations
around the moving player, garage controller order, three-digit speed layout,
saved-area preparation feedback, direction-aware UK national-limit signs and HD
vehicle lamp emission. Old Mac launcher copies have been moved into recoverable
non-indexed archives. Future updater backups use the same convention.

## Verification

| Check | Result |
| --- | --- |
| World-data suite | 66 passed; one optional check skipped |
| Packaged worker session/security checks | Passed |
| Radio policy | 4,012 checks passed |
| Menu layout and ordered navigation | 31 checks passed |
| Minimap texture lifecycle | 19 checks passed |
| Transport/sign policy | 9,638 checks passed |
| Hekla physical verge crossings | 18 passed; maximum tilt 6.47 degrees |
| Following a stationary player | Stopped at 7.00 m centre-to-centre |
| Exact Kessock bridge fixture | Chassis overlaps reduced from 37 to zero |
| Coastline and shore-distance indexes | 14,399 equivalence checks passed |
| Published scenery grounding | 40 checks passed |
| Imported Standard/HD lamp states | 21 checks passed |
| Native controller menus | 387 checks passed |
| Native HD radio/graphics | 350 checks passed |
| Native Fort William, both graphics modes | 1,024 submitted tree-base samples; none unsupported, maximum gap 0.010 m |
| Native Fort William sea bed, each mode | 6,253 fine mesh samples and 670 published sea samples; no false terrain walls |
| Native mountain and coastal spike | Terrain reaches 1,337.26 m; former shoreline spike is 0.25 m above sea |
| Native Kessock water mask | 141 road samples covered, including 21 wave-risk samples |
| Native drive | 1,120 m route / 1,117.55 m physical travel; zero damage, airborne time or streaming braking |
| Native updater | 44 checks passed, including atomic install, rollback and non-indexed backups |
| Installed Standard/HD startup | Both passed; approximately 13.6 seconds per isolated test process |
| Mac package | Archive integrity, matching gameplay assemblies, signatures and chooser self-check passed |

Native checks use disposable profiles. Terrain checks inspect real published
meshes and submitted trees across four camera views per renderer. Screenshots
were reviewed in addition to numeric checks. Physics cases use unconstrained
rigidbodies and real colliders. The fast drive includes a bridge and adjoining
roads; it is not a full international or all-speed endurance test.

The bounded spatial-index fixture retains 64.8% less memory and produces 67.2%
fewer allocation events. The warm-cache repeat read 105 finished geometry entries
(6.65 MB) and required no streaming braking. Geometry cache identities now include
the affected runtime sources, preventing stale geometry from bypassing fixes.

## Known limits and owner checks

Brief frame hitches remain under investigation in
[issue 18](https://github.com/wiffair/earth-unscripted/issues/18). The final cold
76-second drive recorded four frames above 100 ms, worst 227 ms, and none above
333 ms. This is not a claim of hitch-free driving. Diagnostic profiler-recorder
lock contention was removed; remaining pauses need a representative owner
playtest and further profiling if reproduced.

Radio state and PCM policy tests pass, but automated checks cannot replace
listening through the owner's speakers and AirPods. Recheck the reported car
swap, upgrade and long-drive cases. Also revisit the missing near and distant
mountains, floating trees, Kessock water, Hekla verge crossing and traffic behind
a stopped car. Issues remain open pending that validation.

The Mac was locked during final automation; native rendering and screenshot
capture worked. No claim is made that both launcher buttons were clicked by a
person, or that these measurements represent an unlocked desktop's frame pacing.

All reported defects and new findings are in the
[GitHub issue log](https://github.com/wiffair/earth-unscripted/issues). Linux and
VR are tracked on the roadmap. This work has not been pushed or published.
