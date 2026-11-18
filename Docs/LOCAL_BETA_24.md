# Local Beta 24

Buildings are prepared through a bounded pipeline of up to eight pending jobs, using two numeric workers. Main-thread capture and ordered mesh assembly yield between buildings when the 2 ms slice is reached. The change applies to initial world building, deferred nearby scenery and distant buildings. Road planning retains priority, and colliders must finish before a new chunk becomes drivable. Individual capture/append operations can still exceed the slice budget.

Distant building generation skips footprints already covered by active full chunks, avoiding invisible duplicate work. Results are appended in original source order. Regression tests compare real serial and pipelined building vertices and triangle indices exactly, including pitched roofs, underground/invalid footprints and a retired-source check.

HD diffuse bounce lighting now uses half-resolution screen-space rays and reconstruction. The main scene and UI retain the chosen display resolution. This reduces rendering cost while keeping bounced light; it is separate from the building-worker improvements.

Native measurements at the saved Inverness position used cloned careers, cold private finished-geometry caches and the same HD settings. At roughly 100 seconds, completed building jobs increased from 1,441 to 4,984. At the 145-second observation boundary, the baseline still had nine road chunks and sixteen scenery chunks pending; the final candidate had none. Source-arrival timing and the changing amount of completed scenery limit direct end-to-end comparisons. The diagnostic switches to a 1600×1000 window after 115 seconds in every run; completion is therefore not a native-resolution-only timing claim.

At native resolution during the 20–110 second window, the pipeline-only candidate had a median frame interval of 28.57 ms, versus 24.99 ms with half-resolution bounce lighting. The original baseline was 25.45 ms while displaying less completed scenery. These measurements support faster scenery completion and lower HD rendering cost; they do not establish a large overall FPS increase or eliminate every hitch. Positive GPU samples are recorded separately; zero samples are unavailable, and Metal GPU timing may include overlapping work.

The full regression gate passes, including nine building-pipeline and 43 scenery/timing checks. Standard/HD driving, the final queue-completion observation and installation receipts are retained outside the repository. This update is local; public releases and website feeds remain unchanged.
