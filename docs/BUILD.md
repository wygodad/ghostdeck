# Building & releasing

> **Building the `.exe` does NOT need admin rights and does NOT need an MSI laptop.**
> It works on any machine with the .NET 8 SDK. Only *running / testing* the app
> needs an MSI laptop (the EC/WMI interface) and elevation (UAC).

## Prerequisites
- .NET 8 SDK (`dotnet --version` → 8.x). Already installed on the dev machine.

## Compile check (fast)
```powershell
dotnet build -c Release
```

## Produce the single-file exe
From the repo root:
```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -p:Version=X.Y.Z -o release
```
Output: `release/GhostDeck.exe` (~2.5 MB, framework-dependent, requires the .NET 8 Desktop Runtime and admin to *run*).
The release workflow renames this file to `GhostDeck-win-x64.exe` after signing; that is the name published as the release asset.
A local re-publish to `release/` needs the running app closed first (file lock) - exit it from the tray.
Local builds are **unsigned**; only the release workflow signs (see below).

## Day-to-day
```powershell
git commit -am "..."      # CI (.github/workflows) build-checks every push to main
git push origin main
```

## Cutting a release
1. Add a `## [X.Y.Z] - YYYY-MM-DD` section at the top of [`../CHANGELOG.md`](../CHANGELOG.md).
2. Commit it.
3. Tag and push:
   ```powershell
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```
GitHub Actions then builds the exe with a pinned SDK (8.0.424), **signs it** (Azure Artifact Signing via
OIDC; publisher "WYGODA DAWID FENIX INSPIRE") and publishes a **Release** with the exe
attached and the notes taken from the matching CHANGELOG section. The workflow verifies the
signature and **refuses to publish** when it is missing or has an unexpected subject, so a
failed signing step can never ship an unsigned exe. Details: [TECHNICAL.md](TECHNICAL.md) §35.

To test the signing pipeline without publishing anything: GitHub → Actions → **Release** →
*Run workflow* with the **dry-run** box ticked. It builds and signs, then uploads the exe as
a workflow artifact instead of creating a release.

> Don't tag a release of an untested feature. Push to `main` first, test the local exe,
> then tag.
