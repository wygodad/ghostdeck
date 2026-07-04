# GhostDeck - FAQ

Common questions about what GhostDeck can and can't do, and how it behaves next to MSI Center.

> *Unofficial project - not affiliated with or endorsed by MSI. "MSI", "MSI Center" and "Cooler Boost" are trademarks of Micro-Star International, used here descriptively only.*

---

**Can I control the fans in profiles other than Extreme?**
Yes. GhostDeck has a **Fan curve** tab that runs a custom CPU/GPU curve on **Balanced, Extreme and Super Battery** (MSI Center only lets you in Extreme), and it's fully reversible. There's also **Fan Boost** (both fans to max) that works in any profile, via a click, a tray entry or a hotkey.
The one exception is **Silent**: on this EC the Silent power cap and the "custom curve" mode share the same byte (`0xD4`), so enabling a curve in Silent necessarily drops it to Balanced power - the app warns you first. If what you want is *quiet and low power*, that is exactly what Silent already gives you, and a curve can't beat it without giving up the cap.

**Can I set an exact wattage (a power slider / PL1 / PL2)?**
Not the way this app works - and that's actually the core reason it exists. The app doesn't set watts directly: it flips MSI's built-in EC power *modes* (the Silent / Balanced / Extreme presets), and the firmware decides the wattage for each mode. Setting an arbitrary PL1/PL2 number would mean writing Intel's power-limit registers - but on these MSI laptops those are **locked** (MSR is BIOS-locked, MMIO is overridden by Intel DTT). That's exactly why ThrottleStop and Intel XTU can't cap wattage on most of these machines either. MSI's EC doesn't expose a writable power-limit register, and the msi-ec maps don't show one for the boards I've checked - so from software, on a locked machine, a free slider isn't on the table. In practice, **"Silent" is the low-PL policy MSI removed** (it drops package power from ~100 W to ~30 W under load, verifiable in HWiNFO), so the profiles *are* your power control here.

**There is one route to a real slider, though - outside this app.** If your model lets you disable **Overclocking Lock / CFG Lock** in the hidden Advanced BIOS, the MSR power-limit registers open up, and **ThrottleStop** (or Intel XTU) can then set PL1/PL2 directly - that's your actual watt slider. Caveats: (1) on many 13th-gen MSI these BIOS options are greyed out or locked by microcode, so it's not guaranteed; (2) it's a manual, at-your-own-risk change in an unofficial BIOS menu; (3) even after the MSR is unlocked, Intel DTT can still override the limit via MMIO.

**Why don't my changes show up in MSI Center? Can I run both?**
You can run both. MSI Center caches its own UI state and doesn't live-read the EC, so it **won't reflect** changes made by anything else - but that's purely a display thing: the change still applies (GhostDeck writes the *exact same* EC bytes MSI Center writes; verify it in HWiNFO). Each app only touches the EC when you actually do something, so they don't fight over it. GhostDeck also **reads live state**, so if you switch a profile in MSI Center, GhostDeck syncs on its own. The only niche caveat: if you enable automatic AC/battery profile switching in **both** apps at once, their automations could ping-pong - but that's off by default here.

**Can it auto-clear RAM when I launch a game?**
No, and it's not planned. "Freeing" RAM (trimming working sets or the standby list) doesn't really help modern games: Windows already evicts cached pages on demand, and dumping the standby list can actually *cause* stutter as that data gets re-read. It's also outside what GhostDeck is - an EC power/fan controller, not a system/RAM tweaker.

**Is there any risk of damaging my laptop?**
Very low. The app uses MSI's **official WMI interface** (the same channel MSI Center uses), writes only the exact register values MSI's own profiles use, and EC writes are **volatile** - a reboot resets the EC to firmware defaults (nothing is flashed). On an **unrecognized firmware it stays read-only** and writes nothing. The CPU also keeps its own hardware thermal protection that no EC write can disable. Experimental models are opt-in and write only documented mode registers.

**Why does it ask for administrator (UAC)?**
EC access via WMI requires elevation. Launching manually shows one UAC prompt; the *Start with Windows* option uses an elevated scheduled task so there's **no UAC nag at every logon**.
