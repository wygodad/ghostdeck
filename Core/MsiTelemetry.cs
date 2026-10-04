using System.Management;

namespace GhostDeck;

/// <summary>
/// Read-only temperature source for MSI laptops whose firmware does NOT expose the EC method
/// interface (`MSI_ACPI`, GUID ABBC0F6E) that GhostDeck normally drives. Established in
/// issue #48 on a Delta 15 A5EFK: the owner extracted the BIOS and decoded the firmware's
/// `_WDG`, where that GUID is absent entirely - while the vendor DATA blocks below are
/// firmware-backed and answer normally.
///
/// Each block is exposed as instances `ACPI\PNP0C14\0_N`, where N is the byte index inside
/// the block and the value sits in a property named after the class. **Byte index 1 is the
/// live temperature in °C**, confirmed on that machine by CPU-load correlation (56 -> 90 °C
/// under load; GPU steady ~52-54 °C).
///
/// This class is the temperatures-only fallback: it runs on machines whose model has no
/// backup-path layout on record (see <see cref="EcBlocks"/> and Ec.TryBackupPath), turning an
/// otherwise dead app into a working thermometer. Requires elevation (the blocks deny access
/// to non-admin callers), which the app always has.
/// </summary>
public static class MsiTelemetry
{
    private const int TempByteIndex = 1;

    public readonly record struct Sample(int CpuTemp, int GpuTemp)
    {
        public bool Any => CpuTemp > 0 || GpuTemp > 0;
    }

    private static Sample _last;
    private static DateTime _lastAt = DateTime.MinValue;

    /// <summary>Cached read (2 s) of both blocks; 0 = that sensor did not answer.</summary>
    public static Sample Read()
    {
        if ((DateTime.UtcNow - _lastAt).TotalSeconds < 2) return _last;
        _lastAt = DateTime.UtcNow;
        _last = new Sample(ReadBlockByte("MSI_CPU", "CPU"), ReadBlockByte("MSI_VGA", "VGA"));
        return _last;
    }

    /// <summary>True when this machine answers on the data blocks (probe for the telemetry mode).</summary>
    public static bool Available() => Read().Any;

    /// <summary>Raw survey of the vendor blocks for the diagnostic package (see EcBlocks.Dump).</summary>
    public static string Dump() => EcBlocks.Dump();

    private static int ReadBlockByte(string cls, string prop)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", $"SELECT InstanceName, {prop} FROM {cls}");
            foreach (ManagementObject o in searcher.Get())
            {
                // instance name ends with "_<byte index>"
                string name = o["InstanceName"]?.ToString() ?? "";
                int us = name.LastIndexOf('_');
                if (us < 0 || !int.TryParse(name[(us + 1)..], out int idx) || idx != TempByteIndex) continue;
                var v = o[prop];
                if (v == null) continue;
                int t = Convert.ToInt32(v);
                return t is > 0 and < 120 ? t : 0;   // same sanity window as the EC path
            }
        }
        catch { }   // absent class, access denied, WMI hiccup - all mean "no telemetry"
        return 0;
    }
}
