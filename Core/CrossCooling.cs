using System.Text.Json.Serialization;

namespace GhostDeck;

public enum CrossCoolingMode { Off, CpuToGpu, GpuToCpu, Both }
public enum CoolingMix { Maximum, Average, WeightedAverage, PriorityBoost }

/// <summary>Portable cooling preferences, including the user's saved activation choice.</summary>
public sealed class CrossCoolingSettings
{
    public bool Enabled { get; set; }
    public CrossCoolingMode Mode { get; set; }
    public CoolingMix Algorithm { get; set; }
    public int CpuWeight { get; set; } = 50;
    public int GpuWeight { get; set; } = 50;
    public int BoostPercent { get; set; } = 100;
    public int SamplingSeconds { get; set; } = 2;
    public int UpdateSeconds { get; set; } = 5;
    public int Threshold { get; set; } = 5;
    public int IncreaseHysteresis { get; set; } = 0;
    public int DecreaseHysteresis { get; set; } = 5;
    public int DecreaseStep { get; set; } = 5;
    public bool Smoothing { get; set; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    public CrossCoolingSettings Clone() => new()
    {
        Enabled = Enabled, Mode = Mode, Algorithm = Algorithm, CpuWeight = CpuWeight, GpuWeight = GpuWeight,
        BoostPercent = BoostPercent, SamplingSeconds = SamplingSeconds, UpdateSeconds = UpdateSeconds,
        Threshold = Threshold, IncreaseHysteresis = IncreaseHysteresis, DecreaseHysteresis = DecreaseHysteresis,
        DecreaseStep = DecreaseStep, Smoothing = Smoothing, Extra = Extra == null ? null : new(Extra),
    };

    public void Normalize()
    {
        if (!Enum.IsDefined(Mode)) Mode = CrossCoolingMode.Off;
        if (Mode == CrossCoolingMode.Off) Enabled = false;
        if (!Enum.IsDefined(Algorithm)) Algorithm = CoolingMix.Maximum;
        CpuWeight = Math.Clamp(CpuWeight, 0, 100); GpuWeight = Math.Clamp(GpuWeight, 0, 100);
        if (CpuWeight + GpuWeight == 0) { CpuWeight = 50; GpuWeight = 50; }
        BoostPercent = Math.Clamp(BoostPercent, 0, 100);
        SamplingSeconds = Math.Clamp(SamplingSeconds, 1, 10);
        UpdateSeconds = Math.Clamp(UpdateSeconds, 5, 60);
        Threshold = Math.Clamp(Threshold, 1, 20);
        IncreaseHysteresis = Math.Clamp(IncreaseHysteresis, 0, 10);
        DecreaseHysteresis = Math.Clamp(DecreaseHysteresis, 0, 20);
        DecreaseStep = Math.Clamp(DecreaseStep, 1, 20);
    }
}

public readonly record struct CoolingDemand(double CpuOriginal, double GpuOriginal, double Mixed,
                                             double CpuTarget, double GpuTarget);

/// <summary>Pure demand arithmetic. Values use the device's scale, which can reach 150%.</summary>
public static class CrossCooling
{
    public static CoolingDemand Calculate(double cpu, double gpu, int max, CrossCoolingSettings s)
    {
        if (!double.IsFinite(cpu) || !double.IsFinite(gpu) || max <= 0)
            throw new ArgumentOutOfRangeException(nameof(cpu));
        if (!Enum.IsDefined(s.Mode) || !Enum.IsDefined(s.Algorithm)) throw new ArgumentException("Invalid mode or algorithm");
        cpu = Math.Clamp(cpu, 0, max); gpu = Math.Clamp(gpu, 0, max);
        if (s.CpuWeight is < 0 or > 100 || s.GpuWeight is < 0 or > 100 || s.CpuWeight + s.GpuWeight == 0)
            throw new ArgumentException("Weights must be between 0 and 100 with a positive sum");
        if (s.BoostPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(s.BoostPercent));
        double mix = s.Algorithm switch
        {
            CoolingMix.Maximum => Math.Max(cpu, gpu),
            CoolingMix.Average => (cpu + gpu) / 2,
            CoolingMix.WeightedAverage => (s.CpuWeight * cpu + s.GpuWeight * gpu) / (s.CpuWeight + s.GpuWeight),
            _ => Math.Max(cpu, gpu), // directional boost is evaluated separately for each target
        };
        double Target(double own, double other) => Math.Clamp(s.Algorithm == CoolingMix.PriorityBoost
            ? own + s.BoostPercent / 100.0 * Math.Max(0, other - own) : Math.Max(own, mix), own, max);
        return new(cpu, gpu, mix,
            s.Mode is CrossCoolingMode.GpuToCpu or CrossCoolingMode.Both ? Target(cpu, gpu) : cpu,
            s.Mode is CrossCoolingMode.CpuToGpu or CrossCoolingMode.Both ? Target(gpu, cpu) : gpu);
    }

    public static bool ValidTemperature(double? t) => t is > 0 and <= 115 && double.IsFinite(t.Value);

    public static bool ValidCurve(int[] temps, int[] speeds, int max) => max > 0 && temps.Length == 6 && speeds.Length == 6 &&
        temps.All(t => t is >= 0 and <= 115) && speeds.All(s => s >= 0 && s <= max) &&
        temps.Zip(temps.Skip(1)).All(p => p.First <= p.Second) &&
        speeds.Zip(speeds.Skip(1)).All(p => p.First <= p.Second);

    /// <summary>Keeps every original node and the monotonic constraint.</summary>
    public static int[] Floor(int[] speeds, double demand, int max)
    {
        if (!double.IsFinite(demand) || speeds.Length != 6 || speeds.Any(s => s < 0 || s > max) ||
            speeds.Zip(speeds.Skip(1)).Any(p => p.First > p.Second)) throw new ArgumentException("Invalid speed table");
        int floor = (int)Math.Ceiling(Math.Clamp(demand, 0, max));
        return speeds.Select(s => Math.Max(s, floor)).ToArray();
    }
}

public sealed record CoolingPlan(CoolingDemand Demand, int[] CpuTable, int[] GpuTable, bool PlanChanged);

/// <summary>
/// Stateful, clock-injected floor scheduling. Hardware ownership belongs to CrossCoolingController.
/// </summary>
public sealed class CrossCoolingScheduler
{
    private double? _cpu, _gpu;
    private int[]? _cpuTable, _gpuTable;
    private DateTimeOffset? _last;
    public DateTimeOffset? LastPlan => _last;

    public void Reset() { _cpu = _gpu = null; _cpuTable = _gpuTable = null; _last = null; }

    public CoolingPlan? Sample(double? cpu, double? gpu, DateTimeOffset sampled, DateTimeOffset now,
        int[] ct, int[] cs, int[] gt, int[] gs, int max, CrossCoolingSettings s,
        bool supported = true, bool singleFan = false, bool conflict = false, bool cancelled = false)
    {
        if (!supported || singleFan || conflict || cancelled || !CrossCooling.ValidTemperature(cpu) || !CrossCooling.ValidTemperature(gpu) ||
            now < sampled || (now - sampled).TotalSeconds > Math.Max(6, s.SamplingSeconds * 3) ||
            !CrossCooling.ValidCurve(ct, cs, max) || !CrossCooling.ValidCurve(gt, gs, max))
        { Reset(); return null; }
        double Smooth(double raw, double? old) => !s.Smoothing || old == null || raw >= old ? raw : old.Value + .35 * (raw - old.Value);
        _cpu = Smooth(cpu!.Value, _cpu); _gpu = Smooth(gpu!.Value, _gpu);
        double dc = CurveModel.SpeedAt(ct, cs, (float)cpu.Value), dg = CurveModel.SpeedAt(gt, gs, (float)gpu.Value);
        var d = CrossCooling.Calculate(CurveModel.SpeedAt(ct, cs, (float)_cpu), CurveModel.SpeedAt(gt, gs, (float)_gpu), max, s);
        d = d with { CpuOriginal = dc, GpuOriginal = dg, CpuTarget = Math.Max(dc, d.CpuTarget), GpuTarget = Math.Max(dg, d.GpuTarget) };
        if (s.Mode == CrossCoolingMode.Off)
        { Reset(); return new(new(dc, dg, d.Mixed, dc, dg), (int[])cs.Clone(), (int[])gs.Clone(), false); }
        // Raise only the selected table. Own demand is already encoded by its original curve.
        int[] c = s.Mode is CrossCoolingMode.GpuToCpu or CrossCoolingMode.Both ? CrossCooling.Floor(cs, d.CpuTarget, max) : (int[])cs.Clone();
        int[] g = s.Mode is CrossCoolingMode.CpuToGpu or CrossCoolingMode.Both ? CrossCooling.Floor(gs, d.GpuTarget, max) : (int[])gs.Clone();
        int[] Ease(int[] next, int[]? old)
        {
            if (old == null) return next;
            return next.Select((v, i) => v >= old[i] ? (v - old[i] >= s.IncreaseHysteresis ? v : old[i])
                : old[i] - v > s.DecreaseHysteresis ? Math.Max(v, old[i] - s.DecreaseStep) : old[i]).ToArray();
        }
        if (s.Mode is CrossCoolingMode.GpuToCpu or CrossCoolingMode.Both) c = Ease(c, _cpuTable);
        if (s.Mode is CrossCoolingMode.CpuToGpu or CrossCoolingMode.Both) g = Ease(g, _gpuTable);
        bool Different(int[] next, int[]? old) => old == null || next.Where((v, i) => Math.Abs(v - old[i]) >= s.Threshold).Any();
        bool changed = (_last == null || (now - _last.Value).TotalSeconds >= s.UpdateSeconds) &&
                       (Different(c, _cpuTable) || Different(g, _gpuTable));
        if (changed) { _cpuTable = c; _gpuTable = g; _last = now; }
        return new(d, (int[])(_cpuTable ?? c).Clone(), (int[])(_gpuTable ?? g).Clone(), changed);
    }
}
