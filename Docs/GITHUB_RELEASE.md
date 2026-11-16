# Publishing a release

Earth Unscripted lives at [wiffair/earth-unscripted](https://github.com/wiffair/earth-unscripted).
The repository and release downloads are public.

## Source and content

Upload the prepared repository, not its parent development workspace. The source
contains the Unity project, mapping worker, native adapters, launchers and docs.
Saves, geographic caches, credentials and generated build folders stay out of Git.

Before a source update:

```sh
python3 Tools/audit_repository.py
python3 -m compileall -q World/Source Tools
git status --short
git diff --check
```

Review the actual changes and their provenance. The source audit catches known
filename and text patterns; it cannot establish ownership or detect every secret.
Use GitHub Desktop or authenticated Git to push a focused commit. Never put a
credential in a remote URL or commit.

Large content belongs in versioned release assets:

| Asset | Used by |
| --- | --- |
| Mac and Windows game ZIPs | Players; includes runtime scenery, radio and mapping worker |
| Source ZIP | Developers; snapshot of the tagged source |
| SceneryCore, RadioRuntime | Developers restoring large build inputs |
| ValidationFixtures | Developers running the production builder's geographic checks |
| Radio-Music | Listeners; tagged MP3 copies for use outside the game |

Preserve published archives and checksums. A content change needs a new revision
and matching catalogue entry. The [hydration tool](BUILDING.md) checks local
content archives before restoring them into a source checkout.

## Player downloads

Follow [Building](BUILDING.md) to compile and package Standard and HD. Test the
complete extracted ZIP on its target platform, with disposable saves. Include
licences and setup instructions; leave build backups and debug symbols out.

Upload through the GitHub release editor or the official GitHub CLI:

```sh
gh release upload v0.24.0-beta.3 /path/to/game-download.zip /path/to/checksums.txt \
  --repo wiffair/earth-unscripted
```

Git pushes source commits; release assets use a separate upload API. An existing
browser sign-in or connected app does not automatically authenticate terminal Git
or GitHub CLI. Choose the credential storage you want when setting up the CLI.

After attaching the game ZIPs and checksums to the matching release, check
that they can be downloaded without signing in. Put the Mac and Windows game
links first in the release notes. Keep source and content downloads under a
separate developer section so players know which files they need.

Record what was tested and any remaining platform limits in [Release status](RELEASE_STATUS.md).
A successful cross-build alone does not establish Windows gameplay quality.
Publisher signing/notarization uses the publisher's own credentials.

## Public release checks

Review source, history, branches, tags, assets and Actions logs before publication.
Public copies cannot be recalled by changing visibility later. Keep credentials,
player data and private signing keys outside the repository and release assets.
Keep original code under MIT, original creative work under CC BY-SA 4.0 and
third-party material under its existing terms.

Publish signed updater metadata only after the advertised artifacts have passed
their checks. Follow [Update operations](UPDATES.md), then deploy the website and
verify both platform downloads while signed out. Preserve immutable release files;
a revision needs a new version. The game feed must promote an explicit game tag,
not whichever pack happened to be released most recently.

The [monitoring guide](MONITORING.md) explains repository notifications and CI.
Publishing a repository does not itself activate a monitoring service.
