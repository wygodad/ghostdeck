# GhostDeck - FAQ

Common questions about what GhostDeck can and can't do, and how it behaves next to MSI Center.

> *Unofficial project - not affiliated with or endorsed by MSI. "MSI", "MSI Center" and "Cooler Boost" are trademarks of Micro-Star International, used here descriptively only.*

---

## Can I control the fans in profiles other than Extreme?

Yes. GhostDeck has a **Fan curve** tab that runs a custom CPU/GPU curve on **Balanced, Extreme and Super Battery** (MSI Center only lets you in Extreme), and it's fully reversible. There's also **Fan Boost** (both fans to max) that works in any profile, via a click, a tray entry or a hotkey.

The one exception is **Silent**: on this EC the Silent power cap and the "custom curve" mode share the same byte (`0xD4`), so enabling a curve in Silent necessarily drops it to Balanced power - the app warns you first. If what you want is *quiet and low power*, that is exactly what Silent already gives you, and a curve can't beat it without giving up the cap.

## Can I set an exact wattage (a power slider / PL1 / PL2)?

Not the way this app works - and that's actually the core reason it exists. The app doesn't set watts directly: it flips MSI's built-in EC power *modes* (the Silent / Balanced / Extreme presets), and the firmware decides the wattage for each mode. Setting an arbitrary PL1/PL2 number would mean writing Intel's power-limit registers - but on these MSI laptops those are **locked** (MSR is BIOS-locked, MMIO is overridden by Intel DTT). That's exactly why ThrottleStop and Intel XTU can't cap wattage on most of these machines either. MSI's EC doesn't expose a writable power-limit register, and the msi-ec maps don't show one for the boards I've checked - so from software, on a locked machine, a free slider isn't on the table. In practice, **"Silent" is the low-PL policy MSI removed** - on the boards where that cap is real it drops package power from ~100 W to ~30 W under load, verifiable in HWiNFO - so the profiles *are* your power control here. On some models Silent only slows the fans; see [Does Silent lower power on every laptop?](#does-silent-lower-power-on-every-laptop) for how to tell which one you have.

**There is one route to a real slider, though - outside this app.** If your model lets you disable **Overclocking Lock / CFG Lock** in the hidden Advanced BIOS, the MSR power-limit registers open up, and **ThrottleStop** (or Intel XTU) can then set PL1/PL2 directly - that's your actual watt slider. Caveats: (1) on many 13th-gen MSI these BIOS options are greyed out or locked by microcode, so it's not guaranteed; (2) it's a manual, at-your-own-risk change in an unofficial BIOS menu; (3) even after the MSR is unlocked, Intel DTT can still override the limit via MMIO.

## Why don't my changes show up in MSI Center? Can I run both?

You can run both. MSI Center caches its own UI state and doesn't live-read the EC, so it **won't reflect** changes made by anything else - but that's purely a display thing: the change still applies (GhostDeck writes the *exact same* EC bytes MSI Center writes; verify it in HWiNFO). Each app only touches the EC when you actually do something, so they don't fight over it. GhostDeck also **reads live state**, so if you switch a profile in MSI Center, GhostDeck syncs on its own. The only niche caveat: if you enable automatic AC/battery profile switching in **both** apps at once, their automations could ping-pong - but that's off by default here.

## GhostDeck worked before, but after a clean Windows install it says "unsupported". Is my laptop no longer supported?

Your laptop is fine - a freshly installed Windows is just missing one piece. Windows needs a small description file (the **MSI WMI schema**, `msiapcfg.dll`) before it will expose MSI's hardware interface as the WMI class GhostDeck talks to, and MSI ships that file **only with its own software** - it is not in the firmware and there is no standalone download.

**The fix: install MSI Center once.** The schema is deployed during installation and the interface appears; after that you can keep MSI Center or uninstall it - measurements show the schema stays behind (it even survives MSI's own cleanup tool), and GhostDeck never needs MSI Center running. GhostDeck deliberately does not deploy the file itself: it is an MSI-signed system component, and redistributing it is not the project's call to make. Full write-up with sources and measurements: [MSI-WMI-SCHEMA.md](MSI-WMI-SCHEMA.md); the original report: [discussion #56](../../../discussions/56).

## Can GhostDeck control my keyboard backlight? Why not on my laptop?

It depends on which of the two backlight designs your machine has, and the difference is in the hardware, not in the app.

**Single-colour or zone backlight.** Brightness is one register in the Embedded Controller, exactly like a power profile, and msi-ec documents it per model. GhostDeck ships that control: off / low / mid / high as a tile on the Scenarios tab, an assignable hotkey (`Ctrl+Alt+F6`, disabled by default), `--kbd` on the command line and a field in scenes. It follows your Fn key, so both stay in sync. Around 82 firmware families are covered; if yours is one of them, the tile simply appears.

**Per-key RGB backlight** (SteelSeries, e.g. Raider GE78HX). The tile does not appear, and that is deliberate. On these machines the keyboard is a **separate device with its own processor**: the Fn brightness key is handled inside that firmware and never tells Windows anything. This was measured, not assumed - the key changes no EC byte, sends no report on any interface with SteelSeries GG both running and closed, and reading the controller's state returns nothing. Tellingly, **SteelSeries' own software has no brightness control either**, and no tool in the world implements one. The only way to try would be to send undocumented commands to the keyboard, and there is a documented case of exactly that permanently killing a laptop's backlight, which reflashing the BIOS did not repair. We are not willing to risk your keyboard for a feature your Fn key already performs perfectly. The full evidence is in [LIGHTING.md](LIGHTING.md).

## What about colours, effects or RGB in general?

Not supported, and it is a stated non-goal of the project. GhostDeck is a power, thermal and fan tool; RGB is a large, per-model, per-keyboard-generation problem with excellent dedicated software already available (SteelSeries GG or MSI Center on Windows, OpenRGB cross-platform). Setting colours on per-key keyboards *is* technically within reach - the protocol is confirmed on real hardware - so ideas like "keyboard colour follows the active profile" sit on the roadmap as a possible future extra. But it would always be opt-in, because writing colours replaces whatever effect your lighting software is running until you re-apply it.

## Can GhostDeck switch the MUX - discrete-GPU direct mode ("独显直连")?

Not yet - and the mechanism deserves a real explanation, because the question keeps coming back (first asked in [#70](../../../issues/70), again in [discussion #88](../../../discussions/88)).

**What the switch actually is.** Discrete / Hybrid on MSI machines is a hardware **MUX** (multiplexer): it physically routes the laptop panel's signal path either through the integrated GPU (hybrid) or straight to the discrete one. MSI documents mode changes as reboot-to-apply ([MSI FAQ](https://us.msi.com/faq/8805), [MSI's MUX explainer](https://uk.msi.com/blog/what-is-mux-switch-what-It-can-do-for-you)), and the reboot is inherent, not laziness: which GPU owns the panel is negotiated between the firmware and the graphics drivers during platform init, so re-routing the panel needs one - on every brand's manual MUX.

**"But ASUS switches without a reboot"** - that is a different mechanism, not a faster MUX. G-Helper's instant Eco/Standard toggle powers the discrete GPU off and on while **staying** in hybrid routing, so no display re-routing happens; its "Ultimate" mode - the actual ASUS MUX - requires a reboot exactly like MSI's ([G-Helper docs](https://deepwiki.com/seerge/g-helper/3.2-gpu-mode-management), [Linux asus-wmi patch](https://lkml.rescloud.iu.edu/2208.1/05680.html)). The only true no-reboot display switching is NVIDIA **Advanced Optimus** - a driver-plus-panel feature on specifically wired laptops, not something a third-party app can trigger.

**Where GhostDeck stands.** The toggle does not live in the Embedded Controller register space this project works in, and none of the community register maps this app builds on (msi-ec, MControlCenter) document it - MSI Center flips it through a different, undocumented mechanism of its own and then asks for the reboot. Writing guesses into hardware is the one thing this project refuses to do, so for a long time the honest answer was "out of scope". That answer is outdated in one respect: **reverse-engineering work on this exact mechanism has been underway here for some time.** It is too early for details or dates - but the topic is no longer closed. Until then, set the mode in MSI Center if you need it; GhostDeck does not touch that mechanism, so the two do not conflict.

## Can it auto-clear RAM when I launch a game?

No, and it's not planned. "Freeing" RAM (trimming working sets or the standby list) doesn't really help modern games: Windows already evicts cached pages on demand, and dumping the standby list can actually *cause* stutter as that data gets re-read. It's also outside what GhostDeck is - an EC power/fan controller, not a system/RAM tweaker.

## I turned on the temperature icons in the tray and nothing appeared

They are there, Windows just hid them. Windows 11 puts every newly registered notification icon into the hidden overflow area (the `^` arrow next to the clock) until you say otherwise. Click the arrow, then drag the temperature icons down onto the taskbar and they stay there. The same happens to the GhostDeck ghost icon on a fresh install. If the overflow area has no temperature icons at all, check Settings -> System, card "Temperature in the tray": the card is hidden entirely on machines whose temperatures the app cannot read.

## Does Silent lower power on every laptop?

No, and it is worth knowing which kind of machine you have.

Silent writes one byte (`0xD4 = 0x1D`). What the firmware does with it differs by board. On a Raider
GE78HX the profile is a real power policy: package power drops from ~100 W to ~30 W under load and the
machine is measurably slower. On an MSI Sword 16 HX B13V, an owner's power test measured the CPU doing
**the same work in Silent as in Balanced** at the same clocks - the two differed by 0.04 %, against a
second-to-second variation of about 2 % inside each phase - while only the fans came down (3053 vs
3665 rpm) and the CPU ran 3 °C cooler. Same byte, same app, different firmware behaviour.

Neither is a fault, and nothing is being written differently. It matters because it tells you what to
expect: on a "power" board Silent buys quiet by giving up speed, on a "fan-only" board it buys quiet
for free.

One limit of the method is worth stating: each profile is held for 60 s and the last 25 s are averaged,
so a cap that only tightened after several minutes would not show up. What the test does prove on the
spot is that it *can* see a difference - in that same run Extreme came out 12 % ahead, far outside the
noise.

**How to tell, in about five minutes:** tray menu → **Report / verify** → **Power test**. It runs the
same all-core load in Silent, Balanced and Extreme and prints the work each profile completed. If the
Silent row does the same work as Balanced, your board is the fan-only kind. The report says so in as
many words, and it measures Balanced twice so you can see whether the machine simply got hot during
the run.

## The fan speed shows "--" instead of a percentage or RPM. Is it broken?

Usually not, and MSI Center does the same thing on the same machine. Two separate causes:

**The fan is not spinning.** On a cool laptop the firmware stops a fan completely - most often the GPU fan on battery or at idle. A stopped fan has no speed to report: the controller returns nothing, so the app shows "--" rather than inventing a zero. Load the machine for a minute and both numbers come back. A discrete GPU that has powered down also reports no temperature, which is why its whole row can read "--" at once.

**The fan is spinning slower than the register can express.** The tachometer register does not hold RPM, it holds a divisor: RPM = 478000 / value, in a single byte. The lowest speed that can be expressed at all is therefore 478000/255 = **1874 RPM** - below that, whatever sits in the register is not a reading. GhostDeck used to divide it anyway and reported speeds around 9958 RPM (issue #92); since v1.34.0 anything above 8000 RPM - well past the fastest fan ever logged on any model, 7206 - is treated as no reading and shown as "--".

If a fan is audibly roaring and still shows "--", that is worth reporting: open an issue with your model, firmware and what MSI Center or HWiNFO64 shows at that moment.

## Is there any risk of damaging my laptop?

Very low. The app uses MSI's **official WMI interface** (the same channel MSI Center uses), writes only the exact register values MSI's own profiles use, and EC writes are **volatile** - a reboot resets the EC to firmware defaults (nothing is flashed). On an **unrecognized firmware it stays read-only** and writes nothing. The CPU also keeps its own hardware thermal protection that no EC write can disable. Experimental models are opt-in and write only documented mode registers.

## My antivirus / VirusTotal flags GhostDeck.exe - is it malware?

No - but the flag is understandable, and here is how to verify it yourself. GhostDeck ticks several boxes that antivirus heuristics dislike: it's a self-contained single-file exe (it self-extracts the .NET runtime), asks for administrator rights, talks to the Embedded Controller, registers global hotkeys and can update itself. Occasionally a single engine (typically a small one, via a generic "W32.Malware.*" heuristic name) flags it on VirusTotal while all the major engines stay clean - a classic false positive pattern.

Since **v1.24.0 every release is digitally signed**: right-click the exe → Properties → **Digital Signatures** should show **"WYGODA DAWID FENIX INSPIRE"** (the developer's registered business) with a valid timestamp. A correct signature proves the file is an untampered official build; signing also gradually builds SmartScreen reputation, so "unknown publisher" warnings fade over time. Releases before 1.24.0 were unsigned.

Additional checks: compare the SHA-256 with the asset on the [Releases](../../../releases) page (`certutil -hashfile GhostDeck.exe SHA256` in a terminal) - every release is built from the public source by GitHub Actions, so the code that produced the exe is fully auditable, and you can always build it yourself (see the README). If a file claiming to be GhostDeck has no signature (v1.24.0+) or a *majority* of engines flag it, don't run it and tell us - that would not be our build.

## Why does it ask for administrator (UAC)?

EC access via WMI requires elevation. Launching manually shows one UAC prompt; the *Start with Windows* option uses an elevated scheduled task so there's **no UAC nag at every logon**.
