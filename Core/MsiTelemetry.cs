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
/// This is telemetry only: data blocks cannot switch profiles, drive fan curves or set the
/// charge limit, so it never substitutes for the EC path - it only turns an otherwise dead
/// app into a working thermometer on such machines. Requires elevation (the blocks deny
/// access to non-admin callers), which the app always has.
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

    /// <summary>
    /// Raw dump of the vendor blocks for the diagnostic package: every instance with its byte
    /// index and value, or the exact error the class returned. Boards differ a lot here - a
    /// GE78HX (working EC interface) answers `NotSupported` for these blocks, while a Delta 15
    /// (no EC interface) serves them - so this is the first thing to look at when telemetry
    /// mode does not light up on a machine that should have it.
    /// </summary>
    public static string Dump()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== MSI WMI vendor blocks (root\\wmi) ===");
        sb.AppendLine("Instance suffix = byte index inside the block; index 1 = live temperature (issue #48).");
        sb.AppendLine();
        foreach (var (cls, prop) in new[]
                 {
                     ("MSI_CPU", "CPU"), ("MSI_VGA", "VGA"), ("MSI_Master_Battery", "Master_Battery"),
                     ("MSI_Power", "Power"), ("MSI_System", "System"), ("MSI_AP", "AP"),
                 })
        {
            sb.Append(cls).AppendLine(":");
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {cls}");
                int n = 0;
                foreach (ManagementObject o in searcher.Get())
                {
                    string name = o["InstanceName"]?.ToString() ?? "?";
                    object? v = null;
                    try { v = o[prop]; } catch { }
                    sb.AppendLine($"  {name} -> {prop} = {v ?? "(null)"}");
                    if (++n >= 40) { sb.AppendLine("  … (truncated)"); break; }
                }
                if (n == 0) sb.AppendLine("  (no instances returned)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  ERROR: {ex.GetType().Name}: {ex.Message.Trim()}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

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
