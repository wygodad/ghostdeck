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
}
