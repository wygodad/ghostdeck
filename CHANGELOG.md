# Changelog

All notable changes to this project are documented here.
Format loosely based on [Keep a Changelog](https://keepachangelog.com/).

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
