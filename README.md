# GhostDeck - for MSI laptops

<sub>*(formerly “MSI Profile Switcher” - renamed to keep the project clearly independent of MSI; see [docs/ABOUT_THE_NAME.md](docs/ABOUT_THE_NAME.md))*</sub>

A lightweight, **independent** Windows **tray app** to switch MSI laptop power profiles - **Silent / Balanced / Extreme / Super Battery** - instantly via global hotkeys, the tray menu, or auto-switch on AC/battery, with an on-screen overlay showing the active profile.

Built because **MSI Center 2.0 removed the _Silent_ profile**. This app talks to the Embedded Controller (EC) through MSI's own **WMI interface** - no kernel driver, no disabling of Windows security - so it works regardless of the MSI Center version (even with MSI Center uninstalled).

> ⚠️ **Hardware-specific.** Developed and tested on **MSI Raider GE78HX 13V** (board MS-17S1, i9-13950HX, EC firmware `17S1IMS1.114`), and confirmed by an owner on the **GE78 HX 14V** (`17S2IMS2`, same board). EC registers are model/firmware-specific - read [docs/TECHNICAL.md](docs/TECHNICAL.md) before trying it on another model. **Use at your own risk.**

📋 **~135 MSI models recognised** - the GE78HX board (13V + 14V) is confirmed on real hardware, the rest are experimental (opt-in). See the **[full supported-models list](docs/SUPPORTED_MODELS.md)**, or browse it live in the app's **Models** tab.

## Features

- 🖥️ Tray icon (color = active profile) with a profile menu, plus a **tabbed main window** (Scenarios / Status / Fan curve / Models / Report / Updates) with a **light / dark theme**
- ⌨️ Global, **rebindable** hotkeys (default `Ctrl+Alt+F1–F4`, `Ctrl+Alt+P` = cycle)
- 🔔 On-screen overlay (OSD) on every profile change
- 🎮 **Detachable gaming overlay (HUD)** - a small always-on-top panel with temps / fan RPM / profile / load / GPU% / VRAM / clocks / RAM / battery, in a **card or bar** layout. Pick which metrics to show, drag it anywhere (position remembered) or snap to a corner, toggle with a hotkey (default `Ctrl+Shift+O`). Rendered per-pixel with **independent background & content opacity**, smooth anti-aliased text, a readability shadow, optional click-through lock (`Ctrl+Shift+L`)
- 🌍 **8 languages** - EN / PL / DE / FR / ES / 中文 / PT-BR / RU
- 🎨 Custom color per profile
- 📊 **Status** - live CPU/GPU temperature & fan rings, **fan RPM**, CPU usage & **approx. clock**, **GPU load % / VRAM**, RAM, **battery %**, plus a live **EC profile-byte matrix** (what each profile writes vs. the current values). Extra metrics are read **driver-free** (Windows PDH counters - no kernel driver, anti-cheat-safe)
- 🌀 **Fan curve editor** - drag a custom CPU/GPU curve and run it on **Balanced / Extreme / Super Battery** (MSI Center only allows one in Extreme); fully reversible. *Silent is the exception:* its power cap lives in the same EC byte the curve needs, so turning a curve on in Silent necessarily leaves Silent for Balanced - the app warns and switches for you
- 🌪️ **Fan Boost** - force both fans to full speed with one click, a tray entry or a global hotkey (default `Ctrl+Alt+F5`), independent of the active profile; shown as a compact toggle "brick" on the Scenarios tab *(equivalent of MSI's Cooler Boost)*
- 📜 **Change-history log** - a running log of recent profile switches and EC writes (time, source: hotkey / tray / auto-AC / fan curve / external sync, the bytes written, and a readback), with a full-log window - handy for model-support reports
- 🛡️ **Firmware-change guard** - after a BIOS/EC update the app detects the changed firmware, blocks automatic writes and asks you to re-verify the model before it touches the EC again
- 🔌 Optional **auto-switch** on AC / battery (off by default, so it won't fight MSI software)
- 🔋 **Battery charge limit** (60 / 80 / 100 %)
- 🚀 **Start with Windows** (elevated scheduled task - no UAC nag at logon)
- 🔄 Syncs the UI if the profile is changed externally (e.g. by MSI Center)
- ⬇️ **Automatic update check** (once a day, can be disabled) - tray notification + one-click to the download page
- 📣 **Announcements & feedback** - occasional in-app notices (tray balloon + a dismissible banner) fetched read-only from the repo on the same daily check; a **Send feedback…** tray entry opens GitHub Discussions. No data is collected by the app (a plain download, same privacy footprint as the update check); both can be turned off with the update-check toggle

## Comparison with MSI software

GhostDeck is a small, focused tool - it deliberately does one thing (power/fan profiles) well, rather than replacing MSI Center. The table shows where it helps most: the **Silent** profile MSI removed, a fan curve outside just Extreme, no background services, and full transparency of what it writes to the EC.

| Feature | MSI Center 2.0 | GhostDeck |
|---|:---:|:---:|
| **Silent profile** | ❌ *(removed in 2.0)* | ✅ |
| Balanced / Extreme / Super Battery modes | ✅ | ✅ |
| Full fan speed (Fan Boost / MSI Cooler Boost) | ✅ | ✅ |
| Battery charge limit | ✅ *(60/80/100)* | ✅ *(60/80/100)* |
| Custom fan curve | Limited¹ | ✅ *(Balanced / Extreme / Super Battery)*¹ |
| Global **rebindable** hotkeys | Limited² | ✅ |
| Auto-switch profile on AC / battery | ❌ | ✅ |
| On-screen overlay (OSD) | ✅ *(profile / Fn keys)* | ✅ *(every function)*⁶ |
| Detachable gaming HUD overlay | ❌ | ✅ *(temps / RPM / GPU% / VRAM / clocks / RAM / battery)* |
| Live EC profile-byte view / transparency | ❌ | ✅ |
| Change & EC-write history log | ❌ | ✅ |
| Hardware monitoring | ✅ | Limited³ |
| Works with any / no MSI Center version | ❌ | ✅ |
| Installed size | ~950 MB⁴ + background services | ~155 MB⁵ *(single portable .exe, no services)* |
| RGB / keyboard / other MSI-Center features | ✅ | ❌ |
| Open source | ❌ | ✅ |

1. MSI Center only allows a custom fan curve in **Extreme**; this app runs one on **Balanced / Extreme / Super Battery**, fully reversible. **Silent** is a hardware exception: its power cap and the fan-curve mode share the same EC byte (`0xD4`), so enabling a curve in Silent necessarily switches the profile to Balanced (the app warns first).
2. MSI Center's shortcuts are limited; here every hotkey is global and rebindable.
3. Monitors CPU/GPU temperature, fan RPM, CPU & RAM usage via EC/WMI, plus GPU load %, VRAM, an approximate CPU clock and battery % via driver-free Windows PDH counters - not MSI Center's full telemetry.
4. MSI Center 2.0.x as the UWP app plus the files it installs to `C:\Program Files (x86)\MSI` on first launch.
5. Self-contained single `.exe` - no installer, no background service, no separate .NET runtime.
6. MSI Center shows an overlay for profile / Fn-key changes; this app shows one for **every** action it performs - profile switch **and** Fan Boost (and future functions) - so you always get feedback on what changed.

> The comparison is against **MSI Center 2.0** (the version that dropped Silent). This app is an **unofficial, independent** project - **not affiliated with, endorsed, sponsored or supported by MSI**. "MSI", "MSI Center" and "Cooler Boost" are trademarks of Micro-Star International Co., Ltd.; they are used here only descriptively to state compatibility.

## Screenshots

| | |
|:---:|:---:|
| ![Tray menu](docs/images/tray-menu.png) | ![Scenarios](docs/images/scenarios.png) |
| **Tray menu** - switch profile, Status, Language, Settings | **Scenarios** - large profile tiles + charge limit and AC/battery auto-switch |
| ![Status](docs/images/status.png) | ![Settings](docs/images/settings.png) |
| **Status** - temperature/fan rings, fan RPM, CPU usage and RAM, tier badge | **Settings** - grouped cards: theme, language, power, startup, rebindable hotkeys |
| ![Report my model](docs/images/report_my_model.png) | ![Updates](docs/images/updates.png) |
| **Report my model** - guided in-app EC capture → pre-filled GitHub issue | **Updates** - installed version, check now, release history |
| ![Fan curve](docs/images/fan_curve.png) | ![Status EC bytes](docs/images/status_ec.png) |
| **Fan curve** - drag a custom CPU/GPU fan curve (manual fan control) | **Status (EC bytes)** - live profile-byte matrix, legend and fan-curve tables |

## Download

Grab the latest **`GhostDeck.exe`** from the [**Releases**](../../releases) page.
It's a single, self-contained file - no install, no .NET runtime needed. Run it and approve the UAC prompt (EC access requires administrator).

## Supported models

Each model is **✅ tested** (verified on real hardware) or **⚗️ experimental** (built from the [msi-ec](https://github.com/BeardOverflow/msi-ec) register maps but not yet verified - the "Silent" power-cap behaviour is unconfirmed). On an **unrecognized firmware** the app runs **read-only** (Status works, writes disabled), so it never writes wrong registers on an untested machine.

Experimental models are **opt-in**: enable them in *Settings → Power → "Enable experimental models"*. They write only documented MSI shift/fan registers (low risk), but switching may not give the same low-power "Silent" until an owner confirms it.

**~134 models** are recognised, grouped into two EC families taken from the [msi-ec](https://github.com/BeardOverflow/msi-ec) maps and cross-checked against [MControlCenter](https://github.com/dmitry-s93/MControlCenter):

| Tier | Models | EC firmware / registers | Fan curve |
|---|---|---|---|
| ✅ **Tested** | **MSI Raider GE78HX / Vector GP78HX 13V**, **GE78 HX 14V** | `17S1IMS1.*`, `17S2IMS2.*` - shift `0xD2` / fan `0xD4` | ✅ editable |
| ⚗️ **G2 family** (~101) | Raider / Vector / Titan HX (13V–14V), Stealth 16-18, Sword / Pulse / Crosshair 16, Katana, Cyborg, Bravo, Modern / Prestige / Summit | shift `0xD2` / fan `0xD4` / super-batt `0xEB` | ◉ editable after opt-in (unverified) |
| ⚗️ **G1 family** (~33) | older GS / GF / GE / GP, Modern, Alpha, Bravo, Delta, Creator | shift `0xF2` / fan `0xF4` / charge `0xEF` | - profiles only |

The G2 fan-curve tables use the fixed addresses (CPU `0x6A`/`0x72`, GPU `0x82`/`0x8A`) that MControlCenter writes across the whole family, so they are practice-confirmed; on experimental models the curve is editable once you opt in, but stays flagged **unverified** until you compare it with MSI Center on your own model. See the **[full per-firmware list of all ~135 models](docs/SUPPORTED_MODELS.md)** (source of truth: [`Devices.cs`](Devices.cs)). A handful of models whose msi-ec config documents no "Silent" fan value are deliberately left out (Silent is this app's core function - guessing it would be unsafe).

**Got a different MSI - or own an experimental one and can confirm it works?** The easiest way is right inside the app: tray menu → **Report my model…** (also a button in the Status window). It walks you through a read-only EC capture in each MSI Center scenario, builds the report, copies it to your clipboard, saves it to a file, and opens a pre-filled GitHub issue - just paste and submit. (Requires MSI Center installed as the scenario reference.)

Prefer to do it by hand? Open a **[Model support request](../../issues/new?template=model-support.yml)** with your EC firmware (shown in the app's Status window) and the output of the diagnostic scripts in [`scripts/diagnostics/`](scripts/diagnostics). The procedure is in [docs/TECHNICAL.md](docs/TECHNICAL.md) §11.

## Build from source

Requires the **.NET 8 SDK**.

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The app icon is generated by `tools/gen-icon.ps1` (already committed as `app.ico`).

## How it works (short version)

Each profile is a small set of EC register writes sent through `root\wmi` → **`MSI_ACPI.Set_Data`** (a 32-byte `Package_32` buffer: `Bytes[0]` = address, `Bytes[1]` = value). The key lever is the **fan-mode register `0xD4`** (Silent = `0x1D`), which the EC firmware ties to the power cap - in testing it dropped package power from ~104 W to ~30 W under load.

Full reverse-engineering write-up, register map, measurements and the diagnostic scripts: **[docs/TECHNICAL.md](docs/TECHNICAL.md)** - also available in **[Polski](docs/TECHNICAL.pl.md)**.

Full per-firmware list of every recognised model: **[docs/SUPPORTED_MODELS.md](docs/SUPPORTED_MODELS.md)** (also shown live in the app's **Models** tab).

EC register map credit: [**BeardOverflow/msi-ec**](https://github.com/BeardOverflow/msi-ec).

## FAQ

**Can I set an exact wattage (a power slider)?**
Not the way this app works - and that's actually the core reason it exists. The app doesn't set watts directly: it flips MSI's built-in EC power *modes* (the Silent / Balanced / Extreme presets), and the firmware decides the wattage for each mode. Setting an arbitrary PL1/PL2 number would mean writing Intel's power-limit registers - but on these MSI laptops those are **locked** (MSR is BIOS-locked, MMIO is overridden by Intel DTT). That's exactly why ThrottleStop and Intel XTU can't cap wattage on most of these machines either. MSI's EC doesn't expose a writable power-limit register, and the msi-ec maps don't show one for the boards I've checked - so from software, on a locked machine, a free slider isn't on the table.

**There is one route to a real slider, though - outside this app.** If your model lets you disable **Overclocking Lock / CFG Lock** in the hidden Advanced BIOS, the MSR power-limit registers open up, and **ThrottleStop** (or Intel XTU) can then set PL1/PL2 directly - that's your actual watt slider. Caveats: (1) on many 13th-gen MSI these BIOS options are greyed out or locked by microcode, so it's not guaranteed; (2) it's a manual, at-your-own-risk change in an unofficial BIOS menu; (3) even after the MSR is unlocked, Intel DTT can still override the limit via MMIO.

**Is there any risk of damaging my laptop?**
Very low. The app uses MSI's **official WMI interface** (the same channel MSI Center uses), writes only the exact register values MSI's own profiles use, and EC writes are **volatile** - a reboot resets the EC to firmware defaults (nothing is flashed). On an **unrecognized firmware it stays read-only** and writes nothing. The CPU also keeps its own hardware thermal protection that no EC write can disable. Experimental models are opt-in and write only documented mode registers.

**Why does it ask for administrator (UAC)?**
EC access via WMI requires elevation. Launching manually shows one UAC prompt; the *Start with Windows* option uses an elevated scheduled task so there's **no UAC nag at every logon**.

## Testing the model gate (developer)

To preview the **experimental** / **unsupported** UI on any machine, set the `MSIPS_FORCE_FIRMWARE` environment variable to a firmware string before launching. The app then **simulates** that firmware and performs **no EC writes** (UI preview only):

```powershell
# Run from an ADMIN PowerShell so the variable reaches the elevated app:
$env:MSIPS_FORCE_FIRMWARE = "16V1EMS1.100"   # an experimental model
# or "ZZZZ" for an unsupported firmware
& .\GhostDeck.exe
# (close it, clear the variable, relaunch to return to normal)
```

The Status window / tray show a `(test)` marker while simulating.

## License

[MIT](LICENSE) © 2026 wygodad
