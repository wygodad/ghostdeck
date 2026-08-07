using System.Text;

namespace GhostDeck;

/// <summary>
/// A measured comparison of what the profiles actually do, and a probe for a board's fourth
/// shift-mode value. Everything a model report asks the owner to judge by ear ("is Silent
/// quieter?", "does Extreme unlock full power?") is a number here instead: the same synthetic
/// CPU load runs in every profile while temperatures, fan duty, fan RPM, the PDH clock estimate
/// and the amount of work completed are sampled once a second.
///
/// This is the only place in the app that writes an EC value that is NOT part of a profile
/// recipe, so it is deliberately narrow: the ONLY extra address is the model's own shift-mode
/// register, and the ONLY extra value is the one the model database records for that board. The
/// register is read back after the write and again after the revert, and the caller restores the
/// user's profile through the normal path afterwards. EC writes are volatile, so a reboot returns
/// the firmware defaults regardless.
///
/// No UI, no timers, no Control references - the page only feeds it a progress sink and a token.
/// </summary>
public static class PowerTest
{
    // One phase = settle (fans and power policy stop moving) + load (the measurement).
    // The steady window is the tail of the load, so the ramp is not averaged into the result.
    public const int SettleSeconds = 15;
    public const int LoadSeconds = 60;
    public const int SteadySeconds = 25;
    // The fourth-mode probe compares two idle dumps taken this far apart BEFORE writing anything:
    // whatever moves on its own in that gap is drift, and is subtracted from what the write moved.
    private const int ProbeSettleSeconds = 10;
    private const int ProbeGapSeconds = 3;
    // Sustained load on a laptop is meant to be hot; this only catches a machine that is not
    // coping at all, so the run stops instead of sitting at the ceiling for another minute.
    private const int HotCeiling = 99;
    private const int HotSamplesToAbort = 5;

    public readonly record struct Sample(
        int Sec, int CpuTemp, int GpuTemp, int CpuFan, int GpuFan,
        int CpuRpm, int GpuRpm, int ClockMhz, int GpuUsage, long Work);

    public sealed record Phase(
        string Name,
        (byte addr, byte val)[] Written,
        Sample[] Samples,
        byte[] Dump);

    /// <summary>
    /// What happened when the fourth shift value was written from outside the vendor software.
    /// <see cref="Drift"/> = addresses that moved on their own between two idle dumps taken before
    /// the write, so <see cref="Moved"/> can exclude them.
    /// </summary>
    public sealed record FourthProbe(
        string Name, byte Addr, byte Requested,
        byte ShiftBefore, byte ShiftAfterWrite, byte ShiftAfterRevert,
        bool Accepted, bool Cleared,
        byte[] Drift,
        (byte addr, byte before, byte after)[] Moved,
        Sample[] Samples,
        byte[] DumpBefore, byte[] DumpAfterWrite, byte[] DumpLoaded, byte[] DumpAfterRevert);

    public sealed record Result(
        DateTime Started, string AppVersion, string Firmware, string Model, string Tier,
        bool OnAc, bool FanBoost, int Threads, ProfileId StartProfile, byte StartShift,
        byte[] DumpBefore, Phase[] Phases, FourthProbe? Fourth, string? Aborted);

    /// <summary>Where the run is, for the page's progress bar and live line.</summary>
    public readonly record struct Progress(
        int StepIndex, int StepCount, string Step, string Stage, double Fraction, string Live);

    // The profiles that get a loaded run, in the order that keeps the machine coolest first.
    private static readonly ProfileId[] Order =
        { ProfileId.Silent, ProfileId.Balanced, ProfileId.Extreme };

    /// <summary>Steps shown in the page's checklist: one per profile, the probe, and the restore.</summary>
    public static int StepCount(DeviceProfile dev) => Order.Length + (dev.FourthMode != null ? 1 : 0) + 1;

    public static Task<Result> RunAsync(
        DeviceProfile dev, string appVersion, string firmware,
        IProgress<Progress> progress, CancellationToken ct) =>
        Task.Run(() => Run(dev, appVersion, firmware, progress, ct), ct);

    private static Result Run(
        DeviceProfile dev, string appVersion, string firmware,
        IProgress<Progress> pr, CancellationToken ct)
    {
        int threads = Environment.ProcessorCount;
        int steps = StepCount(dev);
        var phases = new List<Phase>();
        FourthProbe? fourth = null;
        string? aborted = null;
        var started = DateTime.Now;
        bool onAc = OnAc();

        Report(pr, 0, steps, "", "dump", 0, "");
        // Captured before anything is written: the run ends by putting this back, and the report
        // states which profile the machine was in when it started. The raw shift byte is kept as
        // well, because a machine already sitting in its fourth mode is not any of the four
        // profiles and the recipe alone would not put it back.
        var startProfile = Ec.GetCurrent(dev);
        byte[] before = SafeDump();
        byte startShift = before.Length == 256 ? before[dev.ShiftMode] : (byte)0;
        // Max fans flatten every fan and temperature column, so the report has to say whether they
        // were on. It is recorded rather than refused: it changes what the numbers mean, not their
        // validity, and turning it off would be a write the consent screen did not list.
        bool boost = false;
        try { boost = Ec.GetCoolerBoost(dev); } catch { }

        try
        {
            for (int i = 0; i < Order.Length && aborted == null; i++)
            {
                var id = Order[i];
                // Invariant name: the report is read on GitHub, not in the reporter's language.
                string name = id.ToString();
                var recipe = dev.Recipes[id];

                try { Ec.Apply(recipe); }
                catch (Exception ex) { aborted = $"could not apply the {name} recipe: {ex.Message}"; break; }
                Settle(pr, i, steps, name, SettleSeconds, ct);

                var samples = Loaded(dev, pr, i, steps, name, threads, ct, ref aborted);
                phases.Add(new Phase(name, recipe, samples, SafeDump()));
            }

            if (aborted == null && dev.FourthMode is { } fm)
                fourth = Probe(dev, fm, pr, Order.Length, steps, threads, ct, ref aborted);
        }
        catch (OperationCanceledException)
        {
            aborted = "cancelled";
        }
        finally
        {
            // Belt and braces: put the shift and fan registers back to the state the run found,
            // straight away, without waiting for the caller's restore. The caller then re-applies
            // through the normal path so the fan curve comes back too.
            try
            {
                Ec.Apply(dev.Recipes[startProfile]);
                if (dev.FourthMode is { } fm4 && startShift == fm4.ShiftValue)
                    Ec.Apply(new[] { (dev.ShiftMode, startShift) });
            }
            catch { }
        }

        return new Result(started, appVersion, firmware, dev.Name, dev.Tier.ToString(),
            onAc, boost, threads, startProfile, startShift, before, phases.ToArray(), fourth, aborted);
    }

    // ---------------- fourth-mode probe ----------------

    private static FourthProbe Probe(
        DeviceProfile dev, FourthModeSpec fm, IProgress<Progress> pr,
        int step, int steps, int threads, CancellationToken ct, ref string? aborted)
    {
        Ec.Apply(dev.Recipes[ProfileId.Extreme]);
        Settle(pr, step, steps, fm.Name, ProbeSettleSeconds, ct);

        // Two idle dumps before touching anything: their difference is the drift floor. Cancelling
        // here is free, nothing has been written yet.
        byte[] control = SafeDump();
        Wait(ProbeGapSeconds * 1000, ct);
        byte[] dumpBefore = SafeDump();
        byte[] drift = Differ(control, dumpBefore).Select(d => d.addr).ToArray();
        byte shiftBefore = At(dumpBefore, dev.ShiftMode);

        // From here to the revert the waits are NOT cancellable. Cancel must not be able to leave
        // the register holding a value the user never asked for, and the readbacks either side of
        // the write are the whole point of the probe - losing them would waste the write.
        Report(pr, step, steps, fm.Name, "write", 0, "");
        Ec.Apply(new[] { (dev.ShiftMode, fm.ShiftValue) });
        Thread.Sleep(ProbeGapSeconds * 1000);
        byte[] dumpAfter = SafeDump();
        byte shiftAfter = At(dumpAfter, dev.ShiftMode);
        bool accepted = shiftAfter == fm.ShiftValue;

        var moved = Differ(dumpBefore, dumpAfter)
            .Where(d => d.addr != dev.ShiftMode && !drift.Contains(d.addr))
            .ToArray();

        // A value the register refused is not worth a minute of load - the answer is already in.
        var samples = accepted
            ? Loaded(dev, pr, step, steps, fm.Name, threads, ct, ref aborted)
            : Array.Empty<Sample>();
        byte[] loaded = accepted ? SafeDump() : Array.Empty<byte>();

        Report(pr, step, steps, fm.Name, "revert", 1, "");
        Ec.Apply(new[] { (dev.ShiftMode, dev.ShiftTurboValue) });
        Thread.Sleep(ProbeGapSeconds * 1000);
        byte[] dumpRevert = SafeDump();
        byte shiftRevert = At(dumpRevert, dev.ShiftMode);

        return new FourthProbe(fm.Name, dev.ShiftMode, fm.ShiftValue,
            shiftBefore, shiftAfter, shiftRevert,
            accepted, shiftRevert == dev.ShiftTurboValue,
            drift, moved, samples, dumpBefore, dumpAfter, loaded, dumpRevert);
    }

    /// <summary>
    /// A dump that fails costs one section of the report, not the whole run: a WMI provider recycle
    /// mid-read is documented as normal (see Ec), and 256 reads is a wide window for one.
    /// </summary>
    private static byte[] SafeDump()
    {
        try { return Ec.DumpAll(); } catch { return Array.Empty<byte>(); }
    }

    private static byte At(byte[] dump, byte addr) => dump.Length == 256 ? dump[addr] : (byte)0;

    private static (byte addr, byte before, byte after)[] Differ(byte[] a, byte[] b) =>
        Enumerable.Range(0, Math.Min(a.Length, b.Length))
                  .Where(i => a[i] != b[i])
                  .Select(i => ((byte)i, a[i], b[i]))
                  .ToArray();

    // ---------------- load + sampling ----------------

    private static Sample[] Loaded(
        DeviceProfile dev, IProgress<Progress> pr, int step, int steps, string name,
        int threads, CancellationToken ct, ref string? aborted)
    {
        var samples = new List<Sample>(LoadSeconds);
        int hot = 0;
        long lastWork = 0;
        using var load = new CpuLoad(threads);

        for (int s = 1; s <= LoadSeconds; s++)
        {
            // Cancel ends the phase rather than unwinding it: the seconds already measured are
            // worth keeping, and the caller promises the report holds whatever was measured.
            try { Wait(1000, ct); }
            catch (OperationCanceledException) { aborted = "cancelled"; break; }

            long work = load.Iterations;
            // A refused WMI read is a MISSING second, not a cold, stopped, zero-RPM one. Recording
            // the defaulted struct would pull every average down and, worse, clear the hot counter.
            if (!Ec.TryReadHw(dev, out var hw)) { lastWork = work; continue; }

            var sample = new Sample(s, hw.CpuTemp, hw.GpuTemp, hw.CpuFan, hw.GpuFan,
                hw.CpuRpm, hw.GpuRpm, Perf.CpuClockMhz(), Perf.GpuUsage(), work - lastWork);
            lastWork = work;
            samples.Add(sample);

            Report(pr, step, steps, name, "load", s / (double)LoadSeconds,
                $"{hw.CpuTemp} °C  ·  {hw.CpuFan} %  ·  {sample.ClockMhz} MHz");

            // Losing mains mid-run does not spoil one number, it spoils every number after it:
            // the firmware caps power on battery, which is exactly why the run refuses to start there.
            if (!OnAc()) { aborted = $"the charger was unplugged during {name}"; break; }

            hot = hw.CpuTemp >= HotCeiling ? hot + 1 : 0;
            if (hot >= HotSamplesToAbort)
            {
                aborted = $"CPU stayed at {HotCeiling} °C or above for {HotSamplesToAbort} s during {name}";
                break;
            }
        }
        return samples.ToArray();
    }

    private static void Settle(IProgress<Progress> pr, int step, int steps, string name, int seconds, CancellationToken ct)
    {
        for (int s = 1; s <= seconds; s++)
        {
            Wait(1000, ct);
            Report(pr, step, steps, name, "settle", s / (double)seconds, "");
        }
    }

    private static void Report(IProgress<Progress> pr, int step, int steps, string name, string stage, double f, string live) =>
        pr.Report(new Progress(step, steps, name, stage, Math.Clamp(f, 0, 1), live));

    /// <summary>Sleep that a cancel actually interrupts.</summary>
    private static void Wait(int ms, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
    }

    private static bool OnAc() =>
        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;

    /// <summary>Whether a run may start, and why not when it may not (already localised).</summary>
    public static string? Blocked(DeviceProfile? dev, bool writable, bool simulating)
    {
        if (simulating) return Lang.T("pt_block_sim");
        if (dev == null) return Lang.T("pt_block_unknown");
        if (!writable) return Lang.T("pt_block_locked");
        if (!OnAc()) return Lang.T("pt_block_battery");
        return null;
    }

    // ---------------- synthetic load ----------------

    /// <summary>
    /// All-core busy work whose completed iterations are the measurement. The absolute number means
    /// nothing; the ratio between phases is the point, and it is the one figure here that reflects
    /// delivered performance rather than a sensor. Threads run below normal priority so the window
    /// keeps repainting, which costs the same in every phase and so cancels out of the comparison.
    /// </summary>
    private sealed class CpuLoad : IDisposable
    {
        private const int Block = 50_000;
        private readonly Thread[] _threads;
        private volatile bool _stop;
        private long _work;
        private double _sink;

        public CpuLoad(int threads)
        {
            _threads = new Thread[Math.Max(1, threads)];
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(Spin)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "ghostdeck-load-" + i,
                };
                _threads[i].Start();
            }
        }

        public long Iterations => Interlocked.Read(ref _work);

        private void Spin()
        {
            double x = 1.0000001, y = 0.9999999;
            while (!_stop)
            {
                for (int i = 0; i < Block; i++)
                {
                    x = x * y + 1e-9;
                    y = y * 1.000000003 + 1e-9;
                    if (x > 1e12) x *= 1e-12;
                    if (y > 1e12) y *= 1e-12;
                }
                Interlocked.Add(ref _work, Block);
            }
            Interlocked.Exchange(ref _sink, x + y);   // keeps the loop from being optimised away
        }

        public void Dispose()
        {
            _stop = true;
            foreach (var t in _threads) { try { t.Join(3000); } catch { } }
        }
    }

    // ---------------- report ----------------

    private static double Avg(IEnumerable<int> v) { var l = v.ToList(); return l.Count == 0 ? 0 : l.Average(); }
    private static double AvgL(IEnumerable<long> v) { var l = v.ToList(); return l.Count == 0 ? 0 : l.Average(); }

    private static Sample[] Steady(Sample[] s) => s.Length <= SteadySeconds ? s : s[^SteadySeconds..];

    public static string BuildReport(Result r, DeviceProfile dev)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== GhostDeck - power test report ===");
        sb.AppendLine($"Generated: {r.Started:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"App version: {r.AppVersion}");
        sb.AppendLine($"EC firmware: {(string.IsNullOrEmpty(r.Firmware) ? "(unknown)" : r.Firmware)}");
        sb.AppendLine($"Model: {r.Model}  ({r.Tier})");
        sb.AppendLine($"Power source: {(r.OnAc ? "AC" : "battery")}");
        sb.AppendLine($"Fan Boost (max fans) at the start: {(r.FanBoost ? "ON - fan and temperature columns are flattened by it" : "off")}");
        string startExtra = dev.FourthMode is { } sfm && r.StartShift == sfm.ShiftValue ? $" + {sfm.Name}" : "";
        sb.AppendLine($"State when the run started (restored afterwards): {r.StartProfile}{startExtra}" +
                      $"  (0x{dev.ShiftMode:X2} = 0x{r.StartShift:X2})");
        sb.AppendLine($"Load: {r.Threads} threads, {LoadSeconds} s after {SettleSeconds} s of settling; " +
                      $"the figures below average the last {SteadySeconds} s");
        if (r.Aborted != null) sb.AppendLine($"RUN DID NOT COMPLETE: {r.Aborted}");
        sb.AppendLine();

        // ---- the comparison this whole thing exists for ----
        sb.AppendLine("--- Steady state ---");
        sb.AppendLine("Profile        shift fan  CPU C  GPU C  CPU%  GPU%  CPU rpm  GPU rpm  CPU MHz  work    n");
        double baseWork = 0;
        foreach (var p in r.Phases)
            if (p.Name == ProfileId.Balanced.ToString()) baseWork = AvgL(Steady(p.Samples).Select(s => s.Work));

        void Row(string name, Sample[] samples, byte[] dump)
        {
            var st = Steady(samples);
            if (st.Length == 0) { sb.AppendLine($"{name,-14} (not measured)"); return; }
            string shift = dump.Length == 256 ? dump[dev.ShiftMode].ToString("X2") : "--";
            string fan = dump.Length == 256 ? dump[dev.FanMode].ToString("X2") : "--";
            double work = AvgL(st.Select(s => s.Work));
            string idx = baseWork > 0 ? (work / baseWork * 100).ToString("F0") : "--";
            sb.AppendLine($"{name,-14} {shift,-5} {fan,-4} " +
                          $"{Avg(st.Select(s => s.CpuTemp)),5:F0}  {Avg(st.Select(s => s.GpuTemp)),5:F0}  " +
                          $"{Avg(st.Select(s => s.CpuFan)),4:F0}  {Avg(st.Select(s => s.GpuFan)),4:F0}  " +
                          $"{Avg(st.Select(s => s.CpuRpm)),7:F0}  {Avg(st.Select(s => s.GpuRpm)),7:F0}  " +
                          $"{Avg(st.Select(s => s.ClockMhz)),7:F0}  {idx,4}  {st.Length,3}");
        }

        foreach (var p in r.Phases) Row(p.Name, p.Samples, p.Dump);
        if (r.Fourth is { Accepted: true } f4) Row(f4.Name, f4.Samples, f4.DumpLoaded);
        sb.AppendLine();
        sb.AppendLine($"work = CPU work completed per second in the steady window, Balanced = 100.");
        sb.AppendLine($"n = seconds actually averaged. Fewer than {SteadySeconds} means the phase was cut short");
        sb.AppendLine("(cancelled, unplugged, too hot, or seconds dropped by a refused controller read),");
        sb.AppendLine("so that row may include the ramp and is not a steady state.");
        sb.AppendLine("shift and fan are read back at the END of each phase. A row whose bytes do not match");
        sb.AppendLine("the recipe below was disturbed while it ran (tray menu, hotkey or command line).");
        sb.AppendLine("A CPU MHz or work column that does not drop in Silent means the Silent fan value");
        sb.AppendLine("does not cap power on this board, whatever the fan noise does.");
        sb.AppendLine();
        sb.AppendLine("--- Bytes written for each profile ---");
        foreach (var p in r.Phases)
            sb.AppendLine($"{p.Name,-14} {string.Join("  ", p.Written.Select(w => $"0x{w.addr:X2}=0x{w.val:X2}"))}");
        sb.AppendLine();

        // ---- fourth mode ----
        if (r.Fourth is { } f)
        {
            sb.AppendLine($"--- Fourth shift mode: \"{f.Name}\" (0x{f.Addr:X2} = 0x{f.Requested:X2}) ---");
            sb.AppendLine($"Shift before the write:  0x{f.ShiftBefore:X2}");
            sb.AppendLine($"Shift after the write:   0x{f.ShiftAfterWrite:X2}   -> {(f.Accepted ? "ACCEPTED" : "REFUSED")}");
            sb.AppendLine($"Shift after the revert:  0x{f.ShiftAfterRevert:X2}   -> {(f.Cleared ? "CLEARED" : "STILL SET")}");
            sb.AppendLine($"Drifting on their own (two idle dumps {ProbeGapSeconds} s apart, no write between): " +
                          (f.Drift.Length == 0 ? "none" : string.Join(" ", f.Drift.Select(a => $"0x{a:X2}"))));
            sb.AppendLine("Moved with the write (drift excluded): " +
                          (f.Moved.Length == 0 ? "nothing besides the shift register"
                           : string.Join("  ", f.Moved.Select(m => $"0x{m.addr:X2} {m.before:X2}->{m.after:X2}"))));
            sb.AppendLine();
        }

        // ---- raw material ----
        sb.AppendLine("--- Per-second samples (sec, CPU C, GPU C, CPU%, GPU%, CPU rpm, GPU rpm, MHz, GPU load%, work) ---");
        foreach (var p in r.Phases) Samples(p.Name, p.Samples);
        if (r.Fourth is { } fs && fs.Samples.Length > 0) Samples(fs.Name, fs.Samples);

        void Samples(string name, Sample[] samples)
        {
            sb.AppendLine();
            sb.AppendLine($"[{name}]");
            foreach (var s in samples)
                sb.AppendLine($"{s.Sec,3}  {s.CpuTemp,3}  {s.GpuTemp,3}  {s.CpuFan,3}  {s.GpuFan,3}  " +
                              $"{s.CpuRpm,5}  {s.GpuRpm,5}  {s.ClockMhz,5}  {s.GpuUsage,3}  {s.Work,12}");
        }

        sb.AppendLine();
        sb.AppendLine("--- EC dumps (256 bytes each) ---");
        Dump("BEFORE THE RUN", r.DumpBefore);
        foreach (var p in r.Phases) Dump(p.Name + " (loaded)", p.Dump);
        if (r.Fourth is { } fd)
        {
            Dump(fd.Name + " - before the write", fd.DumpBefore);
            Dump(fd.Name + " - after the write", fd.DumpAfterWrite);
            Dump(fd.Name + " - loaded", fd.DumpLoaded);
            Dump(fd.Name + " - after the revert", fd.DumpAfterRevert);
        }

        void Dump(string name, byte[] d)
        {
            sb.AppendLine();
            sb.AppendLine($"[{name}]");
            if (d.Length != 256) { sb.AppendLine("(not captured)"); return; }
            for (int row = 0; row < 256; row += 16)
            {
                sb.Append($"{row:X2}: ");
                for (int c = 0; c < 16; c++) sb.Append($"{d[row + c]:X2} ");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>One-line verdict for the page and the issue title.</summary>
    public static string Summary(Result r)
    {
        if (r.Aborted != null) return string.Format(Lang.T("pt_res_aborted"), r.Aborted);
        if (r.Fourth is not { } f) return Lang.T("pt_res_done");
        if (!f.Accepted) return string.Format(Lang.T("pt_res_refused"), f.Name);
        return string.Format(Lang.T(f.Cleared ? "pt_res_accepted" : "pt_res_stuck"), f.Name);
    }
}
