using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Lightweight OS metrics (no external libs): total CPU load via GetSystemTimes
/// deltas, and RAM usage via GlobalMemoryStatusEx. Used by the Status page.
/// </summary>
internal static class SysInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemStatus buffer);

    private static ulong _prevIdle, _prevBusy;

    private static ulong ToU64(FileTime t) => ((ulong)t.High << 32) | t.Low;

    /// <summary>Total CPU load 0..100 (delta since the previous call; first call returns 0).</summary>
    public static int CpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong i = ToU64(idle), k = ToU64(kernel), u = ToU64(user);
        ulong busy = (k + u) - i;            // kernel already includes idle
        ulong dBusy = busy - _prevBusy, dIdle = i - _prevIdle;
        _prevBusy = busy; _prevIdle = i;
        ulong total = dBusy + dIdle;
        if (total == 0) return 0;
        return (int)Math.Clamp(dBusy * 100 / total, 0, 100);
    }

    /// <summary>RAM: (percentUsed 0..100, totalGB, usedGB).</summary>
    public static (int percent, double totalGb, double usedGb) Ram()
    {
        var m = new MemStatus { Length = (uint)Marshal.SizeOf<MemStatus>() };
        if (!GlobalMemoryStatusEx(ref m) || m.TotalPhys == 0) return (0, 0, 0);
        double total = m.TotalPhys / 1073741824.0;
        double used = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
        return ((int)m.MemoryLoad, total, used);
    }

    private static string? _cpuName;

    /// <summary>
    /// The processor's model as people say it ("i9-13980HX", "Ultra 9 185H", "Ryzen AI 9 HX 370"),
    /// cut out of the registry's ProcessorNameString, which carries vendor marks, generation
    /// prefixes and the integrated graphics. Read once; "" when the registry has no name.
    /// </summary>
    public static string CpuShortName()
    {
        if (_cpuName != null) return _cpuName;
        string raw = "";
        try
        {
            raw = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString", "") as string ?? "";
        }
        catch { }
        return _cpuName = ShortenCpuName(raw);
    }

    internal static string ShortenCpuName(string raw)
    {
        string s = raw.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "");
        foreach (var cut in new[] { " w/ ", " with ", "@" })
        {
            int at = s.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
            if (at > 0) s = s[..at];
        }
        // "Core" goes only before i3/i5/i7/i9 and Ultra: in "Core 7 240H" it is part of the model
        s = System.Text.RegularExpressions.Regex.Replace(s,
            @"\b(\d+(st|nd|rd|th) Gen|Intel|AMD|Processor|CPU|\d+-Core|Mobile)\b|\bCore\s+(?=i[3579]-|Ultra\b)", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 0 ? s : raw.Trim();
    }
}
