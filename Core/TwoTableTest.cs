namespace GhostDeck;

/// <summary>
/// The two-table test: which of the two fan-curve tables does the firmware follow, and how
/// does it combine them? Every board on record carries two tables (Fan 1 / CPU and Fan 2 /
/// GPU) even when the chassis has one fan (issues #224, #293: a Cyborg 15 whose single fan
/// would not drop below ~2700 rpm once the app wrote table 1 alone - the factory values left
/// in table 2 kept it up). The test writes a FLAT curve into each table (every node = the
/// same duty, so temperature plays no part), holds each pair of values for half a minute and
/// records the duty readback of each table, the tachometers and the temperatures. The pairs
/// are chosen so that one table always keeps the fans turning - the fans never stop - and
/// so that the readings separate "the higher of the two" from "table 1 only" from "table 2
/// only" (TwoTableTest.VerdictFor). On a two-fan board the same run confirms which table
/// drives which fan.
///
/// Like the sweep (FanSweep) it writes through the editor's own path and keeps Advanced fan
/// mode engaged; unlike the sweep it writes BOTH tables regardless of the model's SingleFan
/// flag, and the caller's restore must put both back (Ec.WriteFanCurve with bothTables).
/// Runs on a worker; progress and the result come back through callbacks the caller marshals.
/// </summary>
public static class TwoTableTest
{
    /// <summary>(table 1, table 2) duty pairs, in run order. The first four separate the rules;
    /// the last two ask what a zero in ONE table does while the other holds the fan.</summary>
    public static readonly (int Table1, int Table2)[] Pairs = { (25, 25), (25, 45), (45, 25), (45, 45), (0, 45), (45, 0) };
    public const int SettleSeconds = 30;   // a fan slowing down takes longer than one spinning up
    public const int SamplesPerStep = 5;   // the last five 1 s readings are averaged
    public const int MaxStartCpuTemp = 70, MaxStartGpuTemp = 60;   // a warm machine holds its fans up by itself
    public const int AbortCpuTemp = 85;
    public const double Tolerance = 0.08;  // readings within 8 % of each other count as the same speed

    public readonly record struct StepResult(int Table1, int Table2, int CpuDuty, int GpuDuty, int CpuRpm, int GpuRpm,
                                             int CpuTemp, int GpuTemp, int FanModeByte);

    public sealed class Result
    {
        public List<StepResult> Steps { get; } = new();
        public bool HasTach { get; init; }
        public bool SingleFan { get; init; }
        public byte AdvancedMode { get; init; }
        public bool Aborted { get; set; }
        public string? Error { get; set; }
        /// <summary>Language key of the reason when the test stopped itself (heat), else null.</summary>
        public string? AbortReason { get; set; }
        public DateTime Started { get; init; } = DateTime.Now;
        public TimeSpan Duration { get; set; }
    }

    public enum Verdict { Undecided, Higher, Table1, Table2, Flat }

    /// <summary>Why the test must not start now, as a language key; null when it may.</summary>
    public static string? Blocked(DeviceProfile dev, HwSnapshot hw)
    {
        if (dev.FanCurve == null) return "tt_block_nocurve";
        if (SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online) return "tt_block_battery";
        if (hw.CpuTemp > MaxStartCpuTemp || hw.GpuTemp > MaxStartGpuTemp) return "tt_block_warm";
        return null;
    }

    /// <summary>
    /// Run the test. <paramref name="progress"/> gets (pairIndex, pairCount, "t1 % / t2 %") on
    /// the worker thread; the caller marshals. Cancels cooperatively at pair boundaries and
    /// inside the settle loop. The restore of both tables and the mode is the CALLER's job.
    /// </summary>
    public static Result Run(DeviceProfile dev, Action<int, int, string> progress, CancellationToken ct)
    {
        var fc = dev.FanCurve ?? throw new InvalidOperationException("no fan-curve tables on this model");
        var res = new Result
        {
            HasTach = dev.CpuRpmAddr != 0 || dev.GpuRpmAddr != 0 || dev.CpuRpmAddr16 != 0 || dev.GpuRpmAddr16 != 0,
            SingleFan = fc.SingleFan,
            AdvancedMode = fc.AdvancedModeValue,
        };
        var t0 = DateTime.Now;
        try
        {
            // the node temperatures stay the board's own, so the write has the editor's shape
            var cur = Ec.ReadFanCurve(dev);
            int[] tC = cur?.cpuTemp ?? new int[fc.Points], tG = cur?.gpuTemp ?? new int[fc.Points];
            for (int si = 0; si < Pairs.Length; si++)
            {
                if (ct.IsCancellationRequested) { res.Aborted = true; break; }
                var (t1, t2) = Pairs[si];
                progress(si, Pairs.Length, $"{t1} % / {t2} %");
                Ec.WriteFanCurve(dev, tC, Enumerable.Repeat(t1, fc.Points).ToArray(), tG, Enumerable.Repeat(t2, fc.Points).ToArray(), bothTables: true);
                Ec.SetFanMode(dev, fc.AdvancedModeValue);
                var window = new List<HwSnapshot>();
                for (int s = 0; s < SettleSeconds + SamplesPerStep; s++)
                {
                    if (ct.WaitHandle.WaitOne(1000)) { res.Aborted = true; break; }
                    if (!Ec.TryReadHw(dev, out var hw)) continue;
                    if (hw.CpuTemp >= AbortCpuTemp) { res.Aborted = true; res.AbortReason = "tt_abort_hot"; break; }
                    if (s >= SettleSeconds) window.Add(hw);
                }
                if (res.Aborted) break;
                if (window.Count == 0) continue;
                int mode = Ec.ReadByte(dev.FanMode);
                res.Steps.Add(new StepResult(t1, t2,
                    (int)Math.Round(window.Average(w => w.CpuFan)), (int)Math.Round(window.Average(w => w.GpuFan)),
                    (int)Math.Round(window.Average(w => w.CpuRpm)), (int)Math.Round(window.Average(w => w.GpuRpm)),
                    (int)Math.Round(window.Average(w => w.CpuTemp)), (int)Math.Round(window.Average(w => w.GpuTemp)),
                    mode));
            }
        }
        catch (Exception ex) { res.Error = ex.Message; }
        res.Duration = DateTime.Now - t0;
        return res;
    }

    /// <summary>The fan's rpm at a pair, or -1 when that pair was not reached or the board has no tachometer.</summary>
    public static int Rpm(Result r, int fan, int t1, int t2)
    {
        if (!r.HasTach) return -1;
        foreach (var s in r.Steps)
            if (s.Table1 == t1 && s.Table2 == t2) return fan == 0 ? s.CpuRpm : s.GpuRpm;
        return -1;
    }

    private static bool Near(int x, int y) => Math.Abs(x - y) <= Math.Max(100, Tolerance * Math.Max(x, y));
    private static bool Above(int x, int y) => x > y + Math.Max(150, 0.15 * y);

    /// <summary>
    /// What the four separating pairs say about one fan. "Higher": 25/45 and 45/25 both read
    /// like 45/45. "Table1": 25/45 reads like 25/25 and 45/25 like 45/45. "Table2": the other
    /// way round. "Flat": 45/45 is not clearly above 25/25, so the run cannot tell the tables
    /// apart (a warm machine holding its fan, or a fan that cannot go that low). Anything else
    /// is undecided and the numbers are shown as they are.
    /// </summary>
    public static Verdict VerdictFor(Result r, int fan)
    {
        int a = Rpm(r, fan, 25, 25), b = Rpm(r, fan, 25, 45), c = Rpm(r, fan, 45, 25), d = Rpm(r, fan, 45, 45);
        if (a <= 0 || b <= 0 || c <= 0 || d <= 0) return Verdict.Undecided;
        if (!Above(d, a)) return Verdict.Flat;
        bool bHigh = Near(b, d), bLow = Near(b, a), cHigh = Near(c, d), cLow = Near(c, a);
        if (bHigh && cHigh) return Verdict.Higher;
        if (bLow && cHigh) return Verdict.Table1;
        if (bHigh && cLow) return Verdict.Table2;
        return Verdict.Undecided;
    }

    /// <summary>
    /// What the numbers say, as (language key, format args) pairs the UI localises. Facts from
    /// the numbers only, like FanSweep.Findings: the same readings always give the same lines.
    /// Fan-scoped lines carry the fan index (1 or 2) as their first argument; the UI swaps it
    /// for the fan's name.
    /// </summary>
    public static List<(string key, object[] args)> Findings(Result r)
    {
        var f = new List<(string, object[])>();
        if (r.Steps.Count == 0) return f;
        int fans = r.SingleFan ? 1 : 2;
        if (!r.HasTach) f.Add(("tt_find_notach", Array.Empty<object>()));
        else
            for (int fan = 0; fan < fans; fan++)
            {
                int a = Rpm(r, fan, 25, 25), b = Rpm(r, fan, 25, 45), c = Rpm(r, fan, 45, 25), d = Rpm(r, fan, 45, 45);
                var nums = new object[] { fan + 1, a, b, c, d };
                switch (VerdictFor(r, fan))
                {
                    case Verdict.Higher: f.Add(("tt_find_higher", nums)); break;
                    case Verdict.Table1: f.Add(("tt_find_table1", nums)); break;
                    case Verdict.Table2: f.Add(("tt_find_table2", nums)); break;
                    case Verdict.Flat: f.Add(("tt_find_flat", new object[] { fan + 1, a, d })); break;
                    default: f.Add(("tt_find_undecided", nums)); break;
                }
                int e = Rpm(r, fan, 0, 45), g = Rpm(r, fan, 45, 0);
                if (e > 0 && b > 0) f.Add(Near(e, b) ? ("tt_find_zero1_holds", new object[] { fan + 1, e }) : ("tt_find_zero1_drops", new object[] { fan + 1, b, e }));
                if (g > 0 && c > 0) f.Add(Near(g, c) ? ("tt_find_zero2_holds", new object[] { fan + 1, g }) : ("tt_find_zero2_drops", new object[] { fan + 1, c, g }));
            }
        var odd = r.Steps.Where(s => s.FanModeByte != r.AdvancedMode).Select(s => s.FanModeByte).Distinct().ToList();
        if (odd.Count > 0) f.Add(("tt_find_mode", new object[] { string.Join(", ", odd.Select(m => $"0x{m:X2}")), $"0x{r.AdvancedMode:X2}" }));
        f.Add(("fc_find_temps", new object[] { r.Steps.Min(s => s.CpuTemp), r.Steps.Max(s => s.CpuTemp) }));
        return f;
    }

    public static bool IsFanScoped(string key) => key.StartsWith("tt_find_") && key != "tt_find_notach" && key != "tt_find_mode";

    /// <summary>
    /// Plain-text report, invariant English like the sweep's: it gets pasted into issues read
    /// by people who do not share the reporter's language. The findings arrive already worded
    /// in the app's language, for the person who ran the test.
    /// </summary>
    public static string Report(Result r, DeviceProfile dev, string firmware, string appVersion, IEnumerable<string>? findings = null)
    {
        var fc = dev.FanCurve;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== GhostDeck - two-table test (which fan-curve table does the firmware follow?) ===");
        sb.AppendLine($"Generated: {r.Started:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"App version: {appVersion}");
        sb.AppendLine($"EC firmware: {firmware}");
        sb.AppendLine($"Model: {dev.Name}  ({dev.Tier}){(r.SingleFan ? "  - single-fan entry: the app normally writes table 1 only" : "")}");
        if (fc != null)
            sb.AppendLine($"Tables: table 1 (CPU) speeds 0x{fc.CpuSpeedBase:X2}-0x{fc.CpuSpeedBase + fc.Points - 1:X2}, table 2 (GPU) speeds 0x{fc.GpuSpeedBase:X2}-0x{fc.GpuSpeedBase + fc.Points - 1:X2}, fan mode 0x{dev.FanMode:X2} (custom-curve value 0x{fc.AdvancedModeValue:X2})");
        string cpuTach = dev.CpuRpmAddr16 != 0 ? $"0x{dev.CpuRpmAddr16:X2}:{dev.CpuRpmAddr16 + 1:X2} (16-bit)" : $"0x{dev.CpuRpmAddr:X2}";
        string gpuTach = dev.GpuRpmAddr16 != 0 ? $"0x{dev.GpuRpmAddr16:X2}:{dev.GpuRpmAddr16 + 1:X2} (16-bit)" : $"0x{dev.GpuRpmAddr:X2}";
        sb.AppendLine($"Tachometers: {(r.HasTach ? $"CPU {cpuTach} / GPU {gpuTach}" : "none on this model - duty readback only, so no verdict")}");
        sb.AppendLine($"Duration: {r.Duration.TotalSeconds:0} s{(r.Aborted ? "  (STOPPED" + (r.AbortReason != null ? ": " + r.AbortReason : "") + ")" : "")}{(r.Error != null ? "  ERROR: " + r.Error : "")}");
        sb.AppendLine();
        sb.AppendLine($"--- Pairs (each held {SettleSeconds} s, last {SamplesPerStep} s averaged; both tables flat at the value shown) ---");
        sb.AppendLine("table1  table2  CPU duty  GPU duty  CPU rpm  GPU rpm  CPU C  GPU C  mode");
        foreach (var s in r.Steps)
            sb.AppendLine($"{s.Table1,6}  {s.Table2,6}  {s.CpuDuty,8}  {s.GpuDuty,8}  {(s.CpuRpm > 0 ? s.CpuRpm.ToString() : "--"),7}  {(s.GpuRpm > 0 ? s.GpuRpm.ToString() : "--"),7}  {s.CpuTemp,5}  {s.GpuTemp,5}  0x{s.FanModeByte:X2}");
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
        sb.AppendLine("duty = what each table asked for at that moment (the controller's own readback per table);");
        sb.AppendLine("rpm = what the fan actually did. The verdict compares rpm across the pairs.");
        return sb.ToString();
    }
}
