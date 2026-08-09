using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Best-effort load on the DISCRETE graphics processor, for the power test.
///
/// A processor-only load answers only half the question on a laptop. If a board's top performance
/// mode raises a budget the two chips share, a load that never asks the graphics side for anything
/// cannot see the difference, and the test would report "this mode does nothing" about a mode that
/// does plenty. So the test can run both sides at once and compare the processor work it still
/// gets through.
///
/// This is the only place in the app that touches a graphics API. It is contained accordingly:
/// every call is a plain function-pointer call into d3d11.dll and dxgi.dll behind
/// <see cref="Active"/>, a single failure anywhere leaves that false and the run carries on with
/// the processor load alone, and the report says which of the two it had. Nothing is drawn, no
/// window is created, and no swap chain exists - it is a compute dispatch into a buffer nobody
/// reads. All work happens on one dedicated thread, because a device context is not thread-safe.
///
/// The vtable indices below are counted off the published interface definitions, inherited methods
/// included. ID3D11DeviceContext derives from ID3D11DeviceChild rather than straight from IUnknown,
/// so its own methods sit four slots later than the device's do.
///   ID3D11Device         3 CreateBuffer, 8 CreateUnorderedAccessView, 18 CreateComputeShader
///   ID3D11DeviceContext  41 Dispatch, 68 CSSetUnorderedAccessViews, 69 CSSetShader, 111 Flush
///   IDXGIFactory1        12 EnumAdapters1
///   IDXGIAdapter1        10 GetDesc1
///   ID3DBlob             3 GetBufferPointer, 4 GetBufferSize
/// </summary>
internal sealed unsafe class GpuLoad : IDisposable
{
    // How big one dispatch should be. Submitting is not free, so tiny dispatches repeated fast burn
    // a processor core feeding the driver, which is the last thing a test that measures processor
    // work needs. Big ones cost nearly nothing to submit, but they must still finish well inside
    // the display driver's watchdog, which resets a device whose work takes seconds. 30 ms leaves
    // that watchdog roughly sixty times the margin it needs, and the size is calibrated at startup
    // so the same target holds on a weak integrated chip and on a fast discrete one alike.
    private const int TargetDispatchMs = 30;
    private const int ThreadsPerGroup = 64;
    private const int CalibrationGroups = 64;
    private const int CalibrationLargeGroups = 1024;
    private const int MaxGroups = 8192;
    private const int InFlight = 3;

    private const string Hlsl = @"
RWStructuredBuffer<float> Sink : register(u0);
[numthreads(64,1,1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float a = 1.0001f + id.x * 1e-6f;
    float b = 0.9999f;
    [loop] for (int i = 0; i < 65536; i++)
    {
        a = a * b + 1e-7f;
        b = b * 1.0000003f + 1e-7f;
        if (a > 1e12f) a *= 1e-12f;
        if (b > 1e12f) b *= 1e-12f;
    }
    Sink[id.x] = a + b;
}";

    private Thread? _thread;
    private volatile bool _stop;
    private long _dispatches;

    /// <summary>False = no discrete graphics load is running, for whatever reason. Never throws.</summary>
    public bool Active { get; private set; }

    /// <summary>Name of the adapter being loaded, for the report. Empty when inactive.</summary>
    public string Adapter { get; private set; } = "";

    /// <summary>Dispatch size calibration settled on, for diagnostics. 0 when inactive.</summary>
    public int DispatchGroups { get; private set; }

    public long Dispatches => Interlocked.Read(ref _dispatches);

    public GpuLoad()
    {
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "ghostdeck-gpuload",
        };
        _thread.Start();
        // The device either comes up in a moment or it is not coming up at all.
        ready.Wait(5000);
    }

    private void Run(ManualResetEventSlim ready)
    {
        IntPtr device = 0, context = 0, shader = 0, buffer = 0, uav = 0, adapter = 0;
        var markers = new IntPtr[InFlight];
        try
        {
            adapter = PickDiscreteAdapter(out string name);
            // driverType must be UNKNOWN when an adapter is supplied.
            int hr = D3D11CreateDevice(adapter, adapter == 0 ? DriverHardware : DriverUnknown, 0, 0,
                                       0, 0, SdkVersion, out device, out _, out context);
            if (hr < 0 || device == 0 || context == 0) { ready.Set(); return; }

            // The bytecode lives inside the blob, so the blob has to outlive CreateComputeShader.
            if (!Compile(out IntPtr blob, out IntPtr code, out nuint codeSize)) { ready.Set(); return; }
            try { hr = Call<CreateComputeShaderFn>(device, 18)(device, code, codeSize, 0, out shader); }
            finally { Release(ref blob); }
            if (hr < 0 || shader == 0) { ready.Set(); return; }

            // Sized for the largest dispatch that calibration is allowed to choose, so the size can
            // be decided after the buffer exists. Nothing ever reads it back.
            int elements = ThreadsPerGroup * MaxGroups;
            var bd = new BufferDesc
            {
                ByteWidth = (uint)(elements * sizeof(float)),
                Usage = 0,                       // DEFAULT
                BindFlags = 0x80,                // UNORDERED_ACCESS
                MiscFlags = 0x40,                // BUFFER_STRUCTURED
                StructureByteStride = sizeof(float),
            };
            if (Call<CreateBufferFn>(device, 3)(device, &bd, 0, out buffer) < 0) { ready.Set(); return; }

            var ud = new UavDesc { Format = 0, ViewDimension = 1, FirstElement = 0, NumElements = (uint)elements, Flags = 0 };
            if (Call<CreateUavFn>(device, 8)(device, buffer, &ud, out uav) < 0) { ready.Set(); return; }

            // Several markers, so work stays queued while the thread waits on the oldest one and
            // the chip never idles. Waiting sleeps, and a sleep can overshoot by more than a whole
            // dispatch, so the queue has to be deep enough to cover that.
            var qd = new QueryDesc { Query = 0, MiscFlags = 0 };   // QUERY_EVENT
            for (int i = 0; i < InFlight; i++)
                if (Call<CreateQueryFn>(device, 24)(device, &qd, out markers[i]) < 0) { ready.Set(); return; }

            IntPtr uavLocal = uav;
            Call<CsSetUavFn>(context, 68)(context, 0, 1, &uavLocal, null);
            Call<CsSetShaderFn>(context, 69)(context, shader, 0, 0);

            var dispatch = Call<DispatchFn>(context, 41);
            var flush = Call<FlushFn>(context, 111);
            var end = Call<EndFn>(context, 28);
            var getData = Call<GetDataFn>(context, 29);

            void Submit(uint groups, IntPtr marker)
            {
                dispatch(context, groups, 1, 1);
                end(context, marker);
                flush(context);                  // hand it to the driver rather than batching forever
                Interlocked.Increment(ref _dispatches);
            }

            // Returns false only if the wait was abandoned, which means something is wrong with the
            // device and the loop should give up rather than spin forever.
            bool Wait(IntPtr marker, bool spin)
            {
                var since = System.Diagnostics.Stopwatch.StartNew();
                while (getData(context, marker, 0, 0, 0) != 0)      // S_FALSE while still running
                {
                    if (since.ElapsedMilliseconds > 5000) return false;
                    if (spin) Thread.SpinWait(200); else Thread.Sleep(1);
                }
                return true;
            }

            // Timing one dispatch and scaling it would be wrong: a single measurement is mostly the
            // fixed cost of submitting and of noticing the marker, not the work itself, and scaling
            // that overhead up picks a size several times too small. Two sizes give a slope, and the
            // slope is the part that actually depends on how much work was asked for.
            double? small = TimeDispatch(CalibrationGroups, Submit, Wait, markers[0]);
            double? large = TimeDispatch(CalibrationLargeGroups, Submit, Wait, markers[0]);
            if (small is null || large is null) { ready.Set(); return; }

            double perGroupMs = (large.Value - small.Value) / (CalibrationLargeGroups - CalibrationGroups);
            uint groupCount = perGroupMs <= 0
                ? MaxGroups
                : (uint)Math.Clamp(TargetDispatchMs / perGroupMs, CalibrationGroups, MaxGroups);

            // A machine whose adapters all report no dedicated memory of their own gets whichever one
            // Direct3D picks, and the report has to name something rather than a blank.
            Adapter = name.Length == 0 ? "default adapter" : name;
            DispatchGroups = (int)groupCount;
            Active = true;
            ready.Set();

            foreach (IntPtr marker in markers) Submit(groupCount, marker);
            for (int slot = 0; !_stop; slot = (slot + 1) % InFlight)
            {
                if (!Wait(markers[slot], spin: false)) break;
                Submit(groupCount, markers[slot]);
            }
            // Let the queued work finish before the resources under it go away.
            foreach (IntPtr marker in markers) Wait(marker, spin: false);
        }
        catch
        {
            // A missing dll, a refused device, a driver that will not compile the shader: all of
            // them mean the same thing here, which is that the run continues without this.
        }
        finally
        {
            Active = false;
            ready.Set();
            for (int i = 0; i < markers.Length; i++) Release(ref markers[i]);
            Release(ref uav); Release(ref buffer); Release(ref shader);
            Release(ref context); Release(ref device); Release(ref adapter);
        }
    }

    /// <summary>
    /// Milliseconds for one dispatch of the given size, best of several so that first-run driver
    /// warmup and a stray scheduling delay do not become the answer. Null if the device stopped
    /// responding, which is the caller's cue to give up.
    /// </summary>
    private static double? TimeDispatch(uint groups, Action<uint, IntPtr> submit,
                                        Func<IntPtr, bool, bool> wait, IntPtr marker)
    {
        long best = long.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            var t = System.Diagnostics.Stopwatch.StartNew();
            submit(groups, marker);
            if (!wait(marker, true)) return null;
            t.Stop();
            if (t.ElapsedTicks < best) best = t.ElapsedTicks;
        }
        return best * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>
    /// The adapter with the most dedicated memory, which on a laptop is the discrete one. Returns 0
    /// to let Direct3D pick, which is what happens on a machine with only integrated graphics.
    /// </summary>
    private static IntPtr PickDiscreteAdapter(out string name)
    {
        name = "";
        IntPtr factory = 0, best = 0;
        ulong bestMem = 0;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out factory) < 0 || factory == 0) return 0;
            var enumAdapters = Call<EnumAdapters1Fn>(factory, 12);
            for (uint i = 0; ; i++)
            {
                if (enumAdapters(factory, i, out IntPtr ad) < 0 || ad == 0) break;
                var desc = new AdapterDesc1();
                bool keep = false;
                if (Call<GetDesc1Fn>(ad, 10)(ad, &desc) >= 0
                    && (desc.Flags & 2) == 0                       // not the software adapter
                    && (ulong)desc.DedicatedVideoMemory > bestMem)
                {
                    bestMem = (ulong)desc.DedicatedVideoMemory;
                    Release(ref best);
                    best = ad;
                    keep = true;
                    name = new string(desc.Description).TrimEnd('\0');
                }
                if (!keep) Release(ref ad);
            }
        }
        catch { Release(ref best); best = 0; name = ""; }
        finally { Release(ref factory); }
        return best;
    }

    /// <summary>Compiles the shader. The caller owns <paramref name="blob"/> and must release it
    /// after the shader is created, because <paramref name="code"/> points inside it.</summary>
    private static bool Compile(out IntPtr blob, out IntPtr code, out nuint size)
    {
        blob = 0; code = 0; size = 0;
        IntPtr errors = 0;
        try
        {
            byte[] src = System.Text.Encoding.ASCII.GetBytes(Hlsl);
            int hr = D3DCompile(src, (nuint)src.Length, null, 0, 0, "main", "cs_5_0", 0, 0, out blob, out errors);
            if (hr < 0 || blob == 0) return false;
            code = Call<GetBufferPointerFn>(blob, 3)(blob);
            size = Call<GetBufferSizeFn>(blob, 4)(blob);
            return code != 0 && size > 0;
        }
        catch { return false; }
        finally { Release(ref errors); }
    }

    // ---------------- COM plumbing ----------------

    private static T Call<T>(IntPtr obj, int slot) where T : Delegate
    {
        IntPtr vtable = *(IntPtr*)obj;
        IntPtr fn = ((IntPtr*)vtable)[slot];
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    private static void Release(ref IntPtr obj)
    {
        if (obj == 0) return;
        try { Call<ReleaseFn>(obj, 2)(obj); } catch { }
        obj = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBufferFn(IntPtr self, BufferDesc* desc, IntPtr initial, out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateUavFn(IntPtr self, IntPtr resource, UavDesc* desc, out IntPtr uav);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateComputeShaderFn(IntPtr self, IntPtr bytecode, nuint length, IntPtr linkage, out IntPtr shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DispatchFn(IntPtr self, uint x, uint y, uint z);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetUavFn(IntPtr self, uint start, uint count, IntPtr* uavs, uint* counts);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CsSetShaderFn(IntPtr self, IntPtr shader, IntPtr instances, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void FlushFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateQueryFn(IntPtr self, QueryDesc* desc, out IntPtr query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EndFn(IntPtr self, IntPtr async);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDataFn(IntPtr self, IntPtr async, IntPtr data, uint size, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1Fn(IntPtr self, AdapterDesc1* desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr GetBufferPointerFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate nuint GetBufferSizeFn(IntPtr self);

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferDesc
    {
        public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UavDesc
    {
        public uint Format, ViewDimension, FirstElement, NumElements, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryDesc
    {
        public uint Query, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId;
        public int Revision;
        public nint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    private const int DriverUnknown = 0, DriverHardware = 1, SdkVersion = 7;

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr context);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(
        byte[] src, nuint srcSize, string? sourceName, IntPtr defines, IntPtr include,
        string entrypoint, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);

    public void Dispose()
    {
        _stop = true;
        try { _thread?.Join(3000); } catch { }
        _thread = null;
    }
}
