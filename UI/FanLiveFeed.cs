namespace GhostDeck;

/// <summary>
/// One reading of what the fans are doing right now, as the Fan-curve views need it:
/// the fan-mode byte (is the custom curve engaged), both temperatures, both duty values,
/// both tachometers when the model has them. Temps &lt;= 0 mean "no reading" (a sleeping dGPU
/// reports 0); RPM 0 means no tach on this model, fan stopped, or an implausible divisor.
/// </summary>
public readonly record struct FanLiveSample(DateTime Time, byte FanMode, int CpuTemp, int GpuTemp,
                                            int CpuFan, int GpuFan, int CpuRpm, int GpuRpm);

/// <summary>
/// The Fan-curve page's single background reader. One WinForms timer, one worker at a time,
/// one EC round-trip set per tick, one <see cref="Sample"/> for every view to consume, plus a
/// short ring of past samples for the operating-point trail. Replaces the page's earlier
/// 1.2 s mode-only timer: the mode byte still arrives every tick, the rest rides along.
///
/// Thread rules (RENDERING §4, StatusPage.RefreshAsync): EC reads happen on a Task, results
/// are posted back with BeginInvoke on the owner control, an Interlocked flag drops a tick
/// while the previous one is still in flight, and BeginInvoke is guarded because the page
/// may be disposed mid-flight. The feed itself never touches the UI thread with WMI.
/// </summary>
public sealed class FanLiveFeed : IDisposable
{
    private readonly Control _owner;
    private readonly Func<DeviceProfile?> _dev;   // re-resolved each tick: the model DB can swap
    private readonly Func<bool> _enabled;         // Known && !Simulating - the page decides
    private readonly System.Windows.Forms.Timer _timer;
    private int _busy;
    private readonly List<FanLiveSample> _ring = new();
    private static readonly TimeSpan TrailSpan = TimeSpan.FromMinutes(3);

    /// <summary>Latest reading; <c>default</c> (Time == MinValue) until the first tick lands.</summary>
    public FanLiveSample Sample { get; private set; }

    /// <summary>Raised on the UI thread after every landed sample.</summary>
    public event Action? Updated;

    public FanLiveFeed(Control owner, Func<DeviceProfile?> dev, Func<bool> enabled, int intervalMs = 1500)
    {
        _owner = owner;
        _dev = dev;
        _enabled = enabled;
        _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() { _timer.Start(); Poll(); }
    public void Stop() => _timer.Stop();

    /// <summary>Samples from the last three minutes, oldest first (a copy).</summary>
    public IReadOnlyList<FanLiveSample> Trail()
    {
        lock (_ring) return _ring.ToList();
    }

    /// <summary>Forget the trail (e.g. after a model-database swap: old samples map to old addresses).</summary>
    public void ClearTrail() { lock (_ring) _ring.Clear(); }

    /// <summary>Force a read now (after a write, so the mode byte reflects it without waiting a tick).</summary>
    public void Poll()
    {
        if (!_enabled() || _dev() is not { } dev) return;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        Task.Run(() =>
        {
            FanLiveSample? s = null;
            try
            {
                // Mode + the four sensor bytes are cheap (5 round-trips); the tachometers ride on
                // the full snapshot only where the model has them (RPM addresses are per model).
                bool tach = dev.CpuRpmAddr != 0 || dev.GpuRpmAddr != 0;
                if (tach && Ec.TryReadHw(dev, out var hw))
                {
                    byte mode = Ec.ReadByte(dev.FanMode);
                    s = new FanLiveSample(DateTime.Now, mode, hw.CpuTemp, hw.GpuTemp, hw.CpuFan, hw.GpuFan, hw.CpuRpm, hw.GpuRpm);
                }
                else
                {
                    var b = Ec.ReadMany(new[] { dev.FanMode, dev.CpuTemp, dev.GpuTemp, dev.CpuFan, dev.GpuFan });
                    s = new FanLiveSample(DateTime.Now, b[0], b[1], b[2], Math.Min(100, (int)b[3]), Math.Min(100, (int)b[4]), 0, 0);
                }
            }
            catch { }
            try
            {
                _owner.BeginInvoke(() =>
                {
                    _busy = 0;
                    if (s is not { } v) return;
                    Sample = v;
                    lock (_ring)
                    {
                        _ring.Add(v);
                        var cut = v.Time - TrailSpan;
                        int drop = 0;
                        while (drop < _ring.Count && _ring[drop].Time < cut) drop++;
                        if (drop > 0) _ring.RemoveRange(0, drop);
                    }
                    Updated?.Invoke();
                });
            }
            catch { _busy = 0; }   // owner disposed mid-flight
        });
    }

    public void Dispose() => _timer.Dispose();
}
