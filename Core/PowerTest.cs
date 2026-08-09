using System.Runtime.InteropServices;
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
    // Below this share of the machine, or with a sample this far from its second, the load was not
    // the only thing running and the comparison stops meaning what it says.
    private const int OwnShareFloor = 85;
    private const int SlowSampleMs = 3000;
    // A shortfall shared equally by every phase cancels out of the ratio the table reports. An
    // UNEVEN one does not: it bends the comparison itself, which is the only thing the table is
    // for. Past this much difference against the baseline phase, say so and say which way.
    private const double MaxShareSkew = 0.08;
    // Measured before anything is written. Catching a busy machine here costs three seconds;
    // catching it afterwards costs five minutes of hot fans and a report that has to be thrown
    // away, which is what happened to three runs out of four while this was being built.
    private const int PreflightSeconds = 3;
    private const int BusyBeforeStartPct = 15;
    // Two logical processors are left out of the load. Every EC read goes through the WMI provider
    // service, which needs a core to answer on, and saturating literally all of them starves it:
    // measured on the reference board, the phase that kept 95 % of the machine waited up to 44 s
    // for one read and ran for eleven minutes, while the phase that happened to keep only 89 %
    // never waited at all. The two spare threads cost the same in every phase, so the ratio the
    // table reports is untouched, and the sampler gets its answers back in about a second.
    private const int LoadThreadHeadroom = 2;

    /// <summary>
    /// One second of the loaded run. <see cref="Ms"/> is the MEASURED gap since the previous
    /// sample, and <see cref="Work"/> is already divided by it: the loop waits a second and then
    /// reads the controller, so the gap is never exactly a second, and a slow read would otherwise
    /// show up as a burst of computation. A GPU that has powered down reports its whole block as
    /// zeros, which is why <see cref="GpuTemp"/> being 0 means "no reading", not "cold".
    /// <see cref="Own"/> is this process's share of the machine's total CPU capacity over that
    /// interval. The comparison only means something if the load had the machine to itself, and
    /// the load threads run below normal priority, so anything else that wants the CPU wins and
    /// the work column ends up describing the competition rather than the profile.
    /// </summary>
    public readonly record struct Sample(
        // Elapsed seconds into the loaded phase, NOT a sample number. The phase is bounded by the
        // clock, so a slow controller yields fewer samples across the same minute, and the steady
        // window has to be carved out of TIME or it swallows the ramp.
        int Sec, int Ms, int Own, int CpuTemp, int GpuTemp, int CpuFan, int GpuFan,
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
        byte[] DumpBefore, Phase[] Phases, FourthProbe? Fourth, string? Aborted,
        // >0 = refused before writing anything, because the machine was already this busy.
        int PreBusyPct = 0,
        // Whether a single EC write happened. The caller restores the machine only when it did,
        // and "nothing was written" has to be true when the report says it.
        bool Wrote = false,
        // Whether the discrete graphics chip was loaded too, and which one. Without this the
        // report cannot be compared against one from a machine where that load never started.
        bool GpuLoaded = false, string GpuAdapter = "");

    /// <summary>Where the run is, for the page's progress bar and live line.</summary>
    public readonly record struct Progress(
        int StepIndex, int StepCount, string Step, string Stage, double Fraction, string Live);

    // The profiles that get a loaded run, in the order that keeps the machine coolest first.
    private static readonly ProfileId[] Order =
        { ProfileId.Silent, ProfileId.Balanced, ProfileId.Extreme };

    /// <summary>
    /// BALANCED is measured a second time, last, after everything else. Every other number in the
    /// table is a percentage of the FIRST BALANCED, so anything that made the machine slower over
    /// the course of the run - a chassis that soaked up heat, a firmware limit tightening as it did
    /// - moved all of those numbers together, and nothing in a single-baseline report can tell the
    /// reader that happened. Measuring the baseline again at the end turns that drift into a number
    /// on the page: the repeat row's work column IS the drift, because it is normalised to the
    /// first one. 100 means the run held; 92 means it finished 8 % slower than it started, and
    /// differences of that size between profiles are then not safe to read as profile differences.
    /// </summary>
    public const string RepeatSuffix = " (repeat)";

    /// <summary>Drift at or beyond this, in percent, gets an explicit warning rather than a note.</summary>
    private const int DriftWarnPct = 5;

    /// <summary>Steps in the page's checklist: one per profile, the probe, the repeated baseline, the restore.</summary>
    public static int StepCount(DeviceProfile dev) => Order.Length + (dev.FourthMode != null ? 1 : 0) + 2;

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

        // Nothing has been written yet, so refusing here is free. Five minutes of load on a machine
        // that is already busy produces a report whose work column describes the other program.
        Report(pr, 0, steps, "", "check", 0, "");
        Result Nothing(string? why, int busy) => new(
            started, appVersion, firmware, dev.Name, dev.Tier.ToString(), onAc, false, threads,
            ProfileId.Balanced, 0, Array.Empty<byte>(), Array.Empty<Phase>(), null, why, busy);

        int preBusy;
        // Cancel is live from the moment the page shows its Cancel button, which is before this
        // wait. Letting it escape would fault the task and the page would blame the WMI interface
        // for something the user did on purpose.
        try { preBusy = MachineBusy(ct); }
        catch (OperationCanceledException) { return Nothing("cancelled", 0); }

        if (preBusy > BusyBeforeStartPct)
            return Nothing($"the machine was already {preBusy} % busy before the run started, so nothing was written", preBusy);

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
        bool wrote = false;

        // Held for the whole run, started before the first settle so temperatures stabilise with it
        // already going. A processor-only load can miss a profile that raises a budget the two chips
        // share, and it must be identical in every phase or the comparison between them means
        // nothing. If it cannot come up, the run continues and the report says so.
        using var gpu = new GpuLoad();

        try
        {
            for (int i = 0; i < Order.Length && aborted == null; i++)
            {
                var id = Order[i];
                // Invariant name: the report is read on GitHub, not in the reporter's language.
                string name = id.ToString();
                var recipe = dev.Recipes[id];

                try { Ec.Apply(recipe); wrote = true; }
                catch (Exception ex) { aborted = $"could not apply the {name} recipe: {ex.Message}"; break; }
                Settle(pr, i, steps, name, SettleSeconds, ct);

                var samples = Loaded(dev, pr, i, steps, name, threads, ct, ref aborted);
                phases.Add(new Phase(name, recipe, samples, SafeDump()));
            }

            if (aborted == null && dev.FourthMode is { } fm)
                fourth = Probe(dev, fm, pr, Order.Length, steps, threads, ct, ref aborted);

            // Last, so the drift it reports covers everything the table above compares.
            if (aborted == null)
            {
                int step = Order.Length + (dev.FourthMode != null ? 1 : 0);
                string name = ProfileId.Balanced + RepeatSuffix;
                var recipe = dev.Recipes[ProfileId.Balanced];
                try { Ec.Apply(recipe); wrote = true; }
                catch (Exception ex) { aborted = $"could not re-apply the {ProfileId.Balanced} recipe: {ex.Message}"; }
                if (aborted == null)
                {
                    Settle(pr, step, steps, name, SettleSeconds, ct);
                    var samples = Loaded(dev, pr, step, steps, name, threads, ct, ref aborted);
                    phases.Add(new Phase(name, recipe, samples, SafeDump()));
                }
            }
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
            // Nothing to put back if nothing was ever written.
            if (wrote)
            {
                try
                {
                    Ec.Apply(dev.Recipes[startProfile]);
                    if (dev.FourthMode is { } fm4 && startShift == fm4.ShiftValue)
                        Ec.Apply(new[] { (dev.ShiftMode, startShift) });
                }
                catch { }
            }
        }

        return new Result(started, appVersion, firmware, dev.Name, dev.Tier.ToString(),
            onAc, boost, threads, startProfile, startShift, before, phases.ToArray(), fourth, aborted,
            0, wrote, gpu.Active, gpu.Adapter);
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
        long lastWork = 0, lastMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var lastCpu = self.TotalProcessorTime;
        using var load = new CpuLoad(Math.Max(1, threads - LoadThreadHeadroom));

        for (int s = 1; clock.ElapsedMilliseconds < LoadSeconds * 1000; s++)
        {
            // Cancel ends the phase rather than unwinding it: the seconds already measured are
            // worth keeping, and the caller promises the report holds whatever was measured.
            try { Wait(1000, ct); }
            catch (OperationCanceledException) { aborted = "cancelled"; break; }

            long nowMs = clock.ElapsedMilliseconds;
            long work = load.Iterations;
            var cpu = self.TotalProcessorTime;
            int ms = (int)Math.Max(1, nowMs - lastMs);
            int own = (int)Math.Round((cpu - lastCpu).TotalMilliseconds / (ms * (double)threads) * 100);
            // A refused WMI read is a MISSING second, not a cold, stopped, zero-RPM one. Recording
            // the defaulted struct would pull every average down and, worse, clear the hot counter.
            if (!Ec.TryReadHw(dev, out var hw)) { lastWork = work; lastMs = nowMs; lastCpu = cpu; continue; }

            var sample = new Sample((int)(nowMs / 1000), ms, Math.Clamp(own, 0, 999),
                hw.CpuTemp, hw.GpuTemp, hw.CpuFan, hw.GpuFan,
                hw.CpuRpm, hw.GpuRpm, Perf.CpuClockMhz(), Perf.GpuUsage(),
                (work - lastWork) * 1000 / ms);
            lastWork = work; lastMs = nowMs; lastCpu = cpu;
            samples.Add(sample);

            Report(pr, step, steps, name, "load", clock.ElapsedMilliseconds / (LoadSeconds * 1000.0),
                $"{hw.CpuTemp} °C  ·  {hw.CpuFan} %  ·  {sample.ClockMhz} MHz");

            // Losing mains mid-run does not spoil one number, it spoils every number after it:
            // the firmware caps power on battery, which is exactly why the run refuses to start there.
            if (!OnAc()) { aborted = $"the charger was unplugged during {name}"; break; }

            // The run heats the graphics chip as well, so the ceiling watches both. A sensor that is
            // not there reads zero and so never trips it.
            bool cpuHot = hw.CpuTemp >= HotCeiling, gpuHot = hw.GpuTemp >= HotCeiling;
            hot = cpuHot || gpuHot ? hot + 1 : 0;
            if (hot >= HotSamplesToAbort)
            {
                string which = cpuHot && gpuHot ? "CPU and GPU" : cpuHot ? "CPU" : "GPU";
                aborted = $"{which} stayed at {HotCeiling} °C or above for {HotSamplesToAbort} s during {name}";
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    /// <summary>
    /// How much of the machine is ALREADY in use, sampled over a few seconds before a single byte
    /// is written. Kernel time includes idle on Windows, so the busy fraction is what is left of it.
    /// Returns -1 when the call is unavailable, which is treated as "cannot tell, carry on".
    /// </summary>
    private static int MachineBusy(CancellationToken ct)
    {
        if (!GetSystemTimes(out long i0, out long k0, out long u0)) return -1;
        Wait(PreflightSeconds * 1000, ct);
        if (!GetSystemTimes(out long i1, out long k1, out long u1)) return -1;
        double total = (k1 - k0) + (u1 - u0);
        if (total <= 0) return -1;
        return (int)Math.Round(Math.Clamp((total - (i1 - i0)) / total, 0, 1) * 100);
    }

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
            double x = 0, y = 0;
            while (!_stop)
            {
                // Seeded per block, so every block is identical work. Carrying x and y across blocks
                // lets y climb by a fixed factor until it wraps at 1e12, which takes 9.21e9 iterations
                // and therefore a fixed number of ITERATIONS rather than of seconds. Throughput drifts
                // across that cycle, the cycle lands differently in a fast phase than in a slow one,
                // and the steady window then averages a different part of it in each phase. Measured
                // on the reference board, that alone moved a 25 s window by 9 % with no laptop
                // involved; reseeding brings it under 5 %.
                x = 1.0000001; y = 0.9999999;
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

    /// <summary>
    /// Mean of a per-second RATE, weighted by the second it was actually measured over. A plain
    /// mean would let a long, starved interval count the same as a short clean one, which is
    /// exactly the case the figure is supposed to expose.
    /// </summary>
    private static double AvgRate(Sample[] s, Func<Sample, double> pick)
    {
        long ms = s.Sum(x => (long)x.Ms);
        return ms <= 0 ? 0 : s.Sum(x => pick(x) * x.Ms) / ms;
    }

    private static Sample[] Steady(Sample[] s) =>
        s.Length == 0 ? s : s.Where(x => x.Sec > s[^1].Sec - SteadySeconds).ToArray();

    public static string BuildReport(Result r, DeviceProfile dev)
    {
        // The report is read on GitHub, not in the reporter's locale, so it is written in invariant
        // English throughout. Without this a Polish machine emits "13,9 s" and "81,0 %" into an
        // otherwise English file, and anything parsing it later has to guess the separator.
        var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try { return BuildReportCore(r, dev); }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = culture; }
    }

    private static string BuildReportCore(Result r, DeviceProfile dev)
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
        sb.AppendLine($"Load: {Math.Max(1, r.Threads - LoadThreadHeadroom)} threads on {r.Threads} logical processors " +
                      $"({LoadThreadHeadroom} left free so the controller can still be read), {LoadSeconds} s per profile " +
                      $"after {SettleSeconds} s of settling; the figures below average the last {SteadySeconds} s");
        sb.AppendLine($"Graphics load: {(r.GpuLoaded ? $"ON, {r.GpuAdapter}" : "OFF - processor only, so a profile that only lifts a graphics or shared budget would not show here")}");
        if (r.Aborted != null) sb.AppendLine($"RUN DID NOT COMPLETE: {r.Aborted}");
        sb.AppendLine();

        // ---- the comparison this whole thing exists for ----
        sb.AppendLine("--- Steady state ---");
        sb.AppendLine("Profile        shift fan  CPU C  GPU C  CPU%  GPU%  CPU rpm  GPU rpm  CPU MHz  MHz range    work  own    n  gpu");
        double baseWork = 0;
        foreach (var p in r.Phases)
            if (p.Name == ProfileId.Balanced.ToString()) baseWork = AvgRate(Steady(p.Samples), s => s.Work);

        void Row(string name, Sample[] samples, byte[] dump)
        {
            var st = Steady(samples);
            if (st.Length == 0) { sb.AppendLine($"{name,-14} (not measured)"); return; }
            // A discrete GPU can power down under a CPU-only load, and the controller then reports
            // its temperature, duty and tachometer as zeros. Averaging those in would invent a
            // 34 C graphics chip, so the GPU columns only count the seconds it was awake.
            var gpu = st.Where(x => x.GpuTemp > 0).ToArray();
            string G(Func<Sample, int> pick) => gpu.Length == 0 ? "--" : Avg(gpu.Select(pick)).ToString("F0");
            // A tachometer reading the app rejected as implausible comes back as 0, the same value
            // as a stopped fan. Averaging those in would report a fan slower than it ever ran.
            string R(Sample[] src, Func<Sample, int> pick)
            {
                var v = src.Where(x => pick(x) > 0).Select(pick).ToArray();
                return v.Length == 0 ? "--" : Avg(v).ToString("F0");
            }

            string shift = dump.Length == 256 ? dump[dev.ShiftMode].ToString("X2") : "--";
            string fan = dump.Length == 256 ? dump[dev.FanMode].ToString("X2") : "--";
            double work = AvgRate(st, s => s.Work);
            string idx = baseWork > 0 ? (work / baseWork * 100).ToString("F0") : "--";
            string range = $"{st.Min(x => x.ClockMhz)}-{st.Max(x => x.ClockMhz)}";
            sb.AppendLine($"{name,-14} {shift,-5} {fan,-4} " +
                          $"{Avg(st.Select(s => s.CpuTemp)),5:F0}  {G(s => s.GpuTemp),5}  " +
                          $"{Avg(st.Select(s => s.CpuFan)),4:F0}  {G(s => s.GpuFan),4}  " +
                          $"{R(st, s => s.CpuRpm),7}  {R(gpu, s => s.GpuRpm),7}  " +
                          $"{Avg(st.Select(s => s.ClockMhz)),7:F0}  {range,-11}  {idx,4}  {AvgRate(st, s => s.Own),3:F0}  {st.Length,3}  {gpu.Length,3}");
        }

        // The repeated baseline belongs at the bottom, after the probe: it is the last thing measured
        // and it describes the whole run above it, not the profile next to it.
        var repeat = r.Phases.FirstOrDefault(p => p.Name.EndsWith(RepeatSuffix));
        foreach (var p in r.Phases.Where(p => !p.Name.EndsWith(RepeatSuffix))) Row(p.Name, p.Samples, p.Dump);
        if (r.Fourth is { Accepted: true } f4) Row(f4.Name, f4.Samples, f4.DumpLoaded);
        if (repeat != null) Row(repeat.Name, repeat.Samples, repeat.Dump);
        sb.AppendLine();
        sb.AppendLine("work = CPU work completed per second in the steady window, Balanced = 100.");
        sb.AppendLine($"n = samples inside that {SteadySeconds} s window. Fewer than {SteadySeconds} of them means the");
        sb.AppendLine("controller was answering slowly or the phase was cut short (cancelled, unplugged, too hot);");
        sb.AppendLine("a handful of samples still describes the window, a couple of them does not.");
        sb.AppendLine("gpu = of those seconds, how many had a readable GPU. A discrete GPU powers down under a");
        sb.AppendLine("CPU-only load and reports zeros, so the GPU columns count only the seconds it was awake;");
        sb.AppendLine("a low gpu count next to a full n is that, not a cool graphics chip.");
        sb.AppendLine("MHz range = lowest and highest clock inside the window. A wide range means the firmware");
        sb.AppendLine("is cycling between two power states, and then the average depends on where the window fell.");
        sb.AppendLine("shift and fan are read back at the END of each phase. A row whose bytes do not match");
        sb.AppendLine("the recipe below was disturbed while it ran (tray menu, hotkey or command line).");
        sb.AppendLine("own = how much of the whole machine's CPU capacity this process had. The load threads run");
        sb.AppendLine("below normal priority, so anything else that wants the CPU takes it first and the work");
        sb.AppendLine($"column then describes the competition, not the profile. A clean run sits a little under");
        sb.AppendLine($"100, because {LoadThreadHeadroom} of the logical processors are deliberately left out of the load.");
        sb.AppendLine("A CPU MHz or work column that does not drop in Silent means the Silent fan value");
        sb.AppendLine("does not cap power on this board, whatever the fan noise does.");

        // ---- did the machine hold still long enough for the table above to mean anything? ----
        if (repeat != null && baseWork > 0)
        {
            double endWork = AvgRate(Steady(repeat.Samples), s => s.Work);
            sb.AppendLine();
            sb.AppendLine("--- Baseline check ---");
            if (endWork <= 0)
            {
                sb.AppendLine("BALANCED was re-run at the end but produced no usable window, so the run cannot say");
                sb.AppendLine("whether it drifted. Read the differences above as approximate.");
            }
            else
            {
                int drift = (int)Math.Round((endWork - baseWork) / baseWork * 100);
                int size = Math.Abs(drift);
                sb.AppendLine("BALANCED is measured twice, once at the start and once at the end, with every other");
                sb.AppendLine("phase in between. The repeat row is normalised to the first one, so its work column is");
                sb.AppendLine("the drift of the whole run: 100 means the machine finished as fast as it started.");
                sb.AppendLine($"Measured: the run ended {size} % {(drift < 0 ? "SLOWER" : "FASTER")} than it started.");
                if (size >= DriftWarnPct)
                {
                    sb.AppendLine($"That is {DriftWarnPct} % or more, so differences between profiles smaller than {size} % are NOT");
                    sb.AppendLine("safe to read as profile differences. A machine already at its temperature limit gets");
                    sb.AppendLine("slower as the run goes on, and every phase pays for the ones before it. Ordering here");
                    sb.AppendLine("(Silent, Balanced, Extreme) can then look like a ranking when it is only a running order.");
                }
                else
                {
                    sb.AppendLine("That is small, so the comparison above is not being carried by the running order.");
                }
            }
        }

        // A run on a busy machine produces confident numbers that are simply wrong, which is the
        // one failure this whole feature exists to avoid. Say so at the top of the file, loudly.
        if (WasBusy(r))
        {
            var loads = Loads(r);
            sb.AppendLine();
            sb.AppendLine("!! THE MACHINE WAS NOT IDLE. Treat the work column as unreliable and re-run !!");
            foreach (var l in loads)
                sb.AppendLine($"   {l.Name,-14} had {l.Own,5:F1} % of the CPU");

            // An equal shortfall cancels out of the ratio; an uneven one does not. Name the phases
            // whose share differed from the baseline's, and which way it pushed their work column,
            // rather than quietly "correcting" a number nobody could then check.
            foreach (var l in loads)
            {
                if (l.Name == ProfileId.Balanced.ToString()) continue;
                double skew = Skew(loads, l);
                if (Math.Abs(skew - 1) <= MaxShareSkew) continue;
                int off = (int)Math.Round(Math.Abs(1 - skew) * 100);
                sb.AppendLine($"   {l.Name} did not get the same share as Balanced ({l.Own:F1} % against " +
                              $"{loads.First(x => x.Name == ProfileId.Balanced.ToString()).Own:F1} %), so its work column reads about " +
                              $"{off} % {(skew < 1 ? "LOWER" : "HIGHER")} than an even run would give.");
            }

            sb.AppendLine("   Something outside GhostDeck was using the processor. Working on the machine while");
            sb.AppendLine("   the test runs is the usual cause and a browser is enough on its own; a virus scan of");
            sb.AppendLine("   a freshly downloaded file is the other, and it keeps going for minutes afterwards.");
            sb.AppendLine("   Leave the machine alone for the five minutes and run it again.");
        }

        // Separate from the block above on purpose. A slow controller answer is not other software
        // competing for the machine, it is this test's own all-core load leaving the WMI provider
        // nothing to answer on, so calling it "not idle" would send the reader looking for a
        // culprit that is not there. It costs samples, never accuracy: every figure is a rate over
        // the interval it was actually measured across.
        var slow = Loads(r).Where(l => l.SlowestMs > SlowSampleMs).ToArray();
        if (slow.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Note: the controller was slow to answer while the load ran.");
            foreach (var l in slow)
                sb.AppendLine($"   {l.Name,-14} waited up to {l.SlowestMs / 1000.0:F1} s for one reading");
            sb.AppendLine("   That is this test starving the service it reads through, not other software.");
            sb.AppendLine("   It costs samples (see the n column), not accuracy.");
        }
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
        sb.AppendLine("--- Per-second samples (sec, ms since the previous one, own% of the machine,");
        sb.AppendLine("    CPU C, GPU C, CPU fan%, GPU fan%, CPU rpm, GPU rpm, MHz, GPU load%, work per second) ---");
        foreach (var p in r.Phases) Samples(p.Name, p.Samples);
        if (r.Fourth is { } fs && fs.Samples.Length > 0) Samples(fs.Name, fs.Samples);

        void Samples(string name, Sample[] samples)
        {
            sb.AppendLine();
            sb.AppendLine($"[{name}]");
            foreach (var s in samples)
                sb.AppendLine($"{s.Sec,3}  {s.Ms,5}  {s.Own,3}  {s.CpuTemp,3}  {s.GpuTemp,3}  {s.CpuFan,3}  {s.GpuFan,3}  " +
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

    /// <summary>
    /// Whether something outside the app was taking the processor while the run measured it.
    /// The numbers still describe something, just not the profile, so this has to reach the page
    /// and not only the bottom of a text file nobody opens before pasting it.
    /// </summary>
    public static bool WasBusy(Result r)
    {
        var loads = Loads(r);
        return loads.Any(l => l.Own < OwnShareFloor || Math.Abs(Skew(loads, l) - 1) > MaxShareSkew);
    }

    /// <summary>How much of the machine each phase actually got, and its worst sampling gap.</summary>
    private sealed record PhaseLoad(string Name, double Own, int SlowestMs);

    private static PhaseLoad[] Loads(Result r) =>
        r.Phases.Select(p => (p.Name, St: Steady(p.Samples)))
                .Concat(r.Fourth is { Accepted: true } f
                            ? new[] { (f.Name, St: Steady(f.Samples)) }
                            : Array.Empty<(string Name, Sample[] St)>())
                .Where(x => x.St.Length > 0)
                .Select(x => new PhaseLoad(x.Name, AvgRate(x.St, s => s.Own), x.St.Max(s => s.Ms)))
                .ToArray();

    /// <summary>A phase's share against the baseline phase's: 1 = they competed on equal terms.</summary>
    private static double Skew(PhaseLoad[] loads, PhaseLoad p)
    {
        var baseline = loads.FirstOrDefault(x => x.Name == ProfileId.Balanced.ToString());
        return baseline == null || baseline.Own <= 0 ? 1 : p.Own / baseline.Own;
    }

    /// <summary>One-line verdict for the page and the issue title.</summary>
    public static string Summary(Result r)
    {
        if (r.PreBusyPct > 0) return string.Format(Lang.T("pt_res_prebusy"), r.PreBusyPct);
        if (r.Aborted != null) return string.Format(Lang.T("pt_res_aborted"), r.Aborted);
        string busy = WasBusy(r) ? Lang.T("pt_res_busy") + "  " : "";
        if (r.Fourth is not { } f) return busy + Lang.T("pt_res_done");
        if (!f.Accepted) return busy + string.Format(Lang.T("pt_res_refused"), f.Name);
        return busy + string.Format(Lang.T(f.Cleared ? "pt_res_accepted" : "pt_res_stuck"), f.Name);
    }
}
