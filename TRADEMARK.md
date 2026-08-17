# GhostDeck trademark policy

**GhostDeck™** is the name of this project. The name is not covered by the code license in
[LICENSE](LICENSE); the logo and icons are covered separately by
[LICENSE-ASSETS.md](LICENSE-ASSETS.md).

GhostDeck is free software and forks are welcome. This policy exists for one reason: so that a
user downloading something called GhostDeck can tell whether it came from this project. An
application that writes to a laptop's embedded controller is exactly the kind of software where
that distinction matters.

## Using the name without asking

- Say that your project **uses, is based on, or is a fork of** GhostDeck. "Fork of GhostDeck",
  "based on GhostDeck", "compatible with GhostDeck" are all fine.
- Write about GhostDeck: articles, reviews, tutorials, videos, comparisons, forum posts.
- Refer to the project by name in documentation, package descriptions and bug reports.
- Distribute **unmodified official releases** under the name GhostDeck, including mirrors and
  package repositories.

No permission is needed for any of the above, and none of it requires this policy to be quoted.

## Using the name with permission

Ask first if you want to:

- name a **modified version** GhostDeck, or use a name built on it such as "GhostDeck Pro",
  "GhostDeck Plus" or "GhostDeck Enterprise"
- use the name in a way that suggests your product is official, endorsed or supported
- use the name in your own product name, company name or domain

Contact the project owner through the repository and describe the intended use.

## Official builds

Official GhostDeck releases are published at
https://github.com/wygodad/ghostdeck/releases and are **digitally signed**. You can check any
build with:

```powershell
Get-AuthenticodeSignature .\GhostDeck-win-x64.exe
```

A valid Authenticode signature naming the GhostDeck publisher lets you verify that a release
binary was signed by this project and has not been altered since. Modified or independently
built versions will not carry that signature. If a file presented to you as an official release
does not carry it, do not run it.

## About MSI

GhostDeck is an independent project and is not affiliated with, endorsed by, or connected to
Micro-Star International Co., Ltd. "MSI" and MSI product names appear in GhostDeck only to
describe which hardware a feature applies to. They are MSI's trademarks, not this project's.

---

Copyright © 2026 Dawid Wygoda.
