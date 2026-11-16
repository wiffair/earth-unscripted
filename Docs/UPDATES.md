# Website and app updates

Earth Unscripted's website and release catalogue live in [`Website`](../Website).
The first site includes a versioned release-discovery endpoint and stable Mac
and Windows download links. The current launchers still use manual downloads.

## What is implemented

`/updates/v1/beta.json` describes one explicitly promoted game release: version,
channel, publication date, release notes, platform, architecture, minimum OS,
archive URL, byte count and SHA-256. Both Standard and HD players belong to the
same platform archive and must be updated together.

A maintainer promotes a specific game tag after its checks pass. The promotion
tool compares GitHub's artifact digests with the release checksum file and
records whether access is private. It rejects drafts, missing or duplicated
platform archives, missing hashes and non-game tags. A newer vehicle or music
pack can never become the game's next version by accident.

The read-only feed supports conditional requests with ETag and HEAD. Five-minute
caching bounds how long an old promotion can persist. Unknown channels/platforms
return 404. Download redirects accept only the exact promoted artifact URLs;
query parameters cannot change their destination. No publisher token is sent
to a player or browser.

This metadata is **not signed** and says `automaticInstall: false`,
`installation: "manual"`, `signature: null`. SHA-256 detects a damaged or changed
archive, but a hash delivered beside an executable is not independent proof of
who published it. Future installers must refuse this discovery document as an
installation authority.

## Recommended next implementation

Keep one update protocol shared by both launchers, with a small native installer
for each platform. Host the signed release feed on earthunscripted.com; keep
large, immutable archives on GitHub Releases initially. The site gives us a
stable address if the file host changes later.

1. **Signed promotion.** Create a separate, versioned install-feed envelope.
   Sign the exact UTF-8 manifest bytes with Ed25519, include a key ID and pin the
   corresponding public key in both launchers. Keep the private signing key in
   protected release infrastructure, outside the repository and site. Specify
   key rotation, expiry, revocation and a monotonically increasing release
   sequence before implementing the client. Do not accept `signature: null`,
   an unknown key, expired metadata or a sequence older than one already trusted.
2. **A check at launch.** Store the update channel, check with a short timeout
   and honour ETag. Failure to reach the feed should leave the existing installed
   game usable. Compare semantic versions, platform, architecture and OS support;
   never compare version strings alphabetically. Let the player view release
   notes, download now or continue on the installed version.
3. **Prepare, then replace.** Download into a separate staging directory, with
   bounded disk-space checks, retries and cancellation. Verify the feed signature,
   exact byte count and SHA-256 before extracting. Reject archive paths that
   escape staging, symlink escapes and unexpected executable layout. Update the
   chooser and both graphics players as one installation.
4. **A safe restart.** Never replace files during a drive. Save and close the
   game, then run a helper outside the app directory. Use an atomic move where
   available; otherwise use an explicit recoverable transaction. Keep a known
   working version until the new launcher and player pass startup checks. Test
   power loss, a full disk, a locked file and rollback on Mac and Windows.
5. **Keep player data separate.** Careers, saved locations, settings and caches
   remain outside installation directories. Content packs keep independent IDs,
   versions, licences and compatibility requirements. Update them through a
   separate pack catalogue rather than replacing user imports silently.

Mac release signing/notarization and Windows publisher signing should accompany
wider distribution. An update feed does not replace the operating system's
publisher checks.

## While downloads are private

A signed feed does not grant access to a private GitHub asset. The installed app
cannot borrow the browser's GitHub cookies. Use an approved per-user GitHub
sign-in flow or an authenticated distribution service if automatic downloads
are needed before the repository becomes public. Never embed the publisher's
GitHub token, put it in a URL, or have a public Worker proxy private files under
that token.

The first website continues to send collaborators to GitHub for manual,
authenticated downloads. Public download access is a separate release decision.

## Hosting and release operations

[Website setup](../Website/README.md) covers Cloudflare login, deployment,
domain binding and promotion commands. Keep the website in this repository for
now: the release data, documentation and game can be reviewed together. Content
packs can stay separate release assets without needing separate repositories.

Cloudflare deployment is independent of the game build. A documentation or
layout correction should not rebuild either Unity player. Before connecting
GitHub to automatic site deployments, choose a preview branch and a production
promotion rule; an arbitrary source push should not promote an untested player.

References: [Cloudflare Static Assets](https://developers.cloudflare.com/workers/static-assets/),
[custom domains](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/),
[GitHub release assets](https://docs.github.com/en/rest/releases/assets).
