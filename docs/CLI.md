# GhostDeck command-line interface

> Since v1.21. For the design/internals see [TECHNICAL.md §27](TECHNICAL.md#27-command-line-interface-v121).

Every core GhostDeck action is scriptable through plain command-line arguments on the same
`GhostDeck.exe` you already run. No extra binary, no config: `GhostDeck.exe --profile Silent` and
you're done. Output is **English-only by design** (machine-readable; scripts must not depend on
the UI language).

## Requirements

- **Administrator rights.** EC access needs elevation, exactly like the app itself (the manifest
  requests it, so an elevated shell / scheduled task with *highest privileges* is required;
  a non-elevated caller gets a UAC prompt or a failure in non-interactive contexts).
- Supported hardware for anything that writes to the EC. `--status`, `--refresh`,
  `--brightness`, `--hdr`, `--touchpad`, `--winlock` and `--diag` work on any machine (they
  are Windows-level or read-only); `--status` reports `"writable": false` there, and on
  monitoring-only boards (#48) it still returns temperatures with `"telemetry": true`.

## Execution model

| App state | What happens |
|---|---|
| **GhostDeck is running** (tray) | The command is forwarded over the local named pipe `GhostDeck_Cli` and executed **by the running instance** on its UI thread - identical code paths, safety gates (tier / experimental opt-in), OSD toasts and change-history entries as clicking the UI. |
| **GhostDeck is not running** | One-shot mode: the process loads `settings.json`, detects the device, applies the same gates, talks to the EC directly, logs to the shared change history, and exits. Nothing stays resident. |

The commands that strictly need the running app are `--overlay` (the overlay is a window of
that process), `--scene` (scenes orchestrate app state like the overlay and hotkeys),
`--winlock` (the keyboard hook lives in the running process) and the optional `--fanboost`
auto-off timer (something has to stay alive to fire it). The opposite special case is
`--diag`: it always runs locally in the calling process, so the zip lands in *your* current
directory and it works even when the app can't start.

## Commands

| Command | Effect | Success output (stdout) |
|---|---|---|
| `--profile <Silent\|Balanced\|Extreme\|SuperBattery>` | Apply the profile recipe (+ the assigned fan-curve preset, if any) | `profile set: Silent` |
| `--cycle` | Switch to the next profile in order | `profile set: <name>` |
| `--fanboost on\|off [seconds]` | Full fan speed on/off; `off` re-asserts the active profile's fan mode. The optional seconds (10-7200) arm a one-off auto-off timer for this activation (**timer requires the app running**) | `fan boost: on (auto-off in 120 s)` |
| `--curve "<preset>"` | Apply a saved fan-curve preset by name (case-insensitive). In Silent this switches to Balanced first (the Silent cap shares the fan byte) | `fan curve applied: <name>` |
| `--curve auto` | Back to stock fan behaviour for the active profile | `fan curve: stock` |
| `--scene "<name>"` | Apply a saved scene by name, case-insensitive (**requires the app running**) | `scene applied: <name>` |
| `--refresh <hz\|max>` | Panel refresh rate; `max` picks the highest mode the panel reports. Windows display API - works on any laptop | `refresh rate: 240 Hz` |
| `--charge <20-100\|off>` | Battery charge limit - any threshold from 20 to 100 % (60, 80 and 100 are the vendor-verified ones); `off` = stop managing (the EC keeps its current threshold, the app just stops re-asserting it) | `charge limit: 80 %` |
| `--travel <days\|off>` | Charge to 100 % for a trip; the previous limit returns automatically after 1-90 full days. `off` = end now and restore the previous limit. Any manual charge-limit change cancels the pending revert. The revert is applied by the running app (poll or next start); with no app running, the next one-shot CLI call catches it up | `travel mode: 100 % until 2026-08-19` |
| `--brightness <0-100>` | Internal-panel brightness (WMI, driver-free) - works on any laptop; external monitors are not covered | `brightness: 45` |
| `--hdr <on\|off>` | HDR / advanced color on every HDR-capable display (DisplayConfig API, any machine) | `hdr: on` |
| `--touchpad <on\|off>` | Enable/disable the precision touchpad at the device level (same operation as Device Manager; admin, any machine). The in-app hotkey and a panic reset always re-enable it | `touchpad: off` |
| `--kbd <off\|low\|mid\|high\|0-3>` | Keyboard-backlight level (models with the EC brightness register) | `keyboard backlight: high` |
| `--webcam on\|off` | EC-level webcam switch - same switch as the Fn camera key. Refused while the hard camera block (Settings → System → Privacy) is active | `webcam: off` |
| `--fnswap <left\|right>` | Which side the Fn key is on - the EC-persisted Fn/Windows swap (boards in msi-ec's `fn_win_swap` map) | `fn key: left` |
| `--winlock on\|off` | Block both Windows keys - software hook, any laptop (**requires the app running**) | `win key lock: on` |
| `--overlay on\|off` | Show/hide the gaming overlay (**requires the app running**) | `overlay: on` |
| `--panic` | Safe state: Fan Boost off, Balanced profile, fans on the automatic curve; also lifts the camera block, re-enables the webcam and releases the Windows-key lock | `panic reset done` |
| `--diag [path.zip]` | Save the one-zip diagnostic package (report, read-only EC dump or its exact failure, vendor WMI blocks, settings/changelog/errors). Always runs locally; default name `ghostdeck-diagnostics-<date>.zip` in the current directory | `diagnostics saved: <path>` |
| `--status` | Print the current state as JSON (see below) | *(JSON document)* |
| `--help` | Print usage | *(usage text)* |

## Exit codes

| Code | Meaning | Typical stderr/stdout message |
|---|---|---|
| `0` | Success | *(command-specific, above)* |
| `1` | Refused or failed | `unsupported hardware (firmware: …)` · `model is experimental - enable Experimental writes in the app settings first` · `preset not found: X` · `overlay control needs the GhostDeck app running` · `EC access failed (…) - run elevated (administrator) on supported hardware` |
| `2` | Bad usage (unknown command / missing argument) | usage text |

## `--status` JSON

```json
{
  "running": true,
  "model": "MSI Raider GE78HX 13V / 14V",
  "firmware": "17S1IMS1.114",
  "tier": "Tested",
  "writable": true,
  "telemetry": false,
  "profile": "Silent",
  "fanBoost": false,
  "overlay": true,
  "winLock": false,
  "cpuTemp": 52, "gpuTemp": 46,
  "cpuFan": 34,  "gpuFan": 0,
  "cpuRpm": 2450, "gpuRpm": 0,
  "refreshHz": 240,
  "chargeLimit": 80,
  "kbdLight": null,
  "webcam": true,
  "fnLeft": false,
  "hdr": false,
  "touchpad": true,
  "batteryPercent": 76, "batteryCharging": true,
  "batteryMinutesLeft": null, "batteryWearPct": 9,
  "disks": [ { "name": "Samsung MZVL21T0HCLR", "tempC": 41 }, { "name": "KINGSTON SKC3000", "tempC": 37 } ],
  "fps": 143, "frameTimeMs": 7.0, "game": "witcher3"
}
```

| Field | Type | Notes |
|---|---|---|
| `running` | bool | `true` = answered by the live tray instance over the pipe; `false` = one-shot probe |
| `model` | string | Detected model name, or `"unsupported"` |
| `firmware` | string | EC firmware string (empty if unreadable, e.g. not elevated) |
| `tier` | string | `Tested` / `Experimental` / `None` |
| `writable` | bool | Whether writes are allowed (tier + experimental opt-in) |
| `telemetry` | bool | `true` on monitoring-only boards (#48): temperatures come from the vendor WMI blocks, everything EC stays unavailable |
| `profile` | string? | Active profile; `null` when unknown/unsupported |
| `fanBoost`, `overlay`, `winLock` | bool | Only present when `running` is `true` |
| `cpuTemp`, `gpuTemp` | int | °C, `0` = unknown |
| `cpuFan`, `gpuFan` | int | fan duty %, `0` = unknown/stopped |
| `cpuRpm`, `gpuRpm` | int | real RPM, `0` = unknown or no tach registers on this model |
| `refreshHz` | int | current refresh rate of the built-in panel (primary display when no internal panel is active), `0` = unknown |
| `chargeLimit` | int | the app's configured charge limit; `0` = not managed |
| `kbdLight` | int? | backlight level 0-3; `null` = no EC brightness register on this model |
| `webcam` | bool? | EC camera switch; `null` = no control on this model |
| `fnLeft` | bool? | `true` = the Fn key is on the left; `null` = no `fn_win_swap` register mapped |
| `hdr` | bool? | HDR (advanced color) state; `null` = no HDR-capable display |
| `touchpad` | bool? | precision-touchpad devnode state; `null` = none found |
| `batteryPercent`, `batteryCharging` | int? / bool? | `null` on machines without a battery |
| `batteryMinutesLeft` | int? | Windows' runtime estimate; `null` on AC or when not reported |
| `batteryWearPct` | int? | design-vs-full-charge wear; `null` when the firmware doesn't report capacities |
| `disks` | array | `{ name, tempC }` per physical disk; `tempC` `null` when the drive doesn't report it. Empty when not elevated |
| `fps`, `frameTimeMs`, `game` | int? / float? / string? | foreground game via the ETW FPS monitor; `null` when the monitor is off (overlay hidden, Gaming tab closed) or no game is presenting. Only present when `running` is `true` |

## Recipes

**Task Scheduler - quiet nights.** Create two basic tasks running with *highest privileges*:
one at 22:00 → `GhostDeck.exe --profile Silent`, one at 07:00 → `GhostDeck.exe --profile Balanced`.

**Stream Deck.** Add a *System → Open* action with `GhostDeck.exe` and the arguments
(e.g. `--fanboost on`). One key per profile, one for `--panic`. (Stream Deck itself must run
elevated for the launched process to inherit elevation without a UAC prompt.)

**AutoHotkey.**
```ahk
^!F11::RunWait "C:\Tools\GhostDeck.exe --curve ""Night quiet""",, "Hide"
```

**PowerShell - log the state.**
```powershell
$s = GhostDeck.exe --status | ConvertFrom-Json
if ($s.cpuTemp -gt 90) { GhostDeck.exe --fanboost on }
```

**Game launcher wrapper.** Start Extreme before the game, return to Silent after:
```powershell
GhostDeck.exe --profile Extreme
Start-Process -Wait "game.exe"
GhostDeck.exe --profile Silent
```
