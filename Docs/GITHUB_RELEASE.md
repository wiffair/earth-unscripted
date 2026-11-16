# Publishing a release

Earth Unscripted lives at [wiffair/earth-unscripted](https://github.com/wiffair/earth-unscripted).
It is private for the first round of testing. Invited collaborators can download
published releases while the repository stays private.

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

Attach the game ZIPs and checksums to the matching GitHub release, then check
that an invited collaborator can download them. Put the Mac and Windows game
links first in the release notes. Keep source and content downloads under a
separate developer section so players know which files they need.

Record what was tested and any remaining platform limits in [Release status](RELEASE_STATUS.md).
A successful cross-build alone does not establish Windows gameplay quality.
Publisher signing/notarization uses the publisher's own credentials.

## Making the repository public

Review the source, history, branches, tags, assets and Actions logs with the owner
before changing visibility. Ask a collaborator to build from a clean clone and
another to try a complete player download.

A repository admin can then use **Settings → General → Danger Zone → Change
repository visibility → Public**. The same repository and release URLs continue
to work. See [GitHub's visibility guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility).

Public visibility exposes history and release assets as well as current files.
People can copy or fork the project; returning it to private cannot recall those
copies. Remove private data before publication, including from history where
needed. Keep original code under MIT, original creative work under CC BY-SA 4.0,
and third-party material under its existing terms.

After publication, check the downloads while signed out and set up the agreed
[monitoring](MONITORING.md). Changing visibility does not activate monitoring.
