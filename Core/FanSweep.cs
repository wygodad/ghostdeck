namespace GhostDeck;

/// <summary>
/// The "curve in action" sweep: hold the fans at a few fixed duty levels, one after another,
/// and record what the tachometers (or, on boards without one, the duty readback) do at each
/// step. The result is a duty -&gt; RPM table measured on THIS machine, the raw material for a
/// per-model fan calibration and for spotting a fan that no longer follows its command.
///
/// How it drives the fans without a "set duty" register: the EC has none, so each step writes
/// a FLAT curve (every node = the step's duty) into the same tables the editor uses and keeps
/// Advanced fan mode engaged. When the sweep ends - normally, cancelled, or by exception - the
/// caller's restore action puts the previous curve/mode back. The sweep never touches the
/// profile registers; the page refuses to start it in Silent for the same reason the editor
/// switches to Balanced first (TECHNICAL 17.5).
///
/// Everything here runs on a worker; progress and the finished result are handed back
/// through callbacks the caller marshals to its UI thread.
/// </summary>
public static class FanSweep
{
    public static readonly int[] DefaultSteps = { 30, 45, 60, 80, 100 };
    public const int SettleSeconds = 6;   // fans need a few seconds to reach a new level
    public const int SamplesPerStep = 3;  // last three 1 s readings are averaged

    public readonly record struct StepResult(int DutyPct, int CpuDuty, int GpuDuty, int CpuRpm, int GpuRpm,
                                             int CpuTemp, int GpuTemp, double SecondsToSettle);

    public sealed class Result
    {
        public List<StepResult> Steps { get; } = new();
        public bool HasTach { get; init; }
        public bool Aborted { get; set; }
        public string? Error { get; set; }
        public DateTime Started { get; init; } = DateTime.Now;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Run the sweep. <paramref name="progress"/> gets (stepIndex, stepCount, message) on the
    /// worker thread; the caller marshals. Cancels cooperatively at step boundaries and inside
    /// the settle loop. The restore is the CALLER's job (finally block around this call).
    /// </summary>
    public static Result Run(DeviceProfile dev, int[] steps, Action<int, int, string> progress, CancellationToken ct)
    {
        var fc = dev.FanCurve ?? throw new InvalidOperationException("no fan-curve tables on this model");
        var res = new Result { HasTach = dev.CpuRpmAddr != 0 || dev.GpuRpmAddr != 0 };
        var t0 = DateTime.Now;
        try
        {
            // temperatures for the flat curve: keep the model's node temperatures so the write
            // shape is identical to a normal apply (only the speed tables change)
            var cur = Ec.ReadFanCurve(dev);
            int[] tC = cur?.cpuTemp ?? new int[fc.Points], tG = cur?.gpuTemp ?? new int[fc.Points];

            for (int si = 0; si < steps.Length; si++)
            {
                if (ct.IsCancellationRequested) { res.Aborted = true; break; }
                int duty = Math.Clamp(steps[si], 0, 100);
                var flat = Enumerable.Repeat(duty, fc.Points).ToArray();
                progress(si, steps.Length, $"{duty}%");
                Ec.WriteFanCurve(dev, tC, flat, tG, flat);
                Ec.SetFanMode(dev, fc.AdvancedModeValue);

                // settle: sample once a second; note when the duty readback first reaches the
                // commanded level (that is the fan's reaction time as the EC reports it)
                var stepStart = DateTime.Now;
                double settledAt = -1;
                var window = new List<HwSnapshot>();
                for (int s = 0; s < SettleSeconds + SamplesPerStep; s++)
                {
                    if (ct.WaitHandle.WaitOne(1000)) { res.Aborted = true; break; }
                    if (!Ec.TryReadHw(dev, out var hw)) continue;
                    if (settledAt < 0 && Math.Abs(hw.CpuFan - duty) <= 2) settledAt = (DateTime.Now - stepStart).TotalSeconds;
                    if (s >= SettleSeconds) window.Add(hw);
                }
                if (res.Aborted) break;
                if (window.Count == 0) continue;
                res.Steps.Add(new StepResult(duty,
                    (int)Math.Round(window.Average(w => w.CpuFan)), (int)Math.Round(window.Average(w => w.GpuFan)),
                    (int)Math.Round(window.Average(w => w.CpuRpm)), (int)Math.Round(window.Average(w => w.GpuRpm)),
                    (int)Math.Round(window.Average(w => w.CpuTemp)), (int)Math.Round(window.Average(w => w.GpuTemp)),
                    settledAt < 0 ? -1 : settledAt));
            }
        }
        catch (Exception ex) { res.Error = ex.Message; }
        res.Duration = DateTime.Now - t0;
        return res;
    }

    /// <summary>What the numbers say, as (language key, format args) pairs the UI localises.</summary>
    public static List<(string key, object[] args)> Findings(Result r)
    {
        // Facts from the numbers only; a line is flagged (the UI colours "drop", "gap" and
        // "slow" amber) solely when a value departs from what a fan following its command
        // would show. No advice, no guessing at causes: the same numbers always give the same
        // lines, and a clean run says so in one line.
        var f = new List<(string, object[])>();
        if (r.Steps.Count < 2) return f;
        for (int fan = 0; fan < 2; fan++)
        {
            var pts = r.Steps.Select(s => (d: s.DutyPct, v: r.HasTach ? (fan == 0 ? s.CpuRpm : s.GpuRpm) : (fan == 0 ? s.CpuDuty : s.GpuDuty))).Where(p => p.v > 0).ToList();
            if (pts.Count < 2) { if (r.HasTach) f.Add(("fc_find_notach", new object[] { fan + 1 })); continue; }
            int drops = 0; string dropAt = "";
            for (int i = 1; i < pts.Count; i++)
                if (pts[i].v < pts[i - 1].v * 0.95) { drops++; dropAt += (dropAt.Length > 0 ? ", " : "") + pts[i].d + "%"; }
            if (drops == 0) f.Add(("fc_find_follows", new object[] { fan + 1, pts[0].v, pts[^1].v }));
            else f.Add(("fc_find_drop", new object[] { fan + 1, dropAt }));
            // neutral fact: the lowest step already spins close to the top step
            if (r.HasTach && pts[0].v > pts[^1].v * 0.75)
                f.Add(("fc_find_floor", new object[] { fan + 1, pts[0].v }));
        }
        if (r.HasTach)
        {
            var both = r.Steps.Where(s => s.CpuRpm > 0 && s.GpuRpm > 0).ToList();
            if (both.Count >= 2)
            {
                var worst = both.OrderByDescending(s => Math.Abs(s.CpuRpm - s.GpuRpm) / (double)Math.Max(s.CpuRpm, s.GpuRpm)).First();
                double gap = Math.Abs(worst.CpuRpm - worst.GpuRpm) / (double)Math.Max(worst.CpuRpm, worst.GpuRpm);
                if (gap > 0.35) f.Add(("fc_find_gap", new object[] { worst.DutyPct, worst.CpuRpm, worst.GpuRpm }));
            }
        }
        // slow reaction - the FIRST step is skipped: it starts from wherever the profile left
        // the fans and spinning up from far away takes longer by nature
        var slow = r.Steps.Skip(1).Where(s => s.SecondsToSettle > 4).ToList();
        if (slow.Count > 0) f.Add(("fc_find_slow", new object[] { string.Join(", ", slow.Select(s => s.DutyPct + "%")), slow.Max(s => s.SecondsToSettle).ToString("0.0") }));
        // context, not judgement
        int tMin = r.Steps.Min(s => s.CpuTemp), tMax = r.Steps.Max(s => s.CpuTemp);
        f.Add(("fc_find_temps", new object[] { tMin, tMax }));
        bool anyFlag = f.Any(x => x.Item1 is "fc_find_drop" or "fc_find_gap" or "fc_find_slow");
        if (!anyFlag) f.Add(("fc_find_clean", Array.Empty<object>()));
        return f;
    }

    /// <summary>
    /// Plain-text report. The body stays in invariant English on purpose - these reports get
    /// pasted into issues and read by people who do not share the reporter's language - while
    /// the conclusions are passed in already worded in the app's language, because they are
    /// for the person who ran the sweep (and are trivial to delete before pasting).
    /// </summary>
    public static string Report(Result r, DeviceProfile dev, string firmware, string appVersion,
                                IEnumerable<string>? findings = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== GhostDeck - fan sweep (curve in action) ===");
        sb.AppendLine($"Generated: {r.Started:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"App version: {appVersion}");
        sb.AppendLine($"EC firmware: {firmware}");
        sb.AppendLine($"Model: {dev.Name}  ({dev.Tier})");
        sb.AppendLine($"Tachometers: {(r.HasTach ? $"CPU 0x{dev.CpuRpmAddr:X2} / GPU 0x{dev.GpuRpmAddr:X2}" : "none on this model - duty readback only")}");
        sb.AppendLine($"Duration: {r.Duration.TotalSeconds:0} s{(r.Aborted ? "  (ABORTED)" : "")}{(r.Error != null ? "  ERROR: " + r.Error : "")}");
        sb.AppendLine();
        sb.AppendLine("--- Steps (each held 6 s, last 3 s averaged) ---");
        sb.AppendLine("set%  CPU duty  GPU duty  CPU rpm  GPU rpm  CPU C  GPU C  reaction s");
        foreach (var s2 in r.Steps)
            sb.AppendLine($"{s2.DutyPct,4}  {s2.CpuDuty,8}  {s2.GpuDuty,8}  {(s2.CpuRpm > 0 ? s2.CpuRpm.ToString() : "--"),7}  {(s2.GpuRpm > 0 ? s2.GpuRpm.ToString() : "--"),7}  {s2.CpuTemp,5}  {s2.GpuTemp,5}  {(s2.SecondsToSettle < 0 ? "--" : s2.SecondsToSettle.ToString("0.0")),10}");
        sb.AppendLine();
        if (findings is { } lines)
        {
            var list = lines.ToList();
            if (list.Count > 0)
            {
                sb.AppendLine("--- What this says (shown in the app's language) ---");
                foreach (var line in list) sb.AppendLine("- " + line);
                sb.AppendLine();
            }
        }
        sb.AppendLine("reaction = seconds until the controller's duty readback first matched the commanded level (+-2).");
        return sb.ToString();
    }
}
