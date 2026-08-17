# Third-Party Notices

GhostDeck is an independent project and is not affiliated with, endorsed by, or connected to
Micro-Star International Co., Ltd. (MSI). "MSI" and product names are used descriptively only.

This file records where GhostDeck's hardware knowledge came from. GhostDeck controls MSI
laptop hardware through registers that the vendor does not document publicly, so much of what
the application knows about specific machines was learned from community projects that
published their findings. Those projects are credited below, together with the upstream
material referenced and how that information was used in GhostDeck.

GhostDeck does not copy or link upstream source files from the projects listed below. It
references hardware mapping information documented by those projects: register addresses,
mode values, and which firmware identifiers they apply to.

---

## msi-ec

| | |
|---|---|
| **Project** | BeardOverflow/msi-ec, a Linux kernel driver for MSI laptop embedded controllers |
| **Upstream** | https://github.com/BeardOverflow/msi-ec |
| **Source file referenced** | `msi-ec.c` |
| **License notice in that file** | `SPDX-License-Identifier: GPL-2.0-or-later` |
| **Repository license** | GPL-2.0 |

The license identifier above is the one carried by the specific file GhostDeck references.
Other files in that repository may carry different or no per-file notices.

### What was used, per structure

**Device family support (`Core/Devices.cs`, `DeviceProfiles`)**

GhostDeck's MSI device support was built in part using hardware mapping information documented
by msi-ec: the classification of firmware identifiers into EC families, and the family-level
register addresses and mode values. Most supported machines fall into a small number of shared
families rather than having individually documented layouts.

| Added | Entries | Commit | First release |
|---|---:|---|---|
| Initial seed, "Gaming Intel" models | 7 | `4599552` | v1.2.0 |
| Bulk import | 127 | `e9336c3` | v1.8.0 |
| From the first weekly upstream diff | 10 | `e546ec7` | v1.26.0 |

Model designations for imported entries follow the naming used in msi-ec's firmware lists.

GhostDeck independently implements its scenario model, control logic, safety gating,
validation tiers and hardware-specific extensions. This includes the mapping of individual EC
axes into GhostDeck's four scenarios, the power-cap co-flag at `0x34`, per-scenario values
obtained from user hardware captures, fan-curve tables and verification, fan RPM addresses,
the opt-in gate for unverified machines, and the power-comparison test.

**Keyboard backlight level (`KbdBacklightMap`)**

82 firmware-prefix mappings, generated from msi-ec's per-configuration `kbd_bl` blocks.
Introduced in `236fd47`, first released in v1.25.0.

**Fn / Windows key swap (`FnWinSwapMap`)**

162 firmware-prefix mappings, generated from msi-ec's per-configuration `fn_win_swap` blocks.
Introduced in `e546ec7`, first released in v1.26.0.

**Webcam control exceptions (`NoWebcamCtrl`)**

3 firmware prefixes, based on the boards msi-ec annotates as having no hardware webcam control.
Introduced in `236fd47`, first released in v1.25.0.

**Upstream change watchdog (`tools/msiec-fw-baseline.txt`)**

372 firmware identifiers, used by `tools/msiec-sync.py` to detect changes in upstream msi-ec.
This is development tooling and is not shipped to users. Introduced in `badbdc0`.

---

## MControlCenter

| | |
|---|---|
| **Project** | dmitry-s93/MControlCenter, a Linux GUI for MSI laptop settings |
| **Upstream** | https://github.com/dmitry-s93/MControlCenter |
| **Source file referenced** | `src/operate.cpp` |
| **License notice in that file** | GNU General Public License, version 3 or (at your option) any later version |
| **Repository license** | GPL-3.0 |

### What was used

Fan and temperature register layouts were cross-checked against MControlCenter: the CPU
temperature and fan tables at `0x6A` / `0x72` and the GPU tables at `0x82` / `0x8A`.

For device and firmware coverage MControlCenter is not an independent source: since its
version 0.5.0 it states that device support depends on the msi-ec kernel driver.

---

## Origin and validation are tracked separately

Entries in GhostDeck carry two independent properties, and later verification does not change
where an entry originally came from.

- **Origin** is where the mapping was first learned: an upstream project, a user hardware
  report, or GhostDeck's own measurement.
- **Validation** is whether the mapping has since been confirmed on physical hardware through
  GhostDeck's own process. Validated entries are marked `Tested` in the application; entries
  that have not been confirmed remain `Experimental` and are opt-in, with the application
  staying read-only on unrecognised firmware.

Entries that were verified on physical hardware credit the person who supplied or confirmed
the data, in the device table and in the changelog.

---

## Other third-party components

### .NET components (bundled)

The GhostDeck executable is published as a framework-dependent single file. It requires the .NET 8
Desktop Runtime, installed separately by the user from Microsoft, and does not carry that
runtime inside itself.

| Bundled component | License |
|---|---|
| `System.Management` (WMI access) | MIT |
| Native application host (`apphost`) | MIT |

No `coreclr.dll`, .NET runtime or Windows Forms libraries are included in the executable.
The MIT text is in `licenses/dotnet-MIT-LICENSE.txt`, and Microsoft's breakdown of which .NET
binaries fall under which license is in `licenses/dotnet-license-information-windows.md`.

GhostDeck implements no analytics, usage tracking or telemetry service of its own.

### Fonts

Icon glyphs are drawn from Segoe MDL2 Assets and Segoe Fluent Icons, the icon fonts installed
with Windows. **No font file is redistributed with GhostDeck**; the glyphs are rendered from
the fonts already present on the system, and GhostDeck runs only on Windows.

---

## Reporting a problem with this notice

If you believe any attribution or provenance information in this file is inaccurate or
incomplete, please open an issue at https://github.com/wygodad/ghostdeck/issues.
