# Changelog

All notable changes to this project are documented here.
Format loosely based on [Keep a Changelog](https://keepachangelog.com/).

## [1.5.0] - 2026-06-28
### Added
- **New tabbed main window** styled after MSI Center: top tabs for **Scenarios**, **Status**, **Settings**, **Report model**, and **Updates** — opening Status or Report from the tray now shows that content inside the same window.
- **Scenarios** tab with large clickable profile tiles (icon + name + hint), plus inline **charge limit** and **AC/battery auto-switch** controls.
- **Status** tab with CPU/GPU temperature and CPU/GPU fan ring gauges plus a details table.
- **Settings** tab fully inline and grouped into cards (appearance, power, startup, updates, hotkeys) with a **restore default hotkeys** button — no more separate dialog.
- **Updates** tab: installed version, "check now" with last-checked time, and the last 5 releases with changelog highlights.
- **Light / dark theme** toggle (persisted), and the main window remembers its size and position.

## [1.4.1] - 2026-06-28
### Added
- Experimental support for **MSI Crosshair A16 HX (D7W/D8W)** (firmware `15PLIMS1`), added from a community EC snapshot ([#2](https://github.com/wygodad/msi-profile-switcher/issues/2)). Shift/fan registers match the G2 recipe exactly; uses no super-battery register and leaves a secondary fan bit untouched pending hardware verification.

## [1.4.0] - 2026-06-27
### Added
- **Automatic update check**: once a day the app asks GitHub for the latest release and, if a newer version exists, shows a tray notification and a green **"⬇ Download new version"** menu item — one click opens the Releases page. Read-only, failures are silent, and it can be turned off in **Settings → Power → "Check for updates"** (on by default).

## [1.3.1] - 2026-06-27
### Fixed
- "Report my model" wizard: taller default window so the left column's **Firmware EC** row is fully visible (notably in the Polish UI, where the longer text pushed it off-screen). The window height also stays user-resizable.

## [1.3.0] - 2026-06-27
### Added
- **"Report my model…" wizard** (tray menu + button in the Status window): a modern, animated dialog that guides a read-only EC capture in each MSI Center scenario (live per-byte progress bar), builds the full report, copies it to the clipboard, saves it to a file, and opens a pre-filled GitHub "Model support request" — no PowerShell, no manual copy-paste. Includes guidance to use MSI Center 2.0.48 (last version with a working SILENT scenario), direct download links (Uptodown, with the version list as a fallback), and a link to MSI's official uninstaller. The `scripts/diagnostics/` flow remains as a fallback and for post-BIOS re-derivation.

## [1.2.3] - 2026-06-27
### Added
- Tray menu profile entries now show a coloured swatch matching each profile's colour (custom colours included); the active profile's swatch is highlighted.

## [1.2.2] - 2026-06-27
### Added
- Coloured tier badge in the Status window: green **TESTED**, amber **EXPERIMENTAL**, red **UNSUPPORTED**.
- `MSIPS_FORCE_FIRMWARE` developer switch to preview the experimental / unsupported UI — it simulates a firmware and performs **no EC writes**.
### Fixed
- Status "Model" row no longer overflows; the full model name is shown and the tier moved into the badge.

## [1.2.1] - 2026-06-26
### Added
- Tested / Experimental indicator in the Status window and tray menu.
### Changed
- Diagnostic scripts (`scripts/diagnostics/`) translated to English; clearer step-by-step model-support issue template.
### Fixed
- Status "Model" row overflow that overlapped the CPU temperature line (ellipsis + tooltip + wider window).

## [1.2.0] - 2026-06-26
### Added
- Experimental support for 7 MSI "Gaming Intel" models — GE68HX 13V, GS66 / GS65 Stealth, Katana GF66 / GF76, GE66 Raider / GP66 Leopard, GF65 Thin — built from the [msi-ec](https://github.com/BeardOverflow/msi-ec) register maps.
- Device **tier** system (Tested vs Experimental) and an **opt-in** toggle for experimental models (Settings → Power).

## [1.1.0] - 2026-06-26
### Added
- Multi-model device layer and a **firmware safety gate**: on an unrecognized EC firmware the app stays read-only (no writes).
- "Model support request" issue template for community contributions.

## [1.0.1] - 2026-06-26
### Added
- "Always on top" toggle for the Status window (persisted).
### Fixed
- Status window widened and header auto-sized so profile names are no longer cut off.

## [1.0.0] - 2026-06-26
### Added
- Initial release. Tray app to switch MSI power profiles (Silent / Balanced / Extreme / Super Battery) via the tray menu or global hotkeys, with an on-screen overlay.
- 8 UI languages, per-profile colours, Status / Diagnostics window (live CPU/GPU temperatures & fans via EC), autostart, AC/battery auto-switch, battery charge limit.
- EC control through MSI's official WMI interface (`root\wmi` → `MSI_ACPI`) — no kernel driver, no security changes.
