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
