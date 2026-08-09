using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// What Windows itself will tell us about the graphics chip: which one it is, its core clock, that
/// clock's ceiling, and its temperature. No vendor library, no package, no driver of ours - this is
/// the same path Task Manager uses, <c>D3DKMTQueryAdapterInfo</c> in gdi32.dll. The two query codes
/// and every struct layout below come from the Windows SDK header <c>shared/d3dkmthk.h</c>.
///
/// The clock is the useful part for this app. A profile that raises a power budget shows up as the
/// card holding a higher clock under the same load, and a clock sitting well under its ceiling
/// while the card is busy is the firmware limiting it. That is the same story a wattage would tell.
///
/// Deliberately absent: watts. The power field Windows exposes is a share of the adapter's own
/// limit rather than a wattage, and on the driver this was measured against it reads zero, so
/// offering a number in watts would mean offering one we cannot actually produce. Adapter fan RPM
/// is in the same position, and that we already read from the laptop's own controller.
/// </summary>
internal static class GpuTelemetry
{
    /// <summary>
    /// A snapshot. <see cref="Ok"/> means a discrete adapter was found and is still there, which is
    /// a different question from whether it answered this instant: a card that has powered itself
    /// down reports no clock at all. Callers keep their tile in place on Ok and show a dash for a
    /// zero clock, rather than letting the tile appear and vanish as the card idles.
    /// </summary>
    public readonly record struct Reading(bool Ok, string Name, int Mhz, int MaxMhz, int TempC)
    {
        /// <summary>Core clock as a share of its ceiling, which is what a power profile moves.</summary>
        public int Percent => MaxMhz > 0 && Mhz > 0 ? (int)Math.Round(Mhz * 100.0 / MaxMhz) : 0;
    }

    private const int QueryNodePerf = 61;
    private const int QueryAdapterPerf = 62;
    private const int MinRefreshMs = 700;

    private static readonly object _lock = new();
    private static readonly System.Diagnostics.Stopwatch _since = System.Diagnostics.Stopwatch.StartNew();
    private static long _lastMs = -MinRefreshMs;
    private static Reading _last;
    private static uint _adapter;          // 0 = not open
    private static string _name = "";
    private static uint _node;             // engine ordinal that actually reports a clock
    private static bool _probed;
    private static int _maxSeen;           // the ceiling is a property of the card, so it is remembered
    private static int _quiet;             // consecutive all-zero samples
    private const int QuietBeforeReopen = 20;

    /// <summary>Cached snapshot, refreshed at most a few times a second. Never throws.</summary>
    public static Reading Read()
    {
        lock (_lock)
        {
            long now = _since.ElapsedMilliseconds;
            if (now - _lastMs < MinRefreshMs) return _last;
            _lastMs = now;
            try { _last = Sample(); }
            catch { _last = default; Close(); }
            return _last;
        }
    }

    private static Reading Sample()
    {
        if (_adapter == 0 && !Open()) return default;

        var node = new NodePerfData { NodeOrdinal = _node };
        int mhz = 0, max = 0;
        if (Query(ref node, QueryNodePerf) == 0)
        {
            mhz = (int)(node.Frequency / 1_000_000);
            max = (int)(node.MaxFrequency / 1_000_000);
        }

        var perf = new AdapterPerfData();
        int temp = Query(ref perf, QueryAdapterPerf) == 0 ? (int)Math.Round(perf.Temperature / 10.0) : 0;

        // An idle card answers with zeros and that is a normal state, not a lost adapter, so the
        // handle is only dropped after this has gone on long enough to mean the driver restarted.
        if (max <= 0 && temp <= 0 && mhz <= 0)
        {
            if (++_quiet >= QuietBeforeReopen) { Close(); return default; }
            // The ceiling does not change, so a remembered one still describes the card.
            return new Reading(true, _name, 0, _maxSeen, 0);
        }
        _quiet = 0;
        if (max > 0) _maxSeen = max;
        return new Reading(true, _name, mhz, max > 0 ? max : _maxSeen, temp);
    }

    private static bool Open()
    {
        if (!TryFindAdapter(out long luid, out string name)) return false;
        var open = new OpenAdapterFromLuid { Luid = luid };
        if (D3DKMTOpenAdapterFromLuid(ref open) != 0 || open.Adapter == 0) return false;
        _adapter = open.Adapter;
        _name = name;
        if (!_probed) { _node = FindNode(); _probed = true; }
        return true;
    }

    /// <summary>
    /// Engine ordinals are the driver's own numbering, so the one carrying the core clock is found
    /// rather than assumed: the first that reports a ceiling at all.
    /// </summary>
    private static uint FindNode()
    {
        for (uint i = 0; i < 8; i++)
        {
            var n = new NodePerfData { NodeOrdinal = i };
            if (Query(ref n, QueryNodePerf) == 0 && n.MaxFrequency > 0) return i;
        }
        return 0;
    }

    private static void Close()
    {
        if (_adapter == 0) return;
        var c = new CloseAdapter { Adapter = _adapter };
        try { D3DKMTCloseAdapter(ref c); } catch { }
        _adapter = 0;
        _probed = false;
    }

    private static int Query<T>(ref T data, int type) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, buf, false);
            var q = new QueryAdapterInfo { Adapter = _adapter, Type = type, Data = buf, Size = (uint)size };
            int hr = D3DKMTQueryAdapterInfo(ref q);
            if (hr == 0) data = Marshal.PtrToStructure<T>(buf);
            return hr;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>The adapter with the most memory of its own, i.e. the discrete one on a laptop.</summary>
    private static unsafe bool TryFindAdapter(out long luid, out string name)
    {
        luid = 0; name = "";
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
        if (CreateDXGIFactory1(ref iid, out IntPtr factory) < 0 || factory == 0) return false;
        try
        {
            IntPtr fvt = *(IntPtr*)factory;
            var next = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Fn>(((IntPtr*)fvt)[12]);
            ulong best = 0;
            for (uint i = 0; ; i++)
            {
                if (next(factory, i, out IntPtr ad) < 0 || ad == 0) break;
                try
                {
                    IntPtr avt = *(IntPtr*)ad;
                    var desc = Marshal.GetDelegateForFunctionPointer<GetDesc1Fn>(((IntPtr*)avt)[10]);
                    var d = new AdapterDesc1();
                    if (desc(ad, ref d) >= 0 && (d.Flags & 2) == 0 && (ulong)d.DedicatedVideoMemory > best)
                    {
                        best = (ulong)d.DedicatedVideoMemory;
                        luid = ((long)d.LuidHigh << 32) | d.LuidLow;
                        name = (d.Description ?? "").TrimEnd('\0');
                    }
                }
                finally { Marshal.GetDelegateForFunctionPointer<ReleaseFn>(((IntPtr*)(*(IntPtr*)ad))[2])(ad); }
            }
            Marshal.GetDelegateForFunctionPointer<ReleaseFn>(((IntPtr*)fvt)[2])(factory);
        }
        catch { return false; }
        return luid != 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1Fn(IntPtr self, uint i, out IntPtr ad);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1Fn(IntPtr self, ref AdapterDesc1 d);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseFn(IntPtr self);

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref CloseAdapter p);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId;
        public int Revision;
        public nint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid { public long Luid; public uint Adapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo { public uint Adapter; public int Type; public IntPtr Data; public uint Size; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter { public uint Adapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
        public uint FanRPM;
        public uint Power;         // tenths of a percent of the adapter's own limit, not watts
        public uint Temperature;   // deci-Celsius
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NodePerfData
    {
        public uint NodeOrdinal, PhysicalAdapterIndex;
        public ulong Frequency, MaxFrequency, MaxFrequencyOC;
        public uint Voltage, VoltageMax, VoltageMaxOC;
        public ulong MaxTransitionLatency;
    }
}
