#!/usr/bin/env python3
"""msi-ec sync (stage 1): compare upstream msi-ec model maps against Core/Devices.cs.

Reads the upstream msi-ec.c (the community-maintained Linux driver whose per-model EC
register maps our Devices.cs was seeded from), parses every CONF_* block and its
ALLOWED_FW_* firmware list, and diffs the result against our device table. Emits a
markdown report with four sections:

  (a) new prefixes ready to import (their conf documents a Silent fan value) with
      ready-to-paste C# lines - always Tier.Experimental;
  (b) new prefixes in confs WITHOUT a Silent fan value - these need a human design
      decision (our profile detection keys off the Silent fan byte), never a blind import;
  (c) address mismatches between our entries and upstream (shift / fan / charge) -
      the early-warning system for upstream corrections;
  (d) firmware version strings not yet in tools/msiec-fw-baseline.txt (informational).

The script only REPORTS. It never edits Devices.cs; changes reach users exclusively
through a normal reviewed commit + release. Run by .github/workflows/msiec-sync.yml
weekly, or by hand:

  python tools/msiec-sync.py                      # fetch upstream, print report
  python tools/msiec-sync.py --report report.md   # also write report.md (only if diff)
  python tools/msiec-sync.py --source msi-ec.c    # use a local copy (tests)
  python tools/msiec-sync.py --update-baseline    # accept current fw list as baseline

Exit codes: 0 = no differences, 10 = differences found (report emitted), 2 = parse
failure (upstream layout changed - the parser refuses to guess).
"""

from __future__ import annotations

import argparse
import re
import sys
import urllib.request
from pathlib import Path

UPSTREAM_URL = "https://raw.githubusercontent.com/BeardOverflow/msi-ec/main/msi-ec.c"
REPO_ROOT = Path(__file__).resolve().parent.parent
DEVICES_CS = REPO_ROOT / "Core" / "Devices.cs"
BASELINE = REPO_ROOT / "tools" / "msiec-fw-baseline.txt"

# Our DeviceProfile property defaults (Core/Devices.cs) - entries that do not set a
# field explicitly use these.
OUR_DEFAULTS = {"shift": 0xD2, "fan": 0xD4, "charge": 0xD7}

# Known, deliberate divergences from upstream (documented in docs/TECHNICAL.md);
# prefixes listed here are excluded from the mismatch section.
MISMATCH_WAIVERS: set[str] = set()

# Address pairs treated as equivalent (ours, upstream). msi-ec standardises battery charge
# control on 0xEF across every conf; on the G2 family we write 0xD7 (0x80|percent), verified
# on real hardware (GE78HX and other Tested models) - both work, not a divergence to chase.
CHARGE_EQUIV: set[tuple[int, int]] = {(0xD7, 0xEF)}

# No-Silent prefixes already known and tracked (the 2026-07-28 review; see the tracking
# issue and TECHNICAL §36). Hidden from the weekly report so it stays quiet; remove entries
# here once the no-Silent handling design lands and they get imported. --include-acked shows them.
NOSILENT_ACK: set[str] = {
    "16P5EMS1", "16U7EMS1", "1782EMS1", "17E7EMS1", "17E8EMS1", "17F2EMS1", "17F3EMS1",
    "17F3EMS2", "17F4EMS2", "17F5EMS1", "17F6EMS1", "17G1EMS1", "17G1EMS2", "17G3EMS1",
}


class ParseError(Exception):
    pass


def fetch_source(source: str | None) -> str:
    if source and Path(source).exists():
        return Path(source).read_text(encoding="utf-8", errors="replace")
    url = source or UPSTREAM_URL
    with urllib.request.urlopen(url, timeout=30) as r:  # noqa: S310 - fixed upstream URL
        return r.read().decode("utf-8", errors="replace")


def brace_block(text: str, start: int) -> str:
    """Return the {...} block starting at the first '{' at/after start."""
    i = text.index("{", start)
    depth = 0
    for j in range(i, len(text)):
        if text[j] == "{":
            depth += 1
        elif text[j] == "}":
            depth -= 1
            if depth == 0:
                return text[i : j + 1]
    raise ParseError("unbalanced braces")


def parse_msiec(src: str) -> dict:
    """-> {prefix: {conf, names, fws, shift, fan, charge, super_batt, fan_modes, shift_modes}}"""
    # 1) firmware arrays, keeping the "// Model name" comments next to entries
    fw_arrays: dict[str, list[tuple[str, str]]] = {}
    for m in re.finditer(r"ALLOWED_FW_(\w+)\s*\[\]\s*__initconst\s*=\s*\{(.*?)\};", src, re.S):
        entries = []
        for line in m.group(2).splitlines():
            fw = re.search(r'"([^"]+)"', line)
            if not fw:
                continue
            comment = re.search(r"//\s*(.+?)\s*$", line)
            entries.append((fw.group(1), comment.group(1) if comment else ""))
        fw_arrays[m.group(1)] = entries
    if not fw_arrays:
        raise ParseError("no ALLOWED_FW_* arrays found")

    # 2) conf structs
    confs: dict[str, dict] = {}
    for m in re.finditer(r"static struct msi_ec_conf CONF_(\w+)\s", src):
        name = m.group(1)
        block = brace_block(src, m.end())

        def sub(field: str) -> str:
            i = block.find(f".{field}")
            if i < 0:
                return ""
            try:
                return brace_block(block, i)
            except (ParseError, ValueError):
                return ""

        def addr(field_block: str) -> int | None:
            a = re.search(r"\.address\s*=\s*(0x[0-9a-fA-F]+)", field_block)
            return int(a.group(1), 16) if a else None

        fw_ref = re.search(r"\.allowed_fw\s*=\s*ALLOWED_FW_(\w+)", block)
        if not fw_ref:
            raise ParseError(f"CONF_{name}: no allowed_fw")
        shift_b, fan_b = sub("shift_mode"), sub("fan_mode")
        # charge control is a plain scalar field, not a struct
        charge = re.search(r"\.charge_control_address\s*=\s*(0x[0-9a-fA-F]+)", block)
        confs[name] = {
            "fw_array": fw_ref.group(1),
            "shift": addr(shift_b),
            "fan": addr(fan_b),
            "charge": int(charge.group(1), 16) if charge else None,
            "super_batt": addr(sub("super_battery")),
            "shift_modes": dict(re.findall(r"\{\s*SM_(\w+?)_NAME\s*,\s*(0x[0-9a-fA-F]+)\s*\}", shift_b)),
            "fan_modes": dict(re.findall(r"\{\s*FM_(\w+?)_NAME\s*,\s*(0x[0-9a-fA-F]+)\s*\}", fan_b)),
        }
    if not confs:
        raise ParseError("no CONF_* structs found")

    # 3) flatten to per-prefix view
    out: dict[str, dict] = {}
    for cname, c in confs.items():
        for fw, comment in fw_arrays.get(c["fw_array"], []):
            prefix = fw.split(".")[0]
            e = out.setdefault(prefix, {"conf": cname, "fws": [], "names": [], **c})
            e["fws"].append(fw)
            if comment and comment not in e["names"]:
                e["names"].append(comment)
    return out


def parse_devices(src: str) -> dict:
    """-> {prefix: {name, shift, fan, charge}} from Core/Devices.cs new() { ... } entries."""
    out: dict[str, dict] = {}
    for m in re.finditer(r"new\(\)\s*\{", src):
        try:
            block = brace_block(src, m.start())
        except ParseError:
            continue
        prefixes = re.search(r"FirmwarePrefixes\s*=\s*new\[\]\s*\{([^}]*)\}", block)
        if not prefixes:
            continue

        def field(name: str, default: int) -> int:
            f = re.search(name + r"\s*=\s*(0x[0-9a-fA-F]+)", block)
            return int(f.group(1), 16) if f else default

        name = re.search(r'Name\s*=\s*"([^"]+)"', block)
        entry = {
            "name": name.group(1) if name else "?",
            "shift": field("ShiftMode", OUR_DEFAULTS["shift"]),
            "fan": field("FanMode", OUR_DEFAULTS["fan"]),
            "charge": field("ChargeCtrl", OUR_DEFAULTS["charge"]),
        }
        for p in re.findall(r'"([^"]+)"', prefixes.group(1)):
            out[p] = entry
    if not out:
        raise ParseError("no DeviceProfile entries found in Devices.cs")
    return out


def csharp_line(prefix: str, e: dict) -> str:
    name = (e["names"][0] if e["names"] else f"TODO name (msi-ec CONF_{e['conf']})").strip()
    sb = f"0x{e['super_batt']:02X}" if e["super_batt"] is not None else "null"
    parts = [f'Name = "MSI {name}"', f'FirmwarePrefixes = new[] {{ "{prefix}" }}', "Tier = Tier.Experimental"]
    if e["shift"] != OUR_DEFAULTS["shift"]:
        parts.append(f"ShiftMode = 0x{e['shift']:02X}")
    if e["fan"] != OUR_DEFAULTS["fan"]:
        parts.append(f"FanMode = 0x{e['fan']:02X}")
    if e["charge"] is not None and e["charge"] != OUR_DEFAULTS["charge"]:
        parts.append(f"ChargeCtrl = 0x{e['charge']:02X}")
    parts.append(f"Recipes = StdRecipes(0x{e['shift']:02X}, 0x{e['fan']:02X}, {sb})")
    return "new() { " + ", ".join(parts) + " },"


def build_report(mec: dict, ours: dict, baseline: set[str], include_acked: bool = False) -> tuple[str, bool]:
    new_ok, new_nosilent, mismatches, new_fws = [], [], [], []

    for prefix in sorted(mec):
        e = mec[prefix]
        if prefix not in ours:
            if "SILENT" in e["fan_modes"]:
                new_ok.append((prefix, e))
            elif include_acked or prefix not in NOSILENT_ACK:
                new_nosilent.append((prefix, e))
        else:
            o = ours[prefix]
            if prefix in MISMATCH_WAIVERS:
                continue
            diffs = []
            for key in ("shift", "fan", "charge"):
                up = e[key]
                if up is None or up == o[key]:
                    continue
                if key == "charge" and (o[key], up) in CHARGE_EQUIV:
                    continue
                diffs.append(f"{key}: ours 0x{o[key]:02X} vs upstream 0x{up:02X}")
            if diffs:
                mismatches.append((prefix, o["name"], e["conf"], diffs))

    for prefix, e in sorted(mec.items()):
        for fw in e["fws"]:
            if fw not in baseline:
                new_fws.append((fw, e["conf"], prefix in ours))

    has_diff = bool(new_ok or new_nosilent or mismatches or new_fws)
    L: list[str] = []
    L.append("Automated weekly diff of [msi-ec](https://github.com/BeardOverflow/msi-ec) against `Core/Devices.cs`.")
    L.append("This is a report only - nothing was changed. New entries must go in as `Tier.Experimental` via a normal reviewed commit.\n")

    if new_ok:
        L.append(f"## (a) New prefixes ready to import ({len(new_ok)})\n")
        L.append("Their conf documents a Silent fan value, so they fit our standard recipes:\n")
        L.append("```csharp")
        for prefix, e in new_ok:
            L.append(csharp_line(prefix, e))
        L.append("```")
    if new_nosilent:
        L.append(f"\n## (b) New prefixes WITHOUT a Silent fan value ({len(new_nosilent)}) - human decision needed\n")
        L.append("Our Silent/Balanced detection keys off the Silent fan byte; do not import blindly (see TECHNICAL §36).\n")
        for prefix, e in new_nosilent:
            names = "; ".join(e["names"]) or "(no model comment upstream)"
            fans = ", ".join(f"{k.lower()}={v}" for k, v in e["fan_modes"].items())
            L.append(f"- `{prefix}` (CONF_{e['conf']}) - {names} - fan modes: {fans}")
    if mismatches:
        L.append(f"\n## (c) Address mismatches with upstream ({len(mismatches)}) - INVESTIGATE\n")
        L.append("Upstream may have corrected a map (or we diverged deliberately - if so, waive it in the script):\n")
        for prefix, name, conf, diffs in mismatches:
            L.append(f"- `{prefix}` ({name}, CONF_{conf}): " + "; ".join(diffs))
    if new_fws:
        L.append(f"\n## (d) Firmware versions not in the baseline ({len(new_fws)}) - informational\n")
        for fw, conf, known in new_fws:
            L.append(f"- `{fw}` (CONF_{conf}{', prefix already supported' if known else ''})")
        L.append("\nAfter handling this report run `python tools/msiec-sync.py --update-baseline` and commit the baseline.")

    if not has_diff:
        L.append("No differences - our table matches upstream.")
    return "\n".join(L) + "\n", has_diff


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--source", help="local msi-ec.c path or URL (default: upstream main)")
    ap.add_argument("--devices", default=str(DEVICES_CS))
    ap.add_argument("--baseline", default=str(BASELINE))
    ap.add_argument("--report", help="write the markdown report here (only when differences exist)")
    ap.add_argument("--update-baseline", action="store_true", help="write the current upstream fw list to the baseline file")
    ap.add_argument("--include-acked", action="store_true", help="also list no-Silent prefixes already tracked in NOSILENT_ACK")
    args = ap.parse_args()

    try:
        mec = parse_msiec(fetch_source(args.source))
        ours = parse_devices(Path(args.devices).read_text(encoding="utf-8"))
    except ParseError as ex:
        print(f"PARSE FAILURE (upstream layout changed?): {ex}", file=sys.stderr)
        return 2

    if args.update_baseline:
        fws = sorted(fw for e in mec.values() for fw in e["fws"])
        Path(args.baseline).write_text("\n".join(fws) + "\n", encoding="utf-8", newline="\n")
        print(f"baseline updated: {len(fws)} firmware ids -> {args.baseline}")
        return 0

    baseline_path = Path(args.baseline)
    baseline = set(baseline_path.read_text(encoding="utf-8").split()) if baseline_path.exists() else set()

    report, has_diff = build_report(mec, ours, baseline, include_acked=args.include_acked)
    print(report)
    if has_diff and args.report:
        Path(args.report).write_text(report, encoding="utf-8", newline="\n")
    return 10 if has_diff else 0


if __name__ == "__main__":
    sys.exit(main())
