# MSI Profile Switcher

A lightweight Windows **tray app** to switch MSI laptop power profiles — **Silent / Balanced / Extreme / Super Battery** — instantly via global hotkeys, the tray menu, or auto-switch on AC/battery, with an on-screen overlay showing the active profile.

Built because **MSI Center 2.0 removed the _Silent_ profile**. This app talks to the Embedded Controller (EC) through MSI's own **WMI interface** — no kernel driver, no disabling of Windows security — so it works regardless of the MSI Center version (even with MSI Center uninstalled).

> ⚠️ **Hardware-specific.** Developed and tested on **MSI Raider GE78HX 13V** (board MS-17S1, i9-13950HX, EC firmware `17S1IMS1.114`). EC registers are model/firmware-specific — read [docs/TECHNICAL.md](docs/TECHNICAL.md) before trying it on another model. **Use at your own risk.**

## Features

- 🖥️ Tray icon (color = active profile) with a profile menu, plus a **tabbed main window** (Scenarios / Status / Fan curve / Report model / Updates) with a **light / dark theme**
- ⌨️ Global, **rebindable** hotkeys (default `Ctrl+Alt+F1–F4`, `Ctrl+Alt+P` = cycle)
- 🔔 On-screen overlay (OSD) on every profile change
- 🌍 **8 languages** — EN / PL / DE / FR / ES / 中文 / PT-BR / RU
- 🎨 Custom color per profile
- 📊 **Status** — live CPU/GPU temperature & fan rings, **fan RPM**, CPU usage, RAM, plus a live **EC profile-byte matrix** (what each profile writes vs. the current values)
- 🌀 **Fan curve editor** — drag a custom CPU/GPU curve and run it on *any* profile (e.g. a quiet-but-precise curve in Silent, which MSI Center only allows in Extreme); fully reversible
- 🔌 Optional **auto-switch** on AC / battery (off by default, so it won't fight MSI software)
- 🔋 **Battery charge limit** (60 / 80 / 100 %)
- 🚀 **Start with Windows** (elevated scheduled task — no UAC nag at logon)
- 🔄 Syncs the UI if the profile is changed externally (e.g. by MSI Center)
- ⬇️ **Automatic update check** (once a day, can be disabled) — tray notification + one-click to the download page

## Screenshots

| | |
|:---:|:---:|
| ![Tray menu](docs/images/tray-menu.png) | ![Scenarios](docs/images/scenarios.png) |
| **Tray menu** — switch profile, Status, Language, Settings | **Scenarios** — large profile tiles + charge limit and AC/battery auto-switch |
| ![Status](docs/images/status.png) | ![Settings](docs/images/settings.png) |
| **Status** — temperature/fan rings, fan RPM, CPU usage and RAM, tier badge | **Settings** — grouped cards: theme, language, power, startup, rebindable hotkeys |
| ![Report my model](docs/images/report_my_model.png) | ![Updates](docs/images/updates.png) |
| **Report my model** — guided in-app EC capture → pre-filled GitHub issue | **Updates** — installed version, check now, release history |
| ![Fan curve](docs/images/fan_curve.png) | ![Status EC bytes](docs/images/status_ec.png) |
| **Fan curve** — drag a custom CPU/GPU fan curve (manual fan control) | **Status (EC bytes)** — live profile-byte matrix, legend and fan-curve tables |

## Download

Grab the latest **`MSIProfileSwitcher.exe`** from the [**Releases**](../../releases) page.
It's a single, self-contained file — no install, no .NET runtime needed. Run it and approve the UAC prompt (EC access requires administrator).

## Supported models

Each model is **✅ tested** (verified on real hardware) or **⚗️ experimental** (built from the [msi-ec](https://github.com/BeardOverflow/msi-ec) register maps but not yet verified — the "Silent" power-cap behaviour is unconfirmed). On an **unrecognized firmware** the app runs **read-only** (Status works, writes disabled), so it never writes wrong registers on an untested machine.

Experimental models are **opt-in**: enable them in *Settings → Power → "Enable experimental models"*. They write only documented MSI shift/fan registers (low risk), but switching may not give the same low-power "Silent" until an owner confirms it.

**~134 models** are recognised, grouped into two EC families taken from the [msi-ec](https://github.com/BeardOverflow/msi-ec) maps and cross-checked against [MControlCenter](https://github.com/dmitry-s93/MControlCenter):

| Tier | Models | EC firmware / registers | Fan curve |
|---|---|---|---|
| ✅ **Tested** | **MSI Raider GE78HX / Vector GP78HX 13V** | `17S1IMS1.*` — shift `0xD2` / fan `0xD4` | ✅ editable |
| ⚗️ **G2 family** (~101) | Raider / Vector / Titan HX (13V–14V), Stealth 16-18, Sword / Pulse / Crosshair 16, Katana, Cyborg, Bravo, Modern / Prestige / Summit | shift `0xD2` / fan `0xD4` / super-batt `0xEB` | 👁️ read-only preview |
| ⚗️ **G1 family** (~33) | older GS / GF / GE / GP, Modern, Alpha, Bravo, Delta, Creator | shift `0xF2` / fan `0xF4` / charge `0xEF` | — profiles only |

The G2 read-only fan-curve preview uses the fixed table addresses (CPU `0x6A`/`0x72`, GPU `0x82`/`0x8A`) that MControlCenter writes across the whole family, so they are practice-confirmed; previews stay read-only until you compare them with MSI Center on your own model. See the **[full per-firmware list of all ~135 models](docs/SUPPORTED_MODELS.md)** (source of truth: [`Devices.cs`](Devices.cs)). A handful of models whose msi-ec config documents no "Silent" fan value are deliberately left out (Silent is this app's core function — guessing it would be unsafe).

**Got a different MSI — or own an experimental one and can confirm it works?** The easiest way is right inside the app: tray menu → **Report my model…** (also a button in the Status window). It walks you through a read-only EC capture in each MSI Center scenario, builds the report, copies it to your clipboard, saves it to a file, and opens a pre-filled GitHub issue — just paste and submit. (Requires MSI Center installed as the scenario reference.)

Prefer to do it by hand? Open a **[Model support request](../../issues/new?template=model-support.yml)** with your EC firmware (shown in the app's Status window) and the output of the diagnostic scripts in [`scripts/diagnostics/`](scripts/diagnostics). The procedure is in [docs/TECHNICAL.md](docs/TECHNICAL.md) §11.

## Build from source

Requires the **.NET 8 SDK**.

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The app icon is generated by `tools/gen-icon.ps1` (already committed as `app.ico`).

## How it works (short version)

Each profile is a small set of EC register writes sent through `root\wmi` → **`MSI_ACPI.Set_Data`** (a 32-byte `Package_32` buffer: `Bytes[0]` = address, `Bytes[1]` = value). The key lever is the **fan-mode register `0xD4`** (Silent = `0x1D`), which the EC firmware ties to the power cap — in testing it dropped package power from ~104 W to ~30 W under load.

Full reverse-engineering write-up, register map, measurements and the diagnostic scripts: **[docs/TECHNICAL.md](docs/TECHNICAL.md)** — also available in **[Polski](docs/TECHNICAL.pl.md)**.

EC register map credit: [**BeardOverflow/msi-ec**](https://github.com/BeardOverflow/msi-ec).

## FAQ

**Can I set an exact wattage (a power slider)?**
Not the way this app works — and that's actually the core reason it exists. The app doesn't set watts directly: it flips MSI's built-in EC power *modes* (the Silent / Balanced / Extreme presets), and the firmware decides the wattage for each mode. Setting an arbitrary PL1/PL2 number would mean writing Intel's power-limit registers — but on these MSI laptops those are **locked** (MSR is BIOS-locked, MMIO is overridden by Intel DTT). That's exactly why ThrottleStop and Intel XTU can't cap wattage on most of these machines either. MSI's EC doesn't expose a writable power-limit register, and the msi-ec maps don't show one for the boards I've checked — so from software, on a locked machine, a free slider isn't on the table.

**There is one route to a real slider, though — outside this app.** If your model lets you disable **Overclocking Lock / CFG Lock** in the hidden Advanced BIOS, the MSR power-limit registers open up, and **ThrottleStop** (or Intel XTU) can then set PL1/PL2 directly — that's your actual watt slider. Caveats: (1) on many 13th-gen MSI these BIOS options are greyed out or locked by microcode, so it's not guaranteed; (2) it's a manual, at-your-own-risk change in an unofficial BIOS menu; (3) even after the MSR is unlocked, Intel DTT can still override the limit via MMIO.

**Is there any risk of damaging my laptop?**
Very low. The app uses MSI's **official WMI interface** (the same channel MSI Center uses), writes only the exact register values MSI's own profiles use, and EC writes are **volatile** — a reboot resets the EC to firmware defaults (nothing is flashed). On an **unrecognized firmware it stays read-only** and writes nothing. The CPU also keeps its own hardware thermal protection that no EC write can disable. Experimental models are opt-in and write only documented mode registers.

**Why does it ask for administrator (UAC)?**
EC access via WMI requires elevation. Launching manually shows one UAC prompt; the *Start with Windows* option uses an elevated scheduled task so there's **no UAC nag at every logon**.

## Testing the model gate (developer)

To preview the **experimental** / **unsupported** UI on any machine, set the `MSIPS_FORCE_FIRMWARE` environment variable to a firmware string before launching. The app then **simulates** that firmware and performs **no EC writes** (UI preview only):

```powershell
# Run from an ADMIN PowerShell so the variable reaches the elevated app:
$env:MSIPS_FORCE_FIRMWARE = "16V1EMS1.100"   # an experimental model
# or "ZZZZ" for an unsupported firmware
& .\MSIProfileSwitcher.exe
# (close it, clear the variable, relaunch to return to normal)
```

The Status window / tray show a `(test)` marker while simulating.

## License

[MIT](LICENSE) © 2026 wygodad
