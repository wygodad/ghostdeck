using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>Per-second FPS snapshot of the foreground game (built by <see cref="FpsMonitor"/>).</summary>
public readonly record struct FpsSnapshot(
    int Pid, string Process,
    int Fps,             // frames presented in the last second
    double FrameTimeMs,  // average frametime over the last second
    int P1LowFps,        // 1% low over the last ~30 s (99th-percentile frametime)
    int Stutters30s);    // frames > max(25 ms, 2x median) in the last ~30 s

/// <summary>Summary of one finished game session (FPS side filled by the monitor; the EC side —
/// temps / RPM / profile from <see cref="HwHistory"/> — is added by the tray before publishing).
/// Spark/SparkPeak: averaged / peak frametime buckets of the session's closing window, feeding
/// the sparkline on the report popup and the data export.</summary>
public sealed record GameSession(
    string Process, DateTime Start, DateTime End,
    int AvgFps, int MinFps, int MaxFps, int P1LowFps, long Frames, long Stutters,
    int MaxCpuTemp = 0, int MaxGpuTemp = 0, int AvgCpuRpm = 0, int AvgGpuRpm = 0, string Profile = "",
    float[]? Spark = null, float[]? SparkPeak = null);

/// <summary>
/// FPS / frametime of any game via a private real-time ETW session — the same user-mode source
/// PresentMon uses (see TECHNICAL §28). We enable the Microsoft-Windows-DXGI and Microsoft-Windows-D3D9
/// providers and count their Present-start events per process: no kernel driver, no DLL injection,
/// nothing touches the game — deliberately anti-cheat-safe, same "no kernel driver" ethos as Perf.
///
/// The session runs ONLY while someone is looking (gaming overlay visible with an FPS metric enabled,
/// or the Status → Gaming sub-tab open); otherwise the cost is zero. The overlay value follows the
/// foreground window's PID; sessions keep counting per PID in the background, so alt-tab doesn't
/// split a game session. Consumes ~one ETW event per presented frame.
/// </summary>
public static class FpsMonitor
{
    // ---------------- public surface ----------------

    /// <summary>Last finished game session (FPS + EC summary), shown on Status → Gaming.</summary>
    public static GameSession? LastSession { get; set; }

    /// <summary>Raised on a worker thread when a tracked game exits (marshal to the UI yourself).</summary>
    public static event Action<GameSession>? SessionEnded;

    /// <summary>Snapshot for the foreground game, or null when nothing is presenting. ~1 s fresh.</summary>
    public static FpsSnapshot? Current { get { lock (_lock) return _current; } }

    /// <summary>Convenience for the HW sampler: current FPS or -1 (no game / monitor off).</summary>
    public static int CurrentFps { get { lock (_lock) return _current?.Fps ?? -1; } }

    /// <summary>Diagnostic state of the ETW plumbing: "off", "ok", or the failing stage + error
    /// code. Shown on Status → Gaming (deliberately untranslated, like CLI output).</summary>
    public static string DiagStatus { get; private set; } = "off";

    /// <summary>Present events accepted by the callback since the session started (any PID).</summary>
    public static long EventsSeen => Interlocked.Read(ref _eventsSeen);
    private static long _eventsSeen;

    /// <summary>Process name of the current (sticky) target — diagnostics; "" when none.</summary>
    public static string TargetName
    {
        get { lock (_lock) return _targetPid != 0 && _watch.TryGetValue(_targetPid, out var t) ? t.Name : ""; }
    }

    /// <summary>Turn the whole monitor on/off (ETW session + 1 s aggregation timer).</summary>
    public static void SetActive(bool on)
    {
        lock (_startLock)
        {
            if (on == _active) return;
            if (on) { _active = StartSession(); }
            else { _active = false; StopSession(); }   // flag first: a racing Tick() bails out
        }
    }

    /// <summary>Stop a session a crashed previous instance left behind (call once at app start).</summary>
    public static void StopOrphan() => ControlStop();

    /// <summary>App exit: stop the ETW session and flush any open game sessions.</summary>
    public static void Shutdown() => SetActive(false);

    /// <summary>Frametimes of the foreground game over the last <paramref name="seconds"/> (for the chart).</summary>
    public static List<(DateTime Time, float Ms)> RecentFrames(int seconds)
    {
        var list = new List<(DateTime, float)>();
        long nowQpc = Stopwatch.GetTimestamp();
        var now = DateTime.Now;
        long cut = nowQpc - seconds * Stopwatch.Frequency;
        lock (_lock)
        {
            if (_targetPid == 0 || !_watch.TryGetValue(_targetPid, out var st)) return list;
            foreach (var (qpc, ms) in st.Frames)
                if (qpc >= cut)
                    list.Add((now - TimeSpan.FromSeconds((nowQpc - qpc) / (double)Stopwatch.Frequency), ms));
        }
        return list;
    }

    // ---------------- state ----------------

    private const string SessionName = "GhostDeck-Present";
    private const int WindowSeconds = 30;        // rolling window for 1% low / stutter
    private const int ChartSeconds = 65;         // frames kept in memory for the 60 s chart
    private const int RateWindowSeconds = 3;     // FPS/frametime window (event timestamps, lag-proof)
    private const int FreshSeconds = 5;          // newest frame at most this old to count as "playing"
    private const int PromoteSeconds = 10;       // continuous presenting before a PID becomes a session
    private const int MinSessionSeconds = 45;    // shorter runs are not reported
    private const int MinSessionFrames = 500;
    private const int HistoBuckets = 512;        // 0.25 ms buckets -> 0..128 ms

    // never treated as game sessions (they present frames all the time); overlay still shows their FPS
    private static readonly string[] NoSession =
        { "explorer", "dwm", "ghostdeck", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi" };

    // the shell never becomes the target — alt-tabbing to the desktop keeps the game's stats up
    private static readonly string[] Shell =
        { "explorer", "dwm", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost" };
    private static bool IsShell(string name) => Shell.Contains(name, StringComparer.OrdinalIgnoreCase);

    private sealed class PidStat
    {
        public string Name = "";
        public long PrevQpc;
        public readonly List<(long Qpc, float Ms)> Frames = new();   // trimmed to WindowSeconds
        public DateTime Start = DateTime.Now;
        public DateTime LastFrameAt = DateTime.Now;
        public long TotalFrames;                 // bumped in the ETW callback
        public long CountedFrames;               // consumed by the per-second tick (stutter/session math)
        public int ActiveSeconds;
        public int MinFps = int.MaxValue, MaxFps;
        public long Stutters;
        public readonly int[] Histo = new int[HistoBuckets];
        public long HistoCount;
        public bool IsSession;
        public int PromoteStreak;
        public int IdleTicks;
        public int CurFps;                       // rate over the last RateWindowSeconds (event time)
        public double CurFrameMs;
        public bool CurFresh;                    // newest frame recent enough to be "live"
        public long ScanQpc;                     // stutter accumulator high-water mark
    }

    private static readonly object _lock = new();       // guards _watch / _current / _targetPid
    private static readonly object _startLock = new();  // serialises SetActive
    private static readonly Dictionary<int, PidStat> _watch = new();
    private static FpsSnapshot? _current;
    private static int _targetPid;
    private static bool _active;
    private static int _ticking;

    private static ulong _session;
    private static ulong _consumer = INVALID_HANDLE;
    private static Thread? _thread;
    private static System.Threading.Timer? _timer;
    private static EventRecordCallbackDelegate? _cb;     // kept alive for the native callback

    // Microsoft-Windows-DXGI (IDXGISwapChain::Present start = event 42) and
    // Microsoft-Windows-D3D9 (Present start = event 1) — the providers PresentMon listens to.
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");

    // ---------------- session lifecycle ----------------

    private static bool StartSession()
    {
        try
        {
            Interlocked.Exchange(ref _eventsSeen, 0);
            ControlStop();   // clear an orphan (crash) or a stale copy of our own session
            IntPtr props = AllocProps();
            try
            {
                uint err = StartTraceW(out _session, SessionName, props);
                if (err == ERROR_ALREADY_EXISTS) { ControlStop(); err = StartTraceW(out _session, SessionName, props); }
                if (err != 0) { DiagStatus = $"StartTrace error {err}"; return false; }
            }
            finally { Marshal.FreeHGlobal(props); }

            var dxgi = DxgiProvider; var d3d9 = D3d9Provider;
            uint e1 = EnableTraceEx2(_session, ref dxgi, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, ulong.MaxValue, 0, 0, IntPtr.Zero);
            uint e2 = EnableTraceEx2(_session, ref d3d9, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, ulong.MaxValue, 0, 0, IntPtr.Zero);
            if (e1 != 0 || e2 != 0) { DiagStatus = $"EnableTrace error DXGI={e1} D3D9={e2}"; ControlStop(); return false; }

            _cb = OnEvent;
            // RAW_TIMESTAMP keeps EventHeader.TimeStamp as QPC ticks (ClientContext=1) — without
            // it ProcessTrace converts to FILETIME and every Stopwatch.Frequency delta is wrong.
            var log = new EVENT_TRACE_LOGFILEW
            {
                LoggerName = SessionName,
                ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD | PROCESS_TRACE_MODE_RAW_TIMESTAMP,
                EventRecordCallback = _cb,
            };
            // the ByValArray fields must not be null or the interop marshaller throws on the call
            log.LogfileHeader.TimeZone.StandardName = new ushort[32];
            log.LogfileHeader.TimeZone.DaylightName = new ushort[32];
            _consumer = OpenTraceW(ref log);
            if (_consumer == INVALID_HANDLE)
            {
                DiagStatus = $"OpenTrace error {Marshal.GetLastWin32Error()}";
                ControlStop();
                return false;
            }

            _thread = new Thread(() =>
            {
                try
                {
                    uint pe = ProcessTrace(new[] { _consumer }, 1, IntPtr.Zero, IntPtr.Zero);
                    // normal stop paths return 0 / ERROR_CANCELLED(1223); anything else is a fault
                    if (pe != 0 && pe != 1223) DiagStatus = $"ProcessTrace error {pe}";
                }
                catch (Exception ex) { DiagStatus = "ProcessTrace threw: " + ex.Message; }
            })
            { IsBackground = true, Name = "GhostDeckFps" };
            _thread.Start();
            _timer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
            DiagStatus = "ok";
            return true;
        }
        catch (Exception ex) { DiagStatus = "start threw: " + ex.Message; return false; }
    }

    private static void StopSession()
    {
        try
        {
            _timer?.Dispose(); _timer = null;
            ControlStop();
            if (_consumer != INVALID_HANDLE) { CloseTrace(_consumer); _consumer = INVALID_HANDLE; }
            _session = 0;
            List<GameSession> flush;
            lock (_lock)
            {
                flush = FinalizeDeadLocked(all: true);
                _watch.Clear();
                _current = null;
                _targetPid = 0;
            }
            DiagStatus = "off";
            foreach (var s in flush) SessionEnded?.Invoke(s);   // don't lose an open session on overlay-off
        }
        catch { }
    }

    private static void ControlStop()
    {
        try
        {
            IntPtr props = AllocProps();
            try { ControlTraceW(0, SessionName, props, EVENT_TRACE_CONTROL_STOP); }
            finally { Marshal.FreeHGlobal(props); }
        }
        catch { }
    }

    // ---------------- ETW event callback (hot path — keep it tiny) ----------------

    private static void OnEvent(IntPtr record)
    {
        var h = Marshal.PtrToStructure<EVENT_HEADER>(record);
        ushort id = h.Descriptor.Id;
        if (!((id == 42 && h.ProviderId == DxgiProvider) || (id == 1 && h.ProviderId == D3d9Provider))) return;
        Interlocked.Increment(ref _eventsSeen);
        lock (_lock)
        {
            if (!_watch.TryGetValue((int)h.ProcessId, out var st)) return;
            if (st.PrevQpc > 0)
            {
                float ms = (float)((h.TimeStamp - st.PrevQpc) * 1000.0 / Stopwatch.Frequency);
                if (ms > 0f && ms < 10000f)
                {
                    st.Frames.Add((h.TimeStamp, ms));
                    st.TotalFrames++;
                    int b = Math.Min((int)(ms * 4), HistoBuckets - 1);
                    st.Histo[b]++; st.HistoCount++;
                }
            }
            st.PrevQpc = h.TimeStamp;
        }
    }

    // ---------------- 1 s aggregation tick ----------------

    private static void Tick()
    {
        if (!_active) return;   // shutting down: don't repopulate _watch / _current after the clear
        if (Interlocked.Exchange(ref _ticking, 1) != 0) return;
        try
        {
            int fgPid = ForegroundPid();
            string fgName = "";
            bool fgUsable = fgPid > 4 && fgPid != Environment.ProcessId;
            if (fgUsable)
            {
                bool known;
                lock (_lock) known = _watch.ContainsKey(fgPid);
                if (!known)
                {
                    try { using var p = Process.GetProcessById(fgPid); fgName = p.ProcessName; }
                    catch { fgUsable = false; }
                }
            }

            List<GameSession> ended;
            lock (_lock)
            {
                if (fgUsable && !_watch.ContainsKey(fgPid))
                    _watch[fgPid] = new PidStat { Name = fgName };

                // Target selection is STICKY: alt-tabbing to the desktop / explorer / GhostDeck
                // itself keeps the last game as the target while it lives and keeps presenting.
                // A foreground app takes the target over only when it actually presents frames
                // (or the current target went quiet) — otherwise a screenshot tool or any plain
                // window grabbing focus for a moment would blank the chart it can't feed.
                if (fgUsable && _watch.TryGetValue(fgPid, out var fgSt) && !IsShell(fgSt.Name))
                {
                    bool curFresh = _targetPid != 0 && _watch.TryGetValue(_targetPid, out var curT) && curT.IdleTicks < 5;
                    if (fgPid == _targetPid || fgSt.CurFresh || !curFresh)
                        _targetPid = fgPid;
                }
                else if (_targetPid != 0 && (!_watch.TryGetValue(_targetPid, out var oldT) || oldT.IdleTicks >= 10))
                    _targetPid = 0;

                long nowQpc = Stopwatch.GetTimestamp();
                long freq = Stopwatch.Frequency;
                long cut = nowQpc - ChartSeconds * freq;              // keep enough for the chart
                long statCut = nowQpc - WindowSeconds * freq;         // stats stay on 30 s
                long rateWin = nowQpc - RateWindowSeconds * freq;
                long freshCut = nowQpc - FreshSeconds * freq;

                foreach (var (pid, st) in _watch)
                {
                    st.Frames.RemoveAll(f => f.Qpc < cut);
                    long newFrames = st.TotalFrames - st.CountedFrames;
                    st.CountedFrames = st.TotalFrames;

                    // Rate from EVENT timestamps over the last few seconds — NOT "frames in the
                    // last wall-clock second": real-time ETW flushes its buffers up to ~1 s late,
                    // so the last wall-clock second is usually still empty even at 300 FPS (that
                    // lag made the 60 s chart work while every live number stayed "--").
                    int n = 0; double sumMs = 0; long first = 0, last = 0;
                    float median = MedianLocked(st.Frames, statCut);
                    float stutterAt = Math.Max(25f, 2f * median);
                    foreach (var (qpc, ms) in st.Frames)
                    {
                        if (qpc < rateWin) continue;
                        if (n == 0) first = qpc;
                        last = qpc; n++; sumMs += ms;
                    }
                    if (n >= 2 && last > first)
                    {
                        st.CurFps = (int)Math.Round((n - 1) * (double)freq / (last - first));
                        st.CurFrameMs = sumMs / n;
                    }
                    else { st.CurFps = 0; st.CurFrameMs = 0; }
                    st.CurFresh = st.CurFps > 0 && last >= freshCut;

                    // session accumulators: stutters among frames not scanned yet, min/max FPS
                    if (median > 0)
                        foreach (var (qpc, ms) in st.Frames)
                            if (qpc > st.ScanQpc && ms > stutterAt) st.Stutters++;
                    if (st.Frames.Count > 0) st.ScanQpc = Math.Max(st.ScanQpc, st.Frames[^1].Qpc);

                    if (newFrames > 0)
                    {
                        st.LastFrameAt = DateTime.Now;
                        st.ActiveSeconds++;
                        st.IdleTicks = 0;
                    }
                    else st.IdleTicks++;
                    if (st.CurFresh)
                    {
                        st.MinFps = Math.Min(st.MinFps, st.CurFps);
                        st.MaxFps = Math.Max(st.MaxFps, st.CurFps);
                    }

                    // promotion: the target has to keep presenting for a while before it counts
                    // as a "game" (skips launchers, file dialogs, the desktop itself)
                    if (pid == _targetPid && st.CurFps >= 5) st.PromoteStreak++;
                    else if (st.CurFps < 5) st.PromoteStreak = 0;
                    if (!st.IsSession && st.PromoteStreak >= PromoteSeconds &&
                        !NoSession.Contains(st.Name, StringComparer.OrdinalIgnoreCase))
                        st.IsSession = true;
                }

                // live snapshot for the overlay / Gaming tab / CLI (sticky target)
                _current = null;
                if (_targetPid > 0 && _watch.TryGetValue(_targetPid, out var tgt) && tgt.CurFresh)
                    _current = new FpsSnapshot(_targetPid, tgt.Name, tgt.CurFps, tgt.CurFrameMs,
                        P1LowLocked(tgt.Frames, statCut), Stutters30sLocked(tgt.Frames, statCut));

                ended = FinalizeDeadLocked(all: false);
            }
            foreach (var s in ended) SessionEnded?.Invoke(s);
        }
        catch { }
        finally { Interlocked.Exchange(ref _ticking, 0); }
    }

    // Finalize sessions whose process has exited (or everything on shutdown). Caller holds _lock.
    private static List<GameSession> FinalizeDeadLocked(bool all)
    {
        var done = new List<GameSession>();
        List<int>? drop = null;
        foreach (var (pid, st) in _watch)
        {
            bool dead = all;
            // also checked while the PID is still the (sticky) target — a closed game should
            // publish its session report within a few seconds, not when the target lets go
            if (!dead && st.IdleTicks >= 3)
            {
                try { using var p = Process.GetProcessById(pid); dead = p.HasExited; }
                catch { dead = true; }
            }
            if (!dead)
            {
                // an idle non-session PID (file manager, launcher that stopped presenting) is just dropped
                if (!st.IsSession && pid != _targetPid && st.IdleTicks >= 10) (drop ??= new()).Add(pid);
                continue;
            }
            (drop ??= new()).Add(pid);
            var dur = st.LastFrameAt - st.Start;
            if (!st.IsSession || dur.TotalSeconds < MinSessionSeconds || st.TotalFrames < MinSessionFrames) continue;
            int avg = st.ActiveSeconds > 0 ? (int)(st.TotalFrames / st.ActiveSeconds) : 0;
            var (spark, sparkPeak) = BuildSpark(st.Frames);
            done.Add(new GameSession(st.Name, st.Start, st.LastFrameAt,
                avg, st.MinFps == int.MaxValue ? 0 : st.MinFps, st.MaxFps,
                P1FromHisto(st.Histo, st.HistoCount), st.TotalFrames, st.Stutters,
                Spark: spark, SparkPeak: sparkPeak));
        }
        if (drop != null) foreach (int pid in drop) _watch.Remove(pid);
        return done;
    }

    // Downsample the (up to 30 s) frame window into fixed buckets: average = the line on the
    // report popup, peak = the stutter dots. Caller holds _lock.
    private static (float[]?, float[]?) BuildSpark(List<(long Qpc, float Ms)> frames)
    {
        const int N = 120;
        if (frames.Count < 8) return (null, null);
        long a = frames[0].Qpc, b = frames[^1].Qpc;
        if (b <= a) return (null, null);
        var sum = new float[N]; var cnt = new int[N]; var peak = new float[N];
        foreach (var (q, ms) in frames)
        {
            int i = (int)((q - a) * (N - 1) / (b - a));
            sum[i] += ms; cnt[i]++;
            if (ms > peak[i]) peak[i] = ms;
        }
        var avg = new float[N];
        float prev = 0;
        for (int i = 0; i < N; i++)
        {
            if (cnt[i] > 0) { avg[i] = sum[i] / cnt[i]; prev = avg[i]; }
            else { avg[i] = prev; peak[i] = prev; }   // gap: carry the line, no false spike
        }
        return (avg, peak);
    }

    // ---------------- math helpers (caller holds _lock) ----------------

    // The frame list holds ChartSeconds of data; the stats helpers work on the trailing
    // WindowSeconds only (fromQpc), so the 60 s chart doesn't stretch the 30 s statistics.
    private static float[] SortedMsFrom(List<(long Qpc, float Ms)> frames, long fromQpc)
    {
        int n = 0;
        for (int i = frames.Count - 1; i >= 0 && frames[i].Qpc >= fromQpc; i--) n++;   // chronological list
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = frames[frames.Count - n + i].Ms;
        Array.Sort(a);
        return a;
    }

    private static float MedianLocked(List<(long Qpc, float Ms)> frames, long fromQpc)
    {
        var a = SortedMsFrom(frames, fromQpc);
        return a.Length == 0 ? 0 : a[a.Length / 2];
    }

    private static int P1LowLocked(List<(long Qpc, float Ms)> frames, long fromQpc)
    {
        var a = SortedMsFrom(frames, fromQpc);
        if (a.Length < 100) return 0;   // below ~100 frames a p99 is noise
        float p99 = a[Math.Min(a.Length - 1, (int)(a.Length * 0.99))];
        return p99 > 0 ? (int)Math.Round(1000f / p99) : 0;
    }

    private static int Stutters30sLocked(List<(long Qpc, float Ms)> frames, long fromQpc)
    {
        float median = MedianLocked(frames, fromQpc);
        if (median <= 0) return 0;
        float at = Math.Max(25f, 2f * median);
        int n = 0;
        for (int i = frames.Count - 1; i >= 0 && frames[i].Qpc >= fromQpc; i--)
            if (frames[i].Ms > at) n++;
        return n;
    }

    private static int P1FromHisto(int[] histo, long count)
    {
        if (count < 100) return 0;
        long need = (long)(count * 0.99);
        long acc = 0;
        for (int i = 0; i < histo.Length; i++)
        {
            acc += histo[i];
            if (acc >= need)
            {
                float ms = (i + 1) / 4f;   // bucket upper edge (0.25 ms buckets)
                return (int)Math.Round(1000f / ms);
            }
        }
        return 0;
    }

    private static int ForegroundPid()
    {
        try
        {
            IntPtr w = GetForegroundWindow();
            if (w == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(w, out uint pid);
            return (int)pid;
        }
        catch { return 0; }
    }

    // ---------------- ETW P/Invoke plumbing ----------------

    private const uint EVENT_TRACE_CONTROL_STOP = 1;
    private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
    private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    private const uint PROCESS_TRACE_MODE_RAW_TIMESTAMP = 0x00001000;
    private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
    private const byte TRACE_LEVEL_INFORMATION = 4;
    private const uint ERROR_ALREADY_EXISTS = 183;
    private const ulong INVALID_HANDLE = ulong.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNODE_HEADER
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;   // 1 = QPC timestamps
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE_PROPERTIES
    {
        public WNODE_HEADER Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    // The session-name buffer lives right after the struct; StartTrace/ControlTrace fill it.
    private static IntPtr AllocProps()
    {
        int structSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
        int size = structSize + 2048;
        IntPtr p = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, p, size);
        var props = new EVENT_TRACE_PROPERTIES();
        props.Wnode.BufferSize = (uint)size;
        props.Wnode.ClientContext = 1;                       // QPC — matches Stopwatch.Frequency
        props.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
        props.BufferSize = 64;                               // KB per buffer
        // Min/MaximumBuffers stay 0 = system defaults. ETW requires roughly two buffers per
        // logical processor; a hardcoded small pair fails StartTrace with error 87 on big
        // CPUs (32 threads on the GE78HX), which silently killed the whole feature.
        props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
        props.FlushTimer = 1;                                // deliver events at most 1 s late
        props.LogFileNameOffset = 0;
        props.LoggerNameOffset = (uint)structSize;
        Marshal.StructureToPtr(props, p, false);
        return p;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_DESCRIPTOR
    {
        public ushort Id;
        public byte Version, Channel, Level, Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    // EVENT_RECORD starts with this header — it's all the callback needs (PID + QPC + provider + id).
    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_HEADER
    {
        public ushort Size, HeaderType, Flags, EventProperty;
        public uint ThreadId, ProcessId;
        public long TimeStamp;
        public Guid ProviderId;
        public EVENT_DESCRIPTOR Descriptor;
        public ulong ProcessorTime;
        public Guid ActivityId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE_HEADER
    {
        public ushort Size;
        public byte HeaderType, MarkerFlags;
        public uint Version;
        public uint ThreadId, ProcessId;
        public long TimeStamp;
        public Guid Guid;
        public ulong ProcessorTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE
    {
        public EVENT_TRACE_HEADER Header;
        public uint InstanceId, ParentInstanceId;
        public Guid ParentGuid;
        public IntPtr MofData;
        public uint MofLength, ClientContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TIME_ZONE_INFORMATION
    {
        public int Bias;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public ushort[] StandardName;
        public SYSTEMTIME StandardDate;
        public int StandardBias;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public ushort[] DaylightName;
        public SYSTEMTIME DaylightDate;
        public int DaylightBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACE_LOGFILE_HEADER
    {
        public uint BufferSize, Version, ProviderVersion, NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution, MaximumFileSize, LogFileMode, BuffersWritten;
        public Guid LogInstanceGuid;
        public IntPtr LoggerName, LogFileName;
        public TIME_ZONE_INFORMATION TimeZone;
        public long BootTime, PerfFreq, StartTime;
        public uint ReservedFlags, BuffersLost;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EventRecordCallbackDelegate(IntPtr eventRecord);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EVENT_TRACE_LOGFILEW
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? LogFileName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LoggerName;
        public long CurrentTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public EVENT_TRACE CurrentEvent;
        public TRACE_LOGFILE_HEADER LogfileHeader;
        public IntPtr BufferCallback;
        public uint BufferSize, Filled, EventsLost;
        public EventRecordCallbackDelegate? EventRecordCallback;
        public uint IsKernelTrace;
        public IntPtr Context;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(ulong sessionHandle, string? sessionName, IntPtr properties, uint controlCode);
    [DllImport("advapi32.dll")]
    private static extern uint EnableTraceEx2(ulong sessionHandle, ref Guid providerId, uint controlCode, byte level, ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILEW logfile);
    [DllImport("advapi32.dll")]
    private static extern uint ProcessTrace(ulong[] handles, uint handleCount, IntPtr startTime, IntPtr endTime);
    [DllImport("advapi32.dll")]
    private static extern uint CloseTrace(ulong traceHandle);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
