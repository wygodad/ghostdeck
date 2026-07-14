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
- Supported hardware for anything that writes (`--status` also works on unsupported machines and
  reports `"writable": false`).

## Execution model

| App state | What happens |
|---|---|
| **GhostDeck is running** (tray) | The command is forwarded over the local named pipe `GhostDeck_Cli` and executed **by the running instance** on its UI thread - identical code paths, safety gates (tier / experimental opt-in), OSD toasts and change-history entries as clicking the UI. |
| **GhostDeck is not running** | One-shot mode: the process loads `settings.json`, detects the device, applies the same gates, talks to the EC directly, logs to the shared change history, and exits. Nothing stays resident. |

The only command that strictly needs the running app is `--overlay` (the overlay is a window of
that process).

## Commands

| Command | Effect | Success output (stdout) |
|---|---|---|
| `--profile <Silent\|Balanced\|Extreme\|SuperBattery>` | Apply the profile recipe (+ the assigned fan-curve preset, if any) | `profile set: Silent` |
| `--cycle` | Switch to the next profile in order | `profile set: <name>` |
| `--fanboost on\|off` | Full fan speed on/off; `off` re-asserts the active profile's fan mode | `fan boost: on` |
| `--curve "<preset>"` | Apply a saved fan-curve preset by name (case-insensitive). In Silent this switches to Balanced first (the Silent cap shares the fan byte) | `fan curve applied: <name>` |
| `--curve auto` | Back to stock fan behaviour for the active profile | `fan curve: stock` |
| `--overlay on\|off` | Show/hide the gaming overlay (**requires the app running**) | `overlay: on` |
| `--panic` | Safe state: Fan Boost off, Balanced profile, fans on the automatic curve | `panic reset done` |
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
  "profile": "Silent",
  "fanBoost": false,
  "overlay": true,
  "cpuTemp": 52, "gpuTemp": 46,
  "cpuFan": 34,  "gpuFan": 0,
  "cpuRpm": 2450, "gpuRpm": 0
}
```

| Field | Type | Notes |
|---|---|---|
| `running` | bool | `true` = answered by the live tray instance over the pipe; `false` = one-shot probe |
| `model` | string | Detected model name, or `"unsupported"` |
| `firmware` | string | EC firmware string (empty if unreadable, e.g. not elevated) |
| `tier` | string | `Tested` / `Experimental` / `None` |
| `writable` | bool | Whether writes are allowed (tier + experimental opt-in) |
| `profile` | string? | Active profile; `null` when unknown/unsupported |
| `fanBoost`, `overlay` | bool | Only present when `running` is `true` |
| `cpuTemp`, `gpuTemp` | int | °C, `0` = unknown |
| `cpuFan`, `gpuFan` | int | fan duty %, `0` = unknown/stopped |
| `cpuRpm`, `gpuRpm` | int | real RPM, `0` = unknown or no tach registers on this model |

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
