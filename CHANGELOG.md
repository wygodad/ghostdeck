# Changelog

All notable changes to this project are documented here.
Format loosely based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]
### Added
- **Settings export / import** (Settings → Backup): save all preferences (colours, hotkeys,
  rules, overlay, alerts) to a JSON file and restore them after a reinstall or on another
  machine. Machine-specific state (firmware guard, window position) is kept local.
- **Temperature alert** (Settings → Notifications, off by default): an OSD toast and a tray
  balloon when the CPU or GPU stays above a chosen threshold (80-100 °C) for a chosen time
  (5-60 s), with a 5-minute cool-down between alerts and an entry in the change history.
- **Panic reset hotkey** (default Ctrl+Alt+F10): one press returns the machine to a safe
  stock state - Fan Boost off, Balanced profile, fans back on the automatic curve.

## [1.19.0] - 2026-07-13
### Added
- **Fifth application-icon style**: the dark-style cyan ghost on the light tile
  (Settings → Application icon).
- **"Start over" buttons in both report wizards** - a capture run in the wrong MSI Center
  state can now be repeated without restarting the app ([#9](../../discussions/9)).
### Changed
- **Scenario tiles restyled** like the ghostdeck.dev cards: a soft inner glow on the active
  profile with an outlined ACTIVE chip, a subtle SELECT hint on the rest.
- **Uniform feature bricks.** Charge limit and AC/battery auto-switch moved into the same
  style of boxes as Fan Boost and Gaming overlay - one consistent block per feature.
- **Header layout**: the GhostDeck wordmark moved to the left of the tabs, and the
  tested/experimental badge now sits in the header next to the version (instead of on
  each page).
- **Subtle background grid** on every tab, matching the ghostdeck.dev page texture.
- **New "Interface" settings section**: the background grid can be turned off, and every main
  tab can individually be moved out of the tab row into an icon-only button on the right of
  the header (e.g. keep Models one click away without it occupying the tab row).
- **Status tables** now use the change-log colour language (muted labels, accent keys,
  per-fan colours in the live curve table), and the full-log window colours its source column.
- **Charts row justified**: the five gauges and the metric boxes under them spread across the
  full content width instead of hugging the left edge.
### Fixed
- **Tabs are clickable across their full height**, not only on the text/icon line.
- **First-show flashes and sluggish tabs eliminated at the source.** The one-time white flash
  per tab was the native handle-creation storm; the window is now pre-built hidden shortly
  after startup (all pages and their controls created off-screen), and closing the window
  hides it instead of destroying it - so reopening and every tab switch is instant.

## [1.18.1] - 2026-07-13
### Added
- **Application icon choice** (Settings → Appearance): GhostDeck logo, ghost on a dark rounded
  tile (new default - crisper in the taskbar and title bar, with rounded corners), ghost on a
  light tile, or the classic pre-1.18 gauge. The tray icon keeps following the active profile
  colour in every style ([#9](../../discussions/9)).
- **"Send feedback" can be hidden** from the tray menu (Settings → Tray menu)
  ([#9](../../discussions/9)).
### Fixed
- **Autostart survives moving the exe.** The scheduled task stores the exe path from the moment
  autostart was enabled, so moving `GhostDeck.exe` (e.g. into Program Files) silently broke
  autostart; the app now re-points the task at its current location on every start
  ([#9](../../discussions/9)).
- **Clicking overlay checkboxes no longer lights up the Position dropdown** - the combo showed a
  solid selection highlight when it merely received focus ([#9](../../discussions/9)).
- **Overlay "Limit" metric now shows OFF** when the battery-charge limit is not managed by the
  app, instead of keeping the last percentage ([#9](../../discussions/9)).
- **Shortcut capture boxes are theme-aware** - they had a glaring white system border in dark
  mode; now they use the theme border (accent when capturing) ([#9](../../discussions/9)).
### Changed
- **Status tables restyled.** The EC profile-byte matrix now uses a cleaner per-profile row wash
  with a coloured left edge (and a cyan edge on the active profile); the byte legend, live
  fan-curve table, change-log and the Charts detail card now use alternating row shading for
  easier reading.
- **Update download bar.** During an in-app update the progress now shows as a "Downloading… X%"
  label above a rounded progress bar (the Install/Check buttons hide while it runs).
- **Fan-curve charts** now draw a translucent gradient fill under the curve and a vertical
  guide line at every node, matching the ghostdeck.dev look.

## [1.18.0] - 2026-07-13
### Added
- **In-app updates.** The Updates tab can now download and install a new release directly: an
  *Install vX.Y.Z* button appears when a newer version is found, with a progress bar; the app
  restarts itself on the new version (the previous exe is kept as `.bak` and cleaned up on the
  next start). Falls back to opening the releases page if the download fails
  ([#9](../../discussions/9)).
- **"Restore default colors"** button under the profile colours in Settings.
- **Brand icon set.** New ghost logo everywhere: application/taskbar/window icon, tray icon
  (ghost on the profile-coloured tile), and a GhostDeck wordmark in the header. Icon vector
  sources live in `assets/icons/*.svg`.
### Changed
- **Full ghostdeck.dev visual refresh.** The dark theme now matches the website palette
  (near-black background, neon-cyan accents for indicators, blue fills for buttons/toggles/
  sliders so white text stays readable); the light theme accent moved from purple to blue.
  Dark mode is now the default for new installs.
- **New scenario icons** (feather / scales / bolt / battery) and new default profile colours
  (blue / amber / pink / green).
- **Status gauges** redrawn as segmented tick rings with a colour gradient; GPU rings use the
  violet data colour.
- **Verified/experimental/unsupported badges** restyled as outlined chips matching the site.
- **Change history window** restyled: themed alternating rows instead of the white-on-black
  grid, readable buttons.
### Fixed
- **Sluggish tab switching.** Status and Fan curve did dozens of synchronous EC/WMI reads on
  the UI thread when entering the tab (and periodically); reads now run in the background and
  the page paints instantly from the last snapshot.
- **Settings page delays.** The page is pre-built shortly after startup, and a language change
  repaints in one go instead of blanking for a second.
- **Scrolling Settings past a dropdown** no longer stops the page scroll or changes the value
  (hovering the language combo while scrolling used to switch languages)
  ([#9](../../discussions/9)).
- **Settings tab icon no longer clipped** at some DPI scales ([#9](../../discussions/9)).
- **Launching the exe while GhostDeck is already running** now brings up the main window of the
  running instance instead of doing nothing.

## [1.17.1] - 2026-07-12
### Fixed
- **Dark-theme dropdowns.** The Language, On AC / On battery and overlay-position selects kept a white
  field, drop button and list in dark mode (and flashed light on hover/click). They now use a fully
  theme-aware combo that owns its painting, so they match the theme with no flicker
  ([#9](../../discussions/9)).

## [1.17.0] - 2026-07-12
### Added
- **Gaming overlay: "Bold text" option** (Settings → Gaming overlay → Options, on by default). Renders the
  small metric labels bold and slightly larger so they stay readable when the overlay is scaled down. In
  the horizontal bar layout the bar height is unchanged whether the option is on or off
  ([#10](../../discussions/10)).
- **Keyboard shortcuts can be enabled/disabled** (Settings → Keyboard shortcuts): a toggle next to each
  shortcut plus a master "All shortcuts" switch, so accidental triggers can be turned off. Defaults on
  ([#9](../../discussions/9)).
- **Status: VRAM as a bar under RAM.** When the total dedicated VRAM is known, VRAM is shown as a progress
  bar beneath the RAM bar (instead of a used-MB tile) ([#9](../../discussions/9)).
- **Tray menu entries can be hidden** (Settings → Tray menu): toggles for *Status*, *Fan curve*, *Models*,
  *Report/verify* and *Change log*, so the context menu can be trimmed. Defaults on ([#9](../../discussions/9)).
### Changed
- **Settings layout tidied** ([#9](../../discussions/9)): the *Updates* card moved to the bottom of the
  left column so it lines up with *Power* and *Keyboard shortcuts*; profile colours now sit on a single row.
- **Tray menu order** now mirrors the main tabs — *Settings* comes right after *Fan curve*, and *Change log*
  moved below *Language*. The *Fan Boost*, *Gaming overlay* and *Lock overlay* items now show a dim marker
  when off (instead of nothing) so their state is always visible ([#9](../../discussions/9)).
### Fixed
- **Settings tab no longer leaves a large empty gap.** Scrolling down, resizing the window width, then
  scrolling back up used to open a big blank area, because the page positioned its cards at absolute
  coordinates inside a scrolled `AutoScroll` panel. Children are now placed relative to the scroll
  position ([#10](../../discussions/10)).

## [1.16.6] - 2026-07-12
### Fixed
- **Fan-curve wizard now explains *why* a capture found nothing.** If the test curve isn't in the EC, the
  app checks the live fan mode: when the laptop isn't in the Advanced-curve state (e.g. it's in Silent, so
  the EC still holds the default curve), it now says exactly that — "switch to Extreme, set the Advanced
  curve, Save, stay in Extreme, then capture again" — instead of the vague "couldn't locate the test curve".
- **"Capture & scan" no longer renders as "Capture _scan".** The literal `&` in the step text and button
  was being treated as a WinForms mnemonic; fixed with `NoPrefix` / `UseMnemonic = false`.

## [1.16.5] - 2026-07-12
### Fixed
- **Report wizards no longer pre-fill the "paste your dump here" field.** The app used to seed that field
  (via the GitHub form URL) with a "paste it here" placeholder; reopening or reloading the prefilled link
  restored the placeholder and wiped what the user had pasted, so dumps sometimes arrived empty
  ([#12](../../issues/12), [#16](../../issues/16)). The field is now left empty — the full report is on the
  clipboard and saved to a .txt — so Ctrl+V just works. All other fields stay prefilled. The curve wizard
  also now shows where the report was saved, and the model form's help text explains the paste step.

## [1.16.4] - 2026-07-12
### Changed
- **MSI Raider GE67 HX 12U: fan curve verified** ([issue #16](../../issues/16)). The owner ran the fan-curve
  wizard and it reported the test curve at the shipped `0x72` (CPU) / `0x8A` (GPU) addresses, so the curve
  is now marked editable/verified for this model.

## [1.16.3] - 2026-07-12
### Changed
- **MSI Crosshair A16 HX (D7W/D8W): fan curve verified** ([issue #11](../../issues/11)). The owner ran the
  fan-curve wizard and it found the test curve at exactly `0x72` (CPU) / `0x8A` (GPU) — the shipped
  addresses — so the curve is now marked editable/verified for this model.
- **MSI Raider GE67 HX 12U (`1545IMS1`) promoted from Experimental to Tested** ([issue #14](../../issues/14)).
  The owner's per-scenario snapshot matches the shipped recipe 1:1 and they hardware-confirmed all three
  checks (Silent lowers power/noise vs Balanced, Extreme unlocks, switching stable).

## [1.16.2] - 2026-07-05
### Changed
- **MSI Sword 16 HX B13V / B14V: fan curve verified** ([issue #8](../../issues/8)). Using the new
  fan-curve wizard, the owner set a known test curve in MSI Center and GhostDeck found those exact values
  at `0x72` (CPU) / `0x8A` (GPU) — the shipped `ModernCurve` addresses — so the curve is now marked
  editable/verified for this model instead of "unverified". First model verified end-to-end via the wizard.

## [1.16.1] - 2026-07-05
### Fixed
- **MSI Sword 16 HX B13V / B14V (`15P2EMS1`): CPU fan RPM now shows** instead of "—". The fan-tachometer
  registers were not mapped for this model; the owner's per-scenario dump ([issue #6](../../issues/6))
  shows plausible, load-varying values at `0xC9` (the same address the tested GE78HX G2 board uses), so
  RPM is now read from `0xC9`/`0xCB`. GPU RPM reads 0 when the dGPU fan is idle. Reported in
  [issue #7](../../issues/7); owner to confirm the value against HWiNFO.

## [1.16.0] - 2026-07-05
### Added
- **Sub-tabs** — a reusable segmented control (`SubTabs`) that splits a page into shorter sub-pages.
  - **Status** is now three sub-tabs: **Charts** (rings, RAM, metric boxes, details), **EC bytes**
    (profile-byte matrix + legend + live curve tables) and **Change log** — instead of one long scroll.
  - **Report** is two sub-tabs: **Profiles** and **Fan curve**.
- **Fan-curve verification wizard** (Report → Fan curve). Guides you through setting a distinctive test
  curve in MSI Center (Extreme → Advanced), reads the EC back (read-only), and **locates the curve
  tables by scanning the dump** for the test values — discovering the per-model addresses, not just
  confirming them. If they match the shipped map the model's curve can be marked verified; either way it
  opens a pre-filled GitHub report (new `curve-support.yml` template).
- **Report entry points** — "Verify my model" CTA on the Models tab and "Report fan curve" on the Fan
  curve tab (deep-link to the right Report sub-tab); the tray groups both under "Report / verify".
### Changed
- **Report and Updates moved out of the main tab strip** to icon buttons on the right (`⚑` report,
  `⟳` updates, next to the theme toggle), freeing room in the strip.
- **Status → Change log** now shows the last 20 entries (was 6).
- Sub-tab bar restyled: softer (less rounded) corners and more breathing room above/below.
- All new UI strings localized into all 8 languages.

## [1.15.3] - 2026-07-05
### Changed
- **MSI Sword 16 HX B13V / B14V (`15P2EMS1`) promoted from Experimental to Tested** — owner-confirmed in
  [issue #6](../../issues/6): under Cinebench 2026, HWiNFO64 shows each profile hitting its intended CPU
  package-power limit, and the fan curve behaves on par with MSI Center. Their per-scenario EC dump also
  matched the shipped recipe 1:1 (`0xD2` C1/C1/C4/C2, `0xD4` 1D/0D/0D/0D, super-batt `0xEB` only in Super
  Battery). The owner is switching over from MSI Center for profile control.
### Fixed
- CI workflow actions bumped to Node 24 releases (checkout v5, setup-dotnet v5, action-gh-release v3) to
  clear the GitHub-hosted-runner Node 20 deprecation warning.

## [1.15.2] - 2026-07-04
### Changed
- **MSI Crosshair A16 HX (D7W/D8W, `15PLIMS1`) promoted from Experimental to Tested** — owner-confirmed
  in [issue #5](../../issues/5): HWiNFO64 shows Silent measurably lowering CPU package power/clocks vs
  Balanced (~38 W avg / 54.5 W peak on Balanced vs a tight ~35.7-35.8 W band on Silent). Note: Silent and
  Super Battery read identically on this unit — unlike the Intel reference board, ECO shift (`0xC2`)
  doesn't cap further than Comfort + silent fan (`0xC1`/`0x1D`) here, so no super-battery register is used
  (`Recipes` passes `null` for it).
### Docs
- **Model-support request now has an (optional) hardware-verification section** — the submitter can attest,
  before sending, that they switched profiles in the app and confirmed Silent lowers power/fan vs Balanced,
  Extreme unlocks, and switching is stable. This is exactly what we need to promote a model from Experimental
  to Tested, so recognised-model owners can get verified in one round instead of a follow-up ask.

## [1.15.1] - 2026-07-04
### Changed
- **Repository renamed to `wygodad/ghostdeck`** (following the app rename). Updated the hard-coded
  URLs in-app so update checks, the announcements feed and "Send feedback" point at the new repo
  (`Updater`, `Notices`, feedback Discussion, Report links, `announcements.json`). GitHub redirects
  the old `msi-profile-switcher` URLs, so older releases keep working.
### Fixed
- **Settings tab scrolling** — the title is now a child label (was hand-painted with the scroll
  offset while the cards scrolled natively), so the page scrolls natively with no flicker or the
  phantom gap that opened above the first group.
- **Status tab scrolling** — rendered into a DPI-aware `BufferedGraphics` (allocated from the
  control's own `Graphics`) and blitted on scroll, so it's smooth **and** the text stays sharp at
  high DPI (150 % etc.) instead of the blurry/"doubled" text a plain 96-DPI bitmap produced.
### Docs
- New [docs/RENDERING.md](docs/RENDERING.md): how the Status tab and gaming overlay are rendered
  (DPI-aware buffered canvas vs. per-pixel layered window), a plain-language overview, how the other
  tabs draw, and do/don't rules. Linked from TECHNICAL.

## [1.15.0] - 2026-07-04
### Changed
- **Renamed the app to “GhostDeck”** (tagline: *for MSI laptops*) — to keep the project clearly
  independent of MSI and avoid using MSI trademarks in the product name. The download is now
  **`GhostDeck.exe`**, the window/tray/title say GhostDeck, and the settings folder moved to
  `%AppData%\GhostDeck`.
- **Automatic migration on first launch:** existing settings and change-log (`settings.json`,
  `changelog.json`) are copied from the old `%AppData%\MSIProfileSwitcher` folder (the old folder is
  left as a backup), and the autostart task is renamed from `MSIProfileSwitcher` to `GhostDeck`
  (pointing at the new exe). Nothing for the user to do; after updating you can delete the old
  `MSIProfileSwitcher.exe`.
- The `MSI` name now appears only descriptively (“for MSI laptops”), never as the product brand.

## [1.14.1] - 2026-07-04
### Fixed
- **Announcements no longer nag.** A notice is shown once: an in-window banner when the panel is open
  (which marks it read) or a tray balloon when it's closed — never both at once. The manual
  "Check now" button now also refreshes announcements but **respects the read state**, so an
  already-read notice doesn't pop up again.

## [1.14.0] - 2026-07-04
### Added
- **In-app announcements channel** — the app now fetches a static `announcements.json` from the repo
  (same daily cadence and opt-out as the update check, read-only, no identifiers sent) and shows unseen
  notices as a tray balloon **and** a dismissible banner at the top of the window. Seen notices are
  remembered (`SeenNoticeIds`). First use: a heads-up about the upcoming rename to **GhostDeck**.
- **"Send feedback…" tray entry** — opens a prefilled GitHub Discussion in the browser (model reports
  still go to Issues via the Report wizard). No data is collected by the app.

### Changed
- **Renamed the "Cooler Boost" feature to "Fan Boost"** in the UI (tray, Scenarios brick, OSD, overlay,
  hotkey label, all 8 languages) to avoid using MSI's *Cooler Boost* trademark as our own feature name.
  Behaviour and the EC bit (`0x98` bit 7) are unchanged; the README keeps one descriptive reference
  ("equivalent of MSI's Cooler Boost"). Tightened the trademark/affiliation disclaimer ("not affiliated,
  endorsed, sponsored or supported by MSI").

## [1.13.0] - 2026-07-03
### Added
- **Per-pixel overlay rendering** (`UpdateLayeredWindow`, 32-bpp premultiplied ARGB) replacing the old
  uniform window opacity + chroma-key. Enables **independent background vs content opacity** (two
  sliders, each with preset chips), **smooth anti-aliased** text/icons on any game background, a
  **readability drop-shadow**, perfect rounded corners and natural click-through. See TECHNICAL §20.4.
- **Background opacity** control (`0/40/70/100 %` chips + free-drag slider), independent of content.
- **Battery %, GPU load %, VRAM and approx. CPU clock** now shown in the **Status** tab as compact
  counters (CPU clock next to the fan-RPM counters; battery/GPU%/VRAM in a matching row below).
### Changed
- Overlay **drag handle**: while unlocked the panel now forces a visible, grabbable surface (even with
  the background off) plus a stronger accent frame and a 3×3 dot grip, so it's easy to find and move.
  Locking restores the configured background and click-through.
- Overlay frame is hidden when the background is off and the panel is locked (clean text-only HUD).
### Fixed
- **"Show overlay"** state is synced between the Scenarios brick and the Settings toggle.

## [1.12.0] - 2026-07-02
### Added
- **Gaming-overlay extra metrics** read driver-free (no kernel driver, anti-cheat-safe): **GPU load %** and **VRAM used** via Windows PDH GPU counters, an **approximate CPU clock** (`% Processor Performance` × base MHz), and **current battery %** — all via `PdhAddEnglishCounter` so they resolve on localized (Polish) Windows, showing `—` on failure. Also surfaced in the **Status** tab. See TECHNICAL §20 for the full options/pros-cons analysis (why not WinRing0/LibreHardwareMonitor, and the FPS/frametime routes).
- **Overlay settings redesign** (Settings → Gaming overlay): full-width DPI-aware card — metric checkbox grid, **preset chips *and* a free-drag slider** for opacity & size, **background on/off + colour picker**, corner position, **Restore defaults** button; options are toggle switches.
- **Icons** on the Settings section headers.
### Changed
- Overlay show/hide and lock default hotkeys are now **`Ctrl+Shift+O`** / **`Ctrl+Shift+L`** (auto-migrated from earlier dev defaults).
### Fixed
- **Lock / click-through now actually locks**: window opacity capped at 0.99 so `WS_EX_LAYERED` stays on (needed for `WS_EX_TRANSPARENT`) + a hard drag guard. Previously at 100 % opacity the panel still caught the mouse.
- **Settings no longer jump-scroll to the top** when toggling an option (`ScrollToControl` override).
- Overlay settings + the Cooler Boost brick are DPI-correct at 125 %/150 % (no clipped labels/overlap).

## [1.10.0] - 2026-07-02
### Added
- **Cooler Boost (max fans)** — force both fans to full speed independent of the profile, for a render
  or a long game. New checkable tray item, a global hotkey (default **Ctrl+Alt+F5**), an OSD toast and a
  compact toggle **"brick"** on the Scenarios tab (with a hover-tooltip help "?"). One EC bit (`0x98`
  bit 7, the msi-ec `cooler_boost` address, matching MSI Center); read-modify-write, fully reversible,
  kept in sync by the background poll. **Hardware-confirmed on `17S1IMS1`** (GE78HX 13V) against MSI's
  Fn+↑ toggle (`0x98`: `02`↔`82`); the CPU fan spins down gradually (~10–25 s) after off, as the tooltip
  notes. See TECHNICAL §17.7.
- **Change-history log** — a rolling record of the last profile / EC changes: time, **source** (hotkey,
  tray, panel, auto AC/battery, fan curve, external sync, charge limit, cooler boost, firmware), the
  **written bytes** and a **readback** of those addresses. Shown compactly on the **Status** tab with a
  **"Full log…"** button that opens a dedicated window (copy / clear, live refresh); also reachable from
  the tray ("Change log"). Persisted to `changelog.json` so it survives a restart and can be attached to
  a model-support report. The readback is informational only (bytes are dynamic — see TECHNICAL §19.4/§19.7).
- **Firmware-change guard** — the app remembers the last-seen EC firmware; if it differs on the next
  start it **pauses automatic writes** (charge-limit-on-start, AC/battery auto-switch) and shows
  *"EC firmware changed, verify model again"* with a red tray item to acknowledge. Manual switching stays
  enabled. See TECHNICAL §19.8.
### Docs
- README: new **Comparison with MSI software** table (vs MSI Center 2.0); Features updated with Cooler
  Boost, the change-history log and the firmware guard.
- TECHNICAL (EN/PL) §17.7: Cooler Boost marked hardware-confirmed with the diagnostic (Fn+↑ vs the
  `0x98`/bit 7 snapshot) and the gradual fan-down note; feature-brick UI documented.

## [1.9.1] - 2026-07-01
### Changed
- Hidden test tools now route every EC write through the central write gate, so `MSIPS_FORCE_FIRMWARE` (simulate mode) also blocks them (no writes reach the real EC while pretending to be another model).
- Legacy PowerShell scripts synced to the current recipe (`0x34 = 00` only in Extreme) and clearly marked GE78HX-only / not the backend.
### Docs
- New TECHNICAL section "Design decisions and rationale" (EN/PL) documenting the settled facts for future reviewers: `0x34` is dynamic and inferred, the fan curve is intentionally writable on unverified models, Silent/Balanced detection uses `0xD4` only, no write-readback by design, and `17S2IMS2` (GE78 HX 14V) is owner-confirmed Tested.
- Marked the historical `0x34` measurements as point-in-time snapshots; corrected the cheat sheet.

## [1.9.0] - 2026-07-01
### Added
- **New "Models" tab** (also in the tray menu): a live, **searchable** table of every recognised firmware ID (~135), rendered straight from `Devices.cs` so it never drifts from the code. Columns: model, EC firmware, family (G1/G2), status (tested/experimental), fan-curve mode, super-battery (with an info tooltip), and fan-RPM support. The machine's **detected model is highlighted** at the top; the search box filters by model name or firmware.
- **`docs/SUPPORTED_MODELS.md`** — full per-firmware list of all recognised models, linked from the README and from TECHNICAL (EN/PL).
### Changed
- Fan-curve column now labels the experimental state as **"unverified"** (editable after opting into Experimental, but the table addresses are unconfirmed on that exact model — compare with MSI Center first) instead of the misleading "preview"; the app in fact lets you write the curve once Experimental is enabled.
- Shortened the **"Report"** tab label to free up space in the header bar.

## [1.8.4] - 2026-06-30
### Added
- **MSI Crosshair A16 HX (D7W/D8W)** now reads fan RPM (`0xC9`/`0xCB`), confirmed by full per-scenario EC dumps (issues #3/#4) which also validated its profile bytes and fan-curve tables.
### Docs
- TECHNICAL (EN/PL): note that the purpose of `0x34` is empirically inferred ("Extreme power unlock"), not officially documented.

## [1.8.3] - 2026-06-30
### Added
- **MSI Raider GE78 HX 14V** (`17S2IMS2`) — same board and EC layout as the tested 13V (dump-confirmed: identical profile bytes, fan-curve tables and RPM registers), so it shares the verified profile.

## [1.8.2] - 2026-06-29
### Changed
- Fan curve enable is now a **toggle switch** (consistent with the rest of the app), with a separate label.
### Fixed
- Fan duty readings are clamped to 100% (the raw PWM byte could read slightly above 100, e.g. "103%").

## [1.8.1] - 2026-06-29
### Fixed
- **Profile detection** now relies solely on the fan byte `0xD4` (`1D` = Silent). Diffing full EC dumps of all four MSI Center 2.0.48 scenarios proved `0x34` is the **Extreme power-unlock** flag (`00` only in Extreme), not a Silent/Balanced marker — so it no longer affects detection.
- **`0x34` in the profile recipes** corrected to match MSI exactly (`00` in Extreme, `01` elsewhere; previously reversed), so Extreme actually unlocks full power.
### Changed
- **Fan curve** is repositioned as manual fan control. On Balanced / Extreme / Super Battery it only changes the fans (lossless). On Silent it must leave Silent — because Silent's power cap lives in the *same byte* (`0xD4`) as the curve — so the app warns and switches to Balanced.
- Status tab and TECHNICAL docs (PL/EN) updated to describe `0x34` correctly and the `0xD4` Silent-cap/curve overlap.

## [1.8.0] - 2026-06-29
### Added
- **Bulk model import** (~126 new MSI laptops, all **Experimental** / opt-in) generated from the
  [msi-ec](https://github.com/BeardOverflow/msi-ec) register maps and cross-checked against
  [MControlCenter](https://github.com/dmitry-s93/MControlCenter): the full **G2 family** (Raider /
  Vector / Titan / Stealth 16-18 / Sword / Pulse / Crosshair / Katana / Cyborg / Bravo / Modern /
  Prestige / Summit) on shift `0xD2` / fan `0xD4` / super-batt `0xEB`, and the **G1 family** (older
  GS / GF / GE / GP, Modern, Alpha, Bravo, Delta, Creator) on shift `0xF2` / fan `0xF4` / charge `0xEF`.
- **Read-only fan-curve preview on the whole G2 family**: the curve tables use the fixed modern layout
  (CPU `0x6A`/`0x72`, GPU `0x82`/`0x8A`) that MControlCenter reads/writes across this family, so the
  addresses are practice-confirmed rather than guessed. The preview stays unverified
  (`FanCurveSpec.Verified=false`) until the user compares it with MSI Center on their own model; G1
  models get profiles only (their EC layout differs and the curve addresses are not confirmed).
- Models whose msi-ec config documents **no Silent fan value** (some GF75 Thin, GP65/GL65 & GP75/GL75
  Leopard, GS75 Stealth, GE63, GT72) were intentionally left out — Silent is this app's core function
  and writing an unconfirmed value would be a guess.
### Fixed
- **GS66 Stealth (`16V1EMS1`)** was using G2 EC registers (`0xD2`/`0xD4`) on what is actually a G1
  board; corrected to `0xF2`/`0xF4` per the msi-ec `CONF_G1_3` map.

## [1.7.0] - 2026-06-29
### Added
- **Fan curve editor** (new tab + tray entry): drag CPU (Fan 1) and GPU (Fan 2) speed points; a single **Custom fan curve** checkbox writes the curve as an Advanced fan overlay on the *current* power mode (e.g. a custom curve in Silent, which MSI Center does not allow), with an **MSI default** preset and a live **fan-mode** indicator. Unchecking hands the fans back to the active profile.
- **Read-only fan-curve preview** for the modern experimental models (GE68HX 13V, GS66 Stealth, Katana GF66/GF76, GE66 Raider / GP66 Leopard, Crosshair A16 HX) — confirmed against community EC dumps; editing is gated by the Experimental opt-in.
- **Status** tab expanded: a live **profile-byte matrix** (`0xD2`/`0x34`/`0xEB`/`0xD4`) with the active profile highlighted in its colour and a **Now (live)** row, a **byte legend** with value descriptions, and **live fan-curve tables**. Fan counters now labelled **CPU:** / **GPU:** RPM.
### Fixed
- **Profile no longer flips to Balanced** when a custom fan curve is active: profile detection is decoupled from the fan byte (the poll keeps the chosen profile while the fan runs in Advanced mode). See `docs/TECHNICAL.md` §17.
- **Smooth scrolling** on the Status page — the content is now painted on an inner canvas that WinForms scrolls natively, removing the ghosting/smearing during scroll and resize.

## [1.5.1] - 2026-06-29
### Added
- **Fan RPM** in Status: real CPU/GPU fan speed shown as framed counters under the fan rings (verified on the Raider GE78HX 13V — `0xC9`/`0xCB`, `RPM = 478000 / raw`), alongside **CPU usage** (distinct colour) and a **RAM** usage bar with values.
- Hidden EC test / discovery tools (**Ctrl+Shift+T**) for bringing up new models: RPM finder, live RPM, read-only EC dump, and an Advanced-fan experiment. Documented in `docs/TECHNICAL.md` §16.

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
- Experimental support for **MSI Crosshair A16 HX (D7W/D8W)** (firmware `15PLIMS1`), added from a community EC snapshot ([#2](https://github.com/wygodad/ghostdeck/issues/2)). Shift/fan registers match the G2 recipe exactly; uses no super-battery register and leaves a secondary fan bit untouched pending hardware verification.

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
