# Cross-Cooling

Cross-Cooling is an optional live controller in **Fan curve → Cross-Cooling**. Enabling it
captures the current six-node CPU/GPU tables and fan mode, raises selected speed tables using
combined curve demand, and enters the device's Advanced fan mode. It uses the existing
`Ec.WriteFanCurve` / `Ec.SetFanMode` path, with no new direct PWM registers.

Off is the default. The enabled state, selected mode and options are saved. Enabled control
is restored after startup profile/curve setup, subject to the existing hardware and firmware
gates. Turning the switch off saves that choice without losing the selected mode. Normal exit
restores the hardware baseline while keeping the enabled preference for the next start.
The switch reports actual controller activation. There are no simulated-temperature controls.

## Modes and mathematics

Temperatures are converted independently with `CurveModel.SpeedAt`:
`Dc = CpuCurve(Tc)`, `Dg = GpuCurve(Tg)`. Temperatures themselves are not averaged.

| Mode | CPU target | GPU target |
| --- | --- | --- |
| Off | Dc | Dg |
| CPU → GPU fan | Dc | max(Dg, M) |
| GPU → CPU fan | max(Dc, M) | Dg |
| Both fans | max(Dc, M) | max(Dg, M) |

| Algorithm | Mixed demand M |
| --- | --- |
| Maximum (default) | max(Dc, Dg) |
| Average | (Dc + Dg) / 2 |
| Weighted Average | (Wc × Dc + Wg × Dg) / (Wc + Wg) |
| Priority Boost | Per selected fan: Own + B × max(0, Other - Own) |

Priority Boost's displayed mixed reference is max(Dc, Dg); its targets apply B separately.
Weights accept 0–100 and are normalized by their positive sum. CPU Priority = 75/25,
GPU Priority = 25/75, Balanced = 50/50. The UI rejects 0/0; malformed saved weights recover
to Balanced. Targets retain their own original-demand floor and the device's maximum,
including 150% where specified.

Selected speed nodes become `max(originalNode, ceil(target))`. Temperature nodes remain
unchanged; tables stay monotonic and never fall below the captured original table.
The unselected fan retains its original table. Both + Maximum requests equal demands;
different hardware and firmware can still produce different actual RPM.
Curve interpolation estimates demand; it is not measured duty or airflow.

## Scheduling and status

| Option | Default | Range |
| --- | --- | --- |
| Mode | Off | Off / CPU → GPU / GPU → CPU / Both |
| Algorithm | Maximum | Four algorithms above |
| Sampling interval | 2 seconds | 1–10 seconds |
| Minimum table update interval | 5 seconds | 5–60 seconds |
| Meaningful change | 5 percentage points | 1–20 pp |
| Increase hysteresis | 0 pp | 0–10 pp |
| Decrease hysteresis | 5 pp | 0–20 pp |
| Maximum decrease per update | 5 pp | 1–20 pp |
| Temperature smoothing | Off | On / Off |
| Weights | 50/50 | 0–100 each, positive total |
| Boost factor | 100% | 0–100% |

The tray owns the controller and timer. Sampling and EC writes run on a worker and continue
when the page closes. The page timer only refreshes status. Smoothing keeps 65% of the previous
temperature on a fall and passes increases immediately; raw own demand remains a floor.
Targets can precede applied tables because updates are bounded. Settings changes retain the
write schedule so dragging sliders cannot flood EC writes.

The page displays active/paused/error status, real temperatures, estimated demands, targets,
firmware duty and available RPM. **Last successful EC update** is set only after table and mode
calls complete successfully. It confirms write-call completion, not physical airflow or
sustained firmware retention. Stale readings are hidden.

## Ownership and restoration

Control requires a supported dual-fan, six-point method interface and existing write-permission
and firmware-change gates. Experimental entries require the existing Experimental opt-in.
Single-fan, unsupported, application simulation and backup WMI paths are excluded.
`FanCurveSpec.Verified` remains the existing confidence marker, not a new write gate.

The controller serializes its read/calculate/write transaction. Existing EC sessions suspend
it and restore the baseline before profiles, presets, scenes, diagnostics or other writers
proceed. Editing views suspend control before reading/editing tables. After the operation,
a new baseline is captured so old floors never become the new user curve. Active-curve
settings and presets retain original tables. A model database swap disables and restores
the old device controller first.

Fan Boost, Silent, sleep, curve editing and another EC session pause control. It resumes
after those conditions clear while enabled. Missing/zero/sleeping-GPU temperatures, values
above 115°C, invalid tables and stale/future samples restore and pause. Staleness is
max(6 seconds, three sampling intervals).

A hardware read/write exception restores and disarms until explicitly enabled again.
If another program changes Advanced mode, control stops and preserves that newly selected
mode while restoring original tables. Failed restoration retains the baseline for retry
and requests the Balanced recipe's native Auto mode as fallback. Failures are logged and
displayed; fallback is an attempt, not a guarantee of successful hardware access.

Disabling, Off, reset, Panic Reset and normal exit restore original tables and mode.
Off, reset and Panic Reset also save the disabled choice; normal exit preserves it.
Closing the page leaves the tray controller running. Forced termination cannot run cleanup:
applied floor tables may remain until firmware resets them. Firmware retention and response
can differ across models. Native thermal protections are not modified.

`AppSettings.CrossCooling` supports serialization, backups, cloning and import, preserving
unknown option fields. Preset and scene JSON contracts are unchanged. Actual applications,
restoration and failures use ChangeLog; duplicate samples create no entries. The UI uses
shared theme/DPI controls and all 15 languages.

## Validation

Run [BUILD.md](BUILD.md). Hardware-free tests cover math, floors, weights, limits, scheduling,
sensors, settings, localization and native UI navigation/rebuild/disposal in both themes.
An injected hardware adapter tests Auto-to-Advanced activation, both tables, exact baseline
and mode restoration, directionality, settings-change intervals, competing sessions,
concurrent suspension, Fan Boost, partial writes, read failure, failed restoration with Auto
fallback/retry, native mode changes, disable and disposal. A disk save/load test also covers enabled restart, every saved option,
clone/import, disabled restart and legacy defaults. Tests perform no physical EC writes.

This change has not been physically validated on MS-17S1 / `17S1IMS1.114` or any other laptop.
Hardware acceptance still requires CPU-only, GPU-only and combined loads with duty/RPM and
temperature recording, confirmation of table retention, profile/scene/Fan Boost/diagnostic
interactions, sleep/resume, sensor loss and disable/exit restoration. Equal requested
percentages do not establish equal RPM. Keep firmware protections enabled during measurements.

## Implementation

`Core/CrossCooling.cs` contains settings, math and scheduling.
`Core/CrossCoolingController.cs` owns lifecycle through `ICrossCoolingHardware`;
`Core/EcCrossCoolingHardware.cs` adapts existing EC calls. `TrayContext.cs` owns activation,
gates, sessions and workers. `UI/CrossCoolingPanel.cs` displays controls/status within
`UI/FanCurvePage.cs`. Tests are in `tests/Program.cs`.

| Modified files | Purpose |
| --- | --- |
| `Core/CrossCooling.cs`, `Core/CrossCoolingController.cs`, `Core/EcCrossCoolingHardware.cs` | Settings, math, scheduling, live ownership and existing EC adapter |
| `Core/AppSettings.cs`, `Core/Lang.cs` | Persistence, normalization, clone/import and 15-language UI |
| `TrayContext.cs`, `UI/MainDeps.cs`, `UI/MainForm.cs` | Tray lifecycle, EC sessions, status/actions and editor conflict detection |
| `UI/FanCurvePage.cs`, `UI/CrossCoolingPanel.cs` | New sub-tab, native settings, hardware status and visualization |
| `GhostDeck.csproj`, `.github/workflows/ci.yml`, `.github/workflows/release.yml` | Preserve .NET 11 migration, exclude test source, update SDK/output paths and run tests |
| `UI/Controls/Slider.cs`, `SegControl.cs`, `ToggleSwitch.cs`, `ThemedComboBox.cs`, `CheckItem.cs`, `HelpPopup.cs` | Runtime property annotations for the .NET 11 WinForms serialization analyzer |
| `UI/SubTabs.cs`, `UI/ReportPage.cs`, `UI/SettingsTrueColor.cs`, `UI/UpdatesPage.cs`, `UI/Dash/DashCanvas.cs` | Same build annotations |
| `Forms/GhostCardForm.cs`, `Forms/OsdForm.cs`, `Forms/ReportForm.cs` | Same build annotations |
| `tests/GhostDeck.Tests.csproj`, `tests/Program.cs` | Hardware-free math, controller and native UI tests |
| `tools/restricted-build.py` | Exact compiler-argument fallback and FolderProfile publish support |
| `docs/BUILD.md`, `docs/FAN-CURVE.md`, `docs/TECHNICAL.md`, `docs/CROSS-COOLING.md` | Build instructions, behavior, limits and physical acceptance procedure |

The existing Visual Studio `Properties/PublishProfiles/FolderProfile.pubxml` is used as supplied.
