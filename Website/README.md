# Earth Unscripted website

The site for **earthunscripted.com**, built from the game's brand artwork and a
reviewed release catalogue. It introduces the game, links to Mac and Windows
players, lists vehicle/music packs and points contributors to the documentation.

The source lives alongside the game so download information and release changes
can be reviewed together. Cloudflare serves the pages and a small read-only
Worker handles release discovery and download redirects.

## Work locally

Use Node.js 22 or newer. From `Website`:

```sh
npm ci
npm run dev
```

Open the local address Wrangler prints. The page is generated from
`templates/`, `public/assets`, the pack catalogues and `data/beta.json`; no game build is
needed. The graphics selector on the website explains the two choices. The
player's launcher makes the actual graphics selection.

Before publishing:

```sh
npm test
npm run check
npx wrangler deploy --dry-run
```

After changing bindings, regenerate `worker-configuration.d.ts` with
`npx wrangler types --include-runtime false`.

## Publish on Cloudflare

The configuration deploys the `earth-unscripted-website` Worker and binds its
custom domain, `earthunscripted.com`. The domain’s active zone and absence of
existing routes were checked before the first deployment. No personal account
ID or credentials are committed. For a fork or another account, remove or replace
the custom-domain route before deploying, verify a preview first, then attach
your own domain. Preserve existing DNS records for email
and other services. Do not replace another site's deployment based on a name
match alone.

1. Sign in to the correct Cloudflare account, then check `npx wrangler whoami`.
   The initial deployment used only `account:read`, `user:read`,
   `workers_scripts:write`, `workers_routes:write` and `zone:read` OAuth scopes,
   plus Wrangler’s required refresh access. An expired login must be renewed
   with the owner’s approval.
2. Check the existing Workers/Pages project, the domain's zone status and its
   DNS records. If it already uses Pages, decide whether to retain Pages or move
   the site to this Worker before changing the domain binding. This source is
   configured for Workers Static Assets; a plain static Pages upload would omit
   the Worker feed and download routes.
3. Run `npm run deploy`. Check `/`, `/privacy`, a missing page, both download
   redirects, `/healthz` and `/updates/v1/beta.json` on the deployed preview URL.
4. Add the verified custom domain using Cloudflare's Custom Domains settings,
   or add the matching `routes` entry to `wrangler.jsonc` and deploy. Only bind
   `www` if that hostname belongs to this project and has been agreed.
5. Verify HTTPS and those routes again on `https://earthunscripted.com`.

Wrangler's OAuth permissions can cover more than this one site. Use the minimum
permissions available and let the owner approve the consent screen. No API token
or GitHub credential belongs in the website, source catalogue or browser code.
Production needs no GitHub API token: downloads redirect to GitHub, which enforces
its own access rules.

The checked-in site does not collect analytics, use external fonts, set cookies
or offer a sign-up form. Its privacy page explains Cloudflare hosting and GitHub
links. Revisit that page if these choices change.

## Promote a game release

Run this only after the players and their release checks are approved:

```sh
node tools/promote-release.mjs --tag v0.24.0-beta.2
npm test
npm run check
```

The GitHub CLI must be authenticated locally. Set `GH_BINARY` if it is not on
`PATH`. The command selects an explicit **game** tag, requires both platform
archives and their GitHub SHA-256 digests, and compares those digests with the
release's checksum file. Review and commit the resulting catalogue, then deploy.
It never selects GitHub's generic “latest release”: the newest release may be a
car or radio pack.

`data/beta.json` is the single source for the displayed version, file sizes,
checksums, stable download redirects and discovery feed. Production paths:

| Path                          | Purpose                                          |
| ----------------------------- | ------------------------------------------------ |
| `/downloads/beta/macOS-arm64` | Redirect to the reviewed Mac archive             |
| `/downloads/beta/Windows-x64` | Redirect to the reviewed Windows archive         |
| `/updates/v1/beta.json`       | Versioned discovery metadata; supports ETag/HEAD |
| `/healthz`                    | Deployment health and promoted game version      |

Private release access stays private. GitHub downloads and source links require
an invited account while the repository is private. Making the brochure public
does not make those files public. Before a public game release, review the pack
and project copy as well as the catalogue's access flag.

## Automatic updates

The discovery feed is ready for future launcher integration. **The installed
launchers do not yet check it, download updates or replace the game.** It is
unsigned metadata, explicitly `automaticInstall: false`, and must never be used
as permission to install code. [The update design](../Docs/UPDATES.md) describes
the signed feed, authentication and platform installers still needed.

## Source and artwork

Website code uses the repository's MIT licence. The cover, icon, route illustration
and favicon use CC BY-SA 4.0 where copyright applies; see
[`public/assets/CREDITS.txt`](public/assets/CREDITS.txt) and the repository's
licence scope. The cover is artwork, not a gameplay screenshot.

## Pages and pack media

The home, game, downloads, project, car and radio sections have separate URLs.
Each car has its own gallery and download page; each station has its own page,
with a working radio player and song list. Layout and navigation are shared.
CSS and JavaScript filenames include their content hash so a new deployment
loads the matching styles and controls rather than a cached older copy.

Car previews are studio renders of the exact released meshes, using the same
wheel and steering attachment positions as the game. They are labelled as
renders. `tools/prepare_vehicle_catalogue.py` records the pack versions, URLs,
file sizes and checksums, and preserves the included licence notices.
`tools/render_vehicles.py` renders the three views using Blender 4.3.2 in
background mode. These tools read the released vehicle packs in the source tree.

The web radio serves twelve attributed 45-second previews, one per song, from
the reviewed two-station pack. `tools/prepare_radio.py` checks source hashes and
uses FFmpeg to extract seconds 30–75, with fades and peak limiting. It records
source and output hashes in `data/radio-preview-provenance.json`, retains song
and fictional artist names, and adds Richard Cole / CC BY-SA 4.0 attribution.
Full game packs stay on GitHub and keep their existing private access. The
website previews are public when the website is published.

The generated PNGs, MP3 previews, catalogues and notices are committed so an
ordinary site build needs only Node. Regenerating previews needs the source
packs and the tools above. Full song recordings are not copied into the site.
Run `npm run verify` to check release rules, page links, generated assets and
types before deploying. Playback also needs a browser check: play/pause should
retain position, presets should change station, and only one preview should play
at a time. The player starts paused and makes no listening analytics requests.
