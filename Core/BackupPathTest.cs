using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// The path test of the backup WMI path (TECHNICAL §73): one run that establishes, on the
/// owner's own machine, whether profile switching works through the device's data blocks,
/// and returns everything it saw as one text report.
///
/// The order is fixed and each stage has to succeed before the next one writes anything:
///   1. READ     - block survey, the current shift-mode / fan-mode values, a layout check.
///                 A mismatch ends the run with nothing written.
///   2. ONE BYTE - the fan-mode slot is set to the other stock value (auto / silent), read
///                 back, and put back. A value that does not stick ends the run.
///   3. PROFILES - Silent and Balanced are applied and read back, held for a few seconds and
///                 read again (does anything revert them?). Extreme, Super Battery and
///                 Extreme again each run a short all-core load; Super Battery delivering
///                 clearly less work than BOTH Extreme runs is the proof that the hardware
///                 follows the shift mode, not merely stores it.
///   4. CURVE    - optional, the owner's choice. The CPU fan's speed table is raised to the
///                 highest value it already holds, with the fan mode set to Advanced, first in
///                 the comfort shift mode and then in the turbo one, and the tachometer is
///                 watched. Only speed slots are written, never a temperature slot.
///   5. RESTORE  - everything goes back to what stage 1 found, verified by read-back.
///
/// The original values are also written to a small file before the first write, so a run
/// that dies half-way is undone at the next start (<see cref="RecoverPending"/>). The
/// controller's own state is volatile as well: a restart of the laptop resets it.
/// </summary>
public static class BackupPathTest
{
    public const int HoldSeconds = 6;          // a profile without load: is the write still there afterwards?
    public const int SettleSeconds = 4;
    // A processor may run above its sustained limit for the first seconds of a load, in every
    // mode, before the limit of the mode takes hold. The work figure therefore counts only what
    // comes after that opening stretch; counting it would blur the very difference being looked for.
    public const int LoadSeconds = 20;
    private const int LoadSkipSeconds = 5;     // opening stretch, excluded from the work figure
    public const int CurveBaseSeconds = 12;
    public const int CurvePhaseSeconds = 40;
    // Super Battery must deliver at most this share of Extreme's work. Two runs of the SAME mode
    // differ by a few percent (up to 5 % seen when replaying a dump on a busy machine, the same
    // bound the power test works with), while a power-saving shift mode is expected to cost tens of percent - the
    // line sits in the gap between the two. The report prints the raw shares, so a verdict
    // near the line can be judged by eye.
    private const double EffectRatio = 0.90;
    private const int CurveRiseRpm = 500;
    private const byte FanAuto = 0x0D, FanSilent = 0x1D, FanAdvanced = 0x8D;

    public enum Verdict { Passed, NoEffect, WriteNotKept, RestoreUnconfirmed, Cancelled, Refused, Failed }
    public enum CurveVerdict { NotRun, Skipped, EveryProfile, ExtremeOnly, NoReaction }

    /// <summary>Steps of the card's checklist, in order. The curve step is shown only when chosen.</summary>
    public enum Step { Read, OneByte, Profiles, Curve, Restore }

    public readonly record struct Progress(Step Step, double Fraction, string Live);

    public sealed record Sample(double Sec, int CpuTemp, int CpuDuty, int CpuRpm, int GpuRpm, int Mhz);

    public sealed record ProfileCheck(
        string Name, byte ShiftWant, byte FanWant, int ShiftGot, int FanGot, int ShiftAfter, int FanAfter)
    {
        public bool Ok => ShiftGot == ShiftWant && FanGot == FanWant && ShiftAfter == ShiftWant && FanAfter == FanWant;
    }

    public sealed record LoadRun(string Name, double WorkPerSec, int AvgMhz, int EndTemp, int EndRpm);

    public sealed record CurvePhase(string Name, Sample[] Samples)
    {
        /// <summary>Mean of the last three tachometer samples - where the fan had got to.</summary>
        public int EndRpm => Samples.Length == 0 ? 0 : (int)Samples.TakeLast(3).Average(s => s.CpuRpm);
        public int TempChange => Samples.Length == 0 ? 0 : Samples[^1].CpuTemp - Samples[0].CpuTemp;
    }

    public sealed record CurveResult(
        CurveVerdict Verdict, string Note, int[] Temps, int[] Speeds, byte Target, CurvePhase[] Phases);

    public sealed record Result(
        DateTime Started, string AppVersion, string Firmware, string Model,
        Verdict Verdict, string Detail,
        string Survey, int ShiftOrig, int FanOrig,
        ProfileCheck[] Profiles, LoadRun[] Loads, CurveResult Curve,
        bool Wrote, bool Restored, string[] VendorProcesses, string[] Log)
    {
        public bool Passed => Verdict == Verdict.Passed;
    }

    public static Task<Result> RunAsync(DeviceProfile dev, string appVersion, string firmware, bool withCurve,
                                        IProgress<Progress> progress, CancellationToken ct) =>
        Task.Run(() => Run(dev, appVersion, firmware, withCurve, progress, ct));

    private static Result Run(DeviceProfile dev, string appVersion, string firmware, bool withCurve,
                              IProgress<Progress> pr, CancellationToken ct)
    {
        var started = DateTime.Now;
        var clock = Stopwatch.StartNew();
        var log = new List<string>();
        void Log(string s) => log.Add($"[{clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),6} s] {s}");
        var noCurve = new CurveResult(CurveVerdict.NotRun, "", Array.Empty<int>(), Array.Empty<int>(), 0, Array.Empty<CurvePhase>());
        var profiles = new List<ProfileCheck>();
        var loads = new List<LoadRun>();
        var curve = noCurve;
        string survey = "";
        int shift0 = -1, fan0 = -1;
        string[] vendor = VendorProcesses();

        Result End(Verdict v, string detail, bool wrote, bool restored) => new(
            started, appVersion, firmware, dev.Name, v, detail, survey, shift0, fan0,
            profiles.ToArray(), loads.ToArray(), curve, wrote, restored, vendor, log.ToArray());

        if (Ec.BackupSpec is not { } spec) return End(Verdict.Refused, "the backup path is not active", false, true);
        if (SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online)
            return End(Verdict.Refused, "the laptop runs on battery", false, true);

        // ---------------- 1. read ----------------
        int[] curveTemps = Array.Empty<int>(), curveSpeeds = Array.Empty<int>();
        string curveSkip = "";
        try
        {
            pr.Report(new Progress(Step.Read, 0, ""));
            survey = EcBlocks.Dump();
            shift0 = EcBlocks.ReadFresh(spec.ShiftMode);
            fan0 = EcBlocks.ReadFresh(spec.FanMode);
            Log($"read {spec.ShiftMode} = 0x{shift0:X2}, {spec.FanMode} = 0x{fan0:X2}");
            if ((shift0 & 0xF0) != 0xC0 || (fan0 & 0x0F) != 0x0D)
                return End(Verdict.Refused, $"the values do not match the layout on record ({spec.ShiftMode} = 0x{shift0:X2}, {spec.FanMode} = 0x{fan0:X2})", false, true);

            if (withCurve)
            {
                if (spec.CpuCurve is not { } cc) curveSkip = "no curve table on record for this model";
                else if (spec.CpuRpm == null) curveSkip = "no tachometer slot on record for this model";
                else
                {
                    curveTemps = Enumerable.Range(0, cc.Count).Select(i => EcBlocks.Read(cc.Temp(i))).ToArray();
                    curveSpeeds = Enumerable.Range(0, cc.Count).Select(i => EcBlocks.Read(cc.Speed(i))).ToArray();
                    Log($"curve table {cc.Class}: temps {string.Join(' ', curveTemps)} | speeds {string.Join(' ', curveSpeeds)}");
                    // The thresholds after the anchor rise steadily inside a sane band and the speeds
                    // stay within the byte range a fan table uses; anything else is another layout.
                    var th = curveTemps.Skip(1).ToArray();
                    bool rising = th.Zip(th.Skip(1), (a, b) => b > a).All(x => x);
                    if (!rising || th.Any(t => t is < 30 or > 110) || curveSpeeds.Any(s => s is < 0 or > 150) || curveSpeeds.Max() < 30)
                        curveSkip = "the table does not look like a fan curve";
                }
                if (curveSkip.Length > 0) { curve = noCurve with { Verdict = CurveVerdict.Skipped, Note = curveSkip }; Log("curve step skipped: " + curveSkip); }
            }
        }
        catch (Exception ex)
        {
            return End(Verdict.Refused, "the data blocks could not be read: " + ex.Message, false, true);
        }

        // Originals on disk before the first write, so a run that dies is undone at the next start.
        var originals = new List<SavedSlot>
        {
            new(spec.ShiftMode.Class, spec.ShiftMode.Index, shift0),
            new(spec.FanMode.Class, spec.FanMode.Index, fan0),
        };
        if (withCurve && curveSkip.Length == 0 && spec.CpuCurve is { } c0)
            for (int i = 0; i < c0.Count; i++) originals.Add(new(c0.Class, c0.SpeedFirst + i, curveSpeeds[i]));
        try { File.WriteAllText(RestorePath, JsonSerializer.Serialize(new Saved(firmware, originals))); }
        catch (Exception ex) { return End(Verdict.Refused, "the restore file could not be written: " + ex.Message, false, true); }

        bool gateWas = Ec.BackupWritesOpen;
        Ec.OpenBackupWrites(true);
        Ec.BackupAllow(spec.ShiftMode, (byte)shift0);
        Ec.BackupAllow(spec.FanMode, (byte)fan0);
        bool wrote = false, restored = false, curveWrote = false;
        var verdict = Verdict.Failed;
        string detail = "";

        int Put(BlockRef slot, byte value)
        {
            wrote = true;
            Ec.BackupWrite(slot, value);
            Wait(300, CancellationToken.None);   // let the controller take the value before the read-back
            int got = EcBlocks.ReadFresh(slot);
            Log($"write {slot} = 0x{value:X2} -> read 0x{got:X2}{(got == value ? "" : "   NOT KEPT")}");
            return got;
        }

        try
        {
            // ---------------- 2. one byte ----------------
            pr.Report(new Progress(Step.OneByte, 0, ""));
            byte alt = fan0 == FanAuto ? FanSilent : FanAuto;
            int g1 = Put(spec.FanMode, alt);
            int g2 = Put(spec.FanMode, (byte)fan0);
            if (g1 != alt || g2 != fan0)
            {
                verdict = Verdict.WriteNotKept;
                detail = $"{spec.FanMode}: wrote 0x{alt:X2}, read 0x{g1:X2}; wrote 0x{fan0:X2} back, read 0x{g2:X2}";
            }
            else
            {
                // ---------------- 3. profiles ----------------
                (ProfileId Id, string Name, bool Load)[] plan =
                {
                    (ProfileId.Silent, "Silent", false), (ProfileId.Balanced, "Balanced", false),
                    (ProfileId.Extreme, "Extreme", true), (ProfileId.SuperBattery, "Super Battery", true),
                    (ProfileId.Extreme, "Extreme (repeat)", true),
                };
                for (int i = 0; i < plan.Length; i++)
                {
                    var (id, name, load) = plan[i];
                    var recipe = dev.Recipes[id];
                    byte sWant = recipe.First(p => p.addr == dev.ShiftMode).val, fWant = recipe.First(p => p.addr == dev.FanMode).val;
                    wrote = true;
                    Ec.Apply(recipe);
                    Wait(300, ct);
                    int sGot = EcBlocks.ReadFresh(spec.ShiftMode), fGot = EcBlocks.ReadFresh(spec.FanMode);
                    Log($"{name}: wrote shift 0x{sWant:X2} / fan 0x{fWant:X2} -> read 0x{sGot:X2} / 0x{fGot:X2}");
                    double f0 = i / (double)plan.Length, f1 = (i + 1) / (double)plan.Length;
                    if (load) loads.Add(Loaded(dev, name, pr, f0, f1, ct));
                    else Hold(dev, name, HoldSeconds, pr, Step.Profiles, f0, f1, ct);
                    int sAfter = EcBlocks.ReadFresh(spec.ShiftMode), fAfter = EcBlocks.ReadFresh(spec.FanMode);
                    if (sAfter != sWant || fAfter != fWant) Log($"{name}: after the hold the slots read 0x{sAfter:X2} / 0x{fAfter:X2}   CHANGED");
                    profiles.Add(new ProfileCheck(name, sWant, fWant, sGot, fGot, sAfter, fAfter));
                }

                var bad = profiles.FirstOrDefault(p => !p.Ok);
                var sb = loads.First(l => l.Name == "Super Battery");
                double extreme = loads.Where(l => l.Name.StartsWith("Extreme")).Min(l => l.WorkPerSec);
                if (bad != null)
                {
                    verdict = Verdict.WriteNotKept;
                    detail = $"{bad.Name}: wanted 0x{bad.ShiftWant:X2} / 0x{bad.FanWant:X2}, read 0x{bad.ShiftGot:X2} / 0x{bad.FanGot:X2}, later 0x{bad.ShiftAfter:X2} / 0x{bad.FanAfter:X2}";
                }
                else if (extreme <= 0 || sb.WorkPerSec > EffectRatio * extreme)
                {
                    verdict = Verdict.NoEffect;
                    detail = $"Super Battery delivered {Pct(sb.WorkPerSec, extreme)} % of the work of the slower Extreme run (a pass needs {EffectRatio * 100:0} % or less)";
                }
                else
                {
                    verdict = Verdict.Passed;
                    detail = $"Super Battery delivered {Pct(sb.WorkPerSec, extreme)} % of the work of the slower Extreme run";
                }

                // ---------------- 4. curve (optional) ----------------
                // The verdict above stands whatever happens here: this step is an extra measurement.
                if (withCurve && curveSkip.Length == 0 && bad == null && spec.CpuCurve is { } cc)
                {
                    curveWrote = true;
                    try { curve = RunCurve(dev, spec, cc, curveTemps, curveSpeeds, pr, Put, Log, ct); }
                    catch (OperationCanceledException)
                    {
                        curve = noCurve with { Verdict = CurveVerdict.Skipped, Note = "cancelled by the user" };
                        Log("curve step cancelled");
                    }
                    catch (Exception ex)
                    {
                        curve = noCurve with { Verdict = CurveVerdict.Skipped, Note = "stopped on an error: " + ex.Message };
                        Log("curve step stopped: " + ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            verdict = Verdict.Cancelled;
            detail = "cancelled by the user";
            Log("cancelled");
        }
        catch (Exception ex)
        {
            verdict = Verdict.Failed;
            detail = ex.GetType().Name + ": " + ex.Message;
            Log("FAILED: " + detail);
        }
        finally
        {
            // ---------------- 5. restore ----------------
            pr.Report(new Progress(Step.Restore, 0, ""));
            restored = !wrote || Restore(spec, originals, curveWrote, Log);
            if (restored) { try { File.Delete(RestorePath); } catch { } }
            Ec.OpenBackupWrites(gateWas);
            pr.Report(new Progress(Step.Restore, 1, ""));
        }

        if (wrote && !restored)
        {
            detail = (detail.Length > 0 ? detail + "; " : "") + "the original values could not be confirmed after the run";
            verdict = Verdict.RestoreUnconfirmed;
        }
        return End(verdict, detail, wrote, restored);
    }

    private static int Pct(double part, double whole) => whole <= 0 ? 0 : (int)Math.Round(part / whole * 100);

    // ---------------- measurement helpers ----------------

    private static Sample Take(DeviceProfile dev, double sec)
    {
        Ec.TryReadHw(dev, out var hw);
        return new Sample(sec, hw.CpuTemp, hw.CpuFan, hw.CpuRpm, hw.GpuRpm, Perf.CpuClockMhz());
    }

    private static string Live(string name, Sample s) =>
        $"{name}  ·  {s.CpuTemp} °C  ·  {(s.CpuRpm > 0 ? s.CpuRpm + " RPM" : "— RPM")}  ·  {s.Mhz} MHz";

    private static void Hold(DeviceProfile dev, string name, int seconds, IProgress<Progress> pr, Step step,
                             double f0, double f1, CancellationToken ct)
    {
        for (int s = 1; s <= seconds; s++)
        {
            Wait(1000, ct);
            pr.Report(new Progress(step, f0 + (f1 - f0) * s / seconds, Live(name, Take(dev, s))));
        }
    }

    /// <summary>
    /// A short all-core load in the profile just applied. The work figure is completed
    /// iterations per second over the window after the spin-up - the same measure the power
    /// test uses, and the only one here that reflects delivered performance, not a sensor.
    /// </summary>
    private static LoadRun Loaded(DeviceProfile dev, string name, IProgress<Progress> pr, double f0, double f1, CancellationToken ct)
    {
        int total = SettleSeconds + LoadSeconds;
        Hold(dev, name, SettleSeconds, pr, Step.Profiles, f0, f0 + (f1 - f0) * SettleSeconds / total, ct);
        var mhz = new List<int>();
        Sample last = default!;
        long i0 = 0;
        var win = new Stopwatch();
        long done;
        using (var load = new PowerTest.CpuLoad(Environment.ProcessorCount))
        {
            for (int s = 1; s <= LoadSeconds; s++)
            {
                Wait(1000, ct);
                if (s == LoadSkipSeconds) { i0 = load.Iterations; win.Restart(); }
                last = Take(dev, s);
                if (s > LoadSkipSeconds) mhz.Add(last.Mhz);
                pr.Report(new Progress(Step.Profiles, f0 + (f1 - f0) * (SettleSeconds + s) / total, Live(name, last)));
            }
            win.Stop();
            done = load.Iterations - i0;
        }
        double work = win.Elapsed.TotalSeconds > 0 ? done / win.Elapsed.TotalSeconds : 0;
        return new LoadRun(name, work, mhz.Count > 0 ? (int)mhz.Average() : 0, last.CpuTemp, last.CpuRpm);
    }

    private static CurveResult RunCurve(DeviceProfile dev, BlockPathSpec spec, BlockCurveSpec cc, int[] temps, int[] speeds,
                                        IProgress<Progress> pr, Func<BlockRef, byte, int> put, Action<string> log, CancellationToken ct)
    {
        byte target = (byte)speeds.Max();
        var phases = new List<CurvePhase>();
        int total = CurveBaseSeconds + CurvePhaseSeconds * 2, at = 0;

        CurvePhase Watch(string name, int seconds)
        {
            var samples = new List<Sample>();
            for (int s = 2; s <= seconds; s += 2)
            {
                Wait(2000, ct);
                var smp = Take(dev, s);
                samples.Add(smp);
                pr.Report(new Progress(Step.Curve, (at + s) / (double)total, Live(name, smp)));
            }
            at += seconds;
            var phase = new CurvePhase(name, samples.ToArray());
            log($"{name}: fan {phase.EndRpm} RPM at the end, temperature change {phase.TempChange:+0;-0;0} °C");
            return phase;
        }

        // Baseline: Balanced with the firmware's own fan control.
        Ec.Apply(dev.Recipes[ProfileId.Balanced]);
        phases.Add(Watch("baseline (Balanced, automatic fan)", CurveBaseSeconds));

        // The table's own top value in every speed slot, then the fan mode that reads the table.
        for (int i = 0; i < cc.Count; i++)
        {
            Ec.BackupAllow(cc.Speed(i), (byte)speeds[i], target);
            if (put(cc.Speed(i), target) != target) throw new EcPathException($"{cc.Speed(i)} did not keep the written value");
        }
        Ec.BackupAllow(spec.FanMode, FanAdvanced);
        if (put(spec.FanMode, FanAdvanced) != FanAdvanced) throw new EcPathException($"{spec.FanMode} did not keep the Advanced value");
        phases.Add(Watch("table raised, Advanced fan, comfort shift mode", CurvePhaseSeconds));

        if (put(spec.ShiftMode, dev.ShiftTurboValue) != dev.ShiftTurboValue) throw new EcPathException($"{spec.ShiftMode} did not keep the turbo value");
        phases.Add(Watch("table raised, Advanced fan, turbo shift mode", CurvePhaseSeconds));

        int baseRpm = phases[0].EndRpm, comfort = phases[1].EndRpm, turbo = phases[2].EndRpm;
        var verdict = comfort - baseRpm >= CurveRiseRpm ? CurveVerdict.EveryProfile
                    : turbo - comfort >= CurveRiseRpm ? CurveVerdict.ExtremeOnly
                    : CurveVerdict.NoReaction;
        string note = $"fan {baseRpm} -> {comfort} -> {turbo} RPM (baseline -> comfort shift -> turbo shift)";
        return new CurveResult(verdict, note, temps, speeds, target, phases.ToArray());
    }

    /// <summary>Sleep that a cancel interrupts.</summary>
    private static void Wait(int ms, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
    }

    // ---------------- restore ----------------

    private sealed record SavedSlot(string Class, int Index, int Value);
    private sealed record Saved(string Firmware, List<SavedSlot> Slots);

    private static string RestorePath => Path.Combine(AppSettings.Dir, "backup-path-restore.json");

    /// <summary>
    /// Put the saved values back, table first, then fan mode, then shift mode, each verified by
    /// read-back and retried twice. True only when every slot reads its original value.
    /// </summary>
    private static bool Restore(BlockPathSpec spec, List<SavedSlot> originals, bool withTable, Action<string> log)
    {
        bool all = true;
        var order = originals.Where(o => !IsMode(spec, o)).Concat(originals.Where(o => IsMode(spec, o)).Reverse()).ToList();
        foreach (var o in order)
        {
            var slot = new BlockRef(o.Class, o.Index);
            if (!withTable && !IsMode(spec, o)) continue;   // the table was never touched
            bool ok = false;
            for (int attempt = 0; attempt < 3 && !ok; attempt++)
            {
                try
                {
                    Ec.BackupAllow(slot, (byte)o.Value);
                    if (EcBlocks.ReadFresh(slot) != o.Value) Ec.BackupWrite(slot, (byte)o.Value);
                    Thread.Sleep(300);
                    ok = EcBlocks.ReadFresh(slot) == o.Value;
                }
                catch (Exception ex) { log($"restore {slot}: {ex.Message}"); }
            }
            log($"restore {slot} = 0x{o.Value:X2}: {(ok ? "confirmed" : "NOT CONFIRMED")}");
            all &= ok;
        }
        return all;
    }

    private static bool IsMode(BlockPathSpec spec, SavedSlot o) =>
        (o.Class == spec.ShiftMode.Class && o.Index == spec.ShiftMode.Index) ||
        (o.Class == spec.FanMode.Class && o.Index == spec.FanMode.Index);

    /// <summary>
    /// Undo a run that never reached its own restore (the app was closed or crashed during
    /// the test). Called at startup once the backup path is active. Only slots that belong to
    /// this model's layout are taken from the file. Returns a line for the change log, or null
    /// when there was nothing to do.
    /// </summary>
    public static string? RecoverPending(string firmware)
    {
        try
        {
            if (!File.Exists(RestorePath) || Ec.BackupSpec is not { } spec) return null;
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(RestorePath));
            if (saved == null || !string.Equals(saved.Firmware, firmware, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(RestorePath);
                return null;
            }
            var mine = saved.Slots.Where(o => IsMode(spec, o) ||
                (spec.CpuCurve is { } c && o.Class == c.Class && o.Index >= c.SpeedFirst && o.Index < c.SpeedFirst + c.Count)).ToList();
            bool gateWas = Ec.BackupWritesOpen;
            Ec.OpenBackupWrites(true);
            bool ok;
            try { ok = Restore(spec, mine, withTable: true, _ => { }); }
            finally { Ec.OpenBackupWrites(gateWas); }
            if (ok) File.Delete(RestorePath);
            return ok ? "Backup path test: the state from before an interrupted run was put back"
                      : "Backup path test: the state from before an interrupted run could NOT be confirmed";
        }
        catch (Exception ex)
        {
            AppLifecycle.Report(ex, "backup-path-recover");
            return null;
        }
    }

    // ---------------- report ----------------

    /// <summary>MSI's own software that is running, by process name - it may drive the same registers.</summary>
    private static string[] VendorProcesses()
    {
        try
        {
            return Process.GetProcesses()
                .Select(p => { try { return p.ProcessName; } catch { return ""; } finally { p.Dispose(); } })
                .Where(n => n.Contains("MSI", StringComparison.OrdinalIgnoreCase) || n.Contains("Dragon", StringComparison.OrdinalIgnoreCase))
                .Where(n => !n.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Take(20).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>The whole run as text, in English: it is read on GitHub, not in the owner's language.</summary>
    public static string BuildReport(Result r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== GhostDeck - backup path test ===");
        sb.AppendLine($"Started: {r.Started:yyyy-MM-dd HH:mm}   App version: {r.AppVersion}");
        sb.AppendLine($"Model: {r.Model}   Firmware: {r.Firmware}");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString}");
        sb.AppendLine($"MSI software running: {(r.VendorProcesses.Length > 0 ? string.Join(", ", r.VendorProcesses) : "none seen")}");
        sb.AppendLine();
        sb.AppendLine($"VERDICT: {r.Verdict}" + (r.Detail.Length > 0 ? $" - {r.Detail}" : ""));
        sb.AppendLine(r.Verdict switch
        {
            Verdict.Passed => "Profile switching works through the backup path on this machine and is now unlocked in the app.",
            Verdict.NoEffect => "The writes were stored and read back, but the delivered performance did not differ between Super Battery and Extreme. Profile switching stays locked.",
            Verdict.WriteNotKept => "A written value did not read back as written. Profile switching stays locked.",
            Verdict.RestoreUnconfirmed => "The values from before the run could not be confirmed. A restart of the laptop resets the controller.",
            Verdict.Cancelled => "The run was cancelled; see the restore lines in the log below.",
            Verdict.Refused => "Nothing was written.",
            _ => "The run ended on an error; see the log below.",
        });
        sb.AppendLine($"Written anything: {(r.Wrote ? "yes" : "no")}   Original state confirmed afterwards: {(r.Restored ? "yes" : "NO")}");
        if (r.ShiftOrig >= 0) sb.AppendLine($"State found: shift mode 0x{r.ShiftOrig:X2}, fan mode 0x{r.FanOrig:X2}");
        sb.AppendLine();

        if (r.Profiles.Length > 0)
        {
            sb.AppendLine("--- Profiles: value wanted / read right after the write / read after the hold ---");
            foreach (var p in r.Profiles)
                sb.AppendLine($"{p.Name,-18} shift 0x{p.ShiftWant:X2} / 0x{p.ShiftGot:X2} / 0x{p.ShiftAfter:X2}    fan 0x{p.FanWant:X2} / 0x{p.FanGot:X2} / 0x{p.FanAfter:X2}    {(p.Ok ? "ok" : "MISMATCH")}");
            sb.AppendLine();
        }
        if (r.Loads.Length > 0)
        {
            double top = r.Loads.Max(l => l.WorkPerSec);
            sb.AppendLine($"--- Short all-core load ({LoadSeconds} s each, first {LoadSkipSeconds} s not counted) ---");
            foreach (var l in r.Loads)
                sb.AppendLine($"{l.Name,-18} work {Pct(l.WorkPerSec, top),3} %   {l.AvgMhz} MHz   {l.EndTemp} °C at the end   fan {l.EndRpm} RPM");
            sb.AppendLine("Work is completed iterations per second, as a share of the fastest run.");
            sb.AppendLine();
        }
        sb.AppendLine("--- Fan curve check ---");
        sb.AppendLine(r.Curve.Verdict switch
        {
            CurveVerdict.NotRun => "not run",
            CurveVerdict.Skipped => "skipped: " + r.Curve.Note,
            CurveVerdict.EveryProfile => "The fan followed the raised table already in the comfort shift mode. " + r.Curve.Note,
            CurveVerdict.ExtremeOnly => "The fan followed the raised table only in the turbo shift mode. " + r.Curve.Note,
            _ => "No fan reaction above " + CurveRiseRpm + " RPM was seen. " + r.Curve.Note,
        });
        if (r.Curve.Phases.Length > 0)
        {
            sb.AppendLine($"Table found: temps {string.Join(' ', r.Curve.Temps)} | speeds {string.Join(' ', r.Curve.Speeds)}; every speed slot was set to {r.Curve.Target} for the check.");
            foreach (var ph in r.Curve.Phases)
            {
                sb.AppendLine($"[{ph.Name}]  end {ph.EndRpm} RPM, temperature change {ph.TempChange:+0;-0;0} °C");
                sb.AppendLine("  s:    " + string.Join(' ', ph.Samples.Select(s => $"{s.Sec,5:0}")));
                sb.AppendLine("  rpm:  " + string.Join(' ', ph.Samples.Select(s => $"{s.CpuRpm,5}")));
                sb.AppendLine("  duty: " + string.Join(' ', ph.Samples.Select(s => $"{s.CpuDuty,5}")));
                sb.AppendLine("  °C:   " + string.Join(' ', ph.Samples.Select(s => $"{s.CpuTemp,5}")));
            }
            sb.AppendLine("A fan needs a minute or more to reach a new speed; the figures above are where it had got to. A temperature that rose during a phase also raises the fan on its own.");
        }
        sb.AppendLine();
        sb.AppendLine("--- Log ---");
        foreach (var l in r.Log) sb.AppendLine(l);
        sb.AppendLine();
        sb.AppendLine("--- Block survey taken before the first write ---");
        sb.AppendLine(r.Survey);
        return sb.ToString();
    }

    /// <summary>
    /// Write the report to the desktop (documents when there is none) and keep the latest copy
    /// in the app folder, where the diagnostic package picks it up. Returns the desktop path.
    /// </summary>
    public static string? SaveReport(Result r)
    {
        string text = BuildReport(r);
        try { File.WriteAllText(Path.Combine(AppSettings.Dir, "backup-path-test.txt"), text); } catch { }
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string tag = new string(r.Firmware.TakeWhile(char.IsLetterOrDigit).ToArray());
            string path = Path.Combine(dir, $"ghostdeck-path-test-{tag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text);
            return path;
        }
        catch { return null; }
    }
}
