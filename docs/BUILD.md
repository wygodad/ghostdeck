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
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none -p:Version=X.Y.Z -o release
```
Output: `release/GhostDeck.exe` (~154 MB, self-contained, requires admin to *run*).
A local re-publish to `release/` needs the running app closed first (file lock) — exit it from the tray.

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
GitHub Actions then builds the self-contained exe and publishes a **Release** with the
exe attached and the notes taken from the matching CHANGELOG section.

> Don't tag a release of an untested feature. Push to `main` first, test the local exe,
> then tag.
