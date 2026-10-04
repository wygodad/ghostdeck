using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>
/// Raised when something asks the backup WMI path for a register it does not carry, or for a
/// write this build does not perform there. Derives from InvalidOperationException so the
/// periodic-sampling guards (Ec.TryReadHw) treat it as a missing sample, not a crash.
/// </summary>
public sealed class EcPathException : InvalidOperationException
{
    public EcPathException(string message) : base(message) { }
}

/// <summary>
/// The backup WMI path: MSI's data blocks in root\wmi (`MSI_System`, `MSI_CPU`, `MSI_VGA`,
/// `MSI_AP`, ...). On firmware that refuses the `MSI_ACPI` method interface these blocks are
/// still served by the firmware (TECHNICAL §39), each as instances `ACPI\PNP0C14\0_N` whose
/// value sits in a property named after the class. A slot is addressed as class[index] - the
/// index the firmware itself uses.
///
/// READ-ONLY: this class has no write routine at all.
///
/// MSIPS_BLOCKS_FILE=&lt;msi-wmi-blocks.txt&gt; replays a saved block dump instead of querying
/// WMI, and MSIPS_BLOCKS_BIOS supplies the BIOS version string to go with it (this-machine
/// testing hook, same spirit as MSIPS_FORCE_FIRMWARE).
/// </summary>
public static class EcBlocks
{
    // Blocks read by the diagnostic dump. MSI_Software is left out on purpose: the firmware
    // flags it as an "expensive" collection, which makes Windows call a firmware enable
    // routine before every read - not something a read-only survey should trigger.
    private static readonly string[] DumpClasses =
    {
        "MSI_CPU", "MSI_VGA", "MSI_System", "MSI_AP", "MSI_Power",
        "MSI_Master_Battery", "MSI_Slave_Battery", "MSI_Device",
    };

    private static readonly HashSet<string> KnownClasses =
        new(DumpClasses, StringComparer.Ordinal);

    public static bool IsKnownClass(string cls) => KnownClasses.Contains(cls);

    /// <summary>Value property of a block class: the class name without its "MSI_" prefix.</summary>
    private static string Prop(string cls) => cls[4..];

    // One WQL query returns every instance of a block, so a tick that needs three slots of
    // MSI_CPU costs one query, not three. Entries live for a second - shorter than the 3 s poll,
    // long enough for the reads that belong to one sample.
    private static readonly Dictionary<string, (long At, Dictionary<int, int> Values)> _cache = new();
    private static readonly object _lock = new();
    private const int CacheMs = 1000;

    private static readonly string? ReplayFile = Environment.GetEnvironmentVariable("MSIPS_BLOCKS_FILE");
    public static bool Replaying => !string.IsNullOrEmpty(ReplayFile) && File.Exists(ReplayFile);

    /// <summary>Every instance of one block: index -> value. Throws what WMI throws.</summary>
    public static Dictionary<int, int> ReadClass(string cls)
    {
        if (!IsKnownClass(cls)) throw new EcPathException("Unknown data block " + cls);
        lock (_lock)
        {
            long now = Environment.TickCount64;
            if (_cache.TryGetValue(cls, out var hit) && now - hit.At < CacheMs) return hit.Values;
            var values = Replaying ? ReplayClass(cls) : QueryClass(cls);
            _cache[cls] = (now, values);
            return values;
        }
    }

    private static Dictionary<int, int> QueryClass(string cls)
    {
        var values = new Dictionary<int, int>();
        string prop = Prop(cls);
        using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT InstanceName, {prop} FROM {cls}");
        foreach (ManagementObject o in searcher.Get())
            using (o)
            {
                if (IndexOf(o["InstanceName"]?.ToString()) is not { } idx) continue;
                var v = o[prop];
                if (v != null) values[idx] = Convert.ToInt32(v);
            }
        return values;
    }

    /// <summary>Instance names end with "_&lt;index&gt;".</summary>
    private static int? IndexOf(string? instanceName)
    {
        if (string.IsNullOrEmpty(instanceName)) return null;
        int us = instanceName.LastIndexOf('_');
        return us >= 0 && int.TryParse(instanceName[(us + 1)..], out int idx) ? idx : null;
    }

    /// <summary>One slot. Throws EcPathException when the block does not carry that index.</summary>
    public static int Read(BlockRef r) =>
        ReadClass(r.Class).TryGetValue(r.Index, out int v)
            ? v
            : throw new EcPathException($"{r} is not served by this firmware");

    /// <summary>Drop cached block values (after sleep/resume the first sample must be fresh).</summary>
    public static void DropCache() { lock (_lock) _cache.Clear(); }

    // ---------------- BIOS version -> board code ----------------

    /// <summary>SMBIOS BIOS version (e.g. "E16W2IMS.105"), or "" when it cannot be read.</summary>
    public static string BiosVersion()
    {
        var env = Environment.GetEnvironmentVariable("MSIPS_BLOCKS_BIOS");
        if (Replaying && !string.IsNullOrEmpty(env)) return env;
        try
        {
            using var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (ManagementObject o in s.Get())
                using (o) return o["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "";
        }
        catch { }
        return "";
    }

    // MSI BIOS versions read E + <4-character board code> + <platform letter> + "MS" + "." +
    // revision: E16W2IMS.105 (Intel), E15CKAMS.10C (AMD). The EC firmware of the same board
    // reads <board code> + "EMS1". Only the board code is shared, so only that is extracted.
    private static readonly Regex BiosPattern = new(@"^E([0-9A-Z]{4})[A-Z]MS\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Board code from a BIOS version string, or null when the string has another shape.</summary>
    public static string? BoardFromBios(string biosVersion)
    {
        var m = BiosPattern.Match(biosVersion ?? "");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    // ---------------- diagnostic dump ----------------

    /// <summary>
    /// Raw survey of the vendor blocks for the diagnostic package: every instance with its
    /// index and value (or the exact error the class returned), how long each query took,
    /// the type and access of the value property, whether a single instance can be fetched
    /// by path, and the names of the block routines the firmware declares. Read-only.
    /// </summary>
    public static string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MSI WMI vendor blocks (root\\wmi) ===");
        sb.AppendLine("Instance suffix = index inside the block. Read-only survey.");
        if (Replaying) sb.AppendLine("REPLAY of a saved dump: " + ReplayFile);
        sb.AppendLine("BIOS version: " + BiosVersion());
        sb.AppendLine();
        foreach (var cls in DumpClasses)
        {
            sb.Append(cls).Append(':');
            try
            {
                var sw = Stopwatch.StartNew();
                var names = new List<(int Idx, string Name, object? Val)>();
                if (Replaying)
                {
                    foreach (var kv in ReplayClass(cls)) names.Add((kv.Key, $"(replay)_{kv.Key}", kv.Value));
                }
                else
                {
                    using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {cls}");
                    foreach (ManagementObject o in searcher.Get())
                        using (o)
                        {
                            string name = o["InstanceName"]?.ToString() ?? "?";
                            object? v = null;
                            try { v = o[Prop(cls)]; } catch { }
                            names.Add((IndexOf(name) ?? -1, name, v));
                            if (names.Count >= 64) break;
                        }
                }
                sw.Stop();
                sb.AppendLine($"  ({names.Count} instances, {sw.ElapsedMilliseconds} ms{PropertyInfo(cls)})");
                foreach (var (_, name, v) in names.OrderBy(n => n.Idx))
                    sb.AppendLine($"  {name} -> {Prop(cls)} = {v ?? "(null)"}");
                if (names.Count == 0) sb.AppendLine("  (no instances returned)");
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine($"  ERROR: {ex.GetType().Name}: {ex.Message.Trim()}");
            }
            sb.AppendLine();
        }
        sb.AppendLine("MSI_Software: not read (the firmware flags it as an expensive collection).");
        sb.AppendLine();
        sb.AppendLine("Single-instance fetch by path (MSI_System index 7): " + SingleInstanceProbe("MSI_System", 7));
        sb.AppendLine("Firmware block routines (DSDT): " + FirmwareRoutines());
        return sb.ToString();
    }

    /// <summary>CIM type and write qualifier of a block's value property, from the schema only.</summary>
    private static string PropertyInfo(string cls)
    {
        if (Replaying) return "";
        try
        {
            using var c = new ManagementClass(@"root\wmi", cls, null);
            c.Get();
            var p = c.Properties[Prop(cls)];
            bool write = false;
            foreach (QualifierData q in p.Qualifiers)
                if (q.Name.Equals("write", StringComparison.OrdinalIgnoreCase)) write = true;
            return $", {Prop(cls)}: {p.Type}, write qualifier: {(write ? "yes" : "no")}";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Can one instance be fetched by its object path, without listing the whole block?
    /// A read; the answer decides how later builds address a single slot.
    /// </summary>
    private static string SingleInstanceProbe(string cls, int index)
    {
        if (Replaying) return "skipped (replay)";
        try
        {
            string? instance = null;
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT InstanceName FROM {cls}"))
                foreach (ManagementObject o in searcher.Get())
                    using (o)
                    {
                        string n = o["InstanceName"]?.ToString() ?? "";
                        if (IndexOf(n) == index) { instance = n; break; }
                    }
            if (instance == null) return "no such instance";
            // inside an object path the backslash is an escape character
            string path = $"{cls}.InstanceName=\"{instance.Replace(@"\", @"\\")}\"";
            var sw = Stopwatch.StartNew();
            using var mo = new ManagementObject(@"root\wmi", path, null);
            mo.Get();
            sw.Stop();
            return $"ok, value={mo[Prop(cls)]}, {sw.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            return $"failed: {ex.GetType().Name}: {ex.Message.Trim()}";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);

    /// <summary>
    /// Names of the block routines in the firmware's ACPI table: `WQxx` answers a read of block
    /// xx, `WSxx` a write. Windows hands the table out to any process; nothing is executed.
    /// A name alone proves only that it exists in the table, which is why this is diagnostic
    /// text and never a condition in code.
    /// </summary>
    public static string FirmwareRoutines()
    {
        try
        {
            const uint acpi = 0x41435049, dsdt = 0x54445344;   // 'ACPI', 'DSDT'
            uint size = GetSystemFirmwareTable(acpi, dsdt, null, 0);
            if (size == 0) return "table not available";
            var buf = new byte[size];
            if (GetSystemFirmwareTable(acpi, dsdt, buf, size) == 0) return "table not available";
            string text = Encoding.Latin1.GetString(buf);
            var names = Regex.Matches(text, "W[QS][A-Z0-9][A-Z0-9]").Select(m => m.Value)
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (names.Count == 0) return $"none found ({size} bytes)";
            var pairs = names.Where(n => n[1] == 'Q' && names.Contains("WS" + n[2..]))
                .Select(n => n[2..]).ToList();
            return $"{string.Join(", ", names)}  |  read+write pairs: {(pairs.Count > 0 ? string.Join(", ", pairs) : "none")}  ({size} bytes)";
        }
        catch (Exception ex)
        {
            return "failed: " + ex.Message;
        }
    }

    // ---------------- replay of a saved dump (testing hook) ----------------

    private static Dictionary<string, Dictionary<int, int>>? _replay;

    private static Dictionary<int, int> ReplayClass(string cls)
    {
        _replay ??= ParseDump(File.ReadAllLines(ReplayFile!));
        return _replay.TryGetValue(cls, out var v)
            ? v
            : throw new ManagementException("Not supported (class absent from the replayed dump)");
    }

    // "MSI_CPU:" opens a block; "  ACPI\PNP0C14\0_7 -> System = 196" is one instance.
    private static Dictionary<string, Dictionary<int, int>> ParseDump(string[] lines)
    {
        var all = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
        Dictionary<int, int>? cur = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var head = Regex.Match(line, @"^(MSI_[A-Za-z_]+):");
            if (head.Success) { cur = all[head.Groups[1].Value] = new(); continue; }
            var m = Regex.Match(line, @"_(\d+) -> \w+ = (\d+)\s*$");
            if (m.Success && cur != null)
                cur[int.Parse(m.Groups[1].Value)] = int.Parse(m.Groups[2].Value);
        }
        // a block that listed no instance at all was not served
        foreach (var k in all.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList()) all.Remove(k);
        return all;
    }
}
