# Security and privacy

Report vulnerabilities through [GitHub private vulnerability reporting](https://github.com/wiffair/earth-unscripted/security/advisories/new).
Do not post reproduction steps, provider credentials, signing keys, saved careers
or private locations in public issues. The maintainer uses draft security advisories
to triage confirmed vulnerabilities and coordinate fixes before disclosure.
Ordinary hardening and dependency maintenance can be tracked in public issues.

The current beta is the supported review target. Older beta downloads may lack
security fixes; use the newest published beta. Code signing identifies the publisher
and detects later modification; it does not replace code review or dependency updates.

Third-party data access is optional and source-specific. A public data licence
does not promise unlimited use of a hosted API. Keep account credentials in a
local collector or provider configuration, never in a shipped client or repository.
See [data-service setup](Docs/DATA_SERVICES.md) for implemented connections and
remaining account integration work, and [the private-first release guide](Docs/GITHUB_RELEASE.md)
before uploading. Private repository visibility does not make committing secrets safe.

Imported packs are untrusted inputs. Preserve path, byte-budget, integrity,
schema, revision and atomic-publication checks when changing import code.
