# Bundled component licenses

`GhostDeck.exe` is published as a **framework-dependent single file**. It requires the .NET 8
Desktop Runtime to be installed, and it does not carry that runtime inside itself. What the
executable does bundle is:

| Bundled | Owner | License |
|---|---|---|
| GhostDeck's own code | this project | see `LICENSE` at the repository root |
| `System.Management` | Microsoft (.NET) | MIT |
| the native application host (`apphost`) | Microsoft (.NET) | MIT |

No `coreclr.dll`, no .NET runtime and no Windows Forms libraries are included in the
executable; those are supplied by the runtime the user installs from Microsoft.

## Why the apphost is MIT

Microsoft lists the .NET binaries that fall under the .NET Library License rather than MIT:
`coreclr.dll` and .NET runtimes included in **single-file** binaries, `Microsoft.DiaSymReader.Native`,
and three WPF-specific libraries. The document then states that all other binaries and files are
MIT. The application host is not on that list, and GhostDeck is a Windows Forms application, so
none of the WPF entries apply either.

Microsoft's own breakdown is reproduced in `dotnet-license-information-windows.md`.

## Files here

| File | What it is |
|---|---|
| `dotnet-MIT-LICENSE.txt` | The .NET MIT license, covering `System.Management` and the apphost |
| `dotnet-ThirdPartyNotices.txt` | The upstream .NET third-party notice file, retained alongside the bundled apphost as a conservative compliance measure. It covers .NET as a whole and may list components that are not present in GhostDeck |
| `dotnet-license-information-windows.md` | Microsoft's breakdown of which .NET binaries fall under which license |

The two Microsoft files above were copied from a .NET 8 installation (SDK 8.0.424 / runtime
8.0.30) and from the `dotnet/core` repository. Re-copy them when the pinned SDK is raised.

`System.CodeDom` appears as a declared dependency of `System.Management` but contributes no
assembly to the published artefact: its entry in `GhostDeck.deps.json` carries no runtime
assets and no `System.CodeDom.dll` is deployed. It is therefore not a bundled component.

GhostDeck's own license is in `LICENSE` at the repository root; the provenance of its hardware
data is recorded in `THIRD-PARTY-NOTICES.md`.
