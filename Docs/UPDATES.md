# Game and pack updates

Open **Settings → Game & pack updates** to check for updates or turn on automatic
downloads. The game checks at launch and every 15 minutes while it is running.
A failed check leaves the installed game usable.

With automatic updates off, choose **Update now** or **Download · install at close**.
With it on, downloads run in the background and a game update installs after you
save and close. Standard, HD and the graphics chooser are replaced together.
The chooser reopens after installation. Careers, locations, settings and caches
stay in the separate player-data directory.

The first updater-enabled release is **0.24.0-beta.6**. Install that complete
download once; earlier beta launchers cannot install it themselves.

## Packs and radio playback

Official car and radio packs have independent IDs, versions and compatibility
requirements. Updates only replace matching managed IDs; arbitrary user imports
are not downloaded from untrusted addresses. Car changes take effect after restart.

Radio updates can add stations, songs and playlists while you drive. The current
song keeps playing; the next selection uses the updated playlist. Old audio is
retained so a playing clip remains available. The normal menu pause still pauses
and resumes the same clip.

Radio transfer recipes split the exact pack archive into hashed pieces. The
client reuses matching compressed audio from installed packs and downloads only
missing pieces, then verifies the reconstructed archive against its signed size
and SHA-256 before importing. A playlist-only proof reused **98,008,697 bytes**
without downloading audio. Re-encoding or recompressing a recording can require a
new piece even if it sounds the same. Downloaded pieces are cached under `Updates`.

The current catalogue supports installable `.eucar` and `.euradio` packs.
SceneryCore is a source-build archive, not a separately hot-swappable scenery pack.
New runtime pack formats need their own validated importer before joining updates.

## Trust and installation

The website serves two different documents:

- `/updates/v1/beta.json` is unsigned discovery metadata for manual download links.
  It is never accepted as installation authority.
- `/updates/install/v1/beta.json` is a signed envelope for the app and installer.
  Its exact payload bytes are signed with **RSA-3072 / SHA-256, PKCS#1 v1.5**.
  The public key and key ID are pinned in `UpdateTrust.cs`.

The client rejects unknown keys, invalid signatures, expired metadata and a
sequence lower than one it has already trusted. Metadata expires after 90 days;
the publisher must renew it even when the game version stays the same. A failed
or expired check does not stop an installed game from running.

Downloads are restricted to this project's GitHub release assets and approved
GitHub asset hosts. Exact sizes and SHA-256 hashes are checked. Archive checks
reject traversal, duplicate paths, escaping symlinks and oversized extraction.
The native helper independently repeats the signature, hash, archive and package
checks after the player closes. It validates both players and probes the chooser
before and after replacement. A previous installation is retained for recovery.

Mac uses a filesystem exchange to keep the installed app path present. Windows
uses journalled directory moves and rolls back caught installation failures.
An interrupted Windows move may require restoring the retained backup manually;
it is not a fully power-loss-atomic transaction. Jobs, results and transaction
records are under the player's `BetaData/Updates` directory. The helper needs
write access to the installation's parent folder.

The beta downloads are ad-hoc signed on Mac and have no Windows publisher
certificate. Feed signatures protect the update chain; they do not provide OS
publisher identity or replace signing and notarization.

## Publishing a release

See [installer build and checks](../Updater/README.md). After building and testing
both complete platform ZIPs:

The Windows packaged-runtime workflow uses a read-only token and cannot see draft
release assets. Finish local source/build/archive checks first, then publish the
explicit prerelease and run that workflow. Keep the website and signed install
feed on the previous version until it passes. A draft `release not found` error
is an access/ordering failure, not a runtime test result. Do not increase the
workflow token permissions to work around it.

1. Commit the reviewed source and create the matching game tag. Upload the player
   archives, source ZIP, checksums and verification record to its GitHub release.
2. Run `Tools/prepare_update_release.py --players /path/to/zips --output /path/to/update-assets --sequence N`.
   Review `catalogue.unsigned.json`, including every pack's ID, version, hash and
   speech approval. Upload its radio recipes and hash-named chunks to that release.
3. Compare the uploaded GitHub asset sizes and digests with the catalogue. Never
   replace an advertised archive under the same URL/version.
4. Sign the reviewed catalogue with `Tools/sign_update_catalogue.mjs INPUT PRIVATE_KEY OUTPUT`.
   The private key belongs outside the checkout and release assets. Keep an
   encrypted offline backup accessible to the owner; losing it requires a manual
   trusted app release to introduce a replacement key.
5. Promote the exact game tag with `Website/tools/promote-release.mjs`, then copy
   the signed envelope to `Website/data/install-beta.json`. `npm run verify` checks
   the signature and agreement with the manual download catalogue. Deploy the
   website and check anonymous downloads, signed feed, HEAD and ETag responses.

Increase the sequence for every signed promotion, including metadata renewals.
The current client has one pinned key. Rotate it through a reviewed game release
that pins the replacement before signing feeds with it; unknown keys are refused.
For a compromised key, disable the install feed and distribute a separately
verified replacement app. There is no independent online revocation service.

[Website operations](../Website/README.md) covers Cloudflare deployment.
