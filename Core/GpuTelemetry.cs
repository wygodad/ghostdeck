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

    /// <summary>The card the clock tile describes: the same one the load and VRAM readings use.</summary>
    private static bool TryFindAdapter(out long luid, out string name)
    {
        var main = GetRoles().Main;
        luid = main?.Luid ?? 0;
        name = main?.Name ?? "";
        return luid != 0;
    }

    // ---- which card is which (shared with Perf) ----

    /// <summary>A hardware graphics adapter as Windows lists it.</summary>
    public sealed record Adapter(long Luid, string Name, ulong DedicatedBytes, bool Integrated);

    /// <summary>
    /// <see cref="Main"/> is the card the app calls "GPU": the discrete one on a two-card laptop,
    /// the only one otherwise. <see cref="Integrated"/> is set only on a two-card laptop.
    /// <see cref="KnownLuids"/> holds every adapter Windows listed, software ones included, so a
    /// counter instance with an identifier outside it means the set of adapters has changed
    /// (driver update, an adapter disabled and enabled again) and the roles must be read again.
    /// </summary>
    public sealed record AdapterRoles(Adapter? Main, Adapter? Integrated, IReadOnlyCollection<long> KnownLuids);

    private static readonly object _rolesLock = new();
    private static AdapterRoles? _roles;

    /// <summary>Adapter roles, read once and cached. Never throws; an empty result means "unknown".</summary>
    public static AdapterRoles GetRoles()
    {
        lock (_rolesLock)
        {
            if (_roles != null) return _roles;
            try { _roles = Classify(); }
            catch { _roles = new AdapterRoles(null, null, Array.Empty<long>()); }
            return _roles;
        }
    }

    /// <summary>Drop the cached roles; the next <see cref="GetRoles"/> enumerates the adapters again.</summary>
    public static void ForgetRoles() { lock (_rolesLock) _roles = null; }

    /// <summary>
    /// Integrated or discrete is the driver's own statement, not a guess from memory sizes. Two
    /// sources say it: DXCore's IsIntegrated property (Windows 10 2004+) and the hybrid flags in
    /// the kernel adapter type, which drivers set on two-card laptops. Measured on a GE78HX: both
    /// mark the Intel UHD integrated and the RTX discrete, and the Basic Render Driver (Windows'
    /// software renderer, also present in the counters) is a software adapter. "Most memory of
    /// its own" stays as the fallback among cards no source calls integrated, because an
    /// integrated chip can be given more memory than the discrete card has (AMD's Variable
    /// Graphics Memory reaches 16 GB and more).
    /// </summary>
    private static unsafe AdapterRoles Classify()
    {
        var known = new List<long>();
        var hw = new List<(Adapter A, bool HybridDiscrete)>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
        if (CreateDXGIFactory1(ref iid, out IntPtr factory) < 0 || factory == 0)
            return new AdapterRoles(null, null, known);
        IntPtr dxcore = DxCoreFactory();
        try
        {
            IntPtr fvt = *(IntPtr*)factory;
            var next = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Fn>(((IntPtr*)fvt)[12]);
            for (uint i = 0; ; i++)
            {
                if (next(factory, i, out IntPtr ad) < 0 || ad == 0) break;
                try
                {
                    IntPtr avt = *(IntPtr*)ad;
                    var desc = Marshal.GetDelegateForFunctionPointer<GetDesc1Fn>(((IntPtr*)avt)[10]);
                    var d = new AdapterDesc1();
                    if (desc(ad, ref d) < 0) continue;
                    long luid = ((long)d.LuidHigh << 32) | d.LuidLow;
                    known.Add(luid);
                    if ((d.Flags & 2) != 0) continue;   // DXGI_ADAPTER_FLAG_SOFTWARE
                    uint type = KernelAdapterType(luid);
                    if ((type & 4) != 0) continue;      // SoftwareDevice
                    bool integrated = (type & 0x20) != 0 || DxCoreIsIntegrated(dxcore, luid);
                    hw.Add((new Adapter(luid, (d.Description ?? "").TrimEnd('\0'), (ulong)d.DedicatedVideoMemory, integrated),
                            (type & 0x10) != 0));
                }
                finally { Marshal.GetDelegateForFunctionPointer<ReleaseFn>(((IntPtr*)(*(IntPtr*)ad))[2])(ad); }
            }
        }
        finally
        {
            Marshal.GetDelegateForFunctionPointer<ReleaseFn>(((IntPtr*)(*(IntPtr*)factory))[2])(factory);
            if (dxcore != 0) Marshal.GetDelegateForFunctionPointer<ReleaseFn>(((IntPtr*)(*(IntPtr*)dxcore))[2])(dxcore);
        }

        Adapter? main = hw.Where(h => h.HybridDiscrete && !h.A.Integrated).Select(h => h.A).FirstOrDefault()
            ?? hw.Where(h => !h.A.Integrated).OrderByDescending(h => h.A.DedicatedBytes).Select(h => h.A).FirstOrDefault()
            ?? hw.Select(h => h.A).FirstOrDefault();   // an integrated-only laptop: its one card is "the GPU"
        Adapter? igpu = hw.Select(h => h.A).FirstOrDefault(a => a.Integrated && a != main);
        return new AdapterRoles(main, igpu, known);
    }

    /// <summary>D3DKMT_ADAPTERTYPE bits (KMTQAITYPE_ADAPTERTYPE = 15); 0 when unreadable.</summary>
    private static uint KernelAdapterType(long luid)
    {
        var open = new OpenAdapterFromLuid { Luid = luid };
        if (D3DKMTOpenAdapterFromLuid(ref open) != 0 || open.Adapter == 0) return 0;
        IntPtr buf = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buf, 0);
            var q = new QueryAdapterInfo { Adapter = open.Adapter, Type = 15, Data = buf, Size = 4 };
            return D3DKMTQueryAdapterInfo(ref q) == 0 ? (uint)Marshal.ReadInt32(buf) : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            var c = new CloseAdapter { Adapter = open.Adapter };
            try { D3DKMTCloseAdapter(ref c); } catch { }
        }
    }

    // DXCore vtable slots, counted off dxcore_interface.h (IUnknown takes 0-2):
    //   IDXCoreAdapterFactory  4 GetAdapterByLuid
    //   IDXCoreAdapter         5 IsPropertySupported, 6 GetProperty   (IsIntegrated = property 12)
    private static IntPtr DxCoreFactory()
    {
        try
        {
            var iid = new Guid("78ee5945-c36e-4b13-a669-005dd11c0f06");   // IDXCoreAdapterFactory
            return DXCoreCreateAdapterFactory(ref iid, out IntPtr f) >= 0 ? f : 0;
        }
        catch { return 0; }   // dxcore.dll is absent before Windows 10 2004
    }

    private static unsafe bool DxCoreIsIntegrated(IntPtr factory, long luid)
    {
        if (factory == 0) return false;
        var iid = new Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e");   // IDXCoreAdapter
        var byLuid = Marshal.GetDelegateForFunctionPointer<GetAdapterByLuidFn>(((IntPtr*)(*(IntPtr*)factory))[4]);
        if (byLuid(factory, ref luid, ref iid, out IntPtr ad) < 0 || ad == 0) return false;
        try
        {
            IntPtr avt = *(IntPtr*)ad;
            const uint IsIntegrated = 12;
            if (!Marshal.GetDelegateForFunctionPointer<IsPropertySupportedFn>(((IntPtr*)avt)[5])(ad, IsIntegrated)) return false;
            return Marshal.GetDelegateForFunctionPointer<GetPropertyFn>(((IntPtr*)avt)[6])(ad, IsIntegrated, 1, out byte v) >= 0 && v != 0;
        }
        finally { Marshal.GetDelegateForFunctionPointer<ReleaseFn>(((IntPtr*)(*(IntPtr*)ad))[2])(ad); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1Fn(IntPtr self, uint i, out IntPtr ad);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1Fn(IntPtr self, ref AdapterDesc1 d);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAdapterByLuidFn(IntPtr self, ref long luid, ref Guid iid, out IntPtr ad);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.U1)] private delegate bool IsPropertySupportedFn(IntPtr self, uint property);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPropertyFn(IntPtr self, uint property, nuint size, out byte value);

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("dxcore.dll")] private static extern int DXCoreCreateAdapterFactory(ref Guid iid, out IntPtr factory);
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
