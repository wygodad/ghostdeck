# Keyboard lighting on MSI laptops

How GhostDeck controls keyboard backlight, what it deliberately does not control, and the
hardware evidence behind both decisions. Two different mechanisms exist, and telling them
apart is the whole point of this document:

| Keyboard type | Mechanism | GhostDeck |
|---|---|---|
| Single-colour / zone backlight | one EC register, documented per model by msi-ec | **brightness control shipped** (see `TECHNICAL.md` §42) |
| Per-key RGB (SteelSeries) | separate USB lighting controllers with their own firmware | **no control**; brightness proven unreachable, colours technically possible |

The first case is a normal EC write, no different from switching a power profile, and is
covered in `TECHNICAL.md` §42. The rest of this document is about the second case.

For the short, user-facing version of this answer see [FAQ.md](FAQ.md) - *"Can GhostDeck
control my keyboard backlight?"* and *"What about colours, effects or RGB in general?"*.

---

# Per-key RGB keyboards: what the host can and cannot control

*Research performed 2026-07-31 on a Raider GE78HX 13VH. All measurements were made with a
throwaway probe outside the application; GhostDeck itself ships no code that talks to these
devices.*

Models with a **per-key RGB keyboard** (SteelSeries, e.g. Raider GE78HX) are absent from the
keyboard-backlight map in `TECHNICAL.md` §42 because msi-ec marks their `kbd_bl` register
unsupported. This document records what was measured and read to establish what those machines
expose instead, so the question does not have to be reopened from scratch.

## 1. Device topology

A per-key RGB machine presents **three** independent USB devices under vendor `0x1038`
(SteelSeries), verified on a Raider GE78HX 13VH:

| PID | Product string | Role |
|---|---|---|
| `0x2050` | SteelSeries Gaming Keyboard | key input (typing, Fn row, consumer keys) |
| `0x113A` | SteelSeries KLC | Keyboard Lighting Controller (per-key LEDs) |
| `0x114D` | SteelSeries ALC | Ambient Lighting Controller (light bar) |

`0x113A` and `0x114D` sit on the same internal hub; `0x2050` is separate. Each exposes a vendor
HID collection on usage page `0xFFC0` with 65-byte input, 65-byte output and a 525-byte feature
report, plus a consumer-control collection on usage page `0x000C`.

The KLC report descriptor (identical on the ALC) declares **no report IDs** and exactly one
feature report of 524 data bytes:

```
06 C0 FF 09 01 A1 01 06 C1 FF 15 00 26 FF 00 75 08
09 F0 95 40 81 02   ; usage 0xF0, 64 bytes, Input
09 F1 95 40 91 02   ; usage 0xF1, 64 bytes, Output
09 F2 96 0C 02 B1 02 ; usage 0xF2, 0x020C = 524 bytes, Feature
C0
```

## 2. The protocol that is known and verified

Reverse-engineered by the OpenRGB project (branch `steelseries-aps`, merge request !2740,
still draft) and by `msi-perkeyrgb` for the predecessor PID `0x1122`. Three opcodes exist:

| Opcode | Channel | Meaning |
|---|---|---|
| `0x0E` | 525-byte feature report | set per-key colours |
| `0x09` | 65-byte output write | apply / commit |
| `0x10` | 65-byte output write, then read | query keyboard layout |

Colour packet layout: `buf[1]=0x0E`, `buf[3]` = LED count in this packet, then 12-byte entries
from offset 5: `R,G,B` at `+0..+2`, a per-key mode byte at `+9` (OpenRGB hardcodes `0x01`), the
LED id at `+11`. Entry byte `+8` is an effect-slot id that vendor software sets and OpenRGB
leaves at zero. Up to 0x1E LEDs per packet; the `0x09` apply follows the last one.

**Verified on real hardware (2026-07-31):** the layout query was sent to the KLC and answered
`0x0E`, which is exactly the value OpenRGB documents for PID `0x113A`, entry
*"US ansi 13VH-438US"*, i.e. this laptop. The transport, framing and identity of the protocol
are therefore confirmed, not merely assumed.

## 3. Brightness: not reachable from the host

The Fn+F8 key cycles **five** brightness levels (one of which is fully off) and changes the
keyboard and the light bar simultaneously. Everything below was measured:

1. **Not the EC.** With the EC live view (`TECHNICAL.md` §45) open, pressing Fn+F8 changed no EC byte; only
   sensor registers moved. Consistent with msi-ec, which sets `bl_state_address` to unsupported
   for RGB configurations.
2. **No HID notification.** The vendor collections of all three devices were opened and read for
   over an hour across four sessions, with SteelSeries GG running and fully closed, while the
   key was pressed dozens of times. Not one input report arrived.
3. **Feature reads return zeros.** `HidD_GetFeature` on the 525-byte feature report yields an
   all-zero block.
4. **No brightness opcode exists in any implementation.** OpenRGB's ApS controller, the QCK Mat
   controller sharing the same `0x0E` framing, `msi-perkeyrgb`, its effects fork and
   `omarchy-msi-rgb` all implement "brightness" as software scaling of the RGB values before
   sending them. `msi-perkeyrgb` issue #39, *"Brightness Adjustment and Disable Backlight"*, has
   been open with no reply.
5. **The vendor software does not expose it either.** SteelSeries GG's MSI per-key panel offers
   configurations and effects but no brightness control, and the captured GG traffic implements
   its "Disable" preset by writing `R=G=B=0` in a normal colour packet rather than by any
   dedicated command.

Conclusion: the five levels are firmware state inside the keyboard controller, driven by its own
key matrix, and no documented host-sendable command sets them. SteelSeries does use a real
standalone brightness opcode on other product lines (Apex 8-Zone and Aerox: `0x23` to set,
`0xA3` to read, and the OpenRGB protocol notes state it is the same value the hardware brightness
keys change), but nothing connects that opcode to the KLC/ALC firmware.

## 4. Why blind opcode probing is refused

`msi-perkeyrgb` issue #24 (open, marked critical): on an MSI GE65 Raider with a SteelSeries KLC
(`1038:1122`, the direct predecessor of `0x113A`), a single malformed effect packet permanently
killed the keyboard backlight. The device disappeared from `lsusb` until a full power cycle and
never lit again; reflashing the UEFI firmware per MSI's instructions did not recover it.

Additionally, the dangerous opcodes in this vendor's protocol families sit in the low range next
to the ones already known here: in SteelSeries' own firmware-update protocol `0x01` is reset with
`0x01 0x01` entering the bootloader, `0x02` erases a file and `0x03` writes one. The same byte can
mean opposite things across product lines: `0xA3` reads brightness on Apex hardware but is
`WriteChunk` in the gamepad protocol. Sweeping opcodes on a machine in daily use is therefore
not an acceptable experiment, and GhostDeck does not ship any code that talks to these devices.

## 5. What would be safe and feasible

Setting **per-key colours** through the documented `0x0E` + `0x09` sequence is well-established
and is what every existing tool does. It writes no persistent state, it is fully reversible by
re-applying a configuration in SteelSeries GG, and it uses opcodes whose meaning is confirmed on
this exact hardware. That makes GhostDeck-driven lighting (for example a keyboard colour per
power profile, or a colour set by a scene) a realistic future feature, unlike brightness. The
cost is that writing colours replaces whatever effect the vendor software is running until the
user re-applies it, so it must be an explicit opt-in rather than something that happens silently.

## 6. Sources

- OpenRGB, branch `steelseries-aps` (Morgan Guimard), MR !2740: ApS ALC/KLC controller.
- OpenRGB issue #4625 (SteelSeries APS devices) and #2642 (MSI GE76), the latter with USB captures.
- OpenRGB master: `SteelSeriesApex8ZoneController` (brightness `0x23`/`0xA3`),
  `SteelSeriesApexTZoneController`, `SteelSeriesAerox*`, `SteelSeriesQCKMatController`.
- `Askannz/msi-perkeyrgb`, including issue #24 (permanent failure) and issue #39 (brightness request).
- `Gibtnix/MSIKLM` and `ed10vi/msi-steelseries-led` for the older `1770:FF00` region-based boards,
  whose brightness byte is part of a palette command and does not apply to this silicon.
- `flozz/rivalcfg` for independent confirmation of the SteelSeries brightness opcode and ranges.
- Local measurements: HID enumeration, four passive listening sessions, feature reads and the
  layout query, all performed with a throwaway probe outside the application.

**Caveat for anyone repeating this:** Windows opens keyboard and consumer-control collections
exclusively for its own class driver, so a user-mode read of those nodes returns nothing no
matter what the device sends. Only the vendor collections (`0xFFC0`) can be observed this way;
observing the rest requires Raw Input with `RIDEV_INPUTSINK` or a USB capture driver.
