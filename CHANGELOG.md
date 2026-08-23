# Changelog

All notable changes to this project are documented here.
Format loosely based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]
### Fixed
- **The tray's Fan curve submenu now marks the active preset** ([#100](../../issues/100)) -
  a check lands on "Auto (stock)" or on the preset that is actually live in the EC. The fan
  byte read at the moment the submenu opens decides whether a custom curve is running at all,
  so the mark stays truthful after profile switches, and the preset is recognised by its
  points, so it is marked no matter whether it was applied from the tray or from the Fan curve
  tab; a hand-edited curve matches no preset and ticks nothing. The Fan curve tab also mirrors
  a preset applied from the tray - the picker and the chart follow, so both surfaces tell the
  same story.

### Changed
- **MSI Katana GF66 12U / Sword 15 A12UC (`1584EMS1`) promoted to tested, with a verified fan
  curve and fan RPM** ([#116](../../issues/116), [#119](../../issues/119),
  [#120](../../issues/120), thanks @messer2212 and @Error29112002) - one board behind two retail
  lines: the Katana 12UD owner's capture matches the standard recipes byte for byte and he
  confirmed all three hardware checks, while the Sword owner's dumps verify the curve addresses
  and show both fan tachometers alive at `0xC9`/`0xCB`. 27 models tested.
- **Katana GF66 11UE / 11UG (`1581EMS1`): fan curve verified and fan RPM enabled**
  ([#121](../../issues/121), [#122](../../issues/122)) - a second owner re-confirmed every
  recipe byte on firmware .107 and proved the curve addresses with a test curve; his dumps show
  both tachometers as single-byte divisors, so RPM readout is on. @vlf1e joins the entry's
  credit.
- **Katana 17 B12UCXK / B12VGK (`17L5EMS2`): fan curve verified, entry renamed**
  ([#125](../../issues/125), thanks @Dkrimz) - the B12VGK owner's test curve sits byte for byte
  at the shipped addresses. The model itself stays experimental until a clean scenario capture
  and power test.
- **MSI Raider GE78 HX 14VHG (`17S1IMS2`) promoted to tested** ([#102](../../issues/102),
  [#103](../../issues/103), thanks @OrbNRG) - the owner's capture matches the standard recipes
  byte for byte in all four scenarios, and his power test shows a working Silent well beyond the
  run's 6 % thermal drift: CPU 80 C vs 95 C, GPU 67 C vs 90 C, fans at 48 % duty vs 53-77 %, at
  92 % of Balanced's work. Fan RPM enabled at `0xC9`/`0xCB`. 26 models tested.
- **@megadude9704 joins the Raider GE78HX 13V / Vector 17 HX A14V credit** ([#115](../../issues/115)) -
  a 13VH owner re-confirmed the recipes and all three hardware checks on firmware .114. His
  request for fan-curve speeds above 100 % is recorded on the research list.
- **Experimental writes are now enabled per model, not globally.** The old switch unlocked
  every unverified machine in the database at once; the new one, shown only when the detected
  laptop is experimental, names its firmware prefix and unlocks writes for that machine alone.
  An existing consent migrates to the detected machine automatically, and a firmware change
  puts the machine back on read-only until it is consented to again.
### Added
- **Keyboard backlight and Fn/Windows swap on the Creator M14 A13VE** ([#91](../../issues/91)) -
  the owner confirmed both registers by changing them: `0xD3` steps 80-83 with the backlight
  level and `0xE8` reads 01/11 for the swap, so both maps now carry `14P1IWS1` even though
  msi-ec still lists only the sibling board.

### Changed
- **MSI Thin 15 B12UCX / B12VE (`16R8IMS2`): fan curve verified, single-fan layout, CPU fan RPM**
  ([#111](../../issues/111), thanks @arcfybrr) - the owner's test curve sits byte for byte at the
  shipped CPU address; the missing GPU half matches the single-fan Thin B12 chassis (the same
  signature as the sibling Thin GF63 12VE, and teardowns confirm one fan), and the CPU tachometer
  is live in his capture. The model itself stays experimental until a clean power test.
- **Vector 16 HX AI (`15M3EMS1`) re-confirmed on firmware .113** ([#113](../../issues/113),
  [#114](../../issues/114)) - a second owner's capture matches every recipe byte and finds the
  test curve at the verified addresses again; @mithril01 joins the entry's credit.
- **The `17S2IMS2` entry is named "MSI Raider GE78HX 13V / Vector 17 HX A14V"** - every report
  of that firmware on record comes from Vector 17 HX owners ([#32](../../issues/32)), so the
  entry now names the machines that supplied the evidence.
- **MSI Stealth 14 Studio A13VF (`14K1EMS1`) promoted to tested, fan curve verified** ([#107](../../issues/107),
  [#108](../../issues/108), [#109](../../issues/109), thanks @kltk) - the owner's capture matches the standard
  recipes byte for byte in all four scenarios, and his test curve was found byte for byte at the
  shipped curve addresses, so the curve editor is confirmed on this board. His power test shows the
  known thin-chassis pattern: under a full combined load Silent completes the same work as Balanced
  while running 4 C cooler at lower fan speed (the machine is heat-limited, so the profiles converge;
  Silent still applies the firmware's quiet-fan preset). Fan RPM enabled at `0xC9`/`0xCB`. 25 models
  tested.
- **MSI Raider GE68 HX 14VIG / Vector 16 HX A13V (`15M1IMS2`) promoted to tested** ([#104](../../issues/104),
  [#105](../../issues/105), thanks @dodi6161) - the owner's per-scenario capture matches the standard
  recipes on the three main profiles with a real Silent (`0xD4=0x1D`, MSI Center 2.0.48), and his
  power test shows the Silent cap outright: 73% of Balanced's work at 2474 vs 3364 MHz, Extreme 10%
  above Balanced, recipe bytes read back intact after every phase. On this board the vendor's Super
  Battery writes shift `0xC6` and never touches `0xEB`, so the entry carries those exact bytes
  instead of the family defaults. Fan RPM enabled at `0xC9`/`0xCB` (family scheme, owner asked to
  cross-check against HWiNFO). 24 models tested.
- **MSI Creator M14 A13VE (`14P1IWS1`) promoted to tested** ([#91](../../issues/91), thanks
  @otherpartsoftheworld-spec) - his power-test run measured Silent dropping the fan from 3571
  to 2964 rpm at unchanged throughput, with every phase reading its recipe back cleanly.
  Extreme measured equal to Balanced under the combined CPU+GPU load (clock pinned at
  ~2457 MHz), consistent with a shared package budget on this thin 14" chassis; the recipe is
  unchanged and the equality is recorded in the device table. 23 models tested.

## [1.35.0] - 2026-08-17
> **Upgrading from v1.34.0?** Install this one by hand. The release file is named
> `GhostDeck-win-x64.exe` now, so the updater in your copy cannot swap it for you: pressing
> Install reports a failed download and opens this page. That is expected, nothing is broken and
> your installed copy is left untouched. Download the file from here, and make sure the
> [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) is installed.
> Updates after this one work normally again.

### Changed
- **GhostDeck is now licensed under GPL-3.0-or-later.** Releases through v1.34.x were published under the MIT license for material the project had the right to license under those terms, and existing MIT grants to such material remain in effect; from this release the code is GPL. You may still use, study, modify and redistribute GhostDeck freely, but a modified version you distribute has to keep those freedoms and make its source available. The name and the project's visual assets are not covered by the code license, see `TRADEMARK.md` and `LICENSE-ASSETS.md`. A new `THIRD-PARTY-NOTICES.md` records where GhostDeck's hardware knowledge came from, naming the upstream files it was researched from and what was used from each; `licenses/` carries the licenses of the components bundled in the executable.
- **The download is now one small file instead of one large one.** GhostDeck used to ship with the whole .NET runtime packed inside the executable, which made it about 160 MB. It is now built framework-dependent, so the download is around 2.5 MB and updates are correspondingly quicker. In exchange the **.NET 8 Desktop Runtime has to be installed** - most machines already have it, and Windows offers the download if it is missing. The release asset is named `GhostDeck-win-x64.exe`.
- **Banners in the README are raster images now.** The generator used to convert every glyph to vector paths, which put outlines of Windows fonts in the repository; it draws text to a bitmap instead, at twice the previous resolution.

### Added
- **The battery charge limit can be any value from 20 to 100 %**, not just 60 / 80 / 100. The three presets stay one click away - they are what MSI Center offers and the only values verified on real hardware - and a fourth segment, **Custom**, opens a slider for anything in between; the custom value is remembered, so switching between a preset and your own number is one click. A line under the slider says plainly that other values are written through the same register but nobody has measured whether every firmware honours them exactly. Scenes and `--charge` accept the full range too, and a scene keeps a custom value instead of rounding it to a preset.
- **A charge limit changed outside GhostDeck is now noticed** instead of being silently wrong. Installing MSI Center resets the EC threshold to 100 %, so the battery charged to full while the app still showed the 80 % you had chosen. Every hardware sample already read that register - it was simply never compared. The app now adopts the new value, updates Status and the tray, writes it to the change log, and (opt-out, Settings → Notifications) shows an OSD toast plus a tray notification naming the old and the new limit. It deliberately does **not** write your value back: two applications fighting over one register is the kind of loop this app avoids.

### Fixed
- **Updates tab: older release entries now read at full width.** Release notes written with hard-wrapped lines rendered every wrapped line as its own paragraph, so a long entry came out as a stack of short ragged lines while an unwrapped one filled the card. An indented continuation line is now joined to the line above, the way Markdown treats a break inside a paragraph, so every past entry reads properly without editing anything already published.
- Settings → Hotkeys: the warning about shortcuts Windows refused is shown as a full-width amber note, like every other warning on a card.

## [1.34.0] - 2026-08-16
### Added
- **Fan curve page rebuilt around four views of one curve** (sub-tabs; the last one is remembered): **Chart** with a live operating point + optional trail (temperature mapped onto the node axis), audibility zones behind the plot, intent tiles (Quiet / Balanced / Cool / Max, shapes derived from the factory default), up to three comparison layers of saved presets, and a coupled points table under both charts (hover a row = halo on both nodes; click a % to type it); **Equalizer** with one fader per node and wheel nudging; **Deck** with rotary dials, live VU bars and a crossfader blending the whole curve between two shapes; **In action**, which never edits: your curve with the **last hour of real readings** drawn over it (hover a dot for its values, click to pin, or turn every label on), an airflow gauge driven by the current fan duty, and a collapsible diagnostics panel. One shared background reader (1.5 s) replaces the page's mode-only timer.
- **Fan sweep** (Fan curve → In action → diagnostics) - holds the fans at 30 / 45 / 60 / 80 / 100 % for 6 s each behind a consent dialog that names the exact addresses, records tachometers (or duty readback plus reaction time on boards without one), restores the previous curve, fan mode and profile on every exit path, and writes plain-language findings under the results. Every run is kept (the last 30) and can be re-opened and re-exported from the picker, with the firmware and app version it was actually run on. The copied report stays in English for pasting into issues; the findings are appended in your own language.
- **[docs/FAN-CURVE.md](docs/FAN-CURVE.md)** - full documentation of the Fan curve page: the curve arithmetic and why its interpolation is a model rather than a measurement, all four views, the sweep's safeguards, **how each finding is composed and where its thresholds come from**, and the page's DPI and scrolling rules.
- **Seven more UI languages** - 日本語 (Japanese), 한국어 (Korean), 中文（繁體） (Traditional Chinese, Taiwan usage), Türkçe, Tiếng Việt, Bahasa Indonesia and Italiano - 15 in total. Every one of the 612 UI strings is translated (no English fallbacks; the CI gate now checks all 15). Simplified Chinese is labelled 中文（简体） so the two Chinese entries are distinguishable. Native-speaker corrections are welcome as pull requests to `Core/Lang.cs`.
- Updates tab: each release entry gained a **Wiki** button next to Details, opening the illustrated "What's new" tour for that version on the project wiki.

### Fixed
- **Scrolling pages no longer come apart.** `TextRenderer`, which draws every label in the app, honours neither the scroll transform nor the paint clip, so on a scrolled page the cards, curves and dots moved while the captions stayed behind and content painted over the page header. The Report and Scenarios pages now draw their labels through a wrapper that carries the scroll offset, and the Fan curve page's In-action view scrolls by offsetting its own geometry, with its header painted last and off-screen controls hidden. Documented in `docs/RENDERING.md` §5.1 so the next scrolling page cannot inherit it.
- Fan curve, In action: the diagnostics panel no longer scheduled a repaint of itself on every frame (a help dot was hidden and re-shown inside the paint), the newest stored sweep is loaded when the page opens instead of showing an empty panel above its own date, a report re-exported from history carries the firmware and app version of that run rather than today's, and the results table measures its columns in pixels so translated headers stay over their numbers in every language.
- Fan curve: the second line of the page description is no longer cut in half by the sub-tab strip.
- **A keyboard shortcut Windows refuses no longer fails in silence** (#92). When another running app already owns a key combination, Windows rejects the registration - the shortcut then did nothing, with no way to tell it from a broken feature. Refused shortcuts now raise a notification once and are marked on their row in Settings → Hotkeys.
- **A fan slower than the tachometer register can express is no longer reported as ~9958 RPM** (#92). The register holds a divisor in a single byte, so the lowest speed it can state at all is 478000/255 = 1874 RPM; below that, whatever stays in it is not a reading. The plausibility ceiling is now 8000 RPM (the fastest fan ever logged on any model is 7206), so those readings show as "--" instead of an invented number.
- Fan sweep: declining the second dialog (the one naming the addresses) after accepting the first no longer leaves the machine in Balanced. Both questions are asked before anything is switched.
- Scenarios: the setting pills on a scene card get their height from the font instead of a fixed 20 px, so the text no longer sits against their edges (and no longer clips at higher display scaling).
- **A horizontal scrollbar no longer appears after clicking maximize and then restore** (Status, Scenarios, Settings, Report; reported with video in discussion #9). Nothing was actually too wide: resizing the content of a page does not make Windows Forms re-evaluate that page's scrollbars, so the extent worked out while the window was maximized survived the restore. It disappeared as soon as you switched tabs because that path forces a full layout; every page now forces one itself after it lays out.

### Changed
- New installs default to the **Ghost (light)** application icon (existing installs keep whatever icon they have chosen).
- **Model database: MSI GE66 Raider / GP66 Leopard (`1543EMS1`) now gets its Balanced profile right** (#52, thanks @krystian-pytlik). On this board our Balanced sat at a fixed 30 W power limit - the same as Silent - while MSI Center's Balanced held a moving 57-71 W, even though both write the same profile bytes. The owner's per-scenario dump had exactly one configuration byte differing between the vendor's Silent and Balanced, and a test build confirmed it by measurement: the limit became dynamic, settling at 55 W under load with ~58 W average package power. GhostDeck now writes that byte with the vendor's own values on this family. Ships through the signed model database.
- Model database: `1585EMS1` promoted to **Tested** and renamed to **MSI Creator M16 B13VF / Pulse 15 B13VGK / Katana 15 B13UDXK** - one board sold under three names, found via the report form's "Actual model" field. Byte-perfect per-scenario dumps from one owner plus all three hardware checks from another (#89, #90, thanks @Punssama and @Gangan-Lin). Fan RPM stays off for now: this board reports tachometers as 16-bit register pairs, a format the app will learn separately. Ships through the signed model database.
- Model database: MSI Pulse 16 AI C1VGKG/C1VFKG (`15P3EMS1`) promoted to **Tested**, with live fan RPM (`0xC9`/`0xCB`) and the fan curve verified on both fans - all three hardware checks plus a power test and a curve capture from the owner (#68, #84, #85, thanks @migecko). Ships through the signed model database, no app update needed.

## [1.33.0] - 2026-08-12
### Added
- **SSD temperature alert** *(opt-in, Settings → Notifications)* - an OSD toast + tray
  notification when the hottest drive stays above a chosen threshold (55-80 °C) for 30 s,
  with the same 5-minute cool-down as the CPU/GPU alert. Disk temperatures come from the
  Windows storage APIs, so the alert works on every machine, including unsupported firmware.
- **Charge-limit travel mode** (Settings → Power; CLI `--travel <days|off>`) - one click
  charges the battery to 100 % for a trip and the previous limit comes back on its own after
  the chosen number of days (3/7/14/30 in the UI, 1-90 via CLI). Any manual charge-limit
  change cancels the pending revert; ending the mode early restores the previous limit
  immediately.
- **`docs/SUPPORTED_MODELS.md` is now generated from the code.** The hidden CLI
  `--dump-supported-md` writes the whole page from the compiled model tables, and CI fails
  whenever the committed file - or the README model counters - drifts from the code, so a
  model promotion can no longer leave the docs stale.

### Changed
- **The charge limit is re-asserted a few seconds after resume** - hibernation can drop the
  EC's charge threshold on some boards; re-writing the same byte is harmless.
- **Tray temperature icons: a sleeping discrete GPU now shows `--`** instead of its icon
  vanishing - a disappearing icon read as a bug and made the neighbouring tray icons jump
  around. The dash uses a muted colour, so it is never mistaken for a good temperature.
- **Settings → Notifications: the alert groups are visually separated** and a "Restore
  defaults" button resets the whole card in one click; the travel-mode row gained an
  in-app help bubble explaining the automatic return of the previous limit.
- **Models tab: an owner-verified curve on a not-yet-tested model now reads "verified
  (opt-in)"** instead of plain "editable" - the same distinction the supported-models table
  makes, so the app and the docs tell one story.

### Fixed
- **Models tab: boards with a single fan tachometer no longer show a bogus `/0x00` GPU
  address** in the RPM column (e.g. Modern 14 C12M).

## [1.32.0] - 2026-08-11
### Changed
- **Model database:** the `158NIMS1` entry now names both machines MSI ships that board in -
  **Bravo 15 C7V and Katana A15 AI B8VG** - and gained live fan-RPM addresses (`0xC9`/`0xCB`)
  from the Katana owner's capture. Reaches existing installs through the signed model
  database, no update needed ([#80](https://github.com/wygodad/ghostdeck/issues/80)).

### Added
- **The startup EC probe says WHY there is no control instead of one generic "unsupported".**
  The app now distinguishes: the MSI WMI schema not being registered at all (fresh Windows
  installs - the message points to the one-time MSI Center installation, see
  [docs/MSI-WMI-SCHEMA.md](docs/MSI-WMI-SCHEMA.md)), a firmware that refuses the interface,
  denied access, an empty firmware answer, and a transient WMI failure - the last one is
  retried automatically for about half a minute, so one WMI hiccup at launch no longer leaves
  the app dead until a restart. Hard startup failures are recorded in errors.log, and the
  diagnostic package gained `wmi-interface.txt`: the probe verdict, schema registration and
  its source file (with signer), `MofImagePath` and the ACPI-WMI devices - the questions that
  used to take a round of scripts per report
  ([#56](https://github.com/wygodad/ghostdeck/discussions/56)).

### Fixed
- **Refresh-rate controls follow the laptop panel, not the primary display.** With an external
  monitor set as primary, the Power page listed and changed the monitor's rates instead of the
  panel's, and the same targeting fed scenes, the AC/battery auto-switch, `--refresh` and
  `--status`. All of them act on the built-in panel: among the active display paths the one whose
  connector is embedded (internal / eDP / LVDS) names the device, resolved fresh on every call so
  docking cannot stale it; with no active internal panel (lid closed, desktops) the primary
  display is used. The Power card gained a line naming the display being controlled, with the
  panel's EDID name when it reports one. The Power card and the Scenarios rate brick also follow
  display-mode switches live (dock/undock, "second screen only") instead of waiting for an app
  restart. Scenes remember WHICH physical display their rate was chosen for and skip it when
  that display is not the one being controlled - a scene saved against an external monitor
  never retunes the laptop panel after undocking
  ([#69](https://github.com/wygodad/ghostdeck/issues/69)).

## [1.31.0] - 2026-08-10
### Added
- **The power test loads the graphics chip as well as the processor.** A processor-only load answers
  only half the question: on a thin chassis it never reaches the ceiling the top profile already
  grants, so a mode above that one has nothing to show, and a budget the two chips share stays
  invisible. The discrete adapter is now loaded for the whole run, started before the first settle so
  temperatures stabilise with it going, and identical in every phase. The report header states which
  adapter it was, or that the run had the processor only, because the two cannot be compared with
  each other. Nothing is drawn and nothing is read back; if a device cannot be created the run
  continues on the processor alone. On the reference board the profiles grew *more* distinguishable:
  Extreme against Silent went from 1.82× to 1.98× once both chips were asking for power.
- **The power test measures its baseline twice.** BALANCED runs once at the start and once more at
  the end, with every other phase in between, and the repeat row is normalised to the first one - so
  its work column is the drift of the whole run. A machine that finished as fast as it started prints
  100 and the comparison stands; a run carried by heat soak or by the running order says so itself
  instead of pretending. The report gained a "Baseline check" section that spells the verdict out.
- **MSI Vector 16 HX AI A2XWHG / A2XWIG (`15M3EMS1`) is now tested**, the first model promoted on a
  measurement rather than on its owner's judgement. The power test answered all three hardware checks
  with numbers: Silent holds 84 % of Balanced's delivered work at 4728 MHz against 5432 and 56 °C
  against 69, so the Silent fan value caps power there and not just fan noise; Extreme reaches 111 %
  at 6306 MHz; and the dumps show the tachometers alive across four fan states, so real fan RPM is
  shown. Thanks to **@xulu19861102-hub** ([#74](https://github.com/wygodad/ghostdeck/issues/74)).
- **Report / verify opens on a start screen.** Three tiles say what each test answers, what it needs
  (the first two need MSI Center as an independent reference, the power test needs nothing) and
  whether it writes anything, so the choice explains itself before any wizard starts. The sub-tab
  strip gained icons and a Start segment that brings the overview back.
- **Status shows the graphics core clock next to its ceiling** ("GPU clock: 2280 MHz · 73 %"),
  read from Windows itself with no vendor software - the same interface Task Manager uses. The share
  of the ceiling is the point: a busy card sitting well under it is the firmware holding it there,
  which is exactly what a performance profile changes. A ? dot opens a bubble explaining the figure,
  and names the adapter.
- **Help bubbles you can actually read.** The circled ? next to a control now opens a themed bubble
  on click and it stays until you click again, replacing the system tooltip that styled itself and
  vanished mid-sentence. Same behaviour on Scenarios, Status and the Models table header.

### Fixed
- **Switching the UI language now updates every caption in place.** The main tab strip, the sub-tab
  bars on Status and Report and the export buttons kept the old language until a restart, because
  their captions are captured when the controls are built; a language change now rebuilds them.
- **The power test's work meter no longer drifts on its own.** The load kernel reseeds its arithmetic
  every block, so every block is identical work; before, the state carried across blocks and the
  throughput swung by up to half over a fixed cycle, which could move the work column by several
  points depending on where the steady window fell in the cycle.

### Changed
- **The thermal stop watches the graphics chip too**, now that the run deliberately heats it. A
  sensor that is not present reads zero and never trips it.
- **The consent card names both chips** and the run now takes about seven minutes (four measured
  phases plus the probe) instead of five.

## [1.30.3] - 2026-08-07
### Fixed
- **The steady window is carved out of time, not out of sample numbers.** A phase that manages
  twenty-two readings across its minute has a window of the last twenty-five seconds, roughly nine
  of them, and the ramp at the start of the phase stays out of it where it belongs.
- **A phase could run for eleven minutes instead of one.** The load loop counted samples rather than
  seconds, and each sample waits a second and then reads the controller. When a controller read
  took forty seconds the phase stretched with it, so a run that promises about five minutes took
  more than twenty while the report still claimed sixty seconds per profile. Phases are now bounded
  by the clock; a slow read costs samples, which the **n** column already reports, and nothing else.
- **The test was starving the service it reads through.** Every controller read goes via the WMI
  provider, which needs a processor to answer on, and the load was taking all of them: the phase
  that held 95 % of the machine waited up to forty-four seconds for one reading, while the phase
  that happened to hold only 89 % never waited at all. Two logical processors are now left out of
  the load. They cost the same in every phase, so the comparison is unchanged.
- **A slow controller reading was reported as "the machine was not idle".** It is the opposite:
  the test's own load causes it. It now has its own note saying so, instead of sending the reader
  hunting for other software.
- **"Copy all" on an empty change log wiped the clipboard.** The retrying copy added in 1.30.2
  accepts an empty string where the old call refused it, so an empty log replaced whatever the user
  had copied instead of doing nothing.
- **A failed clipboard copy could be drawn below the scrollable area of the page**, so the one
  person who needed to read that warning was the one who could not reach it. All three report
  wizards now reserve room for it, and it appears even when the file write failed as well.
- **Cancelling during the new idle check** ended the run by reporting a controller-read failure,
  blaming the MSI interface for something the user did on purpose.
- **A refused run still put the machine back**, writing the profile recipe, logging a change and
  flashing the overlay for a run that never touched the controller and said so.
- **A refusal verdict stayed on screen** and stacked with whatever the next attempt reported.
- **Fan RPM averages counted rejected readings as a stopped fan.** The plausibility gate added in
  1.30.1 returns 0, which already meant "stopped", so one rejected reading in a window pulled the
  printed figure down.
- **The steady window was 25 samples rather than 25 seconds**, so dropped controller reads widened
  it silently while the report still claimed a full window. The work and share figures are also
  weighted by the interval each was actually measured over, instead of counting a long starved
  second the same as a short clean one.
- **The idle check ignored the fourth-mode phase**, so contamination confined to that phase left the
  verdict green.

### Added
- **The power test checks the machine is idle before it writes anything.** Three seconds of
  measuring how busy the computer already is, and if more than 15 % of it is spoken for the run
  refuses and says by how much. Catching a busy machine up front costs three seconds; catching it
  afterwards costs five minutes of hot fans and a report that has to be thrown away. Three runs out
  of four during development were spoiled this way, one of them badly enough that Extreme appeared
  to deliver less work than Balanced.
- The warning card now says plainly to **leave the machine alone while the test runs**, which is the
  one instruction that decides whether the numbers mean anything and was missing.

## [1.30.2] - 2026-08-07
### Fixed
- **A report could silently fail to reach the clipboard.** Every report wizard copies its result and
  then tells you to paste it into a GitHub issue, but Windows refuses the clipboard outright
  whenever another program is holding it open, and that failure was swallowed. The file was still
  written correctly, the clipboard kept the **previous** report, and a paste sent the wrong run to
  an issue with nothing on screen saying so. This happened on a real report. The write now retries,
  and if it still cannot get there the page says so and points at the saved file instead of claiming
  a copy that did not happen. Applies to all three report wizards, the interim report dialog and the
  change-log window.

### Changed
- **The power test reports uneven CPU shares, not just low ones.** A shortfall shared equally by
  every phase cancels out of the ratio the table prints; an uneven one does not, and bends the
  comparison itself. A phase that got a different share of the machine than Balanced is now named
  along with how far, and which way, that pushes its work column. On the run that prompted this,
  Silent held 81 % against Balanced's 88 %, which accounts for its 65 where a clean run gives 69.
- The power-test report is written in **invariant English** throughout. On a machine with a comma
  decimal separator it was emitting "13,9 s" and "81,0 %" into an otherwise English file.

## [1.30.1] - 2026-08-07
### Fixed
- **Fan RPM could read as tens of thousands.** The tachometer register is a divisor, so catching it
  between updates (a raw value of 2) turned into 239,000 RPM in Status, in the gaming overlay and in
  a power-test report. Anything past what a laptop fan can physically do is now reported as no
  reading instead of as a number. Found by the first power-test run on real hardware ([#72](../../issues/72)).
- **The power test's GPU columns counted seconds when there was no GPU to read.** A discrete GPU
  powers down under a CPU-only load and the controller then reports its whole block as zeros;
  averaging those in produced a 34 °C graphics chip next to a 60 °C one. The GPU columns now count
  only the seconds it was awake, and a new **gpu** column says how many those were.
- **The power test assumed exactly one second between samples.** It waits a second and then reads
  the controller, so the gap is always a little more, and a slow read was reported as a burst of
  computation. Each sample now carries the measured gap and the work figure is divided by it.

### Added
- **The power test says so when the machine was not idle.** Its load threads run below normal
  priority, so anything else wanting the processor takes it first, and the comparison then describes
  that other work rather than the profiles. Every second now records how much of the machine the
  test actually had; a run that got too little, or whose sampling was held off, is called out on the
  results line and at the top of the report instead of handing over confident numbers about the
  wrong thing. A virus scanner working through a freshly downloaded file is the usual cause, and it
  cost a real run 60 % of the machine and stalled one sample for 53 seconds.
- The power-test table shows the **lowest and highest CPU clock** inside the steady window. A board
  cycling between two power states averages differently depending on where the window falls, and
  that is now visible instead of hidden ([#72](../../issues/72) shows 3400-4950 MHz in Balanced).
- The `power-test` label the report form asks for now exists, so those reports arrive labelled.

## [1.30.0] - 2026-08-07
### Added
- **Power test**, a third wizard under Report / verify (⚑), measures your profiles instead of
  asking you to judge them by ear. The same all-core load runs in Silent, Balanced and Extreme
  while temperatures, both fan duty values, both tachometers, the CPU clock and the work the
  processor actually completes are sampled every second; the last 25 seconds of each phase become
  one comparison table. Whether Silent really caps power stops being an impression and becomes a
  column. Unlike the other two wizards it needs **no MSI Center**. About five minutes, mains power
  and an explicit tick required, every address it may write listed before you start, and your
  profile restored at the end (also by Cancel).
- **Fourth performance mode, recorded and probed.** Some newer boards accept a fourth value in the
  shift register, which their MSI Center build shows as a switch inside the top scenario rather
  than as a scenario of its own. The signed model database can now carry that value per model, and
  the Power test probes it: it writes the value, reads the register back, compares two idle dumps
  taken beforehand so sensor drift can be subtracted from whatever moved with the write, runs the
  same load, then reverts and reads back again. Nothing writes it as a feature yet - this release
  gathers the evidence that would justify one.
- **MSI Stealth 16 AI+ B3WI** (`2631EMS1`) recognised, experimental
  ([#66](../../issues/66), [#67](../../issues/67), thanks @SteppinStone). The fan curve is
  hardware-verified at the shipped addresses, the fan tachometers are live so Status shows RPM, and
  its fourth shift value `0xC5` is the first one in the database. 146 models recognised.

### Fixed
- **A board's fourth shift value no longer reads as the wrong profile.** With the value recorded,
  the app reports it as Extreme, so the 3 s poll stops logging a profile change whenever the vendor
  software sets that mode.

## [1.29.0] - 2026-08-07
### Added
- **Model-database updates apply without restarting** and are checked far more often. The
  database is now fetched at every start, when you open the Models tab, and from a new
  **Check now** button in Settings → System → Updates, instead of once a day alongside the
  release check. A newer database goes live immediately: the Models tab, the tier gates, fan
  RPM and the fan-curve layout all follow without closing the app. The one exception is an open
  fan-curve editor with the switch on, where the update waits until you leave it rather than
  changing the register layout under an edit in progress.
- **Katana 15 HX B14WEK promoted to Tested** ([#63](../../issues/63), thanks
  @zajebistylukasz-beep) - the per-scenario capture matches the shipped recipe byte for byte and
  all three hardware checks are confirmed, so the model leaves the experimental opt-in. Its fan
  curve is hardware-verified on both fans ([#64](../../issues/64)) and the fan tachometers at
  `0xC9` / `0xCB` are live, so Status now shows RPM for it. 18 models tested.

### Changed
- **Tray temperature colours** now ship as brand cyan / amber / red instead of green / amber /
  red. The digits carry a dark outline, which lets the lighter, more saturated set stay readable
  on a light and a dark taskbar alike. Existing settings keep whatever colours they hold; the
  "Default colours" button applies the new set.

### Fixed
- **The unassigned-hotkey placeholder showed Polish text in every language.** Settings → Hotkeys
  now says "(none)" in the language you picked.
- **Scene cards** kept a fixed gap under the scene name instead of squeezing the chips against it
  once a card carried two rows of them.

## [1.28.0] - 2026-08-05
### Added
- **Temperature in the notification area** ([discussion #9](../../discussions/9), thanks
  @its475) - optional CPU and GPU readouts as their own tray icons, next to the profile
  ghost. Two separate icons because a tray icon is 16x16 px at 100 % scaling: room for two
  bold digits, not for two values. Both are off by default and each can be enabled on its
  own in Settings → System, card "Temperature in the tray", together with the warning/hot
  thresholds (default 70 °C / 85 °C) and the three colours. The card appears on recognised models
  and on any machine currently reporting a CPU temperature. Note: Windows puts new tray icons in
  the overflow area at first, so drag them onto the taskbar to keep them in sight.
- **Settings can always open on Start** - an option for people who prefer the dashboard as
  the entry point every time instead of returning to the sub-tab they left. Settings → General,
  card "Navigation"; off by default.
- **Clicking the tab you are already on resets the page** - Settings goes back to Start,
  Status back to its first sub-tab, so there is a one-click way home from anywhere.

### Fixed
- **Horizontal scrollbar in Settings on smaller screens**
  ([discussion #9](../../discussions/9)): switching to a sub-tab whose strip did not fit
  pushed a scrollbar onto the whole page. The strip now falls back to icons only when it cannot fit, keeping the label on the tab
  you are on and expanding the one under the cursor in place. At the minimum window size the
  full strip does not fit in 7 of the 8 languages (Chinese is the only one that does), so this
  affected everyone with a narrow window, not just one language.
- **Scenarios and Report drew in the wrong place after scrolling** - both pages positioned
  their content in client coordinates while the scroll offset had already been applied, so
  cards and buttons landed shifted (and could overlap) once the page was scrolled.
- Gaming overlay: the rounded border path was not released after painting.

### Changed
- **One WMI session instead of one per call** - every EC read/write used to open its own
  connection to the MSI WMI interface, several times per 3-second tick for the life of the
  process. The session is now created once and shared. Any failure drops it and retries on
  a fresh one, so a WMI provider restart or waking from sleep still recovers by itself.
- **Memory** - the Status page's offscreen buffer (~6 MB at 1600x980) is released when the
  page is hidden and re-rendered on demand, and the translation tables are built in chunks
  so the JIT no longer has to hold one enormous method.
- **Tray mouse-wheel actions are now opt-in on new installs** - the feature needs a
  system-wide mouse hook, so every mouse event in Windows passes through it. Existing
  settings are untouched; Settings → System → Tray menu turns it on.

## [1.27.0] - 2026-08-04
### Added
- **Modern 14 C12M promoted to Tested** ([#61](../../issues/61), thanks @ping-myildirim) -
  all three hardware checks confirmed on real hardware, and the fan curve is
  hardware-verified as a single-fan board ([#60](../../issues/60)): the test curve sits
  exactly at the shipped CPU table, the GPU table is a no-op, and the CPU fan RPM register
  is live. 17 models tested.
- **Model support without waiting for a release** - the app now checks (on the existing
  daily check, same opt-out) for a **digitally signed model database** published in the
  repo and uses it from the next start when it is newer than the built-in tables. A newly
  verified model or fan curve can reach every user the same day. Safety first: the file is
  signed with a key that exists only on the maintainer's machine (ECDSA P-256), the
  signature is re-checked on every load, an older file is never accepted (anti-rollback),
  and anything invalid silently falls back to the built-in tables - a bad download can
  never break the app. Settings → System → Updates shows the database version in effect.

## [1.26.0] - 2026-08-04
### Added
- **Cyborg 15 A13VF hardware-verified** ([#57](../../issues/57), thanks @M-Essa11) - the
  `15K1IMS1` entry now covers the A13VF explicitly: all hardware checks confirmed on real
  hardware (including the classic Silent cap that MSI Center 2.0.7x no longer offers) and
  live fan RPM. Note for reporters: MSI Center 2.0.72 on this model ships only 3 scenarios -
  its "Silent" is the super-battery state.
- **Fn / Windows key swap** - swap the two keys in hardware on boards where msi-ec documents
  the `fn_win_swap` register (162 firmware prefixes; bit 4 at `0xBF` or `0xE8` with a
  per-family direction flag). A "Keyboard layout" card in Settings → System (shown only on
  mapped boards) picks which side the Fn key sits on; the setting lives in the EC itself, so
  it survives reboots. CLI: `--fnswap left|right`.
- **Screen brightness in scenes** - a scene can now set the internal panel's brightness
  (5-100 %, plain Windows WMI, driver-free), so "Gaming" can mean 80 % and "Travel" 25 %.
  Also available as `--brightness <0-100>` in the CLI; both work on any laptop, supported
  or not (external monitors are out of scope).
- **Windows-key lock** - blocks both Windows keys so a game never loses focus to an
  accidental Start menu: a brick on the Scenarios tab, a scene field, a hotkey shipped as
  `Ctrl+Alt+F8` (disabled by default) and `--winlock on|off` in the CLI (needs the running
  app). A low-level keyboard hook, no EC involved, works on any laptop. Fine print: Win+L
  is blocked too while active, Ctrl+Alt+Del never is, and a **panic reset always lifts the
  lock**.
- **Scene schedule** - different settings for work hours, nights and weekends: rules
  (weekdays + a time window, overnight ranges allowed) apply a scene when the window starts.
  Edge-triggered by design - a manual change inside a window is respected; the active window
  also applies at startup and after waking across a boundary. First matching rule wins;
  managed in Settings → Power with a per-rule editor dialog.
- **Battery-level rules** - e.g. below 30 % switch to Super Battery, above 80 % back to
  Balanced. Two slots (below / above), each with a threshold and an action (any profile or
  scene). Direction-aware: the lower rule fires only while discharging, the upper one only
  while charging, once per crossing (re-armed 3 pp past the threshold) - so it never fights
  you or the AC/battery auto-switch.
- **HDR as a scene field** - a scene can switch HDR (advanced color) on or off, so "Gaming"
  or "Movies" turns it on and "Work" turns it off. Also `--hdr on|off` in the CLI. Windows
  DisplayConfig API - the row only shows on HDR-capable displays.
- **Touchpad switch** - enable/disable the precision touchpad from a scene, a Scenarios
  brick, a hotkey (`Ctrl+Alt+F9`, disabled by default) or `--touchpad on|off`. Device-level
  (the same thing Device Manager does), so it works on any laptop; the hotkey and a panic
  reset always re-enable it, and the hint says so.
- **10 new experimental models from the weekly msi-ec sync** ([#59](../../issues/59)):
  Venture 14 AI A2HMG, Prestige 14 Flip AI+ D3MTG, Stealth 15M B12UE, CreatorPro
  Z16HXStudio B13V, Modern 15 H B13M, Cyborg 15 B13WFKG/B2RW, Venture A15 AI, GV62 8RD,
  Creator 15 A11UE and GE76 Raider 10UG - recognition grows to ~145 firmware ids
  (129 experimental). Four G1-era prefixes without a documented Silent fan value stay out
  (our Silent/Balanced detection needs it).
- **Scene cards show their settings as chips** - every setting the scene touches is its own
  small pill (the profile pill highlighted), pills wrap to extra lines and all cards in the
  grid share the tallest card's height - the whole scene is visible at a glance instead of
  a truncated sentence.
- **Gear shortcut on the Scenarios tab** - a small gear between the profile tiles and the
  bricks jumps straight to Settings → General → "Scenarios tab" and highlights that card
  with a colored frame for a while, so the "what is visible here" switches are easy to find.
- **CLI catch-up** - the command line now covers everything the recent releases added:
  `--refresh <hz|max>` (panel refresh rate, any machine), `--charge <60|80|100|off>`
  (battery charge limit), `--fanboost on <seconds>` (a one-off auto-off timer, 10-7200 s,
  needs the running app), `--diag [path.zip]` (the one-zip diagnostic package, headless -
  works even when the UI won't start), and a much richer `--status` JSON: battery percent /
  charging / minutes left / wear, per-disk temperatures, charge limit, keyboard-backlight
  level, webcam state, Fn-key side, Windows-key lock, HDR, touchpad and a `telemetry` flag
  on monitoring-only boards (one-shot `--status` now reads the vendor WMI blocks there too).

### Fixed
- **Segmented buttons never squeeze their labels against the edges any more** - every
  segmented control now enforces a minimum width of its widest label plus breathing room
  (seen on "Fn on the left/right" and the Hz picker in Settings → Power → Display).
- **Scene editor window widened** (520 → 600 px) - long row labels no longer run into the
  value combos.

## [1.25.0] - 2026-08-01
### Added
- **Vector A18 HX A9WHG (`182LIMS1`) promoted to Tested** ([#54](../../issues/54), thanks
  @Skullkidsrevenge) - all three hardware checks confirmed on real hardware (Silent quieter,
  Extreme ramps up, switching stable in daily use).
- **Raider A18 HX A7VIG (`182KIMS1`) fan curve hardware-verified** ([#55](../../issues/55),
  thanks @afk789) - the owner's wizard capture shows the MSI Center test curve exactly at the
  shipped table addresses (CPU `0x72`, GPU `0x8A`), so the editor loses its "addresses
  unconfirmed" caveat on this board.
- **Scenes** (roadmap #21) - one-click macros over the existing controls. A scene can set any
  combination of: profile, fan-curve preset, display refresh rate, gaming overlay, charge
  limit, keyboard-backlight level, webcam and Fan Boost - each field is optional, so a scene
  only touches what you picked. Run a scene from the new Scenes section on the Scenarios tab,
  the tray menu, a per-scene global hotkey, the tray-icon scroll wheel, or the command line
  (`--scene "Name"`). Ships with a one-click "Add example scenes" starter set (Gaming / Work /
  Travel - plus a "Current setup" scene frozen from the machine's state at that moment, so
  one click brings everything back after trying the examples). Applied in a safe order
  (profile first, then the curve), logged as one change-log entry with a summary, one OSD
  toast.
- **Tray-icon mouse actions** (roadmap #23) - the tray icon now answers to more than a left
  click: **scroll wheel** switches profiles (or scenes, or keyboard-backlight level), **middle
  click** toggles Fan Boost by default, and all three - left, middle, wheel - are configurable
  in Settings → System → Tray menu (profiles, Fan Boost, overlay, panic reset, show state, or
  opening any tab). Fast wheel spins are coalesced: the target is previewed on the OSD and
  written once when the wheel rests.
- **Keyboard-backlight level** (roadmap #26) - off / low / mid / high on models where msi-ec
  documents the EC brightness register (82 firmware prefixes, mostly single-colour keyboards;
  per-key RGB boards are controlled by SteelSeries software instead and are not covered). A
  segmented brick on the Scenarios tab, a cycle hotkey (off → low → mid → high) shipped as
  `Ctrl+Alt+F6` but disabled by default, a scroll-wheel mode, `--kbd <off|low|mid|high>` in
  the CLI, and a scene field. The state follows the laptop's own Fn key.
- **Webcam switch** (roadmap #27) - the same EC-level switch the Fn camera key flips: off
  means the camera drops off the USB bus entirely, below Windows privacy settings. A toggle
  brick on the Scenarios tab, a hotkey shipped as `Ctrl+Alt+F7` (disabled by default),
  `--webcam on|off` in the CLI and a scene field. Plus an advanced **hard camera block**
  (Settings → System → Privacy) with an amber warning and an inline confirm step: while
  active, neither the Fn key nor the switch can re-enable the camera. A panic reset lifts
  the block and re-enables the camera, so there is always a one-key way back to stock.
- **Scenarios tab grows up** - a panel refresh-rate switch (pure Windows display API, works
  on any laptop) and a red **panic-reset button** join the quick-control bricks; the grid
  moves to three columns on wide windows; every element there - each brick and the whole
  Scenes section - can be hidden in Settings → General → "Scenarios tab"; and each scene
  card carries its own action buttons (run / move up / move down / edit / delete, with a
  click-again-to-confirm delete instead of a popup).
- Settings → Power → Display now shows the **current panel refresh rate** plus a manual
  "Change now" picker, and the Start dashboard's Power tile carries the live rate too.
- **EC live view** (default hotkey `Ctrl+Shift+E`, also a button in the hidden EC test
  dialog; rebindable or disable-able in Settings → Hotkeys) - a read-only window with the full 256-byte EC dump refreshed every 1.5 s: bytes
  that just changed glow amber and every change lands in a log as `0xF3: 80 → 82`. Built for
  model support - press an Fn key (backlight, camera, fans) and see immediately which EC
  register reacts, no diagnostic zips to compare by hand.

### Changed
- The Raider GE76 `17K4EMS1` entry is now named **"Raider GE76 12UE / 12UGS"** - the same
  MS-17K4 board ships under both names ([#47](../../issues/47), thanks @moragab1993).
- Documented why laptops with **per-key RGB keyboards** get no backlight control: their five
  brightness levels live inside the keyboard's own controller, invisible to the EC, to HID and
  even to SteelSeries' own software, which has no brightness control either. Measured on real
  hardware and written up in `docs/LIGHTING.md` together with the protocol that *is*
  confirmed on those keyboards, so the question does not have to be reopened.

## [1.24.1] - 2026-07-29
### Added
- **Fan Boost can switch itself off** ([#51](../../discussions/51), thanks @cesarcamps) -
  Settings → Power → "Turn Fan Boost off automatically after": 30 s, 1, 2, 3, 5, 10 or 15
  minutes, or any custom value up to two hours (off by default). The countdown starts whenever
  Fan Boost goes on - tray, hotkey, the Scenarios tile or the command line - and when it
  elapses the fans go straight back to whatever the active profile or curve was doing, with an
  on-screen note and a change-log entry. Turning Boost off by hand cancels the timer.
- **Temperature readings on laptops without MSI's EC interface** ([#48](../../issues/48),
  thanks @SavAlexander) - a few MSI models ship firmware that does not implement the EC
  control interface at all (proven on a Delta 15 A5EFK: the owner extracted the BIOS and the
  interface GUID is absent from the firmware tables). GhostDeck used to be completely dead on
  such machines. It now falls back to MSI's own WMI sensor blocks and shows **live CPU/GPU
  temperature** on the Status tab and in the overlay, with a plain explanation that profiles,
  fan curves and the charge limit are unavailable on that firmware. Read-only, no driver.
- **Custom fan curves survive a reboot** ([#49](../../discussions/49), thanks @inokra) - the
  EC resets to its factory fan mode on every cold boot, so a custom curve always needed
  re-enabling by hand. A new opt-in switch (Settings → Power → "Restore fan curve after wake /
  at startup") makes GhostDeck remember the curve that was active - a preset or a manual one
  from the editor - and re-apply it a few seconds after startup and resume, with a change-log
  entry each time. Profile restore also re-asserts its state now even when the EC happens to
  boot into the same profile.
- **Status sub-tabs got icons** - Charts / History / Gaming / EC bytes / Change log now carry
  the same icon style as the Settings sub-tabs.
- **One-click diagnostic package** (Settings → System → Diagnostics) - a single zip with a
  read-only EC dump (or the exact error it produced), settings, the change history and
  errors.log; everything a bug report keeps asking for, attached in one file. No personal data
  lives in any of these files.
- **Battery health panel** (Settings → Power) - design capacity vs full-charge capacity,
  wear % and charge cycles, read straight from Windows' battery WMI classes; the natural
  neighbour of the charge limit.
- **Estimated battery time** - while discharging, the tray tooltip shows Windows' own
  "~2 h 40 min left" estimate, a new box on Status → Charts shows it too, and a new optional
  overlay metric puts it in the HUD during gaming.
- **Storage panel with per-disk S.M.A.R.T. temperature** - Status → Charts lists every
  physical disk with its name, used/total space (with a usage bar) and live S.M.A.R.T. temperature (no kernel driver; the hottest
  disk also available as an optional overlay metric); NVMe drives can throttle during long
  sessions.
- **Every Settings card now has an icon** and the Backup card moved to the left column of
  System.
- **Raider A18 HX A7VIG (`182KIMS1`) promoted to Tested** ([#50](../../issues/50), thanks
  @afk789) - per-scenario capture matches the shipped map, all three hardware checks passed,
  and fan-RPM readout is enabled. That's **15 tested models**.
- **GP66 Leopard 11UG / GE66 Raider (`1543EMS1`): fan curve verified and RPM enabled**
  ([#52](../../issues/52), [#53](../../issues/53), thanks @krystian-pytlik) - the owner's test
  curve was found byte-for-byte at `0x72` / `0x8A`, so the editor's "unverified" tag is gone.
- **Raider GE76 12UE (`17K4EMS1`) promoted to Tested, fan curve verified**
  ([#45](../../issues/45), [#47](../../issues/47), thanks @moragab1993) - the per-scenario
  capture matches the shipped register map, the owner's test curve was found byte-for-byte at
  `0x72` / `0x8A`, and fan-RPM readout is enabled (`0xC9` / `0xCB`).

### Fixed
- **Super Battery on the A18 HX boards no longer writes a register MSI Center leaves alone**
  ([#50](../../issues/50), [#54](../../issues/54)) - two independent captures (Raider A18 HX
  A7VIG and Vector A18 HX A9WHG) show `0xEB` staying at 00 in Super Battery on those AMD
  boards, so GhostDeck now mirrors MSI Center exactly and skips that write.
- **Buttons react to the mouse again** - outline-style buttons (Details in the release list,
  Try again, backup buttons and others) had no visible hover state on the dark theme; they now
  highlight under the cursor.
- **A clearer message when the EC cannot be read** ([#48](../../issues/48)) - the capture
  wizard used to end with a bare "Details: Unsupported". It now says what actually happened
  (the firmware refused the request) and points to reporting the model, with separate wording
  for access-denied and for machines without MSI's WMI interface at all.

## [1.24.0] - 2026-07-28
### Added
- **Crosshair 16 HX AI D2XW (`15P4EMS1`) promoted to Tested, fan curve verified**
  ([#43](../../issues/43), [#44](../../issues/44), thanks @Harsh3456D) - a clean per-scenario
  EC capture confirmed the shipped register map (shift `0xD2` C2/C1/C4, super-battery `0xEB`),
  the wizard located the owner's test curve at exactly `0x72`/`0x8A`, and all three hardware
  checks passed.
- **Releases are digitally signed** - starting with this version, the `GhostDeck.exe`
  published on GitHub carries an Authenticode signature ("WYGODA DAWID FENIX INSPIRE", Azure
  Artifact Signing, RFC 3161 timestamp). Check it under file Properties → Digital Signatures;
  the release pipeline refuses to publish an unsigned or wrongly-signed file. This also means
  SmartScreen "unknown publisher" warnings will gradually disappear as reputation builds.
- **Settings reorganized into sub-tabs** - the one long two-column page is now split into six
  groups (General / Power / Notifications / Gaming / Hotkeys / System) behind an icon strip
  like the Status tab, plus a **Start page with a tile per group** for quick orientation.
  GhostDeck **remembers which sub-tab you were on** and reopens Settings right there, even
  after an app restart. Display settings (refresh-rate switching) got their own card next to
  Power.
- **The Settings Start page is a dashboard** - every tile carries a live third line with the
  group's current values (charge limit and refresh rates, alert threshold, overlay state and
  metric count, enabled hotkeys, autostart and update check) plus an on/off dot; the Gaming
  and Notifications tiles get a **quick master switch** right on the tile; a **status header**
  shows the laptop model, support tier, firmware and app version, and turns into a clickable
  **"new version available" chip** when the daily check finds a release; a **"What's new in
  vX"** link opens Updates with the current version's notes already expanded.
- **Release notes readable inside the app** - clicking a release in Updates → Release history
  expands its full formatted notes inline (section headers, bullets, bold; click again to
  collapse), the old "Details" link is now a proper button that opens the release on GitHub,
  each entry shows its **download count**, and the list covers the **last 20 releases**
  (was 5 with two-line previews only).

### Fixed
- **The top bar always shows where you are** - pages opened from icon-only buttons (Report,
  Updates, or any tab collapsed to an icon via Settings → Interface) left nothing highlighted
  in the strip; the active icon now gets an accent frame and accent glyph.
- **Updates tab recovers after connection loss** - a failed fetch used to show the error once
  and never load again, even after the internet came back. The tab now retries when you open
  it again, the error message has a "Try again" button, and a background re-check runs every
  30 s while the tab stays open.
- **Settings always show the current state** - switching the language from the tray menu or
  the theme from the header button left the Settings page showing the old values; the page now
  syncs itself whenever it is shown or the app state changes.
- **A failed hardware read no longer crashes the app** - the gaming overlay refreshed the EC
  every second with no error handling, so one refused WMI call (provider restart, sleep/resume,
  shutdown) was enough to raise a .NET "unhandled exception" box. The overlay now keeps the last
  reading and tries again on the next refresh, and a last-chance handler catches anything else
  and writes it to `%AppData%\GhostDeck\errors.log` instead of showing a dialog.
- **A switched-off overlay no longer reads the hardware** - turning the gaming overlay off only
  hides its window, and the 1 s sampling timer kept running with it, so the EC was polled every
  second for the rest of the session. The timer now follows the window's visibility.

## [1.23.1] - 2026-07-27
### Changed
- **Thin GF63 12VE: fan-curve addresses verified** ([#22](../../issues/22)) - the owner confirmed
  this is a single-curve board (MSI Center exposes one fan slider, the CPU fan) and the test
  curve was found at the shipped `0x72`, so the editor's "unverified" tag is gone.
- **Single-curve boards get a single-curve editor** - on models with one controllable fan curve
  (currently the Thin GF63 12VE) the Fan curve tab shows one full-width plot instead of a dead
  GPU graph, and the dead GPU tables are never written to the EC.
- **Fan-curve wizard reports each fan separately** - finding only the CPU test curve now says
  exactly that (with the located address) instead of a blanket "not located", so single-fan
  models verify cleanly ([#22](../../issues/22)).

## [1.23.0] - 2026-07-26
### Added
- **Seven more models verified on real hardware** - thank you, reporters! Thin GF63 12VE
  ([#21](../../issues/21), incl. fan-RPM readout), Titan 18 HX Dragon Edition
  ([#23](../../issues/23) / [#24](../../issues/24), incl. RPM and a verified fan curve),
  Bravo 15 B7ED ([#25](../../issues/25)), Bravo 17 C7VE/D7VFK ([#40](../../issues/40) /
  [#41](../../issues/41), verified fan curve), GF63 Thin 11UC/11SC ([#30](../../issues/30)),
  Katana GF66 11UE/11UG ([#34](../../issues/34)), Pulse/Katana 17 B13V/GK
  ([#38](../../issues/38) / [#39](../../issues/39), verified fan curve). Fan-curve addresses
  also verified on the Cyborg 15 A12VF ([#29](../../issues/29)) and the Bravo 15 C7V
  ([#27](../../issues/27)); the AMD Bravos drop the no-op super-battery write (no `0xEB`
  register on those boards, same as the Crosshair). That's **12 tested models** now.
- **FPS & frametime of any game, driver-free** - a private ETW session listens to Windows' own
  `Present` events (the same source Intel PresentMon uses): no DLL injection, nothing touches the
  game, anti-cheat-safe. New **FPS** and **Frametime** overlay metrics (FPS on by default), and
  the monitor runs only while the overlay or the new Gaming tab is open - zero idle cost.
- **Status → Gaming sub-tab** - live FPS / frametime / 1% low / stutter boxes, a 60 s frametime
  chart with stutter markers and a median line, plus a card with the last game session.
- **Game-session report** - when a game exits: a borderless summary popup (game, duration,
  avg FPS / 1% low / max temp / fan RPM, frametime sparkline with stutter dots) pairing the FPS
  stats with the EC data that only GhostDeck sees, plus a change-history entry. The popup can
  **save itself as a PNG**, **export the session data as JSON/CSV** or jump to Status → Gaming;
  it never steals focus, can be **dragged anywhere**, and hides after a configurable time
  (Settings → Notifications: 20-60 s or "until closed") - any interaction pins it until the ✕.
- **Saved game sessions** - the last 5-50 sessions (Settings → Notifications, default 10) are
  kept on disk; Status → Gaming gets a **session picker** (newest first) with per-session
  **JSON/CSV export**.
- **History niceties** - the FPS chart is now always present (with a hint explaining it fills
  while a game runs), and hovering any History chart shows a translucent **value bubble** at
  the cursor; the frametime chart on Gaming got a legend.
- **Restore profile after wake** *(opt-in, Settings → Power)* - some ECs wake from sleep or
  hibernation in Super Battery on their own; this option re-asserts the profile you chose a few
  seconds after resume and at app start (skipped while AC/battery auto-switch is on).
- **FPS in History** - a fourth chart on Status → History and an `fps` column in the CSV/JSON
  export (`-1` = no reading). `--status` (CLI) now also reports `fps`, `frameTimeMs` and `game`.
### Fixed
- **Export button on Status → History grew a few pixels on every chart refresh** (its measured
  size fed back into itself); it is now measured against the DPI-aware canvas, which also keeps
  the label intact at 125-150% display scale.

## [1.22.0] - 2026-07-15
### Added
- **Refresh-rate auto-switch** (Settings → Power, opt-in): pick a preferred display refresh
  rate for AC and for battery (only modes your panel reports are offered) and GhostDeck applies
  it on every plug/unplug, with an OSD toast and a change-history entry. Pure Windows display
  API - no EC involved - so it works on **every** model, including unrecognised firmware
  ([#18](../../discussions/18), thanks @alibi90). `--status` now also reports `refreshHz`.
### Fixed
- **History Export button clipped** at 125-140% display scale (sizing now uses the control's
  DPI-aware preferred size).
- **Models intro said "1 tested"** regardless of reality - the count now comes live from the
  model table; the whole Models tab is also translated into all 8 languages (it was EN/PL only).

## [1.21.0] - 2026-07-15
### Added
- **Fan-curve presets** (Fan curve tab): save the current curve under a name, switch between
  presets from the editor or straight from the tray menu, rename/delete, and **export/import**
  a preset as a JSON file. A **Share…** button opens a prefilled GitHub Discussion so you can
  post your curve for others with your model/firmware attached (nothing is sent automatically).
- **Per-profile fan curve**: assign a preset to Balanced / Extreme / Super Battery and it is
  applied automatically on every profile switch made through GhostDeck (hotkey, tray, panel,
  AC/battery auto-switch). Silent deliberately stays stock - its power cap lives in the same
  EC byte a curve needs. Panic reset and profile changes made by MSI software never apply presets.
- **History sub-tab on Status**: local charts of CPU/GPU temperature and fan duty over the last
  5-60 minutes, fed by a background sampler every 3 s. Memory-only by design: nothing is written
  to disk and nothing leaves the machine.
- **Command-line interface**: `GhostDeck.exe --profile Silent`, `--cycle`, `--fanboost on|off`,
  `--overlay on|off`, `--curve <preset|auto>`, `--panic`, `--status` (JSON). Commands go to the
  running app (same safety gates as the UI) or run one-shot against the EC when it isn't running.
  Made for Task Scheduler, Stream Deck and scripts; exit codes 0/1/2.
- **History crosshair**: a tracking line with per-series dots and a "selected · now" value
  readout on the history charts; the fan RPM chart keeps at least 500 RPM of headroom.
- **History export**: the visible history window (time, profile, temps, fan duty, RPM, CPU load)
  can be saved as **CSV or JSON** for external analysis - a plain local file, nothing leaves
  the machine.
- **CLI reference**: full command documentation with the `--status` JSON schema and automation
  recipes in [docs/CLI.md](docs/CLI.md).
- **MSI Cyborg 15 A12VF promoted to Tested** - owner-verified report with full per-scenario
  dumps, fan RPM enabled at `0xC9`/`0xCB` ([#19](../../issues/19), thanks @hengeleng10-tech).
- **"Thanks" column on the Models tab**: the GitHub user whose report/verification backs each
  tested model, linking to their issue.
### Fixed
- **Updates tab horizontal scrollbar** that could appear even though everything fit the window.

## [1.20.0] - 2026-07-14

## [1.20.0] - 2026-07-14
### Added
- **Settings export / import** (Settings → Backup): save all preferences (colours, hotkeys,
  rules, overlay, alerts) to a JSON file and restore them after a reinstall or on another
  machine. Machine-specific state (firmware guard, window position) is kept local.
- **Temperature alert** (Settings → Notifications, off by default): an OSD toast and a tray
  balloon when the CPU or GPU stays above a chosen threshold (80-100 °C) for a chosen time
  (5-60 s), with a 5-minute cool-down between alerts and an entry in the change history.
- **Panic reset hotkey** (default Ctrl+Alt+F10): one press returns the machine to a safe
  stock state - Fan Boost off, Balanced profile, fans back on the automatic curve.
- **Alert threshold test steps** 70/75 °C so the temperature alert can be tried out without
  heating the laptop up first.
- **Adjustable OSD display time** (Settings → Notifications, 1-15 s, default 3): how long the
  on-screen toasts stay visible; the temperature alert always stays up at least 5 s.
### Changed
- **Internal source reorganization**: files grouped into `Core/` (hardware + app logic),
  `UI/` (tabbed window, pages, shared controls) and `Forms/` (standalone windows); the
  116 kB `MainPages.cs` split into per-class files; the dead pre-tabs settings dialog
  removed. No functional changes.

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
