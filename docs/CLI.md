# GhostDeck command-line interface

> Since v1.21. For the design/internals see [TECHNICAL.md §27](TECHNICAL.md#27-command-line-interface-v121).

Every core GhostDeck action is scriptable through plain command-line arguments on the same
`GhostDeck.exe` you already run. No extra binary, no config: `GhostDeck.exe --profile Silent` and
you're done. Output is **English-only by design** (machine-readable; scripts must not depend on
the UI language).

## Requirements

- **Administrator rights, but only where the EC is touched.** With the app running, the caller
  needs none: the command is handed to the running app, which already has them (since 1.38 the
  exe no longer demands elevation at launch; TECHNICAL.md §77). Without the app running, the
  one-shot talks to the EC itself: an unelevated caller gets one UAC prompt (and a failure in a
  non-interactive context), an elevated shell or a scheduled task with *highest privileges*
  runs straight through.
- Supported hardware for anything that writes to the EC. `--status`, `--refresh`,
  `--brightness`, `--hdr`, `--touchpad`, `--winlock` and `--diag` work on any machine (they
  are Windows-level or read-only); `--status` reports `"writable": false` there, and on
  monitoring-only boards (#48) it still returns temperatures with `"telemetry": true`.

## Execution model

| App state | What happens |
|---|---|
| **GhostDeck is running** (tray) | The command is forwarded over the local named pipe `GhostDeck_Cli` and executed **by the running instance** on its UI thread - identical code paths, safety gates (tier / experimental opt-in), OSD toasts and change-history entries as clicking the UI. |
| **GhostDeck is not running** | One-shot mode: the process loads `settings.json`, detects the device, applies the same gates, talks to the EC directly, logs to the shared change history, and exits. Nothing stays resident. |
| **GhostDeck is not running** and the command lives in the app (`--scene`, `--overlay`, `--winlock`, the `--fanboost` timer) | The one-shot starts the tray app and hands the command over once its pipe answers (up to 15 s); the app stays in the tray. The answer is prefixed `GhostDeck started; `. |

The commands that live in the running app are `--overlay` (the overlay is a window of that
process), `--scene` (scenes orchestrate app state like the overlay and hotkeys), `--winlock`
(the keyboard hook lives in the running process) and the optional `--fanboost` auto-off timer
(something has to stay alive to fire it) - with no app running they start it (third row). The
opposite special case is
`--diag`: it always runs locally in the calling process, so the zip lands in *your* current
directory and it works even when the app can't start.

## Commands

| Command | Effect | Success output (stdout) |
|---|---|---|
| `--profile <Silent\|Balanced\|Extreme\|SuperBattery>` | Apply the profile recipe (+ the assigned fan-curve preset, if any) | `profile set: Silent` |
| `--cycle` | Switch to the next profile, in the order set under *Profile order* (Standard unless changed) | `profile set: <name>` |
| `--fanboost on\|off [seconds]` | Full fan speed on/off; `off` re-asserts the active profile's fan mode. The optional seconds (10-7200) arm a one-off auto-off timer for this activation (**timer requires the app running**) | `fan boost: on (auto-off in 120 s)` |
| `--curve "<preset>"` | Apply a saved fan-curve preset by name (case-insensitive). In Silent this switches to Balanced first (the Silent cap shares the fan byte) | `fan curve applied: <name>` |
| `--curve auto` | Back to stock fan behaviour for the active profile | `fan curve: stock` |
| `--scene "<name>"` | Apply a saved scene by name, case-insensitive (**requires the app running**) | `scene applied: <name>` |
| `--refresh <hz\|max>` | Panel refresh rate; `max` picks the highest mode the panel reports. Windows display API - works on any laptop | `refresh rate: 240 Hz` |
| `--charge <20-100\|off>` | Battery charge limit - any threshold from 20 to 100 % (60, 80 and 100 are the vendor-verified ones); `off` = stop managing (the EC keeps its current threshold, the app just stops re-asserting it) | `charge limit: 80 %` |
| `--travel <days\|off>` | Charge to 100 % for a trip; the previous limit returns automatically after 1-90 full days. `off` = end now and restore the previous limit. Any manual charge-limit change cancels the pending revert. The revert is applied by the running app (poll or next start); with no app running, the next one-shot CLI call catches it up | `travel mode: 100 % until 2026-08-19` |
| `--turbo <on\|off\|status>` | CPU turbo boost through the active Windows power plan (*Processor performance boost mode*, both power sources). `off` saves the plan's previous values and writes "disabled"; `on` restores them (or an enabled mode when nothing was saved); `status` only reads. Documented Windows API, no EC involved - works on any laptop. The mode names in the output come from Windows, in its own language | `turbo boost: off (AC <mode>, battery <mode>)` |
| `--brightness <0-100>` | Internal-panel brightness (WMI, driver-free) - works on any laptop; external monitors are not covered | `brightness: 45` |
| `--hdr <on\|off>` | HDR / advanced color on every HDR-capable display (DisplayConfig API, any machine) | `hdr: on` |
| `--touchpad <on\|off>` | Enable/disable the precision touchpad at the device level (same operation as Device Manager; admin, any machine). The in-app hotkey and a panic reset always re-enable it | `touchpad: off` |
| `--mic <on\|off>` | Unmute / mute the default Windows recording device (the mute flag in the Windows sound settings; when calls use a different default device, both are switched). Works on any laptop, no EC involved | `microphone: off` |
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
| `1` | Refused or failed | `unsupported hardware (firmware: …)` · `model is experimental - enable Experimental writes in the app settings first` · `preset not found: X` · `could not start the GhostDeck app` · `EC access failed (…) - run elevated (administrator) on supported hardware` |
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

## `ghostdeck://` links

Every state-changing command is also a link: `ghostdeck://<command>/<argument>[/<second>]`
maps one to one onto the switches above, with the same gates, OSD toasts and history entries.

| Link | Same as |
|---|---|
| `ghostdeck://profile/silent` | `--profile Silent` |
| `ghostdeck://cycle` | `--cycle` |
| `ghostdeck://scene/Gaming%20night` | `--scene "Gaming night"` (spaces as `%20`) |
| `ghostdeck://fanboost/on/300` | `--fanboost on 300` |
| `ghostdeck://curve/My%20quiet` | `--curve "My quiet"` |
| `ghostdeck://refresh/max` | `--refresh max` |
| `ghostdeck://charge/80`, `ghostdeck://travel/7` | `--charge 80`, `--travel 7` |
| `ghostdeck://turbo/off`, `ghostdeck://mic/off`, `ghostdeck://hdr/on`, `ghostdeck://kbd/high`, ... | the matching switch |
| `ghostdeck://panic` | `--panic` |

Not reachable as links, on purpose: `--status` and `--help` (nothing to print into), `--diag`
and the maintainer commands (a web page must not be able to write a file on your disk).

The link is registered for the current Windows user (`HKCU\Software\Classes\ghostdeck`) when
the app starts, no administrator needed, and re-pointed when the exe moves. Settings → System →
*ghostdeck:// links* switches the registration off and removes the key. A browser asks once
whether to open GhostDeck, as for any such link; the Run dialog (Win+R), a shortcut, AutoHotkey
(`Run ghostdeck://scene/Gaming`) and a Stream Deck *Open* or *Website* action launch it
directly. With the app running the link runs without any prompt and answers with the usual
OSD; a refusal (an unknown scene, say) shows as a GhostDeck card with the message, because a
link has no console to answer into. Without the app running the link behaves like the CLI: one UAC prompt,
and a scene starts the app.

## Taskbar jump list

A right-click on the GhostDeck taskbar button opens a jump list: the profiles in the order you
set, every scene, and Fan Boost on / off plus the panic reset as tasks. Each entry is the
matching CLI command, carried out by the running app. The button exists while the main window
is open; pin GhostDeck to the taskbar (right-click the button → *Pin to taskbar*) and the list
is there at any time, also through Win+Alt+<position of the button>. A pinned GhostDeck takes
the icon style chosen in Settings (*Application icon*).

## Recipes

**Task Scheduler - quiet nights.** Create two basic tasks running with *highest privileges*:
one at 22:00 → `GhostDeck.exe --profile Silent`, one at 07:00 → `GhostDeck.exe --profile Balanced`.

**Stream Deck.** Add a *System → Open* action with `GhostDeck.exe` and the arguments
(e.g. `--fanboost on`). One key per profile, one for `--panic`. With the app running no
elevation is involved; a *Website* action with `ghostdeck://profile/silent` works just as well
and needs no path to the exe.

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
