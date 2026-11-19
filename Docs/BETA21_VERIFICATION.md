# Beta 21 — sparse moving police

Local owner-test build only. Public GitHub downloads, website and update feed remain Beta11.

Normal road worlds now have one genuine base patrol. It drives with its dynamic VehicleController, follows legal source lanes and directional limits, and uses the existing signal, stop, give-way, junction and leader rules. Future asphalt is checked every8m through a stopping horizon; unavailable collision imposes a speed cap. Physical police cannot be recruited into kinematic traffic retreat. Traffic observations include the real patrol and its physical velocity.

Idle encounters use real grounded road travel:800m minimum plus an exponential random extra gap with2500m mean. This schedules a search, not a guaranteed encounter. Old/candidate positions must be out of view and sufficiently separated; unsupported or occupied sites are rejected. A suitable shoulder allows parking, otherwise a valid lane produces a cruising patrol. Actual player pose resets and world/player ownership changes do not manufacture encounter distance. A chasing or disengaging patrol is never recycled to keep up.

Parked and cruising police can witness speeding and collision offences through visibility/range/cooldown checks. The existing60s pursuit duration and maximum upgrades remain. Extra responders are not part of this change.

The required normal source roster is genuinely26 actors (player+24traffic+one patrol). Required slots are not hidden or reduced to whatever spawned. The separately reviewed scoped800 source retains its actual three initial patrols/28actors and initial fleet policy; its native golden proofs have not been rerun for this batch. No semantic snapshot freshness, physical layout or factory authority was relaxed.

## Verification

- Actual Mono patrol policy/source/LOS/command checks:38 passed. Includes irregular gaps, normal26/scoped28 source declarations, parked/mobile witnesses, real collider occlusion, visible/pursuit placement refusal, one-way authority, stop/non-green signal/following caps, missing physical road stop and own-collider filtering.
- Full Beta10 regression gate passed after the production changes, including genuine deferred-roster/scarcity/coastal checks. Initial Editor fixture failures were inactive physics bodies, corrected in the fixture; no production Awake workaround was added.
- Private native Standard observation passed: one real base,26 unique real roots, ordinary fleet references include police,21.80m physical cruising/88 moving cruise frames, maximum16.19m/s. A distant actor then reached an unpublished chunk around773m from the held player and was safely disabled/kinematic out of view. The observer explicitly permits and separately counts one render-frame chunk-retirement transition; this is not sustained physics-phase or zero-gap proof. The early overly strict diagnostic failures are retained. Automatic load checks now use actual published collision chunks; strict cruise asphalt-ahead coverage remains separate, so genuine shoulders/off-road pursuit are not falsely treated as unloaded.
- Normal Standard/HD pair built and packaged with matching gameplay assembly; private observer/seed helpers absent. The packaged normal Standard player drove999.39physical metres with zero damage, airborne time, streaming-brake time and unpublished bridge samples. Its moving-frame maximum was63.4ms; a101.8ms timing event occurred essentially stationary. Both final installed Standard/HD imported-car startup checks passed in15.13s each. Owner save stayed byte-identical through installation and these checks. An earlier startup diagnostic falsely required three patrols; it now reads the genuine declared normal/scoped source count while retaining clear-start and spacing checks. The only change after the integrated drive was this diagnostic count/receipt correction, not gameplay behaviour. HD gameplay assembly parity is verified; a new HD1km run is left to owner testing after Beta20's paired clean runs.

Source: `a8abc6af7f994cab9a81e4352eb50138668e1a3a067f51c5d974539aaa875f2c`.
Both gameplay assemblies: `f99b706a412832d9f7196fb7a3bd5618dba826cce45314c38ed7bdc4d16c55f6`.

## Testing handoff

The owner requested a pause after installation. Long-route sparse placement, parked merging and everyday junction behaviour need sustained owner testing. A seeded4km companion observer is prepared privately but uncompiled/unrun; no long-route recycling success is claimed. The intermittent HD hitch remains issue18; Beta20's shorter clean drives did not prove it eliminated. Windows runtime checks are not included in this local Mac batch.

Beta20's bridge-water/startup fixes remain included. Owner saves are preserved and no release publication is authorized.

Bounded repository filename/text scan passed1,342files with no findings; this does not establish binary provenance or legal ownership.

CPU follow-up: owner reported low overall/efficiency-core use. Higher user-initiated thread QoS is a possible next test; thread/core residency, GPU/I/O/main-thread waits and generation budgets need measurement first. No QoS change was made in this beta. Owner requested pausing for testing.
