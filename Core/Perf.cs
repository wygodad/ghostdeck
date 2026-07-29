using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GhostDeck;

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

    // Total dedicated VRAM in MB (-1 = unknown). Read once from the display-adapter registry keys
    // (HardwareInformation.qwMemorySize, in bytes) and cached — the total never changes at runtime. We
    // take the largest adapter, i.e. the discrete GPU on a laptop with an iGPU + dGPU. This is the same
    // "no kernel driver" spirit as the rest of Perf; on any failure we return -1 and the UI hides the bar.
    private static int _vramTotalMb = -2;   // -2 = not yet probed, -1 = unavailable
    public static int VramTotalMb()
    {
        if (_vramTotalMb != -2) return _vramTotalMb;
        long maxBytes = 0;
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls != null)
                foreach (var sub in cls.GetSubKeyNames())
                {
                    if (!int.TryParse(sub, out _)) continue;   // only the 0000/0001/... adapter keys
                    using var k = cls.OpenSubKey(sub);
                    var v = k?.GetValue("HardwareInformation.qwMemorySize");
                    long bytes = v switch
                    {
                        long l => l,
                        int i => i,
                        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0,
                    };
                    if (bytes > maxBytes) maxBytes = bytes;
                }
        }
        catch { }
        _vramTotalMb = maxBytes > 0 ? (int)(maxBytes / (1024 * 1024)) : -1;
        return _vramTotalMb;
    }

    // ---- Physical disks with S.M.A.R.T. temperature (roadmap #17) ----
    // Names/sizes come from MSFT_PhysicalDisk; the temperature from the associated
    // MSFT_StorageReliabilityCounter. The counter class often cannot be enumerated directly
    // (returns nothing on many systems - PowerShell's Get-StorageReliabilityCounter also
    // requires piping a disk in), so each disk's counter is fetched via an ASSOCIATORS query.
    // Admin only, no kernel driver. Cached for 5 s (Status + overlay poll every second).
    // UsedGb/VolGb are summed over the disk's mounted volumes (MSI Center shows the same
    // numbers); VolGb <= SizeGb because unpartitioned space carries no volume.
    public readonly record struct DiskInfo(int Index, string Name, double SizeGb, int TempC, double UsedGb, double VolGb);

    private static IReadOnlyList<DiskInfo> _disks = Array.Empty<DiskInfo>();
    private static DateTime _disksAt = DateTime.MinValue;

    public static IReadOnlyList<DiskInfo> Disks()
    {
        if ((DateTime.UtcNow - _disksAt).TotalSeconds < 10) return _disks;
        _disksAt = DateTime.UtcNow;
        var list = new List<DiskInfo>();
        try
        {
            const string ns = @"root\microsoft\windows\storage";

            // bulk read first - cheap when it works
            var temps = new Dictionary<string, int>();
            try
            {
                using var rc = new System.Management.ManagementObjectSearcher(
                    ns, "SELECT DeviceId, Temperature FROM MSFT_StorageReliabilityCounter");
                foreach (var o in rc.Get())
                {
                    string id = o["DeviceId"]?.ToString() ?? "";
                    if (id.Length > 0 && o["Temperature"] != null) temps[id] = Convert.ToInt32(o["Temperature"]);
                }
            }
            catch { }

            var usage = VolumeUsageByDiskIndex();

            using var pd = new System.Management.ManagementObjectSearcher(
                ns, "SELECT ObjectId, DeviceId, FriendlyName, Size FROM MSFT_PhysicalDisk");
            foreach (var o in pd.Get())
            {
                string id = o["DeviceId"]?.ToString() ?? "";
                string name = o["FriendlyName"]?.ToString() is { Length: > 0 } n ? n : "Disk " + id;
                double gb = o["Size"] != null ? Convert.ToUInt64(o["Size"]) / 1e9 : 0;

                int idx = int.TryParse(id, out int pi) ? pi : -1;
                if (!temps.TryGetValue(id, out int t) || t <= 0)
                    t = TempViaAssociation(ns, o["ObjectId"]?.ToString());
                if (t <= 0 && idx >= 0)
                    t = TempViaIoctl(idx);       // WMI counter absent on many systems - ask the device itself
                if (t <= 0 && idx >= 0)
                    t = TempViaNvmeSmart(idx);   // some drives skip the temperature property - read the NVMe SMART log

                (double used, double vol) = idx >= 0 && usage.TryGetValue(idx, out var u) ? u : (0, 0);
                list.Add(new DiskInfo(idx, name, gb, t is > 0 and < 120 ? t : -1, used, vol));
            }
        }
        catch { }
        _disks = list.OrderBy(d => d.Index).ToList();   // Windows disk order (Disk 0, Disk 1…)
        return _disks;
    }

    // Last-resort temperature source: IOCTL_STORAGE_QUERY_PROPERTY with
    // StorageDeviceTemperatureProperty (= 52; value, 24-byte descriptor header and 16-byte
    // STORAGE_TEMPERATURE_INFO stride all verified against winioctl.h 10.0.19041, not recalled
    // from memory). Plain user-mode DeviceIoControl - elevation is enough, desiredAccess 0
    // suffices for property queries, no kernel driver involved.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
        string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h,
        uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int ret, IntPtr ov);

    // Deepest fallback: the NVMe SMART/health log (LID 0x02) via a protocol-specific query -
    // the same route CrystalDiskInfo takes, supported by practically every NVMe drive even when
    // the simpler temperature property is not (e.g. Kingston SKC3000). Composite Temperature
    // sits at bytes 1-2 of the log, little-endian, in Kelvin. Struct layout verified against
    // winioctl.h / nvme.h 10.0.19041: STORAGE_PROTOCOL_SPECIFIC_DATA = 10 DWORDs (40 B),
    // ProtocolTypeNvme = 3, NVMeDataTypeLogPage = 2, NVME_LOG_PAGE_HEALTH_INFO = 0x02;
    // response payload starts at 8 (descriptor header) + ProtocolDataOffset.
    private static int TempViaNvmeSmart(int driveIndex)
    {
        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
        try
        {
            using var h = CreateFileW($@"\\.\PhysicalDrive{driveIndex}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) return -1;
            foreach (int propId in new[] { 49, 50 })   // StorageAdapterProtocolSpecificProperty, then the device variant
            {
                var buf = new byte[8 + 40 + 512];      // STORAGE_PROPERTY_QUERY header + protocol data + log payload
                BitConverter.GetBytes(propId).CopyTo(buf, 0);
                // STORAGE_PROTOCOL_SPECIFIC_DATA at AdditionalParameters (offset 8):
                BitConverter.GetBytes(3).CopyTo(buf, 8);        // ProtocolType = Nvme
                BitConverter.GetBytes(2).CopyTo(buf, 12);       // DataType = LogPage
                BitConverter.GetBytes(0x02).CopyTo(buf, 16);    // RequestValue = health-info log
                BitConverter.GetBytes(40).CopyTo(buf, 24);      // ProtocolDataOffset = sizeof(specific data)
                BitConverter.GetBytes(512).CopyTo(buf, 28);     // ProtocolDataLength
                var outBuf = new byte[8 + 40 + 512];
                if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, buf.Length, outBuf, outBuf.Length, out int ret, IntPtr.Zero))
                    continue;
                int dataOff = 8 + BitConverter.ToInt32(outBuf, 8 + 16);   // 8 + returned ProtocolDataOffset
                if (dataOff + 3 > ret) continue;
                int kelvin = outBuf[dataOff + 1] | (outBuf[dataOff + 2] << 8);
                int c = kelvin - 273;
                if (c is > 0 and < 120) return c;
            }
        }
        catch { }
        return -1;
    }

    private static int TempViaIoctl(int driveIndex)
    {
        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
        try
        {
            using var h = CreateFileW($@"\\.\PhysicalDrive{driveIndex}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) return -1;
            var inBuf = new byte[16];
            inBuf[0] = 52;   // STORAGE_PROPERTY_QUERY.PropertyId = StorageDeviceTemperatureProperty
            var outBuf = new byte[512];
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, inBuf, inBuf.Length, outBuf, outBuf.Length, out int ret, IntPtr.Zero)
                || ret < 24 + 16)
                return -1;
            int count = BitConverter.ToUInt16(outBuf, 12);   // InfoCount
            int best = -1;
            for (int i = 0; i < count && 24 + i * 16 + 4 <= ret; i++)
            {
                int t = BitConverter.ToInt16(outBuf, 24 + i * 16 + 2);   // Temperature, °C, signed
                if (t > best) best = t;
            }
            return best;
        }
        catch { return -1; }
    }

    // Used/total volume space per physical-disk index (MSFT_PhysicalDisk.DeviceId == the
    // Win32_DiskDrive index): partition -> logical-disk associations in root\cimv2.
    private static Dictionary<int, (double used, double vol)> VolumeUsageByDiskIndex()
    {
        var map = new Dictionary<int, (double, double)>();
        try
        {
            using var parts = new System.Management.ManagementObjectSearcher(
                "SELECT DeviceID, DiskIndex FROM Win32_DiskPartition");
            foreach (var p in parts.Get())
            {
                int idx = Convert.ToInt32(p["DiskIndex"]);
                string pid = p["DeviceID"]?.ToString() ?? "";
                if (pid.Length == 0) continue;
                using var ld = new System.Management.ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{pid}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");
                foreach (var l in ld.Get())
                {
                    double size = l["Size"] != null ? Convert.ToUInt64(l["Size"]) / 1e9 : 0;
                    double free = l["FreeSpace"] != null ? Convert.ToUInt64(l["FreeSpace"]) / 1e9 : 0;
                    var cur = map.TryGetValue(idx, out var v) ? v : (0d, 0d);
                    map[idx] = (cur.Item1 + Math.Max(0, size - free), cur.Item2 + size);
                }
            }
        }
        catch { }
        return map;
    }

    private static int TempViaAssociation(string ns, string? objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return -1;
        try
        {
            // ObjectId contains backslashes and quotes - escape for the WQL object path
            string esc = objectId.Replace("\\", "\\\\").Replace("\"", "\\\"");
            using var s = new System.Management.ManagementObjectSearcher(
                new System.Management.ManagementScope(ns),
                new System.Management.RelatedObjectQuery(
                    $"ASSOCIATORS OF {{MSFT_PhysicalDisk.ObjectId=\"{esc}\"}} WHERE ResultClass = MSFT_StorageReliabilityCounter"));
            foreach (var o in s.Get())
                if (o["Temperature"] != null)
                    return Convert.ToInt32(o["Temperature"]);
        }
        catch { }
        return -1;
    }

    /// <summary>Temperatures of the first two disks in Windows order (-1 = not reporting) - the overlay's SSD metric.</summary>
    public static (int First, int Second) DiskTemps2()
    {
        var d = Disks();
        return (d.Count > 0 ? d[0].TempC : -1, d.Count > 1 ? d[1].TempC : -1);
    }

    // ---- Estimated battery time left (roadmap #15): Windows' own estimate via Win32_Battery. ----
    // EstimatedRunTime is in minutes; the API returns huge sentinel values (e.g. 0x44444444) when
    // charging or unknown. -1 = no estimate (on AC, no battery, or query failed). Cached for 15 s.
    private static int _battMin = -1;
    private static DateTime _battMinAt = DateTime.MinValue;

    public static int BatteryMinutesLeft()
    {
        if ((DateTime.UtcNow - _battMinAt).TotalSeconds < 15) return _battMin;
        _battMinAt = DateTime.UtcNow;
        int min = -1;
        try
        {
            if (SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online)
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT EstimatedRunTime FROM Win32_Battery");
                foreach (var o in searcher.Get())
                {
                    var t = o["EstimatedRunTime"];
                    if (t != null)
                    {
                        int m = Convert.ToInt32(Convert.ToUInt32(t));
                        if (m > 0 && m < 6000) min = m;   // sentinel/garbage guard (100 h cap)
                    }
                }
            }
        }
        catch { }
        _battMin = min;
        return _battMin;
    }
}
