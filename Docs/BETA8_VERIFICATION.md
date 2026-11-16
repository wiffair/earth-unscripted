# Beta 8 verification

Issues: #5–#11. Candidate checks are recorded below; release installation is recorded once the packaged beta has passed.

- Reproduced the fallback county camper failure with active traffic: a kinematic traffic car reached approximately 3,326 m/s and launched the camper to 654 m/s. The same traffic test after the handback fix ran for 200 simulated seconds, travelled 1,989 m, peaked at 20.91 m/s, and stayed within 0.315 m of its normal road support height. The no-traffic comparison completed 4,040 m without a launch.
- Reproduced the Inverness worker exception against the retained prepared road model. The stack ended at `sign_metadata.contexts`, indexing an empty Shapely LineString. Boundary-crossing and outside-UK regression cases now pass.
- Full Inverness loading now reaches the ready state. Public map requests returned data for New York (3,047 ways), Berlin (1,520 ways) and Sydney (2,755 ways) through the added regional/global fallbacks. Their responses also pass the game converter: 1,722, 1,123 and 1,544 drivable segments respectively. The converter now consistently excludes pedestrian-only highways; 9,632 transport-control checks pass. Availability is external and cannot be guaranteed.
- The worker suite passed 50 tests, with one optional test skipped. The packaged Mac worker passed authenticated session, denied anonymous/origin/Host/traversal, and recovery-token rotation checks.
- HD close-up sign rendering remains legible after forcing UI font-atlas rebuilds; the sign masters retain their own atlas and glyph coordinates. Standard rendering, complete location boots, packaged player checks and installation are still pending at this record's first commit.

Logs and cached geographic fixtures remain local. Raw player logs and career files are not published. Windows driving and physical-controller checks require Windows hardware.
