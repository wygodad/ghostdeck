# Building & releasing

> **Building the `.exe` does NOT need admin rights and does NOT need an MSI laptop.**
> This checkout targets .NET 11 and builds with the .NET 11 SDK. Only *running / testing* the app
> needs an MSI laptop (the EC/WMI interface) and elevation (UAC).

## Prerequisites
- .NET 11 SDK (`dotnet --version` → 11.x; locally validated with 11.0.100).

## Compile check (fast)
```powershell
dotnet build -c Release
```

Restore and run the hardware-free Cross-Cooling test executable:

```powershell
dotnet restore
dotnet run --project tests/GhostDeck.Tests.csproj -c Release
python tools/lang-check.py
python tools/dpi-check.py
```

The app is built entirely in code. Runtime-only WinForms control properties explicitly use
`DesignerSerializationVisibility.Hidden`, satisfying the .NET 11 WFO1000 analyzer without
disabling it. The existing .NET 11 target and System.Management package migration are retained.

In a restricted runner that denies the compiler's named pipe (`MSB3883`, access denied under
`\\.\pipe\LOCAL\dotnet_*`), use:

```powershell
python tools/restricted-build.py --configuration Release --tests
```

This restores dependencies, gets MSBuild's exact compiler arguments, runs the installed SDK's
compiler and analyzers directly, and completes MSBuild output assembly. It returns failure on
any compiler or test error. It produces the Windows app host, DLLs and runtime files under
`bin/Release/net11.0-windows7.0`; use the publish command below for a single-file executable. This is an environment
workaround, not a suppression of source errors. See [CROSS-COOLING.md](CROSS-COOLING.md) for what
is and is not hardware-validated.

To validate and publish with the existing Visual Studio FolderProfile in that restricted runner:

```powershell
python tools/restricted-build.py --configuration Release --tests --publish
```

The profile produces a self-contained folder at `bin/Release/net11.0/publish/win-x64/`.
Run `GhostDeck.exe` from that folder and keep its companion files together.

## Produce the single-file exe
From the repo root:
```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -p:Version=X.Y.Z -o release
```
Output: `release/GhostDeck.exe` (framework-dependent, requires the .NET 11 Desktop Runtime and admin to *run*).
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
GitHub Actions then builds the exe with a pinned SDK (11.0.100), **signs it** (Azure Artifact Signing via
OIDC; publisher "WYGODA DAWID FENIX INSPIRE") and publishes a **Release** with the exe
attached and the notes taken from the matching CHANGELOG section. The workflow verifies the
signature and **refuses to publish** when it is missing or has an unexpected subject, so a
failed signing step can never ship an unsigned exe. Details: [TECHNICAL.md](TECHNICAL.md) §35.

To test the signing pipeline without publishing anything: GitHub → Actions → **Release** →
*Run workflow* with the **dry-run** box ticked. It builds and signs, then uploads the exe as
a workflow artifact instead of creating a release.

> Don't tag a release of an untested feature. Push to `main` first, test the local exe,
> then tag.
