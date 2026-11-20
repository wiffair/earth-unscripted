# Windows font patch 0.24.1-beta.32

Based on published Beta 32 tag 4773a68a612d4c43117117a8abdd0ab194a3eb1d. This patch contains the shared font selector and patch version; unfinished Beta 33 gameplay/world changes are excluded.

The reported Windows Standard and HD menu failures are accompanied by repeated Helvetica Neue face-load failures and missing-font text-mesh errors. UI font selection now chooses an actually installed family for the platform, keeps its canonical spelling and passes one family to Unity. It retains a built-in runtime fallback and does not redistribute operating-system fonts.

The installed updater rejects a dotted beta suffix such as beta.32.1. Version 0.24.1-beta.32 is accepted and newer than 0.24.0-beta.32. A later Beta 33 must use 0.24.1-beta.33 or another strictly greater version.

The isolated Unity regression suite and 20 font checks pass. Updater-native and packaged-player checks are recorded with the candidate artifacts. Rendered Windows Standard/HD acceptance and a fresh log remain required; a Mac font check is not a Windows render pass.

This is a draft test upload. Public releases, public downloads and the signed updater feed remain unchanged until publication is approved.
