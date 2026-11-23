namespace GhostDeck;

public sealed record CoolingBaseline(FanCurvePreset Curve, byte Mode);
public readonly record struct CoolingReading(DateTimeOffset Time, HwSnapshot Hardware, byte Mode, bool Boost);
public sealed record CoolingStatus(bool Enabled, string Key, string Error = "", CoolingReading? Reading = null,
    CoolingDemand? Demand = null, DateTimeOffset? LastWrite = null);

/// <summary>Only the existing fan-table and fan-mode operations. No direct duty/PWM registers.</summary>
public interface ICrossCoolingHardware
{
    byte AdvancedMode { get; }
    CoolingBaseline Capture();
    CoolingReading Read();
    void Apply(FanCurvePreset original, int[] cpuSpeeds, int[] gpuSpeeds);
    void Restore(CoolingBaseline baseline);
    void Automatic();
}

/// <summary>
/// One owner for the full read/calculate/write transaction. External EC sessions synchronously
/// suspend it before starting, so diagnostics/profile writes cannot overlap a pending cooling write.
/// A failed restoration retains its baseline for retry; a native-auto fallback is attempted too.
/// </summary>
public sealed class CrossCoolingController : IDisposable
{
    private readonly object _gate = new();
    private readonly ICrossCoolingHardware _hardware;
    private readonly int _max;
    private readonly Action<string, string> _log;
    private readonly CrossCoolingScheduler _scheduler = new();
    private CrossCoolingSettings _settings = new();
    private CoolingBaseline? _baseline;
    private bool _dirty, _disposed, _suspended;
    private CoolingStatus _status = new(false, "cc_disabled");
    public CrossCoolingController(ICrossCoolingHardware hardware, int max, Action<string, string>? log = null)
    { _hardware = hardware; _max = max; _log = log ?? ((_, _) => { }); }

    public CoolingStatus Status => Volatile.Read(ref _status);
    public FanCurvePreset? Baseline { get { lock (_gate) return _baseline?.Curve.Clone(); } }

    public void Configure(CrossCoolingSettings settings, bool enabled)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var next = settings.Clone(); next.Normalize();
            if (_status.Enabled && enabled && next.Mode != CrossCoolingMode.Off && _status.Key != "cc_restore_failed")
            {
                // Slider changes keep the current schedule; dragging cannot bypass the write interval.
                _settings = next;
                return;
            }
            bool restored = Restore();
            _settings = next;
            bool on = enabled && _settings.Mode != CrossCoolingMode.Off;
            _suspended = false;
            _status = new(on, restored ? on ? "cc_starting" : "cc_disabled" : "cc_restore_failed", _status.Error);
            _log(on ? "Cross-Cooling enabled" : "Cross-Cooling disabled", _settings.Mode + ", " + _settings.Algorithm);
        }
    }

    public void Tick(DateTimeOffset? now = null, string? blocked = null)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_suspended) return;
            if (!_status.Enabled || blocked != null)
            {
                bool ok = Restore();
                if (ok && (_status.Enabled || _status.Key == "cc_restore_failed"))
                    _status = _status with { Key = _status.Enabled ? blocked ?? "cc_disabled" : "cc_disabled", Error = "", Demand = null };
                return;
            }
            try
            {
                // Retry an earlier failed restoration before attempting any new control.
                if (_status.Key == "cc_restore_failed" && !Restore()) return;
                var reading = _hardware.Read();
                var hw = reading.Hardware;
                _status = _status with { Reading = reading };
                if (reading.Boost) { Suspend("cc_boost_paused"); return; }
                if (_dirty && reading.Mode != _hardware.AdvancedMode)
                {
                    // Keep the newly selected native mode when relinquishing ownership.
                    if (_baseline != null) _baseline = _baseline with { Mode = reading.Mode };
                    Suspend("cc_mode_changed"); _status = _status with { Enabled = false }; return;
                }
                _baseline ??= _hardware.Capture();
                var c = _baseline.Curve;
                var checkedAt = now ?? DateTimeOffset.UtcNow;
                var plan = _scheduler.Sample(hw.CpuTemp, hw.GpuTemp, reading.Time, checkedAt,
                    c.CpuTemp, c.CpuSpeed, c.GpuTemp, c.GpuSpeed, _max, _settings);
                if (plan == null) { Suspend("cc_sensor_paused"); return; }
                if (plan.PlanChanged)
                {
                    _dirty = true; // even a partially failed table write must be restored
                    _hardware.Apply(c, plan.CpuTable, plan.GpuTable);
                    _status = _status with { LastWrite = checkedAt };
                    _log("Cross-Cooling applied", $"CPU {plan.Demand.CpuTarget:0.#}%, GPU {plan.Demand.GpuTarget:0.#}%");
                }
                _status = _status with { Key = "cc_running", Error = "", Demand = plan.Demand };
            }
            catch (Exception ex)
            {
                _log("Cross-Cooling failed", ex.Message);
                Restore();
                // A write/read exception disarms until the user explicitly retries.
                _status = _status with { Enabled = false, Key = _dirty ? "cc_restore_failed" : "cc_failed", Error = ex.Message, Demand = null };
            }
        }
    }

    public void Suspend(string reason = "cc_session_paused")
    {
        lock (_gate)
        {
            _suspended = true;
            if (Restore()) _status = _status with { Key = reason, Demand = null, Error = "" };
        }
    }

    public void Resume() { lock (_gate) { if (!_disposed) _suspended = false; } }

    private bool Restore()
    {
        if (_baseline == null) { _scheduler.Reset(); return true; }
        try
        {
            if (_dirty) { _hardware.Restore(_baseline); _log("Cross-Cooling restored", "Original tables and fan mode"); }
            _baseline = null; _dirty = false; _scheduler.Reset();
            return true;
        }
        catch (Exception ex)
        {
            _log("Cross-Cooling restore failed", ex.Message);
            try { _hardware.Automatic(); } catch (Exception fallback) { _log("Cross-Cooling auto fallback failed", fallback.Message); }
            _status = _status with { Key = "cc_restore_failed", Error = ex.Message, Demand = null };
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _status = _status with { Enabled = false }; Restore(); _disposed = true; }
    }
}
