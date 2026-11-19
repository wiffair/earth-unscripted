# Beta 12 local verification

Beta 12 improves traffic cooperation, adds cockpit rain and sweeping wipers, and corrects the reviewed VW camper interior. It is a local test build; the public download remains Beta 11.

Both Standard and HD use source `6eab94c28f74880f5d1e78a020187d133c496d72ea6741a2d80331336cbbd42c` and gameplay assembly SHA-256 `c9333d9b465972f5ac04eaad6777de4d892dec52435fd694ba88be74384ef754`.

## Checks

- The complete paired-player build suite passed, including 631 windscreen checks, 24 cooperation checks covering 10 actual PhysX cases, and 8 topology-equivalence checks.
- Four native-player tests passed: cockpit/rain and the Kessock driving route, in Standard and HD. Driver and cabin screenshots were inspected. Both camper front frames are thin and matched; both quarterlights and the centre windscreen divider are present. The dashboard is visible from the driving camera.
- Each driving test covered approximately 1,118 physical metres, with no runtime errors, airborne time, vehicle damage or streaming-brake time. Each checked 1,086 bridge samples, with no unpublished bridge samples.
- The Standard drive recorded three frames above 100 ms, worst 143 ms. HD recorded five, worst 831 ms; the large HD event also recorded approximately 715 ms of graphics wait. Hitches remain under investigation in issue #18. These tests do not establish that frame pacing is fixed.
- The road-node topology change preserves emitted sections, points, source identifiers and ordering. It removes per-node HashSet allocations. The Mono allocation counter was unavailable, so no measured byte saving is claimed.

The canonical local app was atomically replaced after code-signature, launcher, source and matching-assembly checks. Its existing save was preserved byte-for-byte. Both installed players then passed isolated native startup checks (13.10 s Standard, 13.63 s HD test-process duration).

## Scope and remaining work

Traffic can arrange a bounded retreat to a supported wider bay, then safely resume. A blocked rear or missing safe bay still requires waiting. Controlled junction rules remain in force.

Rain is bounded to the visible windscreen and cleared by actual blade sweeps. It does not yet refract the scene through individual droplets. The VW interior adaptation is limited to the verified version 2.0.0 pack; its archive is unchanged.

Grass rising through asphalt at the Kessock bridge-to-beach landing is confirmed in emitted geometry and tracked separately in issue #40. This terrain repair is being prepared for the next staged beta; it is not included in Beta 12. Owner acceptance of the local build is pending.
