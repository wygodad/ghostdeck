using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MSIProfileSwitcher;

/// <summary>
/// Extra hardware metrics that don't come from the MSI EC: GPU utilisation %, VRAM used, and an
/// approximate CPU clock. All read via Windows PDH performance counters using the *English* counter
/// API (PdhAddEnglishCounter), so the counter paths work regardless of the OS display language
/// (Polish etc.). This is the deliberate "no kernel driver" path (see TECHNICAL §21): PDH is the same
/// source Task Manager uses — no WinRing0/MSR, no anti-cheat risk. Everything is guarded: on any
/// failure the getters return -1 and the UI shows "—".
/// </summary>
internal static class Perf
{
    private const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Explicit)]
    private struct FmtValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FmtItem { public IntPtr szName; public FmtValue value; }

    [DllImport("pdh.dll")] private static extern uint PdhOpenQuery(string? src, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint fmt, out uint type, out FmtValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint fmt, ref uint size, out uint count, IntPtr buffer);

    private static readonly object _lock = new();
    private static bool _init, _ok;
    private static IntPtr _query, _cpuPerf, _gpu3d, _vram;
    private static int _baseMhz;
    private static DateTime _lastTick = DateTime.MinValue;

    // last sampled values (-1 = unavailable)
    private static int _gpuUsage = -1, _vramMb = -1, _cpuClock = -1;

    private static void Init()
    {
        _init = true;
        try
        {
            _baseMhz = (int)(Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0) ?? 0);
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
            // % Processor Performance can exceed 100 on boost — that's what makes the clock estimate useful.
            if (PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _cpuPerf) != 0)
                PdhAddEnglishCounter(_query, @"\Processor(_Total)\% Processor Performance", IntPtr.Zero, out _cpuPerf);
            PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu3d);
            PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _vram);
            PdhCollectQueryData(_query);   // prime (rate counters need two samples)
            _ok = true;
        }
        catch { _ok = false; }
    }

    /// <summary>Refresh the sampled values (throttled to ~700 ms). Safe to call from any poll.</summary>
    public static void Tick()
    {
        lock (_lock)
        {
            if (!_init) Init();
            if (!_ok) return;
            if ((DateTime.UtcNow - _lastTick).TotalMilliseconds < 700) return;
            _lastTick = DateTime.UtcNow;
            try
            {
                if (PdhCollectQueryData(_query) != 0) return;

                if (_cpuPerf != IntPtr.Zero && PdhGetFormattedCounterValue(_cpuPerf, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var cv) == 0 && cv.CStatus == 0)
                    _cpuClock = _baseMhz > 0 ? (int)Math.Round(_baseMhz * cv.doubleValue / 100.0) : -1;

                double gpu = ArraySum(_gpu3d);
                _gpuUsage = gpu >= 0 ? (int)Math.Clamp(Math.Round(gpu), 0, 100) : -1;

                double vram = ArraySum(_vram);
                _vramMb = vram >= 0 ? (int)Math.Round(vram / (1024.0 * 1024.0)) : -1;
            }
            catch { }
        }
    }

    // Sum a wildcard counter's instances (e.g. all GPU 3D engines / all adapters).
    private static double ArraySum(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return -1;
        uint size = 0, count = 0;
        if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero) != PDH_MORE_DATA || size == 0) return -1;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return -1;
            double sum = 0; int stride = Marshal.SizeOf<FmtItem>();
            for (int i = 0; i < count; i++)
            {
                var it = Marshal.PtrToStructure<FmtItem>(buf + i * stride);
                if (it.value.CStatus == 0) sum += it.value.doubleValue;
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static int GpuUsage() { Tick(); return _gpuUsage; }
    public static int VramUsedMb() { Tick(); return _vramMb; }
    public static int CpuClockMhz() { Tick(); return _cpuClock; }
}
