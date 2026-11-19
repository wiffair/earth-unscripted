# Beta 27 local verification

This update adds Low, Medium, High and Ultra graphics presets, closes the split-window camper's interior and floor joins, fixes its steering pivot, and replenishes ordinary traffic farther ahead of the moving player. The road V8 sound gearbox also retains its cruising gear across throttle changes.

Standard and HD share gameplay assembly `78859963f3a3e80cc692b8965014264e4d8f623a857b473d372d38286fb33615` and source identity `147b88ef3d855edb7d79107799f373e2d958b0fee9df1a35b2dbe1963598d5f4`. The map worker and finished-geometry cache identities are unchanged.

Validation:

- Full Unity regression gate passed, including supported road coverage, traffic scarcity, the 70 mph ahead/opposing-car placement fixture and cruising-gear hysteresis.
- Native camper checks passed in Standard and HD: steering rim rotation about a fixed shaft, open windows, working wipers, inner panels and twelve floor samples covering both footwell edges. Screenshots were reviewed.
- Native graphics checks verified actual 70%, 85% and native scene sizes, unchanged display/UI resolution, menu frame limits and road colour. The graphics changes were verified before the final floor-only correction.
- Controller menu verification passed.
- Final physical drives passed for 1.2 km in Standard and 500 m in HD. The road's 50 mph limit meant these were not a continuous 70 mph acceptance drive.
- Installed-player startup checks passed in both renderers using copies of the latest career. The original career remained byte-for-byte unchanged.
- Repository audit and whitespace checks passed.

The intermittent off-road forward stop still needs a captured reproduction. Longer-session frame pacing also remains under investigation. These checks do not establish a universal FPS gain or eliminate provider latency.

See [Graphics settings](GRAPHICS_SETTINGS.md) for the preset budgets. This build is installed locally; publication is a separate step.
