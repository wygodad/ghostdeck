# Security Policy

## Supported versions

Security fixes are provided for the latest released version of GhostDeck.

| Version | Supported |
| ------- | --------- |
| Latest release | Yes |
| Older releases | No |

## Signing and provenance

Three separate signatures cover this project, and it is worth knowing which answers which question:

- **Release binaries** (`GhostDeck.exe`, v1.24.0 and later) are signed with **Authenticode** through
  Azure Artifact Signing, as part of the release workflow. Properties → Digital Signatures on the
  downloaded file shows the publisher. An unsigned build claiming to be v1.24.0+ is not ours.
- **The model database** (`data/models.json`), which the app fetches between releases, is signed with
  **ECDSA P-256**; the public key lives in `tools/model-signing.pub`. The signature is checked on
  every load, a file older than the built-in tables is refused, and anything invalid falls back to
  the tables compiled into the app - a bad or hostile download cannot change what the app writes to
  your hardware.
- **Commits and tags** are signed with an **SSH key registered on the maintainer's GitHub account**
  (from 2026-08-16; earlier history predates the key). This is what makes the source auditable:
  without it, anyone can author a commit under the maintainer's name and email, because git accepts
  whatever `user.email` says. GitHub marks signed commits **Verified**.

If you find a build, a model database or a commit that claims to be ours and does not verify, please
report it through the process below rather than running it.

## What the app checks on its own

GhostDeck runs with administrator rights, so a few things it does are checked rather than trusted:

- **Updates installed from the app** (Updates tab) are verified before anything is replaced: the
  downloaded file must carry a valid Authenticode signature from the GhostDeck release identity,
  and the version written into that signed file must be the one the release announced and newer
  than the running program. A file that fails is deleted and the tab says so; it is never run.
  The swap itself is checked step by step, and when it fails the previous program is put back.
- **The model database** fetched between releases must carry a valid signature (see above); an
  older or invalid file is refused.
- **A settings backup you import** is checked and rehearsed on a copy before any live setting
  changes; a file that fails leaves everything as it was.
- **The command channel** (`GhostDeck.exe --profile ...`, `ghostdeck://` links) is reachable only
  by your own user account, reads with a length cap and a deadline, and reports the real result of
  every command (exit code 1 with the reason when a write failed).
- **The release workflow** signs every release in CI and refuses to publish an unsigned or
  wrongly-signed build; every GitHub action it uses is pinned to a commit, not to a tag.

The code behind the updater, the command channel and the settings backup went through an
independent security review in October 2026; the fixes that came out of it shipped in v1.42.0.
If you run an older version, update: the in-app updater in versions before v1.42.0 does not
verify what it downloads.

## Reporting a vulnerability

Please do not report security vulnerabilities in public GitHub Issues or Discussions.

Use GitHub's private vulnerability reporting feature:

1. Open the repository's **Security** tab.
2. Select **Advisories**.
3. Click **Report a vulnerability**.

Please include:

- the affected GhostDeck version,
- Windows and laptop model,
- a clear description of the vulnerability,
- steps required to reproduce it,
- potential impact,
- any relevant logs or screenshots with private data removed.

Do not attach executable files, installers, credentials, API keys, or personal data.

You should receive an initial response within 7 days. Please allow reasonable time for investigation and remediation before publishing details.
