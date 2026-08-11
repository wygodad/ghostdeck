# The MSI WMI schema: what GhostDeck needs from Windows, and the one thing a fresh Windows is missing

> *Unofficial project - not affiliated with or endorsed by MSI. "MSI" and "MSI Center" are
> trademarks of Micro-Star International, used here descriptively only.*

This document explains how GhostDeck reaches the hardware, which system component that path
depends on, and why a freshly installed Windows shows "unsupported" until MSI Center has been
installed once. It comes out of the investigation in
[discussion #56](https://github.com/wygodad/ghostdeck/discussions/56), with every claim below
either measured on real hardware or backed by a public source.

## The three layers

Hardware access is a chain of three independent layers:

```
GhostDeck.exe  (user mode, admin)
   |  System.Management (WMI)
   v
A. SCHEMA          the DESCRIPTION of the MSI_ACPI class and its methods
   |               currently: a compiled MOF resource inside msiapcfg.dll
   v
B. TRANSPORT       wmiacpi.sys - Microsoft's built-in ACPI-WMI driver
   |               (device ACPI\PNP0C14; no MSI component in this layer)
   v
C. IMPLEMENTATION  the laptop's firmware: ACPI _WDG table + AML methods
   |               (what the board actually supports)
   v
Embedded Controller
```

The layers really are independent. A machine can have the schema without the firmware
implementation (then every call returns `NotSupported` -
[issue #48](https://github.com/wygodad/ghostdeck/issues/48) is exactly that case), and a
machine can have a fully working firmware without the schema - then the `MSI_ACPI` class
does not exist in Windows at all, and no user-mode software can call it. That second case
is what a fresh Windows install looks like, and it is what discussion #56 reported.

## Where the schema comes from

Windows needs a machine-readable description (a compiled MOF, "BMOF") before it will expose
an ACPI-WMI interface as a WMI class. Microsoft documents three places it can come from, in
priority order: a DLL named by the `MofImagePath` registry value under the `WmiAcpi` service
key, a resource inside `wmiacpi.sys` itself, or a BMOF embedded in the firmware.

MSI uses only the first: a small resource-only DLL, `msiapcfg.dll`, Authenticode-signed by
Micro-Star International. It describes 17 classes (`MSI_ACPI`, `MSI_CPU`, `MSI_VGA`,
`MSI_Power`, the `Package_*` buffer types and others). The "MSI NBFoundation Service"
package, which installs together with MSI Center, deploys it to `C:\Windows\SysWOW64\` and
sets `HKLM\SYSTEM\CurrentControlSet\Services\WmiAcpi\MofImagePath` to point at it. From that
moment Windows' own `wmiacpi.sys` publishes the classes and keeps doing so on every boot.

MSI does **not** embed this schema in the firmware (confirmed independently by the
[msi-ec](https://github.com/BeardOverflow/msi-ec/discussions/98) reverse-engineering work),
and MSI offers **no standalone download** of the component for current laptop models - it
ships only with MSI Center.

## What this means in practice

- **Fresh Windows install:** the `MSI_ACPI` class does not exist, so GhostDeck cannot reach
  the EC and reports the machine as unsupported. **Installing MSI Center once deploys the
  schema and GhostDeck works from then on.** No reboot of GhostDeck's own logic is involved;
  the class simply appears.
- **MSI Center does not need to run.** Measured on the development machine (2026-08-11):
  GhostDeck stays fully functional with every MSI service stopped.
- **A one-time installation is enough.** Also measured: after uninstalling MSI Center, its
  SDK and the NBFoundation Service completely (zero MSI software left, services deregistered),
  the deployed DLL, the registry value and the registered classes remain, and GhostDeck keeps
  working across reboots. Even MSI's own "MSI Center Cleaner Master" cleanup tool leaves them
  in place. Reinstalling MSI Center later restores the full package cleanly. (All of this was
  measured on one machine, a Raider GE78HX; treat the uninstall part with that caveat.)
- Residual note: with all MSI software removed there is no service left watching the file,
  so a future major Windows upgrade or WMI repository rebuild could in principle orphan the
  schema. The fix is the same one-time MSI Center installation.

## Why GhostDeck does not deploy the schema itself

Technically it could: the mechanism is a documented Microsoft standard, and at least one
open-source project (HandheldCompanion, for the MSI Claw) bundles this exact DLL, writes
`MofImagePath` and restarts the ACPI-WMI device to make the classes appear. GhostDeck
deliberately does not do this:

- redistributing an MSI-signed system component raises licensing questions the project
  cannot resolve;
- it would mean writing to `HKLM` and `SysWOW64` and taking on the role of an installer of
  vendor system components, which is against the project's character (GhostDeck installs
  nothing system-wide);
- the supported fix is trivial and official: install MSI Center once.

GhostDeck's role here is diagnosis: telling you clearly that the hardware interface exists
but the schema is missing, instead of a generic "unsupported".

## What stays true about safety

The security story is unchanged by any of this. GhostDeck installs and bundles **no kernel
driver** (no WinRing0, no NTIOLib, no MsIo64), and disables no Windows security feature. The
transport layer is Microsoft's own `wmiacpi.sys`, present in every Windows installation. The
MSI schema file is a passive data resource - a class description, not executable driver code.

## References

- [Discussion #56](https://github.com/wygodad/ghostdeck/discussions/56) - the report and
  investigation this document comes from
- [Issue #48](https://github.com/wygodad/ghostdeck/issues/48) - the mirror case: schema
  present, firmware implementation absent
- Microsoft: [Publishing a WMI Schema](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/publishing-a-wmi-schema),
  [Setting the MofImagePath Registry Value](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/setting-the-mofimagepath-registry-value),
  [WMI ACPI Sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/wmi-acpi-sample)
- [BeardOverflow/msi-ec discussion #98](https://github.com/BeardOverflow/msi-ec/discussions/98) -
  firmware does not carry the BMOF
- [Linux kernel: msi-wmi-platform](https://docs.kernel.org/wmi/devices/msi-wmi-platform.html) -
  the same interface documented from the kernel side
- [Valkirie/HandheldCompanion](https://github.com/Valkirie/HandheldCompanion) - the project
  that bundles and deploys the DLL for the MSI Claw
