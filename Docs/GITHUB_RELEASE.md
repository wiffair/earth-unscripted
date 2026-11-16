# Private-first GitHub release

Yes: create a **private** GitHub repository first, review it with selected
collaborators, then change the same repository to **public**. No second upload or
licence change is required just because visibility changes. A repository admin
can change visibility subject to the owner's/organisation's policy. See
[GitHub's current visibility documentation](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility).

The initial repository is [wiffair/earth-unscripted](https://github.com/wiffair/earth-unscripted),
created as **private** on 16 November 2026. This guide also documents repeating the
process from a clean source checkout.
The code remains MIT and the original creative content CC BY-SA 4.0, with retained
third-party terms; private visibility controls access, not the licence text.

## 1. Upload only the prepared source

Use the folder containing this `README.md`, `Unity/`, `World/`, `LICENSE` and
`.gitignore`—the prepared repository, **not its parent development workspace**.
The verified source ZIP is an alternative: extract it and use its EarthUnscripted
folder. Historical working directories contain private runtime and research data
that are deliberately excluded from the release.

Before uploading, run from that source folder:

```sh
python3 Tools/audit_repository.py
python3 -m compileall -q World/Source Tools
```

Read [release status](RELEASE_STATUS.md), [licence scope](../LICENSE_SCOPE.md) and
[third-party service setup](DATA_SERVICES.md). The source check is a bounded
filename/text scan, not a guarantee against every secret format or binary risk.
No API keys, careers, prepared geographic caches or account files belong in Git.
Review what Git stages, not just what appears in Finder.

## 2. Create an empty private repository

Sign in to your own [GitHub account](https://github.com/), create a new repository
and select **Private**. Choose a clear name such as `earth-unscripted` if available.
Leave the remote README, licence and gitignore initialization options unselected;
the prepared source already contains them. Enable two-factor authentication for
your account and invite only the collaborators who need access.

Use GitHub Desktop, or the following local commands from the prepared source
folder. Run `git init` only if it is not already a Git repository:

```sh
git init -b main
git add .
git status --short
git diff --cached --stat
git diff --cached --check
git commit -m "Prepare Earth Unscripted open-source project"
```

`git diff --cached --check` can report whitespace in verbatim upstream notices.
Review the paths; do not rewrite third-party licence text merely to remove its
original spacing. `.gitattributes` preserves exact bytes for pinned worker and
third-party inputs so a checkout does not change source/provenance hashes.

Supply your own Git author identity if Git asks. Use the repository's real Clone
URL with `git remote add origin`, then `git push -u origin main`. Those last
commands intentionally have no fabricated owner/URL here. Authenticate using
GitHub Desktop, SSH or GitHub's supported token flow; never embed a token in the
remote URL. See [GitHub's import instructions](https://docs.github.com/en/migrations/importing-source-code/using-the-command-line-to-import-source-code/adding-locally-hosted-code-to-github).

Start with this clean snapshot rather than importing the whole development
history. A private first upload is useful for review, but committed secrets are
still unsafe and can remain in history after the file is deleted. Revoke/rotate
any exposed credential, then follow [GitHub's sensitive-data removal guidance](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository).

## 3. Add matching content as release assets

Large scenery/radio content is intentionally outside ordinary Git history:

| Release asset | Purpose |
| --- | --- |
| `EarthUnscripted-SceneryCore-1.0.0.zip` | Exact scenery resources restored by the hydration tool |
| `EarthUnscripted-RadioRuntime-2.0.1.zip` | Approved importer-compatible radio archives |
| `EarthUnscripted-ValidationFixtures-1.0.0.zip` | Large unchanged geographic policy fixture needed when building from source |
| `EarthUnscripted-Radio-Music-1.0.0.zip` | Optional separately tagged listening copies; not a runtime importer pack |

The scenery, runtime-radio and validation archives have exact versions, sizes and SHA-256 hashes in
`Content/catalogue.json`. Keep ZIPs intact: rebuilding or retagging audio changes
their hashes and needs an explicit new revision/catalogue update. Full licence
and attribution notices accompany the content. A source ZIP is not a playable app.

Attach the matching archives to a GitHub release under the same repository.
Private repository releases are accessible only to authorised readers until the
repository becomes public. Draft releases are for publisher review, not a promise
that all collaborators can download them; publish a release **within the private
repository** when it is ready for collaborator testing. Check its access yourself.

Download the files locally and use [the hydration commands](BUILDING.md). The
current tool takes local archive paths and verifies them; it does not log in to
GitHub or automatically fetch private assets. Catalogue URLs point to the matching `v0.23.0-beta.52` release in this repository.
Private assets need GitHub-authorized access; keep all hashes when changing URLs.
Generated Unity libraries, built workers and players stay outside ordinary Git.
See [GitHub's file-size guidance](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github).

## 4. Review while private

Check the staged/tracked files and Git history, attribution, licence scopes, content
downloads and hydration. Ask a collaborator to follow [building](BUILDING.md) from
a clean clone. The source-hygiene Actions workflow needs no provider credentials
and performs no Unity build. Unity CI would require a separately designed licence
and signing-secret setup; it is not included or required for source publication.

No new Windows native validation or publisher notarization is implied by source
readiness. Keep those limitations in the release notes. Test actual player packages
on their target systems before advertising them as validated public downloads.

## 5. Make the reviewed repository public

On GitHub: **Settings → General → Danger Zone → Change repository visibility →
Public**, then complete GitHub's confirmation for the correct repository. Account
or organisation policy can restrict who is allowed to do this.

Review commits, branches, tags, release assets, issues and CI logs/artifacts first.
Private code/history becomes public; GitHub specifically notes that Actions history
and logs become visible too. Removing a file from the latest commit is insufficient
if sensitive material remains in earlier commits. After public access, people can
copy/fork/download it; changing back to private cannot recall those copies or
revoke valid earlier open-source permissions.

Finally, check the public README, release asset links and an anonymous download.
Update the website/repository links only once their real addresses are known.
