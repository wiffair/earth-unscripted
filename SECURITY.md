# Security and privacy

Never put provider credentials, signing keys, saved careers or private locations
in public issues. Report a security problem privately to the repository owner
through GitHub's private vulnerability reporting when it has been enabled.
Until then, request a private contact channel without posting the sensitive data.

Third-party data access is optional and source-specific. A public data licence
does not promise unlimited use of a hosted API. Keep account credentials in a
local collector or provider configuration, never in a shipped client or repository.
See [data-service setup](Docs/DATA_SERVICES.md) for implemented connections and
remaining account integration work, and [the private-first release guide](Docs/GITHUB_RELEASE.md)
before uploading. Private repository visibility does not make committing secrets safe.

Imported packs are untrusted inputs. Preserve path, byte-budget, integrity,
schema, revision and atomic-publication checks when changing import code.
